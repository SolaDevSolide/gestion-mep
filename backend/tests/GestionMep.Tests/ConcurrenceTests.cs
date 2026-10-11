using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>
/// Écritures simultanées sur une même MEP : les actions sur des étapes différentes aboutissent toutes (réessai automatique
/// côté serveur), alors qu'un vrai conflit (même étape, révision ou version périmée) reste refusé.
/// </summary>
[Collection("Api")]
public class ConcurrenceTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    // ---------- Aides ----------
    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static int Version(JsonElement mep) => mep.GetProperty("version").GetInt32();
    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);
    private static Guid EtapeId(JsonElement mep, string code) => Etape(mep, code).GetProperty("id").GetGuid();
    private static int Revision(JsonElement mep, string code) => Etape(mep, code).GetProperty("revision").GetInt32();
    private static string Etat(JsonElement mep, string code) => Etape(mep, code).GetProperty("etat").GetString()!;

    private static async Task<List<(string Type, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private static async Task<string> Corps(HttpResponseMessage rep) => $"{(int)rep.StatusCode} : {await rep.Content.ReadAsStringAsync()}";

    /// <summary>Lance toutes les requêtes en même temps et attend leurs réponses.</summary>
    private static async Task<HttpResponseMessage[]> Simultanement(params Func<Task<HttpResponseMessage>>[] requetes)
    {
        using var depart = new SemaphoreSlim(0);
        var taches = requetes.Select(async r =>
        {
            await depart.WaitAsync();
            return await r();
        }).ToList();
        depart.Release(requetes.Length);
        return await Task.WhenAll(taches);
    }

    private static Task<HttpResponseMessage> Demarrer(HttpClient c, Guid id, JsonElement mep, string code) =>
        c.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, code)}/demarrer", new { revision = Revision(mep, code) });

    private static Task<HttpResponseMessage> Terminer(HttpClient c, Guid id, JsonElement mep, string code) =>
        c.PostAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, code)}/terminer",
            new { revision = Revision(mep, code), resultat = "Reussie", preuve = "OK", resultatVerifie = true });

    private static Task<HttpResponseMessage> Commenter(HttpClient c, Guid id, JsonElement mep, string code, string texte) =>
        c.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(mep, code)}/commentaire", new { revision = Revision(mep, code), commentaire = texte });

    /// <summary>Démo passée en exécution (Go) : E0004 et E0006 sont disponibles, E0005 est en échec (exécutée par Bob).</summary>
    private async Task<(HttpClient Alice, HttpClient Bob, HttpClient Claire, Guid Id, JsonElement Mep)> EnExecution()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        return (alice, bob, claire, id, mep);
    }

    private static async Task ToutesOk(HttpResponseMessage[] reponses)
    {
        foreach (var rep in reponses) Assert.True(rep.StatusCode == HttpStatusCode.OK, await Corps(rep));
    }

    // ---------- Étapes différentes : toutes les actions aboutissent ----------
    [Fact]
    public async Task Actions_simultanees_sur_des_etapes_differentes_aboutissent_toutes()
    {
        var (alice, bob, claire, id, mep) = await EnExecution();
        var version = Version(mep);
        var journalAvant = (await Journal(alice, id)).Count;

        // Démarrer E0004 (Alice), démarrer E0006 (Claire), commenter E0005 (Bob) : même version de MEP lue par tous.
        await ToutesOk(await Simultanement(
            () => Demarrer(alice, id, mep, "E0004"),
            () => Demarrer(claire, id, mep, "E0006"),
            () => Commenter(bob, id, mep, "E0005", "Analyse en cours")));

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(version + 3, Version(mep));
        Assert.Equal("EnCours", Etat(mep, "E0004"));
        Assert.Equal("EnCours", Etat(mep, "E0006"));

        // Terminer les deux étapes et commenter de nouveau E0005, toujours en même temps.
        await ToutesOk(await Simultanement(
            () => Terminer(alice, id, mep, "E0004"),
            () => Terminer(claire, id, mep, "E0006"),
            () => Commenter(bob, id, mep, "E0005", "Cause trouvée")));

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(version + 6, Version(mep));
        Assert.Equal("Reussie", Etat(mep, "E0004"));
        Assert.Equal("Reussie", Etat(mep, "E0006"));
        Assert.Equal("Cause trouvée", Etape(mep, "E0005").GetProperty("commentaire").GetString());
        var participants = mep.GetProperty("participants").EnumerateArray()
            .ToDictionary(p => p.GetProperty("id").GetGuid(), p => p.GetProperty("nom").GetString());
        Assert.Equal("Alice", participants[Etape(mep, "E0004").GetProperty("executantId").GetGuid()]);
        Assert.Equal("Claire", participants[Etape(mep, "E0006").GetProperty("executantId").GetGuid()]);

        // Une seule ligne de journal par action, aucune en double.
        var journal = await Journal(alice, id);
        Assert.Equal(journalAvant + 6, journal.Count);
        Assert.Single(journal, e => e.Type == "etape.demarree" && e.Detail.StartsWith("E0004"));
        Assert.Single(journal, e => e.Type == "etape.demarree" && e.Detail.StartsWith("E0006"));
        Assert.Single(journal, e => e.Type == "etape.reussie" && e.Detail.StartsWith("E0004"));
        Assert.Single(journal, e => e.Type == "etape.reussie" && e.Detail.StartsWith("E0006"));
        Assert.Equal(2, journal.Count(e => e.Type == "etape.commentaire" && e.Detail.Contains("E0005")));
    }

    [Fact]
    public async Task Actions_simultanees_repetees_sur_des_etapes_differentes_aboutissent_toutes()
    {
        var (alice, bob, claire, id, mep) = await EnExecution();
        mep = await Ok(await Demarrer(alice, id, mep, "E0004"));
        mep = await Ok(await Demarrer(claire, id, mep, "E0006"));
        var version = Version(mep);
        var journalAvant = (await Journal(alice, id)).Count;

        const int tours = 10;
        for (var tour = 1; tour <= tours; tour++)
        {
            var lue = mep;
            var t = tour;
            await ToutesOk(await Simultanement(
                () => Commenter(alice, id, lue, "E0004", $"Alice tour {t}"),
                () => Commenter(bob, id, lue, "E0005", $"Bob tour {t}"),
                () => Commenter(claire, id, lue, "E0006", $"Claire tour {t}")));
            mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
            Assert.Equal(version + 3 * tour, Version(mep));
            Assert.Equal($"Alice tour {t}", Etape(mep, "E0004").GetProperty("commentaire").GetString());
            Assert.Equal($"Bob tour {t}", Etape(mep, "E0005").GetProperty("commentaire").GetString());
            Assert.Equal($"Claire tour {t}", Etape(mep, "E0006").GetProperty("commentaire").GetString());
        }

        var journal = await Journal(alice, id);
        Assert.Equal(journalAvant + 3 * tours, journal.Count);
        foreach (var qui in new[] { "Alice", "Bob", "Claire" })
            for (var tour = 1; tour <= tours; tour++)
                Assert.Single(journal, e => e.Type == "etape.commentaire" && e.Detail.EndsWith($"{qui} tour {tour}"));
    }

    // ---------- Vrais conflits : toujours refusés ----------
    [Fact]
    public async Task Deux_demarrages_simultanes_de_la_meme_etape_un_seul_aboutit()
    {
        var (alice, bob, _, id, mep) = await EnExecution();
        var version = Version(mep);

        var reponses = await Simultanement(() => Demarrer(alice, id, mep, "E0004"), () => Demarrer(bob, id, mep, "E0004"));

        var reussies = reponses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        Assert.Single(reussies);
        var refusee = reponses.Single(r => r.StatusCode != HttpStatusCode.OK);
        Assert.True(refusee.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, await Corps(refusee));

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(version + 1, Version(mep));
        Assert.Equal("EnCours", Etat(mep, "E0004"));
        var gagnant = (await Ok(reussies[0])).GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == "E0004")
            .GetProperty("executantId").GetGuid();
        Assert.Equal(gagnant, Etape(mep, "E0004").GetProperty("executantId").GetGuid());
        Assert.Single(await Journal(alice, id), e => e.Type == "etape.demarree" && e.Detail.StartsWith("E0004"));
    }

    [Fact]
    public async Task Une_revision_d_etape_perimee_reste_refusee()
    {
        var (alice, bob, claire, id, lue) = await EnExecution();
        // Alice démarre E0004 : la révision lue avant devient périmée pour cette étape.
        await Ok(await Demarrer(alice, id, lue, "E0004"));

        // Seule, puis en même temps que des écritures sur d'autres étapes : refus 409 dans les deux cas.
        var seule = await Commenter(alice, id, lue, "E0004", "Trop tard");
        Assert.Equal(HttpStatusCode.Conflict, seule.StatusCode);
        Assert.Contains("E0004 a été modifiée", await seule.Content.ReadAsStringAsync());

        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var reponses = await Simultanement(
            () => Terminer(alice, id, lue, "E0004"),
            () => Demarrer(claire, id, mep, "E0006"),
            () => Commenter(bob, id, mep, "E0005", "En parallèle"));
        Assert.Equal(HttpStatusCode.Conflict, reponses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, reponses[1].StatusCode);
        Assert.Equal(HttpStatusCode.OK, reponses[2].StatusCode);

        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("EnCours", Etat(mep, "E0004"));
        Assert.NotEqual("Trop tard", Etape(mep, "E0004").GetProperty("commentaire").GetString());
        var journal = await Journal(alice, id);
        Assert.DoesNotContain(journal, e => e.Type == "etape.reussie" && e.Detail.StartsWith("E0004"));
        Assert.DoesNotContain(journal, e => e.Detail.Contains("Trop tard"));
    }

    [Fact]
    public async Task Deux_modifications_simultanees_avec_la_meme_version_de_mep_une_seule_aboutit()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var version = Version(mep);
        var replisAvant = (await Journal(alice, id)).Count(e => e.Type == "repli.modifie");

        // La version de la MEP lue par le client est exigée : l'écriture passée entre-temps est un vrai conflit, pas de réessai.
        var reponses = await Simultanement(
            () => alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { version, quandDeclencher = "Par Alice" }),
            () => bob.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { version, quandDeclencher = "Par Bob" }));

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.OK);
        var refusee = reponses.Single(r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, refusee.StatusCode);
        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.True(Version(mep) > version);
        Assert.Equal(replisAvant + 1, (await Journal(alice, id)).Count(e => e.Type == "repli.modifie"));
    }
}
