using ClosedXML.Excel;

namespace GestionMep.Tableur;

/// <summary>
/// Mise en forme d'une cellule écrite par <see cref="FeuilleExcel.Ecrire"/>. Les couleurs sont en hexadécimal « RRGGBB ».
/// Le texte est toujours renvoyé à la ligne et aligné en haut, pour rester lisible à l'impression.
/// </summary>
public sealed record Format
{
    public bool Gras { get; init; }
    public double? Taille { get; init; }
    public string? Couleur { get; init; }
    public string? Fond { get; init; }
    /// <summary>Police à chasse fixe (code, commandes).</summary>
    public bool ChasseFixe { get; init; }
    public bool Centre { get; init; }
    public bool Bordure { get; init; }
    /// <summary>Cellule à remplir par le destinataire : seule modifiable une fois la feuille protégée.</summary>
    public bool ASaisir { get; init; }
    /// <summary>Format de date jour/mois/année.</summary>
    public bool Date { get; init; }

    public static readonly Format Normal = new();
}

/// <summary>
/// Classeur Excel (.xlsx) en écriture : brique commune des exportations. Ne dépend d'aucun objet métier ; la mise en
/// forme partagée (en-têtes, cases à remplir en jaune, code sur fond gris) est dans <see cref="Charte"/>.
/// </summary>
public sealed class ClasseurExcel : IDisposable
{
    private readonly XLWorkbook _classeur = new();
    private readonly HashSet<string> _noms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ajoute une feuille ; le nom est rendu valide pour Excel (<see cref="NomsFeuilles.Assainir"/>) et unique.</summary>
    public FeuilleExcel AjouterFeuille(string nom)
    {
        var nomValide = NomsFeuilles.Unique(NomsFeuilles.Assainir(nom), _noms);
        _noms.Add(nomValide);
        var feuille = _classeur.Worksheets.Add(nomValide);
        feuille.Style.Font.FontName = Charte.Police;
        feuille.Style.Font.FontSize = 11;
        feuille.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        feuille.PageSetup.FitToPages(1, 0);
        return new FeuilleExcel(feuille);
    }

    /// <summary>
    /// Feuille masquée de contrôle : une paire clé / valeur par ligne (colonnes A et B), lue au retour du fichier par
    /// <see cref="LecteurClasseur.LireControle"/>. Elle est protégée comme les autres.
    /// </summary>
    public FeuilleExcel AjouterFeuilleControle(string nom, IEnumerable<KeyValuePair<string, string>> valeurs)
    {
        var f = AjouterFeuille(nom);
        var ligne = 1;
        foreach (var (cle, valeur) in valeurs)
        {
            f.Ecrire(ligne, 1, cle);
            f.Ecrire(ligne, 2, valeur);
            ligne++;
        }
        f.Interne.Visibility = XLWorksheetVisibility.Hidden;
        return f;
    }

    /// <summary>
    /// Verrouille la structure du classeur : onglets impossibles à renommer, déplacer, supprimer, ajouter ou afficher
    /// (sans mot de passe : c'est un garde-fou contre les fausses manipulations, pas une sécurité).
    /// </summary>
    public void VerrouillerStructure() => _classeur.Protect(XLWorkbookProtectionElements.Windows);

    /// <summary>Contenu du fichier .xlsx ; la première feuille est active.</summary>
    public byte[] Enregistrer()
    {
        if (_classeur.Worksheets.Count > 0) _classeur.Worksheet(1).SetTabActive();
        using var flux = new MemoryStream();
        _classeur.SaveAs(flux);
        return flux.ToArray();
    }

    public void Dispose() => _classeur.Dispose();
}

/// <summary>Feuille d'un <see cref="ClasseurExcel"/> : lignes et colonnes numérotées à partir de 1, comme dans Excel.</summary>
public sealed class FeuilleExcel
{
    /// <summary>Texte maximal d'une cellule Excel.</summary>
    public const int TexteMax = 32_767;

    internal IXLWorksheet Interne { get; }

    internal FeuilleExcel(IXLWorksheet feuille) => Interne = feuille;

    /// <summary>Nom réel de l'onglet (après assainissement).</summary>
    public string Nom => Interne.Name;

