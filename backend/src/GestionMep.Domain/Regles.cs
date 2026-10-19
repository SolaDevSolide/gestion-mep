namespace GestionMep.Domain;

/// <summary>
/// Refus d'une opération par une règle métier (HTTP 422) : la demande est valide, mais l'état actuel la refuse. Le message
/// est destiné à l'utilisateur.
/// </summary>
public class RegleMetierException(string message) : Exception(message);

/// <summary>
/// Refus faute de droits (HTTP 403) : le compte n'a pas le rôle exigé (pilote, suppléant, exécutant, participant,
/// administrateur…), quel que soit l'état de la MEP.
/// </summary>
public class DroitInsuffisantException(string message) : RegleMetierException(message);

/// <summary>
/// Saisie invalide en elle-même (HTTP 400) : champ obligatoire vide, trop long, valeur ou référence inconnue, doublon.
/// </summary>
public class SaisieInvalideException(string message) : RegleMetierException(message);

public enum StatutDisponibilite
{
    PlanInvalide,
    /// <summary>Plus produit depuis que l'échéance de retour arrière est facultative ; gardé pour la compatibilité de l'API.</summary>
    EcheanceARenseigner,
    RetourArriere,
    FicheACompleter,
    InstructionsACompleter,
    Attend,
    Terminee,
    EnCours,
    ArretResolution,
    ResolutionEnCours,
    /// <summary>Étape du plan de retour arrière tant que celui-ci n'est pas déclenché.</summary>
    SiRetourArriere,
    Disponible,
}

public record Disponibilite(StatutDisponibilite Statut, string Libelle);

/// <summary>
/// Règles du plan de MEP : disponibilité des étapes, validité du graphe, anomalies du plan, changements d'état,
/// retour arrière, prise de main et résolutions types.
/// </summary>
public static partial class Regles
{
    public const string MotifEcheance = "Échéance de retour arrière atteinte.";

    // Les règles sont réparties par domaine dans les fichiers Regles.*.cs.
}
