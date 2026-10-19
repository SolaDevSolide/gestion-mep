using System.Globalization;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GestionMep.Api.Services;

/// <summary>
/// Paramètres de recherche de la liste des MEP (#59), tels qu'ils s'écrivent dans l'adresse de la page d'accueil, l'API
/// (<c>GET /api/meps</c>, <c>GET /api/meps/excel</c>) et, avec les mêmes valeurs, la ligne de commande
/// (<c>gestion-mep meps lister</c>). Les listes sont séparées par des virgules. Voir <see cref="FiltreMeps"/>.
/// </summary>
public record ParametresFiltreMeps(
    [property: FromQuery(Name = "recherche")] string? Recherche = null,
    [property: FromQuery(Name = "systeme")] string? Systeme = null,
    [property: FromQuery(Name = "environnement")] string? Environnement = null,
    [property: FromQuery(Name = "equipe")] string? Equipe = null,
    [property: FromQuery(Name = "personne")] string? Personne = null,
    [property: FromQuery(Name = "role")] string? Role = null,
    [property: FromQuery(Name = "etat")] string? Etat = null,
    [property: FromQuery(Name = "phase")] string? Phase = null,
    [property: FromQuery(Name = "type")] string? Type = null,
    [property: FromQuery(Name = "du")] string? Du = null,
    [property: FromQuery(Name = "au")] string? Au = null,
    [property: FromQuery(Name = "echec")] string? Echec = null);

/// <summary>
/// Filtre de la liste des MEP, partagé par l'API, l'export Excel et la ligne de commande ; le front applique les mêmes
/// règles (frontend/src/rechercheMeps.ts), vérifiées des deux côtés par les mêmes cas (frontend/src/vecteurs-filtre-meps.json).
/// <list type="bullet">
/// <item><c>Systemes</c> : systèmes du catalogue (identifiants) ou saisis à la main (<c>libre:Nom</c>) ; la MEP en touche au moins un.</item>
/// <item><c>Environnements</c> : sur les systèmes retenus (tous si aucun système n'est filtré), sans casse ni accents.</item>
/// <item><c>Echec</c> : au moins un échec d'étape survenu sur les systèmes retenus (sur la MEP si aucun système n'est filtré).</item>
/// <item><c>Equipes</c> : la MEP appartient à l'une des équipes. <c>Personne</c> : la personne intervient sur la MEP, avec l'un
/// des <c>Roles</c> s'il y en a.</item>
/// <item><c>Etats</c> : phase en cours ou issue (réussie, avec incidents, repliée). <c>Phase</c> et <c>Type</c> restent pour
/// les adresses existantes.</item>
/// <item><c>Du</c>, <c>Au</c> : bornes incluses sur le jour de la MEP (Go, à défaut création).</item>
/// <item><c>Recherche</c> : outil, version, objet, pilote, personnes (tous leurs comptes reliés) et systèmes.</item>
/// </list>
/// Résultat trié par jour de MEP décroissant, puis par création décroissante.
/// </summary>
public sealed record FiltreMeps
{
    public const string PrefixeLibre = "libre:";

    public string Recherche { get; init; } = "";
    public IReadOnlyList<string> Systemes { get; init; } = [];
    public IReadOnlyList<string> Environnements { get; init; } = [];
    public IReadOnlyList<Guid> Equipes { get; init; } = [];
    public Guid? Personne { get; init; }
    public IReadOnlyList<RoleMep> Roles { get; init; } = [];
    public IReadOnlyList<EtatMep> Etats { get; init; } = [];
    public Phase? Phase { get; init; }
    public TypeMep? Type { get; init; }
    public DateOnly? Du { get; init; }
    public DateOnly? Au { get; init; }
    public bool Echec { get; init; }

    public static readonly FiltreMeps Aucun = new();

    /// <summary>Noms des rôles et des états dans les adresses (et la ligne de commande).</summary>
    public static readonly IReadOnlyDictionary<string, RoleMep> NomsRoles = new Dictionary<string, RoleMep>
    {
        ["pilote"] = RoleMep.Pilote, ["suppleant"] = RoleMep.Suppleant, ["responsable"] = RoleMep.ResponsableTechnique,
        ["executant"] = RoleMep.Executant, ["approbateur"] = RoleMep.Approbateur,
    };

    public static readonly IReadOnlyDictionary<string, EtatMep> NomsEtats = new Dictionary<string, EtatMep>
    {
        ["preparation"] = EtatMep.Preparation, ["execution"] = EtatMep.Execution, ["recette"] = EtatMep.Recette,
        ["retour-arriere"] = EtatMep.RetourArriere, ["reussie"] = EtatMep.Reussie, ["incidents"] = EtatMep.AvecIncidents,
        ["repliee"] = EtatMep.Repliee, ["reportee"] = EtatMep.Reportee, ["echec"] = EtatMep.Echec, ["annulee"] = EtatMep.Annulee,
    };

