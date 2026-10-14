using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Services;
using Microsoft.Extensions.Configuration;

namespace GestionMep.Tests;

/// <summary>
/// Présence en temps réel : qui a une MEP ouverte et sur quel élément il travaille. Elle vit avec le flux SSE de l'onglet
/// (fermer l'onglet la retire) et ne verrouille rien.
/// </summary>
public class PresenceTests
{
    private static DiffuseurMep Diffuseur() => new(new ConfigurationBuilder().Build());

    // ---------- Diffuseur (sans HTTP) ----------
    [Fact]
    public void La_presence_suit_les_flux_ouverts_et_regroupe_les_onglets_d_une_personne()
    {
        var diffuseur = Diffuseur();
        Guid mep = Guid.NewGuid(), autre = Guid.NewGuid(), bob = Guid.NewGuid(), claire = Guid.NewGuid();
        using var b1 = diffuseur.Abonner(mep, bob, "Bob Martin", "onglet-1")!;
        using var b2 = diffuseur.Abonner(mep, bob, "Bob Martin", "onglet-2")!;
        var c1 = diffuseur.Abonner(mep, claire, "Claire Petit", "onglet-c")!;
        using var ailleurs = diffuseur.Abonner(autre, claire, "Claire Petit", "onglet-d")!;

        Assert.True(diffuseur.DefinirElements(mep, bob, "onglet-1", ["etape:e1"]));
        Assert.True(diffuseur.DefinirElements(mep, bob, "onglet-2", ["preparation:intervention", "etape:e1"]));
        var presences = diffuseur.Presences(mep);
        Assert.Equal(["Bob Martin", "Claire Petit"], presences.Select(p => p.Nom));
        Assert.Equal(["etape:e1", "preparation:intervention"], presences[0].Elements);
        Assert.Empty(presences[1].Elements);

        // Un onglet ne déclare que pour lui : autre compte, autre MEP ou onglet inconnu sont sans effet.
        Assert.False(diffuseur.DefinirElements(mep, claire, "onglet-1", ["etape:e2"]));
        Assert.False(diffuseur.DefinirElements(autre, bob, "onglet-1", ["etape:e2"]));
        Assert.False(diffuseur.DefinirElements(mep, bob, "inconnu", ["etape:e2"]));
        Assert.DoesNotContain("etape:e2", diffuseur.Presences(mep).SelectMany(p => p.Elements));

        // Fermer un onglet retire ses éléments ; fermer le dernier retire la personne.
        b2.Dispose();
        Assert.Equal(["etape:e1"], diffuseur.Presences(mep)[0].Elements);
        c1.Dispose();
        Assert.Equal(["Bob Martin"], diffuseur.Presences(mep).Select(p => p.Nom));
        Assert.Equal(["Claire Petit"], diffuseur.Presences(autre).Select(p => p.Nom));
    }

    [Fact]
    public async Task Chaque_changement_de_presence_reveille_les_flux_de_la_MEP()
    {
        var diffuseur = Diffuseur();
        Guid mep = Guid.NewGuid(), bob = Guid.NewGuid(), claire = Guid.NewGuid();
        using var b = diffuseur.Abonner(mep, bob, "Bob", "b")!;
        using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await b.AttendreAsync(delai.Token));
        Assert.Equal(new DiffuseurMep.Envoi(null, true), b.Prendre()); // sa propre arrivée : première présence à envoyer

        using var c = diffuseur.Abonner(mep, claire, "Claire", "c")!;
        diffuseur.DefinirElements(mep, claire, "c", ["etape:e3"]);
        diffuseur.Notifier(mep, 7);
        diffuseur.Notifier(mep, 6); // une version plus ancienne n'efface pas la plus récente
        Assert.True(await b.AttendreAsync(delai.Token));
        Assert.Equal(new DiffuseurMep.Envoi(7, true), b.Prendre());
        Assert.Equal(new DiffuseurMep.Envoi(null, false), b.Prendre());

        // Déclarer les mêmes éléments ne réveille personne.
        diffuseur.DefinirElements(mep, claire, "c", ["etape:e3"]);
        Assert.Equal(new DiffuseurMep.Envoi(null, false), b.Prendre());
    }

    [Theory]
    [InlineData("etape e1")]
    [InlineData("<script>")]
    [InlineData("é")]
    public void Une_cle_d_element_invalide_est_refusee(string cle)
    {
        var diffuseur = Diffuseur();
        Guid mep = Guid.NewGuid(), bob = Guid.NewGuid();
        using var b = diffuseur.Abonner(mep, bob, "Bob", "b")!;
        var e = Assert.Throws<ErreurHttp>(() => diffuseur.DefinirElements(mep, bob, "b", [cle]));
        Assert.Equal(400, e.Statut);
        Assert.Throws<ErreurHttp>(() => diffuseur.DefinirElements(mep, bob, "b", [.. Enumerable.Range(0, DiffuseurMep.MaxElements + 1).Select(i => $"etape:{i}")]));
        Assert.Throws<ErreurHttp>(() => diffuseur.DefinirElements(mep, bob, "b", [new string('a', DiffuseurMep.LongueurMaxElement + 1)]));
    }

}

