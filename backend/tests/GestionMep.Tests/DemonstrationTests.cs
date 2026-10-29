using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Mode démonstration : comptes proposés sans connexion, connexion rapide limitée aux comptes de démonstration,
/// réinitialisation réservée aux administrateurs, MEP de démonstration dans chaque état.
/// </summary>
[Collection("Api")]
public class DemonstrationTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    private static Task<HttpResponseMessage> ConnexionRapide(HttpClient c, string login) =>
        c.PostAsJsonAsync("/api/demo/connexion", new { login });

    private static async Task<string> Moi(HttpClient c) => (await Ok(await c.GetAsync("/api/auth/moi"))).GetProperty("login").GetString()!;

    [Fact]
    public async Task Les_comptes_de_demonstration_sont_proposes_sans_connexion()
    {
        var demo = await Ok(await _api.Client().GetAsync("/api/demo"));
        Assert.True(demo.GetProperty("actif").GetBoolean());
        var comptes = demo.GetProperty("comptes").EnumerateArray().ToList();
        Assert.Equal(["alice", "bob", "claire"], comptes.Select(c => c.GetProperty("login").GetString()!).ToArray());
        Assert.True(comptes[0].GetProperty("estAdmin").GetBoolean());
        Assert.False(comptes[1].GetProperty("estAdmin").GetBoolean());
        Assert.Equal("Pilote / DBA", comptes[0].GetProperty("role").GetString());
        Assert.All(comptes, c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("description").GetString())));

        // Un compte désactivé n'est plus proposé.
        var alice = await _api.ClientConnecte("alice");
        var claire = (await Ok(await alice.GetAsync("/api/admin/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == "claire");
        await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{claire.GetProperty("id").GetGuid()}/desactiver", null));
        demo = await Ok(await _api.Client().GetAsync("/api/demo"));
        Assert.DoesNotContain(demo.GetProperty("comptes").EnumerateArray(), c => c.GetProperty("login").GetString() == "claire");
        Assert.Equal(HttpStatusCode.Forbidden, (await ConnexionRapide(_api.Client(), "claire")).StatusCode);
    }

    [Fact]
    public async Task La_connexion_rapide_ouvre_une_session_comme_le_formulaire()
    {
        var client = _api.Client();
        var rep = await ConnexionRapide(client, " Bob ");
        Assert.Equal("bob", (await Ok(rep)).GetProperty("login").GetString());
        Assert.Contains(rep.Headers.GetValues("Set-Cookie"), c => c.StartsWith("gestion-mep.auth=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("bob", await Moi(client));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/meps")).StatusCode);

        // Protection anti-CSRF : sans l'en-tête X-Requested-With, refus.
        var forge = await _api.ClientSansEnTete().PostAsJsonAsync("/api/demo/connexion", new { login = "alice" });
        Assert.Equal(HttpStatusCode.Forbidden, forge.StatusCode);
        Assert.False(forge.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task La_connexion_rapide_ne_connecte_jamais_un_autre_compte()
    {
        var alice = await _api.ClientConnecte("alice");
        // Administrateur créé ensuite, puis mot de passe choisi : jamais connecté par la connexion rapide.
        var cree = await Ok(await alice.PostAsJsonAsync("/api/admin/utilisateurs", new { login = "david", nomAffiche = "David", estAdmin = true }));
        Assert.True(cree.GetProperty("utilisateur").GetProperty("estAdmin").GetBoolean());
        foreach (var login in new[] { "david", "admin", "", "alice2", new string('a', 300) })
        {
            var refus = await ConnexionRapide(_api.Client(), login);
            Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
            Assert.False(refus.Headers.Contains("Set-Cookie"));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.Client().PostAsync("/api/demo/connexion",
            new StringContent("{pas du json", System.Text.Encoding.UTF8, "application/json"))).StatusCode);

        // Compte de démonstration dont le mot de passe a changé : refusé (le mot de passe « demo » ne vaut plus).
        var bob = await _api.ClientConnecte("bob");
        await Ok(await bob.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = "demo", nouveau = "un-mot-de-passe-solide" }));
        var change = await ConnexionRapide(_api.Client(), "bob");
        Assert.Equal(HttpStatusCode.Unauthorized, change.StatusCode);
        Assert.Contains("n'utilise plus le mot de passe", await Message(change));
        // Les échecs comptent comme ceux du formulaire : blocage progressif du login depuis cette adresse.
        for (var i = 1; i < 5; i++) await ConnexionRapide(_api.Client(), "bob");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ConnexionRapide(_api.Client(), "bob")).StatusCode);
    }

    [Fact]
    public async Task La_reinitialisation_est_reservee_aux_administrateurs()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().PostAsync("/api/demo/reinitialiser", null)).StatusCode);
        var bob = await _api.ClientConnecte("bob");
        var refus = await bob.PostAsync("/api/demo/reinitialiser", null);
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientSansEnTete().PostAsync("/api/demo/reinitialiser", null)).StatusCode);
        Assert.Equal("bob", await Moi(bob)); // rien n'a bougé
    }

    [Fact]
    public async Task La_reinitialisation_remet_l_etat_initial_de_la_demonstration()
    {
        var initial = await Etat();
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");

        // Modifications : MEP créée, MEP de démo supprimée, pièce jointe, compte, paramètre, réglage, catalogue, mot de passe.
        var neuve = await Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Temporaire" }));
        var idPortail = await AidesApi.IdDemo(alice);
        var fichier = new MultipartFormDataContent();
        var contenu = new ByteArrayContent([1, 2, 3]);
        contenu.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        fichier.Add(contenu, "fichier", "note.txt");
        var piece = (await Ok(await alice.PostAsync($"/api/meps/{idPortail}/pieces-jointes", fichier))).GetProperty("piecesJointes")[0].GetProperty("id").GetGuid();
        var cheminPiece = Path.Combine(_api.DossierPieces, piece.ToString("N"));
        Assert.True(File.Exists(cheminPiece));
        await Ok(await alice.PostAsJsonAsync("/api/admin/utilisateurs", new { login = "david", nomAffiche = "David", estAdmin = true }));
        await Ok(await alice.PutAsJsonAsync("/api/parametres", new { approbationsMinimum = 3 }));
        await Ok(await alice.PutAsJsonAsync("/api/admin/reglages/lien-signalement", new { valeur = "https://outil.exemple.invalid" }));
        await Ok(await alice.PostAsJsonAsync("/api/catalogue/systemes", new { nom = "CRM", description = "", cible = "", actif = true }));
        await Ok(await bob.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = "demo", nouveau = "un-mot-de-passe-solide" }));
        Assert.NotEqual(initial, await Etat());

        var rep = await Ok(await alice.PostAsync("/api/demo/reinitialiser", null));
        Assert.Equal(16, rep.GetProperty("meps").GetInt32());
        Assert.True(rep.GetProperty("fichiersSupprimes").GetInt32() >= 1);

        // Base identique à une base neuve en démonstration ; fichiers supprimés ; anciennes sessions refusées.
        Assert.Equal(initial, await Etat());
        Assert.False(File.Exists(cheminPiece));
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.GetAsync("/api/meps")).StatusCode);
        var nouvelleAlice = await _api.ClientConnecte("alice");
        Assert.Equal(HttpStatusCode.NotFound, (await nouvelleAlice.GetAsync($"/api/meps/{neuve.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConnexionRapide(_api.Client(), "bob")).StatusCode); // mot de passe « demo » rétabli
        Assert.Equal(1, (await Ok(await nouvelleAlice.GetAsync("/api/parametres"))).GetProperty("approbationsMinimum").GetInt32());
        var reglage = (await Ok(await nouvelleAlice.GetAsync("/api/admin/reglages"))).EnumerateArray().Single(r => r.GetProperty("nom").GetString() == "lien-signalement");
        Assert.NotEqual("console", reglage.GetProperty("origine").GetString());

        // Journal d'administration : seule la ligne de la réinitialisation, signée par Alice (nouveau compte).
        var journal = (await Ok(await nouvelleAlice.GetAsync("/api/admin/journal"))).EnumerateArray().ToList();
        var ligne = Assert.Single(journal);
        Assert.Equal("demo.reinitialisee", ligne.GetProperty("type").GetString());
        Assert.Equal("Démonstration réinitialisée par Alice (alice)", ligne.GetProperty("detail").GetString());
        Assert.Equal("alice", ligne.GetProperty("auteurLogin").GetString());
    }

    /// <summary>Contenu comparable de la base : comptes, MEP (outil, phase, issue, étapes, journal), catalogue, réglages…</summary>
    private async Task<string> Etat()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var comptes = await db.Utilisateurs.OrderBy(u => u.Login).Select(u => $"{u.Login}:{u.EstAdmin}:{u.Actif}:{u.Role}").ToListAsync();
        var meps = await db.Meps.OrderBy(m => m.Outil).Select(m => $"{m.Outil}:{m.EstModele}:{m.Phase}:{m.Issue}:{m.Etapes.Count}:"
            + $"{m.Etapes.Count(e => e.Etat == EtatEtape.Reussie)}:{db.Journal.Count(j => j.MepId == m.Id)}:{m.Resolutions.Count}").ToListAsync();
        var etats = await db.Etapes.GroupBy(e => e.Etat).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}").ToListAsync();
        return string.Join("|", comptes) + "\n" + string.Join("|", meps) + "\n" + string.Join("|", etats) + "\n"
            + $"catalogue={await db.SystemesCatalogue.CountAsync()} resolutions={await db.ResolutionsTypes.CountAsync()} "
            + $"versions={await db.VersionsResolutionType.CountAsync()} criteres={await db.CriteresGoNoGo.CountAsync()} "
            + $"approbations={await db.Parametres.Select(p => p.ApprobationsMinimum).SingleAsync()} reglages={await db.Reglages.CountAsync()} "
            + $"sso={await db.ConfigurationsSso.CountAsync(s => !s.Active)} pieces={await db.PiecesJointes.CountAsync()} "
            + $"admin={await db.JournalAdmin.CountAsync(j => j.Type != "demo.reinitialisee")}";
    }

    [Fact]
    public async Task Les_mep_de_demonstration_s_ouvrent_dans_leur_etat()
    {
        var alice = await _api.ClientConnecte("alice");
        var liste = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().ToList();
        Assert.Equal(16, liste.Count);
        Assert.Equal(DonneesDemo.OutilPortail, liste[0].GetProperty("outil").GetString()); // la plus récente reste en tête
        var phases = liste.GroupBy(m => m.GetProperty("phase").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(4, phases["Preparation"]);
        Assert.Equal(2, phases["Execution"]);
        Assert.Equal(1, phases["Recette"]);
        Assert.Equal(1, phases["RetourArriere"]);
        Assert.Equal(7, phases["Cloturee"]);
        Assert.Equal(1, phases["Annulee"]);
        Assert.Equal(["Echec", "Repliee", "Repliee", "Reussie", "Reussie", "Reussie", "Reussie"], liste.Where(m => m.GetProperty("phase").GetString() == "Cloturee")
            .Select(m => m.GetProperty("issue").GetString()!).Order().ToArray());

        async Task<JsonElement> Mep(string outil) => await Ok(await alice.GetAsync($"/api/meps/{await AidesApi.IdMep(alice, outil)}"));
        string Dispo(JsonElement mep, string code) => mep.GetProperty("etapes").EnumerateArray()
            .Single(e => e.GetProperty("code").GetString() == code).GetProperty("disponibilite").GetProperty("libelle").GetString()!;

        Assert.NotEmpty((await Mep(DonneesDemo.OutilIncomplete)).GetProperty("erreursPlan").EnumerateArray());
        Assert.Empty((await Mep(DonneesDemo.OutilPrete)).GetProperty("erreursPlan").EnumerateArray());
        var execution = await Mep(DonneesDemo.OutilExecution);
        Assert.Equal("En cours", Dispo(execution, "E0004"));
        Assert.Equal("Résolution en cours", Dispo(execution, "E0005"));
        var recette = await Mep(DonneesDemo.OutilRecette);
        Assert.Equal("Reussie", Assert.Single(recette.GetProperty("resolutions").EnumerateArray()).GetProperty("statut").GetString());
        var repli = await Mep(DonneesDemo.OutilRetourArriere);
        // Après l'échec de la résolution, c'est la pilote qui a déclenché le retour arrière (#94).
        Assert.Equal("Alice", repli.GetProperty("retourArriere").GetProperty("par").GetString());
        Assert.Equal("En cours", Dispo(repli, "R0002"));
        Assert.StartsWith("E0003 en échec", (await Mep(DonneesDemo.OutilReussie)).GetProperty("bilan").GetProperty("incidents").GetString());
        // Sorties du cycle de vie (#93) : reportée avec sa date visée, annulée après un Go, clôturée en échec après un secours.
        var reportee = await Mep(DonneesDemo.OutilReportee);
        Assert.Equal("Reportee", reportee.GetProperty("etat").GetString());
        Assert.NotEqual(JsonValueKind.Null, reportee.GetProperty("report").GetProperty("dateVisee").ValueKind);
        var annulee = await Mep(DonneesDemo.OutilAnnulee);
        Assert.NotEqual("", annulee.GetProperty("annulation").GetProperty("etatProduction").GetString());
        Assert.NotEqual(JsonValueKind.Null, annulee.GetProperty("premierGo").ValueKind);
        var echec = await Mep(DonneesDemo.OutilEchec);
        Assert.Equal("Echec", echec.GetProperty("etat").GetString());
        Assert.Equal("INC-DEMO-1042", echec.GetProperty("cloture").GetProperty("referenceIncident").GetString());
        Assert.Equal("Alice", echec.GetProperty("pilotageSecours").GetProperty("nouveauPilote").GetString());

        // Journal de chaque MEP lisible, et aucun retour arrière déclenché par l'échéance à l'ouverture.
        foreach (var m in liste)
        {
            var journal = (await Ok(await alice.GetAsync($"/api/meps/{m.GetProperty("id").GetGuid()}/journal"))).EnumerateArray().ToList();
            Assert.NotEmpty(journal);
            Assert.DoesNotContain(journal, e => e.GetProperty("detail").GetString()!.Contains(Regles.MotifEcheance, StringComparison.Ordinal));
        }
        // Bob pilote « Annuaire des agences » : il peut donner le Go (après approbation de Claire, déjà donnée).
        var bob = await _api.ClientConnecte("bob");
        var idPrete = await AidesApi.IdMep(bob, DonneesDemo.OutilPrete);
        await Ok(await bob.PostAsJsonAsync($"/api/meps/{idPrete}/phase", new { phase = "Execution", criteresCoches = await AidesApi.CriteresActifs(bob) }));
    }
    [Fact]
    public async Task La_commande_demo_reinitialiser_exige_oui_et_remet_l_etat_initial()
    {
        var alice = await _api.ClientConnecte("alice");
        await Ok(await alice.PostAsJsonAsync("/api/meps", new { outil = "Temporaire" }));
        var (code, _, erreur) = await Cli("demo", "reinitialiser");
        Assert.Equal(4, code);
        Assert.Contains("--oui", erreur);
        Assert.Equal(17, (await Ok(await alice.GetAsync("/api/meps?modele=false"))).GetArrayLength());

        var (fait, sortie, _) = await Cli("demo", "reinitialiser", "--oui");
        Assert.Equal(0, fait);
        Assert.Contains("Démonstration réinitialisée : 16 MEP", sortie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/meps")).StatusCode);
        var nouvelleAlice = await _api.ClientConnecte("alice");
        Assert.Equal(16, (await Ok(await nouvelleAlice.GetAsync("/api/meps?modele=false"))).GetArrayLength());
        var ligne = Assert.Single((await Ok(await nouvelleAlice.GetAsync("/api/admin/journal"))).EnumerateArray());
        Assert.Equal(("demo.reinitialisee", "Ligne de commande"), (ligne.GetProperty("type").GetString(), ligne.GetProperty("auteur").GetString()));
        Assert.Contains("gestion-mep demo reinitialiser [--oui]", (await Cli("aide")).Sortie);
    }

    private static async Task<(int Code, string Sortie, string Erreur)> Cli(params string[] args)
    {
        var (sortie, erreur) = (new StringWriter(), new StringWriter());
        var (avant, avantErreur) = (Console.Out, Console.Error);
        Console.SetOut(sortie);
        Console.SetError(erreur);
        try
        {
            return (await LigneDeCommande.Executer(args), sortie.ToString(), erreur.ToString());
        }
        finally
        {
            Console.SetOut(avant);
            Console.SetError(avantErreur);
        }
    }
}

/// <summary>Hors mode démonstration : aucune route /api/demo, commande refusée, aucune donnée de démonstration.</summary>
[Collection("Api")]
public class HorsDemonstrationTests : IDisposable
{
    private readonly ApiFixture _api = new(demo: false);
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Rien_n_est_expose_ni_cree()
    {
        var client = _api.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/demo/connexion", new { login = "alice" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/demo/reinitialiser", null)).StatusCode);
        // Même réponse qu'une route inconnue, même après de nombreux essais (aucune limite de débit propre à la démo).
        for (var i = 0; i < 30; i++) Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/demo/connexion", new { login = "alice" })).StatusCode);
        Assert.Contains("\"message\":\"Route introuvable.\"", await (await client.GetAsync("/api/demo")).Content.ReadAsStringAsync());

        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        Assert.False(await db.Utilisateurs.AnyAsync());
        Assert.False(await db.Meps.AnyAsync());

        var (avant, avantErreur) = (Console.Out, Console.Error);
        var erreur = new StringWriter();
        Console.SetOut(new StringWriter());
        Console.SetError(erreur);
        try
        {
            Assert.Equal(1, await LigneDeCommande.Executer(["demo", "reinitialiser", "--oui"]));
        }
        finally
        {
            Console.SetOut(avant);
            Console.SetError(avantErreur);
        }
        Assert.Contains("n'est pas activé", erreur.ToString());
        Assert.False(await db.Utilisateurs.AnyAsync());
    }
}
