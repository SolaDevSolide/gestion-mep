using System.Globalization;
using System.Text.RegularExpressions;

namespace GestionMep.Api.Services;

/// <summary>
/// Calculs de couleurs de la charte visuelle : conversions, contraste WCAG, couleurs dérivées. Couleurs « #rrggbb » en
/// minuscules. Copie exacte de <c>frontend/src/charte/couleurs.ts</c> (aperçu en direct de la console) : les deux passent
/// les mêmes cas de test (<c>vecteurs-charte.json</c>) ; toute modification se fait des deux côtés, dans le même ordre
/// d'opérations.
/// </summary>
public static partial class CouleursCharte
{
    public const string Blanc = "#ffffff";
    /// <summary>Texte foncé de l'application (--fg du thème clair).</summary>
    public const string Encre = "#16161a";

    public readonly record struct Rvb(double R, double V, double B);
    public readonly record struct Tsv(double T, double S, double V);
    public readonly record struct Tsl(double T, double S, double L);

    private static double Borne(double x, double min = 0, double max = 1) => Math.Min(max, Math.Max(min, x));
    /// <summary>Arrondi .5 vers le haut, comme Math.round en JavaScript.</summary>
    private static double Arrondi(double x) => Math.Floor(x + 0.5);

    [GeneratedRegex("^[0-9a-f]{3}$")]
    private static partial Regex Court();
    [GeneratedRegex("^[0-9a-f]{6}$")]
    private static partial Regex Long();

    /// <summary>« #RRGGBB », « #RGB », avec ou sans « # » → « #rrggbb » ; null si ce n'est pas une couleur.</summary>
    public static string? NormaliserHex(string? saisie)
    {
        if (saisie is null) return null;
        var s = saisie.Trim();
        if (s.StartsWith('#')) s = s[1..];
        s = s.ToLowerInvariant();
        if (Court().IsMatch(s)) s = string.Concat(s.Select(c => $"{c}{c}"));
        return Long().IsMatch(s) ? "#" + s : null;
    }

    public static Rvb HexVersRvb(string hex)
    {
        var h = (NormaliserHex(hex) ?? "#000000")[1..];
        return new(int.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string RvbVersHex(Rvb c)
    {
        static string X(double n) => ((int)Arrondi(Borne(n, 0, 255))).ToString("x2", CultureInfo.InvariantCulture);
        return $"#{X(c.R)}{X(c.V)}{X(c.B)}";
    }

    private static double Teinte(double r, double v, double b, double max, double d)
    {
        if (d == 0) return 0;
        double t;
        if (max == r) t = (v - b) / d % 6;
        else if (max == v) t = (b - r) / d + 2;
        else t = (r - v) / d + 4;
        t *= 60;
        if (t < 0) t += 360;
        return t;
    }

    public static Tsv RvbVersTsv(Rvb c)
    {
        double r = c.R / 255, v = c.V / 255, b = c.B / 255;
        var max = Math.Max(Math.Max(r, v), b);
        var d = max - Math.Min(Math.Min(r, v), b);
        return new(Teinte(r, v, b, max, d), max == 0 ? 0 : d / max, max);
    }

    private static (double, double, double) Secteur(double t, double c, double x) =>
        t < 60 ? (c, x, 0) : t < 120 ? (x, c, 0) : t < 180 ? (0, c, x) : t < 240 ? (0, x, c) : t < 300 ? (x, 0, c) : (c, 0, x);

    public static Rvb TsvVersRvb(Tsv c)
    {
        var chroma = c.V * c.S;
        var x = chroma * (1 - Math.Abs(c.T / 60 % 2 - 1));
        var m = c.V - chroma;
        var (a, b, d) = Secteur(c.T, chroma, x);
        return new((a + m) * 255, (b + m) * 255, (d + m) * 255);
    }

    public static Tsl RvbVersTsl(Rvb c)
    {
        double r = c.R / 255, v = c.V / 255, b = c.B / 255;
        var max = Math.Max(Math.Max(r, v), b);
        var min = Math.Min(Math.Min(r, v), b);
        var l = (max + min) / 2;
        var d = max - min;
        return new(Teinte(r, v, b, max, d), d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1)), l);
    }

