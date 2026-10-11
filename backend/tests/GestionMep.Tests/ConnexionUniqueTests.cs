using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>Connexion unique facultative : réglages de la console, bouton de connexion, liaison des comptes.</summary>
[Collection("Api")]
public class ConnexionUniqueTests : IDisposable
{
    private const string Annuaire = "11111111-2222-3333-4444-555555555555";
    private const string Emetteur = $"https://login.microsoftonline.com/{Annuaire}/v2.0";
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static object Reglages(string annuaire = Annuaire, string? secret = "secret-de-test", string roleAdmin = "", bool creation = true) =>
        new { fournisseur = "Microsoft", annuaire, autorite = "", clientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", secret, secretExpireLe = "2027-04-01", creationAutomatique = creation, roleAdmin, libelle = "" };

    [Fact]
    public async Task La_connexion_unique_est_desactivee_par_defaut_et_le_mot_de_passe_suffit()
    {
        var options = await Ok(await _api.Client().GetAsync("/api/auth/options"));
        Assert.False(options.GetProperty("ssoActif").GetBoolean());

        var alice = await _api.ClientConnecte("alice");
        var reglages = await Ok(await alice.GetAsync("/api/admin/sso"));
        Assert.False(reglages.GetProperty("active").GetBoolean());
        Assert.EndsWith("/connexion-sso/retour", reglages.GetProperty("urlRetour").GetString());
        Assert.NotEmpty(reglages.GetProperty("anomalies").EnumerateArray());

        var bob = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/sso")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync("/api/admin/sso", Reglages())).StatusCode);
    }

    [Fact]
    public async Task Les_reglages_refusent_un_annuaire_ouvert_a_tous_et_ne_renvoient_jamais_le_secret()
    {
        var alice = await _api.ClientConnecte("alice");
        var refus = await alice.PutAsJsonAsync("/api/admin/sso", Reglages(annuaire: "common"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("tous les comptes Microsoft", await refus.Content.ReadAsStringAsync());

        // Activer sans secret est refusé.
        await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages(secret: null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsync("/api/admin/sso/activer", null)).StatusCode);

        var enregistres = await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages()));
        Assert.True(enregistres.GetProperty("secretEnregistre").GetBoolean());
        Assert.True(enregistres.GetProperty("secretLisible").GetBoolean());
        Assert.DoesNotContain("secret-de-test", enregistres.GetRawText());
        Assert.Empty(enregistres.GetProperty("anomalies").EnumerateArray());

        // Secret absent de la saisie : inchangé.
        var sansSecret = await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages(secret: "")));
        Assert.True(sansSecret.GetProperty("secretLisible").GetBoolean());

        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var ligne = await db.ConfigurationsSso.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("secret-de-test", ligne.SecretChiffre);
        Assert.Equal("secret-de-test", ConnexionUnique.Secret(ligne, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()));
        Assert.Contains(await db.JournalAdmin.Select(e => e.Type).ToListAsync(), t => t == "sso.regle");
    }

    [Fact]
    public async Task Changer_d_annuaire_ou_d_application_efface_le_secret()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages()));
        await Ok(await alice.PostAsync("/api/admin/sso/activer", null));

        // Connexion active : changer d'annuaire sans nouveau secret est refusé (l'ancien ne partira pas vers un autre émetteur).
        var autre = "99999999-8888-7777-6666-555555555555";
        var refus = await alice.PutAsJsonAsync("/api/admin/sso", Reglages(annuaire: autre, secret: ""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("secret client est obligatoire", await refus.Content.ReadAsStringAsync());

        // Connexion désactivée : le changement passe et l'ancien secret est effacé.
        await Ok(await alice.PostAsync("/api/admin/sso/desactiver", null));
        var change = await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages(annuaire: autre, secret: "")));
        Assert.False(change.GetProperty("secretEnregistre").GetBoolean());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("secretExpireLe").ValueKind);

        // Avec le secret de la nouvelle application, tout redevient utilisable ; un réglage annexe garde le secret.
        await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages(annuaire: autre)));
        var annexe = await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages(annuaire: autre, secret: "", creation: false)));
        Assert.True(annexe.GetProperty("secretLisible").GetBoolean());
    }

    [Fact]
    public async Task Le_test_n_envoie_pas_le_secret_hors_de_chez_Microsoft()
    {
        using var scope = _api.Services.CreateScope();
        var cles = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var c = new ConfigurationSso { Fournisseur = FournisseurSso.Microsoft, Annuaire = Annuaire, ClientId = "client" };
        ConnexionUnique.PoserSecret(c, cles, "secret-de-test");
        var envois = new List<Uri>();
        using var http = new HttpClient(new FauxFournisseur("""{"token_endpoint":"https://attaquant.example/token"}""", envois));
        var verifications = await ConnexionUnique.Tester(c, cles, http);
        Assert.False(verifications.Last().Ok);
        Assert.Contains("login.microsoftonline.com", verifications.Last().Detail);
        Assert.DoesNotContain(envois, u => u.Host == "attaquant.example");
    }

    private sealed class FauxFournisseur(string decouverte, List<Uri> envois) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken ct)
        {
            envois.Add(requete.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(decouverte) });
        }
    }

    [Fact]
    public async Task Activer_affiche_le_bouton_et_desactiver_le_retire_sans_redemarrage()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages()));
        Assert.True((await Ok(await alice.PostAsync("/api/admin/sso/activer", null))).GetProperty("active").GetBoolean());

        var options = await Ok(await _api.Client().GetAsync("/api/auth/options"));
        Assert.True(options.GetProperty("ssoActif").GetBoolean());
        Assert.Equal(ConnexionUnique.LibelleMicrosoft, options.GetProperty("ssoLibelle").GetString());
        // Le mot de passe local reste possible.
        await _api.ClientConnecte("bob");

        await Ok(await alice.PostAsync("/api/admin/sso/desactiver", null));
        Assert.False((await Ok(await _api.Client().GetAsync("/api/auth/options"))).GetProperty("ssoActif").GetBoolean());
        var anonyme = _api.ClientSansRedirection();
        var depart = await anonyme.GetAsync("/api/auth/sso/connexion");
        Assert.Equal("/connexion?erreur=sso-inactif", depart.Headers.Location?.OriginalString);
        var retour = await anonyme.GetAsync("/connexion-sso/retour?code=x&state=y");
        Assert.Equal("/connexion?erreur=sso-inactif", retour.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Une_liaison_ne_se_fait_qu_avec_le_jeton_de_la_session_qui_la_demande()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PutAsJsonAsync("/api/admin/sso", Reglages()));
        await Ok(await alice.PostAsync("/api/admin/sso/activer", null));

        var bob = await _api.ClientConnecte("bob");
        var url = (await Ok(await bob.PostAsync("/api/auth/sso/lier", null))).GetProperty("url").GetString()!;
        Assert.StartsWith("/api/auth/sso/connexion?lier=", url);

        // Le jeton de Bob, rejoué par une autre session, est refusé avant tout passage chez le fournisseur.
        var claire = _api.ClientSansRedirection();
        await claire.PostAsJsonAsync("/api/auth/connexion", new { login = "claire", motDePasse = "demo" });
        var rejeu = await claire.GetAsync(url);
        Assert.Equal("/profil?sso=sso-session", rejeu.Headers.Location?.OriginalString);

        var etat = await Ok(await bob.GetAsync("/api/auth/sso/etat"));
        Assert.True(etat.GetProperty("disponible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, etat.GetProperty("identifiant").ValueKind);
    }

    private async Task<(ConnexionUnique.Resultat, MepDbContext, IServiceScope)> Resoudre(ConfigurationSso c, string sujet, string identifiant, Guid? lier = null, params string[] roles)
    {
        var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var r = await ConnexionUnique.Resoudre(db, c, new ConnexionUnique.Identite(Emetteur, sujet, identifiant, "Dana Martin", roles), lier, DateTimeOffset.UtcNow);
        return (r, db, scope);
    }

    [Fact]
    public async Task Une_premiere_connexion_cree_le_compte_puis_le_retrouve_par_l_identifiant_immuable()
    {
        var c = new ConfigurationSso { Active = true, CreationAutomatique = true };
        var (r, _, s1) = await Resoudre(c, "oid-dana", "Dana.Martin@entreprise.fr");
        using (s1)
        {
            Assert.Null(r.Erreur);
            Assert.Equal("dana.martin@entreprise.fr", r.Compte!.Login);
            Assert.Equal("Dana Martin", r.Compte.NomAffiche);
            Assert.False(r.Compte.EstAdmin);
        }
        // Même personne, adresse changée chez Microsoft : même compte (liaison par oid, pas par mail).
        var (r2, db, s2) = await Resoudre(c, "oid-dana", "dana.dupont@entreprise.fr");
        using (s2)
        {
            Assert.Equal(r.Compte.Id, r2.Compte!.Id);
            Assert.Equal("dana.dupont@entreprise.fr", (await db.IdentitesExternes.SingleAsync(x => x.Sujet == "oid-dana")).Identifiant);
        }
    }

    [Fact]
    public async Task Un_compte_local_n_est_jamais_rapproche_par_son_login_ni_cree_si_la_creation_est_coupee()
    {
        var (r, _, s1) = await Resoudre(new ConfigurationSso { CreationAutomatique = true }, "oid-x", "bob");
        using (s1) Assert.Equal("login-existant", r.Erreur);
        var (r2, _, s2) = await Resoudre(new ConfigurationSso { CreationAutomatique = false }, "oid-y", "inconnu@entreprise.fr");
        using (s2) Assert.Equal("compte-inconnu", r2.Erreur);
    }

    [Fact]
    public async Task Un_compte_local_se_lie_depuis_son_profil_une_seule_fois()
    {
        var c = new ConfigurationSso { CreationAutomatique = false };
        Guid bob, claire;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            bob = (await db.Utilisateurs.SingleAsync(u => u.Login == "bob")).Id;
            claire = (await db.Utilisateurs.SingleAsync(u => u.Login == "claire")).Id;
        }
        var (lie, _, s1) = await Resoudre(c, "oid-bob", "bob@entreprise.fr", bob);
        using (s1) Assert.Equal(bob, lie.Compte!.Id);
        var (ensuite, _, s2) = await Resoudre(c, "oid-bob", "bob@entreprise.fr");
        using (s2) Assert.Equal(bob, ensuite.Compte!.Id);
        // Ce compte Microsoft ne peut pas être lié en plus à Claire.
        var (vol, _, s3) = await Resoudre(c, "oid-bob", "bob@entreprise.fr", claire);
        using (s3) Assert.Equal("deja-lie", vol.Erreur);

        var bobClient = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.NoContent, (await bobClient.DeleteAsync("/api/auth/sso/lien")).StatusCode);
        var (apres, _, s4) = await Resoudre(c, "oid-bob", "bob@entreprise.fr");
        using (s4) Assert.Equal("compte-inconnu", apres.Erreur);
    }

    [Fact]
    public async Task Le_role_administrateur_suit_le_role_d_application_sauf_pour_le_dernier_administrateur()
    {
        var c = new ConfigurationSso { CreationAutomatique = true, RoleAdmin = "MEP.Admin" };
        var (admin, _, s1) = await Resoudre(c, "oid-eve", "eve@entreprise.fr", null, "MEP.Admin");
        using (s1) Assert.True(admin.Compte!.EstAdmin);
        var (retire, _, s2) = await Resoudre(c, "oid-eve", "eve@entreprise.fr");
        using (s2) Assert.False(retire.Compte!.EstAdmin);

        // Alice, liée et dernière administratrice active, garde son rôle même sans le rôle d'application.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            await db.Utilisateurs.Where(u => u.Login != "alice").ExecuteUpdateAsync(u => u.SetProperty(x => x.EstAdmin, false));
        }
        Guid alice;
        using (var scope = _api.Services.CreateScope())
            alice = (await scope.ServiceProvider.GetRequiredService<MepDbContext>().Utilisateurs.SingleAsync(u => u.Login == "alice")).Id;
        var (lie, _, s3) = await Resoudre(c, "oid-alice", "alice@entreprise.fr", alice);
        using (s3) Assert.True(lie.Compte!.EstAdmin);
    }
}
