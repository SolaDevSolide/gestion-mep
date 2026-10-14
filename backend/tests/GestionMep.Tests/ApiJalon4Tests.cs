using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Tests d'intégration du jalon 4 : profils, sauvegarde par système, fiches de recette, graphe par lignes (création, placer).</summary>
[Collection("Api")]
public class ApiJalon4Tests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    // ---------- Aides ----------
    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    private static int Version(JsonElement mep) => mep.GetProperty("version").GetInt32();
    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);
    private static Guid EtapeId(JsonElement mep, string code) => Etape(mep, code).GetProperty("id").GetGuid();
    private static int Revision(JsonElement mep, string code) => Etape(mep, code).GetProperty("revision").GetInt32();
    private static int Ligne(JsonElement mep, string code) => Etape(mep, code).GetProperty("ligne").GetInt32();
    private static string[] Prealables(JsonElement mep, string code) =>
        Etape(mep, code).GetProperty("dependances").EnumerateArray()
            .Select(d => mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == d.GetGuid()).GetProperty("code").GetString()!).ToArray();
    private static Guid Systeme(JsonElement mep, string nom) =>
        mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static List<string> Liste(JsonElement mep, string champ) => mep.GetProperty(champ).EnumerateArray().Select(e => e.GetString()!).ToList();
    private static JsonElement Fiche(JsonElement mep, string titre) =>
        mep.GetProperty("fichesRecette").EnumerateArray().Single(f => f.GetProperty("titre").GetString() == titre);

    private static async Task<List<(string Type, string Auteur, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("auteur").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private async Task<(HttpClient Alice, Guid Id, JsonElement Mep)> Demo()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        return (alice, id, await Ok(await alice.GetAsync($"/api/meps/{id}")));
    }

    private static async Task<JsonElement> Reussir(HttpClient c, Guid id, JsonElement mep, string code)
    {
        mep = await Ok(await c.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, code)}/demarrer", new { revision = Revision(mep, code) }));
        return await Ok(await c.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, code)}/terminer",
            new { revision = Revision(mep, code), resultat = "Reussie", preuve = "OK", resultatVerifie = true }));
    }

    /// <summary>Go, tout le déploiement de la démo réussi, puis passage en recette.</summary>
    private async Task<JsonElement> EnRecette(HttpClient alice, Guid id)
    {
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var mep = await AidesApi.Go(_api, alice, id);
        mep = await Reussir(alice, id, mep, "E0004");
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0005")}/autoriser-reprise", new { revision = Revision(mep, "E0005") }));
        mep = await Reussir(bob, id, mep, "E0005");
        mep = await Reussir(claire, id, mep, "E0006");
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0007")}/autoriser-non-retour", new { revision = Revision(mep, "E0007") }));
        mep = await Reussir(bob, id, mep, "E0007");
        mep = await Reussir(claire, id, mep, "E0008");
        mep = await Reussir(alice, id, mep, "E0009");
        return await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Recette" }));
    }

    // ---------- Comptes et profils ----------
    [Fact]
    public async Task Profil_demo_puis_chacun_gere_le_sien()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var catalogue = (await Ok(await bob.GetAsync("/api/catalogue/systemes"))).EnumerateArray()
            .ToDictionary(s => s.GetProperty("nom").GetString()!, s => s.GetProperty("id").GetGuid());

        var moi = await Ok(await bob.GetAsync("/api/auth/moi"));
        Assert.Equal("Développeur API", moi.GetProperty("role").GetString());
        Assert.StartsWith("Fictif", moi.GetProperty("contact").GetString());
        Assert.Equal([catalogue["API"], catalogue["MDM"]], moi.GetProperty("expertises").EnumerateArray().Select(x => x.GetProperty("systemeCatalogueId").GetGuid()).ToArray());
        Assert.Equal("Service commandes", moi.GetProperty("expertises")[0].GetProperty("detail").GetString());
        var claire = (await Ok(await bob.GetAsync("/api/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == "claire");
        Assert.Equal("Développeuse front", claire.GetProperty("role").GetString());

        var profil = await Ok(await bob.PutAsJsonAsync("/api/utilisateurs/moi/profil", new
        {
            nomAffiche = " Bobby ", role = " Lead API ", contact = " 06 00 ",
            expertises = new[] { new { systemeCatalogueId = catalogue["API"], detail = " Expert de la zone facturation " }, new { systemeCatalogueId = catalogue["BATCH"], detail = "" } },
        }));
        Assert.Equal(("Bobby", "Lead API", "06 00"), (profil.GetProperty("nomAffiche").GetString(), profil.GetProperty("role").GetString(), profil.GetProperty("contact").GetString()));
        Assert.Equal("Expert de la zone facturation", profil.GetProperty("expertises")[0].GetProperty("detail").GetString());
        Assert.Equal(2, (await Ok(await bob.GetAsync("/api/auth/moi"))).GetProperty("expertises").GetArrayLength());

        // Le nom affiché du cookie suit le profil (auteur du journal).
        var mep = await Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Profil" }));
        Assert.Equal("Bobby", (await Journal(bob, mep.GetProperty("id").GetGuid())).Single().Auteur);

        // nomAffiche absent : inchangé ; expertises absentes : vidées.
        profil = await Ok(await bob.PutAsJsonAsync("/api/utilisateurs/moi/profil", new { role = "Dev", contact = "" }));
        Assert.Equal("Bobby", profil.GetProperty("nomAffiche").GetString());
        Assert.Equal(0, profil.GetProperty("expertises").GetArrayLength());

        foreach (var invalide in new object[]
        {
            new { role = "", contact = "", expertises = new[] { new { systemeCatalogueId = Guid.NewGuid(), detail = "" } } },
            new { role = "", contact = "", expertises = new[] { new { systemeCatalogueId = catalogue["API"], detail = "a" }, new { systemeCatalogueId = catalogue["API"], detail = "b" } } },
            new { role = "", contact = "", expertises = new[] { new { systemeCatalogueId = (Guid?)null, detail = "a" } } },
            new { nomAffiche = " ", role = "", contact = "" },
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsJsonAsync("/api/utilisateurs/moi/profil", invalide)).StatusCode);

        // Profil d'un autre : administrateur seulement.
        var idClaire = claire.GetProperty("id").GetGuid();
        var corps = new { role = "Testeuse", contact = "poste C", expertises = new[] { new { systemeCatalogueId = catalogue["WEB"], detail = "Portail" } } };
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/utilisateurs/{idClaire}/profil", corps)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsJsonAsync($"/api/utilisateurs/{Guid.NewGuid()}/profil", corps)).StatusCode);
        var modifiee = await Ok(await alice.PutAsJsonAsync($"/api/utilisateurs/{idClaire}/profil", corps));
        Assert.Equal(("Claire", "Testeuse"), (modifiee.GetProperty("nomAffiche").GetString(), modifiee.GetProperty("role").GetString()));

        // Création d'un compte avec rôle et contact.
        var dave = await Ok(await alice.PostAsJsonAsync("/api/utilisateurs",
            new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false, role = " Exploitant ", contact = "poste D" }));
        Assert.Equal(("Exploitant", "poste D", 0), (dave.GetProperty("role").GetString(), dave.GetProperty("contact").GetString(), dave.GetProperty("expertises").GetArrayLength()));
    }

    // ---------- Sauvegarde par système ----------
    [Fact]
    public async Task Sauvegarde_par_systeme_journalisee_et_controlee()
    {
        var (alice, id, mep) = await Demo();
        var bdd = Systeme(mep, "BDD");
        var sv = mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == bdd).GetProperty("sauvegarde");
        Assert.Equal(("Requise", "PendantMep"), (sv.GetProperty("statut").GetString(), sv.GetProperty("moment").GetString()));
        Assert.Contains("E0001", sv.GetProperty("contenu").GetString());
        await AidesApi.Approuver(_api, id);
        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var url = $"/api/meps/{id}/systemes/{bdd}/sauvegarde";

        mep = await Ok(await alice.PutAsJsonAsync(url, new { version = Version(mep), statut = "NonRequise", moment = "AvantMep", contenu = "", justification = " " }));
        Assert.Contains("Sauvegarde de BDD non requise : justifier.", Liste(mep, "erreursPlan"));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("approbations")[0].GetProperty("decision").ValueKind);
        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "sauvegarde.systeme" && e.Detail == "Sauvegarde BDD : non requise");
        Assert.Contains(journal, e => e.Type == "approbations.reinitialisees");

        mep = await Ok(await alice.PutAsJsonAsync(url, new { version = Version(mep), statut = "ARenseigner" }));
        Assert.Contains("Sauvegarde : indiquer si BDD doit être sauvegardé.", Liste(mep, "erreursPlan"));
        mep = await Ok(await alice.PutAsJsonAsync(url, new { version = Version(mep), statut = "Requise", moment = "AvantMep", contenu = " Dump **complet** " }));
        Assert.DoesNotContain(Liste(mep, "erreursPlan"), e => e.Contains("BDD"));
        Assert.Contains(await Journal(alice, id), e => e.Detail == "Sauvegarde BDD : requise (avant la MEP)");

        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync(url, new { version = Version(mep) - 1, statut = "Requise" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(url, new { version = Version(mep) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{Guid.NewGuid()}/sauvegarde", new { version = Version(mep), statut = "Requise" })).StatusCode);

        mep = await AidesApi.Go(_api, alice, id);
        var tard = await alice.PutAsJsonAsync(url, new { version = Version(mep), statut = "NonRequise", justification = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tard.StatusCode);
        Assert.Contains("préparation", await Message(tard));
    }

    // ---------- Fiches de recette : préparation ----------
    [Fact]
    public async Task Fiches_de_recette_ajout_modification_suppression()
    {
        var (alice, id, mep) = await Demo();
        var web = Systeme(mep, "WEB");
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync($"/api/meps/{id}/tests-recette", new { libelle = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette", new { systemeId = Guid.NewGuid(), titre = "x" })).StatusCode);

        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette", new { systemeId = web, titre = " Vérifier les journaux " }));
        var fiche = Fiche(mep, "Vérifier les journaux");
        var fid = fiche.GetProperty("id").GetGuid();
        Assert.Equal((6, web, 0), (fiche.GetProperty("ordre").GetInt32(), fiche.GetProperty("systemeId").GetGuid(), fiche.GetProperty("etapes").GetArrayLength()));
        Assert.Contains("Recette « Vérifier les journaux » : ajouter au moins une étape de test.", Liste(mep, "erreursPlan"));

        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/fiches-recette/{fid}", new
        {
            systemeId = web, titre = "Vérifier les journaux",
            etapes = new object[] { new { instructions = "```\ntail -f app.log\n```", resultatAttendu = "Aucune erreur" }, new { instructions = "Ouvrir la supervision", resultatAttendu = "" } },
        }));
        fiche = Fiche(mep, "Vérifier les journaux");
        var (t1, t2) = (fiche.GetProperty("etapes")[0].GetProperty("id").GetGuid(), fiche.GetProperty("etapes")[1].GetProperty("id").GetGuid());
        Assert.Contains("Recette « Vérifier les journaux », étape 2 : compléter les instructions et le résultat attendu.", Liste(mep, "erreursPlan"));

        // Une fiche reste dans son système.
        var autre = await alice.PutAsJsonAsync($"/api/meps/{id}/fiches-recette/{fid}", new { systemeId = Systeme(mep, "API"), titre = "x", etapes = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, autre.StatusCode);
        Assert.Equal("Une fiche de recette reste dans son système : créez une nouvelle fiche dans l'autre système.", await Message(autre));

        // Les identifiants connus sont gardés, l'ordre est celui reçu ; sans système précisé, la fiche garde le sien.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/fiches-recette/{fid}", new
        {
            systemeId = (Guid?)null, titre = "Vérifier les journaux",
            etapes = new object[] { new { id = t2, instructions = "Ouvrir la supervision", resultatAttendu = "Tout est vert" }, new { id = t1, instructions = "tail", resultatAttendu = "Aucune erreur" } },
        }));
        fiche = Fiche(mep, "Vérifier les journaux");
        Assert.Equal([t2, t1], fiche.GetProperty("etapes").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal([1, 2], fiche.GetProperty("etapes").EnumerateArray().Select(t => t.GetProperty("ordre").GetInt32()).ToArray());
        Assert.Equal(web, fiche.GetProperty("systemeId").GetGuid());
        Assert.Equal(["Il faut 1 approbation(s)."], Liste(mep, "erreursPlan"));

        // Suppression : les fiches restantes sont renumérotées.
        var premiere = mep.GetProperty("fichesRecette")[0].GetProperty("id").GetGuid();
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/fiches-recette/{premiere}"));
        Assert.Equal([1, 2, 3, 4, 5], mep.GetProperty("fichesRecette").EnumerateArray().Select(f => f.GetProperty("ordre").GetInt32()).ToArray());
        // « Vérifier les journaux » est restée dans WEB.
        Assert.DoesNotContain("Aucune recette pour le système WEB.", Liste(mep, "avertissements"));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/meps/{id}/fiches-recette/{premiere}")).StatusCode);

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "recette.fiche-ajoutee" && e.Detail == "Recette « Vérifier les journaux » ajoutée");
        Assert.Contains(journal, e => e.Type == "recette.fiche-modifiee");
        Assert.Contains(journal, e => e.Type == "recette.fiche-supprimee" && e.Detail == "Recette « Parcours de commande » supprimée");

        mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette", new { titre = "Trop tard" })).StatusCode);
    }

    [Fact]
    public async Task Supprimer_un_systeme_sans_etape_detache_ses_fiches_de_recette()
    {
        var (alice, id, mep) = await Demo();
        var batch = (await Ok(await alice.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "BATCH").GetProperty("id").GetGuid();
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/systemes/depuis-catalogue", new { catalogueId = batch, impact = "" }));
        var sid = Systeme(mep, "BATCH");
        Assert.Equal("ARenseigner", mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == sid).GetProperty("sauvegarde").GetProperty("statut").GetString());
        Assert.Contains("Sauvegarde : indiquer si BATCH doit être sauvegardé.", Liste(mep, "erreursPlan"));
        Assert.Contains("Aucune recette pour le système BATCH.", Liste(mep, "avertissements"));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette", new { systemeId = sid, titre = "Traitements" }));

        var utilise = await alice.DeleteAsync($"/api/meps/{id}/systemes/{Systeme(mep, "LDAP")}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, utilise.StatusCode);
        Assert.Contains("E0005", await Message(utilise));
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/systemes/{sid}"));
        Assert.Equal(JsonValueKind.Null, Fiche(mep, "Traitements").GetProperty("systemeId").ValueKind);
        Assert.Contains("Recette « Traitements » : sans système, à rattacher à un système de la MEP.", Liste(mep, "avertissements"));
        // Elle reste modifiable et se rattache à un autre système.
        var fid = Fiche(mep, "Traitements").GetProperty("id").GetGuid();
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/fiches-recette/{fid}", new { systemeId = Systeme(mep, "API"), titre = "Traitements", etapes = Array.Empty<object>() }));
        Assert.Equal(Systeme(mep, "API"), Fiche(mep, "Traitements").GetProperty("systemeId").GetGuid());
    }

    // ---------- Fiches de recette : déroulé ----------
    [Fact]
    public async Task Recette_deroulee_par_les_participants_puis_cloture()
    {
        var (alice, id, _) = await Demo();
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/utilisateurs",
            new { login = "dave", nomAffiche = "Dave", motDePasse = "motdepasse", estAdmin = false })).StatusCode);
        var dave = await _api.ClientConnecte("dave", "motdepasse");

        var mep = await EnRecette(alice, id);
        Assert.Equal("Recette", mep.GetProperty("phase").GetString());
        var fiche = Fiche(mep, "Parcours de commande");
        var fid = fiche.GetProperty("id").GetGuid();
        var (t1, t2) = (fiche.GetProperty("etapes")[0].GetProperty("id").GetGuid(), fiche.GetProperty("etapes")[1].GetProperty("id").GetGuid());
        string Url(Guid t, string action = "resultat") => $"/api/meps/{id}/fiches-recette/{fid}/etapes/{t}/{action}";

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await dave.PostAsJsonAsync(Url(t1), new { resultat = "Conforme", observation = "OK" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync(Url(t1), new { resultat = "Conforme", observation = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await claire.PostAsJsonAsync(Url(t1), new { observation = "OK" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await claire.PostAsJsonAsync(Url(Guid.NewGuid()), new { resultat = "Conforme", observation = "OK" })).StatusCode);
        mep = await Ok(await claire.PostAsJsonAsync(Url(t1), new { resultat = "Conforme", observation = "Page affichée" }));
        var etape = Fiche(mep, "Parcours de commande").GetProperty("etapes")[0];
        Assert.Equal(("Conforme", "Page affichée", "Claire"), (etape.GetProperty("resultat").GetString(), etape.GetProperty("observation").GetString(), etape.GetProperty("par").GetString()));
        var deja = await bob.PostAsJsonAsync(Url(t1), new { resultat = "Echec", observation = "Erreur" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deja.StatusCode);
        Assert.Equal("Recette « Parcours de commande », étape 1 déjà validée : le pilote peut la faire refaire.", await Message(deja));
        mep = await Ok(await bob.PostAsJsonAsync(Url(t2), new { resultat = "Echec", observation = "Erreur 500" }));

        var refus = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "Rien à signaler" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Equal("Recette « Parcours de commande », étape 2 non conforme : clôture impossible.", await Message(refus));

        // Refaire : une étape puis toute la fiche (pilote ou suppléant).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsync(Url(t2, "refaire"), null)).StatusCode);
        mep = await Ok(await alice.PostAsync(Url(t2, "refaire"), null));
        Assert.Equal(JsonValueKind.Null, Fiche(mep, "Parcours de commande").GetProperty("etapes")[1].GetProperty("resultat").ValueKind);
        mep = await Ok(await bob.PostAsync($"/api/meps/{id}/fiches-recette/{fid}/refaire", null));
        Assert.All(Fiche(mep, "Parcours de commande").GetProperty("etapes").EnumerateArray(), t => Assert.Equal(JsonValueKind.Null, t.GetProperty("resultat").ValueKind));

        foreach (var f in mep.GetProperty("fichesRecette").EnumerateArray().ToList())
            foreach (var t in f.GetProperty("etapes").EnumerateArray().ToList())
                mep = await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/fiches-recette/{f.GetProperty("id").GetGuid()}/etapes/{t.GetProperty("id").GetGuid()}/resultat",
                    new { resultat = "Conforme", observation = "OK" }));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "Rien à signaler" }));
        Assert.Equal("Reussie", mep.GetProperty("issue").GetString());

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "recette.test" && e.Auteur == "Claire" && e.Detail == "Recette « Parcours de commande » étape 1 conforme : Page affichée");
        Assert.Contains(journal, e => e.Type == "recette.test" && e.Detail == "Recette « Parcours de commande » étape 2 en échec : Erreur 500");
        Assert.Contains(journal, e => e.Type == "recette.refaire" && e.Detail == "Recette « Parcours de commande » étape 2 à refaire");
        Assert.Contains(journal, e => e.Type == "recette.refaire" && e.Auteur == "Bob" && e.Detail == "Recette « Parcours de commande » à refaire");
    }

    // ---------- Étapes : création et placement ----------
    [Fact]
    public async Task Creation_d_une_etape_dans_une_ligne_avec_liens_par_defaut()
    {
        var (alice, id, mep) = await Demo();
        var api = Systeme(mep, "API");
        var revisionE7 = Revision(mep, "E0007");
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new
        {
            ligne = 1, position = 0, action = "Prévenir le support", systemeIds = new[] { api, api, Systeme(mep, "WEB") }, dureeEstimeeMinutes = 5,
            responsableId = mep.GetProperty("piloteId"), instructions = "Appeler le support", resultatAttendu = "Support prévenu",
        }));
        var e10 = Etape(mep, "E0010");
        Assert.Equal((1, 0, 5), (e10.GetProperty("ligne").GetInt32(), e10.GetProperty("ordre").GetInt32(), e10.GetProperty("dureeEstimeeMinutes").GetInt32()));
        Assert.Equal([api, Systeme(mep, "WEB")], e10.GetProperty("systemeIds").EnumerateArray().Select(s => s.GetGuid()).ToArray());
        Assert.Equal(["E0001", "E0002", "E0003"], Prealables(mep, "E0010"));
        Assert.Equal(["E0004", "E0005", "E0006", "E0010"], Prealables(mep, "E0007"));
        Assert.Equal(revisionE7 + 1, Revision(mep, "E0007"));
        Assert.Equal(1, Etape(mep, "E0004").GetProperty("ordre").GetInt32());
        // GET : étapes triées par plan, ligne puis ordre.
        var lu = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(["E0001", "E0002", "E0003", "E0010", "E0004", "E0005", "E0006", "E0007", "E0008", "E0009", "R0001", "R0002", "R0003"],
            lu.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToArray());

        // Dépendances explicites : pas de liens par défaut.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { ligne = 2, dependances = new[] { EtapeId(mep, "E0001") } }));
        Assert.Equal(["E0001"], Prealables(mep, "E0011"));
        Assert.Equal(["E0007", "E0011"], new[] { "E0007", "E0011" }.Where(c => Ligne(mep, c) == 2).ToArray());
        Assert.DoesNotContain(EtapeId(mep, "E0011"), Etape(mep, "E0008").GetProperty("dependances").EnumerateArray().Select(d => d.GetGuid()));
        var montant = await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { ligne = 0, dependances = new[] { EtapeId(mep, "E0001") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, montant.StatusCode);
        Assert.Equal("Un lien descend toujours : E0001 doit être sur une ligne au-dessus de E0012.", await Message(montant));

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { ligne = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { position = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { systemeIds = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { dureeEstimeeMinutes = -5 })).StatusCode);
    }

    [Fact]
    public async Task Placer_une_etape_deplace_la_fiche_et_recree_les_liens()
    {
        var (alice, id, mep) = await Demo();
        await AidesApi.Approuver(_api, id);
        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var url = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0007")}/placer";

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(url, new { position = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(url, new { ligne = 6, position = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(url, new { ligne = 0, position = -1 })).StatusCode);

        // E0007 rejoint la première ligne : sa ligne disparaît, E0008 attend la ligne au-dessus d'elle.
        mep = await Ok(await alice.PostAsJsonAsync(url, new { ligne = 0, position = 99 }));
        Assert.Equal((0, 3), (Ligne(mep, "E0007"), Etape(mep, "E0007").GetProperty("ordre").GetInt32()));
        Assert.Empty(Prealables(mep, "E0007"));
        Assert.Equal(["E0004", "E0005", "E0006"], Prealables(mep, "E0008"));
        Assert.Equal(2, Ligne(mep, "E0008"));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("approbations")[0].GetProperty("decision").ValueKind);
        Assert.Contains(await Journal(alice, id), e => e.Type == "etape.deplacee" && e.Detail == "E0007 placée ligne 1");

        // Même ligne : simple réordonnancement, liens inchangés.
        var avant = Prealables(mep, "E0004");
        mep = await Ok(await alice.PostAsJsonAsync(url, new { ligne = 0, position = 0 }));
        Assert.Equal(0, Etape(mep, "E0007").GetProperty("ordre").GetInt32());
        Assert.Equal(avant, Prealables(mep, "E0004"));

        // Sans liens par défaut : seuls les liens devenus montants sont retirés.
        var e4 = $"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}/placer";
        mep = await Ok(await alice.PostAsJsonAsync(e4, new { ligne = 4, position = 0, liensParDefaut = false }));
        Assert.Equal(4, Ligne(mep, "E0004"));
        Assert.Equal(["E0001", "E0002", "E0003", "E0007"], Prealables(mep, "E0004"));
        Assert.Equal(["E0005", "E0006"], Prealables(mep, "E0008"));

        mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync(url, new { ligne = 1, position = 0 })).StatusCode);
    }
}