    public static Rvb TslVersRvb(Tsl c)
    {
        var chroma = (1 - Math.Abs(2 * c.L - 1)) * c.S;
        var x = chroma * (1 - Math.Abs(c.T / 60 % 2 - 1));
        var m = c.L - chroma / 2;
        var (a, b, d) = Secteur(c.T, chroma, x);
        return new((a + m) * 255, (b + m) * 255, (d + m) * 255);
    }

    /// <summary>Luminosité TSL (0 noir, 1 blanc).</summary>
    public static double Luminosite(string hex) => RvbVersTsl(HexVersRvb(hex)).L;

    /// <summary>Même teinte, luminosité décalée de <paramref name="delta"/> (−1..1).</summary>
    public static string Decaler(string hex, double delta)
    {
        var c = RvbVersTsl(HexVersRvb(hex));
        return RvbVersHex(TslVersRvb(c with { L = Borne(c.L + delta) }));
    }

    /// <summary>Mélange : <paramref name="part"/> de <paramref name="a"/> dans <paramref name="b"/> (1 → a, 0 → b).</summary>
    public static string Melanger(string a, string b, double part)
    {
        var x = HexVersRvb(a);
        var y = HexVersRvb(b);
        return RvbVersHex(new(x.R * part + y.R * (1 - part), x.V * part + y.V * (1 - part), x.B * part + y.B * (1 - part)));
    }

    private static double Lineaire(double c)
    {
        var s = c / 255;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>Luminance relative WCAG (0 noir, 1 blanc).</summary>
    public static double LuminanceRelative(string hex)
    {
        var c = HexVersRvb(hex);
        return 0.2126 * Lineaire(c.R) + 0.7152 * Lineaire(c.V) + 0.0722 * Lineaire(c.B);
    }

    /// <summary>Rapport de contraste WCAG 2 (1 à 21).</summary>
    public static double Contraste(string a, string b)
    {
        var la = LuminanceRelative(a);
        var lb = LuminanceRelative(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Texte lisible posé sur un fond : blanc s'il atteint 4,5:1 ou contraste au moins autant que l'encre, sinon l'encre.</summary>
    public static string TexteSur(string fond)
    {
        var blanc = Contraste(Blanc, fond);
        return blanc >= 4.5 || blanc >= Contraste(Encre, fond) ? Blanc : Encre;
    }

    /// <summary>
    /// Même teinte, luminosité ajustée pas à pas jusqu'à atteindre <paramref name="cible"/> sur <paramref name="fond"/> : la
    /// couleur s'éloigne du fond (assombrie si elle est plus foncée que lui, éclaircie sinon ; à égalité, assombrie sur un fond clair).
    /// </summary>
    public static string AjusterPourContraste(string hex, string fond, double cible)
    {
        if (Contraste(hex, fond) >= cible) return hex;
        var lc = LuminanceRelative(hex);
        var lf = LuminanceRelative(fond);
        var sens = lc < lf ? -1 : lc > lf ? 1 : lf > 0.4 ? -1 : 1;
        var c = hex;
        for (var i = 0; i < 100 && Contraste(c, fond) < cible; i++) c = Decaler(c, sens * 0.01);
        return c;
    }

    /// <summary>Écart de teinte (0..180), pour signaler une couleur de charte trop proche d'une couleur d'état.</summary>
    public static double EcartTeinte(string a, string b)
    {
        var ta = RvbVersTsv(HexVersRvb(a)).T;
        var tb = RvbVersTsv(HexVersRvb(b)).T;
        var d = Math.Abs(ta - tb) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>« 4,5:1 » (tronqué : un rapport sous le minimum ne s'affiche jamais égal au minimum).</summary>
    public static string FormatRapport(double r) =>
        (Math.Floor(r * 10) / 10).ToString("0.0", CultureInfo.GetCultureInfo("fr-FR")) + ":1";
}
