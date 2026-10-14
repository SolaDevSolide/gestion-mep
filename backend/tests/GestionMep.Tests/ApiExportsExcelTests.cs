using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Exportations Excel des vues (#58) : liste des MEP avec ses filtres, préparation, étapes, retour arrière et journal.</summary>
[Collection("Api")]
public class ApiExportsExcelTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<XLWorkbook> Classeur(HttpClient c, string url)
    {
        var rep = await c.GetAsync(url);
        Assert.True(rep.IsSuccessStatusCode, $"{(int)rep.StatusCode} : {await rep.Content.ReadAsStringAsync()}");
        Assert.Equal(VuesExcel.TypeContenu, rep.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".xlsx", rep.Content.Headers.ContentDisposition?.FileNameStar ?? rep.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        return new XLWorkbook(new MemoryStream(await rep.Content.ReadAsByteArrayAsync()));
    }

    /// <summary>Valeurs d'une colonne du tableau (en-têtes ligne 4), jusqu'à la première ligne vide.</summary>
    private static List<string> Colonne(IXLWorksheet f, string entete)
    {
        var col = f.Row(4).CellsUsed().Single(c => c.GetString() == entete).Address.ColumnNumber;
        var valeurs = new List<string>();
        for (var l = 5; !f.Cell(l, 1).IsEmpty(); l++) valeurs.Add(f.Cell(l, col).GetFormattedString());
        return valeurs;
    }

    [Fact]
    public async Task La_liste_des_mep_s_exporte_avec_les_filtres_de_la_page()
    {
        var bob = await _api.ClientConnecte("bob");
        var toutes = (await Ok(await bob.GetAsync("/api/meps?modele=false"))).EnumerateArray().ToList();

        using (var c = await Classeur(bob, "/api/meps/excel"))
        {
            var f = c.Worksheet("MEP");
            Assert.Equal("Mises en production", f.Cell(1, 1).GetString());
            Assert.Contains("aucun filtre", f.Cell(2, 1).GetString());
            Assert.Equal(toutes.Select(m => m.GetProperty("outil").GetString()!).Order(), Colonne(f, "Outil").Order());
            Assert.True(f.AutoFilter.IsEnabled);
        }

        // Phase et recherche (sans accents ni casse, comme la page d'accueil : « electronique » trouve « électronique »).
        using (var c = await Classeur(bob, "/api/meps/excel?phase=RetourArriere&recherche=MESSAGERIE"))
        {
            var f = c.Worksheet("MEP");
            Assert.Equal([DonneesDemo.OutilRetourArriere], Colonne(f, "Outil"));
            Assert.Equal(["Retour arrière"], Colonne(f, "Phase"));
            Assert.Contains("phase Retour arrière", f.Cell(2, 1).GetString());
            Assert.Contains("recherche « MESSAGERIE »", f.Cell(2, 1).GetString());
        }
        using (var c = await Classeur(bob, "/api/meps/excel?recherche=electronique"))
            Assert.Contains(DonneesDemo.OutilIncomplete, Colonne(c.Worksheet("MEP"), "Outil"));
        using (var c = await Classeur(bob, "/api/meps/excel?recherche=rien-de-tel"))
            Assert.Contains("Aucune MEP", c.Worksheet("MEP").Cell(5, 1).GetString());

        // Modèles : leur propre onglet.
        using (var c = await Classeur(bob, "/api/meps/excel?modele=true"))
            Assert.NotEmpty(Colonne(c.Worksheet("Modèles"), "Outil"));

        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync("/api/meps/excel?phase=demain")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/meps/excel?equipe={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().GetAsync("/api/meps/excel")).StatusCode);
    }

    [Fact]
    public async Task La_preparation_s_exporte_section_par_section()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(bob);
        var mep = await Ok(await bob.GetAsync($"/api/meps/{id}"));
        using var c = await Classeur(bob, $"/api/meps/{id}/preparation/excel");
        Assert.Equal(["Intervention", "Personnes", "Systèmes", "Responsables techniques", "Objets impactés", "Sauvegarde", "Validation",
            "Communication", "Pièces jointes"], c.Worksheets.Select(f => f.Name));
        var intervention = c.Worksheet("Intervention");
        Assert.Equal("Outil", intervention.Cell(4, 1).GetString());
        Assert.Equal(DonneesDemo.OutilPortail, intervention.Cell(4, 2).GetString());
        Assert.Equal(mep.GetProperty("participants").GetArrayLength(), Colonne(c.Worksheet("Personnes"), "Nom").Count);
        Assert.Equal(mep.GetProperty("systemes").EnumerateArray().Select(s => s.GetProperty("nom").GetString()!), Colonne(c.Worksheet("Systèmes"), "Système"));
        Assert.Equal(mep.GetProperty("objetsImpactes").GetArrayLength(), Colonne(c.Worksheet("Objets impactés"), "Nom").Count);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/meps/{Guid.NewGuid()}/preparation/excel")).StatusCode);
    }

    [Fact]
    public async Task Les_etapes_et_le_retour_arriere_s_exportent_dans_l_ordre_du_graphe()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(bob);
        var mep = await Ok(await bob.GetAsync($"/api/meps/{id}"));
        string[] Codes(string plan) => mep.GetProperty("etapes").EnumerateArray().Where(e => e.GetProperty("plan").GetString() == plan)
            .OrderBy(e => e.GetProperty("ligne").GetInt32()).ThenBy(e => e.GetProperty("ordre").GetInt32())
            .Select(e => e.GetProperty("code").GetString()!).ToArray();

        using (var c = await Classeur(bob, $"/api/meps/{id}/etapes/excel"))
        {
            var f = c.Worksheet("Étapes");
            Assert.Equal(Codes("Deploiement"), Colonne(f, "Code"));
            Assert.All(Colonne(f, "État"), e => Assert.Contains(e, new[] { "À faire", "En cours", "Réussie", "Échec" }));
            Assert.Contains(Colonne(f, "Instructions"), i => i.Length > 0);
        }
        using (var c = await Classeur(bob, $"/api/meps/{id}/retour-arriere/excel"))
        {
            Assert.Equal("Fiche du plan", c.Worksheet(1).Name);
            Assert.Equal(Codes("RetourArriere"), Colonne(c.Worksheet("Étapes de retour arrière"), "Code"));
        }
    }

    [Fact]
    public async Task Le_journal_s_exporte_avec_ses_filtres_et_les_tests_unitaires()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdMep(bob, DonneesDemo.OutilReussie);
        var journal = (await Ok(await bob.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().ToList();

        using (var c = await Classeur(bob, $"/api/meps/{id}/journal/excel"))
        {
            Assert.Equal(journal.Select(e => e.GetProperty("detail").GetString()!), Colonne(c.Worksheet("Journal"), "Détail"));
            Assert.NotNull(c.Worksheet("Tests unitaires"));
        }
        var etapes = journal.Count(e => e.GetProperty("type").GetString()!.StartsWith("etape.", StringComparison.Ordinal));
        Assert.True(etapes > 0);
        using (var c = await Classeur(bob, $"/api/meps/{id}/journal/excel?type=etape"))
        {
            var f = c.Worksheet("Journal");
            Assert.Equal(etapes, Colonne(f, "Détail").Count);
            Assert.All(Colonne(f, "Type"), t => Assert.Equal("Étapes", t));
            Assert.Contains("type Étapes", f.Cell(2, 1).GetString());
        }
        var mot = journal[0].GetProperty("detail").GetString()!.Split(' ')[0];
        using (var c = await Classeur(bob, $"/api/meps/{id}/journal/excel?recherche={Uri.EscapeDataString(mot.ToUpperInvariant())}"))
            Assert.All(Colonne(c.Worksheet("Journal"), "Détail"), d => Assert.Contains(mot, d, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Les_filtres_de_la_liste_suivent_ceux_de_la_page()
    {
        MepResumeDto Mep(string outil, Phase phase, TypeMep type, params string[] personnes) => new(Guid.NewGuid(), outil, "1.0", "", null, phase,
            null, null, 0, 0, 0, false, false, DateTimeOffset.UtcNow, null, true, [], [.. personnes], type);
        var meps = new[] { Mep("Œuvre", Phase.Preparation, TypeMep.Complete, "Élodie"), Mep("Paie", Phase.Execution, TypeMep.Simplifiee) };
        Assert.Equal(["Œuvre"], VuesExcel.FiltrerMeps(meps, null, null, "oeuvre").Select(m => m.Outil));
        Assert.Equal(["Œuvre"], VuesExcel.FiltrerMeps(meps, null, null, " elodie ").Select(m => m.Outil));
        Assert.Equal(["Paie"], VuesExcel.FiltrerMeps(meps, Phase.Execution, null, null).Select(m => m.Outil));
        Assert.Empty(VuesExcel.FiltrerMeps(meps, Phase.Execution, TypeMep.Complete, null));
        Assert.Equal("approbation", VuesExcel.Famille("approbateurs.modifies"));
    }
}
