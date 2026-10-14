namespace GestionMep.Domain;

// Règles métier : point de non-retour.
public static partial class Regles
{
    /// <summary>Première étape point de non-retour déjà démarrée (dans l'ordre du plan), ou null.</summary>
    public static Etape? EtapeNonRetourFranchie(Mep mep) =>
        mep.EtapesDu(TypePlan.Deploiement).FirstOrDefault(e => e.PointDeNonRetour && (e.Etat != EtatEtape.AFaire || e.DebutReel is not null));

    /// <summary>
    /// Une étape point de non-retour a été démarrée : plus d'échéance sur le déploiement, plus de retour arrière
    /// (ni automatique ni manuel) ; on corrige en avant.
    /// </summary>
    public static bool NonRetourFranchi(Mep mep) => EtapeNonRetourFranchie(mep) is not null;

    /// <summary>Accord du pilote, pendant l'exécution, pour démarrer une étape point de non-retour.</summary>
    public static void AutoriserNonRetour(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut autoriser le passage du point de non-retour.");
        if (!etape.PointDeNonRetour) throw new RegleMetierException($"{etape.Code} n'est pas un point de non-retour.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le passage du point de non-retour s'autorise pendant l'exécution.");
        if (etape.Etat != EtatEtape.AFaire || etape.DebutReel is not null) throw new RegleMetierException($"{etape.Code} est déjà démarrée.");
        if (etape.NonRetourAutoriseParId is not null) throw new RegleMetierException($"Le passage de {etape.Code} est déjà autorisé.");
        etape.NonRetourAutoriseParId = utilisateurId;
        etape.NonRetourAutoriseLe = maintenant;
        etape.Revision++;
    }

    /// <summary>
    /// Type d'étape et point de non-retour : réservés au déploiement. Une étape standard perd l'emplacement et la méthode de
    /// récupération propres aux étapes de sauvegarde.
    /// </summary>
    public static void DefinirType(Etape etape, TypeEtape type, bool pointDeNonRetour)
    {
        if (etape.Plan == TypePlan.RetourArriere && (type != TypeEtape.Standard || pointDeNonRetour))
            throw new RegleMetierException("Le plan de retour arrière n'accepte ni étape de sauvegarde ni point de non-retour.");
        etape.Type = type;
        if (type != TypeEtape.Sauvegarde)
        {
            etape.SauvegardeEmplacement = "";
            etape.SauvegardeMethodeRecuperation = "";
        }
        if (etape.PointDeNonRetour != pointDeNonRetour)
        {
            etape.NonRetourAutoriseParId = null;
            etape.NonRetourAutoriseLe = null;
        }
        etape.PointDeNonRetour = pointDeNonRetour;
    }
}
