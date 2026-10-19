using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Équipes, filtre des MEP par équipe, comptes reliés d'une même personne et comptes désactivés (API de bout en bout).</summary>
[Collection("Api")]
public class EquipesEtComptesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    private static async Task<JsonElement> Compte(HttpClient c, string login) =>
        (await Ok(await c.GetAsync("/api/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == login);

    private static async Task<Guid> IdCompte(HttpClient c, string login) => (await Compte(c, login)).GetProperty("id").GetGuid();

    /// <summary>Identifiants des MEP (hors modèles) dont un participant est relié à l'un des comptes donnés.</summary>
    private static async Task<HashSet<Guid>> MepsAvec(HttpClient c, params Guid[] comptes)
    {
        var ids = new HashSet<Guid>();
        foreach (var m in (await Ok(await c.GetAsync("/api/meps?modele=false"))).EnumerateArray())
        {
            var id = m.GetProperty("id").GetGuid();
            var mep = await Ok(await c.GetAsync($"/api/meps/{id}"));
            if (mep.GetProperty("participants").EnumerateArray().Any(p =>
                    p.GetProperty("utilisateurId").ValueKind == JsonValueKind.String && comptes.Contains(p.GetProperty("utilisateurId").GetGuid())))
                ids.Add(id);
        }
        return ids;
    }

    private static async Task<HashSet<Guid>> MepsDeLEquipe(HttpClient c, Guid equipe) =>
        (await Ok(await c.GetAsync($"/api/meps?modele=false&equipe={equipe}"))).EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ToHashSet();

    private static async Task<List<string>> TypesJournal(HttpClient admin) =>
        (await Ok(await admin.GetAsync("/api/admin/journal"))).EnumerateArray().Select(e => e.GetProperty("type").GetString()!).ToList();

    [Fact]
    public async Task Les_equipes_sont_gerees_par_les_administrateurs_et_filtrent_les_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var idBob = await IdCompte(alice, "bob");
        var idClaire = await IdCompte(alice, "claire");

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/equipes", new { nom = "Pirates" })).StatusCode);

        var cree = await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = " Exploitation ", description = "Équipe de nuit", membreIds = new[] { idBob } });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var equipe = await Ok(cree);
        var idEquipe = equipe.GetProperty("id").GetGuid();
        Assert.Equal("Exploitation", equipe.GetProperty("nom").GetString());
        var autre = await Ok(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "Développement", membreIds = new[] { idBob, idClaire } }));

        // Nom déjà pris (sans tenir compte de la casse), nom vide, compte inconnu.
        var doublon = await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "exploitation" });
        Assert.Equal(HttpStatusCode.BadRequest, doublon.StatusCode);
        Assert.Equal("L'équipe « exploitation » existe déjà.", await Message(doublon));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "X", membreIds = new[] { Guid.NewGuid() } })).StatusCode);

        // Lecture pour tous ; une personne appartient à plusieurs équipes (bob est aussi dans l'équipe de démonstration).
        Assert.Equal(4, (await Ok(await bob.GetAsync("/api/equipes"))).GetArrayLength());
        Assert.Equal(3, (await Compte(bob, "bob")).GetProperty("equipeIds").GetArrayLength());

        // Une MEP appartient à l'équipe dès qu'une de ses personnes en fait partie.
        var mepsDeBob = await MepsAvec(alice, idBob);
        Assert.NotEmpty(mepsDeBob);
        Assert.Equal(mepsDeBob, await MepsDeLEquipe(alice, idEquipe));
        var toutes = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().ToList();
        Assert.All(toutes, m => Assert.Equal(mepsDeBob.Contains(m.GetProperty("id").GetGuid()),
            m.GetProperty("equipeIds").EnumerateArray().Any(e => e.GetGuid() == idEquipe)));
        Assert.Equal(await MepsAvec(alice, idBob, idClaire), await MepsDeLEquipe(alice, autre.GetProperty("id").GetGuid()));

        // Renommer et retirer bob : la MEP ne fait plus partie de l'équipe ; supprimer ne touche ni comptes ni MEP.
        var modifiee = await Ok(await alice.PutAsJsonAsync($"/api/admin/equipes/{idEquipe}", new { nom = "Exploitation N2", description = "", membreIds = Array.Empty<Guid>() }));
        Assert.Equal("Exploitation N2", modifiee.GetProperty("nom").GetString());
        Assert.Empty(await MepsDeLEquipe(alice, idEquipe));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/admin/equipes/{idEquipe}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/admin/equipes/{idEquipe}")).StatusCode);
        Assert.Equal(toutes.Count, (await Ok(await alice.GetAsync("/api/meps?modele=false"))).GetArrayLength());

        var journal = (await Ok(await alice.GetAsync("/api/admin/journal"))).EnumerateArray().ToList();
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "equipe.creee" && e.GetProperty("detail").GetString()!.Contains("Bob (bob)"));
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "equipe.modifiee"
            && e.GetProperty("detail").GetString() == "Équipe « Exploitation » : renommée « Exploitation N2 », description modifiée, Bob (bob) retiré");
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "equipe.supprimee");
    }

    /// <summary>Crée un compte et change son mot de passe provisoire ; renvoie son identifiant et un client connecté.</summary>
    private async Task<(Guid Id, HttpClient Client)> NouveauCompte(HttpClient admin, string login, string nom)
    {
        var cree = await Ok(await admin.PostAsJsonAsync("/api/admin/utilisateurs", new { login, nomAffiche = nom, estAdmin = false, role = "", contact = "" }));
        var provisoire = cree.GetProperty("motDePasseProvisoire").GetString()!;
        var client = await _api.ClientConnecte(login, provisoire);
        await Ok(await client.PostAsJsonAsync("/api/auth/mot-de-passe", new { ancien = provisoire, nouveau = "un mot de passe solide" }));
        return (cree.GetProperty("utilisateur").GetProperty("id").GetGuid(), client);
    }

    [Fact]
    public async Task Deux_comptes_relies_apparaissent_comme_une_seule_personne_sans_perdre_l_historique()
    {
        var alice = await _api.ClientConnecte("alice");
        var idBob = await IdCompte(alice, "bob");
        var idAlice = await IdCompte(alice, "alice");
        var (idSso, bobSso) = await NouveauCompte(alice, "robert.sso", "Robert (SSO)");

        // Le compte secondaire crée une MEP avant la liaison : son historique est à son nom.
        var avant = await Ok(await bobSso.PostAsJsonAsync("/api/meps", new { outil = "Outil d'avant", objet = "" }));
        var idAvant = avant.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await (await _api.ClientConnecte("bob")).PostAsJsonAsync($"/api/admin/utilisateurs/{idSso}/relier", new { comptePrincipalId = idBob })).StatusCode);
        var relie = await Ok(await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idSso}/relier", new { comptePrincipalId = idBob }));
        Assert.Equal(idBob, relie.GetProperty("comptePrincipalId").GetGuid());
        Assert.Equal("Bob", relie.GetProperty("nomPersonne").GetString());
        Assert.Equal("Robert (SSO)", relie.GetProperty("nomAffiche").GetString());
        Assert.True(relie.GetProperty("actif").GetBoolean());

        // Un seul niveau de liaison, jamais soi-même.
        Assert.Equal((HttpStatusCode)422, (await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idBob}/relier", new { comptePrincipalId = idSso })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idAlice}/relier", new { comptePrincipalId = idSso })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idBob}/relier", new { comptePrincipalId = idBob })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idBob}/relier", new { })).StatusCode);

        // Rapports : la MEP du compte secondaire nomme désormais la personne ; son journal garde l'auteur et le login d'origine.
        var mep = await Ok(await alice.GetAsync($"/api/meps/{idAvant}"));
        Assert.Equal("Bob", mep.GetProperty("participants")[0].GetProperty("utilisateurNom").GetString());
        var journalMep = await Ok(await alice.GetAsync($"/api/meps/{idAvant}/journal"));
        Assert.Contains(journalMep.EnumerateArray(), e => e.GetProperty("auteur").GetString() == "Robert (SSO)" && e.GetProperty("auteurLogin").GetString() == "robert.sso");

        // Le compte secondaire, resté utilisable, signe sous le nom de la personne.
        var apres = await Ok(await bobSso.PostAsJsonAsync("/api/meps", new { outil = "Outil d'après", objet = "" }));
        Assert.Equal("Bob", apres.GetProperty("participants")[0].GetProperty("nom").GetString());

        // Recherche : chaque MEP de l'un des comptes porte les noms et logins des deux.
        var resumes = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().ToList();
        foreach (var id in (await MepsAvec(alice, idBob)).Append(idAvant))
        {
            var personnes = resumes.Single(m => m.GetProperty("id").GetGuid() == id).GetProperty("personnes").EnumerateArray().Select(p => p.GetString()).ToList();
            Assert.Contains("Bob", personnes);
            Assert.Contains("robert.sso", personnes);
            Assert.Contains("Robert (SSO)", personnes);
        }

        // Équipes : le compte principal dans l'équipe suffit pour les MEP du compte relié.
        var equipe = await Ok(await alice.PostAsJsonAsync("/api/admin/equipes", new { nom = "Exploitation", membreIds = new[] { idBob } }));
        Assert.Contains(idAvant, await MepsDeLEquipe(alice, equipe.GetProperty("id").GetGuid()));

        // Approbateurs : la même personne n'approuve pas deux fois.
        var idDemo = await AidesApi.IdDemo(alice);
        var demo = await Ok(await alice.GetAsync($"/api/meps/{idDemo}"));
        var deuxFois = await alice.PutAsJsonAsync($"/api/meps/{idDemo}/approbateurs",
            new { version = demo.GetProperty("version").GetInt32(), utilisateurIds = new[] { idBob, idSso } });
        Assert.Equal(HttpStatusCode.BadRequest, deuxFois.StatusCode);
        var messageDeuxFois = await Message(deuxFois);
        Assert.True(messageDeuxFois.Contains("une personne n'approuve qu'une fois", StringComparison.Ordinal), messageDeuxFois);

        // Défaire la liaison : chacun retrouve son nom, rien n'est perdu.
        var delie = await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idSso}/delier", null));
        Assert.Equal(JsonValueKind.Null, delie.GetProperty("comptePrincipalId").ValueKind);
        Assert.Equal("Robert (SSO)", delie.GetProperty("nomPersonne").GetString());
        Assert.Equal((HttpStatusCode)422, (await alice.PostAsync($"/api/admin/utilisateurs/{idSso}/delier", null)).StatusCode);
        Assert.Equal("Robert (SSO)", (await Ok(await alice.GetAsync($"/api/meps/{idAvant}"))).GetProperty("participants")[0].GetProperty("utilisateurNom").GetString());
        Assert.DoesNotContain(idAvant, await MepsDeLEquipe(alice, equipe.GetProperty("id").GetGuid()));

        // Relier en désactivant le compte secondaire : ses sessions sont coupées ; impossible pour son propre compte.
        var desactive = await Ok(await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idSso}/relier", new { comptePrincipalId = idBob, desactiver = true }));
        Assert.False(desactive.GetProperty("actif").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await bobSso.GetAsync("/api/meps")).StatusCode);
        var soiMeme = await alice.PostAsJsonAsync($"/api/admin/utilisateurs/{idAlice}/relier", new { comptePrincipalId = idBob, desactiver = true });
        Assert.Equal((HttpStatusCode)422, soiMeme.StatusCode);

        var types = await TypesJournal(alice);
        Assert.Contains("compte.relie", types);
        Assert.Contains("compte.delie", types);
    }

    [Fact]
    public async Task Un_compte_desactive_reste_visible_partout_mais_ne_peut_plus_etre_designe_approbateur()
    {
        var alice = await _api.ClientConnecte("alice");
        var idClaire = await IdCompte(alice, "claire");
        var idBob = await IdCompte(alice, "bob");
        var idDemo = await AidesApi.IdDemo(alice);
        var journalAvant = (await Ok(await alice.GetAsync($"/api/meps/{idDemo}/journal"))).GetArrayLength();
        await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idClaire}/desactiver", null));
        await Ok(await alice.PostAsync($"/api/admin/utilisateurs/{idBob}/desactiver", null));

        // Listes de personnes, MEP et journal : toujours là, avec le nom et le login.
        var claire = await Compte(alice, "claire");
        Assert.False(claire.GetProperty("actif").GetBoolean());
        Assert.Equal("Claire", claire.GetProperty("nomPersonne").GetString());
        var mep = await Ok(await alice.GetAsync($"/api/meps/{idDemo}"));
        Assert.Contains(mep.GetProperty("participants").EnumerateArray(), p => p.GetProperty("utilisateurNom").GetString() == "Claire");
        Assert.Contains(mep.GetProperty("approbations").EnumerateArray(), a => a.GetProperty("nom").GetString() == "Claire");
        Assert.Equal(journalAvant, (await Ok(await alice.GetAsync($"/api/meps/{idDemo}/journal"))).GetArrayLength());
        var resume = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == idDemo);
        Assert.Contains("Claire", resume.GetProperty("personnes").EnumerateArray().Select(p => p.GetString()));

        // Claire, déjà approbatrice, reste désignée ; Bob, désactivé, ne peut plus l'être.
        var version = mep.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync($"/api/meps/{idDemo}/approbateurs", new { version, utilisateurIds = new[] { idClaire } })).StatusCode);
        mep = await Ok(await alice.GetAsync($"/api/meps/{idDemo}"));
        var refus = await alice.PutAsJsonAsync($"/api/meps/{idDemo}/approbateurs",
            new { version = mep.GetProperty("version").GetInt32(), utilisateurIds = new[] { idClaire, idBob } });
        Assert.Equal((HttpStatusCode)422, refus.StatusCode);
        Assert.Equal("Le compte Bob (bob) est désactivé : il ne peut pas être désigné approbateur.", await Message(refus));
    }
}
