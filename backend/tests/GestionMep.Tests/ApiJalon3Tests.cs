using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Tests d'intégration du jalon 3 : approbation, go / no-go, point de non-retour, recette, bilan, modèles, pièces jointes, paramètres.</summary>
[Collection("Api")]
public class ApiJalon3Tests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    // ---------- Aides ----------
    private static async Task<JsonElement> Lire(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) => (await Lire(rep)).GetProperty("message").GetString()!;
    private static int Version(JsonElement mep) => mep.GetProperty("version").GetInt32();

    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    private static int Revision(JsonElement mep, string code) => Etape(mep, code).GetProperty("revision").GetInt32();
    private static Guid EtapeId(JsonElement mep, string code) => Etape(mep, code).GetProperty("id").GetGuid();
    private static Guid Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static List<string> Erreurs(JsonElement mep) => mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToList();

    private static Task<HttpResponseMessage> Action(HttpClient c, Guid mepId, JsonElement mep, string code, string action, object? corps = null) =>
        c.PostAsJsonAsync($"/api/meps/{mepId}/etapes/{EtapeId(mep, code)}/{action}", corps ?? new { revision = Revision(mep, code) });

    private static async Task<JsonElement> Reussir(HttpClient c, Guid id, JsonElement mep, string code, string preuve)
    {
        mep = await Ok(await Action(c, id, mep, code, "demarrer"));
        return await Ok(await Action(c, id, mep, code, "terminer",
            new { revision = Revision(mep, code), resultat = "Reussie", preuve, resultatVerifie = true }));
    }

    private static async Task<List<(string Type, string Auteur, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("auteur").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    /// <summary>Corps de <c>PUT /meps/{id}</c> reprenant l'état actuel, avec les suppléants donnés.</summary>
    private static object Identite(JsonElement mep, IEnumerable<Guid> suppleants) => new
    {
        version = Version(mep), outil = mep.GetProperty("outil").GetString(), versionActuelle = mep.GetProperty("versionActuelle").GetString(),
        versionCible = mep.GetProperty("versionCible").GetString(), objet = mep.GetProperty("objet").GetString(), impacts = mep.GetProperty("impacts").GetString(),
        dateMep = mep.GetProperty("dateMep"), heureDebut = mep.GetProperty("heureDebut"), heureFin = mep.GetProperty("heureFin"),
        fuseauHoraire = mep.GetProperty("fuseauHoraire").GetString(), indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"),
        piloteId = mep.GetProperty("piloteId").GetGuid(), suppleantIds = suppleants.ToArray(), echeanceRepli = mep.GetProperty("echeanceRepli"),
    };

    private static MultipartFormDataContent Fichier(string nom, byte[] contenu, string type = "text/plain")
    {
        var corps = new MultipartFormDataContent();
        var fichier = new ByteArrayContent(contenu);
        fichier.Headers.ContentType = new MediaTypeHeaderValue(type);
        corps.Add(fichier, "fichier", nom);
        return corps;
    }

    // ---------- Démo ----------
    [Fact]
    public async Task Demo_du_jalon_3_modele_criteres_approbatrice_et_parametres()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var modeles = (await Ok(await alice.GetAsync("/api/meps?modele=true"))).EnumerateArray().ToList();
        Assert.Equal("Modèle : déploiement API standard", Assert.Single(modeles).GetProperty("outil").GetString());
        Assert.Equal(2, (await Ok(await alice.GetAsync("/api/meps"))).GetArrayLength());
        Assert.Equal(["Équipe présente et joignable", "Sauvegarde disponible", "Utilisateurs prévenus de l'interruption", "Supervision en place"],
            (await Ok(await claire.GetAsync("/api/criteres-go-no-go"))).EnumerateArray().Select(c => c.GetProperty("libelle").GetString()!).ToArray());
        Assert.Equal(1, (await Ok(await claire.GetAsync("/api/parametres"))).GetProperty("approbationsMinimum").GetInt32());

        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await claire.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Production", mep.GetProperty("environnement").GetString());
        Assert.False(mep.GetProperty("estModele").GetBoolean());
        Assert.Equal([Participant(mep, "Bob")], mep.GetProperty("suppleantIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());
        var approbation = Assert.Single(mep.GetProperty("approbations").EnumerateArray().ToList());
        Assert.Equal("Claire", approbation.GetProperty("nom").GetString());
        Assert.Equal(JsonValueKind.Null, approbation.GetProperty("decision").ValueKind);
        Assert.True(mep.GetProperty("droits").GetProperty("estApprobateur").GetBoolean());
        Assert.False(mep.GetProperty("droits").GetProperty("estPilote").GetBoolean());
        Assert.Equal(1, mep.GetProperty("approbationsMinimum").GetInt32());
        Assert.Equal("PendantMep", mep.GetProperty("sauvegarde").GetProperty("mode").GetString());
        Assert.Equal("Sauvegarde", Etape(mep, "E0001").GetProperty("type").GetString());
        Assert.True(Etape(mep, "E0004").GetProperty("pointDeNonRetour").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Etape(mep, "E0004").GetProperty("nonRetourAutorise").ValueKind);
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("nonRetourFranchi").ValueKind);
        var tests = mep.GetProperty("testsRecette").EnumerateArray().ToList();
        Assert.Equal(["Ouvrir la page d'accueil fictive", "Appeler l'API de test"], tests.Select(t => t.GetProperty("libelle").GetString()!).ToArray());
        Assert.Equal(Participant(mep, "Claire"), tests[0].GetProperty("responsableId").GetGuid());
        Assert.True(mep.GetProperty("bilanActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("bilan").ValueKind);
        Assert.Equal(0, mep.GetProperty("goNoGo").GetArrayLength());
        Assert.Equal(0, mep.GetProperty("piecesJointes").GetArrayLength());
    }

    // ---------- Suppléants ----------
    [Fact]
    public async Task Suppleants_multiples_sans_le_pilote_ni_doublon()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var (pAlice, pBob, pClaire) = (Participant(mep, "Alice"), Participant(mep, "Bob"), Participant(mep, "Claire"));

        var pilote = await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, [pBob, pAlice]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pilote.StatusCode);
        Assert.Equal("Alice est le pilote : il ne peut pas être aussi suppléant.", await Message(pilote));
        var doublon = await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, [pBob, pBob]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, doublon.StatusCode);
        Assert.Contains("deux fois", await Message(doublon));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, [Guid.NewGuid()]))).StatusCode);

        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, [pBob, pClaire])));
        Assert.Equal(2, mep.GetProperty("suppleantIds").GetArrayLength());
        Assert.True((await Ok(await claire.GetAsync($"/api/meps/{id}"))).GetProperty("droits").GetProperty("estPilote").GetBoolean());
        Assert.Contains(await Journal(alice, id), e => e.Detail == "MEP modifiée : suppléants");
        // Un suppléant ne peut pas être supprimé des participants.
        var suppression = await alice.DeleteAsync($"/api/meps/{id}/participants/{pClaire}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, suppression.StatusCode);
        Assert.Contains("suppléant", await Message(suppression));
    }

    // ---------- Approbation ----------
    [Fact]
    public async Task Approbation_refus_puis_modification_remet_a_zero_puis_go()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await claire.GetAsync($"/api/meps/{id}"));

        var nonDesigne = await bob.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = Version(mep), decision = "Approuvee", commentaire = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nonDesigne.StatusCode);
        Assert.Contains("approbateur désigné", await Message(nonDesigne));
        Assert.Equal(HttpStatusCode.Conflict,
            (await claire.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = Version(mep) - 1, decision = "Approuvee", commentaire = "" })).StatusCode);
        var sansMotif = await claire.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = Version(mep), decision = "Refusee", commentaire = " " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansMotif.StatusCode);
        Assert.Equal("Expliquer le refus dans le commentaire.", await Message(sansMotif));

        mep = await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = Version(mep), decision = "Refusee", commentaire = "Préciser le repli" }));
        Assert.Contains("Claire a refusé : Préciser le repli", Erreurs(mep));
        Assert.Equal("Refusee", mep.GetProperty("approbations")[0].GetProperty("decision").GetString());

        // Modifier le plan de repli efface la décision.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new
        {
            version = Version(mep), quandDeclencher = "Si l'API ne répond plus", dureeTesteeMinutes = 30, versionARestaurer = "1.0",
            donneesDepuisSauvegarde = "Sauvegarde E0001", siRepliEchoue = "Astreinte",
        }));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("approbations")[0].GetProperty("decision").ValueKind);
        Assert.Equal(["Il faut 1 approbation(s)."], Erreurs(mep));
        var journal = await Journal(alice, id);
        Assert.Equal(("approbations.reinitialisees", "Alice", "Approbations à refaire : la MEP a été modifiée"), journal[0]);
        Assert.Contains(journal, e => e.Type == "approbation.refusee" && e.Detail == "MEP refusée par Claire : Préciser le repli");

        // Sans décision à effacer, une modification n'écrit rien de plus.
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/livrables", new { objet = "Notes de version", version = "2.0", emplacement = "", systemeId = (Guid?)null }));
        Assert.Equal("livrable.ajoute", (await Journal(alice, id))[0].Type);

        mep = await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/approbation", new { version = Version(mep), decision = "Approuvee", commentaire = "OK" }));
        Assert.Empty(Erreurs(mep));
        Assert.Contains(await Journal(alice, id), e => e.Type == "approbation.approuvee" && e.Detail == "MEP approuvée par Claire : OK");

        // Changer les approbateurs : le pilote est refusé, Bob est ajouté et la décision de Claire est effacée.
        var utilisateurs = (await Ok(await alice.GetAsync("/api/utilisateurs"))).EnumerateArray()
            .ToDictionary(u => u.GetProperty("login").GetString()!, u => u.GetProperty("id").GetGuid());
        var refus = await alice.PutAsJsonAsync($"/api/meps/{id}/approbateurs", new { version = Version(mep), utilisateurIds = new[] { utilisateurs["alice"] } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("Alice est le pilote", await Message(refus));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PutAsJsonAsync($"/api/meps/{id}/approbateurs", new { version = Version(mep), utilisateurIds = new[] { Guid.NewGuid() } })).StatusCode);
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/approbateurs",
            new { version = Version(mep), utilisateurIds = new[] { utilisateurs["claire"], utilisateurs["bob"] } }));
        Assert.Equal(["Claire", "Bob"], mep.GetProperty("approbations").EnumerateArray().Select(a => a.GetProperty("nom").GetString()!).ToArray());
        Assert.All(mep.GetProperty("approbations").EnumerateArray(), a => Assert.Equal(JsonValueKind.Null, a.GetProperty("decision").ValueKind));
        Assert.Contains(await Journal(alice, id), e => e.Detail == "Approbateurs : Claire, Bob");

        mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
    }

    // ---------- Go / no-go et paramètres ----------
    [Fact]
    public async Task Go_exige_les_criteres_et_no_go_journalise_le_motif()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        await AidesApi.Approuver(_api, id);
        var criteres = await AidesApi.CriteresActifs(alice);

        var incomplet = await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = criteres.Take(3) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplet.StatusCode);
        Assert.Equal("Go impossible : cocher « Supervision en place ».", await Message(incomplet));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution" })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync($"/api/meps/{id}/no-go", new { motif = "Pas prêt" })).StatusCode);
        var sansMotif = await alice.PostAsJsonAsync($"/api/meps/{id}/no-go", new { motif = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansMotif.StatusCode);
        Assert.Equal("Indiquer le motif du no-go.", await Message(sansMotif));
        var nogo = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/no-go", new { motif = "Supervision absente" }));
        Assert.Equal("Preparation", nogo.GetProperty("phase").GetString());
        Assert.Equal(("gonogo.nogo", "Alice", "No-go : Supervision absente"), (await Journal(alice, id))[0]);

        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = criteres }));
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        var go = mep.GetProperty("goNoGo").EnumerateArray().ToList();
        Assert.Equal(4, go.Count);
        Assert.All(go, c => Assert.Equal("Alice", c.GetProperty("par").GetString()));
        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "gonogo.go" && e.Detail.StartsWith("Go (Équipe présente et joignable"));
        Assert.Contains(journal, e => e.Type == "phase.execution");
    }

    [Fact]
    public async Task Parametres_et_criteres_reserves_aux_admins()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync("/api/parametres", new { approbationsMinimum = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync("/api/parametres", new { approbationsMinimum = -1 })).StatusCode);
        Assert.Equal(0, (await Ok(await alice.PutAsJsonAsync("/api/parametres", new { approbationsMinimum = 0 }))).GetProperty("approbationsMinimum").GetInt32());

        var corps = new { libelle = "Astreinte prévenue", ordre = (int?)null, actif = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/criteres-go-no-go", corps)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/criteres-go-no-go", corps with { libelle = " " })).StatusCode);
        var cree = await alice.PostAsJsonAsync("/api/criteres-go-no-go", corps);
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var critere = await Lire(cree);
        Assert.Equal(5, critere.GetProperty("ordre").GetInt32());
        var cid = critere.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/criteres-go-no-go/{cid}", corps)).StatusCode);
        var inactif = await Ok(await alice.PutAsJsonAsync($"/api/criteres-go-no-go/{cid}", corps with { actif = false }));
        Assert.False(inactif.GetProperty("actif").GetBoolean());

        // Sans approbation exigée et avec un critère inactif non coché : Go accepté.
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = await AidesApi.CriteresActifs(alice) }));
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        Assert.DoesNotContain(mep.GetProperty("goNoGo").EnumerateArray(), c => c.GetProperty("libelle").GetString() == "Astreinte prévenue");

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/criteres-go-no-go/{cid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/criteres-go-no-go/{cid}")).StatusCode);
        Assert.Equal(4, (await Ok(await bob.GetAsync("/api/criteres-go-no-go"))).GetArrayLength());
    }

    // ---------- Point de non-retour ----------
    [Fact]
    public async Task Point_de_non_retour_autorise_par_le_pilote_puis_retour_arriere_refuse()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await AidesApi.Go(_api, alice, id);
        mep = await Reussir(bob, id, mep, "E0002", "API démo OK");
        mep = await Reussir(claire, id, mep, "E0003", "WEB démo OK");

        var sansAccord = await Action(claire, id, mep, "E0004", "demarrer");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansAccord.StatusCode);
        Assert.Equal("Point de non-retour : l'accord du pilote est requis.", await Message(sansAccord));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Action(claire, id, mep, "E0004", "autoriser-non-retour")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(alice, id, mep, "E0004", "autoriser-non-retour", new { revision = Revision(mep, "E0004") + 1 })).StatusCode);

        mep = await Ok(await Action(alice, id, mep, "E0004", "autoriser-non-retour"));
        Assert.Equal("Alice", Etape(mep, "E0004").GetProperty("nonRetourAutorise").GetProperty("par").GetString());
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("nonRetourFranchi").ValueKind);
        mep = await Ok(await Action(claire, id, mep, "E0004", "demarrer"));
        Assert.Equal("E0004", mep.GetProperty("nonRetourFranchi").GetString());

        var repli = await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Incident" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repli.StatusCode);
        Assert.Equal("Point de non-retour franchi (E0004) : corriger en avant.", await Message(repli));
        Assert.Contains(await Journal(alice, id), e => e.Type == "etape.non-retour-autorise" && e.Detail == "Passage du point de non-retour E0004 autorisé");
    }

    // ---------- Recette et bilan ----------
    [Fact]
    public async Task Recette_tests_valides_puis_cloture_avec_bilan()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);

        // Édition des tests en préparation (remet l'approbation à zéro, comme tout contenu).
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/tests-recette", new { libelle = "Vérifier les journaux", resultatAttendu = "Aucune erreur", responsableId = Participant(mep, "Alice") }));
        var t3 = mep.GetProperty("testsRecette")[2];
        Assert.Equal(3, t3.GetProperty("ordre").GetInt32());
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/tests-recette/{t3.GetProperty("id").GetGuid()}/deplacer", new { sens = "haut" }));
        Assert.Equal("Vérifier les journaux", mep.GetProperty("testsRecette")[1].GetProperty("libelle").GetString());
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/tests-recette/{t3.GetProperty("id").GetGuid()}", new { libelle = "Vérifier les journaux", resultatAttendu = "", responsableId = Participant(mep, "Alice") }));
        Assert.Contains("Test de recette 2 : compléter Résultat attendu.", Erreurs(mep));
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/tests-recette/{t3.GetProperty("id").GetGuid()}"));
        Assert.Equal([1, 2], mep.GetProperty("testsRecette").EnumerateArray().Select(t => t.GetProperty("ordre").GetInt32()).ToArray());
        var tests = mep.GetProperty("testsRecette").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();

        mep = await AidesApi.Go(_api, alice, id);
        mep = await Reussir(bob, id, mep, "E0002", "API démo OK");
        mep = await Reussir(claire, id, mep, "E0003", "WEB démo OK");
        mep = await Ok(await Action(alice, id, mep, "E0004", "autoriser-non-retour"));
        mep = await Reussir(claire, id, mep, "E0004", "Parcours démo OK");
        // Recette impossible tant que E0005 n'est pas réussie.
        var tot = await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Recette" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tot.StatusCode);
        Assert.Equal("E0005 n'est pas réussie : recette impossible.", await Message(tot));
        mep = await Ok(await Action(alice, id, mep, "E0005", "autoriser-reprise"));
        mep = await Reussir(bob, id, mep, "E0005", "Incident corrigé");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Recette" })).StatusCode);
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Recette" }));
        Assert.Equal("Recette", mep.GetProperty("phase").GetString());
        Assert.Equal("Terminée", Etape(mep, "E0002").GetProperty("disponibilite").GetProperty("libelle").GetString());

        // Claire ne valide pas le test de Bob ; observation obligatoire.
        var autre = await claire.PostAsJsonAsync($"/api/meps/{id}/tests-recette/{tests[1]}/resultat", new { resultat = "Conforme", observation = "OK" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, autre.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await claire.PostAsJsonAsync($"/api/meps/{id}/tests-recette/{tests[0]}/resultat", new { resultat = "Conforme", observation = "" })).StatusCode);
        mep = await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/tests-recette/{tests[0]}/resultat", new { resultat = "Conforme", observation = "Page affichée" }));
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/tests-recette/{tests[1]}/resultat", new { resultat = "Echec", observation = "Erreur 500" }));
        Assert.Equal("Echec", mep.GetProperty("testsRecette")[1].GetProperty("resultat").GetString());
        Assert.Equal("Bob", mep.GetProperty("testsRecette")[1].GetProperty("par").GetString());

        var refus = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Déploiement", incidents = "API" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Contains("Test de recette 2", await Message(refus));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsync($"/api/meps/{id}/tests-recette/{tests[1]}/refaire", null)).StatusCode);
        mep = await Ok(await alice.PostAsync($"/api/meps/{id}/tests-recette/{tests[1]}/refaire", null));
        Assert.Equal(JsonValueKind.Null, mep.GetProperty("testsRecette")[1].GetProperty("resultat").ValueKind);
        mep = await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/tests-recette/{tests[1]}/resultat", new { resultat = "Conforme", observation = "API OK" }));

        var sansBilan = await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansBilan.StatusCode);
        Assert.Equal("Bilan : décrire ce qui a marché et les incidents (« Rien à signaler » accepté).", await Message(sansBilan));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "Erreur 500 sur l'API, corrigée", actions = "Ajouter une sonde" }));
        Assert.Equal("Cloturee", mep.GetProperty("phase").GetString());
        Assert.Equal("Reussie", mep.GetProperty("issue").GetString());
        Assert.Equal("Ajouter une sonde", mep.GetProperty("bilan").GetProperty("actions").GetString());

        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "phase.recette" && e.Auteur == "Bob" && e.Detail == "MEP passée en recette");
        Assert.Contains(journal, e => e.Type == "recette.test" && e.Detail == "Test de recette 1 conforme : Page affichée");
        Assert.Contains(journal, e => e.Type == "recette.test" && e.Detail == "Test de recette 2 en échec : Erreur 500");
        Assert.Contains(journal, e => e.Type == "recette.refaire" && e.Detail == "Test de recette 2 à refaire");
        Assert.Contains(journal, e => e.Type == "recette.test-ajoute");
    }

    // ---------- Duplication ----------
    [Fact]
    public async Task Duplication_d_un_modele_et_d_une_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        var modeleId = (await Ok(await alice.GetAsync("/api/meps?modele=true"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        var modele = await Ok(await alice.GetAsync($"/api/meps/{modeleId}"));
        Assert.True(modele.GetProperty("estModele").GetBoolean());

        // Un modèle ne passe pas en exécution.
        var go = await alice.PostAsJsonAsync($"/api/meps/{modeleId}/phase", new { phase = "Execution", criteresCoches = await AidesApi.CriteresActifs(alice) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, go.StatusCode);
        Assert.StartsWith("Un modèle ne passe pas en exécution", await Message(go));

        var cree = await bob.PostAsJsonAsync($"/api/meps/{modeleId}/dupliquer", new { environnement = "Préproduction", commeModele = false });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var copie = await Lire(cree);
        var copieId = copie.GetProperty("id").GetGuid();
        Assert.Equal($"/api/meps/{copieId}", cree.Headers.Location!.ToString());
        Assert.False(copie.GetProperty("estModele").GetBoolean());
        Assert.Equal("Préproduction", copie.GetProperty("environnement").GetString());
        Assert.Equal("Preparation", copie.GetProperty("phase").GetString());
        Assert.Equal(1, copie.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, copie.GetProperty("echeanceRepli").ValueKind);
        Assert.Equal(modele.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()),
            copie.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()));
        Assert.All(copie.GetProperty("etapes").EnumerateArray(), e => Assert.Equal("AFaire", e.GetProperty("etat").GetString()));
        Assert.Equal([EtapeId(copie, "E0002"), EtapeId(copie, "E0003")], Etape(copie, "E0004").GetProperty("dependances").EnumerateArray().Select(d => d.GetGuid()).ToArray());
        Assert.DoesNotContain(copie.GetProperty("participants").EnumerateArray(), p => modele.GetProperty("participants").EnumerateArray().Any(m => m.GetProperty("id").GetGuid() == p.GetProperty("id").GetGuid()));
        Assert.Equal(Participant(copie, "Alice"), copie.GetProperty("piloteId").GetGuid());
        Assert.Contains("Renseigner une échéance de retour arrière future.", Erreurs(copie));
        Assert.Equal(("mep.creee", "Bob", "Créée par duplication du modèle « Modèle : déploiement API standard »"), (await Journal(bob, copieId)).Single());

        // Dupliquer une MEP comme modèle, sans changer l'environnement.
        var nouveauModele = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/dupliquer", new { commeModele = true }));
        Assert.True(nouveauModele.GetProperty("estModele").GetBoolean());
        Assert.Equal("Production", nouveauModele.GetProperty("environnement").GetString());
        Assert.Equal(2, (await Ok(await alice.GetAsync("/api/meps?modele=true"))).GetArrayLength());
        Assert.Equal(2, (await Ok(await alice.GetAsync("/api/meps?modele=false"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync($"/api/meps/{Guid.NewGuid()}/dupliquer", new { commeModele = false })).StatusCode);
    }

    // ---------- Sauvegarde et type d'étape ----------
    [Fact]
    public async Task Mode_de_sauvegarde_et_type_d_etape()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var e1 = Etape(mep, "E0001");
        object Saisie(JsonElement m, string code, string type, bool pnr, Guid[] deps) => new
        {
            revision = Revision(m, code), action = Etape(m, code).GetProperty("action").GetString(), systemeId = Etape(m, code).GetProperty("systemeId"),
            responsableId = Etape(m, code).GetProperty("responsableId"), criticite = "Elevee", instructions = "i", resultatAttendu = "r",
            dependances = deps, type, pointDeNonRetour = pnr,
        };

        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{e1.GetProperty("id").GetGuid()}", Saisie(mep, "E0001", "Standard", false, [])));
        Assert.Contains("Sauvegarde pendant la MEP : indiquer l'étape de déploiement de type Sauvegarde.", Erreurs(mep));
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/sauvegarde", new
        {
            version = Version(mep), mode = "AvantMep", structure = true, donnees = true, configuration = false, responsableId = (Guid?)null,
            realiseeLe = (string?)null, procedure = "", copies = "", restaurationTestee = false, dateTestRestauration = (string?)null, preuveTestRestauration = "",
        }));
        Assert.Equal("AvantMep", mep.GetProperty("sauvegarde").GetProperty("mode").GetString());
        Assert.DoesNotContain(Erreurs(mep), e => e.StartsWith("Sauvegarde pendant"));

        var repli = await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "R0001")}", Saisie(mep, "R0001", "Sauvegarde", false, []));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repli.StatusCode);
        Assert.Contains("retour arrière", await Message(repli));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "R0001")}", Saisie(mep, "R0001", "Standard", true, []))).StatusCode);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes", new { action = "Sauvegarde finale", type = "Sauvegarde", pointDeNonRetour = true }));
        Assert.Equal("Sauvegarde", Etape(mep, "E0006").GetProperty("type").GetString());
        Assert.True(Etape(mep, "E0006").GetProperty("pointDeNonRetour").GetBoolean());
    }

    // ---------- Pièces jointes ----------
    [Fact]
    public async Task Pieces_jointes_depot_telechargement_suppression()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var contenu = Encoding.UTF8.GetBytes("Journal de déploiement fictif");

        // Nom de fichier avec un chemin : seul le nom est gardé, le fichier est stocké sous l'Id.
        mep = await Ok(await bob.PostAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0002")}/pieces-jointes", Fichier("../../etc/journal:api.txt", contenu)));
        var piece = Assert.Single(mep.GetProperty("piecesJointes").EnumerateArray().ToList());
        var pid = piece.GetProperty("id").GetGuid();
        Assert.Equal("journalapi.txt", piece.GetProperty("nomFichier").GetString());
        Assert.Equal(EtapeId(mep, "E0002"), piece.GetProperty("etapeId").GetGuid());
        Assert.Equal(contenu.Length, piece.GetProperty("taille").GetInt64());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(contenu)), piece.GetProperty("sha256").GetString());
        Assert.Equal("Bob", piece.GetProperty("deposePar").GetString());
        Assert.Equal("text/plain", piece.GetProperty("typeContenu").GetString());
        Assert.Equal([pid.ToString("N")], Directory.GetFiles(_api.DossierPieces).Select(f => Path.GetFileName(f)!).ToArray());

        // Supprimer une étape efface ses pièces jointes (ligne et fichier).
        mep = await Ok(await alice.PostAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}/pieces-jointes", Fichier("e4.txt", [4])));
        Assert.Equal(2, Directory.GetFiles(_api.DossierPieces).Length);
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/etapes/{EtapeId(mep, "E0004")}"));
        Assert.Single(mep.GetProperty("piecesJointes").EnumerateArray());
        Assert.Single(Directory.GetFiles(_api.DossierPieces));

        mep = await Ok(await alice.PostAsync($"/api/meps/{id}/pieces-jointes", Fichier("procédure.pdf", [1, 2, 3], "application/pdf")));
        var liste = (await Ok(await claire.GetAsync($"/api/meps/{id}/pieces-jointes"))).EnumerateArray().ToList();
        Assert.Equal(2, liste.Count);
        Assert.Equal(JsonValueKind.Null, liste[1].GetProperty("etapeId").ValueKind);

        var telecharge = await claire.GetAsync($"/api/pieces-jointes/{pid}/contenu");
        Assert.Equal(HttpStatusCode.OK, telecharge.StatusCode);
        Assert.Equal(contenu, await telecharge.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", telecharge.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("journalapi.txt", telecharge.Content.Headers.ContentDisposition.FileNameStar ?? telecharge.Content.Headers.ContentDisposition.FileName);
        Assert.Equal("nosniff", telecharge.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await claire.GetAsync($"/api/pieces-jointes/{Guid.NewGuid()}/contenu")).StatusCode);

        // Taille maximale (1 Mo dans les tests), fichier vide, corps non multipart.
        var gros = await alice.PostAsync($"/api/meps/{id}/pieces-jointes", Fichier("gros.bin", new byte[1024 * 1024 + 1]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, gros.StatusCode);
        Assert.Equal("Fichier trop volumineux : 1 Mo maximum.", await Message(gros));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsync($"/api/meps/{id}/pieces-jointes", Fichier("vide.txt", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/pieces-jointes", new { })).StatusCode);
        Assert.Equal(2, Directory.GetFiles(_api.DossierPieces).Length);

        // Suppression : déposant ou pilote.
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.DeleteAsync($"/api/meps/{id}/pieces-jointes/{pid}")).StatusCode);
        mep = await Ok(await bob.DeleteAsync($"/api/meps/{id}/pieces-jointes/{pid}"));
        Assert.Single(mep.GetProperty("piecesJointes").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await claire.GetAsync($"/api/pieces-jointes/{pid}/contenu")).StatusCode);
        Assert.False(File.Exists(Path.Combine(_api.DossierPieces, pid.ToString("N"))));
        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "piece.ajoutee" && e.Auteur == "Bob" && e.Detail == "Pièce jointe ajoutée à E0002 : journalapi.txt");
        Assert.Contains(journal, e => e.Type == "piece.supprimee" && e.Detail == "Pièce jointe supprimée à E0002 : journalapi.txt");

        // Après clôture : ni ajout ni suppression.
        mep = await AidesApi.Go(_api, alice, id);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Exercice" }));
        mep = await Reussir(bob, id, mep, "R0001", "rétablie");
        mep = await Reussir(claire, id, mep, "R0002", "Service 1.0 OK");
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Le repli", incidents = "Rien à signaler" }));
        var apres = await alice.PostAsync($"/api/meps/{id}/pieces-jointes", Fichier("tard.txt", [1]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, apres.StatusCode);
        Assert.Contains("clôturée", await Message(apres));
        var restante = mep.GetProperty("piecesJointes")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.DeleteAsync($"/api/meps/{id}/pieces-jointes/{restante}")).StatusCode);
    }

    [Fact]
    public async Task Pieces_jointes_d_une_resolution_type_supprimees_par_un_admin()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var rid = (await Ok(await bob.GetAsync("/api/resolutions"))).EnumerateArray().First().GetProperty("id").GetGuid();

        var cree = await bob.PostAsync($"/api/resolutions/{rid}/pieces-jointes", Fichier("capture.png", [137, 80, 78, 71], "image/png"));
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var piece = await Lire(cree);
        var pid = piece.GetProperty("id").GetGuid();
        Assert.Equal(rid, piece.GetProperty("resolutionTypeId").GetGuid());
        Assert.Equal(JsonValueKind.Null, piece.GetProperty("mepId").ValueKind);
        Assert.Single((await Ok(await bob.GetAsync($"/api/resolutions/{rid}/pieces-jointes"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/resolutions/{Guid.NewGuid()}/pieces-jointes", Fichier("x.txt", [1]))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/resolutions/{rid}/pieces-jointes/{pid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/resolutions/{rid}/pieces-jointes/{pid}")).StatusCode);
        Assert.Empty((await Ok(await bob.GetAsync($"/api/resolutions/{rid}/pieces-jointes"))).EnumerateArray());
        Assert.Empty(Directory.GetFiles(_api.DossierPieces));
    }
}
