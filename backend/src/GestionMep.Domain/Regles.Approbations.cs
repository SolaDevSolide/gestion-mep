namespace GestionMep.Domain;

/// <summary>
/// Ce que la vérification du plan lit hors de la MEP : le seuil d'approbations (paramètre global), l'annuaire des personnes
/// (comptes reliés, pour la séparation des tâches) et les résolutions types liées aux étapes (actives et complètes). Sans
/// annuaire, un compte n'est comparé qu'à lui-même ; sans résolutions, elles ne sont pas vérifiées.
/// </summary>
public sealed record ContexteVerification(int ApprobationsMinimum, AnnuairePersonnes? Annuaire = null,
    IReadOnlyCollection<ResolutionType>? Resolutions = null);

// Règles métier : circuit d'approbation et séparation des tâches.
public static partial class Regles
{
    /// <summary>
    /// Toute modification du contenu en préparation remet les décisions à null et efface les résolutions figées (elles le
    /// seront de nouveau à la prochaine approbation). Renvoie vrai si au moins une décision a été effacée (à journaliser).
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
        if (mep.ResolutionsFigeesLe is not null || mep.ResolutionsFigees.Count > 0)
        {
            mep.ResolutionsFigees = [];
            mep.ResolutionsFigeesLe = null;
        }
        return effacee;
    }

    /// <summary>
    /// Toute modification du plan en préparation efface aussi la relecture de la procédure : l'opérateur relit le plan modifié.
    /// Renvoie vrai si une relecture a été effacée (à journaliser).
    /// </summary>
    public static bool ReinitialiserRelecture(Mep mep)
    {
        if (mep.Phase != Phase.Preparation || mep.ProcedureRelueParId is null) return false;
        mep.ProcedureRelueParId = null;
        mep.ProcedureRelueLe = null;
        return true;
    }

    /// <summary>
    /// Séparation des tâches : rôle qui interdit à ce compte d'approuver la MEP (« pilote », « suppléant du pilote »,
    /// « responsable technique », suivi de « , par un compte relié » si c'est un autre compte de la même personne), ou null.
    /// Le pilote, ses suppléants (implicites pour une MEP simplifiée) et les responsables techniques n'approuvent pas leur
    /// propre plan : sinon une seule personne pourrait écrire, approuver et lancer la MEP.
    /// </summary>
    public static string? EmpechementApprobation(Mep mep, Guid compteId, AnnuairePersonnes? annuaire = null)
    {
        Guid Personne(Guid c) => annuaire?.Personne(c) ?? c;
        var personne = Personne(compteId);
        string? Role(Guid? participantId, string role)
        {
            if (mep.Participants.FirstOrDefault(p => p.Id == participantId)?.UtilisateurId is not { } compte || Personne(compte) != personne) return null;
            return compte == compteId ? role : $"{role}, par un compte relié";
        }
        return Role(mep.PiloteId, "pilote")
            ?? Suppleants(mep).Select(s => Role(s, "suppléant du pilote")).FirstOrDefault(r => r is not null)
            ?? mep.ResponsablesTechniques.Select(r => Role(r.ParticipantId, "responsable technique")).FirstOrDefault(r => r is not null);
    }

    /// <summary>« Bob ne peut pas approuver cette MEP (suppléant du pilote). »</summary>
    public static string MessageEmpechement(string nom, string role) => $"{nom} ne peut pas approuver cette MEP ({role}).";

    /// <summary>
    /// Remplace la liste des approbateurs (comptes) ; ceux déjà désignés gardent leur entrée. Refusé pour un compte du pilote,
    /// d'un suppléant ou d'un responsable technique (comptes reliés compris si <paramref name="annuaire"/> est donné).
    /// </summary>
    public static void DefinirApprobateurs(Mep mep, IReadOnlyList<(Guid UtilisateurId, string Nom)> approbateurs, AnnuairePersonnes? annuaire = null)
    {
        ExigerPreparation(mep);
        foreach (var a in approbateurs)
            if (EmpechementApprobation(mep, a.UtilisateurId, annuaire) is { } role)
                throw new RegleMetierException(MessageEmpechement(a.Nom, role)
                    + " Choisir un approbateur qui n'est ni pilote, ni suppléant, ni responsable technique de la MEP.");
        mep.Approbations = approbateurs.DistinctBy(a => a.UtilisateurId).Select(a =>
            mep.Approbations.FirstOrDefault(x => x.UtilisateurId == a.UtilisateurId) ?? new Approbation { UtilisateurId = a.UtilisateurId, Nom = a.Nom }).ToList();
    }

    /// <summary>
    /// Décision d'un approbateur désigné, en préparation, quand il ne reste que des anomalies d'approbation.
    /// Un refus exige un commentaire. Refusée au pilote, à un suppléant ou à un responsable technique (séparation des tâches).
    /// Une approbation fige les résolutions types liées si elles ne le sont pas encore (<paramref name="resolutions"/> donné).
    /// </summary>
    public static Approbation Decider(Mep mep, Guid utilisateurId, DecisionApprobation decision, string commentaire, DateTimeOffset maintenant,
        AnnuairePersonnes? annuaire = null, IReadOnlyCollection<ResolutionType>? resolutions = null)
    {
        var approbation = mep.Approbations.FirstOrDefault(a => a.UtilisateurId == utilisateurId)
            ?? throw new DroitInsuffisantException("Seul un approbateur désigné peut approuver ou refuser cette MEP.");
        if (EmpechementApprobation(mep, utilisateurId, annuaire) is { } role)
            throw new DroitInsuffisantException($"Vous ne pouvez pas approuver cette MEP ({role}) : demander au pilote de désigner un autre approbateur.");
        if (mep.Phase != Phase.Preparation) throw new RegleMetierException("L'approbation se fait en préparation.");
        var erreurs = ErreursContenu(mep, maintenant, resolutions);
        if (erreurs.Count > 0) throw new RegleMetierException("Plan incomplet, approbation impossible : " + string.Join(" ", erreurs));
        if (decision == DecisionApprobation.Refusee && string.IsNullOrWhiteSpace(commentaire))
            throw new RegleMetierException("Expliquer le refus dans le commentaire.");
        approbation.Decision = decision;
        approbation.Commentaire = commentaire.Trim();
        approbation.Le = maintenant;
        if (decision == DecisionApprobation.Approuvee && resolutions is not null) FigerResolutions(mep, resolutions, maintenant);
        return approbation;
    }

    // ---------- Résolutions types liées : copie figée ----------

    /// <summary>Résolutions types liées aux étapes de déploiement d'une MEP complète (une MEP simplifiée n'en lie aucune).</summary>
    public static List<Guid> ResolutionsLiees(Mep mep) => EstSimplifiee(mep)
        ? []
        : mep.EtapesDu(TypePlan.Deploiement).SelectMany(e => e.ResolutionIds).Distinct().ToList();

    /// <summary>
    /// Fige une copie des résolutions liées (titre, contenu, version), si ce n'est pas déjà fait. Renvoie vrai si elles viennent
    /// d'être figées (à journaliser : <see cref="DetailResolutionsFigees"/>).
    /// </summary>
    public static bool FigerResolutions(Mep mep, IReadOnlyCollection<ResolutionType> bibliotheque, DateTimeOffset maintenant)
    {
        if (mep.ResolutionsFigeesLe is not null || mep.EstModele) return false;
        var ids = ResolutionsLiees(mep);
        mep.ResolutionsFigees = ids.Select(id => bibliotheque.FirstOrDefault(r => r.Id == id)).OfType<ResolutionType>().Select(ResolutionFigee.De).ToList();
        mep.ResolutionsFigeesLe = maintenant;
        return true;
    }

    /// <summary>« Résolutions types figées pour le déroulé : « API : réponse absente » v3, … » (null s'il n'y en a aucune).</summary>
    public static string? DetailResolutionsFigees(Mep mep) => mep.ResolutionsFigees.Count == 0
        ? null
        : "Résolutions types figées pour le déroulé : " + string.Join(", ", mep.ResolutionsFigees.Select(r => $"« {r.Titre} » v{r.Version}"));

    /// <summary>
    /// Résolution à appliquer pour une étape : la copie figée de la MEP si elle existe, sinon la résolution de la bibliothèque
    /// (MEP simplifiée, ou MEP lancée avant que les résolutions soient figées).
    /// </summary>
    public static ResolutionType ResolutionAAppliquer(Mep mep, ResolutionType bibliotheque) =>
        mep.ResolutionsFigees.FirstOrDefault(f => f.ResolutionTypeId == bibliotheque.Id)?.VersResolutionType(bibliotheque.Actif) ?? bibliotheque;

    /// <summary>
    /// Résolutions liées (MEP complète) : chacune doit exister et être active dans la bibliothèque, et sa copie figée (à défaut,
    /// la version de la bibliothèque) doit être complète. Sinon, le jour J, l'opérateur découvrirait le problème en plein échec.
    /// </summary>
    public static List<string> ErreursResolutionsLiees(Mep mep, IReadOnlyCollection<ResolutionType> bibliotheque)
    {
        var erreurs = new List<string>();
        if (EstSimplifiee(mep)) return erreurs;
        foreach (var e in mep.EtapesDu(TypePlan.Deploiement))
            foreach (var id in e.ResolutionIds.Distinct())
            {
                var type = bibliotheque.FirstOrDefault(r => r.Id == id);
                if (type is null)
                {
                    erreurs.Add($"{e.Code} : une résolution type liée n'existe plus dans la bibliothèque, la délier.");
                    continue;
                }
                var appliquee = ResolutionAAppliquer(mep, type);
                if (!type.Actif)
                    erreurs.Add($"{e.Code} : la résolution « {appliquee.Titre} » est désactivée, la délier ou la faire réactiver.");
                else if (ErreursResolutionType(appliquee) is { Count: > 0 } manques)
                    erreurs.Add($"{e.Code} : la résolution « {appliquee.Titre} » est incomplète ({string.Join(" ", manques)}).");
            }
        return erreurs;
    }
}
