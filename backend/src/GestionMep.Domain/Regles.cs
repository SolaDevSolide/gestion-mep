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

        if (string.IsNullOrWhiteSpace(etape.Action) || etape.SystemeIds.Count == 0 || etape.ResponsableId is null || etape.Criticite is null)
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
    /// plan de retour arrière obligatoire, sauvegarde de chaque système et fiches de recette.
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
        erreurs.AddRange(ErreursSauvegarde(mep));

        foreach (var e in mep.EtapesTriees())
        {
            var manquants = new List<string>();
            if (string.IsNullOrWhiteSpace(e.Action)) manquants.Add("Action");
            if (e.SystemeIds.Count == 0) manquants.Add("Système");
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

        erreurs.AddRange(ErreursRecette(mep));

        if (mep.ProcedureRelueParId is null) erreurs.Add("La procédure doit être relue par un opérateur.");
        return erreurs;
    }

    /// <summary>Sauvegarde de chaque système : statut renseigné, justification si non requise, étape de type Sauvegarde si faite pendant la MEP.</summary>
    public static List<string> ErreursSauvegarde(Mep mep)
    {
        var erreurs = new List<string>();
        foreach (var s in mep.Systemes)
        {
            var sv = s.Sauvegarde;
            switch (sv.Statut)
            {
                case StatutSauvegarde.ARenseigner:
                    erreurs.Add($"Sauvegarde : indiquer si {s.Nom} doit être sauvegardé.");
                    break;
                case StatutSauvegarde.NonRequise when string.IsNullOrWhiteSpace(sv.Justification):
                    erreurs.Add($"Sauvegarde de {s.Nom} non requise : justifier.");
                    break;
                case StatutSauvegarde.Requise when sv.Moment == ModeSauvegarde.PendantMep
                    && !mep.EtapesDu(TypePlan.Deploiement).Any(e => e.Type == TypeEtape.Sauvegarde && e.SystemeIds.Contains(s.Id)):
                    erreurs.Add($"Sauvegarde de {s.Nom} pendant la MEP : ajouter une étape de déploiement de type Sauvegarde sur ce système.");
                    break;
            }
        }
        return erreurs;
    }

    /// <summary>Nom d'une fiche de recette dans les messages : « Recette « Titre » » ou « Recette 2 » sans titre.</summary>
    public static string NomRecette(FicheRecette f) =>
        string.IsNullOrWhiteSpace(f.Titre) ? $"Recette {f.Ordre}" : $"Recette « {f.Titre} »";

    /// <summary>Fiches de recette : au moins une, chacune titrée avec au moins une étape complète.</summary>
    public static List<string> ErreursRecette(Mep mep)
    {
        var erreurs = new List<string>();
        if (mep.FichesRecette.Count == 0) erreurs.Add("Ajouter au moins une fiche de recette.");
        foreach (var f in mep.FichesRecette.OrderBy(f => f.Ordre))
        {
            if (string.IsNullOrWhiteSpace(f.Titre)) erreurs.Add($"{NomRecette(f)} : indiquer un titre.");
            if (f.Etapes.Count == 0) erreurs.Add($"{NomRecette(f)} : ajouter au moins une étape de test.");
            foreach (var t in f.Etapes.OrderBy(t => t.Ordre))
                if (string.IsNullOrWhiteSpace(t.Instructions) || string.IsNullOrWhiteSpace(t.ResultatAttendu))
                    erreurs.Add($"{NomRecette(f)}, étape {t.Ordre} : compléter les instructions et le résultat attendu.");
        }
        return erreurs;
    }

    /// <summary>
    /// Avertissements non bloquants du plan (ils ne figurent pas dans <see cref="ErreursPlan"/>) : aucun responsable technique,
    /// pilote également responsable technique, responsable technique absent dont les étapes ne sont pas reprenables par un autre,
    /// aucun système sauvegardé, système sans fiche de recette.
    /// </summary>
    public static List<string> Avertissements(Mep mep)
    {
        var avertissements = new List<string>();
        string Nom(Guid id) => mep.Participants.FirstOrDefault(p => p.Id == id)?.Nom ?? "Un responsable technique";

        if (mep.ResponsablesTechniques.Count == 0)
            avertissements.Add("Aucun responsable technique renseigné.");
        else
        {
            if (mep.PiloteId is { } pilote && mep.ResponsablesTechniques.Any(r => r.ParticipantId == pilote))
                avertissements.Add($"{Nom(pilote)} est à la fois responsable technique et pilote : prévoir un suppléant qui connaît le plan.");

            foreach (var r in mep.ResponsablesTechniques.Where(r => r.Disponibilite == DisponibiliteJourJ.Absent))
                foreach (var e in mep.EtapesTriees())
                {
                    var concernee = e.ResponsableId == r.ParticipantId || e.SystemeIds.Any(r.SystemeIds.Contains);
                    if (!concernee) continue;
                    var reprenable = !string.IsNullOrWhiteSpace(e.Instructions) && !string.IsNullOrWhiteSpace(e.ResultatAttendu) && e.ResolutionIds.Count > 0;
                    if (!reprenable)
                        avertissements.Add($"{e.Code} : {Nom(r.ParticipantId)} sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre.");
                }
        }

        if (mep.Systemes.Count > 0 && mep.Systemes.All(s => s.Sauvegarde.Statut == StatutSauvegarde.NonRequise))
            avertissements.Add("Aucun système n'est sauvegardé.");
        foreach (var s in mep.Systemes.Where(s => mep.FichesRecette.All(f => f.SystemeId != s.Id)))
            avertissements.Add($"Aucune recette pour le système {s.Nom}.");
        return avertissements;
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

    /// <summary>Compte relié à l'un des responsables techniques de la MEP.</summary>
    public static bool EstResponsableTechnique(Mep mep, Guid utilisateurId) =>
        mep.ResponsablesTechniques.Any(r => mep.Participants.Any(p => p.Id == r.ParticipantId && p.UtilisateurId == utilisateurId));

    /// <summary>Droit d'éditer le contenu en préparation : responsables techniques, pilote, suppléants et administrateurs.</summary>
    public static bool PeutEditerContenu(Mep mep, Guid utilisateurId, bool estAdmin) =>
        estAdmin || EstPilote(mep, utilisateurId) || EstResponsableTechnique(mep, utilisateurId);

    public const string MessageEditionInterdite =
        "Seuls les responsables techniques, le pilote, ses suppléants ou un administrateur peuvent modifier cette MEP.";

    /// <summary>
    /// Définit la liste des responsables techniques : participants de la MEP, une seule fois chacun,
    /// systèmes de la MEP, disponibilité valide. Les textes sont nettoyés.
    /// </summary>
    public static void DefinirResponsablesTechniques(Mep mep, IEnumerable<ResponsableTechnique> responsables)
    {
        ExigerPreparation(mep);
        var liste = responsables.ToList();
        var doublon = liste.GroupBy(r => r.ParticipantId).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null)
        {
            var nom = mep.Participants.FirstOrDefault(p => p.Id == doublon.Key)?.Nom ?? "Ce participant";
            throw new RegleMetierException($"{nom} est déjà responsable technique : un participant n'apparaît qu'une fois.");
        }
        foreach (var r in liste)
        {
            if (mep.Participants.All(p => p.Id != r.ParticipantId)) throw new RegleMetierException("Responsable technique : participant inconnu dans cette MEP.");
            if (!Enum.IsDefined(r.Disponibilite)) throw new RegleMetierException("Responsable technique : disponibilité inconnue.");
            if (r.SystemeIds.Any(s => mep.Systemes.All(x => x.Id != s))) throw new RegleMetierException("Responsable technique : système inconnu dans cette MEP.");
            r.SystemeIds = r.SystemeIds.Distinct().ToList();
            r.Perimetre = (r.Perimetre ?? "").Trim();
            r.ContactJourJ = (r.ContactJourJ ?? "").Trim();
        }
        mep.ResponsablesTechniques = liste;
    }

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
        if (EtapeNonRetourFranchie(mep) is { } franchie)
            throw new RegleMetierException($"Le point de non-retour {franchie.Code} est franchi : on ne peut plus revenir en préparation, tout problème se corrige en avant.");
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
    /// Clôture par le pilote : MEP réussie depuis la recette quand toutes les étapes de test sont conformes,
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
                foreach (var f in mep.FichesRecette.OrderBy(f => f.Ordre))
                {
                    var test = f.Etapes.OrderBy(t => t.Ordre).FirstOrDefault(t => t.Resultat != ResultatControle.Conforme);
                    if (test is not null)
                        throw new RegleMetierException($"{NomRecette(f)}, étape {test.Ordre} non conforme : clôture impossible.");
                }
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

    /// <summary>Nouvelle fiche de recette vide, ajoutée en fin (préparation uniquement). Le système doit appartenir à la MEP.</summary>
    public static FicheRecette AjouterFicheRecette(Mep mep, Guid? systemeId, string titre)
    {
        ExigerPreparation(mep);
        ExigerSystemeDeLaMep(mep, systemeId);
        var fiche = new FicheRecette
        {
            Ordre = mep.FichesRecette.Count == 0 ? 1 : mep.FichesRecette.Max(f => f.Ordre) + 1,
            SystemeId = systemeId, Titre = (titre ?? "").Trim(),
        };
        // Nouvelle liste : la colonne jsonb est réécrite.
        mep.FichesRecette = [.. mep.FichesRecette, fiche];
        return fiche;
    }

    /// <summary>
    /// Remplace le contenu d'une fiche de recette : les étapes reçues avec un identifiant connu gardent leur identité
    /// (et leur résultat éventuel), les autres sont créées ; l'ordre est celui reçu.
    /// </summary>
    public static void ModifierFicheRecette(Mep mep, FicheRecette fiche, Guid? systemeId, string titre,
        IEnumerable<(Guid? Id, string Instructions, string ResultatAttendu)> etapes)
    {
        ExigerPreparation(mep);
        ExigerSystemeDeLaMep(mep, systemeId);
        var anciennes = fiche.Etapes.ToDictionary(t => t.Id);
        var vues = new HashSet<Guid>();
        var nouvelles = new List<EtapeTest>();
        foreach (var (id, instructions, attendu) in etapes)
        {
            var t = id is { } i && anciennes.TryGetValue(i, out var existante) && vues.Add(i) ? existante : new EtapeTest();
            t.Ordre = nouvelles.Count + 1;
            t.Instructions = (instructions ?? "").Trim();
            t.ResultatAttendu = (attendu ?? "").Trim();
            nouvelles.Add(t);
        }
        fiche.SystemeId = systemeId;
        fiche.Titre = (titre ?? "").Trim();
        fiche.Etapes = nouvelles;
        mep.FichesRecette = [.. mep.FichesRecette];
    }

    /// <summary>Supprime une fiche de recette (préparation uniquement) ; les fiches restantes sont renumérotées 1..n.</summary>
    public static void SupprimerFicheRecette(Mep mep, FicheRecette fiche)
    {
        ExigerPreparation(mep);
        var restantes = mep.FichesRecette.Where(f => f.Id != fiche.Id).OrderBy(f => f.Ordre).ToList();
        for (var i = 0; i < restantes.Count; i++) restantes[i].Ordre = i + 1;
        mep.FichesRecette = restantes;
    }

    private static void ExigerSystemeDeLaMep(Mep mep, Guid? systemeId)
    {
        if (systemeId is { } s && mep.Systemes.All(x => x.Id != s)) throw new RegleMetierException("Système inconnu dans cette MEP.");
    }

    /// <summary>
    /// Résultat d'une étape de test, en phase de recette : par tout participant relié à un compte (pilote et suppléants compris)
    /// ou un administrateur ; observation obligatoire ; une étape déjà validée doit d'abord être refaite.
    /// </summary>
    public static void ValiderEtapeTest(Mep mep, FicheRecette fiche, EtapeTest test, ResultatControle resultat, string observation,
        Guid utilisateurId, bool estAdmin, DateTimeOffset maintenant)
    {
        if (!estAdmin && !mep.Participants.Any(p => p.UtilisateurId == utilisateurId))
            throw new RegleMetierException("Seuls les participants de la MEP reliés à un compte, le pilote ou un administrateur peuvent donner le résultat d'une recette.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les recettes se déroulent en phase de recette.");
        if (test.Resultat is not null)
            throw new RegleMetierException($"{NomRecette(fiche)}, étape {test.Ordre} déjà validée : le pilote peut la faire refaire.");
        if (string.IsNullOrWhiteSpace(observation)) throw new RegleMetierException("Décrire ce qui a été observé.");
        test.Resultat = resultat;
        test.Observation = observation.Trim();
        test.ParId = utilisateurId;
        test.Le = maintenant;
        mep.FichesRecette = [.. mep.FichesRecette];
    }

    /// <summary>Le pilote fait refaire une étape de test (<paramref name="test"/> renseigné) ou toute la fiche : les résultats sont effacés.</summary>
    public static void RefaireRecette(Mep mep, FicheRecette fiche, EtapeTest? test, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut faire refaire une recette.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les recettes se refont en phase de recette.");
        var cibles = test is null ? fiche.Etapes.Where(t => t.Resultat is not null).ToList() : [test];
        if (test is { Resultat: null }) throw new RegleMetierException($"{NomRecette(fiche)}, étape {test.Ordre} pas encore validée.");
        if (cibles.Count == 0) throw new RegleMetierException($"{NomRecette(fiche)} : aucune étape validée.");
        foreach (var t in cibles)
        {
            t.Resultat = null;
            t.Observation = "";
            t.ParId = null;
            t.Le = null;
        }
        mep.FichesRecette = [.. mep.FichesRecette];
    }

    // ---------- Sauvegarde par système ----------

    /// <summary>
    /// Sauvegarde d'un système de la MEP (préparation uniquement). Les textes sont rognés ; la justification d'une sauvegarde
    /// non requise et l'étape de sauvegarde sont vérifiées par <see cref="ErreursSauvegarde"/>, pas ici.
    /// </summary>
    public static void DefinirSauvegardeSysteme(Mep mep, Systeme systeme, StatutSauvegarde statut, ModeSauvegarde moment, string contenu, string justification)
    {
        ExigerPreparation(mep);
        if (!Enum.IsDefined(statut) || !Enum.IsDefined(moment)) throw new RegleMetierException("Sauvegarde : valeur inconnue.");
        var s = systeme.Sauvegarde;
        s.Statut = statut;
        s.Moment = moment;
        s.Contenu = (contenu ?? "").Trim();
        s.Justification = (justification ?? "").Trim();
    }

    /// <summary>Détail du journal : « Sauvegarde BDD : requise (avant la MEP) », « … : non requise (justification) »…</summary>
    public static string DetailSauvegarde(Systeme s) => s.Sauvegarde.Statut switch
    {
        StatutSauvegarde.Requise => $"Sauvegarde {s.Nom} : requise ({(s.Sauvegarde.Moment == ModeSauvegarde.PendantMep ? "pendant" : "avant")} la MEP)",
        StatutSauvegarde.NonRequise => string.IsNullOrWhiteSpace(s.Sauvegarde.Justification)
            ? $"Sauvegarde {s.Nom} : non requise" : $"Sauvegarde {s.Nom} : non requise ({s.Sauvegarde.Justification})",
        _ => $"Sauvegarde {s.Nom} : à renseigner",
    };

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
    public static Guid? PrendreLaMain(Mep mep, Etape etape, Participant preneur, string motif)
    {
        ExigerPhaseDuPlan(mep, etape);
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer pourquoi vous prenez la main.");
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
    /// (codes d'étapes, lignes et positions conservés). Ne sont pas copiés : échéance, date de MEP, relecture, go / no-go, bilan,
    /// résolutions exécutées, résultats (étapes, recettes, sauvegarde réalisée) et décisions d'approbation.
    /// </summary>
    public const string PrefixeModele = "Modèle : ";

    /// <summary>
    /// Outil de la copie : « X (copie) » ; une MEP créée depuis un modèle reprend l'outil du modèle sans le préfixe « Modèle : ».
    /// </summary>
    public static string NomCopie(Mep source, bool commeModele)
    {
        var outil = source.Outil.Trim();
        if (source.EstModele && !commeModele)
            return outil.StartsWith(PrefixeModele, StringComparison.OrdinalIgnoreCase) ? outil[PrefixeModele.Length..].Trim() : outil;
        return outil.EndsWith("(copie)", StringComparison.OrdinalIgnoreCase) ? outil : $"{outil} (copie)";
    }

    public static Mep Dupliquer(Mep source, string? environnement, bool commeModele, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var mep = new Mep
        {
            Outil = NomCopie(source, commeModele), VersionActuelle = source.VersionActuelle, VersionCible = source.VersionCible,
            Objet = source.Objet, Impacts = source.Impacts, HeureDebut = source.HeureDebut, HeureFin = source.HeureFin,
            FuseauHoraire = source.FuseauHoraire, IndisponibiliteMinutes = source.IndisponibiliteMinutes,
            Environnement = string.IsNullOrWhiteSpace(environnement) ? source.Environnement : environnement.Trim(),
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
            var copie = new Systeme
            {
                MepId = mep.Id, Nom = s.Nom, Cible = s.Cible, Impact = s.Impact, CatalogueId = s.CatalogueId,
                Sauvegarde = new SauvegardeSysteme
                {
                    Statut = s.Sauvegarde.Statut, Moment = s.Sauvegarde.Moment, Contenu = s.Sauvegarde.Contenu, Justification = s.Sauvegarde.Justification,
                },
            };
            systemes[s.Id] = copie.Id;
            mep.Systemes.Add(copie);
        }
        Guid? S(Guid? id) => id is { } g && systemes.TryGetValue(g, out var n) ? n : null;
        mep.ResponsablesTechniques = source.ResponsablesTechniques
            .Where(r => P(r.ParticipantId) is not null)
            .Select(r => new ResponsableTechnique
            {
                ParticipantId = P(r.ParticipantId)!.Value, SystemeIds = r.SystemeIds.Select(id => S(id)).OfType<Guid>().ToList(),
                Perimetre = r.Perimetre, Disponibilite = r.Disponibilite, ContactJourJ = r.ContactJourJ,
            }).ToList();

        foreach (var l in source.Livrables)
            mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = l.Objet, Version = l.Version, Emplacement = l.Emplacement, SystemeId = S(l.SystemeId) });

        var s0 = source.Sauvegarde;
        mep.Sauvegarde = new Sauvegarde
        {
            Mode = s0.Mode, Structure = s0.Structure, Donnees = s0.Donnees, Configuration = s0.Configuration,
            ResponsableId = P(s0.ResponsableId), Procedure = s0.Procedure, Copies = s0.Copies,
        };

        var etapes = source.Etapes.ToDictionary(e => e.Id, _ => Guid.NewGuid());
        foreach (var e in source.EtapesTriees())
            mep.Etapes.Add(new Etape
            {
                Id = etapes[e.Id], MepId = mep.Id, Plan = e.Plan, Type = e.Type, PointDeNonRetour = e.PointDeNonRetour,
                Code = e.Code, Ligne = e.Ligne, Ordre = e.Ordre, Action = e.Action, SystemeIds = e.SystemeIds.Select(id => S(id)).OfType<Guid>().ToList(),
                DureeEstimeeMinutes = e.DureeEstimeeMinutes, ResponsableId = P(e.ResponsableId),
                Criticite = e.Criticite, Instructions = e.Instructions, ResultatAttendu = e.ResultatAttendu,
                Dependances = e.Dependances.Where(etapes.ContainsKey).Select(d => etapes[d]).ToList(),
                ResolutionIds = [.. e.ResolutionIds],
            });

        mep.FichesRecette = source.FichesRecette.OrderBy(f => f.Ordre).Select(f => new FicheRecette
        {
            Ordre = f.Ordre, SystemeId = S(f.SystemeId), Titre = f.Titre,
            Etapes = f.Etapes.OrderBy(t => t.Ordre).Select(t => new EtapeTest { Ordre = t.Ordre, Instructions = t.Instructions, ResultatAttendu = t.ResultatAttendu }).ToList(),
        }).ToList();
        mep.Approbations = source.Approbations.Select(a => new Approbation { UtilisateurId = a.UtilisateurId, Nom = a.Nom }).ToList();
        return mep;
    }

    // ---------- Structure du plan : graphe par lignes ----------

    /// <summary>Lignes d'un plan, de haut en bas : étapes regroupées par <see cref="Etape.Ligne"/>, chacune triée par <see cref="Etape.Ordre"/>.</summary>
    public static List<List<Etape>> Lignes(Mep mep, TypePlan plan) =>
        mep.EtapesDu(plan).GroupBy(e => e.Ligne).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

    /// <summary>Recalcule Ligne (0..n-1, lignes vides supprimées) et Ordre (0..k-1) depuis l'ordre des listes.</summary>
    private static void Renumeroter(IEnumerable<List<Etape>> lignes)
    {
        var n = 0;
        foreach (var ligne in lignes.Where(l => l.Count > 0))
        {
            for (var i = 0; i < ligne.Count; i++)
            {
                ligne[i].Ligne = n;
                ligne[i].Ordre = i;
            }
            n++;
        }
    }

    /// <summary>Change les préalables d'une étape (nouvelle liste : la colonne est réécrite).</summary>
    private static void PoserDependances(Etape etape, List<Guid> dependances)
    {
        if (!etape.Dependances.SequenceEqual(dependances)) etape.Dependances = dependances;
    }

    /// <summary>Préalables de chaque étape d'un plan avant une opération sur le graphe.</summary>
    private static Dictionary<Etape, List<Guid>> Instantane(Mep mep, TypePlan plan) =>
        mep.EtapesDu(plan).ToDictionary(e => e, e => e.Dependances.ToList());

    /// <summary>
    /// Une seule fois par opération, la révision des étapes dont les préalables ont changé augmente
    /// (un client qui édite l'une d'elles devra la relire).
    /// </summary>
    private static void IncrementerRevisions(Dictionary<Etape, List<Guid>> avant, Etape? sauf = null)
    {
        foreach (var (e, prealables) in avant)
            if (e != sauf && !e.Dependances.SequenceEqual(prealables)) e.Revision++;
    }

    /// <summary>
    /// Liens par défaut : l'étape attend toutes les étapes de la ligne au-dessus et est attendue par toutes celles de la ligne
    /// en dessous ; ses autres liens sont retirés. Les étapes voisines modifiées voient leur révision augmenter.
    /// </summary>
    public static void RelierParDefaut(Mep mep, Etape etape)
    {
        var avant = Instantane(mep, etape.Plan);
        Relier(mep, etape);
        IncrementerRevisions(avant, sauf: etape);
    }

    private static void Relier(Mep mep, Etape etape)
    {
        var plan = mep.EtapesDu(etape.Plan).Where(e => e != etape).ToList();
        foreach (var e in plan.Where(e => e.Dependances.Contains(etape.Id)))
            PoserDependances(e, e.Dependances.Where(d => d != etape.Id).ToList());
        etape.Dependances = plan.Where(e => e.Ligne == etape.Ligne - 1).Select(e => e.Id).ToList();
        foreach (var e in plan.Where(e => e.Ligne == etape.Ligne + 1))
            PoserDependances(e, [.. e.Dependances, etape.Id]);
    }

    /// <summary>Les étapes données qui n'ont plus aucun préalable (hors première ligne) attendent toute la ligne au-dessus d'elles.</summary>
    private static void RattacherOrphelins(Mep mep, IEnumerable<Etape> etapes)
    {
        foreach (var e in etapes.Where(e => e.Ligne > 0 && e.Dependances.Count == 0 && mep.Etapes.Contains(e)))
            PoserDependances(e, mep.EtapesDu(e.Plan).Where(x => x.Ligne == e.Ligne - 1).Select(x => x.Id).ToList());
    }

    /// <summary>Retire les liens devenus invalides (préalable disparu ou qui ne serait plus sur une ligne au-dessus).</summary>
    private static void NettoyerLiens(Mep mep, TypePlan plan)
    {
        var etapes = mep.EtapesDu(plan).ToList();
        var parId = etapes.ToDictionary(e => e.Id);
        foreach (var e in etapes)
            PoserDependances(e, e.Dependances.Where(d => parId.TryGetValue(d, out var p) && p.Ligne < e.Ligne).ToList());
    }

    /// <summary>
    /// Place une nouvelle étape (pas encore dans <see cref="Mep.Etapes"/> ou ignorée si elle y est) : <paramref name="ligne"/> absente
    /// ou égale au nombre de lignes = nouvelle ligne en bas ; <paramref name="position"/> absente = en fin de ligne (bornée sinon).
    /// </summary>
    public static void PlacerNouvelle(Mep mep, Etape etape, int? ligne, int? position)
    {
        ExigerPreparation(mep);
        var lignes = Lignes(mep, etape.Plan).Select(l => l.Where(e => e != etape).ToList()).Where(l => l.Count > 0).ToList();
        var l = ligne ?? lignes.Count;
        if (l < 0 || l > lignes.Count) throw new RegleMetierException($"Ligne inexistante : choisir une ligne entre 0 et {lignes.Count}.");
        if (l == lignes.Count) lignes.Add([]);
        lignes[l].Insert(Math.Clamp(position ?? lignes[l].Count, 0, lignes[l].Count), etape);
        Renumeroter(lignes);
    }

    /// <summary>
    /// Déplace une étape (préparation). Même ligne : simple réordonnancement, liens inchangés. Autre ligne (<paramref name="ligne"/> égale
    /// au nombre de lignes = nouvelle ligne en bas) : liens par défaut si demandé, et ses anciens suivants restés sans préalable attendent
    /// la ligne au-dessus d'eux ; sinon seuls les liens devenus invalides sont retirés. Les lignes vides disparaissent.
    /// </summary>
    public static void Placer(Mep mep, Etape etape, int ligne, int position, bool liensParDefaut)
    {
        ExigerPreparation(mep);
        var lignes = Lignes(mep, etape.Plan);
        if (ligne < 0 || ligne > lignes.Count) throw new RegleMetierException($"Ligne inexistante : choisir une ligne entre 0 et {lignes.Count}.");
        var depart = lignes.FindIndex(l => l.Contains(etape));
        var memeLigne = ligne == depart;
        var anciensSuivants = mep.EtapesDu(etape.Plan).Where(e => e.Dependances.Contains(etape.Id)).ToList();
        var avant = Instantane(mep, etape.Plan);

        lignes[depart].Remove(etape);
        if (ligne == lignes.Count) lignes.Add([]);
        lignes[ligne].Insert(Math.Clamp(position, 0, lignes[ligne].Count), etape);
        Renumeroter(lignes);
        if (memeLigne) return;

        if (liensParDefaut)
        {
            Relier(mep, etape);
            RattacherOrphelins(mep, anciensSuivants);
        }
        NettoyerLiens(mep, etape.Plan);
        IncrementerRevisions(avant);
    }

    /// <summary>
    /// Supprime une étape (préparation) : elle est retirée des préalables des autres, ses anciens suivants restés sans préalable
    /// attendent la ligne au-dessus d'eux, et une ligne devenue vide disparaît.
    /// </summary>
    public static void RetirerEtape(Mep mep, Etape etape)
    {
        ExigerPreparation(mep);
        var avant = Instantane(mep, etape.Plan);
        var suivants = mep.Etapes.Where(e => e.Dependances.Contains(etape.Id)).ToList();
        foreach (var s in suivants) PoserDependances(s, s.Dependances.Where(d => d != etape.Id).ToList());
        mep.Etapes.Remove(etape);
        Renumeroter(Lignes(mep, etape.Plan));
        RattacherOrphelins(mep, suivants);
        IncrementerRevisions(avant, sauf: etape);
    }

    /// <summary>
    /// Lignes des étapes d'un plan créées avant le jalon 4 : profondeur dans le graphe des dépendances (0 sans préalable,
    /// sinon 1 + la plus grande ligne des préalables) ; dans une ligne, l'ancien ordre (puis le code) donne la position.
    /// </summary>
    public static void CalculerLignes(IReadOnlyList<Etape> etapesDuPlan)
    {
        var parId = etapesDuPlan.ToDictionary(e => e.Id);
        var profondeur = new Dictionary<Guid, int>();
        var enCours = new HashSet<Guid>();
        int Profondeur(Etape e)
        {
            if (profondeur.TryGetValue(e.Id, out var p)) return p;
            if (!enCours.Add(e.Id)) return 0; // cycle (données invalides) : on coupe
            var prealables = e.Dependances.Where(d => d != e.Id && parId.ContainsKey(d)).Select(d => Profondeur(parId[d])).ToList();
            enCours.Remove(e.Id);
            return profondeur[e.Id] = prealables.Count == 0 ? 0 : prealables.Max() + 1;
        }
        foreach (var e in etapesDuPlan) Profondeur(e);
        Renumeroter(etapesDuPlan.GroupBy(e => profondeur[e.Id]).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(e => e.Ordre).ThenBy(e => e.Code, StringComparer.Ordinal).ToList()));
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

    /// <summary>
    /// Applique de nouvelles dépendances après avoir vérifié qu'elles gardent un graphe valide : même plan,
    /// et chaque préalable sur une ligne strictement au-dessus (un lien descend toujours).
    /// </summary>
    public static void DefinirDependances(Mep mep, Etape etape, IEnumerable<Guid> dependances)
    {
        ExigerPreparation(mep);
        var nouvelles = dependances.Distinct().ToList();
        var autrePlan = mep.Etapes.FirstOrDefault(e => nouvelles.Contains(e.Id) && e.Plan != etape.Plan);
        if (autrePlan is not null) throw new RegleMetierException($"{autrePlan.Code} appartient à un autre plan.");
        var montant = mep.Etapes.FirstOrDefault(e => e != etape && nouvelles.Contains(e.Id) && e.Ligne >= etape.Ligne);
        if (montant is not null)
            throw new RegleMetierException($"Un lien descend toujours : {montant.Code} doit être sur une ligne au-dessus de {etape.Code}.");
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
