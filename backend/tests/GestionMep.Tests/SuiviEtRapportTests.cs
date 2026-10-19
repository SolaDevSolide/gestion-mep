using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Suivi des MEP (#61) : tableau de bord « Suivi en direct » et son flux, rapport de fin figé à la clôture (ou à la première
/// demande pour une MEP clôturée avant), exports Excel du rapport (API et ligne de commande).
/// </summary>
[Collection("Api")]
public class SuiviEtRapportTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static List<string> Outils(JsonElement liste) => [.. liste.EnumerateArray().Select(m => m.GetProperty("outil").GetString()!)];

    [Fact]
    public async Task Le_tableau_de_bord_donne_les_mep_lancees_prevues_et_cloturees_recemment()
    {
        var claire = await _api.ClientConnecte("claire");
        var suivi = await Ok(await claire.GetAsync("/api/suivi"));
        Assert.Equal(
            new[] { DonneesDemo.OutilExecution, DonneesDemo.OutilRecette, DonneesDemo.OutilRetourArriere, DonneesDemo.OutilSimplifiee }.Order(),
            Outils(suivi.GetProperty("enCours")).Order());
        // MEP complètes, vues par la personne qui demande (droits compris).
        var paiement = suivi.GetProperty("enCours").EnumerateArray().Single(m => m.GetProperty("outil").GetString() == DonneesDemo.OutilExecution);
        Assert.Equal(5, paiement.GetProperty("sections").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, paiement.GetProperty("droits").GetProperty("participantId").ValueKind);
        // Annoncées demain à 20:00 : « Annuaire des agences » et la MEP de démonstration ; la prochaine est l'une d'elles.
        var prevues = Outils(suivi.GetProperty("prevues"));
        Assert.Contains(DonneesDemo.OutilPrete, prevues);
        Assert.Contains(suivi.GetProperty("prochaine").GetProperty("outil").GetString(), prevues);
        // Clôturées il y a plusieurs jours : pas dans les dernières 24 heures.
        Assert.Empty(suivi.GetProperty("cloturees").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().GetAsync("/api/suivi")).StatusCode);
    }

    [Fact]
    public void Le_flux_du_suivi_signale_toute_modification_d_une_mep()
    {
        var diffuseur = new DiffuseurMep(new ConfigurationBuilder().Build());
        using var suivi = diffuseur.AbonnerSuivi(Guid.NewGuid())!;
        using var autre = diffuseur.Abonner(Guid.NewGuid(), Guid.NewGuid())!;
        diffuseur.Notifier(Guid.NewGuid(), 12);
        diffuseur.Notifier(Guid.NewGuid(), 3);
        // Un seul réveil, avec le nombre de modifications diffusées ; l'abonné d'une autre MEP n'a rien reçu.
        Assert.Equal(2, suivi.Prendre().Version);
        Assert.Null(autre.Prendre().Version);
        Assert.Null(suivi.Prendre().Version);
    }

    [Fact]
    public async Task Le_rapport_est_fige_a_la_cloture_et_n_existe_pas_avant()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        var refus = await alice.GetAsync($"/api/meps/{id}/rapport");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.GetAsync($"/api/meps/{id}/rapport/excel")).StatusCode);

        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        foreach (var f in mep.GetProperty("fichesRecette").EnumerateArray().ToList())
            foreach (var t in f.GetProperty("etapes").EnumerateArray().Where(t => t.GetProperty("resultat").GetString() != "Conforme").ToList())
            {
                var url = $"/api/meps/{id}/fiches-recette/{f.GetProperty("id").GetGuid()}/etapes/{t.GetProperty("id").GetGuid()}";
                if (t.GetProperty("resultat").ValueKind != JsonValueKind.Null) await Ok(await alice.PostAsync($"{url}/refaire", null));
                await Ok(await alice.PostAsJsonAsync($"{url}/resultat", new { resultat = "Conforme", observation = "OK" }));
            }
        await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/cloturer", new { ceQuiAMarche = "Tout", incidents = "Rien à signaler", actions = "" }));

        // Figé à la clôture : dans la même requête, avec l'événement de clôture et le bilan.
        using (var scope = _api.Services.CreateScope())
        {
            var fige = await scope.ServiceProvider.GetRequiredService<MepDbContext>().RapportsFin.SingleAsync(r => r.MepId == id);
            Assert.False(fige.CalculeApres);
            Assert.Equal(RapportsFin.FormatActuel, fige.Format);
        }
        var bob = await _api.ClientConnecte("bob");
        var rapport = await Ok(await bob.GetAsync($"/api/meps/{id}/rapport"));
        Assert.False(rapport.GetProperty("calculeApres").GetBoolean());
        Assert.Equal("Cloturee", rapport.GetProperty("mep").GetProperty("phase").GetString());
        // Même issue que la MEP elle-même et que le tableau de bord (réussie, ou avec incidents si une étape a échoué).
        var etat = (await Ok(await bob.GetAsync($"/api/meps/{id}"))).GetProperty("etat").GetString();
        Assert.Contains(etat, new[] { "Reussie", "AvecIncidents" });
        Assert.Equal(etat, rapport.GetProperty("mep").GetProperty("etat").GetString());
        Assert.Equal("Tout", rapport.GetProperty("mep").GetProperty("bilan").GetProperty("ceQuiAMarche").GetString());
        Assert.Equal("mep.cloturee", rapport.GetProperty("journal")[0].GetProperty("type").GetString());
        var synthese = rapport.GetProperty("synthese");
        Assert.Equal(synthese.GetProperty("recetteTests").GetInt32(), synthese.GetProperty("recetteConformes").GetInt32());
        // Instantané neutre : aucun droit de la personne qui a clôturé.
        Assert.Equal(JsonValueKind.Null, rapport.GetProperty("mep").GetProperty("droits").GetProperty("participantId").ValueKind);

        // Clôturée depuis moins de 24 heures : elle apparaît au tableau de bord, et le rapport ne change plus.
        var cloturees = (await Ok(await bob.GetAsync("/api/suivi"))).GetProperty("cloturees");
        Assert.Contains(DonneesDemo.OutilRecette, Outils(cloturees));
        Assert.Equal(etat, cloturees.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id).GetProperty("etat").GetString());
        var encore = await Ok(await alice.GetAsync($"/api/meps/{id}/rapport"));
        Assert.Equal(rapport.GetProperty("figeLe").GetDateTimeOffset(), encore.GetProperty("figeLe").GetDateTimeOffset());
    }

    [Fact]
    public async Task Un_rapport_fige_sans_l_etat_de_la_mep_le_retrouve_dans_son_journal()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdMep(bob, DonneesDemo.OutilReussie);
        await Ok(await bob.GetAsync($"/api/meps/{id}/rapport"));
        // Rapport figé avant que la MEP porte son état (#91) : ni « etat » ni « echecsSurvenus » dans l'instantané.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var fige = await db.RapportsFin.SingleAsync(r => r.MepId == id);
            var contenu = System.Text.Json.Nodes.JsonNode.Parse(fige.Contenu)!;
            Assert.True(contenu["mep"]!.AsObject().Remove("etat"));
            Assert.True(contenu["mep"]!.AsObject().Remove("echecsSurvenus"));
            fige.Contenu = contenu.ToJsonString();
            await db.SaveChangesAsync();
        }
        var mep = (await Ok(await bob.GetAsync($"/api/meps/{id}/rapport"))).GetProperty("mep");
        Assert.Equal("AvecIncidents", mep.GetProperty("etat").GetString());
        Assert.Equal(1, mep.GetProperty("echecsSurvenus").GetInt32());
    }

    [Fact]
    public async Task Une_mep_cloturee_avant_les_rapports_obtient_le_sien_a_la_premiere_demande()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdMep(bob, DonneesDemo.OutilReussie);
        var rapport = await Ok(await bob.GetAsync($"/api/meps/{id}/rapport"));
        Assert.True(rapport.GetProperty("calculeApres").GetBoolean());
        var point = Assert.Single(rapport.GetProperty("pointsBloquants").EnumerateArray());
        Assert.Equal("E0003", point.GetProperty("code").GetString());
        Assert.Equal("API : réponse de test absente", point.GetProperty("resolution").GetString());
        Assert.Equal(8, point.GetProperty("arretMinutes").GetInt32());
        Assert.Equal(5, rapport.GetProperty("testsUnitaires").GetProperty("tests").GetArrayLength());
        // E0003 a échoué puis a été refaite : réussie avec incidents (#91).
        Assert.Equal("AvecIncidents", rapport.GetProperty("mep").GetProperty("etat").GetString());
        Assert.Equal(1, rapport.GetProperty("mep").GetProperty("echecsSurvenus").GetInt32());
        Assert.Equal(rapport.GetProperty("figeLe").GetDateTimeOffset(),
            (await Ok(await bob.GetAsync($"/api/meps/{id}/rapport"))).GetProperty("figeLe").GetDateTimeOffset());

        var rep = await bob.GetAsync($"/api/meps/{id}/rapport/excel");
        Assert.True(rep.IsSuccessStatusCode);
        Assert.Equal(VuesExcel.TypeContenu, rep.Content.Headers.ContentType?.MediaType);
        Assert.Equal("rapport-Gestion-des-stocks-6.3.xlsx", rep.Content.Headers.ContentDisposition?.FileNameStar);
        using var c = new XLWorkbook(new MemoryStream(await rep.Content.ReadAsByteArrayAsync()));
        Assert.Equal(["Synthèse", "Points bloquants", "Résolutions", "Tests unitaires", "Étapes", "Recette", "Journal"],
            c.Worksheets.Select(f => f.Name));
        Assert.Equal("E0003", c.Worksheet("Points bloquants").Cell(5, 1).GetString());
        Assert.Contains(c.Worksheet("Synthèse").Column(2).CellsUsed(), x => x.GetString() == "1 (dont 1 repris avec succès)");
        Assert.Equal("Clôturée : réussie avec incidents",
            c.Worksheet("Synthèse").Column(1).CellsUsed().Single(x => x.GetString() == "Issue").CellRight().GetString());
        Assert.Equal(("Exécutant", "Bob"), (c.Worksheet("Étapes").Cell(4, 4).GetString(), c.Worksheet("Étapes").Cell(7, 4).GetString()));

        // MEP repliée : l'annexe du retour arrière s'ajoute.
        var repliee = await AidesApi.IdMep(bob, DonneesDemo.OutilRepliee);
        var r2 = await bob.GetAsync($"/api/meps/{repliee}/rapport/excel");
        using var c2 = new XLWorkbook(new MemoryStream(await r2.Content.ReadAsByteArrayAsync()));
        Assert.Contains("Retour arrière", c2.Worksheets.Select(f => f.Name));
    }

    [Fact]
    public async Task La_ligne_de_commande_exporte_le_rapport_de_fin()
    {
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdMep(bob, DonneesDemo.OutilReussie);
        Directory.CreateDirectory(_api.DossierPieces);
        var fichier = Path.Combine(_api.DossierPieces, "rapport.xlsx");
        var sortie = new StringWriter();
        var avant = Console.Out;
        Console.SetOut(sortie);
        try
        {
            Assert.Equal(0, await LigneDeCommande.Executer(["meps", "excel", id.ToString()[..8], "rapport", fichier]));
            Assert.Equal(1, await LigneDeCommande.Executer(["meps", "excel", (await AidesApi.IdMep(bob, DonneesDemo.OutilExecution)).ToString(), "rapport", fichier + "2"]));
        }
        finally
        {
            Console.SetOut(avant);
        }
        Assert.Contains("Rapport de fin", sortie.ToString());
        using var c = new XLWorkbook(fichier);
        Assert.Equal("Rapport de fin · Gestion des stocks 6.3", c.Worksheet("Synthèse").Cell(1, 1).GetString());
        Assert.False(File.Exists(fichier + "2"));
    }
}
