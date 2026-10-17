namespace GestionMep.Tableur;

/// <summary>
/// Mise en forme d'une cellule écrite par <see cref="FeuilleExcel.Ecrire"/>. Les couleurs sont en hexadécimal « RRGGBB ».
/// Le texte est aligné en haut et, par défaut, renvoyé à la ligne pour rester lisible à l'écran comme à l'impression.
/// </summary>
public sealed record Format
{
    public bool Gras { get; init; }
    public bool Italique { get; init; }
    public double? Taille { get; init; }
    public string? Couleur { get; init; }
    public string? Fond { get; init; }
    /// <summary>Police à chasse fixe (code, commandes).</summary>
    public bool ChasseFixe { get; init; }
    public bool Centre { get; init; }
    public bool Droite { get; init; }
    /// <summary>Alignement à gauche forcé (une date ou un nombre s'alignent sinon à droite).</summary>
    public bool Gauche { get; init; }
    /// <summary>Filet fin tout autour (et entre les cellules d'une plage).</summary>
    public bool Bordure { get; init; }
    /// <summary>Cellule à remplir par le destinataire : seule modifiable une fois la feuille protégée.</summary>
    public bool ASaisir { get; init; }
    /// <summary>Format de nombre Excel (par exemple <see cref="StylesExcel.FormatDate"/>).</summary>
    public string? NombreFormat { get; init; }
    /// <summary>Format de date jour/mois/année.</summary>
    public bool Date { get; init; }
    /// <summary>Format de date et heure jour/mois/année heures:minutes.</summary>
    public bool DateHeure { get; init; }
    /// <summary>Renvoi à la ligne automatique (vrai par défaut).</summary>
    public bool RetourALaLigne { get; init; } = true;
    /// <summary>Centrage vertical (sinon aligné en haut).</summary>
    public bool CentreVerticalement { get; init; }

    public static readonly Format Normal = new();
}

/// <summary>Tonalité d'une valeur d'état (mise en forme conditionnelle) : mêmes teintes que les pastilles de l'interface.</summary>
public enum Ton { Neutre, Succes, Echec, EnCours, Attention, Attente }

/// <summary>Nature des valeurs d'une colonne de tableau : elle fixe le format de nombre et l'alignement.</summary>
public enum TypeColonne
{
    /// <summary>Texte, renvoyé à la ligne.</summary>
    Texte,
    /// <summary>Code, commande, identifiant : police à chasse fixe.</summary>
    Code,
    /// <summary>Nombre entier, aligné à droite.</summary>
    Entier,
    /// <summary>Nombre à une décimale, séparateur de milliers.</summary>
    Nombre,
    /// <summary>Date (jour/mois/année).</summary>
    Date,
    /// <summary>Date et heure (jour/mois/année heures:minutes).</summary>
    DateHeure,
    /// <summary>Durée en minutes (valeur entière), affichée « 1 h 05 » et additionnable dans Excel.</summary>
    Duree,
    /// <summary>Texte court centré (Oui/Non, états).</summary>
    Court,
}

/// <summary>
/// Styles des classeurs : polices, tailles, formats de nombre et mises en forme prêtes à l'emploi, aux couleurs de la
/// <see cref="PaletteExcel"/> du classeur. Un seul endroit à modifier pour changer l'allure de tous les fichiers.
/// </summary>
public sealed class StylesExcel
{
    public const string Police = "Calibri";
    public const string PoliceCode = "Consolas";
    /// <summary>Taille du texte courant (tableaux et fiches).</summary>
    public const double TailleTexte = 10;
    public const double TailleTitre = 16;

    public const string FormatDate = "dd/mm/yyyy";
    public const string FormatDateHeure = "dd/mm/yyyy hh:mm";
    /// <summary>Durée (fraction de jour) : « 1 h 05 », au-delà de 24 h compris.</summary>
    public const string FormatDuree = "[h]\" h \"mm";
    public const string FormatEntier = "0";
    public const string FormatDecimal = "#,##0.0";

    public PaletteExcel Palette { get; }

