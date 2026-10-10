using System.Net.Http.Json;
using System.Text.Json;

namespace GestionMep.Tests;

/// <summary>Modèles de MEP : création directe et vérification du plan adaptée (ni échéance, ni pilote, ni relecture, ni approbations).</summary>
[Collection("Api")]
public class ModelesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static List<string> Liste(JsonElement mep, string champ) => mep.GetProperty(champ).EnumerateArray().Select(e => e.GetString()!).ToList();

    [Fact]
    public async Task Un_modele_se_cree_directement_et_n_exige_ni_echeance_ni_pilote_ni_validation()
    {
        var bob = await _api.ClientConnecte("bob");
        var modele = await AidesApi.Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Portail", modele = true }));
        Assert.True(modele.GetProperty("estModele").GetBoolean());
        var erreurs = Liste(modele, "erreursPlan");
        Assert.DoesNotContain(erreurs, e => e.Contains("échéance") || e.Contains("pilote") || e.Contains("relue") || e.Contains("approbation"));
        Assert.Contains("Ajouter au moins une étape de déploiement.", erreurs);

        var modeles = await AidesApi.Ok(await bob.GetAsync("/api/meps?modele=true"));
        Assert.Contains(modeles.EnumerateArray(), m => m.GetProperty("id").GetGuid() == modele.GetProperty("id").GetGuid());
        var journal = await AidesApi.Ok(await bob.GetAsync($"/api/meps/{modele.GetProperty("id").GetGuid()}/journal"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("detail").GetString() == "Modèle créé : Portail");

        // Une MEP créée sans le drapeau reste une MEP, avec ses exigences habituelles.
        var mep = await AidesApi.Ok(await bob.PostAsJsonAsync("/api/meps", new { outil = "Portail" }));
        Assert.False(mep.GetProperty("estModele").GetBoolean());
        Assert.Contains("Renseigner le pilote.", Liste(mep, "erreursPlan"));
    }
}
