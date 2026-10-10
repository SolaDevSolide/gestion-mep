namespace GestionMep.Domain;

// Règles métier : circuit d'approbation.
public static partial class Regles
{
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
}
