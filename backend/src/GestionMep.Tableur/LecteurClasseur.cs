using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;

namespace GestionMep.Tableur;

/// <summary>Fichier qui n'est pas un classeur .xlsx lisible (ou trop gros une fois décompressé) : le message est destiné à l'utilisateur.</summary>
public sealed class ClasseurIllisibleException(string message) : Exception(message);

/// <summary>
/// Classeur Excel (.xlsx) en lecture : brique commune des importations. Le fichier est vérifié avant d'être ouvert
/// (archive zip valide, taille décompressée bornée) ; toute erreur de lecture devient <see cref="ClasseurIllisibleException"/>.
/// </summary>
public sealed class LecteurClasseur : IDisposable
{
    /// <summary>Taille décompressée maximale acceptée (protège contre les archives piégées).</summary>
    public const long TailleDecompresseeMax = 64L * 1024 * 1024;
    private const string MessageIllisible = "Fichier illisible : un classeur Excel (.xlsx) est attendu.";

    private readonly XLWorkbook _classeur;

    private LecteurClasseur(XLWorkbook classeur) => _classeur = classeur;

    /// <summary>Ouvre un classeur depuis son contenu.</summary>
    public static LecteurClasseur Ouvrir(byte[] contenu)
    {
        VerifierArchive(contenu);
        try
        {
            return new LecteurClasseur(new XLWorkbook(new MemoryStream(contenu, writable: false)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ClasseurIllisibleException(MessageIllisible);
        }
    }

    private static void VerifierArchive(byte[] contenu)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(contenu, writable: false), ZipArchiveMode.Read);
            long total = 0;
            foreach (var e in zip.Entries)
            {
                total += e.Length;
                if (total > TailleDecompresseeMax) throw new ClasseurIllisibleException("Classeur trop volumineux une fois décompressé.");
            }
            if (zip.GetEntry("[Content_Types].xml") is null) throw new ClasseurIllisibleException(MessageIllisible);
        }
        catch (InvalidDataException)
        {
            throw new ClasseurIllisibleException(MessageIllisible);
        }
    }

    /// <summary>Noms des onglets, dans l'ordre du classeur (masqués compris).</summary>
    public IReadOnlyList<string> Feuilles => _classeur.Worksheets.Select(f => f.Name).ToList();

    /// <summary>Feuille par son nom exact (la casse compte : un onglet renommé n'est pas retrouvé), ou null.</summary>
    public FeuilleLue? Feuille(string nom) =>
        _classeur.Worksheets.FirstOrDefault(f => string.Equals(f.Name, nom, StringComparison.Ordinal)) is { } f ? new FeuilleLue(f) : null;

    /// <summary>
    /// Feuille par son nom sans tenir compte de la casse, des accents, des espaces ni de la ponctuation (« objets impactes »
    /// trouve « Objets impactés »), ou null.
    /// </summary>
    public FeuilleLue? FeuilleApprochee(string nom) => Feuille(nom)
        ?? (_classeur.Worksheets.FirstOrDefault(f => Cle(f.Name) == Cle(nom)) is { } f ? new FeuilleLue(f) : null);

    /// <summary>Toutes les feuilles, dans l'ordre du classeur (masquées comprises).</summary>
    public IEnumerable<FeuilleLue> Toutes => _classeur.Worksheets.Select(f => new FeuilleLue(f));

    /// <summary>
    /// Forme de comparaison d'un en-tête ou d'un nom : minuscules sans accents, lettres et chiffres seulement, mots séparés
    /// par une espace (« Version / correction* » → « version correction »).
    /// </summary>
    public static string Cle(string? texte)
    {
        var d = (texte ?? "").Replace("œ", "oe", StringComparison.Ordinal).Replace("Œ", "oe", StringComparison.Ordinal)
            .Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    /// <summary>Paires clé / valeur d'une feuille de contrôle (colonnes A et B, jusqu'à la première ligne vide), ou null si elle manque.</summary>
    public IReadOnlyDictionary<string, string>? LireControle(string nom)
    {
        if (Feuille(nom) is not { } f) return null;
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var ligne = 1; ligne <= 10_000; ligne++)
        {
            var cle = f.Texte(ligne, 1);
            if (cle.Length == 0) break;
            valeurs[cle] = f.Texte(ligne, 2);
        }
        return valeurs;
    }

    public void Dispose() => _classeur.Dispose();
}

