using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Cli;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;

namespace GestionMep.Tests;

/// <summary>Console d'administration : comptes, mots de passe provisoires, sessions coupées, journal, commandes d'exploitation.</summary>
[Collection("Api")]
public class AdministrationTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    private static async Task<Guid> IdCompte(HttpClient admin, string login) =>
        (await Ok(await admin.GetAsync("/api/admin/utilisateurs"))).EnumerateArray()
            .Single(u => u.GetProperty("login").GetString() == login).GetProperty("id").GetGuid();

    [Fact]
    public async Task Les_routes_admin_sont_reservees_aux_administrateurs()
    {
        var bob = await _api.ClientConnecte("bob");
        var rep = await bob.GetAsync("/api/admin/utilisateurs");
        Assert.Equal(HttpStatusCode.Forbidden, rep.StatusCode);
        Assert.Equal("Réservé aux administrateurs.", await Message(rep));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().GetAsync("/api/admin/journal")).StatusCode);
    }

    [Fact]
    public async Task Un_compte_cree_doit_changer_son_mot_de_passe_provisoire_avant_tout()
    {
        var alice = await _api.ClientConnecte("alice");
        var cree = await alice.PostAsJsonAsync("/api/admin/utilisateurs", new { login = " David ", nomAffiche = "David", estAdmin = false, role = "Dev", contact = "" });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var corps = await Ok(cree);
        var provisoire = corps.GetProperty("motDePasseProvisoire").GetString()!;
        Assert.Matches("^[A-Za-z0-9]{4}(-[A-Za-z0-9]{4}){3}$", provisoire);
        Assert.Equal("david", corps.GetProperty("utilisateur").GetProperty("login").GetString());
        Assert.True(corps.GetProperty("utilisateur").GetProperty("doitChangerMotDePasse").GetBoolean());

        var david = await _api.ClientConnecte("david", provisoire);
        var bloque = await david.GetAsync("/api/meps");
        Assert.Equal(HttpStatusCode.Forbidden, bloque.StatusCode);
        Assert.Equal("Changez votre mot de passe provisoire avant de continuer.", await Message(bloque));
        Assert.True((await Ok(await david.GetAsync("/api/auth/moi"))).GetProperty("doitChangerMotDePasse").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await david.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = provisoire, nouveau = "court" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await david.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = "faux", nouveau = "un mot de passe solide" })).StatusCode);
        var change = await Ok(await david.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = provisoire, nouveau = "un mot de passe solide" }));
        Assert.False(change.GetProperty("doitChangerMotDePasse").GetBoolean());
        // La session courante reste ouverte (cookie réémis) et n'est plus bloquée.
        Assert.Equal(HttpStatusCode.OK, (await david.GetAsync("/api/meps")).StatusCode);
        await _api.ClientConnecte("david", "un mot de passe solide");

        var journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        var types = journal.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("compte.cree", types);
        Assert.Contains("compte.mdp-change", types);
    }

    [Fact]
    public async Task Desactiver_un_compte_coupe_ses_sessions_et_bloque_la_connexion()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var idBob = await IdCompte(alice, "bob");

        var desactive = await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idBob}/desactiver", null));
        Assert.False(desactive.GetProperty("actif").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.GetAsync("/api/meps")).StatusCode);
        var connexion = await _api.Client().PostAsJsonAsync("/api/auth/connexion", new { login = "bob", motDePasse = "demo" });
        Assert.Equal(HttpStatusCode.Forbidden, connexion.StatusCode);
        Assert.Equal("Ce compte est désactivé. Contactez un administrateur.", await Message(connexion));

        await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idBob}/reactiver", null));
        await _api.ClientConnecte("bob");
    }

    [Fact]
    public async Task Le_role_admin_retire_s_applique_sans_attendre_l_expiration_de_la_session()
    {
        var alice = await _api.ClientConnecte("alice");
        var idBob = await IdCompte(alice, "bob");
        await Ok(await alice.PutAsJsonAsync($"/api/admin/utilisateurs/{idBob}", new { nomAffiche = "Bob", estAdmin = true, role = "", contact = "" }));
        var bob = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/admin/utilisateurs")).StatusCode);

        await Ok(await alice.PutAsJsonAsync($"/api/admin/utilisateurs/{idBob}", new { nomAffiche = "Bob", estAdmin = false, role = "", contact = "" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/utilisateurs")).StatusCode);
    }

    [Fact]
    public async Task Le_dernier_administrateur_actif_est_protege()
    {
        var alice = await _api.ClientConnecte("alice");
        var idAlice = await IdCompte(alice, "alice");
        var retrait = await alice.PutAsJsonAsync($"/api/admin/utilisateurs/{idAlice}", new { nomAffiche = "Alice", estAdmin = false, role = "", contact = "" });
        Assert.Equal((HttpStatusCode)422, retrait.StatusCode);
        Assert.Equal("Impossible de retirer le rôle administrateur au dernier administrateur actif.", await Message(retrait));
        var soi = await alice.PostAsync($"/api/admin/utilisateurs/{idAlice}/desactiver", null);
        Assert.Equal((HttpStatusCode)422, soi.StatusCode);
        Assert.Equal("Vous ne pouvez pas désactiver votre propre compte.", await Message(soi));
    }

    [Fact]
    public async Task Reinitialiser_le_mot_de_passe_coupe_les_sessions_et_rend_le_mot_de_passe_provisoire()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var idClaire = await IdCompte(alice, "claire");
        var rep = await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idClaire}/reinitialiser-mot-de-passe", null));
        var provisoire = rep.GetProperty("motDePasseProvisoire").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await claire.GetAsync("/api/meps")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().PostAsJsonAsync("/api/auth/connexion", new { login = "claire", motDePasse = "demo" })).StatusCode);
        var nouvelle = await _api.ClientConnecte("claire", provisoire);
        Assert.Equal(HttpStatusCode.Forbidden, (await nouvelle.GetAsync("/api/meps")).StatusCode);
    }

    [Fact]
    public async Task Les_parametres_globaux_sont_journalises()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PutAsJsonAsync("/api/parametres", new { approbationsMinimum = 2 }));
        var journal = await Ok(await alice.GetAsync("/api/admin/journal?limite=5"));
        var dernier = journal.EnumerateArray().First();
        Assert.Equal("parametres.modifies", dernier.GetProperty("type").GetString());
        Assert.Equal("Alice", dernier.GetProperty("auteur").GetString());
    }

    [Fact]
    public async Task Les_commandes_d_exploitation_gerent_les_comptes()
    {
        var sortie = new StringWriter();
        var avant = Console.Out;
        Console.SetOut(sortie);
        try
        {
            Assert.Equal(0, await LigneDeCommande.Executer(["comptes", "creer", "secours", "--nom", "Compte de secours", "--admin"]));
            Assert.Equal(1, await LigneDeCommande.Executer(["comptes", "creer", "secours"]));
            Assert.Equal(0, await LigneDeCommande.Executer(["comptes", "desactiver", "bob"]));
            Assert.Equal(0, await LigneDeCommande.Executer(["comptes", "lister"]));
            Assert.Equal(0, await LigneDeCommande.Executer(["config", "verifier"]));
            Assert.Equal(0, await LigneDeCommande.Executer(["migrer"]));
            Assert.Equal(2, await LigneDeCommande.Executer(["comptes", "inconnue"]));
        }
        finally
        {
            Console.SetOut(avant);
        }
        var texte = sortie.ToString();
        var provisoire = System.Text.RegularExpressions.Regex.Match(texte, "Mot de passe provisoire : (\\S+)").Groups[1].Value;
        Assert.Matches(@"(?m)^bob\s+non\s+désactivé\s+Bob$", texte);
        Assert.Contains("[ok]        Base PostgreSQL joignable.", texte);
        Assert.Contains("Base à jour", texte);

        var secours = await _api.ClientConnecte("secours", provisoire);
        Assert.True((await Ok(await secours.GetAsync("/api/auth/moi"))).GetProperty("estAdmin").GetBoolean());
        var alice = await _api.ClientConnecte("alice");
        var journal = await Ok(await alice.GetAsync("/api/admin/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("auteur").GetString() == "Ligne de commande" && e.GetProperty("type").GetString() == "compte.desactive");
    }

    [Fact]
    public async Task L_application_donne_sa_version_le_lien_de_signalement_et_les_mises_a_jour_aux_seuls_admins()
    {
        var bob = await _api.ClientConnecte("bob");
        var vueBob = await Ok(await bob.GetAsync("/api/application"));
        Assert.False(string.IsNullOrEmpty(vueBob.GetProperty("version").GetString()));
        Assert.Equal(ApplicationEndpoints.LienSignalementParDefaut, vueBob.GetProperty("lienSignalement").GetString());
        Assert.Equal(JsonValueKind.Null, vueBob.GetProperty("miseAJour").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync("/api/admin/mises-a-jour/verifier", null)).StatusCode);

        var alice = await _api.ClientConnecte("alice");
        var miseAJour = (await Ok(await alice.GetAsync("/api/application"))).GetProperty("miseAJour");
        Assert.False(miseAJour.GetProperty("active").GetBoolean()); // désactivée pendant les tests : aucun appel à GitHub
        Assert.False(miseAJour.GetProperty("disponible").GetBoolean());
        Assert.False((await Ok(await alice.PostAsync("/api/admin/mises-a-jour/verifier", null))).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task L_installation_d_une_mise_a_jour_exige_le_service_actif_et_une_version_publiee_plus_recente()
    {
        var alice = await _api.ClientConnecte("alice");
        var installer = () => alice.PostAsJsonAsync("/api/admin/mises-a-jour/installer", new { version = "9.9.9" });
        var absent = await installer();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, absent.StatusCode);
        // Sans Docker (tests lancés hors conteneur), le message donne la commande du script d'installation.
        Assert.Contains(Installation.EstDocker ? "pas actif" : "Installation sans Docker", await Message(absent));

        // Service présent (battement récent) : la version doit être la dernière publiée, inconnue ici (vérification coupée).
        Directory.CreateDirectory(_api.DossierMiseAJour);
        File.WriteAllText(Path.Combine(_api.DossierMiseAJour, "etat.json"),
            $$"""{"statut":"pret","version":"","message":"Service de mise à jour prêt.","le":"{{DateTimeOffset.UtcNow:O}}","battement":"{{DateTimeOffset.UtcNow:O}}"}""");
        var installateur = (await Ok(await alice.GetAsync("/api/application"))).GetProperty("miseAJour").GetProperty("installateur");
        Assert.True(installateur.GetProperty("present").GetBoolean());
        Assert.Equal("pret", installateur.GetProperty("statut").GetString());
        var refus = await installer();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("dernière version publiée", await Message(refus));
        Assert.False(File.Exists(Path.Combine(_api.DossierMiseAJour, "demande.json")));

        var bob = await _api.ClientConnecte("bob");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/mises-a-jour/installer", new { version = "9.9.9" })).StatusCode);
    }
}
