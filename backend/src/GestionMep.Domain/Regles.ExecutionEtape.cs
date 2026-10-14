namespace GestionMep.Domain;

// Règles métier : exécution d'une étape (ChangerEtat).
public static partial class Regles
{
    /// <summary>Démarrage par le participant relié au compte donné (voir l'autre surcharge).</summary>
    public static void Demarrer(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant) =>
        Demarrer(mep, etape, mep.Participants.FirstOrDefault(p => p.UtilisateurId == utilisateurId), maintenant);

    /// <summary>
    /// Démarrage d'une étape disponible par un participant relié à un compte (pilote et suppléants compris ; un administrateur
    /// non participant le devient côté API) : il en devient l'exécutant.
    /// </summary>
    public static void Demarrer(Mep mep, Etape etape, Participant? executant, DateTimeOffset maintenant)
    {
        ExigerPhaseDuPlan(mep, etape);
        if (executant?.UtilisateurId is null)
            throw new RegleMetierException("Seul un participant relié à un compte, le pilote ou un administrateur peut démarrer une étape.");
        var dispo = Disponibilite(mep, etape, maintenant);
        if (dispo.Statut != StatutDisponibilite.Disponible)
            throw new RegleMetierException($"Démarrage impossible : {dispo.Libelle}");
        if (etape.PointDeNonRetour && etape.NonRetourAutoriseParId is null)
            throw new RegleMetierException("Point de non-retour : l'accord du pilote est requis.");
        etape.Etat = EtatEtape.EnCours;
        etape.ExecutantId = executant.Id;
        etape.DebutReel = maintenant;
        etape.FinReelle = null;
        // Nouvelle tentative : le résultat constaté de la précédente (en échec) reste dans le journal.
        etape.ResultatConstate = "";
        etape.Revision++;
    }

    /// <param name="resultatVerifie">Confirmation « j'ai vérifié et obtenu le résultat attendu », exigée pour Réussie.</param>
    /// <param name="commentaire">Commentaire facultatif enregistré avec la preuve (null : commentaire existant inchangé).</param>
    /// <param name="resultatConstate">
    /// Résultat constaté d'un test unitaire (ignoré pour une simple vérification). À défaut, la preuve, qui décrit aussi ce qui a
    /// été constaté, en tient lieu ; un test unitaire réussi sans l'un ni l'autre est refusé.
    /// </param>
    public static void Terminer(Mep mep, Etape etape, EtatEtape resultat, string preuve, bool resultatVerifie, Guid utilisateurId, DateTimeOffset maintenant,
        string? commentaire = null, string? resultatConstate = null)
    {
        if (resultat is not (EtatEtape.Reussie or EtatEtape.Echec))
            throw new RegleMetierException("Résultat attendu : Réussie ou Échec.");
        ExigerExecution(mep, etape, utilisateurId);
        if (etape.Etat != EtatEtape.EnCours) throw new RegleMetierException("Démarrer l'action avant de déclarer son résultat.");
        if (etape.Plan == TypePlan.Deploiement && resultat == EtatEtape.Reussie && EcheanceAtteinte(mep, maintenant))
            throw new RegleMetierException("Échéance atteinte. Arrêter, prévenir le pilote et appliquer le retour arrière.");
        // La preuve n'est exigée qu'en cas d'échec : un succès se constate, un échec doit être décrit.
        if (resultat == EtatEtape.Echec && string.IsNullOrWhiteSpace(preuve))
            throw new RegleMetierException("Décrire l'erreur constatée dans la preuve.");
        if (resultat == EtatEtape.Reussie && !resultatVerifie)
            throw new RegleMetierException("Confirmer avoir vérifié et obtenu le résultat attendu.");
        var constate = etape.Fin == FinEtape.TestUnitaire
            ? string.IsNullOrWhiteSpace(resultatConstate) ? preuve?.Trim() ?? "" : resultatConstate.Trim()
            : "";
        if (etape.Fin == FinEtape.TestUnitaire && resultat == EtatEtape.Reussie && constate.Length == 0)
            throw new RegleMetierException("Test unitaire : saisir le résultat constaté.");
        etape.ResultatConstate = constate;
        etape.Preuve = preuve?.Trim() ?? "";
        if (commentaire is not null) etape.Commentaire = commentaire.Trim();
        etape.FinReelle = maintenant;
        etape.Etat = resultat;
        etape.Revision++;
    }

