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
    Disponible,
}

public record Disponibilite(StatutDisponibilite Statut, string Libelle);

/// <summary>
/// Règles du plan de MEP, reprises des macros VBA du modèle Excel
/// (modules Collaboration et frmEtape : MEPDisponibilite, ErreurGraphe, ErreursPlan, ChangerEtat…).
/// </summary>
public static class Regles
{
    // ---------- Graphe de dépendances ----------

    /// <summary>Équivalent de ErreurGraphe : code manquant ou en double, préalable introuvable, auto-dépendance, cycle.</summary>
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

    // ---------- Disponibilité ----------

    /// <summary>Équivalent de DisponibiliteDonnees, évalué dans le même ordre que l'Excel.</summary>
    public static Disponibilite Disponibilite(Etape etape, IReadOnlyList<Etape> etapes, DateTimeOffset? echeance, DateTimeOffset maintenant)
    {
        var erreur = ErreurGraphe(etapes);
        if (erreur is not null) return new(StatutDisponibilite.PlanInvalide, erreur);
        if (echeance is null) return new(StatutDisponibilite.EcheanceARenseigner, "Échéance à renseigner");
        if (maintenant >= echeance) return new(StatutDisponibilite.RetourArriere, "RETOUR ARRIÈRE");
        if (string.IsNullOrWhiteSpace(etape.Action) || etape.SystemeId is null || etape.ResponsableId is null || etape.Criticite is null)
            return new(StatutDisponibilite.FicheACompleter, "Fiche à compléter");
        if (string.IsNullOrWhiteSpace(etape.Instructions) || string.IsNullOrWhiteSpace(etape.ResultatAttendu))
            return new(StatutDisponibilite.InstructionsACompleter, "Instructions à compléter");

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
            EtatEtape.Echec => new(StatutDisponibilite.ArretResolution, "Arrêt / résolution"),
            _ => new(StatutDisponibilite.Disponible, "Disponible"),
        };
    }

    // ---------- Vérification du plan ----------

    /// <summary>
    /// Équivalent de ErreursPlan, mais renvoie toutes les anomalies au lieu de la première.
    /// Ajouts par rapport à l'Excel : la procédure doit avoir été relue, et le pilote comme
    /// chaque responsable doivent avoir un compte pour agir dans l'application.
    /// </summary>
    public static List<string> ErreursPlan(Mep mep, DateTimeOffset maintenant)
    {
        var erreurs = new List<string>();
        var graphe = ErreurGraphe(mep.Etapes);
        if (graphe is not null) erreurs.Add(graphe);
        if (mep.EcheanceRepli is null || mep.EcheanceRepli <= maintenant)
            erreurs.Add("Renseigner une échéance de retour arrière future.");

        var pilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId);
        if (pilote is null) erreurs.Add("Renseigner le pilote.");
        else if (pilote.UtilisateurId is null) erreurs.Add($"Le pilote {pilote.Nom} n'est relié à aucun compte.");

        if (mep.Etapes.Count == 0) erreurs.Add("Ajouter au moins une étape.");
        foreach (var e in mep.Etapes.OrderBy(e => e.Ordre))
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

    // ---------- Droits ----------

    public static bool EstPilote(Mep mep, Guid utilisateurId) =>
        mep.Participants.Any(p => p.UtilisateurId == utilisateurId && (p.Id == mep.PiloteId || p.Id == mep.SuppleantId));

    public static bool EstResponsable(Mep mep, Etape etape, Guid utilisateurId) =>
        mep.Participants.Any(p => p.Id == etape.ResponsableId && p.UtilisateurId == utilisateurId);

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
        if (mep.Phase == Phase.Execution) throw new RegleMetierException("Le plan est déjà en exécution.");
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
        if (mep.Phase == Phase.Preparation) throw new RegleMetierException("Le plan est déjà en préparation.");
        var enCours = mep.Etapes.FirstOrDefault(e => e.Etat == EtatEtape.EnCours);
        if (enCours is not null) throw new RegleMetierException($"{enCours.Code} est encore en cours. Attendre sa fin.");
        mep.Phase = Phase.Preparation;
    }

    // ---------- Exécution d'une étape (ChangerEtat) ----------

    public static void Demarrer(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        ExigerExecutionParResponsable(mep, etape, utilisateurId);
        var dispo = Disponibilite(etape, mep.Etapes, mep.EcheanceRepli, maintenant);
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
        ExigerExecutionParResponsable(mep, etape, utilisateurId);
        if (etape.Etat != EtatEtape.EnCours) throw new RegleMetierException("Démarrer l'action avant de déclarer son résultat.");
        if (resultat == EtatEtape.Reussie && (mep.EcheanceRepli is null || maintenant >= mep.EcheanceRepli))
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
    /// Reprise d'une étape en échec, sur décision du pilote. L'Excel exige aussi une fiche de résolution
    /// au contrôle final Conforme : cette condition arrivera avec les fiches de résolution (jalon 2).
    /// </summary>
    public static void AutoriserReprise(Mep mep, Etape etape, Guid utilisateurId, DateTimeOffset maintenant)
    {
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("La reprise n'a de sens qu'en exécution.");
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou son suppléant peut autoriser une reprise.");
        if (etape.Etat != EtatEtape.Echec) throw new RegleMetierException("Seule une étape en échec peut être reprise.");
        if (mep.EcheanceRepli is null || maintenant >= mep.EcheanceRepli)
            throw new RegleMetierException("Échéance atteinte : retour arrière obligatoire, aucune reprise.");
        etape.Etat = EtatEtape.AFaire;
        etape.Revision++;
    }

    private static void ExigerExecutionParResponsable(Mep mep, Etape etape, Guid utilisateurId)
    {
        if (mep.Phase != Phase.Execution) throw new RegleMetierException("Le pilote doit passer le plan en exécution.");
        if (!EstResponsable(mep, etape, utilisateurId))
            throw new RegleMetierException("Seul le responsable affecté peut exécuter cette action.");
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

    /// <summary>Équivalent de InitialiserAction : prochain code E0001, E0002… sans jamais réutiliser un numéro.</summary>
    public static string ProchainCode(IEnumerable<Etape> etapes)
    {
        var max = 0;
        foreach (var e in etapes)
            if (e.Code.Length > 1 && int.TryParse(e.Code[1..], out var n) && n > max) max = n;
        return $"E{max + 1:0000}";
    }

    /// <summary>Applique de nouvelles dépendances après avoir vérifié qu'elles gardent un graphe valide.</summary>
    public static void DefinirDependances(Mep mep, Etape etape, IEnumerable<Guid> dependances)
    {
        ExigerPreparation(mep);
        var anciennes = etape.Dependances;
        etape.Dependances = dependances.Distinct().ToList();
        var erreur = ErreurGraphe(mep.Etapes);
        if (erreur is not null)
        {
            etape.Dependances = anciennes;
            throw new RegleMetierException(erreur);
        }
    }
}