    public StylesExcel(PaletteExcel? palette = null) => Palette = palette ?? PaletteExcel.Defaut;

    /// <summary>Styles aux couleurs de l'application.</summary>
    public static readonly StylesExcel Defaut = new();

    /// <summary>Titre de l'onglet (couleur secondaire).</summary>
    public Format Titre => new() { Gras = true, Taille = TailleTitre, Couleur = Palette.TexteTitre, RetourALaLigne = false, CentreVerticalement = true };
    /// <summary>Ligne de contexte sous le titre.</summary>
    public Format Contexte => new() { Couleur = Palette.TexteFonce };
    /// <summary>Mention discrète (généré par…, aide).</summary>
    public Format Mention => new() { Italique = true, Taille = 9, Couleur = Palette.TexteDiscret };
    /// <summary>En-tête de tableau : fond primaire, texte blanc ou foncé selon le contraste.</summary>
    public Format EnTete => new() { Gras = true, Couleur = Palette.TexteEnTete, Fond = Palette.FondEnTete, Bordure = true, CentreVerticalement = true };
    /// <summary>Intertitre d'une fiche (bandeau tertiaire).</summary>
    public Format Section => new() { Gras = true, Couleur = Palette.TexteSection, Fond = Palette.FondSection, Bordure = true };
    /// <summary>Libellé d'une fiche (colonne de gauche).</summary>
    public Format Libelle => new() { Gras = true, Couleur = Palette.TexteFonce, Bordure = true };
    public Format Cellule => new() { Bordure = true, Couleur = Palette.TexteFonce };
    /// <summary>Valeur absente (« Non renseigné », tableau vide).</summary>
    public Format Vide => new() { Bordure = true, Italique = true, Couleur = Palette.TexteDiscret };
    public Format Code => new() { ChasseFixe = true, Fond = Palette.FondCode, Bordure = true, Couleur = Palette.TexteFonce };
    /// <summary>Case à remplir : fond jaune pâle, modifiable une fois la feuille protégée.</summary>
    public Format ASaisir => new() { ASaisir = true, Fond = Palette.FondASaisir, Bordure = true, Couleur = Palette.TexteFonce };
    /// <summary>Exemple de saisie : italique gris sur fond gris pâle.</summary>
    public Format Exemple => new() { Italique = true, Couleur = Palette.TexteDiscret, Fond = Palette.FondExemple, Bordure = true };
    /// <summary>Valeur aux couleurs d'une tonalité d'état.</summary>
    public Format Etat(Ton ton) => new() { Bordure = true, Centre = true, Fond = Palette.Couleurs(ton).Fond, Couleur = Palette.Couleurs(ton).Texte };

    /// <summary>Format de nombre d'un type de colonne (null : général).</summary>
    public static string? FormatNombre(TypeColonne type) => type switch
    {
        TypeColonne.Date => FormatDate,
        TypeColonne.DateHeure => FormatDateHeure,
        TypeColonne.Duree => FormatDuree,
        TypeColonne.Entier => FormatEntier,
        TypeColonne.Nombre => FormatDecimal,
        _ => null,
    };

    /// <summary>
    /// Hauteur estimée (en points) d'un texte renvoyé à la ligne dans une colonne de <paramref name="largeur"/> caractères :
    /// Excel ne recalcule pas seul la hauteur des lignes d'un fichier généré, ni celle des cellules fusionnées.
    /// </summary>
    public static double HauteurTexte(string texte, double largeur, double minimum = 15, bool code = false, double taille = TailleTexte)
    {
        // Caractères par ligne : la largeur de colonne Excel compte des « 0 » de la police par défaut ; le texte courant est
        // plus étroit en moyenne, le code (chasse fixe) un peu plus large.
        var parLigne = Math.Max(1, (int)(largeur * (code ? 1.05 : 1.3) * 11 / Math.Max(8, taille + 1)));
        var lignes = texte.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / (double)parLigne)));
        return Math.Min(409, Math.Max(minimum, lignes * (taille * 1.35) + 4));
    }
}
