using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;

namespace GestionMep.Tests;

/// <summary>Fiche des systèmes du catalogue, environnements et objets impactés d'une MEP, Excel des objets, par l'API (#54).</summary>
[Collection("Api")]
public class ApiObjetsImpactesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<string> Message(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    private static JsonElement Systeme(JsonElement mep, string nom) =>
        mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom);
    private static Guid SystemeId(JsonElement mep, string nom) => Systeme(mep, nom).GetProperty("id").GetGuid();
    private static string[] Environnements(JsonElement systeme) =>
        systeme.GetProperty("environnements").EnumerateArray().Select(e => e.GetProperty("nom").GetString()!).ToArray();
    private static string[] Erreurs(JsonElement mep) => mep.GetProperty("erreursPlan").EnumerateArray().Select(e => e.GetString()!).ToArray();
    private static JsonElement[] Objets(JsonElement mep, Guid systeme) =>
        mep.GetProperty("objetsImpactes").EnumerateArray().Where(o => o.GetProperty("systemeId").GetGuid() == systeme).ToArray();

    private static async Task<JsonElement> FicheCatalogue(HttpClient c, string nom) =>
        (await Ok(await c.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom);

    private static MultipartFormDataContent Fichier(byte[] contenu)
    {
        var fichier = new ByteArrayContent(contenu);
        fichier.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { fichier, "fichier", "objets.xlsx" } };
    }

    [Fact]
    public async Task Fiche_d_un_systeme_du_catalogue_reservee_aux_administrateurs()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var batch = await FicheCatalogue(bob, "BATCH");
        var id = batch.GetProperty("id").GetGuid();
        Assert.Equal(["Développement", "Recette", "Préproduction", "Production"], Environnements(batch));
        Assert.True(batch.GetProperty("environnements")[3].GetProperty("production").GetBoolean());
        Assert.Equal(["Ordonnancement", "Fichiers d'échange", "Packages"], batch.GetProperty("categories").EnumerateArray().Select(c => c.GetString()!).ToArray());
        var objets = await Ok(await bob.GetAsync($"/api/catalogue/systemes/{id}/objets"));
        Assert.Equal(batch.GetProperty("nbObjets").GetInt32(), objets.GetArrayLength());
        Assert.Contains(objets.EnumerateArray(), o => o.GetProperty("nom").GetString() == "FACT01" && o.GetProperty("type").GetString() == "Chaîne");

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/catalogue/systemes/{id}/environnements", new { environnements = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync($"/api/catalogue/systemes/{id}/objets", new { categorie = "Packages", nom = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync("/api/catalogue/categories", new { categories = new[] { "A" } })).StatusCode);

        var env = await Ok(await alice.PutAsJsonAsync($"/api/catalogue/systemes/{id}/environnements",
            new { environnements = new object[] { new { nom = "Recette", cible = "srv-batch-rec" }, new { nom = "PROD", cible = "srv-batch-prd" } } }));
        Assert.Equal([false, true], env.GetProperty("environnements").EnumerateArray().Select(e => e.GetProperty("production").GetBoolean()).ToArray());

        var cree = await alice.PostAsJsonAsync($"/api/catalogue/systemes/{id}/objets",
            new { categorie = "Ordonnancement", type = "Job", nom = "FACT07", libelle = "Relevés", precision = "" });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var objet = await Ok(cree);
        var doublon = await alice.PostAsJsonAsync($"/api/catalogue/systemes/{id}/objets", new { categorie = "Ordonnancement", nom = "fact07" });
        Assert.Equal(HttpStatusCode.BadRequest, doublon.StatusCode);
        Assert.Contains("existe déjà", await Message(doublon));
        var oid = objet.GetProperty("id").GetGuid();
        Assert.Equal("Relevés mensuels", (await Ok(await alice.PutAsJsonAsync($"/api/catalogue/systemes/{id}/objets/{oid}",
            new { categorie = "Ordonnancement", type = "Job", nom = "FACT07", libelle = "Relevés mensuels" }))).GetProperty("libelle").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/catalogue/systemes/{id}/objets/{oid}")).StatusCode);

        // Retirer une catégorie retire ses objets ; en renommer une les garde.
        var nb = (await FicheCatalogue(alice, "BATCH")).GetProperty("nbObjets").GetInt32();
        var cat = await Ok(await alice.PutAsJsonAsync($"/api/catalogue/systemes/{id}/categories",
            new { categories = new object[] { new { nom = "Jobs", ancien = "Ordonnancement" }, new { nom = "Packages" } } }));
        Assert.Equal(["Jobs", "Packages"], cat.GetProperty("categories").EnumerateArray().Select(c => c.GetString()!).ToArray());
        Assert.Equal(nb - 4, cat.GetProperty("nbObjets").GetInt32());

        var defaut = await Ok(await alice.GetAsync("/api/catalogue/categories"));
        Assert.Equal(29, defaut.GetArrayLength());
        Assert.Equal(["Tables", "vues"], (await Ok(await alice.PutAsJsonAsync("/api/catalogue/categories", new { categories = new[] { " Tables ", "vues", "Vues", "" } })))
            .EnumerateArray().Select(c => c.GetString()!).ToArray());
        Assert.Equal(2, (await Ok(await alice.GetAsync("/api/catalogue/categories"))).GetArrayLength());
    }

    [Fact]
    public async Task Environnements_des_systemes_et_des_etapes_d_une_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var bdd = SystemeId(mep, "BDD");
        Assert.Equal(["Production"], Environnements(Systeme(mep, "BDD")));

        var inconnu = await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{bdd}/environnements", new { noms = new[] { "Formation" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inconnu.StatusCode);
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{bdd}/environnements", new { noms = new[] { "Préproduction", "Production" } }));
        Assert.Equal(["Préproduction", "Production"], Environnements(Systeme(mep, "BDD")));

        // L'étape choisit ses environnements, système par système.
        var etape = mep.GetProperty("etapes").EnumerateArray()
            .First(e => e.GetProperty("plan").GetString() == "Deploiement" && e.GetProperty("systemeIds").EnumerateArray().Any(s => s.GetGuid() == bdd));
        object Saisie(JsonElement m, object[] environnements)
        {
            var e = m.GetProperty("etapes").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == etape.GetProperty("id").GetGuid());
            return new
            {
                revision = e.GetProperty("revision").GetInt32(), action = e.GetProperty("action").GetString(), systemeIds = e.GetProperty("systemeIds"),
                responsableId = e.GetProperty("responsableId"), criticite = e.GetProperty("criticite").GetString(), instructions = e.GetProperty("instructions").GetString(),
                resultatAttendu = e.GetProperty("resultatAttendu").GetString(), dependances = e.GetProperty("dependances"), environnements,
            };
        }
        var url = $"/api/meps/{id}/etapes/{etape.GetProperty("id").GetGuid()}";
        mep = await Ok(await alice.PutAsJsonAsync(url, Saisie(mep, [new { systemeId = bdd, nom = "Préproduction" }])));
        var envEtape = mep.GetProperty("etapes").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == etape.GetProperty("id").GetGuid())
            .GetProperty("environnements");
        Assert.Equal("Préproduction", Assert.Single(envEtape.EnumerateArray(), e => e.GetProperty("systemeId").GetGuid() == bdd).GetProperty("nom").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PutAsJsonAsync(url, Saisie(mep, [new { systemeId = bdd, nom = "Recette" }]))).StatusCode);

        // Retirer l'environnement du système alors que l'étape le garde : erreur bloquante.
        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{bdd}/environnements", new { noms = new[] { "Production" } }));
        Assert.Contains(Erreurs(mep), e => e == $"{etape.GetProperty("code").GetString()} : l'environnement Préproduction n'est plus choisi pour BDD (Préparation › Systèmes) ; le choisir de nouveau ou le retirer de l'étape.");

        // La liste signale les MEP qui touchent la production.
        foreach (var s in mep.GetProperty("systemes").EnumerateArray())
            mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{s.GetProperty("id").GetGuid()}/environnements", new { noms = new[] { "Recette" } }));
        var resume = (await Ok(await alice.GetAsync("/api/meps?modele=false"))).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
        Assert.False(resume.GetProperty("toucheProduction").GetBoolean());
        var journal = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray();
        Assert.Contains(journal, j => j.GetProperty("type").GetString() == "systeme.environnements"
            && j.GetProperty("detail").GetString()!.StartsWith("Environnements de BDD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Objets_impactes_choisis_dans_la_fiche_et_echanges_en_excel()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var bdd = SystemeId(mep, "BDD");
        var fiche = await Ok(await alice.GetAsync($"/api/catalogue/systemes/{Systeme(mep, "BDD").GetProperty("catalogueId").GetGuid()}/objets"));
        Guid Objet(string nom) => fiche.EnumerateArray().Single(o => o.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

        mep = await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{bdd}/objets", new
        {
            objets = new[] { new { objetCatalogueId = Objet("clients"), version = "2.1" }, new { objetCatalogueId = Objet("v_clients_actifs"), version = "nouvelle" } },
        }));
        Assert.Equal(["clients", "v_clients_actifs"], Objets(mep, bdd).Select(o => o.GetProperty("nom").GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Vues", Objets(mep, bdd).Single(o => o.GetProperty("nom").GetString() == "v_clients_actifs").GetProperty("categorie").GetString());
        var inconnu = await alice.PutAsJsonAsync($"/api/meps/{id}/systemes/{bdd}/objets", new { objets = new[] { new { objetCatalogueId = Guid.NewGuid(), version = "" } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inconnu.StatusCode);

        // Modèle vide et export : onglet de saisie et catalogue des objets.
        var modele = await alice.GetAsync($"/api/meps/{id}/objets/excel?modele=true");
        Assert.Equal(HttpStatusCode.OK, modele.StatusCode);
        using (var c = new XLWorkbook(new MemoryStream(await modele.Content.ReadAsByteArrayAsync())))
        {
            Assert.True(c.Worksheet("Objets impactés").Cell(5, 4).IsEmpty());
            Assert.Contains(c.Worksheet("Catalogue").RowsUsed(), r => r.Cell(4).GetString() == "v_factures_a_envoyer");
        }
        var export = await (await alice.GetAsync($"/api/meps/{id}/objets/excel")).Content.ReadAsByteArrayAsync();
        byte[] rempli;
        using (var c = new XLWorkbook(new MemoryStream(export)))
        {
            var f = c.Worksheet("Objets impactés");
            Assert.Equal("Nom", f.Cell(4, 4).GetString());
            var ligne = f.RowsUsed().First(r => r.Cell(4).GetString() == "clients").RowNumber();
            f.Cell(ligne, 7).Value = "2.2";
            var fin = f.LastRowUsed()!.RowNumber() + 1;
            string[][] ajouts = [["BDD", "Tables", "", "factures", "", "", "3.0"], ["BDD", "", "", "inconnue", "", "", "1"], ["ERP", "", "", "x", "", "", ""]];
            for (var i = 0; i < ajouts.Length; i++)
                for (var col = 0; col < ajouts[i].Length; col++) f.Cell(fin + i, col + 1).Value = ajouts[i][col];
            using var sortie = new MemoryStream();
            c.SaveAs(sortie);
            rempli = sortie.ToArray();
        }
        var avant = mep.GetProperty("objetsImpactes").GetArrayLength();
        var import = await Ok(await alice.PostAsync($"/api/meps/{id}/objets/excel", Fichier(rempli)));
        Assert.Equal((1, 1), (import.GetProperty("ajoutes").GetInt32(), import.GetProperty("modifies").GetInt32()));
        var rejetees = import.GetProperty("rejetees").EnumerateArray().ToList();
        Assert.Equal(2, rejetees.Count);
        Assert.Equal("Objet absent de la fiche BDD du catalogue.", rejetees[0].GetProperty("raison").GetString());
        Assert.Equal(Systeme(mep, "BDD").GetProperty("catalogueId").GetGuid(), rejetees[0].GetProperty("systemeCatalogueId").GetGuid());
        Assert.Equal("Le système ERP n'est pas dans la MEP.", rejetees[1].GetProperty("raison").GetString());
        mep = import.GetProperty("mep");
        Assert.Equal(avant + 1, mep.GetProperty("objetsImpactes").GetArrayLength());
        Assert.Equal("2.2", Objets(mep, bdd).Single(o => o.GetProperty("nom").GetString() == "clients").GetProperty("version").GetString());
        var fiches = (await Ok(await alice.GetAsync($"/api/catalogue/systemes/{Systeme(mep, "BDD").GetProperty("catalogueId").GetGuid()}/objets"))).GetArrayLength();
        Assert.Equal(fiche.GetArrayLength(), fiches); // rien n'est créé au catalogue

        using (var vide = new XLWorkbook())
        {
            vide.AddWorksheet("Feuille").Cell(1, 1).Value = "rien";
            using var s = new MemoryStream();
            vide.SaveAs(s);
            var refuse = await alice.PostAsync($"/api/meps/{id}/objets/excel", Fichier(s.ToArray()));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refuse.StatusCode);
            Assert.Contains("ligne d'en-têtes", await Message(refuse));
        }

        // Retirer un objet un par un.
        var oid = Objets(mep, bdd).First().GetProperty("id").GetGuid();
        mep = await Ok(await alice.DeleteAsync($"/api/meps/{id}/objets/{oid}"));
        Assert.DoesNotContain(mep.GetProperty("objetsImpactes").EnumerateArray(), o => o.GetProperty("id").GetGuid() == oid);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/meps/{id}/objets/{oid}")).StatusCode);
    }
}
