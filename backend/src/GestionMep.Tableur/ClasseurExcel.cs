using System.Globalization;
using ClosedXML.Excel;

namespace GestionMep.Tableur;

/// <summary>
/// Identité d'un classeur : titre (propriétés du fichier, en-tête des pages imprimées), application et version qui l'ont
/// produit, date de génération (heure locale affichée telle quelle) et palette de marque.
/// </summary>
/// <param name="Titre">Par exemple « Étapes de déploiement · Paiement mobile 4.2 ».</param>
/// <param name="GenereLe">Date et heure de génération, déjà dans le fuseau du lecteur.</param>
/// <param name="Version">Version de l'application (« 1.4.0 »).</param>
public sealed record InfosClasseur(string Titre, DateTime GenereLe, string Version)
{
    /// <summary>Nom de l'application, repris dans la mention « Généré par » et les propriétés du fichier.</summary>
    public string Application { get; init; } = "Gestion MEP";
    /// <summary>Sujet (propriétés du fichier).</summary>
    public string? Sujet { get; init; }
    /// <summary>Couleurs de marque ; null : celles de l'application.</summary>
    public PaletteExcel? Palette { get; init; }

    /// <summary>« Généré par Gestion MEP 1.4.0 le 17/10/2026 à 14:34 ».</summary>
    public string Mention => $"Généré par {Application} {Version} le {GenereLe.ToString("dd/MM/yyyy 'à' HH:mm", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Classeur Excel (.xlsx) en écriture : brique commune des exportations et des modèles à remplir. Ne dépend d'aucun objet
/// métier. Chaque onglet reçoit la même mise en page (police, quadrillage masqué, impression paysage ajustée à la largeur,
/// en-tête et pied de page numérotés) ; la mise en forme vient de la <see cref="StylesExcel"/>.
/// </summary>
public sealed class ClasseurExcel : IDisposable
{
    public const string FeuilleListes = "Listes";

    private readonly XLWorkbook _classeur = new();
    private readonly HashSet<string> _noms = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tables = new(StringComparer.OrdinalIgnoreCase);
    private FeuilleExcel? _listes;
    private int _colonneListes;

    public InfosClasseur Infos { get; }
    public StylesExcel Styles { get; }

    public ClasseurExcel(InfosClasseur infos)
    {
        Infos = infos;
        Styles = new StylesExcel(infos.Palette);
        _classeur.Style.Font.FontName = StylesExcel.Police;
        _classeur.Style.Font.FontSize = StylesExcel.TailleTexte;
        var p = _classeur.Properties;
        p.Title = infos.Titre;
        p.Subject = infos.Sujet ?? infos.Titre;
        p.Author = infos.Application;
        p.LastModifiedBy = infos.Application;
        p.Company = infos.Application;
        p.Comments = infos.Mention;
        p.Created = infos.GenereLe;
        p.Modified = infos.GenereLe;
    }

    /// <summary>
    /// Ajoute un onglet ; le nom est rendu valide pour Excel (<see cref="NomsFeuilles.Assainir"/>) et unique. Mise en page :
    /// paysage, A4, une page de large, marges réduites ; en-tête (titre du classeur, nom de l'onglet) et pied de page
    /// (mention de génération, numéro de page).
    /// </summary>
    public FeuilleExcel AjouterFeuille(string nom)
    {
        var nomValide = NomsFeuilles.Unique(NomsFeuilles.Assainir(nom), _noms);
        _noms.Add(nomValide);
        var feuille = _classeur.Worksheets.Add(nomValide);
        feuille.Style.Font.FontName = StylesExcel.Police;
        feuille.Style.Font.FontSize = StylesExcel.TailleTexte;
        feuille.ShowGridLines = false;
        var ps = feuille.PageSetup;
        ps.PageOrientation = XLPageOrientation.Landscape;
        ps.PaperSize = XLPaperSize.A4Paper;
        ps.FitToPages(1, 0);
        ps.Margins.Top = 0.6;
        ps.Margins.Bottom = 0.6;
        ps.Margins.Left = 0.4;
        ps.Margins.Right = 0.4;
        ps.Margins.Header = 0.25;
        ps.Margins.Footer = 0.25;
        ps.CenterHorizontally = true;
        ps.Header.Left.AddText(EchapperEnTete(Infos.Titre), XLHFOccurrence.AllPages);
        ps.Header.Right.AddText(XLHFPredefinedText.SheetName, XLHFOccurrence.AllPages);
        ps.Footer.Left.AddText(EchapperEnTete(Infos.Mention), XLHFOccurrence.AllPages);
        ps.Footer.Right.AddText("Page ", XLHFOccurrence.AllPages);
        ps.Footer.Right.AddText(XLHFPredefinedText.PageNumber, XLHFOccurrence.AllPages);
        ps.Footer.Right.AddText(" / ", XLHFOccurrence.AllPages);
        ps.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages, XLHFOccurrence.AllPages);
        return new FeuilleExcel(feuille, this);
    }

