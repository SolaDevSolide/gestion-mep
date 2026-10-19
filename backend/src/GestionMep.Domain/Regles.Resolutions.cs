namespace GestionMep.Domain;

// Règles métier : résolutions types.
public static partial class Regles
{
    /// <summary>
    /// Démarre une résolution type sur une étape de déploiement en échec, à partir d'une copie de la résolution : celle figée
    /// dans la MEP à l'approbation ou au Go (<see cref="Mep.ResolutionsFigees"/>), à défaut celle de la bibliothèque.
    /// </summary>
    public static ExecutionResolution DemarrerResolution(Mep mep, Etape etape, ResolutionType bibliotheque, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var resolution = ResolutionAAppliquer(mep, bibliotheque);
        if (etape.Plan != TypePlan.Deploiement) throw new RegleMetierException("Les résolutions s'appliquent aux étapes de déploiement.");
        ExigerExecution(mep, etape, utilisateurId);
        if (etape.Etat != EtatEtape.Echec) throw new RegleMetierException("Une résolution s'applique à une étape en échec.");
        if (!EstResolutionProposee(mep, etape, resolution)) throw new RegleMetierException("Cette résolution n'est pas prévue pour cette étape.");
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

    /// <summary>
    /// Valide la prochaine correction dans l'ordre. Un échec clôt la résolution en échec : l'étape reste en échec et le pilote
    /// décide (retour arrière, nouvelle tentative), rien n'est déclenché automatiquement.
    /// </summary>
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
            EchouerResolution(execution, maintenant);
    }

    /// <summary>
    /// Vérification finale. Conforme : la résolution est validée et l'étape peut être reprise.
    /// Échec : la résolution échoue, l'étape reste en échec et le pilote décide.
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
            EchouerResolution(execution, maintenant);
        }
    }

    private static Etape ExigerResolutionEnCours(Mep mep, ExecutionResolution execution, Guid utilisateurId)
    {
        if (execution.Statut != StatutResolution.EnCours) throw new RegleMetierException("Cette résolution est terminée.");
        var etape = mep.Etapes.Single(e => e.Id == execution.EtapeId);
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le déploiement est arrêté : appliquer le retour arrière.");
        if (!EstExecutant(mep, etape, utilisateurId) && !EstPilote(mep, utilisateurId))
            throw new DroitInsuffisantException(MessageAutreExecutant);
        return etape;
    }

    /// <summary>
    /// Résolution en échec : elle se termine, l'étape reste en échec. Le retour arrière n'est plus déclenché automatiquement
    /// (un seul échec de correction, parfois une faute de frappe, ne doit pas défaire toute la MEP) : le pilote décide de le
    /// déclencher (motif obligatoire) ou d'autoriser une nouvelle tentative (<see cref="AutoriserReprise"/>).
    /// </summary>
    private static void EchouerResolution(ExecutionResolution execution, DateTimeOffset maintenant)
    {
        execution.Statut = StatutResolution.Echouee;
        execution.TermineeLe = maintenant;
    }

    /// <summary>
    /// Détail du journal d'une résolution en échec : « Résolution « X » en échec sur E0003 : l'étape reste en échec, le pilote
    /// décide (retour arrière ou nouvelle tentative) » ; sans retour arrière possible, seule la nouvelle tentative est proposée.
    /// </summary>
    public static string DetailResolutionEchouee(Mep mep, ExecutionResolution execution, Etape etape) =>
        $"Résolution « {execution.Titre} » en échec sur {etape.Code} : l'étape reste en échec, le pilote décide "
        + (RetourArrierePossible(mep) ? "(retour arrière ou nouvelle tentative)" : "(nouvelle tentative ou correction en avant)");
}
