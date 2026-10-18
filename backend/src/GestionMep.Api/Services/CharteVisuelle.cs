using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Tableur;
using static GestionMep.Api.Services.CouleursCharte;

namespace GestionMep.Api.Services;

/// <summary>Thème de l'interface : la charte a une version claire et une version sombre de chaque couleur.</summary>
public enum ModeCharte { Clair, Sombre }

/// <summary>
/// Charte visuelle normalisée (couleurs « #rrggbb » en minuscules). <see cref="Tertiaire"/> vaut
/// <see cref="CharteVisuelle.SuitPrimaire"/> quand elle suit la primaire. <see cref="Sombre"/> : retouches du thème sombre
/// (rôle → couleur), sinon calculé ; <see cref="Etats"/> : couleurs d'état personnalisées, sinon standard.
/// </summary>
public sealed record Charte
{
    public string Nom { get; init; } = CharteVisuelle.NomDefaut;
    public string Primaire { get; init; } = "#4f46e5";
    public string Secondaire { get; init; } = "#0d0e1a";
    public string Tertiaire { get; init; } = CharteVisuelle.SuitPrimaire;
    /// <summary>« pages » (fond du menu comme les pages) ou « secondaire ».</summary>
    public string MenuLateral { get; init; } = "pages";
    /// <summary>« froids », « purs », « chauds » ou « teintes ».</summary>
    public string Gris { get; init; } = "froids";
    public IReadOnlyDictionary<string, string> Sombre { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Etats { get; init; } = new Dictionary<string, string>();

    public bool TertiaireLiee => Tertiaire == CharteVisuelle.SuitPrimaire;
}

// ---------- Forme du fichier charte.json et des échanges avec l'API ----------

/// <summary>Fichier <c>charte.json</c> (version 1) : seule <c>couleurs.primaire</c> est obligatoire. Couleurs « #RRGGBB » ou « #RGB ».</summary>
public sealed record CharteFichier
{
    [JsonPropertyName("$schema"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Version { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nom { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CouleursCharteFichier? Couleurs { get; init; }
    /// <summary>« pages » ou « secondaire ».</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MenuLateral { get; init; }
    /// <summary>« froids », « purs », « chauds » ou « teintes ».</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Gris { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RetouchesCharteFichier? Sombre { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EtatsCharteFichier? Etats { get; init; }
}

public sealed record CouleursCharteFichier
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Primaire { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Secondaire { get; init; }
    /// <summary>Couleur, ou « primaire » pour suivre la primaire.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tertiaire { get; init; }
}

public sealed record RetouchesCharteFichier
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Primaire { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Secondaire { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tertiaire { get; init; }
}

public sealed record EtatsCharteFichier
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Succes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Avertissement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Erreur { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Info { get; init; }
}

/// <summary>Erreur (bloquante) ou avertissement sur une charte. <paramref name="Ligne"/> : ligne du fichier, quand elle est connue.</summary>
public sealed record ProblemeCharte(string Code, string Champ, string Message, int? Ligne = null);

public sealed record LectureCharte(Charte? Charte, List<ProblemeCharte> Erreurs, List<ProblemeCharte> Avertissements);

/// <summary>Correction proposée par « Corriger » : même teinte, luminosité ajustée.</summary>
public sealed record CorrectionCharte(string Cible, string Couleur);

/// <summary>Contrôle de lisibilité (contraste WCAG AA) ou de ressemblance avec une couleur d'état.</summary>
public sealed record ControleCharte(string Id, string Libelle, ModeCharte Mode, string Type, string Role, string Avant, string Arriere,
    double Rapport, double Minimum, CorrectionCharte? Correction, string? Message)
{
    public bool Reussi => Type == "contraste" && Rapport >= Minimum;
}

/// <summary>
/// Charte visuelle de l'application : couleurs primaire, secondaire et tertiaire, gris, menu latéral, retouches du thème
/// sombre et couleurs d'état. Ce code fait foi : il lit et valide le fichier <c>charte.json</c>, calcule les jetons de
/// couleur des deux thèmes (servis par <c>/charte.css</c>), contrôle la lisibilité et donne la palette des exports Excel.
/// La console en a une copie en TypeScript (<c>frontend/src/charte/charte.ts</c>) pour l'aperçu en direct ; les deux
/// passent les mêmes cas de test (<c>frontend/src/charte/vecteurs-charte.json</c>).
/// </summary>
public static class CharteVisuelle
{
    public const int VersionFichier = 1;
    public const int LongueurNom = 80;
    public const string NomDefaut = "Charte par défaut";
    /// <summary>Valeur de <c>couleurs.tertiaire</c> qui fait suivre la primaire.</summary>
    public const string SuitPrimaire = "primaire";

    public static readonly string[] Roles = ["primaire", "secondaire", "tertiaire"];
    public static readonly string[] EtatsConnus = ["succes", "avertissement", "erreur", "info"];
    public static readonly string[] MenusLateraux = ["pages", "secondaire"];
    public static readonly string[] GrisConnus = ["froids", "purs", "chauds", "teintes"];

    public static readonly Charte Defaut = new();

    /// <summary>État → famille de jetons, libellé, nom dans les contrôles.</summary>
    private static readonly (string Etat, string Famille, string Libelle, string Nom)[] Etats =
    [
        ("succes", "ok", "Succès", "vert « Réussie »"),
        ("avertissement", "warn", "Avertissement", "orange « Résolution »"),
        ("erreur", "danger", "Erreur", "rouge « Échec »"),
        ("info", "run", "Information", "bleu « En cours »"),
    ];

    public static string LibelleRole(string role) => role switch
    {
        "primaire" => "Primaire",
        "secondaire" => "Secondaire",
        "tertiaire" => "Tertiaire",
        _ => Etats.FirstOrDefault(e => e.Etat == role).Libelle ?? role,
    };

    // ---------- Jetons par défaut (valeurs résolues de frontend/src/index.css, copie de defauts.ts) ----------

    private static readonly (string Cle, string Valeur)[] Connexion =
    [
        ("connexion-logo", "oklch(58.5% 0.233 277.117)"),
        ("connexion-logo-fg", "#ffffff"),
        ("connexion-halo-1", "oklch(51.1% 0.262 276.966)"),
        ("connexion-halo-2", "oklch(68.5% 0.169 237.323)"),
        ("connexion-texte", "oklch(93% 0.034 272.788)"),
        ("connexion-discret", "oklch(87% 0.065 274.039)"),
    ];

    private static readonly (string, string)[] TableClaire =
    [
        ("bg", "#f6f6f8"), ("surface", "#ffffff"), ("surface-2", "#f1f1f4"), ("sunken", "#ececf0"), ("line", "#e2e2e8"),
        ("line-strong", "#c9c9d3"), ("fg", "#16161a"), ("fg-muted", "#4c4c57"), ("fg-subtle", "#6b6b78"),
        ("accent", "#4f46e5"), ("accent-hover", "#4338ca"), ("accent-text", "#4338ca"), ("accent-soft", "#eceafd"), ("accent-fg", "#ffffff"),
        ("code-bg", "#f3f3f7"), ("code-fg", "#1d1d26"),
        ("ok-bg", "#dcfce7"), ("ok-fg", "#14532d"), ("ok-bd", "#86efac"), ("ok-solid", "#16a34a"),
        ("run-bg", "#dbeafe"), ("run-fg", "#1e3a8a"), ("run-bd", "#93c5fd"), ("run-solid", "#2563eb"),
        ("wait-bg", "#eef0f4"), ("wait-fg", "#3f4756"), ("wait-bd", "#cbd1db"), ("wait-solid", "#6b7482"),
        ("done-bg", "#166534"), ("done-fg", "#ffffff"), ("done-bd", "#14532d"), ("done-solid", "#166534"),
        ("danger-bg", "#fee2e2"), ("danger-fg", "#7f1d1d"), ("danger-bd", "#fca5a5"), ("danger-solid", "#dc2626"),
        ("warn-bg", "#fef3c7"), ("warn-fg", "#78350f"), ("warn-bd", "#fcd34d"), ("warn-solid", "#d97706"),
        ("later-bg", "#f7f7f9"), ("later-fg", "#5d5d6a"), ("later-bd", "#d6d6de"), ("later-solid", "#a1a1ad"),
        ("rollback-bg", "#b91c1c"), ("rollback-fg", "#ffffff"), ("rollback-bd", "#7f1d1d"), ("rollback-solid", "#b91c1c"),
        ("tertiaire", "#4f46e5"), ("tertiaire-hover", "#4338ca"), ("tertiaire-text", "#4338ca"), ("tertiaire-soft", "#eceafd"), ("tertiaire-fg", "#ffffff"),
        ("secondaire", "#0d0e1a"), ("secondaire-fg", "#ffffff"),
        ("nav-bg", "#ffffff"), ("nav-fg", "#16161a"), ("nav-muted", "#4c4c57"), ("nav-subtle", "#6b6b78"), ("nav-hover", "#f1f1f4"),
        ("nav-active", "#eceafd"), ("nav-active-fg", "#4338ca"), ("nav-line", "#e2e2e8"), ("nav-line-strong", "#c9c9d3"), ("nav-input", "#f1f1f4"),
        .. Connexion,
    ];

    private static readonly (string, string)[] TableSombre =
    [
        ("bg", "#0b0c0f"), ("surface", "#121318"), ("surface-2", "#1a1c23"), ("sunken", "#08090b"), ("line", "#262831"),
        ("line-strong", "#3a3d49"), ("fg", "#ededf1"), ("fg-muted", "#b3b3bf"), ("fg-subtle", "#8f8f9c"),
        ("accent", "#5b54f0"), ("accent-hover", "#6d66f5"), ("accent-text", "#a9a5ff"), ("accent-soft", "#1f1d3f"), ("accent-fg", "#ffffff"),
        ("code-bg", "#0a0b0e"), ("code-fg", "#e4e4ec"),
        ("ok-bg", "#0b2e18"), ("ok-fg", "#86efac"), ("ok-bd", "#166534"), ("ok-solid", "#22c55e"),
        ("run-bg", "#0f2247"), ("run-fg", "#93c5fd"), ("run-bd", "#1e40af"), ("run-solid", "#3b82f6"),
        ("wait-bg", "#1c1f27"), ("wait-fg", "#c5cad5"), ("wait-bd", "#343946"), ("wait-solid", "#8791a3"),
        ("done-bg", "#14532d"), ("done-fg", "#dcfce7"), ("done-bd", "#166534"), ("done-solid", "#22a55a"),
        ("danger-bg", "#3b0d0d"), ("danger-fg", "#fca5a5"), ("danger-bd", "#7f1d1d"), ("danger-solid", "#ef4444"),
        ("warn-bg", "#3a2406"), ("warn-fg", "#fcd34d"), ("warn-bd", "#78350f"), ("warn-solid", "#f59e0b"),
        ("later-bg", "#14151a"), ("later-fg", "#a1a1ae"), ("later-bd", "#2c2e38"), ("later-solid", "#5f6270"),
        ("rollback-bg", "#c81e1e"), ("rollback-fg", "#ffffff"), ("rollback-bd", "#fca5a5"), ("rollback-solid", "#ef4444"),
        ("tertiaire", "#5b54f0"), ("tertiaire-hover", "#6d66f5"), ("tertiaire-text", "#a9a5ff"), ("tertiaire-soft", "#1f1d3f"), ("tertiaire-fg", "#ffffff"),
        ("secondaire", "#0d0e1a"), ("secondaire-fg", "#ffffff"),
        ("nav-bg", "#121318"), ("nav-fg", "#ededf1"), ("nav-muted", "#b3b3bf"), ("nav-subtle", "#8f8f9c"), ("nav-hover", "#1a1c23"),
        ("nav-active", "#1f1d3f"), ("nav-active-fg", "#a9a5ff"), ("nav-line", "#262831"), ("nav-line-strong", "#3a3d49"), ("nav-input", "#1a1c23"),
        .. Connexion,
    ];

    /// <summary>Jetons du thème par défaut, dans l'ordre de index.css.</summary>
    public static IReadOnlyList<(string Cle, string Valeur)> JetonsDefaut(ModeCharte mode) => mode == ModeCharte.Clair ? TableClaire : TableSombre;

    private static Dictionary<string, string> TableDefaut(ModeCharte mode) => JetonsDefaut(mode).ToDictionary(x => x.Cle, x => x.Valeur);

    private static readonly Dictionary<string, string> DefautClair = TableDefaut(ModeCharte.Clair);
    private static readonly Dictionary<string, string> DefautSombre = TableDefaut(ModeCharte.Sombre);

    private static Dictionary<string, string> Defauts(ModeCharte mode) => mode == ModeCharte.Clair ? DefautClair : DefautSombre;

    /// <summary>Couleurs d'état standard (pastilles pleines du thème clair).</summary>
    public static string EtatDefaut(string etat) => DefautClair[Etats.First(e => e.Etat == etat).Famille + "-solid"];

    private static readonly Dictionary<string, Dictionary<ModeCharte, (string, string)[]>> GrisFixes = new()
    {
        ["purs"] = new()
        {
            [ModeCharte.Clair] = [("bg", "#f6f6f6"), ("surface-2", "#f2f2f2"), ("sunken", "#ececec"), ("line", "#e3e3e3"), ("line-strong", "#cacaca"), ("fg-muted", "#4d4d4d"), ("fg-subtle", "#6b6b6b")],
            [ModeCharte.Sombre] = [("bg", "#0c0c0c"), ("surface", "#131313"), ("surface-2", "#1b1b1b"), ("sunken", "#090909"), ("line", "#282828"), ("line-strong", "#3c3c3c")],
        },
        ["chauds"] = new()
        {
            [ModeCharte.Clair] = [("bg", "#f7f6f3"), ("surface-2", "#f2f0eb"), ("sunken", "#ece9e3"), ("line", "#e4e0d8"), ("line-strong", "#cdc7bb"), ("fg-muted", "#514c44"), ("fg-subtle", "#716a60")],
            [ModeCharte.Sombre] = [("bg", "#0f0e0c"), ("surface", "#171613"), ("surface-2", "#1f1d19"), ("sunken", "#0b0a09"), ("line", "#2c2a25"), ("line-strong", "#403c35")],
        },
    };

    private static readonly string[] GrisTeintes = ["bg", "surface-2", "sunken", "line", "line-strong"];
    private static readonly string[] Suffixes = ["", "-hover", "-text", "-soft", "-fg"];

    // ---------- Couleurs des rôles ----------

    /// <summary>Couleur saisie d'un rôle (la tertiaire liée vaut la primaire).</summary>
    public static string CouleurSaisie(Charte c, string role) => role switch
    {
        "primaire" => c.Primaire,
        "secondaire" => c.Secondaire,
        _ => c.TertiaireLiee ? c.Primaire : c.Tertiaire,
    };

    /// <summary>Couleur d'un rôle dans un thème : la retouche du thème sombre, sinon la couleur calculée.</summary>
    public static string CouleurRole(Charte c, string role, ModeCharte mode)
    {
        var b = CouleurSaisie(c, role);
        if (mode == ModeCharte.Clair) return b;
        var retouche = role == "tertiaire" && c.TertiaireLiee ? c.Sombre.GetValueOrDefault("primaire") : c.Sombre.GetValueOrDefault(role);
        if (retouche is not null) return retouche;
        if (role == "secondaire")
        {
            if (b == Defaut.Secondaire) return b;
            return Melanger(b, DefautSombre["bg"], Luminosite(b) > 0.3 ? 0.35 : 0.7);
        }
        // La primaire par défaut garde sa version sombre d'aujourd'hui.
        if (b == Defaut.Primaire) return DefautSombre["accent"];
        return Luminosite(b) < 0.6 ? Decaler(b, 0.06) : b;
    }

    private static string PrimaireDefaut(ModeCharte mode) => Defauts(mode)["accent"];

    private static IEnumerable<(string, string)> Famille(string prefixe, string b, ModeCharte mode, string fond)
    {
        if (b == PrimaireDefaut(mode))
        {
            var d = Defauts(mode);
            foreach (var s in Suffixes) yield return (prefixe + s, d["accent" + s]);
            yield break;
        }
        if (mode == ModeCharte.Clair)
        {
            var soft = Melanger(b, Blanc, 0.1);
            yield return (prefixe, b);
            yield return (prefixe + "-hover", Decaler(b, -0.07));
            yield return (prefixe + "-text", AjusterPourContraste(b, soft, 4.6));
            yield return (prefixe + "-soft", soft);
            yield return (prefixe + "-fg", TexteSur(b));
            yield break;
        }
        var pale = Melanger(b, fond, 0.2);
        yield return (prefixe, b);
        yield return (prefixe + "-hover", Decaler(b, 0.06));
        yield return (prefixe + "-text", AjusterPourContraste(Decaler(b, 0.18), pale, 6));
        yield return (prefixe + "-soft", pale);
        yield return (prefixe + "-fg", TexteSur(b));
    }

    private static IEnumerable<(string, string)> Nav(string fond)
    {
        var fg = TexteSur(fond);
        yield return ("nav-bg", fond);
        yield return ("nav-fg", fg);
        yield return ("nav-muted", AjusterPourContraste(Melanger(fg, fond, 0.72), fond, 4.6));
        yield return ("nav-subtle", AjusterPourContraste(Melanger(fg, fond, 0.6), fond, 4.5));
        yield return ("nav-hover", Melanger(fg, fond, 0.08));
        yield return ("nav-active", Melanger(fg, fond, 0.16));
        yield return ("nav-active-fg", fg);
        yield return ("nav-line", Melanger(fg, fond, 0.14));
        yield return ("nav-line-strong", Melanger(fg, fond, 0.26));
        yield return ("nav-input", Melanger(fg, fond, 0.07));
    }

    private static IEnumerable<(string, string)> Etat(string b, ModeCharte mode)
    {
        if (mode == ModeCharte.Clair)
        {
            var bg = Melanger(b, Blanc, 0.16);
            yield return ("bg", bg);
            yield return ("bd", Melanger(b, Blanc, 0.5));
            yield return ("solid", b);
            yield return ("fg", AjusterPourContraste(Decaler(b, -0.25), bg, 7));
            yield break;
        }
        var fond = DefautSombre["surface"];
        var bgs = Melanger(b, fond, 0.22);
        yield return ("bg", bgs);
        yield return ("bd", Melanger(b, fond, 0.55));
        yield return ("solid", Decaler(b, 0.05));
        yield return ("fg", AjusterPourContraste(Decaler(b, 0.25), bgs, 7));
    }

    private static void Affecter(Dictionary<string, string> t, IEnumerable<(string Cle, string Valeur)> valeurs)
    {
        foreach (var (cle, valeur) in valeurs) t[cle] = valeur;
    }

    /// <summary>Tous les jetons d'un thème avec la charte appliquée, dans l'ordre de index.css.</summary>
    public static Dictionary<string, string> Jetons(Charte c, ModeCharte mode)
    {
        var d = Defauts(mode);
        var t = new Dictionary<string, string>(d);
        var p = CouleurRole(c, "primaire", mode);
        if (GrisFixes.TryGetValue(c.Gris, out var gris)) Affecter(t, gris[mode]);
        if (c.Gris == "teintes")
            foreach (var k in GrisTeintes) t[k] = Melanger(p, d[k], mode == ModeCharte.Clair ? 0.05 : 0.07);
        var fond = mode == ModeCharte.Clair ? Blanc : t["surface"];
        Affecter(t, Famille("accent", p, mode, fond).ToList());
        var ter = CouleurRole(c, "tertiaire", mode);
        if (c.TertiaireLiee)
            foreach (var s in Suffixes) t["tertiaire" + s] = t["accent" + s];
        else Affecter(t, Famille("tertiaire", ter, mode, fond).ToList());
        var sec = CouleurRole(c, "secondaire", mode);
        t["secondaire"] = sec;
        t["secondaire-fg"] = TexteSur(sec);
        if (c.MenuLateral == "secondaire") Affecter(t, Nav(sec).ToList());
        else
            Affecter(t,
            [
                ("nav-bg", t["surface"]), ("nav-fg", t["fg"]), ("nav-muted", t["fg-muted"]), ("nav-subtle", t["fg-subtle"]),
                ("nav-hover", t["surface-2"]), ("nav-active", t["accent-soft"]), ("nav-active-fg", t["accent-text"]), ("nav-line", t["line"]),
                ("nav-line-strong", t["line-strong"]), ("nav-input", t["surface-2"]),
            ]);
        foreach (var (etat, famille, _, _) in Etats)
        {
            if (c.Etats.GetValueOrDefault(etat) is not { } b) continue;
            foreach (var (k, v) in Etat(b, mode).ToList()) t[$"{famille}-{k}"] = v;
            if (etat == "succes")
            {
                var fonce = Decaler(b, -0.12);
                Affecter(t, [("done-bg", fonce), ("done-solid", fonce), ("done-bd", Decaler(b, -0.18))]);
            }
            if (etat == "erreur")
            {
                var fonce = Decaler(b, -0.05);
                Affecter(t, [("rollback-bg", fonce), ("rollback-solid", fonce)]);
            }
        }
        // Page de connexion : panneau en secondaire, logo et halos aux couleurs de la charte.
        if (p != PrimaireDefaut(mode))
        {
            t["connexion-logo"] = p;
            t["connexion-logo-fg"] = TexteSur(p);
            t["connexion-halo-1"] = p;
        }
        if (ter != PrimaireDefaut(mode)) t["connexion-halo-2"] = ter;
        if (sec != Defaut.Secondaire)
        {
            t["connexion-texte"] = t["secondaire-fg"];
            t["connexion-discret"] = t["secondaire-fg"];
        }
        return t;
    }

    /// <summary>Jetons qui diffèrent du thème par défaut, dans l'ordre de index.css. Vide pour la charte par défaut.</summary>
    public static List<(string Cle, string Valeur)> Ecarts(Charte c, ModeCharte mode)
    {
        var t = Jetons(c, mode);
        return [.. JetonsDefaut(mode).Where(x => t[x.Cle] != x.Valeur).Select(x => (x.Cle, t[x.Cle]))];
    }

    // ---------- Feuille de style ----------

    /// <summary>
    /// Feuille <c>/charte.css</c> : redéfinit les variables CSS qui changent, pour chaque thème. Les sélecteurs
    /// <c>:root:root:not(.dark)</c> et <c>:root:root.dark</c> l'emportent sur index.css quel que soit l'ordre de chargement.
    /// La première règle porte l'empreinte, que les onglets ouverts comparent à celle de /api/version.
    /// </summary>
    public static (string Css, string Empreinte) FeuilleCss(Charte c)
    {
        var blocs = new StringBuilder();
        foreach (var (mode, selecteur) in new[] { (ModeCharte.Clair, ":root:root:not(.dark)"), (ModeCharte.Sombre, ":root:root.dark") })
        {
            var ecarts = Ecarts(c, mode);
            if (ecarts.Count == 0) continue;
            blocs.Append(selecteur).Append('{');
            foreach (var (cle, valeur) in ecarts) blocs.Append("--").Append(cle).Append(':').Append(valeur).Append(';');
            blocs.Append("}\n");
        }
        var empreinte = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blocs.ToString())))[..12].ToLowerInvariant();
        var css = "/* Charte visuelle de Gestion MEP, générée par le serveur (console d'administration, fichier charte.json ou charte par défaut). */\n"
            + $":root{{--charte-empreinte:\"{empreinte}\"}}\n" + blocs;
        return (css, empreinte);
    }

    // ---------- Contrôles de lisibilité ----------

    /// <summary>Contrôles WCAG AA dans les deux thèmes (4,5:1 pour le texte, 3:1 pour les cadres et le focus), puis ressemblance avec les couleurs d'état.</summary>
    public static List<ControleCharte> Controles(Charte c)
    {
        var res = new List<ControleCharte>();
        foreach (var mode in new[] { ModeCharte.Clair, ModeCharte.Sombre })
        {
            var t = Jetons(c, mode);
            var suffixe = mode == ModeCharte.Clair ? "clair" : "sombre";
            void Ajoute(string id, string libelle, string role, string avant, string arriere, double minimum, (string Cible, string Couleur, string Contre)? corriger = null)
            {
                var rapport = Contraste(avant, arriere);
                var correction = rapport < minimum && corriger is { } k ? new CorrectionCharte(k.Cible, AjusterPourContraste(k.Couleur, k.Contre, minimum)) : null;
                res.Add(new($"{id}-{suffixe}", libelle, mode, "contraste", role, avant, arriere, rapport, minimum, correction, null));
            }
            var p = t["accent"];
            Ajoute("bouton", "Texte des boutons principaux", "primaire", t["accent-fg"], p, 4.5, ("primaire", p, t["accent-fg"]));
            Ajoute("lien", "Liens, onglet et menu actifs", "primaire", t["accent-text"], t["surface"], 4.5);
            Ajoute("focus", "Contour de focus sur le fond de page", "primaire", p, t["bg"], 3, ("primaire", p, t["bg"]));
            if (c.MenuLateral == "secondaire")
                Ajoute("menu", "Texte du menu latéral", "secondaire", t["nav-fg"], t["nav-bg"], 4.5, ("secondaire", t["nav-bg"], t["nav-fg"]));
            var roleTer = c.TertiaireLiee ? "primaire" : "tertiaire";
            Ajoute("etiquette", "Étiquettes des sections", roleTer, t["tertiaire-text"], t["tertiaire-soft"], 4.5);
            Ajoute("cadre", "Cadre des sections sur le fond du plan", roleTer, t["tertiaire"], t["surface"], 3, (roleTer, t["tertiaire"], t["surface"]));
        }
        foreach (var (etat, _, libelle, _) in Etats)
        {
            if (c.Etats.GetValueOrDefault(etat) is not { } b) continue;
            var rapport = Contraste(b, Blanc);
            res.Add(new($"etat-{etat}-clair", $"Couleur « {libelle} » sur fond blanc", ModeCharte.Clair, "contraste", etat, b, Blanc, rapport, 3,
                rapport < 3 ? new CorrectionCharte(etat, AjusterPourContraste(b, Blanc, 3)) : null, null));
        }
        foreach (var role in new[] { "primaire", "tertiaire" })
        {
            if (role == "tertiaire" && c.TertiaireLiee) continue;
            var coul = CouleurSaisie(c, role);
            foreach (var (etat, _, _, nom) in new[] { Etats[3], Etats[0], Etats[2], Etats[1] })
            {
                var reference = c.Etats.GetValueOrDefault(etat) ?? EtatDefaut(etat);
                if (EcartTeinte(coul, reference) < 14 && Math.Abs(Luminosite(coul) - Luminosite(reference)) < 0.18)
                    res.Add(new($"distinction-{role}-{etat}", $"{LibelleRole(role)} proche du {nom}", ModeCharte.Clair, "distinction", role,
                        coul, reference, Contraste(coul, reference), 0, null, "Un bouton ou une section risque de se lire comme un état de la MEP."));
            }
        }
        return res;
    }

    /// <summary>Contrôles en échec, comme avertissements (jamais bloquants).</summary>
    public static List<ProblemeCharte> Avertissements(Charte c) =>
    [
        .. Controles(c).Where(x => !x.Reussi).Select(x => x.Type == "distinction"
            ? new ProblemeCharte("etat", x.Role, $"{x.Libelle} : {x.Message}")
            : new ProblemeCharte("contraste", x.Role,
                $"{x.Libelle} (thème {(x.Mode == ModeCharte.Clair ? "clair" : "sombre")}) : {FormatRapport(x.Rapport)}, minimum {(x.Minimum == 3 ? "3:1" : "4,5:1")}"
                + (x.Correction is { } k ? $" ; « Corriger » propose {k.Couleur.ToUpperInvariant()}." : "."))),
    ];

    // ---------- Exports Excel (thème clair) ----------

    /// <summary>
    /// Palette des classeurs : en-têtes et liens en primaire, titres en secondaire (la secondaire par défaut garde le texte
    /// d'accent d'aujourd'hui), intertitres sur le fond pâle de la tertiaire, teintes d'état de la charte. La charte par
    /// défaut donne exactement <see cref="PaletteExcel.Defaut"/>.
    /// </summary>
    public static PaletteExcel PaletteExcel(Charte c)
    {
        var t = Jetons(c, ModeCharte.Clair);
        var secondaire = c.Secondaire == Defaut.Secondaire ? t["accent-text"] : c.Secondaire;
        static string X(string hex) => hex.TrimStart('#').ToUpperInvariant();
        return new PaletteExcel(X(t["accent"]), X(secondaire), X(t["tertiaire-soft"]))
        {
            Succes = (X(t["ok-bg"]), X(t["ok-fg"])),
            Echec = (X(t["danger-bg"]), X(t["danger-fg"])),
            EnCours = (X(t["run-bg"]), X(t["run-fg"])),
            Attention = (X(t["warn-bg"]), X(t["warn-fg"])),
        };
    }

    // ---------- Fichier charte.json ----------

    private static readonly JsonSerializerOptions OptionsFichier = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions OptionsCompactes = new(OptionsFichier) { WriteIndented = false };

    private static string Majuscules(string hex) => hex.ToUpperInvariant();

    /// <summary>Charte au format du fichier (« #RRGGBB »), ce qu'exportent la console et <c>gestion-mep charte exporter</c>.</summary>
    public static CharteFichier VersFichier(Charte c, string? schema = null)
    {
        string? Retouche(string r) => c.Sombre.GetValueOrDefault(r) is { } v ? Majuscules(v) : null;
        string? EtatPerso(string e) => c.Etats.GetValueOrDefault(e) is { } v ? Majuscules(v) : null;
        return new CharteFichier
        {
            Schema = schema,
            Version = VersionFichier,
            Nom = c.Nom,
            Couleurs = new() { Primaire = Majuscules(c.Primaire), Secondaire = Majuscules(c.Secondaire), Tertiaire = c.TertiaireLiee ? SuitPrimaire : Majuscules(c.Tertiaire) },
            MenuLateral = c.MenuLateral,
            Gris = c.Gris,
            Sombre = c.Sombre.Count == 0 ? null : new() { Primaire = Retouche("primaire"), Secondaire = Retouche("secondaire"), Tertiaire = Retouche("tertiaire") },
            Etats = c.Etats.Count == 0 ? null
                : new() { Succes = EtatPerso("succes"), Avertissement = EtatPerso("avertissement"), Erreur = EtatPerso("erreur"), Info = EtatPerso("info") },
        };
    }

    /// <summary>Texte du fichier charte.json (indenté).</summary>
    public static string Serialiser(Charte c, string? schema = null) => JsonSerializer.Serialize(VersFichier(c, schema), OptionsFichier);

    /// <summary>Forme compacte, enregistrée en base.</summary>
    public static string SerialiserCompact(Charte c) => JsonSerializer.Serialize(VersFichier(c), OptionsCompactes);

    public static string SerialiserFichier(CharteFichier f) => JsonSerializer.Serialize(f, OptionsFichier);

    /// <summary>Deux chartes identiques (mêmes couleurs, options et nom).</summary>
    public static bool Identiques(Charte a, Charte b) => SerialiserCompact(a) == SerialiserCompact(b);

    private static readonly string[] ClesRacine = ["$schema", "version", "nom", "couleurs", "menuLateral", "gris", "sombre", "etats"];

    /// <summary>
    /// Lit le texte d'un fichier charte.json. Erreurs bloquantes : JSON illisible, version inconnue, couleur invalide, valeur
    /// hors liste, type inattendu, primaire absente, nom trop long. Avertissement : clé inconnue (ignorée), avec sa ligne.
    /// Même règles et mêmes codes que <c>lireFichier</c> dans charte.ts.
    /// </summary>
    public static LectureCharte Lire(string texte)
    {
        var erreurs = new List<ProblemeCharte>();
        var avertissements = new List<ProblemeCharte>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(texte, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            var ligne = e.LineNumber is { } l ? (int)l + 1 : (int?)null;
            erreurs.Add(new("json", "", $"Fichier illisible : JSON invalide{(ligne is { } n ? $" (ligne {n})" : "")}.", ligne));
            return new(null, erreurs, avertissements);
        }
        using (doc)
        {
            var lignes = LignesDesCles(texte);
            return Lire(doc.RootElement, lignes, erreurs, avertissements);
        }
    }

    /// <summary>Lit une charte reçue de l'API (déjà au format du fichier).</summary>
    public static LectureCharte Lire(CharteFichier fichier) => Lire(JsonSerializer.Serialize(fichier, OptionsCompactes));

    private static LectureCharte Lire(JsonElement racine, Dictionary<string, int> lignes, List<ProblemeCharte> erreurs, List<ProblemeCharte> avertissements)
    {
        void Erreur(string code, string champ, string message)
        {
            // Ligne de la clé en cause, dans le message aussi (ligne de commande, journal du serveur).
            var ligne = lignes.GetValueOrDefault(champ) is > 0 and var l ? l : (int?)null;
            erreurs.Add(new(code, champ, ligne is { } n ? $"{message.TrimEnd('.')} (ligne {n})." : message, ligne));
        }
        if (racine.ValueKind != JsonValueKind.Object)
        {
            Erreur("racine", "", "Un objet JSON { … } est attendu.");
            return new(null, erreurs, avertissements);
        }
        void Inconnues(JsonElement o, string[] connues, string prefixe)
        {
            foreach (var p in o.EnumerateObject())
                if (!connues.Contains(p.Name))
                {
                    var champ = prefixe + p.Name;
                    var ligne = lignes.GetValueOrDefault(champ) is > 0 and var l ? l : (int?)null;
                    avertissements.Add(new("cle-inconnue", champ, $"Clé inconnue ignorée : « {champ} »{(ligne is { } n ? $" (ligne {n})" : "")}.", ligne));
                }
        }
        static JsonElement? Prop(JsonElement o, string nom) => o.TryGetProperty(nom, out var v) ? v : null;

        Inconnues(racine, ClesRacine, "");
        var nom = Defaut.Nom;
        string primaire = Defaut.Primaire, secondaire = Defaut.Secondaire, tertiaire = Defaut.Tertiaire;
        string menu = Defaut.MenuLateral, gris = Defaut.Gris;
        var sombre = new Dictionary<string, string>();
        var etats = new Dictionary<string, string>();

        if (Prop(racine, "version") is { } version
            && !(version.ValueKind == JsonValueKind.Number && version.TryGetDouble(out var v) && v == VersionFichier))
            Erreur("version", "version", $"Version {version.GetRawText()} inconnue : seule la version 1 est lue.");
        if (Prop(racine, "nom") is { } n)
        {
            if (n.ValueKind != JsonValueKind.String) Erreur("type", "nom", "nom : texte attendu.");
            else if (n.GetString()!.Trim().Length > LongueurNom) Erreur("longueur", "nom", $"nom : {LongueurNom} caractères au plus.");
            else if (n.GetString()!.Trim() is { Length: > 0 } t) nom = t;
        }
        string? Couleur(string champ, JsonElement brute, bool accepterPrimaire = false)
        {
            if (brute.ValueKind != JsonValueKind.String)
            {
                Erreur("type", champ, $"{champ} : code couleur attendu entre guillemets, par exemple \"#0F766E\".");
                return null;
            }
            var s = brute.GetString()!;
            if (accepterPrimaire && string.Equals(s.Trim(), SuitPrimaire, StringComparison.OrdinalIgnoreCase)) return SuitPrimaire;
            var h = NormaliserHex(s);
            if (h is null) Erreur("couleur", champ, $"{champ} : « {s} » n'est pas une couleur (attendu #RRGGBB ou #RGB, par exemple #0F766E).");
            return h;
        }
        if (Prop(racine, "couleurs") is not { } couleurs) Erreur("obligatoire", "couleurs.primaire", "couleurs.primaire est obligatoire.");
        else if (couleurs.ValueKind != JsonValueKind.Object) Erreur("type", "couleurs", "couleurs : objet attendu.");
        else
        {
            Inconnues(couleurs, Roles, "couleurs.");
            if (Prop(couleurs, "primaire") is not { } p) Erreur("obligatoire", "couleurs.primaire", "couleurs.primaire est obligatoire.");
            else primaire = Couleur("couleurs.primaire", p) ?? primaire;
            if (Prop(couleurs, "secondaire") is { } s) secondaire = Couleur("couleurs.secondaire", s) ?? secondaire;
            if (Prop(couleurs, "tertiaire") is { } te) tertiaire = Couleur("couleurs.tertiaire", te, accepterPrimaire: true) ?? tertiaire;
        }
        string? Choix(string champ, string[] valeurs)
        {
            if (Prop(racine, champ) is not { } brute) return null;
            if (brute.ValueKind == JsonValueKind.String && valeurs.Contains(brute.GetString())) return brute.GetString();
            Erreur("valeur", champ, $"{champ} : {string.Join(", ", valeurs.Select(x => $"« {x} »"))} attendu.");
            return null;
        }
        menu = Choix("menuLateral", MenusLateraux) ?? menu;
        gris = Choix("gris", GrisConnus) ?? gris;
        void Groupe(string cle, string[] connues, Dictionary<string, string> cible)
        {
            if (Prop(racine, cle) is not { } o) return;
            if (o.ValueKind != JsonValueKind.Object)
            {
                Erreur("type", cle, $"{cle} : objet attendu.");
                return;
            }
            Inconnues(o, connues, cle + ".");
            foreach (var r in connues)
                if (Prop(o, r) is { } brute && Couleur($"{cle}.{r}", brute) is { } h) cible[r] = h;
        }
        Groupe("sombre", Roles, sombre);
        Groupe("etats", EtatsConnus, etats);
        if (erreurs.Count > 0) return new(null, erreurs, avertissements);
        return new(new Charte
        {
            Nom = nom,
            Primaire = primaire,
            Secondaire = secondaire,
            Tertiaire = tertiaire,
            MenuLateral = menu,
            Gris = gris,
            Sombre = sombre,
            Etats = etats,
        }, erreurs, avertissements);
    }

    /// <summary>Ligne (à partir de 1) de chaque clé du fichier, par chemin (« couleurs.primaire »), pour les messages.</summary>
    private static Dictionary<string, int> LignesDesCles(string texte)
    {
        var lignes = new Dictionary<string, int>();
        var octets = Encoding.UTF8.GetBytes(texte);
        var lecteur = new Utf8JsonReader(octets);
        var chemin = new List<string?>(); // une entrée par objet ou tableau ouvert : clé en cours (null dans un tableau)
        var ligne = 1;
        long position = 0;
        try
        {
            while (lecteur.Read())
            {
                for (; position < lecteur.TokenStartIndex; position++)
                    if (octets[position] == '\n') ligne++;
                switch (lecteur.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        chemin.Add(lecteur.TokenType == JsonTokenType.StartArray ? null : "");
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        chemin.RemoveAt(chemin.Count - 1);
                        break;
                    case JsonTokenType.PropertyName:
                        chemin[^1] = lecteur.GetString();
                        if (chemin.All(x => x is not null))
                            lignes.TryAdd(string.Join('.', chemin.Where(x => x!.Length > 0)), ligne);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Lignes facultatives : l'erreur de syntaxe est signalée par JsonDocument.
        }
        return lignes;
    }

    /// <summary>Adresse du schéma JSON publié (« $schema » des fichiers exportés), si l'adresse publique est connue.</summary>
    public static string? CheminSchema(string? urlPublique) =>
        urlPublique is { Length: > 0 } u ? $"{u.TrimEnd('/')}/api/charte/schema.json" : null;
}
