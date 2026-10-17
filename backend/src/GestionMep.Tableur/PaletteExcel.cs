using System.Globalization;

namespace GestionMep.Tableur;

/// <summary>
/// Couleurs des classeurs, en hexadécimal « RRGGBB » (thème clair seulement : un classeur s'imprime et se lit sur fond
/// blanc). Trois couleurs de marque : <b>primaire</b> (fond des en-têtes de tableau, liens), <b>secondaire</b> (titres des
/// onglets) et <b>tertiaire</b> (bandeaux d'intertitre) ; puis les neutres et les teintes d'état, modifiables une à une.
/// Une palette se passe au classeur (<see cref="InfosClasseur.Palette"/>) ; <see cref="Defaut"/> reprend les couleurs de
/// l'application. Le texte posé sur une couleur de fond est choisi pour rester lisible (blanc ou presque noir), et une
/// couleur trop claire pour du texte est remplacée par le texte foncé : toute palette de marque reste lisible.
/// </summary>
public sealed record PaletteExcel
{
    public string Primaire { get; }
    public string Secondaire { get; }
    public string Tertiaire { get; }

    /// <summary>Couleurs de l'interface : indigo (accent), indigo foncé (texte d'accent), indigo très pâle (fond d'accent).</summary>
    public static readonly PaletteExcel Defaut = new("4F46E5", "4338CA", "ECEAFD");

    /// <param name="primaire">Couleur forte : fond des en-têtes de tableau, liens.</param>
    /// <param name="secondaire">Couleur des titres ; remplacée par le texte foncé si elle est trop claire pour du texte.</param>
    /// <param name="tertiaire">Fond des intertitres ; éclaircie si elle est trop soutenue pour un fond.</param>
    /// <exception cref="ArgumentException">Couleur qui n'est pas au format « RRGGBB » (« # » initial toléré).</exception>
    public PaletteExcel(string primaire, string secondaire, string tertiaire)
    {
        Primaire = Normaliser(primaire, nameof(primaire));
        Secondaire = Normaliser(secondaire, nameof(secondaire));
        Tertiaire = Normaliser(tertiaire, nameof(tertiaire));
    }

    // ---------- Neutres ----------

    /// <summary>Texte courant (gris anthracite de l'interface).</summary>
    public string TexteFonce { get; init; } = "16161A";
    /// <summary>Texte secondaire (mentions, aides, valeurs absentes).</summary>
    public string TexteDiscret { get; init; } = "6B6B78";
    /// <summary>Filets des tableaux et des fiches.</summary>
    public string Filet { get; init; } = "D4D4DC";
    /// <summary>Fond des blocs de code.</summary>
    public string FondCode { get; init; } = "F3F3F7";
    /// <summary>Fond des exemples de saisie.</summary>
    public string FondExemple { get; init; } = "F6F6F8";
    /// <summary>Jaune pâle des cases à remplir (convention connue des tableurs).</summary>
    public string FondASaisir { get; init; } = "FFF7D6";

    // ---------- Teintes d'état (fond, texte), comme les pastilles de l'interface ----------

    public (string Fond, string Texte) Succes { get; init; } = ("DCFCE7", "14532D");
    public (string Fond, string Texte) Echec { get; init; } = ("FEE2E2", "7F1D1D");
    public (string Fond, string Texte) EnCours { get; init; } = ("DBEAFE", "1E3A8A");
    public (string Fond, string Texte) Attention { get; init; } = ("FEF3C7", "78350F");
    public (string Fond, string Texte) Attente { get; init; } = ("EEF0F4", "3F4756");
    public (string Fond, string Texte) Neutre { get; init; } = ("F7F7F9", "5D5D6A");

    /// <summary>Fond et texte d'une tonalité d'état.</summary>
    public (string Fond, string Texte) Couleurs(Ton ton) => ton switch
    {
        Ton.Succes => Succes,
        Ton.Echec => Echec,
        Ton.EnCours => EnCours,
        Ton.Attention => Attention,
        Ton.Attente => Attente,
        _ => Neutre,
    };

    // ---------- Rôles déduits ----------

    /// <summary>Fond des en-têtes de tableau : la primaire.</summary>
    public string FondEnTete => Primaire;
    /// <summary>Texte des en-têtes de tableau : blanc ou foncé selon la primaire.</summary>
    public string TexteEnTete => TexteSur(Primaire, TexteFonce);
    /// <summary>Titres des onglets : la secondaire si elle se lit sur fond blanc, sinon le texte foncé.</summary>
    public string TexteTitre => Contraste(Secondaire, "FFFFFF") >= 4.5 ? Secondaire : TexteFonce;
    /// <summary>Liens : la primaire si elle se lit sur fond blanc, sinon la couleur des titres.</summary>
    public string TexteLien => Contraste(Primaire, "FFFFFF") >= 4.5 ? Primaire : TexteTitre;
    /// <summary>Fond des intertitres : la tertiaire si elle est claire, sinon une version très éclaircie.</summary>
    public string FondSection => Luminance(Tertiaire) >= 0.75 ? Tertiaire : Eclaircir(Tertiaire, 0.85);
    /// <summary>Texte des intertitres.</summary>
    public string TexteSection => Contraste(TexteTitre, FondSection) >= 4.5 ? TexteTitre : TexteSur(FondSection, TexteFonce);

    private static string Normaliser(string couleur, string parametre)
    {
        var c = (couleur ?? "").Trim().TrimStart('#').ToUpperInvariant();
        if (c.Length != 6 || !int.TryParse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException($"Couleur « {couleur} » invalide : six chiffres hexadécimaux attendus, par exemple 4F46E5.", parametre);
        return c;
    }

    private static (double R, double V, double B) Composantes(string hex) =>
        (int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            int.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            int.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);

    /// <summary>Luminance relative (0 = noir, 1 = blanc), au sens des règles d'accessibilité WCAG.</summary>
    public static double Luminance(string hex)
    {
        static double Lineaire(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        var (r, v, b) = Composantes(Normaliser(hex, nameof(hex)));
        return 0.2126 * Lineaire(r) + 0.7152 * Lineaire(v) + 0.0722 * Lineaire(b);
    }

    /// <summary>Rapport de contraste entre deux couleurs (1 à 21 ; 4,5 au moins pour du texte courant).</summary>
    public static double Contraste(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Texte lisible sur un fond : blanc ou <paramref name="fonce"/>, celui qui contraste le plus.</summary>
    public static string TexteSur(string fond, string fonce = "16161A") => Contraste("FFFFFF", fond) >= Contraste(fonce, fond) ? "FFFFFF" : fonce;

    /// <summary>Couleur mélangée à du blanc : <paramref name="part"/> = 0 la laisse telle quelle, 1 donne du blanc.</summary>
    public static string Eclaircir(string hex, double part)
    {
        var (r, v, b) = Composantes(Normaliser(hex, nameof(hex)));
        string Canal(double c) => ((int)Math.Round((c + (1 - c) * Math.Clamp(part, 0, 1)) * 255)).ToString("X2", CultureInfo.InvariantCulture);
        return Canal(r) + Canal(v) + Canal(b);
    }
}
