using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>
/// Écritures simultanées sur une même MEP : les écritures passent l'une après l'autre (verrou de la MEP), si bien que les
/// actions sur des étapes ou des blocs différents aboutissent toutes, alors qu'un vrai conflit (même étape ou même bloc avec
/// une révision périmée) reste refusé. Jusqu'à 10 personnes en même temps (#96) ; les résultats ne dépendent pas de l'ordre
/// d'arrivée des requêtes.
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

    // ---------- Dix personnes en même temps (#96) ----------

    /// <summary>Dix clients connectés (Alice, pilote et administratrice, depuis plusieurs onglets ; Bob et Claire en lecture).</summary>
    private async Task<HttpClient[]> DixClients()
    {
        var clients = new HttpClient[10];
        for (var i = 0; i < clients.Length; i++) clients[i] = await _api.ClientConnecte("alice");
        return clients;
    }

    private static List<string> Codes(JsonElement mep) =>
        [.. mep.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Dix_ecritures_simultanees_sur_dix_etapes_differentes_aboutissent_toutes_sans_conflit()
    {
        var (_, _, _, id, mep) = await EnExecution();
        var clients = await DixClients();
        var codes = Codes(mep).Where(c => c is not ("E0004" or "E0006")).Take(8).ToList();
        Assert.Equal(8, codes.Count);
        var version = Version(mep);
        var journalAvant = (await Journal(clients[0], id)).Count;

        // Démarrer deux étapes disponibles et commenter huit autres étapes, tous en même temps, avec la même lecture de la MEP.
        var requetes = new List<Func<Task<HttpResponseMessage>>>
        {
            () => Demarrer(clients[0], id, mep, "E0004"),
            () => Demarrer(clients[1], id, mep, "E0006"),
        };
        requetes.AddRange(codes.Select((code, i) => (Func<Task<HttpResponseMessage>>)(() => Commenter(clients[i + 2], id, mep, code, $"Note {code}"))));
        await ToutesOk(await Simultanement([.. requetes]));

        mep = await Ok(await clients[0].GetAsync($"/api/meps/{id}"));
        Assert.Equal(version + 10, Version(mep));
        Assert.Equal("EnCours", Etat(mep, "E0004"));
        Assert.Equal("EnCours", Etat(mep, "E0006"));
        foreach (var code in codes) Assert.Equal($"Note {code}", Etape(mep, code).GetProperty("commentaire").GetString());
        var journal = await Journal(clients[0], id);
        Assert.Equal(journalAvant + 10, journal.Count);
        foreach (var code in codes) Assert.Single(journal, e => e.Type == "etape.commentaire" && e.Detail == $"Commentaire sur {code} : Note {code}");
    }

    [Fact]
    public async Task Dix_ecritures_simultanees_sur_la_meme_etape_une_seule_aboutit_les_autres_recoivent_409()
    {
        var (_, _, _, id, mep) = await EnExecution();
        var clients = await DixClients();
        var version = Version(mep);

        var reponses = await Simultanement([.. clients.Select((c, i) => (Func<Task<HttpResponseMessage>>)(() => Commenter(c, id, mep, "E0005", $"Version {i}")))]);

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var refus in reponses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.Conflict, refus.StatusCode);
            Assert.Contains("E0005 a été modifiée par quelqu'un d'autre", await refus.Content.ReadAsStringAsync());
        }
        var gagnante = (await Ok(reponses.Single(r => r.StatusCode == HttpStatusCode.OK))).GetProperty("etapes").EnumerateArray()
            .Single(e => e.GetProperty("code").GetString() == "E0005").GetProperty("commentaire").GetString();
        mep = await Ok(await clients[0].GetAsync($"/api/meps/{id}"));
        Assert.Equal(version + 1, Version(mep));
        Assert.Equal(gagnante, Etape(mep, "E0005").GetProperty("commentaire").GetString());
        Assert.Single(await Journal(clients[0], id), e => e.Type == "etape.commentaire" && e.Detail == $"Commentaire sur E0005 : {gagnante}");
    }

    [Fact]
    public async Task Dix_ecritures_simultanees_melangees_seuls_les_vrais_conflits_sont_refuses()
    {
        var (_, _, _, id, mep) = await EnExecution();
        var clients = await DixClients();
        var codes = Codes(mep).Where(c => c != "E0005").Take(5).ToList();
        var version = Version(mep);

        // Cinq étapes différentes (une écriture chacune) et cinq écritures sur E0005 avec la même révision.
        var requetes = codes.Select((code, i) => (Func<Task<HttpResponseMessage>>)(() => Commenter(clients[i], id, mep, code, $"Seule sur {code}")))
            .Concat(Enumerable.Range(5, 5).Select(i => (Func<Task<HttpResponseMessage>>)(() => Commenter(clients[i], id, mep, "E0005", $"Concurrente {i}"))))
            .ToArray();
        var reponses = await Simultanement(requetes);

        for (var i = 0; i < 5; i++) Assert.True(reponses[i].StatusCode == HttpStatusCode.OK, await Corps(reponses[i]));
        Assert.Single(reponses[5..], r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, reponses[5..].Count(r => r.StatusCode == HttpStatusCode.Conflict));
        mep = await Ok(await clients[0].GetAsync($"/api/meps/{id}"));
        Assert.Equal(version + 6, Version(mep));
        foreach (var code in codes) Assert.Equal($"Seule sur {code}", Etape(mep, code).GetProperty("commentaire").GetString());
    }

    // ---------- Révision par bloc de formulaire (#96) ----------

    private static int RevisionBloc(JsonElement mep, string bloc) => mep.GetProperty("revisions").GetProperty(bloc).GetInt32();

    private static object Identite(JsonElement mep, string objet, int revision) => new
    {
        revision,
        outil = mep.GetProperty("outil").GetString(), versionActuelle = mep.GetProperty("versionActuelle").GetString(),
        versionCible = mep.GetProperty("versionCible").GetString(), objet, impacts = mep.GetProperty("impacts").GetString(),
        fuseauHoraire = mep.GetProperty("fuseauHoraire").GetString(),
        indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes").ValueKind == JsonValueKind.Null ? (int?)null : mep.GetProperty("indisponibiliteMinutes").GetInt32(),
        piloteId = mep.GetProperty("piloteId").GetGuid(),
        suppleantIds = mep.GetProperty("suppleantIds").EnumerateArray().Select(x => x.GetGuid()).ToList(),
    };

    [Fact]
    public async Task Un_formulaire_de_section_n_est_pas_perime_par_une_ecriture_ailleurs_dans_la_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        var lue = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var identite = RevisionBloc(lue, "identite");

        // Bob modifie le plan de retour arrière et une étape : la révision de l'intervention ne change pas.
        var apres = await Ok(await bob.PutAsJsonAsync($"/api/meps/{id}/plan-repli",
            new { revision = RevisionBloc(lue, "planRepli"), quandDeclencher = "Si le portail ne répond plus" }));
        Assert.Equal(RevisionBloc(lue, "planRepli") + 1, RevisionBloc(apres, "planRepli"));
        Assert.Equal(identite, RevisionBloc(apres, "identite"));
        var autreOnglet = await _api.ClientConnecte("alice");
        apres = await Ok(await autreOnglet.PutAsJsonAsync($"/api/meps/{id}/etapes/{EtapeId(apres, "E0001")}", Fiche(Etape(apres, "E0001"), "Revu")));
        Assert.True(Version(apres) > Version(lue));
        Assert.Equal(identite, RevisionBloc(apres, "identite"));

        // Alice enregistre l'intervention lue avant : aucun faux conflit.
        var mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(lue, "Objet revu par Alice", identite)));
        Assert.Equal("Objet revu par Alice", mep.GetProperty("objet").GetString());
        Assert.Equal(identite + 1, RevisionBloc(mep, "identite"));
        Assert.Equal("Si le portail ne répond plus", mep.GetProperty("planRepli").GetProperty("quandDeclencher").GetString());

        // Bob, qui avait lu l'intervention avant Alice, est refusé avec un message clair ; rien n'est écrasé.
        var refus = await bob.PutAsJsonAsync($"/api/meps/{id}", Identite(lue, "Objet de Bob", identite));
        Assert.Equal(HttpStatusCode.Conflict, refus.StatusCode);
        Assert.Contains("« Intervention » a été modifié par quelqu'un d'autre", await refus.Content.ReadAsStringAsync());
        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Objet revu par Alice", mep.GetProperty("objet").GetString());

        // Sans révision ni version : 400 ; l'ancienne version de toute la MEP reste acceptée (contrôle strict).
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { quandDeclencher = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { version = Version(lue), quandDeclencher = "x" })).StatusCode);
        await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { version = Version(mep), quandDeclencher = "Par version" }));
    }

    [Fact]
    public async Task Dix_formulaires_de_sections_differentes_enregistres_en_meme_temps_aboutissent_tous()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var clients = await DixClients();
        var lue = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var systemes = lue.GetProperty("systemes").EnumerateArray().ToList();
        var etapes = lue.GetProperty("etapes").EnumerateArray().Where(e => e.GetProperty("plan").GetString() == "Deploiement").Take(10 - 3 - Math.Min(3, systemes.Count)).ToList();

        // Intervention, plan de retour arrière, communication, sauvegarde de trois systèmes et fiches d'étapes : tous lus en même temps.
        var requetes = new List<Func<Task<HttpResponseMessage>>>
        {
            () => clients[0].PutAsJsonAsync($"/api/meps/{id}", Identite(lue, "Objet simultané", RevisionBloc(lue, "identite"))),
            () => clients[1].PutAsJsonAsync($"/api/meps/{id}/plan-repli", new { revision = RevisionBloc(lue, "planRepli"), quandDeclencher = "Simultané" }),
            () => clients[2].PutAsJsonAsync($"/api/meps/{id}/communication", new { revision = RevisionBloc(lue, "communication"), referenceDemande = "CHG-96" }),
        };
        requetes.AddRange(systemes.Take(3).Select((s, i) => (Func<Task<HttpResponseMessage>>)(() =>
            clients[3 + i].PutAsJsonAsync($"/api/meps/{id}/systemes/{s.GetProperty("id").GetGuid()}/sauvegarde", new
            {
                revision = s.GetProperty("revisionSauvegarde").GetInt32(), statut = "NonRequise", justification = $"Simultané {i}",
            }))));
        requetes.AddRange(etapes.Select((e, i) => (Func<Task<HttpResponseMessage>>)(() =>
            clients[(6 + i) % 10].PutAsJsonAsync($"/api/meps/{id}/etapes/{e.GetProperty("id").GetGuid()}", Fiche(e, $"Instructions {i}")))));
        Assert.Equal(10, requetes.Count);
        await ToutesOk(await Simultanement([.. requetes]));

        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal("Objet simultané", mep.GetProperty("objet").GetString());
        Assert.Equal("Simultané", mep.GetProperty("planRepli").GetProperty("quandDeclencher").GetString());
        Assert.Equal("CHG-96", mep.GetProperty("communication").GetProperty("referenceDemande").GetString());
        foreach (var (e, i) in etapes.Select((e, i) => (e, i)))
        {
            var apres = mep.GetProperty("etapes").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == e.GetProperty("id").GetGuid());
            Assert.Equal($"Instructions {i}", apres.GetProperty("instructions").GetString());
            // dependances absentes : les liens du graphe sont gardés.
            Assert.Equal(e.GetProperty("dependances").EnumerateArray().Select(x => x.GetGuid()).Order(),
                apres.GetProperty("dependances").EnumerateArray().Select(x => x.GetGuid()).Order());
        }
        Assert.Equal(Version(lue) + 10, Version(mep));
    }

    /// <summary>Fiche complète d'une étape, sans <c>dependances</c> (absentes : inchangées).</summary>
    private static object Fiche(JsonElement e, string instructions) => new
    {
        revision = e.GetProperty("revision").GetInt32(), action = e.GetProperty("action").GetString(),
        systemeIds = e.GetProperty("systemeIds").EnumerateArray().Select(x => x.GetGuid()).ToList(),
        criticite = e.GetProperty("criticite").ValueKind == JsonValueKind.Null ? null : e.GetProperty("criticite").GetString(),
        dureeEstimeeMinutes = e.GetProperty("dureeEstimeeMinutes").ValueKind == JsonValueKind.Null ? (int?)null : e.GetProperty("dureeEstimeeMinutes").GetInt32(),
        instructions, resultatAttendu = e.GetProperty("resultatAttendu").GetString(),
        resolutionIds = e.GetProperty("resolutionIds").EnumerateArray().Select(x => x.GetGuid()).ToList(),
        type = e.GetProperty("type").GetString(), pointDeNonRetour = e.GetProperty("pointDeNonRetour").GetBoolean(),
    };

    [Fact]
    public async Task Un_put_d_etape_sans_dependances_garde_les_liens_du_graphe_et_une_liste_vide_les_retire()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var e = mep.GetProperty("etapes").EnumerateArray().First(x => x.GetProperty("dependances").GetArrayLength() > 0);
        var code = e.GetProperty("code").GetString()!;
        var liens = e.GetProperty("dependances").EnumerateArray().Select(x => x.GetGuid()).Order().ToList();

        // Correction d'une faute de frappe par un script : sans « dependances », le graphe ne bouge pas.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{e.GetProperty("id").GetGuid()}", Fiche(e, "Instructions corrigées")));
        var apres = Etape(mep, code);
        Assert.Equal("Instructions corrigées", apres.GetProperty("instructions").GetString());
        Assert.Equal(liens, apres.GetProperty("dependances").EnumerateArray().Select(x => x.GetGuid()).Order());

        // « dependances » à null : inchangées aussi ; liste vide : plus aucun préalable direct.
        var corps = JsonSerializer.SerializeToNode(Fiche(apres, "Encore"))!.AsObject();
        corps["dependances"] = null;
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{e.GetProperty("id").GetGuid()}", corps));
        Assert.Equal(liens, Etape(mep, code).GetProperty("dependances").EnumerateArray().Select(x => x.GetGuid()).Order());
        corps = JsonSerializer.SerializeToNode(Fiche(Etape(mep, code), "Isolée"))!.AsObject();
        corps["dependances"] = new System.Text.Json.Nodes.JsonArray();
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/etapes/{e.GetProperty("id").GetGuid()}", corps));
        Assert.Empty(Etape(mep, code).GetProperty("dependances").EnumerateArray());
    }
}
