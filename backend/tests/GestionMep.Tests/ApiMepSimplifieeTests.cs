using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>API de la MEP simplifiée (issue #56) : création, changements de type, droits, étapes et résolutions du jour J.</summary>
[Collection("Api")]
public class ApiMepSimplifieeTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static JsonElement Etape(JsonElement mep, string code) => mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    private static Guid Participant(JsonElement mep, string nom) =>
        mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

    private static List<Guid> Suppleants(JsonElement mep) => mep.GetProperty("suppleantIds").EnumerateArray().Select(s => s.GetGuid()).ToList();

    private static async Task<List<(string Type, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private static Task<HttpResponseMessage> ChangerType(HttpClient c, JsonElement mep, string type) =>
        c.PutAsJsonAsync($"/api/meps/{mep.GetProperty("id").GetGuid()}/type", new { version = mep.GetProperty("version").GetInt32(), type });

    [Fact]
    public async Task Creation_simplifiee_et_modele_toujours_complet()
    {
        var alice = await _api.ClientConnecte("alice");
        var mep = await Ok(await alice.PostAsJsonAsync("/api/meps", new { outil = "Correctif", objet = "", type = "Simplifiee" }));
        Assert.Equal("Simplifiee", mep.GetProperty("type").GetString());
        var id = mep.GetProperty("id").GetGuid();
        Assert.Equal(("mep.creee", "MEP simplifiée créée : Correctif"), (await Journal(alice, id))[0]);
        var resume = (await Ok(await alice.GetAsync("/api/meps"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
        Assert.Equal("Simplifiee", resume.GetProperty("type").GetString());

        // Type absent : complète.
        var complete = await Ok(await alice.PostAsJsonAsync("/api/meps", new { outil = "Autre", objet = "" }));
        Assert.Equal("Complete", complete.GetProperty("type").GetString());

        var modele = await alice.PostAsJsonAsync("/api/meps", new { outil = "Modèle", objet = "", modele = true, type = "Simplifiee" });
        Assert.Equal(HttpStatusCode.BadRequest, modele.StatusCode);

        // Une MEP créée depuis un modèle est complète.
        var idModele = (await Ok(await alice.GetAsync("/api/meps?modele=true"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var depuisModele = await Ok(await alice.PostAsJsonAsync($"/api/meps/{idModele}/dupliquer", new { commeModele = false }));
        Assert.Equal("Complete", depuisModele.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Passer_en_simplifiee_puis_en_complete()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Complete", mep.GetProperty("type").GetString());

        // Claire n'est ni pilote ni suppléante : elle ne change pas le type.
        Assert.Equal(HttpStatusCode.Forbidden, (await ChangerType(claire, mep, "Simplifiee")).StatusCode);
        Assert.False((await Ok(await claire.GetAsync($"/api/meps/{id}"))).GetProperty("droits").GetProperty("estPilote").GetBoolean());

        mep = await Ok(await ChangerType(alice, mep, "Simplifiee"));
        Assert.Equal("Simplifiee", mep.GetProperty("type").GetString());
        Assert.Equal("", mep.GetProperty("versionCible").GetString());
        Assert.All(mep.GetProperty("etapes").EnumerateArray(), e =>
        {
            Assert.Equal(JsonValueKind.Null, e.GetProperty("criticite").ValueKind);
            Assert.Equal(JsonValueKind.Null, e.GetProperty("dureeEstimeeMinutes").ValueKind);
            Assert.Equal(0, e.GetProperty("resolutionIds").GetArrayLength());
        });
        Assert.Equal(new[] { Participant(mep, "Bob"), Participant(mep, "Claire") }.Order(), Suppleants(mep).Order());
        var detail = (await Journal(alice, id)).Single(e => e.Type == "mep.type").Detail;
        Assert.StartsWith("MEP passée en simplifiée : effacés ", detail);
        Assert.Contains("versions actuelle et cible", detail);

        // Claire est désormais suppléante implicite : mêmes droits que le pilote.
        Assert.True((await Ok(await claire.GetAsync($"/api/meps/{id}"))).GetProperty("droits").GetProperty("estPilote").GetBoolean());

        // Versions et suppléants envoyés : ignorés.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", new
        {
            version = mep.GetProperty("version").GetInt32(), outil = mep.GetProperty("outil").GetString(), versionActuelle = "1", versionCible = "2",
            objet = "", impacts = "", fuseauHoraire = "Europe/Paris", piloteId = mep.GetProperty("piloteId").GetGuid(),
            suppleantIds = new[] { Participant(mep, "Bob") }, echeanceRepli = mep.GetProperty("echeanceRepli"),
        }));
        Assert.Equal("", mep.GetProperty("versionCible").GetString());
        Assert.Equal(2, Suppleants(mep).Count);

        // Étape modifiée avec criticité, durée et résolution : vidées.
        var e4 = Etape(mep, "E0004");
        var resolution = (await Ok(await alice.GetAsync("/api/resolutions"))).EnumerateArray().First().GetProperty("id").GetGuid();
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{e4.GetProperty("id").GetGuid()}", new
        {
            revision = e4.GetProperty("revision").GetInt32(), action = e4.GetProperty("action").GetString(), systemeIds = e4.GetProperty("systemeIds"),
            criticite = "Elevee", dureeEstimeeMinutes = 10, resolutionIds = new[] { resolution },
            instructions = e4.GetProperty("instructions").GetString(), resultatAttendu = e4.GetProperty("resultatAttendu").GetString(),
            dependances = e4.GetProperty("dependances"),
        }));
        e4 = Etape(mep, "E0004");
        Assert.Equal(JsonValueKind.Null, e4.GetProperty("criticite").ValueKind);
        Assert.Equal(JsonValueKind.Null, e4.GetProperty("dureeEstimeeMinutes").ValueKind);
        Assert.Equal(0, e4.GetProperty("resolutionIds").GetArrayLength());
        Assert.DoesNotContain(mep.GetProperty("erreursPlan").EnumerateArray(), e => e.GetString()!.Contains("Criticité", StringComparison.Ordinal));

        // Retour en complète : suppléants conservés, criticité réclamée.
        mep = await Ok(await ChangerType(claire, mep, "Complete"));
        Assert.Equal("Complete", mep.GetProperty("type").GetString());
        Assert.Equal(2, Suppleants(mep).Count);
        Assert.Contains(mep.GetProperty("erreursPlan").EnumerateArray(), e => e.GetString()!.EndsWith("compléter Criticité.", StringComparison.Ordinal));
        Assert.Contains(await Journal(alice, id), e => e.Type == "mep.type" && e.Detail.StartsWith("MEP passée en complète", StringComparison.Ordinal));

        // Version périmée : conflit.
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/meps/{id}/type", new { version = 0, type = "Simplifiee" })).StatusCode);
    }

    [Fact]
    public async Task Jour_j_une_resolution_des_systemes_de_l_etape_s_applique_sans_etre_liee()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await ChangerType(alice, await Ok(await alice.GetAsync($"/api/meps/{id}")), "Simplifiee"));
        // MEP simplifiée : Claire, personne de la MEP, est suppléante implicite et ne peut plus approuver (#94).
        Assert.Equal("suppléant du pilote", mep.GetProperty("approbations")[0].GetProperty("empechement").GetString());
        Assert.Contains(mep.GetProperty("erreursPlan").EnumerateArray(), e => e.GetString() == "Claire ne peut pas approuver cette MEP (suppléant du pilote). Le retirer des approbateurs.");
        // Son compte n'est plus relié à la personne « Claire » de la MEP : elle approuve de l'extérieur.
        await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/participants/{Participant(mep, "Claire")}",
            new { nom = "Claire", role = "Opératrice", contact = "", utilisateurId = (Guid?)null }));
        // Les criticités effacées ne bloquent pas le Go.
        mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());

        // Après le Go, le type ne change plus.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ChangerType(alice, mep, "Complete")).StatusCode);

        var resolutions = (await Ok(await alice.GetAsync("/api/resolutions"))).EnumerateArray().ToList();
        Guid Resolution(string titre) => resolutions.Single(r => r.GetProperty("titre").GetString() == titre).GetProperty("id").GetGuid();
        var e5 = Etape(mep, "E0005"); // API et LDAP, en échec
        var url = $"/api/meps/{id}/etapes/{e5.GetProperty("id").GetGuid()}/resolutions";
        var bdd = await alice.PostAsJsonAsync(url, new { resolutionTypeId = Resolution("BDD : utilisateur applicatif retiré des droits"), revision = e5.GetProperty("revision").GetInt32() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bdd.StatusCode);
        mep = await Ok(await alice.PostAsJsonAsync(url, new { resolutionTypeId = Resolution("API : réponse de test absente"), revision = e5.GetProperty("revision").GetInt32() }));
        Assert.Equal("EnCours", mep.GetProperty("resolutions").EnumerateArray().Single().GetProperty("statut").GetString());
    }

    [Fact]
    public async Task Un_administrateur_qui_demarre_une_etape_n_est_qu_executant_et_pas_suppleant()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var simplifiee = await Ok(await ChangerType(alice, await Ok(await alice.GetAsync($"/api/meps/{id}")), "Simplifiee"));
        // Claire, suppléante implicite, n'approuverait pas (#94) : son compte n'est plus relié à la personne de la MEP.
        await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/participants/{Participant(simplifiee, "Claire")}",
            new { nom = "Claire", role = "Opératrice", contact = "", utilisateurId = (Guid?)null }));
        var mep = await AidesApi.Go(_api, alice, id);
        var eve = await _api.NouveauCompte("eve", "Eve", estAdmin: true);

        // Eve, administratrice hors MEP, démarre E0004 : ajoutée d'office comme exécutante, pas comme suppléante implicite.
        var e4 = Etape(mep, "E0004");
        mep = await Ok(await eve.PostAsJsonAsync($"/api/meps/{id}/etapes/{e4.GetProperty("id").GetGuid()}/demarrer", new { revision = e4.GetProperty("revision").GetInt32() }));
        var pEve = Participant(mep, "Eve");
        Assert.Equal(pEve, Etape(mep, "E0004").GetProperty("executantId").GetGuid());
        Assert.DoesNotContain(pEve, Suppleants(mep));
        Assert.Contains(Participant(mep, "Bob"), Suppleants(mep));
        var droits = (await Ok(await eve.GetAsync($"/api/meps/{id}"))).GetProperty("droits");
        Assert.False(droits.GetProperty("estPilote").GetBoolean());
        Assert.Equal(pEve, droits.GetProperty("participantId").GetGuid());
        // Droits du pilote refusés ; elle termine l'étape qu'elle exécute.
        Assert.Equal(HttpStatusCode.Forbidden, (await eve.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Panne" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await eve.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Recette" })).StatusCode);
        e4 = Etape(mep, "E0004");
        mep = await Ok(await eve.PostAsJsonAsync($"/api/meps/{id}/etapes/{e4.GetProperty("id").GetGuid()}/terminer",
            new { revision = e4.GetProperty("revision").GetInt32(), resultat = "Reussie", preuve = "Fait", resultatVerifie = true }));
        Assert.Equal("Reussie", Etape(mep, "E0004").GetProperty("etat").GetString());
        // Recherche : exécutante, pas suppléante.
        var resume = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
        var roles = resume.GetProperty("intervenants").EnumerateArray().Single(i => i.GetProperty("nom").GetString() == "Eve").GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal(["Executant"], roles);

        // Pilotage de secours : désignée pilote, elle devient une personne à part entière ; relevée par Alice, elle reste suppléante.
        mep = await Ok(await eve.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { participantId = pEve, motif = "Alice injoignable" }));
        Assert.Equal(pEve, mep.GetProperty("piloteId").GetGuid());
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/pilotage", new { participantId = Participant(mep, "Alice"), motif = "De retour" }));
        Assert.Contains(pEve, Suppleants(mep));
    }
}