/// <summary>Présence par l'API : flux SSE (événement <c>presence</c>), <c>GET</c> et <c>PUT /api/meps/{id}/presence</c>.</summary>
[Collection("Api")]
public class PresenceApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    /// <summary>Lit le flux jusqu'au prochain événement du type donné et renvoie ses données.</summary>
    private static async Task<string> ProchainEvenement(StreamReader lecteur, string type, CancellationToken ct)
    {
        while (true)
        {
            var ligne = await lecteur.ReadLineAsync(ct) ?? throw new InvalidOperationException("Flux fermé.");
            if (ligne != $"event: {type}") continue;
            var donnees = await lecteur.ReadLineAsync(ct);
            return donnees!["data: ".Length..];
        }
    }

    private static async Task<(HttpResponseMessage Reponse, StreamReader Lecteur)> OuvrirFlux(HttpClient c, Guid id, string session)
    {
        var requete = new HttpRequestMessage(HttpMethod.Get, $"/api/meps/{id}/flux?session={session}");
        var reponse = await c.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (reponse, new StreamReader(await reponse.Content.ReadAsStreamAsync()));
    }

    private static List<(string Nom, List<string> Elements)> Lire(string json) =>
        [.. JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(p => (p.GetProperty("nom").GetString()!, p.GetProperty("elements").EnumerateArray().Select(e => e.GetString()!).ToList()))];

    [Fact]
    public async Task Deux_personnes_voient_l_element_que_l_autre_modifie_sans_recharger()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (repAlice, fluxAlice) = await OuvrirFlux(alice, id, "onglet-alice");
        using var _ = repAlice;
        // Première présence reçue à l'ouverture : Alice seule.
        Assert.Single(Lire(await ProchainEvenement(fluxAlice, "presence", delai.Token)));

        var (repBob, fluxBob) = await OuvrirFlux(bob, id, "onglet-bob");
        var arrivee = Lire(await ProchainEvenement(fluxAlice, "presence", delai.Token));
        Assert.Equal(2, arrivee.Count);

        var mep = await AidesApi.Ok(await bob.GetAsync($"/api/meps/{id}"));
        var etape = mep.GetProperty("etapes")[0].GetProperty("id").GetGuid();
        var rep = await bob.PutAsJsonAsync($"/api/meps/{id}/presence", new { session = "onglet-bob", elements = new[] { $"etape:{etape}" } });
        Assert.Equal(HttpStatusCode.NoContent, rep.StatusCode);
        var presences = Lire(await ProchainEvenement(fluxAlice, "presence", delai.Token));
        var nomBob = (await AidesApi.Ok(await bob.GetAsync("/api/auth/moi"))).GetProperty("nomAffiche").GetString();
        Assert.Contains(presences, p => p.Nom == nomBob && p.Elements.SequenceEqual([$"etape:{etape}"]));

        // Même vue par GET.
        var lues = await AidesApi.Ok(await alice.GetAsync($"/api/meps/{id}/presence"));
        Assert.Equal(2, lues.GetArrayLength());
        Assert.Contains(lues.EnumerateArray(), p => p.GetProperty("elements").EnumerateArray().Any(e => e.GetString() == $"etape:{etape}"));

        // Fermer l'onglet de Bob le retire tout seul.
        fluxBob.Dispose();
        repBob.Dispose();
        List<(string Nom, List<string> Elements)> apres;
        do apres = Lire(await ProchainEvenement(fluxAlice, "presence", delai.Token));
        while (apres.Count != 1);
        Assert.NotEqual(nomBob, apres[0].Nom);
    }

    [Fact]
    public async Task Declarer_sans_flux_ouvert_ou_pour_l_onglet_d_un_autre_est_refuse()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        var corps = new { session = "onglet-alice", elements = new[] { "preparation:intervention" } };
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsJsonAsync($"/api/meps/{id}/presence", corps)).StatusCode);

        var (rep, flux) = await OuvrirFlux(alice, id, "onglet-alice");
        using var _ = rep;
        using var __ = flux;
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/meps/{id}/presence", corps)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}/presence", new { session = "", elements = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/meps/{id}/presence", new { session = "onglet-alice", elements = new[] { "pas valide" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync($"/api/meps/{id}/presence", corps)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/meps/{Guid.NewGuid()}/presence")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().GetAsync($"/api/meps/{id}/presence")).StatusCode);
    }
}
