using ClosedXML.Excel;
using GestionMep.Tableur;

namespace GestionMep.Tests;

/// <summary>Couche commune des classeurs Excel (GestionMep.Tableur) : palette, styles, tableaux, mise en page, lecture tolérante.</summary>
public class TableurTests
{
    private static readonly DateTime Le = new(2026, 10, 17, 14, 30, 0, DateTimeKind.Unspecified);

    private static XLWorkbook Rouvrir(ClasseurExcel c) => new(new MemoryStream(c.Enregistrer()));

    private static ClasseurExcel Classeur(PaletteExcel? palette = null) =>
        new(new InfosClasseur("Essai & co", Le, "9.9.9") { Palette = palette });

    [Fact]
    public void La_palette_par_defaut_reprend_les_couleurs_de_l_application_et_choisit_le_texte_par_contraste()
    {
        var p = PaletteExcel.Defaut;
        Assert.Equal(("4F46E5", "4338CA", "ECEAFD"), (p.Primaire, p.Secondaire, p.Tertiaire));
        Assert.Equal("4F46E5", p.FondEnTete);
        Assert.Equal("FFFFFF", p.TexteEnTete);  // texte blanc sur l'indigo
        Assert.Equal("4338CA", p.TexteTitre);
        Assert.Equal("4F46E5", p.TexteLien);
        Assert.Equal("ECEAFD", p.FondSection);

        // Primaire claire : texte foncé sur les en-têtes ; secondaire trop pâle pour un titre : texte foncé.
        var claire = new PaletteExcel("#FACC15", "FDE68A", "0F766E");
        Assert.Equal(("FACC15", "16161A", "16161A"), (claire.FondEnTete, claire.TexteEnTete, claire.TexteTitre));
        Assert.True(PaletteExcel.Luminance(claire.FondSection) >= 0.75); // tertiaire soutenue éclaircie pour servir de fond
        Assert.True(PaletteExcel.Contraste(claire.TexteSection, claire.FondSection) >= 4.5);
        Assert.Equal(21, PaletteExcel.Contraste("000000", "FFFFFF"), 1);

        Assert.Throws<ArgumentException>(() => new PaletteExcel("bleu", "000000", "FFFFFF"));
        Assert.Throws<ArgumentException>(() => new PaletteExcel("12345", "000000", "FFFFFF"));
    }

    [Fact]
    public void Les_styles_suivent_la_palette_injectee()
    {
        var s = new StylesExcel(new PaletteExcel("0F766E", "134E4A", "CCFBF1"));
        Assert.Equal(("0F766E", "FFFFFF"), (s.EnTete.Fond, s.EnTete.Couleur));
        Assert.Equal("134E4A", s.Titre.Couleur);
        Assert.Equal(PaletteExcel.Defaut.Succes, (s.Etat(Ton.Succes).Fond!, s.Etat(Ton.Succes).Couleur!));
        Assert.Equal("4F46E5", StylesExcel.Defaut.EnTete.Fond);
        Assert.Equal(StylesExcel.FormatDuree, StylesExcel.FormatNombre(TypeColonne.Duree));
        Assert.Equal(StylesExcel.FormatDate, StylesExcel.FormatNombre(TypeColonne.Date));
        // Un texte long prend plusieurs lignes, plafonnées à la hauteur maximale d'Excel.
        Assert.InRange(StylesExcel.HauteurTexte("court", 40), 15, 18);
        Assert.True(StylesExcel.HauteurTexte(new string('x', 400), 40) > 60);
        Assert.Equal(409, StylesExcel.HauteurTexte(new string('x', 100_000), 40));
    }

