using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Recette en Excel (export, aperçu et report de la validation des métiers) et bibliothèque de recettes, par l'API.</summary>
[Collection("Api")]
public class RecetteExcelTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);
    private static async Task<JsonElement> Corps(HttpResponseMessage rep) => JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.Clone();
    private static Guid Systeme(JsonElement mep, string nom) =>
        mep.GetProperty("systemes").EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();
    private static JsonElement Fiche(JsonElement mep, string titre) =>
        mep.GetProperty("fichesRecette").EnumerateArray().Single(f => f.GetProperty("titre").GetString() == titre);

    private static MultipartFormDataContent Fichier(byte[] contenu, string nom = "recette.xlsx")
    {
        var fichier = new ByteArrayContent(contenu);
        fichier.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { fichier, "fichier", nom } };
    }

    /// <summary>Emplacement (onglet, ligne) de chaque étape, lu dans l'onglet de contrôle, dans l'ordre du fichier.</summary>
    private static List<(Guid Fiche, Guid Etape, int Ligne, string Onglet)> Etapes(XLWorkbook c)
    {
        var controle = c.Worksheet("Contrôle");
        var liste = new List<(Guid, Guid, int, string)>();
        for (var l = 1; !controle.Cell(l, 1).IsEmpty(); l++)
        {
            if (!controle.Cell(l, 1).GetString().StartsWith("étape ", StringComparison.Ordinal)) continue;
            var p = controle.Cell(l, 2).GetString().Split(';', 5);
            liste.Add((Guid.Parse(p[0]), Guid.Parse(p[1]), int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), p[4]));
        }
        return liste;
    }

    /// <summary>Modifie le classeur comme le ferait le métier dans Excel.</summary>
    private static byte[] Remplir(byte[] xlsx, Action<XLWorkbook> action)
    {
        using var c = new XLWorkbook(new MemoryStream(xlsx));
        action(c);
        using var sortie = new MemoryStream();
        c.SaveAs(sortie);
        return sortie.ToArray();
    }

    private async Task<(HttpClient Alice, Guid Id, byte[] Export)> Rh()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        var rep = await alice.GetAsync($"/api/meps/{id}/recette/excel");
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", rep.Content.Headers.ContentType?.MediaType);
        Assert.Equal("recette-Portail-RH-3.0.xlsx", rep.Content.Headers.ContentDisposition?.FileNameStar ?? rep.Content.Headers.ContentDisposition?.FileName);
        return (alice, id, await rep.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task L_export_contient_un_sommaire_un_onglet_par_systeme_et_un_controle_cache()
    {
        var (alice, id, xlsx) = await Rh();
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        using var c = new XLWorkbook(new MemoryStream(xlsx));
        // Un onglet par système de la MEP, dans l'ordre de la MEP.
        var systemes = mep.GetProperty("systemes").EnumerateArray().Select(s => s.GetProperty("nom").GetString()!).ToList();
        Assert.Equal(["Sommaire", .. systemes, "Contrôle"], c.Worksheets.Select(f => f.Name).ToArray());
        Assert.Equal(["API", "BDD", "LDAP", "WEB"], systemes.Order().ToArray());
        Assert.Equal(XLWorksheetVisibility.Hidden, c.Worksheet("Contrôle").Visibility);
        Assert.Equal(id.ToString(), c.Worksheet("Contrôle").Cell(1, 2).GetString());
        Assert.True(c.IsProtected);
        Assert.All(c.Worksheets, f => Assert.True(f.IsProtected));

        var sommaire = c.Worksheet("Sommaire");
        Assert.Equal("Recette · Portail RH 3.0", sommaire.Cell(1, 1).GetString());
        Assert.Equal(["Système", "Fiche", "Onglet", "Étapes", "Validées", "Refusées"], Enumerable.Range(1, 6).Select(i => sommaire.Cell(7, i).GetString()).ToArray());
        Assert.Equal(4, Enumerable.Range(8, 10).Count(l => !sommaire.Cell(l, 2).IsEmpty()));
        var ligneApi = Enumerable.Range(8, 4).Single(l => sommaire.Cell(l, 1).GetString() == "API");
        Assert.Equal("API congés", sommaire.Cell(ligneApi, 2).GetString());
        Assert.StartsWith("COUNTIF('API'!E", sommaire.Cell(ligneApi, 5).FormulaA1, StringComparison.Ordinal);
        Assert.True(sommaire.Cell(ligneApi, 3).HasHyperlink);

        // Onglet API : titre de fiche coloré, code en police à chasse fixe sur fond gris, résultats déjà saisis pré-remplis.
        var api = c.Worksheet("API");
        Assert.Equal(["Étape", "Déroulé du test", "Résultat attendu", "Résultat constaté", "Validation", "Validé par", "Date"],
            Enumerable.Range(1, 7).Select(i => api.Cell(4, i).GetString()).ToArray());
        Assert.Equal("Fiche : API congés", api.Cell(5, 1).GetString());
        Assert.True(api.Cell(5, 1).IsMerged());
        Assert.Equal("curl -fsS https://rh.exemple.invalid/api/sante", api.Cell(6, 2).GetString());
        Assert.Equal("Consolas", api.Cell(6, 2).Style.Font.FontName);
        Assert.Equal("Réponse 200 « conges 3.0 ».", api.Cell(6, 3).GetString());
        Assert.Equal(("Validé", "200 conges 3.0", "Bob"), (api.Cell(6, 5).GetString(), api.Cell(6, 4).GetString(), api.Cell(6, 6).GetString()));
        Assert.Equal("Refusé", api.Cell(7, 5).GetString());
        Assert.False(api.Cell(6, 5).Style.Protection.Locked);
        Assert.True(api.Cell(6, 3).Style.Protection.Locked);
        Assert.Contains(api.DataValidations, v => v.AllowedValues == XLAllowedValues.List && v.MinValue == "\"Validé,Refusé\"");

        // Une étape par ligne de contrôle, à sa place.
        var etapes = Etapes(c);
        Assert.Equal(mep.GetProperty("fichesRecette").EnumerateArray().Sum(f => f.GetProperty("etapes").GetArrayLength()), etapes.Count);
    }

    [Fact]
    public async Task Un_fichier_rempli_puis_reimporte_met_a_jour_la_recette_apres_apercu()
    {
        var (alice, id, xlsx) = await Rh();
        var claire = await _api.ClientConnecte("claire");
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var soldes = Fiche(mep, "Soldes de congés").GetProperty("etapes")[0].GetProperty("id").GetGuid();
        var acces2 = Fiche(mep, "Accès gestionnaires").GetProperty("etapes")[1].GetProperty("id").GetGuid();

        var rempli = Remplir(xlsx, c =>
        {
            var etapes = Etapes(c);
            var s = etapes.Single(e => e.Etape == soldes);
            var f = c.Worksheet(s.Onglet);
            f.Cell(s.Ligne, 4).Value = "1 250 soldes";
            f.Cell(s.Ligne, 5).Value = "Validé";
            f.Cell(s.Ligne, 6).Value = "Marie (paie)";
            f.Cell(s.Ligne, 7).Value = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Unspecified);
            var a = etapes.Single(e => e.Etape == acces2);
            c.Worksheet(a.Onglet).Cell(a.Ligne, 5).Value = "Refusé";
            c.Worksheet(a.Onglet).Cell(a.Ligne, 4).Value = "Bouton Valider absent";
            c.Worksheet(a.Onglet).Cell(a.Ligne, 6).Value = "Hugo";
        });

        var apercu = await Ok(await claire.PostAsync($"/api/meps/{id}/recette/excel/apercu", Fichier(rempli)));
        Assert.Equal(2, apercu.GetProperty("changements").GetArrayLength());
        Assert.Equal(5, apercu.GetProperty("inchangees").GetInt32()); // les résultats déjà saisis, pré-remplis à l'export
        var premier = apercu.GetProperty("changements").EnumerateArray().Single(c => c.GetProperty("fiche").GetString() == "Soldes de congés");
        Assert.Equal(("BDD", "Soldes de congés", "Conforme", "2026-10-18"), (premier.GetProperty("systeme").GetString(), premier.GetProperty("fiche").GetString(),
            premier.GetProperty("resultat").GetString(), premier.GetProperty("date").GetString()));
        // L'aperçu ne modifie rien.
        Assert.Equal(mep.GetProperty("version").GetInt32(), (await Ok(await alice.GetAsync($"/api/meps/{id}"))).GetProperty("version").GetInt32());

        mep = await Ok(await claire.PostAsync($"/api/meps/{id}/recette/excel/import", Fichier(rempli)));
        var t = Fiche(mep, "Soldes de congés").GetProperty("etapes")[0];
        Assert.Equal(("Conforme", "1 250 soldes", "Marie (paie)", "2026-10-18", "Claire"), (t.GetProperty("resultat").GetString(), t.GetProperty("observation").GetString(),
            t.GetProperty("valideePar").GetString(), t.GetProperty("valideeLe").GetString(), t.GetProperty("par").GetString()));
        Assert.Equal("Echec", Fiche(mep, "Accès gestionnaires").GetProperty("etapes")[1].GetProperty("resultat").GetString());
        var journal = (await Ok(await alice.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray();
        Assert.Contains(journal, e => e.GetProperty("type").GetString() == "recette.import"
            && e.GetProperty("detail").GetString() == "Validation de la recette importée d'Excel : 1 étape(s) validée(s), 1 refusée(s)");

        // Le même fichier une seconde fois : rien de nouveau.
        var encore = await claire.PostAsync($"/api/meps/{id}/recette/excel/import", Fichier(rempli));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, encore.StatusCode);
        Assert.Contains("déjà à jour", (await Corps(encore)).GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_fichier_incoherent_est_refuse_en_entier_avec_un_message_clair()
    {
        var (alice, id, xlsx) = await Rh();
        var avant = await Ok(await alice.GetAsync($"/api/meps/{id}"));

        async Task<List<string>> Refus(byte[] contenu, string nom = "recette.xlsx")
        {
            var rep = await alice.PostAsync($"/api/meps/{id}/recette/excel/import", Fichier(contenu, nom));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
            var corps = await Corps(rep);
            Assert.StartsWith("Fichier refusé, rien n'a été modifié : ", corps.GetProperty("message").GetString(), StringComparison.Ordinal);
            return corps.GetProperty("erreurs").EnumerateArray().Select(e => e.GetString()!).ToList();
        }

        // Pas un classeur.
        Assert.Equal(["Fichier illisible : un classeur Excel (.xlsx) est attendu."], await Refus("bonjour"u8.ToArray(), "recette.txt"));
        // Un classeur quelconque.
        Assert.Contains("onglet « Contrôle » absent", Assert.Single(await Refus(Remplir(xlsx, c => c.Worksheet("Contrôle").Delete()))), StringComparison.Ordinal);
        // Onglet renommé, valeur inconnue, refus sans constat : toutes les raisons sont listées.
        var erreurs = await Refus(Remplir(xlsx, c =>
        {
            var etapes = Etapes(c);
            c.Worksheet("WEB").Name = "Site";
            var bdd = etapes.First(e => e.Onglet == "BDD");
            c.Worksheet("BDD").Cell(bdd.Ligne, 5).Value = "Bof";
            c.Worksheet("BDD").Cell(bdd.Ligne, 6).Value = "Marie";
            var ldap = etapes.Last(e => e.Onglet == "LDAP");
            c.Worksheet("LDAP").Cell(ldap.Ligne, 5).Value = "Refusé";
            c.Worksheet("LDAP").Cell(ldap.Ligne, 6).Value = "Marie";
        }));
        Assert.Equal(3, erreurs.Count);
        Assert.Contains("Onglet « WEB » introuvable : les onglets du fichier ne doivent être ni renommés ni supprimés.", erreurs);
        Assert.Contains(erreurs, e => e.Contains("validation « Bof » inconnue", StringComparison.Ordinal));
        Assert.Contains(erreurs, e => e.Contains("décrire le résultat constaté du refus", StringComparison.Ordinal));
        // Fichier d'une autre MEP.
        Assert.Equal(["Ce fichier est l'export de la recette d'une autre MEP."],
            await Refus(Remplir(xlsx, c => c.Worksheet("Contrôle").Cell(1, 2).Value = Guid.NewGuid().ToString())));
        // Hors phase de recette, rien ne se reporte.
        var portail = await AidesApi.IdDemo(alice);
        var hors = await alice.PostAsync($"/api/meps/{portail}/recette/excel/apercu", Fichier(xlsx));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, hors.StatusCode);
        Assert.Equal("Les recettes se déroulent en phase de recette.", (await Corps(hors)).GetProperty("message").GetString());

        // Rien n'a été modifié.
        Assert.Equal(avant.GetProperty("version").GetInt32(), (await Ok(await alice.GetAsync($"/api/meps/{id}"))).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Un_fichier_exporte_avant_une_modification_de_la_recette_est_refuse()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var xlsx = await (await alice.GetAsync($"/api/meps/{id}/recette/excel")).Content.ReadAsByteArrayAsync();
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var fiche = mep.GetProperty("fichesRecette")[0];
        await Ok(await alice.PutAsJsonAsync($"/api/meps/{id}/fiches-recette/{fiche.GetProperty("id").GetGuid()}", new
        {
            titre = fiche.GetProperty("titre").GetString(),
            etapes = new object[] { new { instructions = "Nouveau déroulé", resultatAttendu = "Nouveau résultat" } },
        }));
        // Préparation : la validation ne se reporte qu'en phase de recette.
        var rep = await alice.PostAsync($"/api/meps/{id}/recette/excel/apercu", Fichier(xlsx));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.Equal("Les recettes se déroulent en phase de recette.", (await Corps(rep)).GetProperty("message").GetString());

        var rh = await AidesApi.IdMep(alice, DonneesDemo.OutilRecette);
        var avant = await (await alice.GetAsync($"/api/meps/{rh}/recette/excel")).Content.ReadAsByteArrayAsync();
        // En recette, le contenu ne se modifie plus : on simule une recette changée en modifiant la version notée dans le fichier.
        var falsifie = Remplir(avant, c => c.Worksheet("Contrôle").Cell(2, 2).Value = "0000000000000000");
        rep = await alice.PostAsync($"/api/meps/{rh}/recette/excel/import", Fichier(falsifie));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rep.StatusCode);
        Assert.Contains("La recette a été modifiée dans l'application depuis l'export de ce fichier",
            (await Corps(rep)).GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bibliotheque_enregistrer_une_fiche_comme_modele_puis_l_inserer_dans_une_mep()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var id = await AidesApi.IdDemo(alice);
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var catalogue = (await Ok(await alice.GetAsync("/api/catalogue/systemes"))).EnumerateArray().ToList();
        Guid Cat(string nom) => catalogue.Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

        // La démonstration en fournit quelques-uns ; filtre par système.
        Assert.Equal(7, (await Ok(await alice.GetAsync("/api/modeles-recette"))).GetArrayLength());
        Assert.Equal("Connexion au portail", (await Ok(await alice.GetAsync($"/api/modeles-recette?systemeCatalogueId={Cat("WEB")}")))[0].GetProperty("titre").GetString());

        var fiche = Fiche(mep, "Parcours de commande");
        var modele = await Ok(await bob.PostAsJsonAsync("/api/modeles-recette/depuis-fiche",
            new { mepId = id, ficheId = fiche.GetProperty("id").GetGuid(), titre = " Parcours type ", systemeCatalogueId = Cat("WEB") }));
        Assert.Equal(("Parcours type", 1, "Bob"), (modele.GetProperty("titre").GetString(), modele.GetProperty("version").GetInt32(), modele.GetProperty("modifiePar").GetString()));
        Assert.Equal(fiche.GetProperty("etapes").GetArrayLength(), modele.GetProperty("etapes").GetArrayLength());
        var mid = modele.GetProperty("id").GetGuid();

        // Modification avec verrou optimiste ; un modèle incomplet est refusé avec la liste des anomalies.
        var incomplet = await bob.PutAsJsonAsync($"/api/modeles-recette/{mid}", new { version = 1, titre = "", systemeCatalogueId = Cat("WEB"), etapes = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplet.StatusCode);
        Assert.Equal(2, (await Corps(incomplet)).GetProperty("erreurs").GetArrayLength());
        modele = await Ok(await bob.PutAsJsonAsync($"/api/modeles-recette/{mid}", new
        {
            version = 1, titre = "Parcours type", systemeCatalogueId = Cat("WEB"),
            etapes = new object[] { new { instructions = "Ouvrir\n\n```\ncurl https://exemple.invalid\n```", resultatAttendu = "Page affichée" } },
        }));
        Assert.Equal(2, modele.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PutAsJsonAsync($"/api/modeles-recette/{mid}",
            new { version = 1, titre = "x", systemeCatalogueId = Cat("WEB"), etapes = new object[] { new { instructions = "a", resultatAttendu = "b" } } })).StatusCode);

        // Insertion : copie dans le système choisi de la MEP.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette/depuis-modele", new { modeleId = mid })).StatusCode);
        mep = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette/depuis-modele", new { modeleId = mid, systemeId = Systeme(mep, "WEB") }));
        var copie = Fiche(mep, "Parcours type");
        Assert.Equal(Systeme(mep, "WEB"), copie.GetProperty("systemeId").GetGuid());
        Assert.Equal("Page affichée", copie.GetProperty("etapes")[0].GetProperty("resultatAttendu").GetString());

        // Suppression réservée aux administrateurs ; la fiche insérée reste.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/modeles-recette/{mid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/modeles-recette/{mid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/modeles-recette/{mid}")).StatusCode);
        Fiche(await Ok(await alice.GetAsync($"/api/meps/{id}")), "Parcours type");

        // Un système du catalogue utilisé par un modèle est seulement désactivé.
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/modeles-recette", new
        {
            titre = "Traitements de nuit", systemeCatalogueId = Cat("BATCH"),
            etapes = new object[] { new { instructions = "Lire le compte rendu.", resultatAttendu = "Fin normale." } },
        })).StatusCode);
        await alice.DeleteAsync($"/api/catalogue/systemes/{Cat("BATCH")}");
        Assert.False((await Ok(await alice.GetAsync("/api/catalogue/systemes"))).EnumerateArray()
            .Single(s => s.GetProperty("nom").GetString() == "BATCH").GetProperty("actif").GetBoolean());
    }

    [Fact]
    public async Task Une_nouvelle_fiche_exige_son_systeme()
    {
        var alice = await _api.ClientConnecte("alice");
        var id = await AidesApi.IdDemo(alice);
        var rep = await alice.PostAsJsonAsync($"/api/meps/{id}/fiches-recette", new { titre = "Sans système" });
        Assert.Equal(HttpStatusCode.BadRequest, rep.StatusCode);
        Assert.Equal("Choisir le système de la fiche de recette.", (await Corps(rep)).GetProperty("message").GetString());
    }
}