    /// <summary>
    /// Écrit une valeur (texte, nombre, date, booléen ou null) avec sa mise en forme. Un texte trop long pour Excel est
    /// tronqué ; un texte qui commence par « = » est écrit tel quel, jamais comme une formule.
    /// </summary>
    public void Ecrire(int ligne, int colonne, object? valeur, Format? format = null)
    {
        var c = Interne.Cell(ligne, colonne);
        switch (valeur)
        {
            case null: c.Clear(XLClearOptions.Contents); break;
            case string s: c.SetValue(s.Length > TexteMax ? s[..(TexteMax - 1)] + "…" : s); break;
            case int i: c.SetValue(i); break;
            case long l: c.SetValue(l); break;
            case double d: c.SetValue(d); break;
            case decimal m: c.SetValue(m); break;
            case bool b: c.SetValue(b); break;
            case DateOnly j: c.SetValue(j.ToDateTime(TimeOnly.MinValue)); break;
            case DateTime dt: c.SetValue(dt); break;
            default: c.SetValue(valeur.ToString()); break;
        }
        Appliquer(Interne.Range(ligne, colonne, ligne, colonne), format ?? Format.Normal);
    }

    /// <summary>Formule Excel (sans le « = » initial), par exemple <c>COUNTIF('WEB'!E5:E9,"Validé")</c>.</summary>
    public void Formule(int ligne, int colonne, string formule, Format? format = null)
    {
        Interne.Cell(ligne, colonne).FormulaA1 = formule;
        Appliquer(Interne.Range(ligne, colonne, ligne, colonne), format ?? Format.Normal);
    }

    /// <summary>Applique une mise en forme à une plage (bordure, fond, saisie autorisée…).</summary>
    public void Formater(int ligne1, int colonne1, int ligne2, int colonne2, Format format) =>
        Appliquer(Interne.Range(ligne1, colonne1, ligne2, colonne2), format);

    /// <summary>Fusionne une plage (la valeur est celle de la cellule en haut à gauche).</summary>
    public void Fusionner(int ligne1, int colonne1, int ligne2, int colonne2)
    {
        if (ligne1 != ligne2 || colonne1 != colonne2) Interne.Range(ligne1, colonne1, ligne2, colonne2).Merge();
    }

    /// <summary>Liste déroulante sur une plage : seules ces valeurs (ou une case vide) sont acceptées par Excel.</summary>
    public void ListeDeroulante(int ligne1, int colonne1, int ligne2, int colonne2, IReadOnlyList<string> valeurs)
    {
        var v = Interne.Range(ligne1, colonne1, ligne2, colonne2).CreateDataValidation();
        v.IgnoreBlanks = true;
        v.List("\"" + string.Join(",", valeurs) + "\"", true);
        v.ErrorStyle = XLErrorStyle.Stop;
        v.ErrorTitle = "Valeur non prévue";
        v.ErrorMessage = "Choisir dans la liste : " + string.Join(" ou ", valeurs) + ".";
    }

