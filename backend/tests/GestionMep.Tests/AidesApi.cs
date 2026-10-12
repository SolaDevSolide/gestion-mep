using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Aides communes aux tests d'API depuis le jalon 3 (approbation et décision Go).</summary>
public static class AidesApi
{
    public static async Task<JsonElement> Ok(HttpResponseMessage rep)
    {
        var texte = await rep.Content.ReadAsStringAsync();
        Assert.True(rep.IsSuccessStatusCode, $"{(int)rep.StatusCode} : {texte}");
        return JsonDocument.Parse(texte).RootElement.Clone();
    }

    /// <summary>Identifiant de la MEP « Portail de démonstration » (le modèle et les autres MEP de démonstration sont écartés).</summary>
    public static async Task<Guid> IdDemo(HttpClient c) => await IdMep(c, Domain.DonneesDemo.OutilPortail);

    /// <summary>Identifiant d'une MEP (hors modèles) d'après son outil.</summary>
    public static async Task<Guid> IdMep(HttpClient c, string outil) =>
        (await Ok(await c.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single(m => m.GetProperty("outil").GetString() == outil).GetProperty("id").GetGuid();

    public static async Task<List<Guid>> CriteresActifs(HttpClient c) =>
        (await Ok(await c.GetAsync("/api/criteres-go-no-go"))).EnumerateArray()
            .Where(x => x.GetProperty("actif").GetBoolean()).Select(x => x.GetProperty("id").GetGuid()).ToList();

    /// <summary>Claire approuve la MEP (version courante).</summary>
    public static async Task<JsonElement> Approuver(ApiFixture api, Guid id)
    {
        var claire = await api.ClientConnecte("claire");
        var mep = await Ok(await claire.GetAsync($"/api/meps/{id}"));
        return await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/approbation",
            new { version = mep.GetProperty("version").GetInt32(), decision = "Approuvee", commentaire = "" }));
    }

    /// <summary>Approbation par Claire puis décision Go par le client donné (tous les critères actifs cochés).</summary>
    public static async Task<JsonElement> Go(ApiFixture api, HttpClient pilote, Guid id)
    {
        await Approuver(api, id);
        return await Ok(await pilote.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = await CriteresActifs(pilote) }));
    }
}
