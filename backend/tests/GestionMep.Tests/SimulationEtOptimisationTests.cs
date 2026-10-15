using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// « Optimiser les liens » (réduction transitive du plan, cercles des sections compris) et « Simuler des collègues » (mode
/// démonstration : Bob et Claire travaillent en vrai sur « Facturation électronique »).
/// </summary>
[Collection("Api")]
public class SimulationEtOptimisationTests
{
    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static Guid IdEtape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    private static Guid IdCercle(JsonElement mep, string titre) =>
        mep.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("ports").EnumerateArray())
            .Single(p => p.GetProperty("titre").GetString() == titre).GetProperty("id").GetGuid();

    /// <summary>Tout ce qu'attend l'étape, directement ou non (ce qui fixe l'ordre d'exécution).</summary>
    private static HashSet<Guid> Prealables(JsonElement mep, string code)
    {
        var deps = mep.GetProperty("etapes").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetGuid(),
            e => e.GetProperty("dependances").EnumerateArray().Select(d => d.GetGuid()).ToList());
        var vus = new HashSet<Guid>();
        var pile = new Stack<Guid>(deps[IdEtape(mep, code)]);
        while (pile.Count > 0)
            if (pile.Pop() is var x && vus.Add(x))
                foreach (var d in deps[x]) pile.Push(d);
        return vus;
    }

    [Fact]
    public async Task Les_liens_redondants_sont_listes_puis_retires_en_une_operation_sans_changer_l_execution()
    {
        using var api = new ApiFixture();
        var alice = await api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilIncomplete);
        var redondants = $"/api/meps/{id}/liens/redondants?plan=Deploiement";
        Assert.Empty((await Ok(await alice.GetAsync(redondants))).EnumerateArray());

        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Guid e1 = IdEtape(mep, "E0001"), e5 = IdEtape(mep, "E0005"), sauvegardeFaite = IdCercle(mep, "Sauvegarde faite");
        // E0005 attend déjà E0001 (par E0002 et « Tables migrées ») et « Sauvegarde faite » (par la section Export) : deux liens inutiles.
        await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/liens", new { de = e1, vers = e5 }));
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/liens", new { de = sauvegardeFaite, vers = e5 }));
        var avant = Prealables(mep, "E0005");

        var liste = (await Ok(await alice.GetAsync(redondants))).EnumerateArray().ToList();
        Assert.Equal(["E0001 → E0005", "Sauvegarde faite (Tables factures) → E0005"],
            liste.Select(l => l.GetProperty("libelle").GetString()!).Order(StringComparer.Ordinal).ToArray());
        var liens = liste.Select(l => new { de = l.GetProperty("de").GetGuid(), vers = l.GetProperty("vers").GetGuid() }).ToList();

        // Liste périmée (le plan a changé depuis le calcul) : refus, rien ne change.
        var perime = await alice.PostAsJsonAsync($"/api/meps/{id}/liens/optimiser", new { plan = "Deploiement", liens = liens.Take(1) });
        Assert.Equal(HttpStatusCode.Conflict, perime.StatusCode);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/liens/optimiser", new { plan = "Deploiement", liens }));
        Assert.Empty((await Ok(await alice.GetAsync(redondants))).EnumerateArray());
        // Ce que E0005 attend, directement ou par les cercles, n'a pas changé ; le lien direct E0001 → E0005 a disparu.
        Assert.True(avant.SetEquals(Prealables(mep, "E0005")));
        Assert.DoesNotContain(mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == "E0005")
            .GetProperty("portsAttendus").EnumerateArray(), p => p.GetGuid() == sauvegardeFaite);
        var journal = await Ok(await alice.GetAsync($"/api/meps/{id}/journal"));
        Assert.Contains(journal.EnumerateArray(), j => j.GetProperty("type").GetString() == "plan.liens-optimises"
            && j.GetProperty("detail").GetString()!.StartsWith("Liens optimisés : 2 liens redondants retirés", StringComparison.Ordinal));
    }

    [Fact]
    public void La_reduction_garde_les_chemins_par_les_cercles_et_ne_retire_que_les_raccourcis()
    {
        var m = new Mep { Outil = "Test" };
        Etape Ajouter(Guid? section = null)
        {
            var e = new Etape { MepId = m.Id, Plan = TypePlan.Deploiement, Code = Regles.ProchainCode(m.Etapes, TypePlan.Deploiement) };
            Regles.AjouterEtape(m, e, new(), section);
            return e;
        }
        void Relier(Guid de, Guid vers) => Regles.Relier(m, TypePlan.Deploiement, de, vers);
        // A → entrée de S → B (dans S) → sortie de S → C, et le raccourci A → C.
        var a = Ajouter();
        var s = Regles.CreerSection(m, TypePlan.Deploiement, "S", "", [], null, DateTimeOffset.UnixEpoch, placee: true);
        var entree = s.Ports.Single(p => p.Sens == SensPort.Entree);
        var sortie = s.Ports.Single(p => p.Sens == SensPort.Sortie);
        var b = Ajouter(s.Id);
        var c = Ajouter();
        Relier(a.Id, entree.Id);
        Relier(sortie.Id, c.Id);
        Relier(a.Id, c.Id);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), c.Dependances.Order());

        var redondants = Regles.LiensRedondants(m, TypePlan.Deploiement);
        Assert.Equal([new Regles.LienPlan(a.Id, c.Id)], redondants);
        // Liste différente de celle du plan : refus, rien ne change.
        Assert.Throws<RegleMetierException>(() => Regles.OptimiserLiens(m, TypePlan.Deploiement, [new Regles.LienPlan(sortie.Id, c.Id)]));
        Assert.Contains(a.Id, Regles.Directes(c));

        Assert.Single(Regles.OptimiserLiens(m, TypePlan.Deploiement, redondants));
        Assert.DoesNotContain(a.Id, Regles.Directes(c));
        Assert.Equal([b.Id], c.Dependances); // A reste attendue, par B et les cercles
        Assert.Contains(a.Id, b.Dependances);
        Assert.Contains(sortie.Id, c.PortsAttendus);
        Assert.Empty(Regles.LiensRedondants(m, TypePlan.Deploiement));
    }

    private static async Task<T> Attendre<T>(Func<Task<T?>> lire, string quoi) where T : class
    {
        var fin = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < fin)
        {
            if (await lire() is { } v) return v;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Attendu : {quoi}.");
    }

    [Fact]
    public async Task La_simulation_fait_travailler_bob_et_claire_en_vrai_puis_s_arrete_proprement()
    {
        Environment.SetEnvironmentVariable("Demo__SimulationPasMs", "25");
        try
        {
            using var api = new ApiFixture();
            var alice = await api.ClientConnecte("alice");
            var id = await AidesApi.IdMep(alice, DonneesDemo.OutilIncomplete);
            Assert.False((await Ok(await alice.GetAsync("/api/demo/simulation"))).GetProperty("actif").GetBoolean());

            // Alice a ouvert E0005 (verrou d'édition) : Claire finit par prendre la main dessus.
            var e5 = IdEtape(await Ok(await alice.GetAsync($"/api/meps/{id}")), "E0005");
            await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/etapes/{e5}/verrou", new { session = "onglet-alice" }));

            var etat = await Ok(await alice.PostAsJsonAsync("/api/demo/simulation", new { actif = true }));
            Assert.True(etat.GetProperty("actif").GetBoolean());
            Assert.Equal(id, etat.GetProperty("mepId").GetGuid());
            Assert.Equal("Alice", etat.GetProperty("lanceePar").GetString());

            // Présence : Bob et Claire sont sur la MEP, dans une section ou une étape.
            await Attendre(async () =>
            {
                var p = (await Ok(await alice.GetAsync($"/api/meps/{id}/presence"))).EnumerateArray().ToList();
                return p.Any(x => x.GetProperty("nom").GetString() == "Bob" && x.GetProperty("elements").EnumerateArray().Any(e => e.GetString()!.StartsWith("ici:", StringComparison.Ordinal)))
                    && p.Any(x => x.GetProperty("nom").GetString() == "Claire") ? p : null;
            }, "Bob et Claire présents");
            // Prise de main sur l'étape d'Alice, visible dans les verrous (qui l'avait).
            await Attendre(async () =>
            {
                var v = (await Ok(await alice.GetAsync($"/api/meps/{id}/verrous"))).EnumerateArray().ToList();
                return v.Any(x => x.GetProperty("etapeId").GetGuid() == e5 && x.GetProperty("nom").GetString() == "Claire"
                    && x.GetProperty("reprisDeNom").GetString() == "Alice") ? v : null;
            }, "Claire a pris la main sur E0005");
            // Modifications réelles, au journal normal, au nom de Bob.
            await Attendre(async () =>
            {
                var j = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().ToList();
                return j.Any(x => x.GetProperty("auteur").GetString() == "Bob" && x.GetProperty("type").GetString() == "etape.modifiee")
                    && j.Any(x => x.GetProperty("auteur").GetString() == "Bob" && x.GetProperty("type").GetString() == "etape.creee")
                    && j.Any(x => x.GetProperty("auteur").GetString() == "Bob" && x.GetProperty("type").GetString() == "etape.deplacee") ? j : null;
            }, "Bob modifie, ajoute et déplace une étape");

            etat = await Ok(await alice.PostAsJsonAsync("/api/demo/simulation", new { actif = false }));
            Assert.False(etat.GetProperty("actif").GetBoolean());
            // Plus personne de simulé : ni présence, ni verrou, ni étape de passage.
            var presences = (await Ok(await alice.GetAsync($"/api/meps/{id}/presence"))).EnumerateArray();
            Assert.DoesNotContain(presences, x => x.GetProperty("nom").GetString() is "Bob" or "Claire");
            Assert.DoesNotContain((await Ok(await alice.GetAsync($"/api/meps/{id}/verrous"))).EnumerateArray(),
                v => v.GetProperty("nom").GetString() is "Bob" or "Claire");
            var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
            Assert.DoesNotContain(mep.GetProperty("etapes").EnumerateArray(), e => e.GetProperty("action").GetString() == SimulationDemo.TitreEtapeSimulee);
            // Aucune autre MEP n'a été touchée par Bob ou Claire pendant la simulation.
            foreach (var autre in (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Where(x => x != id))
                Assert.DoesNotContain((await Ok(await alice.GetAsync($"/api/meps/{autre}/journal"))).EnumerateArray(),
                    j => j.GetProperty("detail").GetString() == SimulationDemo.TitreEtapeSimulee || j.GetProperty("type").GetString() == "etape.deplacee"
                        && j.GetProperty("auteur").GetString() == "Bob" && j.GetProperty("quand").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Demo__SimulationPasMs", null);
        }
    }

    [Fact]
    public async Task Hors_demonstration_la_simulation_n_existe_pas()
    {
        using var api = new ApiFixture(demo: false);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().PostAsJsonAsync("/api/demo/simulation", new { actif = true })).StatusCode);
    }
}