    /// <summary>Date seulement (jour/mois/année) acceptée dans la plage, ou case vide.</summary>
    public void SaisieDate(int ligne1, int colonne1, int ligne2, int colonne2)
    {
        var v = Interne.Range(ligne1, colonne1, ligne2, colonne2).CreateDataValidation();
        v.IgnoreBlanks = true;
        v.Date.GreaterThan(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        v.ErrorStyle = XLErrorStyle.Stop;
        v.ErrorTitle = "Date attendue";
        v.ErrorMessage = "Saisir une date, par exemple 18/10/2026.";
    }

    /// <summary>Lien vers une cellule d'une feuille du même classeur.</summary>
    public void LienInterne(int ligne, int colonne, string texte, string feuille, int ligneCible, int colonneCible = 1)
    {
        var c = Interne.Cell(ligne, colonne);
        c.SetValue(texte);
        var adresse = Interne.Workbook.Worksheet(feuille).Cell(ligneCible, colonneCible).Address.ToStringRelative();
        c.SetHyperlink(new XLHyperlink($"'{feuille.Replace("'", "''")}'!{adresse}"));
        c.Style.Font.FontColor = XLColor.FromHtml("#" + Charte.Accent);
        c.Style.Font.Underline = XLFontUnderlineValues.Single;
    }

    public void LargeurColonne(int colonne, double largeur) => Interne.Column(colonne).Width = largeur;

    public void HauteurLigne(int ligne, double hauteur) => Interne.Row(ligne).Height = hauteur;

    /// <summary>Hauteur actuelle d'une ligne, en points.</summary>
    public double Hauteur(int ligne) => Interne.Row(ligne).Height;

    /// <summary>Fige les lignes au-dessus de <paramref name="lignes"/> + 1 (en-têtes toujours visibles).</summary>
    public void FigerLignes(int lignes) => Interne.SheetView.FreezeRows(lignes);

    /// <summary>
    /// Protège la feuille : seules les cellules écrites avec <see cref="Format.ASaisir"/> restent modifiables
    /// (sans mot de passe). Largeur des colonnes et hauteur des lignes restent ajustables.
    /// </summary>
    public void Proteger() =>
        Interne.Protect(XLProtectionAlgorithm.Algorithm.SimpleHash, XLSheetProtectionElements.FormatColumns | XLSheetProtectionElements.FormatRows
            | XLSheetProtectionElements.SelectEverything);

    private static void Appliquer(IXLRange r, Format f)
    {
        var s = r.Style;
        s.Alignment.WrapText = true;
        s.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        if (f.Centre) s.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        if (f.Gras) s.Font.Bold = true;
        if (f.Taille is { } t) s.Font.FontSize = t;
        if (f.Couleur is { } c) s.Font.FontColor = XLColor.FromHtml("#" + c);
        if (f.ChasseFixe)
        {
            s.Font.FontName = Charte.PoliceCode;
            s.Font.FontSize = 10;
        }
        var fond = f.Fond ?? (f.ASaisir ? Charte.FondASaisir : null);
        if (fond is not null) s.Fill.SetBackgroundColor(XLColor.FromHtml("#" + fond));
        if (f.Bordure)
        {
            s.Border.OutsideBorder = XLBorderStyleValues.Thin;
            s.Border.OutsideBorderColor = XLColor.FromHtml("#" + Charte.Bordure);
            s.Border.InsideBorder = XLBorderStyleValues.Thin;
            s.Border.InsideBorderColor = XLColor.FromHtml("#" + Charte.Bordure);
        }
        if (f.ASaisir) s.Protection.Locked = false;
        if (f.Date) s.DateFormat.Format = "dd/mm/yyyy";
    }
}

/// <summary>Charte commune des classeurs exportés : polices, couleurs et formats prêts à l'emploi.</summary>
public static class Charte
{
    public const string Police = "Calibri";
    public const string PoliceCode = "Consolas";
    /// <summary>Indigo de l'application (en-têtes de tableau, liens).</summary>
    public const string Accent = "4F46E5";
    public const string Bordure = "D4D4DC";
    public const string TexteDiscret = "6B6B78";
    /// <summary>Jaune pâle des cases à remplir par le destinataire.</summary>
    public const string FondASaisir = "FFF7D6";
    /// <summary>Gris des blocs de code.</summary>
    public const string FondCode = "F1F1F4";

    public static readonly Format Titre = new() { Gras = true, Taille = 16 };
    public static readonly Format SousTitre = new() { Couleur = TexteDiscret };
    public static readonly Format EnTete = new() { Gras = true, Couleur = "FFFFFF", Fond = Accent, Bordure = true };
    public static readonly Format Cellule = new() { Bordure = true };
    public static readonly Format Code = new() { ChasseFixe = true, Fond = FondCode, Bordure = true };
    public static readonly Format ASaisir = new() { ASaisir = true, Bordure = true };

    /// <summary>
    /// Hauteur estimée (en points) d'un texte renvoyé à la ligne dans une colonne de <paramref name="largeur"/> caractères :
    /// Excel n'ajuste pas seul la hauteur des cellules fusionnées.
    /// </summary>
    public static double HauteurTexte(string texte, double largeur, double minimum = 18)
    {
        var parLigne = Math.Max(1, (int)(largeur * 1.1));
        var lignes = texte.Replace("\r\n", "\n").Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / (double)parLigne)));
        return Math.Max(minimum, lignes * 15 + 4);
    }
}