    public bool EstVide => this == Aucun || (Recherche.Trim().Length == 0 && Systemes.Count == 0 && Environnements.Count == 0
        && Equipes.Count == 0 && Personne is null && Etats.Count == 0 && Phase is null && Type is null && Du is null && Au is null && !Echec);

    private static List<string> Liste(string? texte) =>
        [.. (texte ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()];

    private static DateOnly? Date(string? texte, string nom) =>
        string.IsNullOrWhiteSpace(texte) ? null
            : DateOnly.TryParseExact(texte.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
            : throw ErreurHttp.Saisie($"Date « {texte} » invalide pour « {nom} » : format AAAA-MM-JJ attendu.");

    /// <summary>Lit les paramètres ; une valeur inconnue est une erreur de saisie (400) qui donne les valeurs admises.</summary>
    public static FiltreMeps Lire(ParametresFiltreMeps p)
    {
        var systemes = Liste(p.Systeme);
        foreach (var s in systemes)
            if (!Guid.TryParse(s, out _) && !(s.StartsWith(PrefixeLibre, StringComparison.Ordinal) && s.Length > PrefixeLibre.Length))
                throw ErreurHttp.Saisie($"Système « {s} » invalide : identifiant du catalogue ou « {PrefixeLibre}Nom » pour un système hors catalogue.");
        var equipes = Liste(p.Equipe).Select(e => Guid.TryParse(e, out var id) ? id
            : throw ErreurHttp.Saisie($"Équipe « {e} » invalide : identifiant attendu.")).ToList();
        Guid? personne = string.IsNullOrWhiteSpace(p.Personne) ? null
            : Guid.TryParse(p.Personne.Trim(), out var pid) ? pid : throw ErreurHttp.Saisie($"Personne « {p.Personne} » invalide : identifiant attendu.");
        var roles = Liste(p.Role).Select(r => NomsRoles.TryGetValue(r.ToLowerInvariant(), out var v) ? v
            : throw ErreurHttp.Saisie($"Rôle « {r} » inconnu : {string.Join(", ", NomsRoles.Keys)}.")).ToList();
        var etats = Liste(p.Etat).Select(e => NomsEtats.TryGetValue(e.ToLowerInvariant(), out var v) ? v
            : throw ErreurHttp.Saisie($"État « {e} » inconnu : {string.Join(", ", NomsEtats.Keys)}.")).ToList();
        var du = Date(p.Du, "du");
        var au = Date(p.Au, "au");
        if (du > au) throw ErreurHttp.Saisie("Période invalide : « du » est après « au ».");
        return new FiltreMeps
        {
            Recherche = (p.Recherche ?? "").Trim(),
            Systemes = systemes,
            Environnements = Liste(p.Environnement),
            Equipes = equipes,
            Personne = personne,
            Roles = roles,
            Etats = etats,
            Phase = Endpoints.ExportsExcelEndpoints.LirePhase(p.Phase),
            Type = Endpoints.ExportsExcelEndpoints.LireType(p.Type),
            Du = du,
            Au = au,
            Echec = p.Echec?.Trim().ToLowerInvariant() switch
            {
                null or "" or "0" or "non" or "false" => false,
                "1" or "oui" or "true" => true,
                _ => throw ErreurHttp.Saisie("« echec » attend 1 ou 0."),
            },
        };
    }

    /// <summary>Le système de la MEP est l'un des systèmes filtrés (catalogue ou saisi à la main, nom sans casse ni accents).</summary>
    public bool Retient(SystemeResumeDto s) =>
        Systemes.Count == 0 || Systemes.Any(f => f.StartsWith(PrefixeLibre, StringComparison.Ordinal)
            ? s.CatalogueId is null && VuesExcel.Normaliser(s.Nom.Trim()) == VuesExcel.Normaliser(f[PrefixeLibre.Length..].Trim())
            : s.CatalogueId is { } id && Guid.TryParse(f, out var g) && g == id);

    /// <summary>Systèmes de la MEP retenus par le filtre (tous si aucun système n'est filtré).</summary>
    public List<SystemeResumeDto> SystemesRetenus(MepResumeDto m) => [.. m.Systemes.Where(Retient)];

    /// <summary>Rôles de la personne filtrée dans la MEP ; null si elle n'y intervient pas (ou si aucune personne n'est filtrée).</summary>
    public IReadOnlyList<RoleMep>? RolesDeLaPersonne(MepResumeDto m) =>
        Personne is { } p ? m.Intervenants.FirstOrDefault(i => i.PersonneId == p)?.Roles : null;

    public bool Correspond(MepResumeDto m)
    {
        if (Type is { } t && m.Type != t) return false;
        if (Phase is { } ph && m.Phase != ph) return false;
        if (Etats.Count > 0 && !Etats.Contains(m.Etat)) return false;
        if (Equipes.Count > 0 && !m.EquipeIds.Any(Equipes.Contains)) return false;
        var systemes = SystemesRetenus(m);
        if (Systemes.Count > 0 && systemes.Count == 0) return false;
        if (Environnements.Count > 0)
        {
            var envs = Environnements.Select(e => VuesExcel.Normaliser(e)).ToHashSet();
            if (!systemes.Any(s => s.Environnements.Any(e => envs.Contains(VuesExcel.Normaliser(e.Nom.Trim()))))) return false;
        }
        if (Echec && (Systemes.Count > 0 ? !systemes.Any(s => s.Echecs > 0) : m.EchecsSurvenus == 0)) return false;
        if (Personne is not null)
        {
            if (RolesDeLaPersonne(m) is not { } roles) return false;
            if (Roles.Count > 0 && !roles.Any(Roles.Contains)) return false;
        }
        if (Du is { } du && m.Jour < du) return false;
        if (Au is { } au && m.Jour > au) return false;
        var q = VuesExcel.Normaliser(Recherche.Trim());
        return q.Length == 0 || new[] { m.Outil, m.VersionCible, m.Objet, m.Pilote ?? "" }.Concat(m.Personnes).Concat(m.Systemes.Select(s => s.Nom))
            .Any(x => VuesExcel.Normaliser(x).Contains(q, StringComparison.Ordinal));
    }

    /// <summary>MEP qui correspondent, par jour de MEP décroissant puis création décroissante.</summary>
    public List<MepResumeDto> Appliquer(IEnumerable<MepResumeDto> meps) =>
        [.. meps.Where(Correspond).OrderByDescending(m => m.Jour).ThenByDescending(m => m.CreeLe)];

    /// <summary>Libellés des rôles et des états, comme dans l'interface (frontend/src/rechercheMeps.ts).</summary>
    public static string Libelle(RoleMep r) => r switch
    {
        RoleMep.Pilote => "Pilote",
        RoleMep.Suppleant => "Suppléant",
        RoleMep.ResponsableTechnique => "Responsable technique",
        RoleMep.Executant => "Exécutant",
        RoleMep.Approbateur => "Approbateur",
        _ => r.ToString(),
    };

    public static string Libelle(EtatMep e) => e switch
    {
        EtatMep.Preparation => "Préparation",
        EtatMep.Execution => "Exécution",
        EtatMep.Recette => "Recette",
        EtatMep.RetourArriere => "Retour arrière",
        EtatMep.Reussie => "Réussie",
        EtatMep.AvecIncidents => "Réussie avec incidents",
        EtatMep.Repliee => "Repliée",
        EtatMep.Reportee => "Reportée",
        EtatMep.Echec => "Échec (service dégradé)",
        EtatMep.Annulee => "Annulée",
        _ => e.ToString(),
    };

    /// <summary>
    /// Description des filtres pour la ligne de contexte d'un export (« système BDD ; environnement Production ; … »).
    /// <paramref name="nomSysteme"/>, <paramref name="nomEquipe"/>, <paramref name="nomPersonne"/> : noms lisibles des identifiants.
    /// </summary>
    public List<string> Decrire(Func<Guid, string?> nomSysteme, Func<Guid, string?> nomEquipe, Func<Guid, string?> nomPersonne)
    {
        var d = new List<string>();
        if (Recherche.Length > 0) d.Add($"recherche « {Recherche} »");
        if (Systemes.Count > 0)
            d.Add((Systemes.Count > 1 ? "systèmes " : "système ") + string.Join(", ", Systemes.Select(s =>
                s.StartsWith(PrefixeLibre, StringComparison.Ordinal) ? $"{s[PrefixeLibre.Length..]} (hors catalogue)"
                    : Guid.TryParse(s, out var id) ? nomSysteme(id) ?? s : s)));
        if (Environnements.Count > 0) d.Add((Environnements.Count > 1 ? "environnements " : "environnement ") + string.Join(", ", Environnements));
        if (Echec) d.Add(Systemes.Count > 0 ? "avec un échec sur ces systèmes" : "avec au moins un échec");
        if (Equipes.Count > 0) d.Add((Equipes.Count > 1 ? "équipes " : "équipe ") + string.Join(", ", Equipes.Select(e => nomEquipe(e) ?? e.ToString())));
        if (Personne is { } p)
            d.Add($"personne {nomPersonne(p) ?? p.ToString()}" + (Roles.Count > 0 ? $" ({string.Join(", ", Roles.Select(r => Libelle(r).ToLowerInvariant()))})" : ""));
        if (Etats.Count > 0) d.Add((Etats.Count > 1 ? "états " : "état ") + string.Join(", ", Etats.Select(e => Libelle(e).ToLowerInvariant())));
        if (Phase is { } ph) d.Add($"phase {VuesExcel.Libelle(ph)}");
        if (Type is { } t) d.Add(t == TypeMep.Simplifiee ? "MEP simplifiées" : "MEP complètes");
        if (Du is { } du && Au is { } au) d.Add($"du {du:dd/MM/yyyy} au {au:dd/MM/yyyy}");
        else if (Du is { } du2) d.Add($"depuis le {du2:dd/MM/yyyy}");
        else if (Au is { } au2) d.Add($"jusqu'au {au2:dd/MM/yyyy}");
        return d;
    }
}