    /// <summary>Dans un en-tête ou un pied de page, « &amp; » introduit un code de mise en forme : il est doublé.</summary>
    private static string EchapperEnTete(string texte) => texte.Replace("&", "&&", StringComparison.Ordinal);

    /// <summary>
    /// Feuille masquée de contrôle : une paire clé / valeur par ligne (colonnes A et B), lue au retour du fichier par
    /// <see cref="LecteurClasseur.LireControle"/>. Elle est protégée.
    /// </summary>
    public FeuilleExcel AjouterFeuilleControle(string nom, IEnumerable<KeyValuePair<string, string>> valeurs)
    {
        var f = AjouterFeuille(nom);
        var ligne = 1;
        foreach (var (cle, valeur) in valeurs)
        {
            f.Ecrire(ligne, 1, cle, Format.Normal with { RetourALaLigne = false });
            f.Ecrire(ligne, 2, valeur, Format.Normal with { RetourALaLigne = false });
            ligne++;
        }
        f.Interne.Visibility = XLWorksheetVisibility.Hidden;
        f.Proteger();
        return f;
    }

    /// <summary>
    /// Liste de valeurs proposées dans les listes déroulantes, rangée dans l'onglet masqué « Listes » (une colonne par liste,
    /// valeurs distinctes sans tenir compte de la casse) et nommée « Liste_<paramref name="nom"/> » dans le classeur.
    /// Renvoie sa plage, ou null si la liste est vide.
    /// </summary>
    public IXLRange? Liste(string nom, IEnumerable<string> valeurs)
    {
        var distinctes = valeurs.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinctes.Count == 0) return null;
        if (_listes is null)
        {
            _listes = AjouterFeuille(FeuilleListes);
            _listes.Interne.Visibility = XLWorksheetVisibility.Hidden;
            _listes.Proteger();
        }
        var col = ++_colonneListes;
        _listes.Ecrire(1, col, nom, Format.Normal with { Gras = true, RetourALaLigne = false });
        for (var i = 0; i < distinctes.Count; i++) _listes.Ecrire(i + 2, col, distinctes[i], Format.Normal with { RetourALaLigne = false });
        _listes.LargeurColonne(col, Math.Min(60, Math.Max(12, distinctes.Max(v => v.Length) + 2)));
        var plage = _listes.Interne.Range(2, col, distinctes.Count + 1, col);
        var d = nom.Normalize(System.Text.NormalizationForm.FormD);
        var nomPlage = "Liste_" + new string(d.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (!_classeur.DefinedNames.Contains(nomPlage)) _classeur.DefinedNames.Add(nomPlage, plage);
        return plage;
    }

    /// <summary>Nom de tableau Excel valide et unique : lettres, chiffres et « _ » ASCII, préfixe « T_ ».</summary>
    internal string NomTable(string souhaite)
    {
        var d = souhaite.Normalize(System.Text.NormalizationForm.FormD);
        var s = new string(d.Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-' or '\'').Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        while (s.Contains("__", StringComparison.Ordinal)) s = s.Replace("__", "_", StringComparison.Ordinal);
        var nom = "T_" + (s.Length == 0 ? "Tableau" : s);
        if (nom.Length > 200) nom = nom[..200];
        var unique = nom;
        for (var i = 2; !_tables.Add(unique); i++) unique = $"{nom}_{i}";
        return unique;
    }

    /// <summary>
    /// Verrouille la structure du classeur : onglets impossibles à renommer, déplacer, supprimer, ajouter ou afficher
    /// (sans mot de passe : c'est un garde-fou contre les fausses manipulations, pas une sécurité).
    /// </summary>
    public void VerrouillerStructure() => _classeur.Protect(XLWorkbookProtectionElements.Windows); // éléments laissés libres : les fenêtres seulement

    /// <summary>
    /// Contenu du fichier .xlsx : premier onglet visible actif et seul sélectionné, curseur en A1 partout. Les formules sont
    /// calculées avant l'enregistrement quand c'est possible, pour que leurs valeurs s'affichent même sans recalcul.
    /// </summary>
    public byte[] Enregistrer()
    {
        var premiere = _classeur.Worksheets.FirstOrDefault(f => f.Visibility == XLWorksheetVisibility.Visible);
        foreach (var f in _classeur.Worksheets)
        {
            f.TabSelected = f == premiere;
            f.TabActive = f == premiere;
            f.SelectedRanges.RemoveAll();
            f.SelectedRanges.Add(f.Range(1, 1, 1, 1));
            f.ActiveCell = f.Cell(1, 1);
        }
        using var flux = new MemoryStream();
        try
        {
            _classeur.SaveAs(flux, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Formule que le calcul intégré ne sait pas évaluer : Excel la calculera à l'ouverture.
            flux.SetLength(0);
            _classeur.SaveAs(flux);
        }
        return flux.ToArray();
    }

    public void Dispose() => _classeur.Dispose();
}