/// <summary>Feuille d'un classeur lu : lignes et colonnes numérotées à partir de 1.</summary>
public sealed class FeuilleLue
{
    private readonly IXLWorksheet _feuille;

    internal FeuilleLue(IXLWorksheet feuille) => _feuille = feuille;

    public string Nom => _feuille.Name;

    /// <summary>Faux pour un onglet masqué.</summary>
    public bool Visible => _feuille.Visibility == XLWorksheetVisibility.Visible;

    /// <summary>Dernière ligne utilisée (0 si la feuille est vide).</summary>
    public int DerniereLigne => _feuille.LastRowUsed()?.RowNumber() ?? 0;

    /// <summary>Dernière colonne utilisée (0 si la feuille est vide).</summary>
    public int DerniereColonne => _feuille.LastColumnUsed()?.ColumnNumber() ?? 0;

    /// <summary>
    /// Repère la ligne d'en-têtes d'un tableau dans les <paramref name="lignesMax"/> premières lignes : la première qui
    /// contient l'en-tête <paramref name="requis"/>. <paramref name="synonymes"/> associe la forme comparable d'un en-tête
    /// (<see cref="LecteurClasseur.Cle"/>) au nom de colonne attendu. Les colonnes peuvent être dans n'importe quel ordre ;
    /// renvoie la ligne et la colonne de chaque nom trouvé, ou null.
    /// </summary>
    public (int Ligne, IReadOnlyDictionary<string, int> Colonnes)? EnTetes(IReadOnlyDictionary<string, string> synonymes, string requis,
        int lignesMax = 20, int colonnesMax = 40)
    {
        for (var r = 1; r <= lignesMax; r++)
        {
            var trouvees = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var c = 1; c <= colonnesMax; c++)
            {
                if (synonymes.TryGetValue(LecteurClasseur.Cle(Texte(r, c)), out var nom) && !trouvees.ContainsKey(nom)) trouvees[nom] = c;
            }
            if (trouvees.ContainsKey(requis)) return (r, trouvees);
        }
        return null;
    }

    /// <summary>Texte affiché d'une cellule, sans les blancs autour (« » si vide). Une formule donne son dernier résultat calculé.</summary>
    public string Texte(int ligne, int colonne)
    {
        var c = _feuille.Cell(ligne, colonne);
        try
        {
            var v = c.HasFormula ? c.CachedValue : c.Value;
            return v.Type switch
            {
                XLDataType.Blank => "",
                XLDataType.Text => v.GetText().Trim(),
                XLDataType.Number => v.GetNumber().ToString(CultureInfo.InvariantCulture),
                XLDataType.Boolean => v.GetBoolean() ? "VRAI" : "FAUX",
                XLDataType.DateTime => v.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                _ => c.GetFormattedString().Trim(),
            };
        }
        catch (Exception ex) when (ex is InvalidCastException or ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>
    /// Date d'une cellule : vraie date Excel, nombre de jours Excel, ou texte jour/mois/année (« 18/10/2026 », « 2026-10-18 »).
    /// <c>Vide</c> si la cellule est vide ; <c>Lisible</c> faux si elle contient autre chose qu'une date.
    /// </summary>
    public (bool Vide, bool Lisible, DateOnly? Date) Date(int ligne, int colonne)
    {
        var c = _feuille.Cell(ligne, colonne);
        XLCellValue v;
        try
        {
            v = c.HasFormula ? c.CachedValue : c.Value;
        }
        catch (Exception ex) when (ex is InvalidCastException or ArgumentException or InvalidOperationException)
        {
            return (false, false, null);
        }
        switch (v.Type)
        {
            case XLDataType.Blank:
                return (true, true, null);
            case XLDataType.DateTime:
                return (false, true, DateOnly.FromDateTime(v.GetDateTime()));
            case XLDataType.Number when v.GetNumber() is >= 1 and < 2_958_466:
                return (false, true, DateOnly.FromDateTime(DateTime.FromOADate(v.GetNumber())));
            case XLDataType.Text:
                var t = v.GetText().Trim();
                if (t.Length == 0) return (true, true, null);
                string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd.MM.yyyy", "dd-MM-yyyy"];
                return DateOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? (false, true, d) : (false, false, null);
            default:
                return (false, false, null);
        }
    }
}
