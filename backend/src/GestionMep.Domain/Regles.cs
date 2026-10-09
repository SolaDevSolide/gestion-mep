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
    /// Équivalent de ErreursPlan, mais renvoie toutes les anomalies au lieu de la première :
    /// anomalies du contenu (<see cref="ErreursContenu"/>) puis du circuit d'approbation (<see cref="ErreursApprobation"/>).
    /// </summary>
    /// <param name="approbationsMinimum">Paramètre global <see cref="ParametresGlobaux.ApprobationsMinimum"/>.</param>
    public static List<string> ErreursPlan(Mep mep, DateTimeOffset maintenant, int approbationsMinimum)
    {
        var erreurs = ErreursContenu(mep, maintenant);
        erreurs.AddRange(ErreursApprobation(mep, approbationsMinimum));
        return erreurs;
    }

    /// <summary>
    /// Anomalies du contenu de la MEP. Ajouts par rapport à l'Excel : procédure relue, comptes reliés,
    /// plan de retour arrière obligatoire, étape de sauvegarde et tests de recette.
    /// </summary>
    public static List<string> ErreursContenu(Mep mep, DateTimeOffset maintenant)
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
        if (mep.Sauvegarde.Mode == ModeSauvegarde.PendantMep && !mep.EtapesDu(TypePlan.Deploiement).Any(e => e.Type == TypeEtape.Sauvegarde))
            erreurs.Add("Sauvegarde pendant la MEP : indiquer l'étape de déploiement de type Sauvegarde.");

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

        if (mep.TestsRecette.Count == 0) erreurs.Add("Ajouter au moins un test de recette.");
        foreach (var t in mep.TestsRecette.OrderBy(t => t.Ordre))
        {
            var manquants = new List<string>();
            if (string.IsNullOrWhiteSpace(t.Libelle)) manquants.Add("Libellé");
            if (string.IsNullOrWhiteSpace(t.ResultatAttendu)) manquants.Add("Résultat attendu");
            var responsable = mep.Participants.FirstOrDefault(p => p.Id == t.ResponsableId);
            if (responsable is null) manquants.Add("Responsable");
            if (manquants.Count > 0) erreurs.Add($"Test de recette {t.Ordre} : compléter {string.Join(", ", manquants)}.");
            if (responsable is { UtilisateurId: null })
                erreurs.Add($"Test de recette {t.Ordre} : le responsable {responsable.Nom} n'est relié à aucun compte.");
        }

        if (mep.ProcedureRelueParId is null) erreurs.Add("La procédure doit être relue par un opérateur.");
        return erreurs;
    }

    /// <summary>Anomalies du circuit d'approbation : approbations manquantes, refus, approbateur pilote.</summary>
    public static List<string> ErreursApprobation(Mep mep, int approbationsMinimum)
    {
        var erreurs = new List<string>();
        var compteDuPilote = CompteDuPilote(mep);
        foreach (var a in mep.Approbations.Where(a => a.UtilisateurId == compteDuPilote))
            erreurs.Add($"{a.Nom} ne peut pas approuver : c'est le pilote.");
        var approuvees = mep.Approbations.Count(a => a.Decision == DecisionApprobation.Approuvee && a.UtilisateurId != compteDuPilote);
        if (approuvees < approbationsMinimum) erreurs.Add($"Il faut {approbationsMinimum} approbation(s).");
        foreach (var a in mep.Approbations.Where(a => a.Decision == DecisionApprobation.Refusee))
            erreurs.Add($"{a.Nom} a refusé : {a.Commentaire}");
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

    /// <summary>Pilote ou l'un de ses suppléants (mêmes droits).</summary>
    public static bool EstPilote(Mep mep, Guid utilisateurId) =>
        mep.Participants.Any(p => p.UtilisateurId == utilisateurId && (p.Id == mep.PiloteId || mep.SuppleantIds.Contains(p.Id)));

    /// <summary>Compte relié au participant pilote (hors suppléants), ou null.</summary>
    public static Guid? CompteDuPilote(Mep mep) => mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId)?.UtilisateurId;

    /// <summary>Pilote et suppléants : le pilote ne peut pas être suppléant, pas de suppléant en double.</summary>
    public static void VerifierSuppleants(Mep mep, Guid? piloteId, IReadOnlyList<Guid> suppleantIds)
    {
        string Nom(Guid id) => mep.Participants.FirstOrDefault(p => p.Id == id)?.Nom ?? "ce participant";
        if (piloteId is { } pilote && suppleantIds.Contains(pilote))
            throw new RegleMetierException($"{Nom(pilote)} est le pilote : il ne peut pas être aussi suppléant.");
        var doublon = suppleantIds.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null) throw new RegleMetierException($"{Nom(doublon.Key)} est désigné deux fois comme suppléant.");
    }

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

    /// <summary>
    /// Décision Go (équivalent de PasserExecution) : réservée au pilote ou à un suppléant, plan sans anomalie
    /// (approbations comprises) et tous les critères go / no-go actifs cochés. Les critères cochés sont recopiés dans <see cref="Mep.GoNoGo"/>.
    /// </summary>
    public static void PasserEnExecution(Mep mep, Guid utilisateurId, IReadOnlyCollection<Guid> criteresCoches,
        IReadOnlyCollection<CritereGoNoGo> criteres, int approbationsMinimum, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut passer le plan en exécution.");
        if (mep.EstModele) throw new RegleMetierException("Un modèle ne passe pas en exécution : le dupliquer pour créer une MEP.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("Le plan n'est pas en préparation.");
        var erreurs = ErreursPlan(mep, maintenant, approbationsMinimum);
        if (erreurs.Count > 0) throw new RegleMetierException("Plan incomplet : " + string.Join(" ", erreurs));
        var actifs = criteres.Where(c => c.Actif).OrderBy(c => c.Ordre).ToList();
        var manquants = actifs.Where(c => !criteresCoches.Contains(c.Id)).Select(c => $"« {c.Libelle} »").ToList();
        if (manquants.Count > 0) throw new RegleMetierException("Go impossible : cocher " + string.Join(", ", manquants) + ".");
        mep.Phase = Phase.Execution;
        mep.DemarrageAutoriseParId = utilisateurId;
        mep.DemarrageAutoriseLe = maintenant;
        mep.GoNoGo = actifs.Select(c => new CritereValide { Libelle = c.Libelle, ParId = utilisateurId, Le = maintenant }).ToList();
    }

    /// <summary>Décision No-go : la MEP reste en préparation ; seul le motif est tracé (journal).</summary>
    public static void NoGo(Mep mep, Guid utilisateurId, string motif)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut décider d'un no-go.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("Le no-go se décide en préparation.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du no-go.");
    }

    /// <summary>Équivalent de RevenirPreparation : refusé tant qu'une action est en cours ; les états sont conservés.</summary>
    public static void RevenirEnPreparation(Mep mep, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut revenir en préparation.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Retour en préparation possible seulement depuis l'exécution.");
        var enCours = mep.Etapes.FirstOrDefault(e => e.Etat == EtatEtape.EnCours);
        if (enCours is not null) throw new RegleMetierException($"{enCours.Code} est encore en cours. Attendre sa fin.");
        if (mep.Resolutions.Any(r => r.Statut == StatutResolution.EnCours))
            throw new RegleMetierException("Une résolution est en cours. Attendre sa fin.");
        mep.Phase = Phase.Preparation;
    }

    /// <summary>
    /// Bascule sur le plan de retour arrière, pendant l'exécution ou la recette. Par le pilote (utilisateurId renseigné)
    /// ou automatiquement (échéance atteinte, résolution en échec). Impossible une fois le point de non-retour franchi.
    /// </summary>
    public static void DeclencherRetourArriere(Mep mep, Guid? utilisateurId, string motif, DateTimeOffset maintenant)
    {
        if (utilisateurId is { } u && !EstPilote(mep, u))
            throw new RegleMetierException("Seul le pilote ou un suppléant peut déclencher le retour arrière.");
        if (mep.Phase is not (Phase.Execution or Phase.Recette))
            throw new RegleMetierException("Le retour arrière se déclenche pendant l'exécution ou la recette.");
        if (EtapeNonRetourFranchie(mep) is { } franchie)
            throw new RegleMetierException($"Point de non-retour franchi ({franchie.Code}) : corriger en avant.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif du retour arrière.");
        mep.Phase = Phase.RetourArriere;
        mep.RetourArriereDeclencheLe = maintenant;
        mep.RetourArriereDeclenchePar = utilisateurId;
        mep.RetourArriereMotif = motif;
    }

    /// <summary>
    /// Déclenche automatiquement le retour arrière si l'échéance est atteinte avant la clôture (exécution ou recette),
    /// sauf si le point de non-retour est franchi.
    /// </summary>
    public static bool AppliquerEcheance(Mep mep, DateTimeOffset maintenant)
    {
        if (mep.Phase is not (Phase.Execution or Phase.Recette) || mep.EcheanceRepli is null || maintenant < mep.EcheanceRepli) return false;
        if (NonRetourFranchi(mep)) return false;
        DeclencherRetourArriere(mep, null, MotifEcheance, maintenant);
        return true;
    }

    /// <summary>Fin du déploiement : le pilote passe la MEP en recette quand tout le déploiement est réussi.</summary>
    public static void PasserEnRecette(Mep mep, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut passer la MEP en recette.");
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le passage en recette se fait depuis l'exécution.");
        var restant = mep.EtapesDu(TypePlan.Deploiement).FirstOrDefault(e => e.Etat != EtatEtape.Reussie);
        if (restant is not null) throw new RegleMetierException($"{restant.Code} n'est pas réussie : recette impossible.");
        mep.Phase = Phase.Recette;
    }

    /// <summary>
    /// Clôture par le pilote : MEP réussie depuis la recette quand tous les tests sont conformes,
    /// ou service rétabli quand tout le retour arrière est réussi. Si le bilan est activé, il est obligatoire.
    /// </summary>
    public static void Cloturer(Mep mep, Guid utilisateurId, DateTimeOffset maintenant, Bilan? bilan = null)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut clôturer la MEP.");
        IssueMep issue;
        switch (mep.Phase)
        {
            case Phase.Execution:
                throw new RegleMetierException("Passer d'abord en recette : la clôture réussie se fait après les tests de recette.");
            case Phase.Recette:
                var test = mep.TestsRecette.OrderBy(t => t.Ordre).FirstOrDefault(t => t.Resultat != ResultatControle.Conforme);
                if (test is not null)
                    throw new RegleMetierException($"Test de recette {test.Ordre} « {test.Libelle} » non conforme : clôture impossible.");
                issue = IssueMep.Reussie;
                break;
            case Phase.RetourArriere:
                var repli = mep.EtapesDu(TypePlan.RetourArriere).FirstOrDefault(e => e.Etat != EtatEtape.Reussie);
                if (repli is not null)
                    throw new RegleMetierException($"{repli.Code} n'est pas réussie : le service n'est pas rétabli. Suivre « Si le repli échoue ».");
                issue = IssueMep.Repliee;
                break;
            default:
                throw new RegleMetierException("Seule une MEP en recette ou en retour arrière peut être clôturée.");
        }
        var ceQuiAMarche = (bilan?.CeQuiAMarche ?? "").Trim();
        var incidents = (bilan?.Incidents ?? "").Trim();
        if (mep.BilanActive && (ceQuiAMarche.Length == 0 || incidents.Length == 0))
            throw new RegleMetierException("Bilan : décrire ce qui a marché et les incidents (« Rien à signaler » accepté).");
        if (bilan is not null && (ceQuiAMarche.Length > 0 || incidents.Length > 0 || !string.IsNullOrWhiteSpace(bilan.Actions)))
            mep.Bilan = new Bilan
            {
                CeQuiAMarche = ceQuiAMarche, Incidents = incidents, Actions = (bilan.Actions ?? "").Trim(),
                RedigeParId = utilisateurId, Le = maintenant,
            };
        mep.Issue = issue;
        mep.Phase = Phase.Cloturee;
        mep.ClotureeLe = maintenant;
        mep.ClotureeParId = utilisateurId;
    }

    // ---------- Circuit d'approbation ----------

    /// <summary>
    /// Toute modification du contenu en préparation remet les décisions à null.
    /// Renvoie vrai si au moins une décision a été effacée (à journaliser).
    /// </summary>
    public static bool ReinitialiserApprobations(Mep mep)
    {
        if (mep.Phase != Phase.Preparation) return false;
        var effacee = false;
        foreach (var a in mep.Approbations)
        {
            if (a.Decision is not null) effacee = true;
            a.Decision = null;
            a.Commentaire = "";
            a.Le = null;
        }
        return effacee;
    }

    /// <summary>Remplace la liste des approbateurs (comptes) ; ceux déjà désignés gardent leur entrée.</summary>
    public static void DefinirApprobateurs(Mep mep, IReadOnlyList<(Guid UtilisateurId, string Nom)> approbateurs)
    {
        ExigerPreparation(mep);
        var compteDuPilote = CompteDuPilote(mep);
        var pilote = approbateurs.FirstOrDefault(a => a.UtilisateurId == compteDuPilote);
        if (compteDuPilote is not null && pilote.UtilisateurId == compteDuPilote)
            throw new RegleMetierException($"{pilote.Nom} est le pilote : il ne peut pas approuver sa propre MEP.");
        mep.Approbations = approbateurs.DistinctBy(a => a.UtilisateurId).Select(a =>
            mep.Approbations.FirstOrDefault(x => x.UtilisateurId == a.UtilisateurId) ?? new Approbation { UtilisateurId = a.UtilisateurId, Nom = a.Nom }).ToList();
    }

    /// <summary>
    /// Décision d'un approbateur désigné, en préparation, quand il ne reste que des anomalies d'approbation.
    /// Un refus exige un commentaire.
    /// </summary>
    public static Approbation Decider(Mep mep, Guid utilisateurId, DecisionApprobation decision, string commentaire, DateTimeOffset maintenant)
    {
        var approbation = mep.Approbations.FirstOrDefault(a => a.UtilisateurId == utilisateurId)
            ?? throw new RegleMetierException("Seul un approbateur désigné peut approuver ou refuser cette MEP.");
        if (CompteDuPilote(mep) == utilisateurId) throw new RegleMetierException("Le pilote ne peut pas approuver sa propre MEP.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("L'approbation se fait en préparation.");
        var erreurs = ErreursContenu(mep, maintenant);
        if (erreurs.Count > 0) throw new RegleMetierException("Plan incomplet, approbation impossible : " + string.Join(" ", erreurs));
        if (decision == DecisionApprobation.Refusee && string.IsNullOrWhiteSpace(commentaire))
            throw new RegleMetierException("Expliquer le refus dans le commentaire.");
        approbation.Decision = decision;
        approbation.Commentaire = commentaire.Trim();
        approbation.Le = maintenant;
        return approbation;
    }

    // ---------- Point de non-retour ----------

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

    /// <summary>Type d'étape et point de non-retour : réservés au déploiement.</summary>
    public static void DefinirType(Etape etape, TypeEtape type, bool pointDeNonRetour)
    {
        if (etape.Plan == TypePlan.RetourArriere && (type != TypeEtape.Standard || pointDeNonRetour))
            throw new RegleMetierException("Le plan de retour arrière n'accepte ni étape de sauvegarde ni point de non-retour.");
        etape.Type = type;
        if (etape.PointDeNonRetour != pointDeNonRetour)
        {
            etape.NonRetourAutoriseParId = null;
            etape.NonRetourAutoriseLe = null;
        }
        etape.PointDeNonRetour = pointDeNonRetour;
    }

    // ---------- Recette ----------

    /// <summary>Résultat d'un test de recette : par son responsable ou le pilote ; observation obligatoire.</summary>
    public static void ValiderTestRecette(Mep mep, TestRecette test, ResultatControle resultat, string observation, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var responsable = mep.Participants.Any(p => p.Id == test.ResponsableId && p.UtilisateurId == utilisateurId);
        if (!responsable && !EstPilote(mep, utilisateurId))
            throw new RegleMetierException("Seul le responsable du test ou le pilote peut en donner le résultat.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les tests de recette se valident en phase de recette.");
        if (test.Resultat is not null) throw new RegleMetierException($"Test {test.Ordre} déjà validé : le pilote peut le faire refaire.");
        if (string.IsNullOrWhiteSpace(observation)) throw new RegleMetierException("Décrire ce qui a été observé.");
        test.Resultat = resultat;
        test.Observation = observation.Trim();
        test.ParId = utilisateurId;
        test.Le = maintenant;
    }

    /// <summary>Le pilote fait refaire un test de recette : son résultat est effacé.</summary>
    public static void RefaireTestRecette(Mep mep, TestRecette test, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut faire refaire un test.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les tests de recette se refont en phase de recette.");
        if (test.Resultat is null) throw new RegleMetierException($"Test {test.Ordre} pas encore validé.");
        test.Resultat = null;
        test.Observation = "";
        test.ParId = null;
        test.Le = null;
    }

    // ---------- Exécution d'une étape (ChangerEtat) ----------

    public static void Demarrer(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        ExigerExecution(mep, etape, utilisateurId);
        var dispo = Disponibilite(mep, etape, maintenant);
        if (dispo.Statut != StatutDisponibilite.Disponible)
            throw new RegleMetierException($"Démarrage impossible : {dispo.Libelle}");
        if (etape.PointDeNonRetour && etape.NonRetourAutoriseParId is null)
            throw new RegleMetierException("Point de non-retour : l'accord du pilote est requis.");
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
        if (etape.Plan == TypePlan.Deploiement && resultat == EtatEtape.Reussie && EcheanceAtteinte(mep, maintenant))
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

    /// <summary>
    /// Résolution en échec : déclenche le retour arrière, sauf point de non-retour franchi
    /// (l'étape reste en échec ; le pilote peut autoriser une reprise).
    /// </summary>
    private static void EchouerResolution(Mep mep, ExecutionResolution execution, Etape etape, string cause, DateTimeOffset maintenant)
    {
        execution.Statut = StatutResolution.Echouee;
        execution.TermineeLe = maintenant;
        if (!NonRetourFranchi(mep))
            DeclencherRetourArriere(mep, null, $"Résolution « {execution.Titre} » sur {etape.Code} : {cause}.", maintenant);
    }

    // ---------- Modèles et duplication ----------

    /// <summary>
    /// Copie profonde d'une MEP, en préparation, avec de nouveaux identifiants et toutes les références remappées
    /// (codes d'étapes conservés). Ne sont pas copiés : échéance, date de MEP, relecture, go / no-go, bilan,
    /// résolutions exécutées, résultats (étapes, tests de recette, sauvegarde réalisée) et décisions d'approbation.
    /// </summary>
    public static Mep Dupliquer(Mep source, string? environnement, bool commeModele, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var mep = new Mep
        {
            Outil = source.Outil, VersionActuelle = source.VersionActuelle, VersionCible = source.VersionCible,
            Objet = source.Objet, Impacts = source.Impacts, HeureDebut = source.HeureDebut, HeureFin = source.HeureFin,
            FuseauHoraire = source.FuseauHoraire, IndisponibiliteMinutes = source.IndisponibiliteMinutes,
            Environnement = environnement is null ? source.Environnement : environnement.Trim(),
            EstModele = commeModele, BilanActive = source.BilanActive,
            CreeLe = maintenant, CreeParUtilisateurId = utilisateurId,
            PlanRepli = new PlanRepli
            {
                QuandDeclencher = source.PlanRepli.QuandDeclencher, DureeTesteeMinutes = source.PlanRepli.DureeTesteeMinutes,
                VersionARestaurer = source.PlanRepli.VersionARestaurer, DonneesDepuisSauvegarde = source.PlanRepli.DonneesDepuisSauvegarde,
                SiRepliEchoue = source.PlanRepli.SiRepliEchoue,
            },
        };

        var participants = new Dictionary<Guid, Guid>();
        foreach (var p in source.Participants)
        {
            var copie = new Participant { MepId = mep.Id, Nom = p.Nom, Role = p.Role, Contact = p.Contact, UtilisateurId = p.UtilisateurId };
            participants[p.Id] = copie.Id;
            mep.Participants.Add(copie);
        }
        Guid? P(Guid? id) => id is { } g && participants.TryGetValue(g, out var n) ? n : null;
        mep.PiloteId = P(source.PiloteId);
        mep.SuppleantIds = source.SuppleantIds.Select(id => P(id)).OfType<Guid>().ToList();

        var systemes = new Dictionary<Guid, Guid>();
        foreach (var s in source.Systemes)
        {
            var copie = new Systeme { MepId = mep.Id, Nom = s.Nom, Cible = s.Cible, Impact = s.Impact, CatalogueId = s.CatalogueId };
            systemes[s.Id] = copie.Id;
            mep.Systemes.Add(copie);
        }
        Guid? S(Guid? id) => id is { } g && systemes.TryGetValue(g, out var n) ? n : null;

        foreach (var l in source.Livrables)
            mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = l.Objet, Version = l.Version, Emplacement = l.Emplacement, SystemeId = S(l.SystemeId) });

        var s0 = source.Sauvegarde;
        mep.Sauvegarde = new Sauvegarde
        {
            Mode = s0.Mode, Structure = s0.Structure, Donnees = s0.Donnees, Configuration = s0.Configuration,
            ResponsableId = P(s0.ResponsableId), Procedure = s0.Procedure, Copies = s0.Copies,
        };

        var etapes = source.Etapes.ToDictionary(e => e.Id, _ => Guid.NewGuid());
        foreach (var e in source.Etapes.OrderBy(e => e.Plan).ThenBy(e => e.Ordre))
            mep.Etapes.Add(new Etape
            {
                Id = etapes[e.Id], MepId = mep.Id, Plan = e.Plan, Type = e.Type, PointDeNonRetour = e.PointDeNonRetour,
                Code = e.Code, Ordre = e.Ordre, Action = e.Action, SystemeId = S(e.SystemeId), ResponsableId = P(e.ResponsableId),
                Criticite = e.Criticite, Instructions = e.Instructions, ResultatAttendu = e.ResultatAttendu,
                Dependances = e.Dependances.Where(etapes.ContainsKey).Select(d => etapes[d]).ToList(),
                ResolutionIds = [.. e.ResolutionIds],
            });

        mep.TestsRecette = source.TestsRecette.OrderBy(t => t.Ordre).Select(t => new TestRecette
        {
            Ordre = t.Ordre, Libelle = t.Libelle, ResultatAttendu = t.ResultatAttendu, ResponsableId = P(t.ResponsableId),
        }).ToList();
        mep.Approbations = source.Approbations.Select(a => new Approbation { UtilisateurId = a.UtilisateurId, Nom = a.Nom }).ToList();
        return mep;
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
