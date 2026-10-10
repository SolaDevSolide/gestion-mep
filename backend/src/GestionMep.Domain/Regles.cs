namespace GestionMep.Domain;

/// <summary>Refus d'une opération par une règle métier. Le message est destiné à l'utilisateur.</summary>
public class RegleMetierException(string message) : Exception(message);

public enum StatutDisponibilite
{
    PlanInvalide,
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
/// Règles du plan de MEP, reprises des macros VBA du modèle Excel
/// (modules Collaboration et frmEtape : MEPDisponibilite, ErreurGraphe, ErreursPlan, ChangerEtat…),
/// complétées par le retour arrière, la prise de main et les résolutions types.
/// </summary>
public static partial class Regles
{
    public const string MotifEcheance = "Échéance de retour arrière atteinte.";

    // Les règles sont réparties par domaine dans les fichiers Regles.*.cs.
}
