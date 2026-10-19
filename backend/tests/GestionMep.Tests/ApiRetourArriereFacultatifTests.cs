using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>API du plan de retour arrière facultatif (issue #49) : activation, journal, Go sans retour arrière ni échéance.</summary>
[Collection("Api")]
public class ApiRetourArriereFacultatifTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    /// <summary>Corps de <c>PUT /meps/{id}</c> reprenant l'état actuel, avec l'échéance et le choix donnés.</summary>
    private static object Identite(JsonElement mep, DateTimeOffset? echeance, bool? retourArriereActive) => new
    {
        version = mep.GetProperty("version").GetInt32(), outil = mep.GetProperty("outil").GetString(),
        versionActuelle = mep.GetProperty("versionActuelle").GetString(), versionCible = mep.GetProperty("versionCible").GetString(),
        objet = mep.GetProperty("objet").GetString(), impacts = mep.GetProperty("impacts").GetString(),
        fuseauHoraire = mep.GetProperty("fuseauHoraire").GetString(), indisponibiliteMinutes = mep.GetProperty("indisponibiliteMinutes"),
        piloteId = mep.GetProperty("piloteId").GetGuid(),
        suppleantIds = mep.GetProperty("suppleantIds").EnumerateArray().Select(s => s.GetGuid()).ToArray(),
        echeanceRepli = echeance, retourArriereActive,
    };

    private static async Task<List<(string Type, string Auteur, string Detail)>> Journal(HttpClient c, Guid id) =>
        (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray()
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("auteur").GetString()!, e.GetProperty("detail").GetString()!)).ToList();

    private static List<string> Erreurs(JsonElement mep) => mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToList();

    [Fact]
    public async Task Desactiver_puis_reactiver_est_journalise_et_le_go_se_passe_de_retour_arriere()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.True(mep.GetProperty("retourArriereActive").GetBoolean());
        var resume = (await Ok(await alice.GetAsync("/api/meps"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
        Assert.True(resume.GetProperty("retourArriereActive").GetBoolean());

        // Désactivation : un événement de journal dédié, et rien n'est plus réclamé pour le retour arrière. L'échéance envoyée
        // (null) est ignorée : elle se change par PUT /meps/{id}/echeance (#94) ; sans plan de retour arrière, elle ne s'applique pas.
        var echeance = mep.GetProperty("echeanceRepli").GetDateTimeOffset();
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, null, false)));
        Assert.False(mep.GetProperty("retourArriereActive").GetBoolean());
        Assert.Equal(echeance, mep.GetProperty("echeanceRepli").GetDateTimeOffset());
        Assert.False(mep.GetProperty("droits").GetProperty("peutModifierEcheance").GetBoolean());
        Assert.DoesNotContain(Erreurs(mep), e => e.Contains("retour arrière", StringComparison.OrdinalIgnoreCase));
        var journal = await Journal(alice, id);
        Assert.Contains(journal.Take(2), e => e == ("repli.desactive", "Alice", "Plan de retour arrière désactivé : ni plan ni échéance réclamés, aucun déclenchement"));
        Assert.Contains(journal.Take(2), e => e.Type == "procedure.reinitialisee");
        Assert.DoesNotContain(journal, e => e.Type == "mep.modifiee");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PutAsJsonAsync($"/api/meps/{id}/echeance",
            new { version = mep.GetProperty("version").GetInt32(), echeanceRepli = (DateTimeOffset?)null, motif = "Sans retour arrière" })).StatusCode);

        // Clé absente : inchangé.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, null, null)));
        Assert.False(mep.GetProperty("retourArriereActive").GetBoolean());

        // Réactivation : de nouveau tracée ; l'échéance reste facultative.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, null, true)));
        Assert.True(mep.GetProperty("retourArriereActive").GetBoolean());
        Assert.DoesNotContain(Erreurs(mep), e => e.Contains("échéance", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("repli.active", (await Journal(alice, id))[0].Type);

        // Désactivé de nouveau, puis Go sans retour arrière ni échéance : aucun déclenchement possible.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, null, false)));
        mep = await AidesApi.Go(_api, alice, id);
        Assert.Equal("Execution", mep.GetProperty("phase").GetString());
        var repli = await alice.PostAsJsonAsync($"/api/meps/{id}/retour-arriere", new { motif = "Incident" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repli.StatusCode);
        var message = JsonDocument.Parse(await repli.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString();
        Assert.Equal("Cette MEP n'a pas de plan de retour arrière : tout problème se corrige en avant.", message);

        // Après le Go, le choix ne change plus.
        mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PutAsJsonAsync($"/api/meps/{id}", Identite(mep, null, true))).StatusCode);
    }

    [Fact]
    public async Task Autre_modification_et_activation_donnent_deux_evenements()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var echeance = mep.GetProperty("echeanceRepli").GetDateTimeOffset();
        var corps = JsonSerializer.SerializeToNode(Identite(mep, echeance.AddHours(1), false))!;
        corps["objet"] = "Objet revu";
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}", corps));
        Assert.Equal(echeance, mep.GetProperty("echeanceRepli").GetDateTimeOffset()); // échéance : ignorée ici
        var journal = await Journal(alice, id);
        Assert.Contains(journal, e => e.Type == "repli.desactive");
        Assert.Contains(journal, e => e.Type == "mep.modifiee" && e.Detail == "MEP modifiée : objet");
    }
}
