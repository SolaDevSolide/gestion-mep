using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionMep.Tests;

/// <summary>Réglages d'exploitation modifiables depuis la console (et la ligne de commande), appliqués sans redémarrage.</summary>
[Collection("Api")]
public class ReglagesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static JsonElement Reglage(JsonElement liste, string nom) =>
        liste.EnumerateArray().Single(r => r.GetProperty("nom").GetString() == nom);

    private IConfiguration Config => _api.Services.GetRequiredService<IConfiguration>();

    [Fact]
    public async Task Seul_un_administrateur_voit_et_modifie_les_reglages()
    {
        var bob = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/reglages")).StatusCode);
        var refus = await bob.PutAsJsonAsync("/api/admin/reglages/url-publique", new { valeur = "https://mep.exemple.fr" });
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
    }

    [Fact]
    public async Task L_adresse_publique_se_regle_puis_se_retablit_et_tout_est_journalise()
    {
        var alice = await _api.ClientConnecte("alice");
        var liste = await Ok(await alice.GetAsync("/api/admin/reglages"));
        Assert.Equal(Reglages.Catalogue.Count, liste.GetArrayLength());
        Assert.Equal("defaut", Reglage(liste, "url-publique").GetProperty("origine").GetString());
        // Valeur du fichier .env (variable d'environnement des tests).
        var taille = Reglage(liste, "pieces-taille-max-mo");
        Assert.Equal("fichier", taille.GetProperty("origine").GetString());
        Assert.Equal("PIECES_TAILLE_MAX_MO", taille.GetProperty("variable").GetString());

        var invalide = await alice.PutAsJsonAsync("/api/admin/reglages/url-publique", new { valeur = "mep.exemple.fr?x=1" });
        Assert.Equal(HttpStatusCode.BadRequest, invalide.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsJsonAsync("/api/admin/reglages/inconnu", new { valeur = "1" })).StatusCode);

        var r = await Ok(await alice.PutAsJsonAsync("/api/admin/reglages/url-publique", new { valeur = " https://mep.exemple.fr/ " }));
        Assert.Equal("https://mep.exemple.fr", r.GetProperty("valeur").GetString());
        Assert.Equal("console", r.GetProperty("origine").GetString());
        Assert.Equal("Alice", r.GetProperty("modifiePar").GetString());
        // Appliqué tout de suite, sans redémarrage : l'adresse de retour de la connexion Microsoft en dépend.
        Assert.Equal("https://mep.exemple.fr", Config["Application:UrlPublique"]);

        r = await Ok(await alice.DeleteAsync("/api/admin/reglages/url-publique"));
        Assert.Equal("defaut", r.GetProperty("origine").GetString());
        Assert.Null(Config["Application:UrlPublique"]);

        var journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "reglage.modifie"
            && e.GetProperty("detail").GetString() == "Adresse publique (url-publique) : (vide) → « https://mep.exemple.fr »");
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("type").GetString() == "reglage.retabli");
    }

    [Fact]
    public async Task La_taille_maximale_des_pieces_jointes_change_sans_redemarrage()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        async Task<HttpResponseMessage> Deposer()
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[1536 * 1024]), "fichier", "gros.bin" } };
            return await alice.PostAsync($"/api/meps/{id}/pieces-jointes", form);
        }

        // 1 Mo dans le fichier .env des tests : 1,5 Mo est refusé.
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Deposer()).StatusCode);
        await Ok(await alice.PutAsJsonAsync("/api/admin/reglages/pieces-taille-max-mo", new { valeur = "2" }));
        await Ok(await Deposer());
        await Ok(await alice.DeleteAsync("/api/admin/reglages/pieces-taille-max-mo"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Deposer()).StatusCode);

        var horsBornes = await alice.PutAsJsonAsync("/api/admin/reglages/pieces-taille-max-mo", new { valeur = "5000" });
        Assert.Equal(HttpStatusCode.BadRequest, horsBornes.StatusCode);
    }

    [Fact]
    public async Task Un_lien_de_signalement_vide_masque_le_lien()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PutAsJsonAsync("/api/admin/reglages/lien-signalement", new { valeur = "" }));
        var application = await Ok(await alice.GetAsync("/api/application"));
        Assert.Equal(JsonValueKind.Null, application.GetProperty("lienSignalement").ValueKind);
    }

    [Fact]
    public async Task Un_changement_fait_par_la_ligne_de_commande_est_relu_par_l_application()
    {
        // La ligne de commande écrit en base depuis un autre processus ; l'application relit la table toutes les 10 secondes.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            await Reglages.Definir(db, config, Reglages.Trouver("connexions-par-minute"), "7", null, DateTimeOffset.UtcNow);
            var evenement = await db.JournalAdmin.OrderByDescending(e => e.Id).FirstAsync(e => e.Type == "reglage.modifie");
            Assert.Equal("Ligne de commande", evenement.Auteur);
        }
        Assert.Equal("1000", Config["Connexion:TentativesParMinute"]);
        _api.Services.GetRequiredService<FournisseurReglagesBase>().Recharger();
        Assert.Equal("7", Config["Connexion:TentativesParMinute"]);
    }

    [Fact]
    public async Task Les_reseaux_du_proxy_changent_sans_redemarrage()
    {
        var alice = await _api.ClientConnecte("alice");
        var options = _api.Services.GetRequiredService<IOptionsMonitor<ForwardedHeadersOptions>>();
        Assert.DoesNotContain(options.CurrentValue.KnownIPNetworks, r => r.ToString() == "10.0.5.12/32");
        await Ok(await alice.PutAsJsonAsync("/api/admin/reglages/proxy-reseaux", new { valeur = "10.0.5.12" }));
        Assert.Contains(options.CurrentValue.KnownIPNetworks, r => r.ToString() == "10.0.5.12/32");
    }

    [Fact]
    public async Task La_base_ne_peut_pas_remplacer_un_reglage_hors_catalogue()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            db.Reglages.Add(new Reglage { Cle = "ConnectionStrings:Mep", Valeur = "Host=ailleurs", ModifiePar = "test" });
            await db.SaveChangesAsync();
        }
        _api.Services.GetRequiredService<FournisseurReglagesBase>().Recharger();
        Assert.DoesNotContain("ailleurs", Config.GetConnectionString("Mep"));
    }

    [Theory]
    [InlineData("proxy-reseaux", "10.0.5.12, 192.168.0.0/16", "10.0.5.12/32, 192.168.0.0/16")]
    [InlineData("proxy-reseaux", "", "")]
    [InlineData("https-obligatoire", "non", "false")]
    [InlineData("verifier-mises-a-jour", "OUI", "true")]
    [InlineData("flux-max", " 50 ", "50")]
    public void Les_saisies_sont_mises_en_forme(string nom, string saisie, string attendu) =>
        Assert.Equal(attendu, Reglages.Normaliser(Reglages.Trouver(nom), saisie));

    [Theory]
    [InlineData("proxy-reseaux", "pas-un-reseau")]
    [InlineData("https-obligatoire", "peut-etre")]
    [InlineData("connexions-par-minute", "0")]
    [InlineData("lien-signalement", "ftp://exemple.fr")]
    [InlineData("url-publique", "https://user:mdp@exemple.fr")]
    public void Les_saisies_invalides_sont_refusees(string nom, string saisie) =>
        Assert.Equal(400, Assert.Throws<ErreurHttp>(() => Reglages.Normaliser(Reglages.Trouver(nom), saisie)).Statut);
}
