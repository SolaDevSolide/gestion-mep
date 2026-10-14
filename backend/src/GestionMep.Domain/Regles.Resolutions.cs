namespace GestionMep.Domain;

// Règles métier : résolutions types.
public static partial class Regles
{
    /// <summary>Démarre une résolution type sur une étape de déploiement en échec, à partir d'une copie figée de la résolution.</summary>
    public static ExecutionResolution DemarrerResolution(Mep mep, Etape etape, ResolutionType resolution, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (etape.Plan != TypePlan.Deploiement) throw new RegleMetierException("Les résolutions s'appliquent aux étapes de déploiement.");
        ExigerExecution(mep, etape, utilisateurId);
        if (etape.Etat != EtatEtape.Echec) throw new RegleMetierException("Une résolution s'applique à une étape en échec.");
        if (!etape.ResolutionIds.Contains(resolution.Id)) throw new RegleMetierException("Cette résolution n'est pas prévue pour cette étape.");
        if (ResolutionEnCours(mep, etape) is not null) throw new RegleMetierException("Une résolution est déjà en cours sur cette étape.");
        if (EcheanceAtteinte(mep, maintenant))
            throw new RegleMetierException("Échéance atteinte : aucune nouvelle correction, retour arrière obligatoire.");
        var erreurs = ErreursResolutionType(resolution);
        if (erreurs.Count > 0) throw new RegleMetierException("Résolution incomplète : " + string.Join(" ", erreurs));

        var execution = new ExecutionResolution
        {
            MepId = mep.Id, EtapeId = etape.Id, ResolutionTypeId = resolution.Id, Titre = resolution.Titre,
            Symptomes = resolution.Symptomes, Diagnostic = resolution.Diagnostic,
            Corrections = resolution.Corrections.OrderBy(c => c.Ordre).Select((c, i) => new CorrectionExecutee
            {
                Ordre = i + 1, Instruction = c.Instruction, ResultatAttendu = c.ResultatAttendu,
            }).ToList(),
            VerificationInstruction = resolution.VerificationInstruction, VerificationAttendu = resolution.VerificationAttendu,
            DemarreeParId = utilisateurId, DemarreeLe = maintenant,
        };
        mep.Resolutions.Add(execution);
        etape.Revision++;
        return execution;
    }

    /// <summary>Valide la prochaine correction dans l'ordre. Un échec clôt la résolution et déclenche le retour arrière (s'il est possible).</summary>
    public static void ValiderCorrection(Mep mep, ExecutionResolution execution, int ordre, ResultatControle resultat, string observation, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var etape = ExigerResolutionEnCours(mep, execution, utilisateurId);
        var suivante = execution.Corrections.OrderBy(c => c.Ordre).FirstOrDefault(c => c.Resultat is null)
            ?? throw new RegleMetierException("Toutes les corrections sont validées : passer à la vérification finale.");
        if (suivante.Ordre != ordre) throw new RegleMetierException($"Valider d'abord la correction {suivante.Ordre}.");
        if (resultat == ResultatControle.Echec && string.IsNullOrWhiteSpace(observation))
            throw new RegleMetierException("Décrire ce qui a été observé.");
        suivante.Resultat = resultat;
        suivante.Observation = observation;
        suivante.ParId = utilisateurId;
        suivante.Le = maintenant;
        etape.Revision++;
        if (resultat == ResultatControle.Echec)
            EchouerResolution(mep, execution, etape, $"correction {ordre} en échec", maintenant);
    }

    /// <summary>
    /// Vérification finale. Conforme : la résolution est validée et l'étape peut être reprise.
    /// Échec : la résolution échoue et le retour arrière est déclenché.
    /// </summary>
    public static void ValiderVerification(Mep mep, ExecutionResolution execution, ResultatControle resultat, string observation, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var etape = ExigerResolutionEnCours(mep, execution, utilisateurId);
        var restante = execution.Corrections.OrderBy(c => c.Ordre).FirstOrDefault(c => c.Resultat is null);
        if (restante is not null) throw new RegleMetierException($"Valider d'abord la correction {restante.Ordre}.");
        if (string.IsNullOrWhiteSpace(observation)) throw new RegleMetierException("Renseigner le résultat observé de la vérification.");
        execution.VerificationResultat = resultat;
        execution.VerificationObservation = observation;
        execution.TermineeLe = maintenant;
        if (resultat == ResultatControle.Conforme)
        {
            execution.Statut = StatutResolution.Reussie;
            etape.Etat = EtatEtape.AFaire;
            etape.Revision++;
        }
        else
        {
            EchouerResolution(mep, execution, etape, "vérification finale en échec", maintenant);
        }
    }

    private static Etape ExigerResolutionEnCours(Mep mep, ExecutionResolution execution, Guid utilisateurId)
    {
        if (execution.Statut != StatutResolution.EnCours) throw new RegleMetierException("Cette résolution est terminée.");
        var etape = mep.Etapes.Single(e => e.Id == execution.EtapeId);
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le déploiement est arrêté : appliquer le retour arrière.");
        if (!EstExecutant(mep, etape, utilisateurId) && !EstPilote(mep, utilisateurId))
            throw new RegleMetierException(MessageAutreExecutant);
        return etape;
    }

    /// <summary>
    /// Résolution en échec : déclenche le retour arrière, sauf point de non-retour franchi ou MEP sans plan de retour arrière
    /// (l'étape reste en échec ; le pilote peut autoriser une reprise).
    /// </summary>
    private static void EchouerResolution(Mep mep, ExecutionResolution execution, Etape etape, string cause, DateTimeOffset maintenant)
    {
        execution.Statut = StatutResolution.Echouee;
        execution.TermineeLe = maintenant;
        if (RetourArrierePossible(mep))
            DeclencherRetourArriere(mep, null, $"Résolution « {execution.Titre} » sur {etape.Code} : {cause}.", maintenant);
    }
}