    [Fact]
    public void Un_onglet_a_un_bloc_de_titre_un_vrai_tableau_et_une_mise_en_page_imprimable()
    {
        using var classeur = Classeur(new PaletteExcel("0F766E", "134E4A", "CCFBF1"));
        var f = classeur.AjouterFeuille("Étapes : déploiement / test");
        var ligne = f.BlocTitre("Étapes · Essai", "Contexte de l'export.", 4);
        Assert.Equal(4, ligne);
        var tons = new Dictionary<string, Ton> { ["Réussie"] = Ton.Succes, ["Échec"] = Ton.Echec };
        var tableau = f.Tableau(ligne, "Étapes", [
            new ColonneTableau("Code", 10, TypeColonne.Code),
            new ColonneTableau("Action", 40),
            new ColonneTableau("Commentaire", 30) { Facultative = true },
            new ColonneTableau("Durée", 12, TypeColonne.Duree),
            new ColonneTableau("État", 14, TypeColonne.Court) { Tons = tons, Note = "État de la dernière exécution." },
            new ColonneTableau("Id", 10) { Masquee = true },
        ], [
            ["E0001", "Sauvegarde " + new string('x', 300), null, TimeSpan.FromMinutes(95), "Réussie", "a"],
            ["E0002", "Déploiement", "", TimeSpan.FromMinutes(5), "Échec", "b"],
        ]);
        Assert.Equal((5, 6), (tableau.PremiereLigne, tableau.DerniereLigne));
        Assert.Equal(0, tableau.Colonne("Commentaire")); // colonne facultative vide : retirée
        Assert.Equal("'Étapes - déploiement - test'!$C$5:$C$6", tableau.Adresse("Durée"));
        classeur.AjouterFeuille("Deuxième").Ecrire(1, 1, "x");

        var octets = classeur.Enregistrer();
        using var c = new XLWorkbook(new MemoryStream(octets));
        var w = c.Worksheet(1);
        Assert.Equal("Étapes - déploiement - test", w.Name); // nom rendu valide pour Excel
        Assert.Equal("Étapes · Essai", w.Cell(1, 1).GetString());
        Assert.Equal("134E4A", w.Cell(1, 1).Style.Font.FontColor.Color.ToArgb().ToString("X8")[2..]);
        Assert.Equal("Généré par Gestion MEP 9.9.9 le 17/10/2026 à 14:30", w.Cell(3, 1).GetString());
        Assert.Equal(["Code", "Action", "Durée", "État", "Id"], Enumerable.Range(1, 5).Select(i => w.Cell(4, i).GetString()).ToArray());
        Assert.Equal("0F766E", w.Cell(4, 1).Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8")[2..]);

        var t = Assert.Single(w.Tables);
        Assert.True(t.ShowAutoFilter);
        Assert.StartsWith("T_", t.Name, StringComparison.Ordinal);
        Assert.Equal("A4:E6", t.RangeAddress.ToString());
        Assert.Equal(4, w.SheetView.SplitRow);
        Assert.True(w.Column(5).IsHidden);
        Assert.Equal(StylesExcel.FormatDuree, w.Cell(5, 3).Style.NumberFormat.Format);
        Assert.Equal(95d / 1440, w.Cell(5, 3).Value.GetUnifiedNumber(), 6); // fraction de jour
        Assert.Equal(StylesExcel.PoliceCode, w.Cell(5, 1).Style.Font.FontName);
        Assert.True(w.Cell(5, 2).Style.Alignment.WrapText);
        Assert.True(w.Row(5).Height > w.Row(6).Height);
        Assert.NotNull(w.Cell(4, 4).GetComment());
        Assert.Equal(2, w.ConditionalFormats.Count());
        Assert.False(w.ShowGridLines);

        var ps = w.PageSetup;
        Assert.Equal((XLPageOrientation.Landscape, XLPaperSize.A4Paper, 1, 0), (ps.PageOrientation, ps.PaperSize, ps.PagesWide, ps.PagesTall));
        Assert.Equal((4, 4), (ps.FirstRowToRepeatAtTop, ps.LastRowToRepeatAtTop));
        // En-tête (titre, « & » doublé ; nom de l'onglet) et pied de page (mention ; page n / total), lus dans le XML.
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(octets)))
        {
            using var lecteur = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var xml = lecteur.ReadToEnd();
            Assert.Contains("<x:oddHeader>&amp;LEssai &amp;&amp; co&amp;R&amp;A</x:oddHeader>", xml, StringComparison.Ordinal);
            Assert.Contains("<x:oddFooter>&amp;LGénéré par Gestion MEP 9.9.9 le 17/10/2026 à 14:30&amp;RPage &amp;P / &amp;N</x:oddFooter>", xml, StringComparison.Ordinal);
        }

