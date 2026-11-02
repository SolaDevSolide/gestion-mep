namespace GestionMep.Domain;

/// <summary>
/// Jeton personnel : créé par une personne pour ses scripts, il agit avec ses droits (jamais plus). Jeton de service : créé
/// par un administrateur pour un outil (CI/CD, supervision), il agit sous un compte technique qui porte son nom.
/// </summary>
public enum TypeJeton { Personnel, Service }

/// <summary>État d'un jeton à un instant donné : seul <see cref="Actif"/> permet d'appeler l'API.</summary>
public enum EtatJeton { Actif, Expire, Revoque }

/// <summary>
/// Jeton d'accès à l'API (<c>Authorization: Bearer gmep_…</c>). Le secret n'est jamais conservé : seulement son empreinte
/// SHA-256 (le secret est aléatoire et long, une empreinte simple suffit) et un préfixe visible pour le reconnaître.
/// </summary>
public class JetonApi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    public TypeJeton Type { get; set; }
    /// <summary>Compte au nom duquel le jeton agit : la personne (jeton personnel) ou le compte technique (jeton de service).</summary>
    public Guid UtilisateurId { get; set; }
    /// <summary>Début visible du jeton (<c>gmep_</c> et 8 caractères), affiché dans les listes et le journal.</summary>
    public string Prefixe { get; set; } = "";
    /// <summary>Empreinte SHA-256 du jeton complet, en hexadécimal minuscule.</summary>
    public string Empreinte { get; set; } = "";
    public List<string> Portees { get; set; } = [];
    public DateTimeOffset CreeLe { get; set; }
    public Guid? CreeParId { get; set; }
    public string CreePar { get; set; } = "";
    public DateTimeOffset ExpireLe { get; set; }
    public DateTimeOffset? RevoqueLe { get; set; }
    public string? RevoquePar { get; set; }
    /// <summary>Dernière utilisation, à quelques minutes près (écriture espacée, voir <see cref="Regles.UtilisationAEnregistrer"/>).</summary>
    public DateTimeOffset? DerniereUtilisationLe { get; set; }
    public string? DerniereAdresseIp { get; set; }

    public EtatJeton Etat(DateTimeOffset maintenant) =>
        RevoqueLe is not null ? EtatJeton.Revoque : ExpireLe <= maintenant ? EtatJeton.Expire : EtatJeton.Actif;
}

/// <summary>Portées d'un jeton : chacune ouvre une famille de routes ; un jeton n'a que celles qu'on lui a données.</summary>
public static class PorteesJeton
{
    public const string MepsLecture = "meps:lecture";
    public const string MepsEcriture = "meps:ecriture";
    public const string Execution = "execution";
    public const string CatalogueLecture = "catalogue:lecture";
    public const string CatalogueEcriture = "catalogue:ecriture";
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> Toutes = [MepsLecture, MepsEcriture, Execution, CatalogueLecture, CatalogueEcriture, Admin];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [MepsLecture] = "Lire les MEP, leur journal, le suivi, les bibliothèques et les exports",
        [MepsEcriture] = "Créer et modifier les MEP et les bibliothèques (comprend meps:lecture)",
        [Execution] = "Démarrer, terminer et commenter les étapes, saisir les résultats de recette",
        [CatalogueLecture] = "Lire le catalogue des systèmes",
        [CatalogueEcriture] = "Modifier le catalogue des systèmes (comprend catalogue:lecture)",
        [Admin] = "Routes d'administration (jetons de service seulement)",
    };

    /// <summary>Vrai si les portées du jeton couvrent <paramref name="portee"/> (une portée d'écriture comprend la lecture).</summary>
    public static bool Couvre(IEnumerable<string> portees, string portee) => portees.Any(p => p == portee
        || p == MepsEcriture && portee == MepsLecture
        || p == CatalogueEcriture && portee == CatalogueLecture);
}

// Règles métier : jetons d'accès à l'API.
public static partial class Regles
{
    public const int DureeJetonParDefautJours = 90;
    public const int DureeJetonMaxJours = 365;
    public const int LongueurMaxNomJeton = 100;
    /// <summary>Jetons actifs au plus par compte (jetons personnels) : au-delà, en révoquer un.</summary>
    public const int JetonsActifsMaxParCompte = 20;
    /// <summary>Écart minimal entre deux enregistrements de la dernière utilisation d'un jeton.</summary>
    public static readonly TimeSpan IntervalleUtilisationJeton = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Nom rogné et obligatoire, portées connues (sans doublon, au moins une ; <c>admin</c> réservée aux jetons de service),
    /// durée de 1 à 365 jours (90 par défaut). Renvoie les portées dans l'ordre du catalogue et la date d'expiration.
    /// </summary>
    public static (string Nom, List<string> Portees, DateTimeOffset ExpireLe) ValiderJeton(TypeJeton type, string? nom, IEnumerable<string>? portees,
        int? dureeJours, DateTimeOffset maintenant)
    {
        var n = (nom ?? "").Trim();
        if (n.Length == 0) throw new SaisieInvalideException("Le nom du jeton est obligatoire.");
        if (n.Length > LongueurMaxNomJeton) throw new SaisieInvalideException($"Le nom du jeton ne doit pas dépasser {LongueurMaxNomJeton} caractères.");
        var liste = (portees ?? []).Select(p => (p ?? "").Trim().ToLowerInvariant()).ToList();
        if (liste.Count == 0) throw new SaisieInvalideException($"Au moins une portée est obligatoire : {string.Join(", ", PorteesJeton.Toutes)}.");
        if (liste.FirstOrDefault(p => !PorteesJeton.Toutes.Contains(p)) is { } inconnue)
            throw new SaisieInvalideException($"Portée inconnue : « {inconnue} ». Portées : {string.Join(", ", PorteesJeton.Toutes)}.");
        if (type == TypeJeton.Personnel && liste.Contains(PorteesJeton.Admin))
            throw new SaisieInvalideException("La portée admin est réservée aux jetons de service, créés par un administrateur.");
        var duree = dureeJours ?? DureeJetonParDefautJours;
        if (duree < 1 || duree > DureeJetonMaxJours)
            throw new SaisieInvalideException($"La durée de validité va de 1 à {DureeJetonMaxJours} jours.");
        return (n, PorteesJeton.Toutes.Where(liste.Contains).ToList(), maintenant.AddDays(duree));
    }

    /// <summary>Révocation définitive ; 422 si le jeton l'est déjà.</summary>
    public static void RevoquerJeton(JetonApi jeton, string auteur, DateTimeOffset maintenant)
    {
        if (jeton.RevoqueLe is not null) throw new RegleMetierException("Ce jeton est déjà révoqué.");
        jeton.RevoqueLe = maintenant;
        jeton.RevoquePar = auteur;
    }

    /// <summary>
    /// La dernière utilisation n'est écrite qu'une fois par <see cref="IntervalleUtilisationJeton"/> (ou au premier appel) :
    /// pas une écriture en base par requête.
    /// </summary>
    public static bool UtilisationAEnregistrer(DateTimeOffset? derniere, DateTimeOffset maintenant) =>
        derniere is not { } d || maintenant - d >= IntervalleUtilisationJeton;
}
