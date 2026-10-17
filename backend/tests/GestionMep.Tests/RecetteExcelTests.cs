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

    private static readonly string[] OngletsAnnexes = ["À lire", "Sommaire", "Listes", "Contrôle"];

    /// <summary>Colonne d'un en-tête (ligne 4) d'un onglet système.</summary>
    private static int Col(IXLWorksheet f, string entete) => f.Row(4).CellsUsed().Single(c => c.GetString() == entete).Address.ColumnNumber;

    /// <summary>Emplacement (onglet, ligne) de chaque étape, lu dans la colonne masquée « Réf. » de chaque onglet système.</summary>
    private static List<(Guid Fiche, Guid Etape, int Ligne, string Onglet)> Etapes(XLWorkbook c)
    {
        var liste = new List<(Guid, Guid, int, string)>();
        foreach (var f in c.Worksheets.Where(f => !OngletsAnnexes.Contains(f.Name)))
        {
            var r = Col(f, "Réf.");
            for (var l = 5; !f.Cell(l, r).IsEmpty(); l++)
            {
                var p = f.Cell(l, r).GetString().Split(';');
                liste.Add((Guid.Parse(p[0]), Guid.Parse(p[1]), l, f.Name));
            }
        }
        return liste;
    }

    /// <summary>Saisie du métier sur une étape : résultat constaté, validation, validé par, date (null : case laissée telle quelle).</summary>
    private static void Saisir(XLWorkbook c, (Guid Fiche, Guid Etape, int Ligne, string Onglet) e, string? constate, string? validation, string? par,
        DateTime? date = null)
    {
        var f = c.Worksheet(e.Onglet);
        if (constate is not null) f.Cell(e.Ligne, Col(f, "Résultat constaté")).Value = constate;
        if (validation is not null) f.Cell(e.Ligne, Col(f, "Validation")).Value = validation;
        if (par is not null) f.Cell(e.Ligne, Col(f, "Validé par")).Value = par;
        if (date is { } d) f.Cell(e.Ligne, Col(f, "Date")).Value = d;
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

    /// <summary>
    /// Classeur au format de la version précédente (« recette-1 ») pour les mêmes étapes : un onglet par système, chaque
    /// étape à une ligne fixe (numéro en colonne A, constaté, validation, validé par et date en colonnes D à G), et l'onglet
    /// « Contrôle » qui note l'emplacement de chaque étape. <paramref name="saisies"/> remplit des étapes comme le métier.
    /// </summary>
    private static byte[] AncienFormat(byte[] export, Dictionary<Guid, (string Constate, string Validation, string Par, DateTime? Date)> saisies)
    {
        using var nouveau = new XLWorkbook(new MemoryStream(export));
        var controle = nouveau.Worksheet("Contrôle");
        using var ancien = new XLWorkbook();
        ancien.AddWorksheet("Sommaire").Cell(1, 1).Value = "Recette";
        var lignes = new List<string>();
        foreach (var g in Etapes(nouveau).GroupBy(e => e.Onglet))
        {
            var f = ancien.AddWorksheet(g.Key);
            string[] entetes = ["Étape", "Déroulé du test", "Résultat attendu", "Résultat constaté", "Validation", "Validé par", "Date"];
            for (var i = 0; i < entetes.Length; i++) f.Cell(4, i + 1).Value = entetes[i];
            var ligne = 5;
            foreach (var fiche in g.GroupBy(e => e.Fiche))
            {
                f.Cell(ligne++, 1).Value = "Fiche";
                var numero = 0;
                foreach (var e in fiche)
                {
                    numero++;
                    f.Cell(ligne, 1).Value = numero;
                    if (saisies.TryGetValue(e.Etape, out var s))
                    {
                        f.Cell(ligne, 4).Value = s.Constate;
                        f.Cell(ligne, 5).Value = s.Validation;
                        f.Cell(ligne, 6).Value = s.Par;
                        if (s.Date is { } d) f.Cell(ligne, 7).Value = d;
                    }
                    lignes.Add($"{e.Fiche};{e.Etape};{ligne};{numero};{g.Key}");
                    ligne += 2; // le déroulé occupait parfois plusieurs lignes fusionnées
                }
            }
        }
        var c = ancien.AddWorksheet("Contrôle");
        c.Cell(1, 1).Value = "mep";
        c.Cell(1, 2).Value = controle.Cell(1, 2).GetString();
        c.Cell(2, 1).Value = "version recette";
        c.Cell(2, 2).Value = controle.Cell(2, 2).GetString();
        c.Cell(3, 1).Value = "format";
        c.Cell(3, 2).Value = "recette-1";
        for (var i = 0; i < lignes.Count; i++)
        {
            c.Cell(4 + i, 1).Value = $"étape {i + 1}";
            c.Cell(4 + i, 2).Value = lignes[i];
        }
        c.Visibility = XLWorksheetVisibility.Hidden;
        using var sortie = new MemoryStream();
        ancien.SaveAs(sortie);
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
    public async Task L_export_contient_consignes_sommaire_un_onglet_par_systeme_et_des_onglets_caches()
    {
        var (alice, id, xlsx) = await Rh();
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        using var c = new XLWorkbook(new MemoryStream(xlsx));
        // Consignes, sommaire, un onglet par système de la MEP (dans l'ordre de la MEP), puis les onglets masqués.
        var systemes = mep.GetProperty("systemes").EnumerateArray().Select(s => s.GetProperty("nom").GetString()!).ToList();
        Assert.Equal(["À lire", "Sommaire", .. systemes, "Listes", "Contrôle"], c.Worksheets.Select(f => f.Name).ToArray());
        Assert.Equal(["API", "BDD", "LDAP", "WEB"], systemes.Order().ToArray());
        Assert.Equal(XLWorksheetVisibility.Hidden, c.Worksheet("Contrôle").Visibility);
        Assert.Equal(XLWorksheetVisibility.Hidden, c.Worksheet("Listes").Visibility);
        Assert.Equal(id.ToString(), c.Worksheet("Contrôle").Cell(1, 2).GetString());
        Assert.Equal("recette-2", c.Worksheet("Contrôle").Cell(3, 2).GetString());
        Assert.True(c.IsProtected);
        Assert.All(c.Worksheets, f => Assert.True(f.IsProtected));
        Assert.True(c.Worksheet("À lire").TabActive);
        Assert.Contains(c.Worksheet("À lire").CellsUsed(), x => x.GetString().Contains("Remplissez les cases jaunes", StringComparison.Ordinal));

        var sommaire = c.Worksheet("Sommaire");
        Assert.Equal("Sommaire de la recette · Portail RH 3.0", sommaire.Cell(1, 1).GetString());
        Assert.Equal(["Système", "Fiche", "Étapes", "Validées", "Refusées", "Restantes", "Aller à"],
            Enumerable.Range(1, 7).Select(i => sommaire.Cell(4, i).GetString()).ToArray());
        Assert.Equal(4, Enumerable.Range(5, 10).Count(l => !sommaire.Cell(l, 2).IsEmpty()));
        var ligneApi = Enumerable.Range(5, 4).Single(l => sommaire.Cell(l, 1).GetString() == "API");
        Assert.Equal("API congés", sommaire.Cell(ligneApi, 2).GetString());
        Assert.StartsWith("COUNTIFS(", sommaire.Cell(ligneApi, 4).FormulaA1, StringComparison.Ordinal);
        Assert.Contains("API!$A$5:$A$", sommaire.Cell(ligneApi, 4).FormulaA1, StringComparison.Ordinal);
        Assert.True(sommaire.Cell(ligneApi, 7).HasHyperlink);
        // Les compteurs sont calculés à l'export (une validée, une refusée, aucune restante sur l'API).
        Assert.Equal((1d, 1d, 0d), (sommaire.Cell(ligneApi, 4).CachedValue.GetNumber(), sommaire.Cell(ligneApi, 5).CachedValue.GetNumber(),
            sommaire.Cell(ligneApi, 6).CachedValue.GetNumber()));

        // Onglet API : un tableau Excel, une ligne par étape, code en chasse fixe, résultats déjà saisis pré-remplis.
        var api = c.Worksheet("API");
        Assert.Equal(["Fiche", "N°", "Déroulé du test", "Résultat attendu", "Résultat constaté", "Validation", "Validé par", "Date", "Réf."],
            Enumerable.Range(1, 9).Select(i => api.Cell(4, i).GetString()).ToArray());
        Assert.Single(api.Tables);
        Assert.True(api.Column(9).IsHidden);
        Assert.Equal(("API congés", "API congés"), (api.Cell(5, 1).GetString(), api.Cell(6, 1).GetString()));
        Assert.Equal("curl -fsS https://rh.exemple.invalid/api/sante", api.Cell(5, 3).GetString());
        Assert.Equal("Consolas", api.Cell(5, 3).GetRichText().First().FontName);
        Assert.Equal("Réponse 200 « conges 3.0 ».", api.Cell(5, 4).GetString());
        Assert.Equal(("Validé", "200 conges 3.0", "Bob"), (api.Cell(5, 6).GetString(), api.Cell(5, 5).GetString(), api.Cell(5, 7).GetString()));
        Assert.Equal("Refusé", api.Cell(6, 6).GetString());
        Assert.False(api.Cell(5, 6).Style.Protection.Locked);
        Assert.True(api.Cell(5, 4).Style.Protection.Locked);
        Assert.Contains(api.DataValidations, v => v.AllowedValues == XLAllowedValues.List && v.MinValue.Contains("Listes!", StringComparison.Ordinal)
            && v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 6));
        Assert.NotEmpty(api.ConditionalFormats);
        Assert.NotNull(api.Cell(4, 6).GetComment());

        // Une ligne par étape de test.
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
            Saisir(c, etapes.Single(e => e.Etape == soldes), "1 250 soldes", "Validé", "Marie (paie)", new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Unspecified));
            Saisir(c, etapes.Single(e => e.Etape == acces2), "Bouton Valider absent", "Refusé", "Hugo");
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

        // Réexporté après l'import : les validations reportées sont dans le fichier (aller-retour sans perte).
        using var c2 = new XLWorkbook(new MemoryStream(await (await alice.GetAsync($"/api/meps/{id}/recette/excel")).Content.ReadAsByteArrayAsync()));
        var s2 = Etapes(c2).Single(e => e.Etape == soldes);
        var f2 = c2.Worksheet(s2.Onglet);
        Assert.Equal(("1 250 soldes", "Validé", "Marie (paie)", new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Unspecified)),
            (f2.Cell(s2.Ligne, Col(f2, "Résultat constaté")).GetString(), f2.Cell(s2.Ligne, Col(f2, "Validation")).GetString(),
                f2.Cell(s2.Ligne, Col(f2, "Validé par")).GetString(), f2.Cell(s2.Ligne, Col(f2, "Date")).GetDateTime()));
    }

    [Fact]
    public async Task Un_fichier_trie_filtre_ou_reordonne_s_importe_et_un_fichier_de_la_version_precedente_aussi()
    {
        var (alice, id, xlsx) = await Rh();
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        var soldes = Fiche(mep, "Soldes de congés").GetProperty("etapes")[0].GetProperty("id").GetGuid();
        var acces2 = Fiche(mep, "Accès gestionnaires").GetProperty("etapes")[1].GetProperty("id").GetGuid();

        // Lignes interverties, colonnes réordonnées, espaces et casse : l'étape est retrouvée par sa référence, la colonne par son en-tête.
        var bouscule = Remplir(xlsx, c =>
        {
            foreach (var f in c.Worksheets.Where(f => !OngletsAnnexes.Contains(f.Name))) f.Unprotect();
            var e = Etapes(c).Single(x => x.Etape == acces2);
            var f2 = c.Worksheet(e.Onglet);
            var autre = e.Ligne == 5 ? 6 : 5;
            f2.Row(e.Ligne).Cells(1, 9).Select(x => (x.Address.ColumnNumber, x.Value)).ToList()
                .ForEach(x => { var v = f2.Cell(autre, x.ColumnNumber).Value; f2.Cell(autre, x.ColumnNumber).Value = x.Value; f2.Cell(e.Ligne, x.ColumnNumber).Value = v; });
            // Colonnes « Validation » et « Validé par » échangées (en-têtes compris), hors tableau Excel.
            foreach (var t in f2.Tables.ToList()) f2.Tables.Remove(t.Name);
            for (var l = 4; l <= 6; l++)
            {
                var (v, p) = (f2.Cell(l, 6).Value, f2.Cell(l, 7).Value);
                f2.Cell(l, 6).Value = p;
                f2.Cell(l, 7).Value = v;
            }
            Saisir(c, (e.Fiche, e.Etape, autre, e.Onglet), "  Bouton absent ", " refusé ", "Hugo");
            Saisir(c, Etapes(c).Single(x => x.Etape == soldes), "", "validée", "Marie");
        });
        var apercu = await Ok(await alice.PostAsync($"/api/meps/{id}/recette/excel/apercu", Fichier(bouscule)));
        var changements = apercu.GetProperty("changements").EnumerateArray().ToList();
        Assert.Equal(2, changements.Count);
        var refus = changements.Single(x => x.GetProperty("fiche").GetString() == "Accès gestionnaires");
        Assert.Equal(("Echec", "Bouton absent", "Hugo"), (refus.GetProperty("resultat").GetString(), refus.GetProperty("observation").GetString(),
            refus.GetProperty("validePar").GetString()));

        // Fichier exporté par la version précédente de l'application : toujours accepté.
        var ancien = AncienFormat(xlsx, new Dictionary<Guid, (string, string, string, DateTime?)>
        {
            [soldes] = ("1 250 soldes", "Validé", "Marie", new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Unspecified)),
        });
        mep = await Ok(await alice.PostAsync($"/api/meps/{id}/recette/excel/import", Fichier(ancien)));
        Assert.Equal(("Conforme", "Marie"), (Fiche(mep, "Soldes de congés").GetProperty("etapes")[0].GetProperty("resultat").GetString(),
            Fiche(mep, "Soldes de congés").GetProperty("etapes")[0].GetProperty("valideePar").GetString()));
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
        // Valeur inconnue, refus sans constat, référence abîmée : toutes les raisons sont listées, avec onglet, ligne et colonne.
        var erreurs = await Refus(Remplir(xlsx, c =>
        {
            var etapes = Etapes(c);
            var bdd = etapes.First(e => e.Onglet == "BDD");
            Saisir(c, bdd, null, "Bof", "Marie");
            var ldap = etapes.Last(e => e.Onglet == "LDAP");
            Saisir(c, ldap, null, "Refusé", "Marie");
            var web = etapes.First(e => e.Onglet == "WEB");
            c.Worksheet("WEB").Cell(web.Ligne, Col(c.Worksheet("WEB"), "Réf.")).Value = "abîmée";
        }));
        Assert.Equal(3, erreurs.Count);
        Assert.Contains(erreurs, e => e.StartsWith("Onglet BDD, ligne 5 (« Soldes de congés », étape 1) : validation « Bof » inconnue (colonne « Validation »",
            StringComparison.Ordinal));
        Assert.Contains(erreurs, e => e.StartsWith("Onglet LDAP, ligne", StringComparison.Ordinal)
            && e.Contains("décrire le résultat constaté du refus (colonne « Résultat constaté »)", StringComparison.Ordinal));
        Assert.Contains(erreurs, e => e.StartsWith("Onglet WEB, ligne 5, colonne « Réf. » : référence modifiée", StringComparison.Ordinal));
        // Colonne supprimée.
        Assert.Contains("colonne « Validation » introuvable", Assert.Single(await Refus(Remplir(xlsx, c =>
        {
            c.Worksheet("API").Unprotect();
            c.Worksheet("API").Cell(4, 6).Value = "Avis";
        }))), StringComparison.Ordinal);
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
