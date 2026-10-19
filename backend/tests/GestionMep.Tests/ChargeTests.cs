using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace GestionMep.Tests;

/// <summary>
/// Contrôle de charge (#96) : dix personnes travaillent en même temps sur une MEP en exécution, chacune sur son étape, en
/// relisant la MEP, son journal (nouveaux événements seulement) et le suivi comme le fait l'interface ; puis toutes sur la même
/// étape, en relisant et recommençant après un refus comme on le leur demande. Les temps de réponse sont écrits dans la sortie
/// du test (<c>dotnet test --logger "console;verbosity=detailed"</c>).
/// </summary>
[Collection("Api")]
public class ChargeTests(ITestOutputHelper sortie) : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private const int Personnes = 10;
    private const int Tours = 15;

    private static JsonElement Etape(JsonElement mep, string code) =>
        mep.GetProperty("etapes").EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);

    private sealed class Mesures
    {
        private readonly Dictionary<string, List<double>> _temps = [];
        private readonly Dictionary<string, Dictionary<int, int>> _codes = [];

        public async Task<HttpResponseMessage> Mesurer(string operation, Func<Task<HttpResponseMessage>> requete)
        {
            var chrono = Stopwatch.StartNew();
            var rep = await requete();
            await rep.Content.LoadIntoBufferAsync();
            var ms = chrono.Elapsed.TotalMilliseconds;
            lock (this)
            {
                if (!_temps.TryGetValue(operation, out var t)) _temps[operation] = t = [];
                t.Add(ms);
                if (!_codes.TryGetValue(operation, out var c)) _codes[operation] = c = [];
                c[(int)rep.StatusCode] = c.GetValueOrDefault((int)rep.StatusCode) + 1;
            }
            return rep;
        }

        public int Nombre(string operation, int code) => _codes.GetValueOrDefault(operation)?.GetValueOrDefault(code) ?? 0;
        public IEnumerable<int> Codes => _codes.Values.SelectMany(c => c.Keys);

        public IEnumerable<string> Lignes()
        {
            static double Centile(List<double> t, double p) => t.Order().ElementAt(Math.Min(t.Count - 1, (int)Math.Ceiling(p * t.Count) - 1));
            foreach (var (op, t) in _temps.OrderBy(x => x.Key))
                yield return $"{op,-28} {t.Count,4} requêtes  médiane {Centile(t, 0.5),6:0} ms  p95 {Centile(t, 0.95),6:0} ms  max {t.Max(),6:0} ms  "
                    + string.Join(" ", _codes[op].OrderBy(c => c.Key).Select(c => $"{c.Key}×{c.Value}"));
        }
    }

    [Fact]
    public async Task Dix_personnes_sur_une_mep_en_execution()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await AidesApi.Go(_api, alice, id);
        var codes = mep.GetProperty("etapes").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).Order(StringComparer.Ordinal).Take(Personnes).ToList();
        Assert.Equal(Personnes, codes.Count);
        var clients = new HttpClient[Personnes];
        for (var i = 0; i < Personnes; i++) clients[i] = await _api.ClientConnecte("alice"); // une session par personne
        var journalAvant = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).GetArrayLength();

        // 1. Chacun son étape : lecture, commentaire, nouveaux événements du journal ; le suivi de temps en temps.
        var mesures = new Mesures();
        var chrono = Stopwatch.StartNew();
        using (var depart = new SemaphoreSlim(0))
        {
            var taches = Enumerable.Range(0, Personnes).Select(async i =>
            {
                var c = clients[i];
                await depart.WaitAsync();
                long dernier = 0;
                for (var tour = 0; tour < Tours; tour++)
                {
                    var lue = await AidesApi.Ok(await mesures.Mesurer("GET /meps/{id}", () => c.GetAsync($"/api/meps/{id}")));
                    var e = Etape(lue, codes[i]);
                    await mesures.Mesurer("PUT commentaire (son étape)", () => c.PutAsJsonAsync($"/api/meps/{id}/etapes/{e.GetProperty("id").GetGuid()}/commentaire",
                        new { revision = e.GetProperty("revision").GetInt32(), commentaire = $"Personne {i}, tour {tour}" }));
                    var nouveaux = await AidesApi.Ok(await mesures.Mesurer("GET journal?apres=", () => c.GetAsync($"/api/meps/{id}/journal?apres={dernier}&limite=200")));
                    if (nouveaux.GetArrayLength() > 0) dernier = nouveaux[0].GetProperty("id").GetInt64();
                    if (tour % 5 == 0) await mesures.Mesurer("GET /suivi", () => c.GetAsync("/api/suivi"));
                }
            }).ToList();
            depart.Release(Personnes);
            await Task.WhenAll(taches);
        }
        var duree1 = chrono.Elapsed;
        // Aucun faux conflit, aucune erreur : chaque écriture est passée et journalisée.
        Assert.All(mesures.Codes, code => Assert.Equal(200, code));
        Assert.Equal(Personnes * Tours, mesures.Nombre("PUT commentaire (son étape)", 200));
        var journalApres = (await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).GetArrayLength();
        Assert.Equal(journalAvant + Personnes * Tours, journalApres);

        // 2. Tous sur la même étape : un vrai conflit à chaque fois que la révision lue est dépassée ; on relit et recommence.
        var conflits = new Mesures();
        var code0 = codes[0];
        chrono.Restart();
        using (var depart = new SemaphoreSlim(0))
        {
            var taches = Enumerable.Range(0, Personnes).Select(async i =>
            {
                var c = clients[i];
                await depart.WaitAsync();
                for (var essai = 0; essai < 50; essai++)
                {
                    var lue = await AidesApi.Ok(await c.GetAsync($"/api/meps/{id}"));
                    var e = Etape(lue, code0);
                    var rep = await conflits.Mesurer("PUT commentaire (même étape)", () => c.PutAsJsonAsync($"/api/meps/{id}/etapes/{e.GetProperty("id").GetGuid()}/commentaire",
                        new { revision = e.GetProperty("revision").GetInt32(), commentaire = $"Même étape, personne {i}" }));
                    if (rep.StatusCode == HttpStatusCode.OK) return;
                    Assert.Equal(HttpStatusCode.Conflict, rep.StatusCode);
                }
                Assert.Fail($"personne {i} : toujours en conflit après 50 essais");
            }).ToList();
            depart.Release(Personnes);
            await Task.WhenAll(taches);
        }
        var duree2 = chrono.Elapsed;
        Assert.Equal(Personnes, conflits.Nombre("PUT commentaire (même étape)", 200));

        sortie.WriteLine($"Charge #96 : {Personnes} personnes, {Tours} tours chacune, une MEP en exécution ({codes.Count} étapes)");
        sortie.WriteLine($"1. Chacun son étape : {duree1.TotalSeconds:0.0} s, {mesures.Lignes().Count()} types de requêtes");
        foreach (var l in mesures.Lignes()) sortie.WriteLine("   " + l);
        sortie.WriteLine($"2. Tous sur la même étape (relire et recommencer après 409) : {duree2.TotalSeconds:0.0} s");
        foreach (var l in conflits.Lignes()) sortie.WriteLine("   " + l);
    }
}
