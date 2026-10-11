using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Durcissements : rôles protégés dans une MEP, preuves de sauvegarde, flux temps réel et stockage bornés, noms affichés,
/// mot de passe initial, archives de sauvegarde complètes.
/// </summary>
[Collection("Api")]
public class DurcissementTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static JsonElement Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom);

    [Fact]
    public async Task Un_responsable_technique_ne_relie_pas_le_pilote_a_son_propre_compte()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var pilote = Participant(mep, "Alice");
        var compteClaire = Participant(mep, "Claire").GetProperty("utilisateurId").GetGuid();
        var saisie = new { nom = "Alice", role = pilote.GetProperty("role").GetString(), contact = "", utilisateurId = compteClaire };

        // Claire (responsable technique) ne peut pas reprendre le siège du pilote…
        var refus = await claire.PutAsJsonAsync($"/api/meps/{id}/participants/{pilote.GetProperty("id").GetGuid()}", saisie);
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        // … mais elle modifie toujours le nom ou le contact du pilote, sans toucher au compte relié.
        var memeCompte = new { nom = "Alice", role = "Pilote", contact = "poste 42", utilisateurId = pilote.GetProperty("utilisateurId").GetGuid() };
        await Ok(await claire.PutAsJsonAsync($"/api/meps/{id}/participants/{pilote.GetProperty("id").GetGuid()}", memeCompte));
    }

    [Fact]
    public async Task Apres_la_preparation_seuls_le_pilote_ou_un_admin_modifient_les_preuves_de_sauvegarde()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        JsonNode Saisie(JsonElement m, string preuve)
        {
            var corps = JsonNode.Parse(m.GetProperty("sauvegarde").GetRawText())!;
            corps["version"] = m.GetProperty("version").GetInt32();
            corps["preuveTestRestauration"] = preuve;
            corps["restaurationTestee"] = true;
            return corps;
        }

        // En préparation, Claire (responsable technique) renseigne la sauvegarde.
        mep = await Ok(await claire.PutAsJsonAsync($"/api/meps/{id}/sauvegarde", Saisie(mep, "Restauration testée le 1er")));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var detail = await db.Journal.Where(e => e.MepId == id && e.Type == "sauvegarde.modifiee").OrderBy(e => e.Id).Select(e => e.Detail).LastAsync();
            Assert.Contains("preuve du test : vide → « Restauration testée le 1er »", detail);
            var m = await db.Meps.SingleAsync(x => x.Id == id);
            m.Phase = Domain.Phase.Execution;
            await db.SaveChangesAsync();
        }

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var refus = await claire.PutAsJsonAsync($"/api/meps/{id}/sauvegarde", Saisie(mep, "Preuve réécrite"));
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/sauvegarde", Saisie(mep, "Complétée par le pilote")));
        Assert.Equal("Complétée par le pilote", mep.GetProperty("sauvegarde").GetProperty("preuveTestRestauration").GetString());
    }

    [Fact]
    public void Les_flux_temps_reel_sont_limites_par_compte_et_liberes_a_la_fermeture()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Flux:ConnexionsMax"] = "25" }).Build();
        var diffuseur = new DiffuseurMep(config);
        Guid mep = Guid.NewGuid(), bob = Guid.NewGuid(), claire = Guid.NewGuid();
        var ouverts = Enumerable.Range(0, DiffuseurMep.MaxParCompte).Select(_ => diffuseur.Abonner(mep, bob)).ToList();
        Assert.All(ouverts, Assert.NotNull);
        Assert.Null(diffuseur.Abonner(mep, bob));
        // Plafond du serveur : 25 flux en tout.
        var autres = Enumerable.Range(0, 5).Select(_ => diffuseur.Abonner(mep, claire)).ToList();
        Assert.All(autres, Assert.NotNull);
        Assert.Null(diffuseur.Abonner(mep, claire));

        ouverts[0]!.Dispose();
        ouverts[0]!.Dispose(); // sans effet la seconde fois
        Assert.NotNull(diffuseur.Abonner(mep, bob));
        Assert.Null(diffuseur.Abonner(mep, claire));
    }

    [Fact]
    public async Task Les_pieces_jointes_respectent_les_plafonds_de_stockage()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var aliceId = (await Ok(await alice.GetAsync("/api/auth/moi"))).GetProperty("id").GetGuid();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var stockage = scope.ServiceProvider.GetRequiredService<StockagePiecesJointes>();
        QuotasPiecesJointes Quotas(string cle, string valeur) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stockage:QuotaMepMo"] = "0", ["Stockage:QuotaCompteJourMo"] = "0", ["Stockage:EspaceLibreMinMo"] = "0", [cle] = valeur,
        }).Build(), stockage);
        const long Mo = 1024 * 1024;
        var maintenant = DateTimeOffset.UtcNow;

        // Par MEP : deux dépôts simultanés de 600 Ko ne tiennent pas dans 1 Mo.
        using (var parMep = Quotas("Stockage:QuotaMepMo", "1"))
        using (await parMep.Reserver(db, id, aliceId, 600 * 1024, maintenant, default))
        {
            var refus = await Assert.ThrowsAsync<ErreurHttp>(() => parMep.Reserver(db, id, aliceId, 600 * 1024, maintenant, default));
            Assert.Equal(422, refus.Statut);
        }
        // Par compte sur 24 heures.
        using (var parCompte = Quotas("Stockage:QuotaCompteJourMo", "1"))
            Assert.Equal(429, (await Assert.ThrowsAsync<ErreurHttp>(() => parCompte.Reserver(db, null, aliceId, 2 * Mo, maintenant, default))).Statut);
        // Disque presque plein.
        using (var disque = Quotas("Stockage:EspaceLibreMinMo", "999999999"))
            Assert.Equal(507, (await Assert.ThrowsAsync<ErreurHttp>(() => disque.Reserver(db, id, aliceId, 1, maintenant, default))).Statut);
    }

    [Fact]
    public async Task Le_nom_affiche_ne_peut_pas_imiter_un_autre_compte_et_le_journal_garde_le_login()
    {
        var bob = await _api.ClientConnecte("bob");
        var alice = await _api.ClientConnecte("alice");
        var refus = await bob.PutAsJsonAsync("/api/utilisateurs/moi/profil", new { nomAffiche = "alice" });
        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);

        await Ok(await bob.PutAsJsonAsync("/api/utilisateurs/moi/profil", new { nomAffiche = "Robert" }));
        var journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        var renomme = journal.EnumerateArray().First(e => e.GetProperty("type").GetString() == "compte.renomme");
        Assert.Contains("(bob)", renomme.GetProperty("detail").GetString());
        Assert.Equal("bob", renomme.GetProperty("auteurLogin").GetString());

        var id = await AidesApi.IdDemo(alice);
        var evenements = await Ok(await alice.GetAsync($"/api/meps/{id}/journal"));
        Assert.Contains(evenements.EnumerateArray(), e => e.GetProperty("auteurLogin").GetString() == "alice");
    }

    [Theory]
    [InlineData("changer-moi", false)]
    [InlineData("Admin", false)]
    [InlineData("password1", false)]
    [InlineData("court", false)]
    [InlineData("Un-mot-de-passe-solide-42", true)]
    public void Le_mot_de_passe_initial_d_exemple_est_refuse(string motDePasse, bool accepte) =>
        Assert.Equal(accepte, AuthentificationLocale.MotDePasseInitialRefuse(motDePasse) is null);

    [Fact]
    public async Task Une_archive_qui_omet_une_table_est_refusee_avant_toute_modification()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var stockage = scope.ServiceProvider.GetRequiredService<StockagePiecesJointes>();
        var dossier = Path.Combine(_api.DossierPieces, "sauvegardes");
        var sauvegardes = new Sauvegardes(dossier, stockage.Dossier, _ => { }, _ => { });
        var complete = Path.Combine(dossier, "complete.tar.gz");
        var manifeste = await sauvegardes.Exporter(db, complete);

        // Même archive, mais le manifeste et le contenu oublient la table des MEP.
        var tronquee = Path.Combine(dossier, "tronquee.tar.gz");
        await using (var source = new TarReader(new GZipStream(File.OpenRead(complete), CompressionMode.Decompress)))
        await using (var cible = new TarWriter(new GZipStream(File.Create(tronquee), CompressionLevel.Fastest), TarEntryFormat.Pax))
        {
            while (await source.GetNextEntryAsync(copyData: true) is { } entree)
            {
                if (entree.Name == "base/Meps.copy") continue;
                if (entree.Name == "manifest.json")
                {
                    var json = JsonSerializer.Serialize(manifeste with { Tables = manifeste.Tables.Where(t => t.Nom != "Meps").ToList() });
                    var nouvelle = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json") { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)) };
                    await cible.WriteEntryAsync(nouvelle);
                    continue;
                }
                await cible.WriteEntryAsync(entree);
            }
        }

        var refus = await Assert.ThrowsAsync<ErreurHttp>(() => sauvegardes.Importer(db, tronquee, "test"));
        Assert.Contains("Meps", refus.Message);
        // Rien n'a été touché : la MEP de démonstration est toujours là, et aucune sauvegarde préalable n'a été faite.
        Assert.True(await db.Meps.AnyAsync(m => m.Id == id));
        Assert.Empty(Directory.GetFiles(dossier, "avant-import-*"));
    }
}
