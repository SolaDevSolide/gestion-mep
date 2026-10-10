namespace GestionMep.Domain;

// Règles métier : disponibilité.
public static partial class Regles
{
    /// <summary>
    /// Disponibilité d'une étape, évaluée dans un ordre fixe : le premier motif qui bloque l'emporte.
    /// Les étapes du retour arrière ne sont disponibles qu'une fois celui-ci déclenché, et ne dépendent pas de l'échéance.
    /// </summary>
    public static Disponibilite Disponibilite(Mep mep, Etape etape, DateTimeOffset maintenant)
    {
        var etapes = mep.EtapesDu(etape.Plan).ToList();
        var erreur = ErreurGraphe(etapes);
        if (erreur is not null) return new(StatutDisponibilite.PlanInvalide, erreur);

        if (etape.Plan == TypePlan.Deploiement)
        {
            if (etape.Etat == EtatEtape.Reussie && mep.Phase is Phase.RetourArriere or Phase.Cloturee or Phase.Recette)
                return new(StatutDisponibilite.Terminee, "Terminée");
            if (mep.Phase == Phase.RetourArriere || mep.Issue == IssueMep.Repliee)
                return new(StatutDisponibilite.RetourArriere, "Retour arrière déclenché");
            // Point de non-retour franchi : l'échéance ne s'applique plus au déploiement.
            if (!NonRetourFranchi(mep))
            {
                if (mep.EcheanceRepli is null) return new(StatutDisponibilite.EcheanceARenseigner, "Échéance à renseigner");
                if (maintenant >= mep.EcheanceRepli && mep.Phase != Phase.Cloturee) return new(StatutDisponibilite.RetourArriere, "RETOUR ARRIÈRE");
            }
        }

        if (string.IsNullOrWhiteSpace(etape.Action) || etape.SystemeIds.Count == 0 || etape.Criticite is null)
            return new(StatutDisponibilite.FicheACompleter, "Fiche à compléter");
        if (string.IsNullOrWhiteSpace(etape.Instructions) || string.IsNullOrWhiteSpace(etape.ResultatAttendu))
            return new(StatutDisponibilite.InstructionsACompleter, "Instructions à compléter");

        if (etape.Plan == TypePlan.RetourArriere && mep.Phase != Phase.RetourArriere && etape.Etat == EtatEtape.AFaire)
            return new(StatutDisponibilite.SiRetourArriere, "Si retour arrière");

        var parId = etapes.ToDictionary(e => e.Id);
        foreach (var dep in etape.Dependances)
        {
            var prealable = parId[dep];
            if (prealable.Etat != EtatEtape.Reussie) return new(StatutDisponibilite.Attend, $"Attend {prealable.Code}");
        }

        return etape.Etat switch
        {
            EtatEtape.Reussie => new(StatutDisponibilite.Terminee, "Terminée"),
            EtatEtape.EnCours => new(StatutDisponibilite.EnCours, "En cours"),
            EtatEtape.Echec when ResolutionEnCours(mep, etape) is not null => new(StatutDisponibilite.ResolutionEnCours, "Résolution en cours"),
            EtatEtape.Echec => new(StatutDisponibilite.ArretResolution, "Arrêt / résolution"),
            _ => new(StatutDisponibilite.Disponible, "Disponible"),
        };
    }

    public static ExecutionResolution? ResolutionEnCours(Mep mep, Etape etape) =>
        mep.Resolutions.FirstOrDefault(r => r.EtapeId == etape.Id && r.Statut == StatutResolution.EnCours);
}