    /// <summary>
    /// Commentaire facultatif sur l'exécution d'une étape, pendant l'exécution, la recette ou le retour arrière :
    /// par l'exécutant, le pilote / un suppléant ou un administrateur. Texte rogné (vide accepté : efface le commentaire).
    /// </summary>
    public static void Commenter(Mep mep, Etape etape, string commentaire, Guid utilisateurId, bool estAdmin)
    {
        if (mep.Phase is not (Phase.Execution or Phase.Recette or Phase.RetourArriere))
            throw new RegleMetierException("Le commentaire d'exécution se saisit pendant l'exécution, la recette ou le retour arrière.");
        if (!estAdmin && !EstExecutant(mep, etape, utilisateurId) && !EstPilote(mep, utilisateurId))
            throw new RegleMetierException("Seul l'exécutant de l'étape, le pilote, un suppléant ou un administrateur peut la commenter.");
        etape.Commentaire = (commentaire ?? "").Trim();
        etape.Revision++;
    }

    /// <summary>
    /// Prise de main : un participant poursuit à la place de l'exécutant une étape en cours ou en échec (exécutant absent,
    /// indisponible…). Le nouvel exécutant est tracé sur l'étape et dans le journal.
    /// </summary>
    /// <returns>Le participant qui exécutait l'étape jusque-là.</returns>
    public static Guid? PrendreLaMain(Mep mep, Etape etape, Participant preneur, string motif)
    {
        ExigerPhaseDuPlan(mep, etape);
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer pourquoi vous prenez la main.");
        if (preneur.UtilisateurId is null) throw new RegleMetierException("Seul un participant relié à un compte peut prendre la main.");
        if (etape.Etat == EtatEtape.Reussie) throw new RegleMetierException($"{etape.Code} est déjà réussie.");
        if (etape.Etat == EtatEtape.AFaire || etape.ExecutantId is null)
            throw new RegleMetierException($"{etape.Code} n'est pas démarrée : il suffit de la démarrer.");
        var precedent = ExecutantCourant(etape);
        if (precedent == preneur.Id) throw new RegleMetierException("Vous exécutez déjà cette étape.");
        etape.ExecutantId = preneur.Id;
        etape.Revision++;
        return precedent;
    }

    /// <summary>
    /// Reprise d'une étape en échec sans résolution type, sur décision du pilote.
    /// Avec une résolution type, la reprise passe par sa vérification finale (ValiderVerification).
    /// </summary>
    public static void AutoriserReprise(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut autoriser une reprise.");
        ExigerPhaseDuPlan(mep, etape);
        if (etape.Etat != EtatEtape.Echec) throw new RegleMetierException("Seule une étape en échec peut être reprise.");
        if (ResolutionEnCours(mep, etape) is not null) throw new RegleMetierException("Une résolution est en cours sur cette étape.");
        if (etape.Plan == TypePlan.Deploiement && EcheanceAtteinte(mep, maintenant))
            throw new RegleMetierException("Échéance atteinte : retour arrière obligatoire, aucune reprise.");
        etape.Etat = EtatEtape.AFaire;
        etape.Revision++;
    }

    /// <summary>Échéance du déploiement atteinte (ou absente) ; jamais une fois le point de non-retour franchi.</summary>
    private static bool EcheanceAtteinte(Mep mep, DateTimeOffset maintenant) =>
        !NonRetourFranchi(mep) && (mep.EcheanceRepli is null || maintenant >= mep.EcheanceRepli);

    private static void ExigerPhaseDuPlan(Mep mep, Etape etape)
    {
        if (etape.Plan == TypePlan.Deploiement && mep.Phase != Phase.Execution)
            throw new RegleMetierException(mep.Phase switch
            {
                Phase.RetourArriere => "Le retour arrière est déclenché : le déploiement est arrêté.",
                Phase.Recette => "La MEP est en recette : le déploiement est terminé.",
                _ => "Le pilote doit passer le plan en exécution.",
            });
        if (etape.Plan == TypePlan.RetourArriere && mep.Phase != Phase.RetourArriere)
            throw new RegleMetierException("Le retour arrière n'est pas déclenché.");
    }

    public const string MessageAutreExecutant =
        "Cette étape est exécutée par quelqu'un d'autre. Utiliser « Prendre la main » pour la poursuivre à sa place.";

    /// <summary>Terminer une étape, lancer ou dérouler une résolution : l'exécutant, ou le pilote / un suppléant.</summary>
    private static void ExigerExecution(Mep mep, Etape etape, Guid utilisateurId)
    {
        ExigerPhaseDuPlan(mep, etape);
        if (!EstExecutant(mep, etape, utilisateurId) && !EstPilote(mep, utilisateurId))
            throw new RegleMetierException(MessageAutreExecutant);
    }
}
