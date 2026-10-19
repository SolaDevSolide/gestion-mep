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

    /// <summary>Suppression définitive d'une MEP (administrateur) : motif et nom de la MEP saisi pour confirmer, dans le corps.</summary>
    public static Task<HttpResponseMessage> Supprimer(HttpClient c, Guid id, string nom, string motif = "Créée par erreur") =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/meps/{id}") { Content = JsonContent.Create(new { motif, nom }) });

    /// <summary>
    /// Ajoute Claire comme responsable technique du système WEB (la démonstration ne l'en fait plus : elle approuve, #94).
    /// Elle ne peut alors plus approuver la MEP.
    /// </summary>
    public static async Task<JsonElement> ClaireResponsableTechnique(HttpClient editeur, Guid id)
    {
        var mep = await Ok(await editeur.GetAsync($"/api/meps/{id}"));
        Guid Participant(string nom) => mep.GetProperty("participants").EnumerateArray().Single(p => p.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
        var web = mep.GetProperty("systemes").EnumerateArray().Single(x => x.GetProperty("nom").GetString() == "WEB").GetProperty("id").GetGuid();
        var responsables = mep.GetProperty("responsablesTechniques").EnumerateArray()
            .Select(r => (object)new
            {
                participantId = r.GetProperty("participantId").GetGuid(),
                systemeIds = r.GetProperty("systemeIds").EnumerateArray().Select(x => x.GetGuid()).Where(x => x != web).ToList(),
                perimetre = r.GetProperty("perimetre").GetString(), disponibilite = r.GetProperty("disponibilite").GetString(),
                contactJourJ = r.GetProperty("contactJourJ").GetString(),
            })
            .Append(new { participantId = Participant("Claire"), systemeIds = new List<Guid> { web }, perimetre = "Portail client", disponibilite = "Joignable", contactJourJ = "Fictif — poste C" })
            .ToList();
        return await Ok(await editeur.PutAsJsonAsync($"/api/meps/{id}/responsables-techniques", new { version = mep.GetProperty("version").GetInt32(), responsables }));
    }

    /// <summary>
    /// Bob relit la procédure si elle n'est pas (ou plus) relue : toute modification du plan efface la relecture (#94).
    /// </summary>
    public static async Task Relire(ApiFixture api, Guid id)
    {
        var bob = await api.ClientConnecte("bob");
        var mep = await Ok(await bob.GetAsync($"/api/meps/{id}"));
        if (mep.GetProperty("procedureRelue").ValueKind == JsonValueKind.Null)
            await Ok(await bob.PostAsJsonAsync($"/api/meps/{id}/procedure-relue", new { relue = true }));
    }

    /// <summary>Claire approuve la MEP (version courante), après avoir fait relire la procédure si besoin.</summary>
    public static async Task<JsonElement> Approuver(ApiFixture api, Guid id)
    {
        await Relire(api, id);
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