        // Premier onglet actif et seul sélectionné, curseur en A1 ; propriétés du fichier.
        Assert.True(w.TabActive);
        Assert.False(c.Worksheet(2).TabSelected);
        Assert.Equal("A1", w.ActiveCell?.Address.ToString());
        Assert.Equal(("Essai & co", "Gestion MEP"), (c.Properties.Title, c.Properties.Author));
    }

    [Fact]
    public void Les_listes_deroulantes_viennent_d_un_onglet_masque_et_la_structure_peut_etre_verrouillee()
    {
        using var classeur = Classeur();
        var f = classeur.AjouterFeuille("Saisie");
        var plage = classeur.Liste("Validation", ["Validé", "Refusé", "validé", " "])!;
        Assert.Null(classeur.Liste("Vide", []));
        f.ListeDeroulante(5, 1, 20, 1, plage);
        f.Formater(5, 1, 20, 1, f.Styles.ASaisir);
        f.Proteger();
        classeur.VerrouillerStructure();

        using var c = Rouvrir(classeur);
        var listes = c.Worksheet(ClasseurExcel.FeuilleListes);
        Assert.Equal(XLWorksheetVisibility.Hidden, listes.Visibility);
        Assert.True(listes.IsProtected);
        Assert.Equal(["Validation", "Validé", "Refusé"], listes.Column(1).CellsUsed().Select(x => x.GetString()).ToArray());
        Assert.True(c.DefinedNames.Contains("Liste_Validation"));
        var saisie = c.Worksheet("Saisie");
        var v = Assert.Single(saisie.DataValidations);
        Assert.Equal(XLAllowedValues.List, v.AllowedValues);
        Assert.Contains("Listes!", v.MinValue, StringComparison.Ordinal);
        Assert.False(saisie.Cell(5, 1).Style.Protection.Locked);
        Assert.True(saisie.IsProtected);
        Assert.True(c.IsProtected);
    }

    [Fact]
    public void La_lecture_repere_les_en_tetes_quel_que_soit_leur_ordre_leur_casse_et_leurs_accents()
    {
        using var x = new XLWorkbook();
        var f = x.AddWorksheet("Objets  Impactes");
        f.Cell(1, 1).Value = "Titre";
        f.Cell(3, 2).Value = " VERSION / correction* ";
        f.Cell(3, 4).Value = "nom";
        f.Cell(3, 5).Value = "Systeme";
        f.Cell(4, 4).Value = "  clients ";
        using var s = new MemoryStream();
        x.SaveAs(s);

        Assert.Equal("version correction", LecteurClasseur.Cle(" VERSION / correction* "));
        Assert.Equal("systeme", LecteurClasseur.Cle("Système"));
        using var l = LecteurClasseur.Ouvrir(s.ToArray());
        var feuille = l.FeuilleApprochee("Objets impactés");
        Assert.NotNull(feuille);
        var synonymes = new Dictionary<string, string> { ["systeme"] = "systeme", ["nom"] = "nom", ["version correction"] = "version" };
        var (ligne, colonnes) = feuille.EnTetes(synonymes, "nom")!.Value;
        Assert.Equal(3, ligne);
        Assert.Equal((2, 4, 5), (colonnes["version"], colonnes["nom"], colonnes["systeme"]));
        Assert.Equal("clients", feuille.Texte(4, 4));
        Assert.Null(feuille.EnTetes(synonymes, "categorie"));
        Assert.Throws<ClasseurIllisibleException>(() => LecteurClasseur.Ouvrir("pas un classeur"u8.ToArray()));
    }
}
