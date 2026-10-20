using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using GestionMep.Api.Cli;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Recherche avancée (#59) sur la démonstration : liste des MEP enrichie et filtrée par l'API, historique d'un système du
/// catalogue et activité du catalogue, exports Excel (liste filtrée avec ses échecs, historique) et ligne de commande.
/// </summary>
[Collection("Api")]
public class ApiRechercheTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static List<string> Outils(JsonElement liste) => [.. liste.EnumerateArray().Select(m => m.GetProperty("outil").GetString()!)];

    private static async Task<Guid> Systeme(HttpClient c, string nom) =>
        (await Ok(await c.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

    private static async Task<Guid> Compte(HttpClient c, string login) =>
        (await Ok(await c.GetAsync("/api/utilisateurs"))).EnumerateArray().Single(u => u.GetProperty("login").GetString() == login).GetProperty("id").GetGuid();

    private static async Task<(XLWorkbook Classeur, string Nom)> Classeur(HttpClient c, string url)
    {
        var rep = await c.GetAsync(url);
        Assert.True(rep.IsSuccessStatusCode, $"{(int)rep.StatusCode} : {await rep.Content.ReadAsStringAsync()}");
        var nom = rep.Content.Headers.ContentDisposition?.FileNameStar ?? rep.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "";
        return (new XLWorkbook(new MemoryStream(await rep.Content.ReadAsByteArrayAsync())), nom);
    }

    private static List<string> Colonne(IXLWorksheet f, string entete)
    {
        var col = f.Row(4).CellsUsed().Single(c => c.GetString() == entete).Address.ColumnNumber;
        var valeurs = new List<string>();
        for (var l = 5; !f.Cell(l, 1).IsEmpty(); l++) valeurs.Add(f.Cell(l, col).GetFormattedString());
        return valeurs;
    }

    [Fact]
    public async Task La_liste_donne_l_etat_les_systemes_et_les_roles_de_chaque_mep()
    {
        var bob = await _api.ClientConnecte("bob");
        var liste = (await Ok(await bob.GetAsync("/api/meps?modele=false"))).EnumerateArray().ToList();
        JsonElement Mep(string outil) => liste.Single(m => m.GetProperty("outil").GetString() == outil);

        var batch = Mep(DonneesDemo.OutilHistoriqueIncidents);
        Assert.Equal("AvecIncidents", batch.GetProperty("etat").GetString());
        Assert.Equal(1, batch.GetProperty("echecsSurvenus").GetInt32());
        Assert.Equal(0, batch.GetProperty("nbEchecs").GetInt32()); // l'étape reprise a réussi : son échec reste dans le journal
        var bdd = batch.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == "BDD");
        Assert.Equal(1, bdd.GetProperty("echecs").GetInt32());
        Assert.Equal("Production", bdd.GetProperty("environnements")[0].GetProperty("nom").GetString());
        var alice = batch.GetProperty("intervenants").EnumerateArray().Single(i => i.GetProperty("nom").GetString() == "Alice");
        Assert.Equal(["Pilote", "Executant"], alice.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!));
        var claire = batch.GetProperty("intervenants").EnumerateArray().Single(i => i.GetProperty("nom").GetString() == "Claire");
        Assert.Equal(["Approbateur"], claire.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!));
        // Jour : celui du Go ; sans Go, celui de la création.
        Assert.Equal(DateOnly.FromDateTime(batch.GetProperty("go").GetDateTimeOffset().UtcDateTime).ToString("yyyy-MM-dd"), batch.GetProperty("jour").GetString());
        var portail = Mep(DonneesDemo.OutilPortail);
        Assert.Equal(JsonValueKind.Null, portail.GetProperty("go").ValueKind);
        Assert.Equal("Preparation", portail.GetProperty("etat").GetString());
        Assert.Equal("Repliee", Mep(DonneesDemo.OutilHistoriqueRepliee).GetProperty("etat").GetString());
        Assert.Equal("AvecIncidents", Mep(DonneesDemo.OutilReussie).GetProperty("etat").GetString());
        // Les plus récentes d'abord.
        var jours = liste.Select(m => m.GetProperty("jour").GetString()!).ToList();
        Assert.Equal(jours.OrderDescending(StringComparer.Ordinal), jours);
    }

    [Fact]
    public async Task L_api_filtre_comme_la_page_d_accueil()
    {
        var bob = await _api.ClientConnecte("bob");
        var bdd = await Systeme(bob, "BDD");
        var idBob = await Compte(bob, "bob");

        var surBdd = Outils(await Ok(await bob.GetAsync($"/api/meps?modele=false&systeme={bdd}")));
        Assert.Contains(DonneesDemo.OutilHistoriqueIncidents, surBdd);
        Assert.DoesNotContain(DonneesDemo.OutilHistoriqueReussie, surBdd);
        // Échec survenu sur BDD : la MEP de Gestion des stocks a échoué sur API, pas sur BDD.
        var echecsBdd = Outils(await Ok(await bob.GetAsync($"/api/meps?modele=false&systeme={bdd}&echec=1")));
        Assert.Contains(DonneesDemo.OutilHistoriqueIncidents, echecsBdd);
        Assert.DoesNotContain(DonneesDemo.OutilReussie, echecsBdd);
        Assert.Equal(new[] { DonneesDemo.OutilHistoriqueIncidents, DonneesDemo.OutilReussie }.Order(),
            Outils(await Ok(await bob.GetAsync("/api/meps?etat=incidents"))).Order());
        var pilote = Outils(await Ok(await bob.GetAsync($"/api/meps?modele=false&personne={idBob}&role=pilote")));
        Assert.Contains(DonneesDemo.OutilRepliee, pilote);
        Assert.Contains(DonneesDemo.OutilHistoriqueRepliee, pilote);
        Assert.DoesNotContain(DonneesDemo.OutilHistoriqueIncidents, pilote);
        Assert.Empty((await Ok(await bob.GetAsync("/api/meps?du=2000-01-01&au=2000-12-31"))).EnumerateArray());
        Assert.Contains(DonneesDemo.OutilHistoriqueReussie, Outils(await Ok(await bob.GetAsync("/api/meps?recherche=portail&etat=reussie"))));

        var refus = await bob.GetAsync("/api/meps?etat=abandonnee");
        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        Assert.Contains("État « abandonnee » inconnu", await refus.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task L_historique_d_un_systeme_donne_ses_mep_leur_issue_et_leurs_echecs()
    {
        var bob = await _api.ClientConnecte("bob");
        var bdd = await Systeme(bob, "BDD");
        var h = await Ok(await bob.GetAsync($"/api/catalogue/systemes/{bdd}/historique"));
        Assert.Equal("BDD", h.GetProperty("nom").GetString());
        var meps = h.GetProperty("meps").EnumerateArray().ToList();
        var r = h.GetProperty("resume");
        Assert.Equal(meps.Count, r.GetProperty("total").GetInt32());
        Assert.Equal(meps.Count(m => m.GetProperty("etat").GetString() == "AvecIncidents"), r.GetProperty("avecIncidents").GetInt32());
        Assert.DoesNotContain(meps, m => m.GetProperty("outil").GetString()!.StartsWith("Modèle", StringComparison.Ordinal));
        var batch = meps.Single(m => m.GetProperty("outil").GetString() == DonneesDemo.OutilHistoriqueIncidents);
        var echec = Assert.Single(batch.GetProperty("echecs").EnumerateArray());
        Assert.Equal("E0002", echec.GetProperty("code").GetString());
        Assert.True(echec.GetProperty("repris").GetBoolean());
        Assert.Contains("verrou", echec.GetProperty("motif").GetString());
        Assert.Equal(["BATCH"], batch.GetProperty("autresSystemes").EnumerateArray().Select(s => s.GetProperty("nom").GetString()!));
        Assert.True(batch.GetProperty("rapport").GetBoolean());
        Assert.True(r.GetProperty("echecsRepris").GetInt32() >= 1);
        // Dernière MEP : celle dont le Go est le plus récent.
        var derniere = meps.Where(m => m.GetProperty("go").ValueKind != JsonValueKind.Null).MaxBy(m => m.GetProperty("go").GetDateTimeOffset());
        Assert.Equal(derniere!.GetProperty("id").GetGuid(), r.GetProperty("derniereMepId").GetGuid());

        var activite = (await Ok(await bob.GetAsync("/api/catalogue/systemes/activite"))).EnumerateArray().ToList();
        var a = activite.Single(x => x.GetProperty("systemeId").GetGuid() == bdd);
        Assert.Equal(meps.Count, a.GetProperty("nbMep").GetInt32());
        Assert.Equal(derniere.GetProperty("id").GetGuid(), a.GetProperty("derniereMepId").GetGuid());
        Assert.Equal(6, activite.Count);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/catalogue/systemes/{Guid.NewGuid()}/historique")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().GetAsync($"/api/catalogue/systemes/{bdd}/historique")).StatusCode);
    }

    [Fact]
    public async Task Les_exports_excel_reprennent_la_recherche_et_l_historique()
    {
        var bob = await _api.ClientConnecte("bob");
        var bdd = await Systeme(bob, "BDD");
        var idAlice = await Compte(bob, "alice");

        var (liste, nom) = await Classeur(bob, $"/api/meps/excel?systeme={bdd}&echec=1&personne={idAlice}");
        Assert.StartsWith("meps-BDD-", nom, StringComparison.Ordinal);
        using (liste)
        {
            var f = liste.Worksheet("Mises en production");
            Assert.Contains("système BDD", f.Cell(2, 1).GetString());
            Assert.Contains("personne Alice", f.Cell(2, 1).GetString());
            Assert.Contains(DonneesDemo.OutilHistoriqueIncidents, Colonne(f, "Outil"));
            var i = Colonne(f, "Outil").IndexOf(DonneesDemo.OutilHistoriqueIncidents);
            Assert.Equal("Réussie avec incidents", Colonne(f, "État")[i]);
            Assert.Equal("Pilote, Exécutant", Colonne(f, "Rôle de Alice")[i]);
            Assert.Contains("BDD : Production", Colonne(f, "Systèmes et environnements")[i]);
            var echecs = liste.Worksheet("Échecs");
            Assert.All(Colonne(echecs, "Systèmes"), s => Assert.Contains("BDD", s));
            Assert.Contains("verrou sur la table factures pendant 6 minutes", string.Join("|", Colonne(echecs, "Motif")));
        }

        var (historique, nomH) = await Classeur(bob, $"/api/catalogue/systemes/{bdd}/historique/excel");
        Assert.StartsWith("historique-BDD-", nomH, StringComparison.Ordinal);
        using (historique)
        {
            Assert.Equal(["Synthèse", "MEP", "Échecs"], historique.Worksheets.Select(w => w.Name));
            Assert.Contains(DonneesDemo.OutilHistoriqueIncidents, Colonne(historique.Worksheet("MEP"), "Outil"));
            Assert.Contains("E0002", Colonne(historique.Worksheet("Échecs"), "Étape"));
        }
    }

    private record Resultat(int Code, string Sortie, string Erreur);

    private static async Task<Resultat> Cli(params string[] args)
    {
        var sortie = new StringWriter();
        var erreur = new StringWriter();
        var (avant, avantErreur) = (Console.Out, Console.Error);
        Console.SetOut(sortie);
        Console.SetError(erreur);
        try
        {
            return new Resultat(await LigneDeCommande.Executer(args), sortie.ToString(), erreur.ToString());
        }
        finally
        {
            Console.SetOut(avant);
            Console.SetError(avantErreur);
        }
    }

    [Fact]
    public async Task La_ligne_de_commande_cherche_et_donne_l_historique()
    {
        var (code, sortie, _) = await Cli("meps", "lister", "--systeme", "bdd", "--echec", "--personne", "alice", "--role", "pilote", "--json");
        Assert.Equal(0, code);
        var meps = JsonDocument.Parse(sortie).RootElement.EnumerateArray().Select(m => m.GetProperty("outil").GetString()!).ToList();
        // Portail RH : migration réparée par une résolution type ; Archivage des courriers : restauration en échec, Alice pilote en
        // secours ; Batch de nuit comptable : migration reprise, il y a sept semaines.
        Assert.Equal([DonneesDemo.OutilRecette, DonneesDemo.OutilEchec, DonneesDemo.OutilHistoriqueIncidents], meps);

        (code, sortie, _) = await Cli("meps", "lister", "--etat", "repliee", "--du", "2000-01-01");
        Assert.Equal(0, code);
        Assert.Contains(DonneesDemo.OutilHistoriqueRepliee, sortie);
        Assert.Contains("Après retour arrière", sortie);

        (code, sortie, _) = await Cli("systemes", "historique", "BDD");
        Assert.Equal(0, code);
        Assert.StartsWith("BDD : ", sortie, StringComparison.Ordinal);
        Assert.Contains("E0002 Migration des tables de facturation : Erreur fictive : verrou", sortie);

        (code, sortie, _) = await Cli("systemes", "historique", "BDD", "--json");
        Assert.Equal(0, code);
        Assert.Equal("BDD", JsonDocument.Parse(sortie).RootElement.GetProperty("nom").GetString());

        var fichier = Path.Combine(_api.DossierPieces, "historique.xlsx");
        Directory.CreateDirectory(_api.DossierPieces);
        (code, sortie, _) = await Cli("systemes", "historique", "BDD", "--excel", fichier);
        Assert.Equal(0, code);
        using (var c = new XLWorkbook(fichier)) Assert.Equal("Synthèse", c.Worksheets.First().Name);

        (code, _, var erreur) = await Cli("meps", "lister", "--role", "chef");
        Assert.NotEqual(0, code);
        Assert.Contains("Rôle « chef » inconnu", erreur);
        (code, _, erreur) = await Cli("systemes", "historique", "INCONNU");
        Assert.NotEqual(0, code);
        Assert.Contains("introuvable", erreur);
    }
}
