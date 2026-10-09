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
public static class Regles
{
    public const string MotifEcheance = "Échéance de retour arrière atteinte.";

    // ---------- Graphe de dépendances ----------

    /// <summary>Équivalent de ErreurGraphe, pour les étapes d'un même plan.</summary>
    public static string? ErreurGraphe(IReadOnlyList<Etape> etapes)
    {
        var parId = etapes.ToDictionary(e => e.Id);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in etapes)
        {
            if (string.IsNullOrWhiteSpace(e.Code)) return "ID manquant";
            if (!codes.Add(e.Code.Trim())) return $"ID en double : {e.Code}";
        }
        foreach (var e in etapes)
        {
            foreach (var dep in e.Dependances)
            {
                if (dep == e.Id) return $"Auto-dépendance : {e.Code}";
                if (!parId.ContainsKey(dep)) return $"Dépendance introuvable pour {e.Code}";
            }
        }

        // Parcours en profondeur avec trois couleurs, comme Visiter() dans l'Excel.
        var couleur = new Dictionary<Guid, int>();
        bool Visiter(Etape e)
        {
            var c = couleur.GetValueOrDefault(e.Id);
            if (c == 1) return true;
            if (c == 2) return false;
            couleur[e.Id] = 1;
            foreach (var dep in e.Dependances)
                if (Visiter(parId[dep])) return true;
            couleur[e.Id] = 2;
            return false;
        }
        foreach (var e in etapes)
            if (Visiter(e)) return "Cycle de dépendances";
        return null;
    }

    public static string? ErreurGraphe(Mep mep, TypePlan plan) => ErreurGraphe(mep.EtapesDu(plan).ToList());

    // ---------- Disponibilité ----------

    /// <summary>
    /// Équivalent de DisponibiliteDonnees, évalué dans le même ordre que l'Excel pour le déploiement.
    /// Les étapes du retour arrière ne sont disponibles qu'une fois celui-ci déclenché, et ne dépendent pas de l'échéance.
    /// </summary>
    public static Disponibilite Disponibilite(Mep mep, Etape etape, DateTimeOffset maintenant)
    {
        var etapes = mep.EtapesDu(etape.Plan).ToList();
        var erreur = ErreurGraphe(etapes);
        if (erreur is not null) return new(StatutDisponibilite.PlanInvalide, erreur);

        if (etape.Plan == TypePlan.Deploiement)
        {
            if (etape.Etat == EtatEtape.Reussie && mep.Phase is Phase.RetourArriere or Phase.Cloturee)
                return new(StatutDisponibilite.Terminee, "Terminée");
            if (mep.Phase == Phase.RetourArriere || mep.Issue == IssueMep.Repliee)
                return new(StatutDisponibilite.RetourArriere, "Retour arrière déclenché");
            if (mep.EcheanceRepli is null) return new(StatutDisponibilite.EcheanceARenseigner, "Échéance à renseigner");
            if (maintenant >= mep.EcheanceRepli && mep.Phase != Phase.Cloturee) return new(StatutDisponibilite.RetourArriere, "RETOUR ARRIÈRE");
        }

        if (string.IsNullOrWhiteSpace(etape.Action) || etape.SystemeId is null || etape.ResponsableId is null || etape.Criticite is null)
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

    // ---------- Vérification du plan ----------

    /// <summary>
    /// Équivalent de ErreursPlan, mais renvoie toutes les anomalies au lieu de la première.
    /// Ajouts par rapport à l'Excel : procédure relue, comptes reliés, et plan de retour arrière obligatoire.
    /// </summary>
    public static List<string> ErreursPlan(Mep mep, DateTimeOffset maintenant)
    {
        var erreurs = new List<string>();
        foreach (var plan in new[] { TypePlan.Deploiement, TypePlan.RetourArriere })
        {
            var graphe = ErreurGraphe(mep, plan);
            if (graphe is not null) erreurs.Add(plan == TypePlan.Deploiement ? graphe : $"Retour arrière : {graphe}");
        }
        if (mep.EcheanceRepli is null || mep.EcheanceRepli <= maintenant)
            erreurs.Add("Renseigner une échéance de retour arrière future.");

        var pilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId);
        if (pilote is null) erreurs.Add("Renseigner le pilote.");
        else if (pilote.UtilisateurId is null) erreurs.Add($"Le pilote {pilote.Nom} n'est relié à aucun compte.");

        if (!mep.EtapesDu(TypePlan.Deploiement).Any()) erreurs.Add("Ajouter au moins une étape de déploiement.");
        if (!mep.EtapesDu(TypePlan.RetourArriere).Any()) erreurs.Add("Ajouter au moins une étape de retour arrière.");
        if (string.IsNullOrWhiteSpace(mep.PlanRepli.QuandDeclencher)) erreurs.Add("Retour arrière : préciser quand le déclencher.");

        foreach (var e in mep.Etapes.OrderBy(e => e.Plan).ThenBy(e => e.Ordre))
        {
            var manquants = new List<string>();
            if (string.IsNullOrWhiteSpace(e.Action)) manquants.Add("Action");
            if (e.SystemeId is null) manquants.Add("Système");
            if (e.ResponsableId is null) manquants.Add("Responsable");
            if (e.Criticite is null) manquants.Add("Criticité");
            if (string.IsNullOrWhiteSpace(e.Instructions)) manquants.Add("Instructions exactes");
            if (string.IsNullOrWhiteSpace(e.ResultatAttendu)) manquants.Add("Résultat attendu");
            if (manquants.Count > 0) erreurs.Add($"{e.Code} : compléter {string.Join(", ", manquants)}.");
            if (e.Action.TrimStart().StartsWith("Exemple", StringComparison.OrdinalIgnoreCase))
                erreurs.Add($"{e.Code} : remplacer l'action d'exemple par une action réelle.");
            var responsable = mep.Participants.FirstOrDefault(p => p.Id == e.ResponsableId);
            if (responsable is { UtilisateurId: null })
                erreurs.Add($"{e.Code} : le responsable {responsable.Nom} n'est relié à aucun compte.");
        }

        if (mep.ProcedureRelueParId is null) erreurs.Add("La procédure doit être relue par un opérateur.");
        return erreurs;
    }

    /// <summary>Anomalies d'une résolution type avant enregistrement.</summary>
    public static List<string> ErreursResolutionType(ResolutionType r)
    {
        var erreurs = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Titre)) erreurs.Add("Donner un titre à la résolution.");
        if (string.IsNullOrWhiteSpace(r.Symptomes)) erreurs.Add("Décrire comment reconnaître le problème.");
        if (r.Corrections.Count == 0) erreurs.Add("Ajouter au moins une correction.");
        foreach (var c in r.Corrections.OrderBy(c => c.Ordre))
            if (string.IsNullOrWhiteSpace(c.Instruction) || string.IsNullOrWhiteSpace(c.ResultatAttendu))
                erreurs.Add($"Correction {c.Ordre} : préciser l'instruction et le résultat attendu.");
        if (string.IsNullOrWhiteSpace(r.VerificationInstruction) || string.IsNullOrWhiteSpace(r.VerificationAttendu))
            erreurs.Add("Préciser la vérification finale et son résultat attendu.");
        return erreurs;
    }

    // ---------- Droits ----------

    public static bool EstPilote(Mep mep, Guid utilisateurId) =>
        mep.Participants.Any(p => p.UtilisateurId == utilisateurId && (p.Id == mep.PiloteId || p.Id == mep.SuppleantId));

    /// <summary>Participant qui exécute l'étape : celui qui a pris la main, sinon le responsable.</summary>
    public static Guid? ExecutantCourant(Etape etape) => etape.ExecutantId ?? etape.ResponsableId;

    public static bool EstExecutant(Mep mep, Etape etape, Guid utilisateurId)
    {
        var executant = ExecutantCourant(etape);
        return mep.Participants.Any(p => p.Id == executant && p.UtilisateurId == utilisateurId);
    }

    /// <summary>Ajout, suppression et modification du plan réservés à la préparation (StructureAutorisee).</summary>
    public static void ExigerPreparation(Mep mep)
    {
        if (mep.Phase != Phase.Preparation)
            throw new RegleMetierException("Modification réservée à la préparation. Contacter le pilote ; ne pas changer la structure pendant l'exécution.");
    }

    // ---------- Phases ----------

    /// <summary>Équivalent de PasserExecution : réservé au pilote ou à son suppléant, plan sans anomalie.</summary>
    public static void PasserEnExecution(Mep mep, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou son suppléant peut passer le plan en exécution.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("Le plan n'est pas en préparation.");
        var erreurs = ErreursPlan(mep, maintenant);
        if (erreurs.Count > 0) throw new RegleMetierException("Plan incomplet : " + string.Join(" ", erreurs));
        mep.Phase = Phase.Execution;
        mep.DemarrageAutoriseParId = utilisateurId;
        mep.DemarrageAutoriseLe = maintenant;
    }

    /// <summary>Équivalent de RevenirPreparation : refusé tant qu'une action est en cours ; les états sont conservés.</summary>
    public static void RevenirEnPreparation(Mep mep, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou son suppléant peut revenir en préparation.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Retour en préparation possible seulement depuis l'exécution.");
        var enCours = mep.Etapes.FirstOrDefault(e => e.Etat == EtatEtape.EnCours);
        if (enCours is not null) throw new RegleMetierException($"{enCours.Code} est encore en cours. Attendre sa fin.");
        if (mep.Resolutions.Any(r => r.Statut == StatutResolution.EnCours))
            throw new RegleMetierException("Une résolution est en cours. Attendre sa fin.");
        mep.Phase = Phase.Preparation;
    }

    /// <summary>
    /// Bascule sur le plan de retour arrière. Par le pilote (utilisateurId renseigné) ou automatiquement
    /// (échéance atteinte, résolution en échec).
    /// </summary>
    public static void DeclencherRetourArriere(Mep mep, Guid? utilisateurId, string motif, DateTimeOffset maintenant)
    {
        if (utilisateurId is { } u && !EstPilote(mep, u))
            throw new RegleMetierException("Seul le pilote ou son suppléant peut déclencher le retour arrière.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le retour arrière se déclenche pendant l'exécution.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du retour arrière.");
        mep.Phase = Phase.RetourArriere;
        mep.RetourArriereDeclencheLe = maintenant;
        mep.RetourArriereDeclenchePar = utilisateurId;
        mep.RetourArriereMotif = motif;
    }

    /// <summary>Déclenche automatiquement le retour arrière si l'échéance est atteinte avant la fin du déploiement.</summary>
    public static bool AppliquerEcheance(Mep mep, DateTimeOffset maintenant)
    {
        if (mep.Phase != Phase.Execution || mep.EcheanceRepli is null || maintenant < mep.EcheanceRepli) return false;
        if (mep.EtapesDu(TypePlan.Deploiement).All(e => e.Etat == EtatEtape.Reussie)) return false;
        DeclencherRetourArriere(mep, null, MotifEcheance, maintenant);
        return true;
    }

    /// <summary>
    /// Clôture par le pilote : MEP réussie quand tout le déploiement est réussi,
    /// ou service rétabli quand tout le retour arrière est réussi.
    /// </summary>
    public static void Cloturer(Mep mep, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou son suppléant peut clôturer la MEP.");
        switch (mep.Phase)
        {
            case Phase.Execution:
                var restant = mep.EtapesDu(TypePlan.Deploiement).FirstOrDefault(e => e.Etat != EtatEtape.Reussie);
                if (restant is not null) throw new RegleMetierException($"{restant.Code} n'est pas réussie : clôture impossible.");
                mep.Issue = IssueMep.Reussie;
                break;
            case Phase.RetourArriere:
                var repli = mep.EtapesDu(TypePlan.RetourArriere).FirstOrDefault(e => e.Etat != EtatEtape.Reussie);
                if (repli is not null)
                    throw new RegleMetierException($"{repli.Code} n'est pas réussie : le service n'est pas rétabli. Suivre « Si le repli échoue ».");
                mep.Issue = IssueMep.Repliee;
                break;
            default:
                throw new RegleMetierException("Seule une MEP en exécution ou en retour arrière peut être clôturée.");
        }
        mep.Phase = Phase.Cloturee;
        mep.ClotureeLe = maintenant;
        mep.ClotureeParId = utilisateurId;
    }

    // ---------- Exécution d'une étape (ChangerEtat) ----------

    public static void Demarrer(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        ExigerExecution(mep, etape, utilisateurId);
        var dispo = Disponibilite(mep, etape, maintenant);
        if (dispo.Statut != StatutDisponibilite.Disponible)
            throw new RegleMetierException($"Démarrage impossible : {dispo.Libelle}");
        etape.Etat = EtatEtape.EnCours;
        etape.DebutReel = maintenant;
        etape.FinReelle = null;
        etape.Revision++;
    }

    /// <param name="resultatVerifie">Confirmation « j'ai vérifié et obtenu le résultat attendu », exigée pour Réussie.</param>
    public static void Terminer(Mep mep, Etape etape, EtatEtape resultat, string preuve, bool resultatVerifie, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (resultat is not (EtatEtape.Reussie or EtatEtape.Echec))
            throw new RegleMetierException("Résultat attendu : Réussie ou Échec.");
        ExigerExecution(mep, etape, utilisateurId);
        if (etape.Etat != EtatEtape.EnCours) throw new RegleMetierException("Démarrer l'action avant de déclarer son résultat.");
        if (etape.Plan == TypePlan.Deploiement && resultat == EtatEtape.Reussie && (mep.EcheanceRepli is null || maintenant >= mep.EcheanceRepli))
            throw new RegleMetierException("Échéance atteinte. Arrêter, prévenir le pilote et appliquer le retour arrière.");
        if (string.IsNullOrWhiteSpace(preuve)) throw new RegleMetierException("Renseigner la preuve du résultat ou le message d'erreur.");
        if (resultat == EtatEtape.Reussie && !resultatVerifie)
            throw new RegleMetierException("Confirmer avoir vérifié et obtenu le résultat attendu.");
        etape.Preuve = preuve;
        etape.FinReelle = maintenant;
        etape.Etat = resultat;
        etape.Revision++;
    }

    /// <summary>
    /// Prise de main : un autre participant exécute l'étape à la place du responsable (absent, indisponible…).
    /// Le responsable prévu reste inchangé ; l'exécutant est tracé sur l'étape et dans le journal.
    /// </summary>
    /// <returns>Le participant qui exécutait l'étape jusque-là.</returns>
    public static Guid? PrendreLaMain(Mep mep, Etape etape, Participant preneur)
    {
        ExigerPhaseDuPlan(mep, etape);
        if (preneur.UtilisateurId is null) throw new RegleMetierException("Seul un participant relié à un compte peut prendre la main.");
        if (etape.Etat == EtatEtape.Reussie) throw new RegleMetierException($"{etape.Code} est déjà réussie.");
        var precedent = ExecutantCourant(etape);
        if (precedent == preneur.Id) throw new RegleMetierException("Vous exécutez déjà cette étape.");
        etape.ExecutantId = preneur.Id == etape.ResponsableId ? null : preneur.Id;
        etape.Revision++;
        return precedent;
    }

    /// <summary>
    /// Reprise d'une étape en échec sans résolution type, sur décision du pilote.
    /// Avec une résolution type, la reprise passe par sa vérification finale (ValiderVerification).
    /// </summary>
    public static void AutoriserReprise(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou son suppléant peut autoriser une reprise.");
        ExigerPhaseDuPlan(mep, etape);
        if (etape.Etat != EtatEtape.Echec) throw new RegleMetierException("Seule une étape en échec peut être reprise.");
        if (ResolutionEnCours(mep, etape) is not null) throw new RegleMetierException("Une résolution est en cours sur cette étape.");
        if (etape.Plan == TypePlan.Deploiement && (mep.EcheanceRepli is null || maintenant >= mep.EcheanceRepli))
            throw new RegleMetierException("Échéance atteinte : retour arrière obligatoire, aucune reprise.");
        etape.Etat = EtatEtape.AFaire;
        etape.Revision++;
    }

    private static void ExigerPhaseDuPlan(Mep mep, Etape etape)
    {
        if (etape.Plan == TypePlan.Deploiement && mep.Phase != Phase.Execution)
            throw new RegleMetierException(mep.Phase == Phase.RetourArriere
                ? "Le retour arrière est déclenché : le déploiement est arrêté."
                : "Le pilote doit passer le plan en exécution.");
        if (etape.Plan == TypePlan.RetourArriere && mep.Phase != Phase.RetourArriere)
            throw new RegleMetierException("Le retour arrière n'est pas déclenché.");
    }

    private static void ExigerExecution(Mep mep, Etape etape, Guid utilisateurId)
    {
        ExigerPhaseDuPlan(mep, etape);
        if (!EstExecutant(mep, etape, utilisateurId))
            throw new RegleMetierException("Cette étape est attribuée à quelqu'un d'autre. Utiliser « Prendre la main » pour l'exécuter à sa place.");
    }

    // ---------- Résolutions types ----------

    /// <summary>Démarre une résolution type sur une étape de déploiement en échec, à partir d'une copie figée de la résolution.</summary>
    public static ExecutionResolution DemarrerResolution(Mep mep, Etape etape, ResolutionType resolution, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (etape.Plan != TypePlan.Deploiement) throw new RegleMetierException("Les résolutions s'appliquent aux étapes de déploiement.");
        ExigerExecution(mep, etape, utilisateurId);
        if (etape.Etat != EtatEtape.Echec) throw new RegleMetierException("Une résolution s'applique à une étape en échec.");
        if (!etape.ResolutionIds.Contains(resolution.Id)) throw new RegleMetierException("Cette résolution n'est pas prévue pour cette étape.");
        if (ResolutionEnCours(mep, etape) is not null) throw new RegleMetierException("Une résolution est déjà en cours sur cette étape.");
        if (mep.EcheanceRepli is null || maintenant >= mep.EcheanceRepli)
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

    /// <summary>Valide la prochaine correction dans l'ordre. Un échec clôt la résolution et déclenche le retour arrière.</summary>
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
        if (!EstExecutant(mep, etape, utilisateurId))
            throw new RegleMetierException("Cette étape est attribuée à quelqu'un d'autre. Utiliser « Prendre la main » pour l'exécuter à sa place.");
        return etape;
    }

    private static void EchouerResolution(Mep mep, ExecutionResolution execution, Etape etape, string cause, DateTimeOffset maintenant)
    {
        execution.Statut = StatutResolution.Echouee;
        execution.TermineeLe = maintenant;
        DeclencherRetourArriere(mep, null, $"Résolution « {execution.Titre} » sur {etape.Code} : {cause}.", maintenant);
    }

    // ---------- Structure du plan ----------

    /// <summary>Équivalent de SuppressionActionPermise.</summary>
    public static void VerifierSuppression(Mep mep, Etape etape)
    {
        ExigerPreparation(mep);
        var dependante = mep.Etapes.FirstOrDefault(e => e.Dependances.Contains(etape.Id));
        if (dependante is not null)
            throw new RegleMetierException($"Suppression impossible : {dependante.Code} dépend de {etape.Code}. Retirer ce préalable d'abord.");
    }

    /// <summary>Équivalent de InitialiserAction : prochain code E0001… (ou R0001… pour le retour arrière) sans jamais réutiliser un numéro.</summary>
    public static string ProchainCode(IEnumerable<Etape> etapes, TypePlan plan = TypePlan.Deploiement)
    {
        var prefixe = plan == TypePlan.Deploiement ? 'E' : 'R';
        var max = 0;
        foreach (var e in etapes)
            if (e.Code.Length > 1 && e.Code[0] == prefixe && int.TryParse(e.Code[1..], out var n) && n > max) max = n;
        return $"{prefixe}{max + 1:0000}";
    }

    /// <summary>Applique de nouvelles dépendances (même plan uniquement) après avoir vérifié qu'elles gardent un graphe valide.</summary>
    public static void DefinirDependances(Mep mep, Etape etape, IEnumerable<Guid> dependances)
    {
        ExigerPreparation(mep);
        var nouvelles = dependances.Distinct().ToList();
        var autrePlan = mep.Etapes.FirstOrDefault(e => nouvelles.Contains(e.Id) && e.Plan != etape.Plan);
        if (autrePlan is not null) throw new RegleMetierException($"{autrePlan.Code} appartient à un autre plan.");
        var anciennes = etape.Dependances;
        etape.Dependances = nouvelles;
        var erreur = ErreurGraphe(mep, etape.Plan);
        if (erreur is not null)
        {
            etape.Dependances = anciennes;
            throw new RegleMetierException(erreur);
        }
    }
}
