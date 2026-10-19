using System.Globalization;

namespace GestionMep.Domain;

// Règles métier : échéance de retour arrière (saisie, motif, journal dans le fuseau de la MEP).
public static partial class Regles
{
    /// <summary>
    /// Modifie l'échéance de retour arrière (null : la retirer). Possible en préparation, en exécution et en recette, sur une MEP
    /// avec plan de retour arrière ; refusé sur un modèle, une fois le retour arrière déclenché et sur une MEP terminée. Une
    /// nouvelle échéance doit être future. Le motif est obligatoire, sauf pour la première saisie en préparation. Un nouveau
    /// signalement (recette) aura lieu si la nouvelle échéance est à son tour dépassée. Renvoie l'ancienne valeur (à journaliser
    /// avec <see cref="DetailEcheance"/> si elle a changé). Les droits (pilote, suppléant, administrateur) sont vérifiés par l'API.
    /// </summary>
    public static DateTimeOffset? ModifierEcheance(Mep mep, DateTimeOffset? nouvelle, string motif, DateTimeOffset maintenant)
    {
        if (mep.EstModele) throw new RegleMetierException("Un modèle n'a pas d'échéance de retour arrière : elle se choisit dans chaque MEP.");
        switch (mep.Phase)
        {
            case Phase.Cloturee or Phase.Annulee:
                throw new RegleMetierException("La MEP est terminée (clôturée ou annulée) : l'échéance de retour arrière ne se modifie plus.");
            case Phase.RetourArriere:
                throw new RegleMetierException("Le retour arrière est déclenché : l'échéance ne sert plus et ne se modifie plus.");
        }
        if (!mep.RetourArriereActive)
            throw new RegleMetierException("Cette MEP n'a pas de plan de retour arrière : elle n'a pas d'échéance de retour arrière.");
        var ancienne = mep.EcheanceRepli;
        if (ancienne == nouvelle) return ancienne;
        if (nouvelle is { } n && n <= maintenant) throw new RegleMetierException("L'échéance de retour arrière doit être dans le futur.");
        if (string.IsNullOrWhiteSpace(motif) && !(mep.Phase == Phase.Preparation && ancienne is null))
            throw new RegleMetierException("Indiquer le motif du changement d'échéance : il est tracé au journal avec l'ancienne et la nouvelle valeur.");
        mep.EcheanceRepli = nouvelle;
        mep.EcheanceSignaleeLe = null;
        return ancienne;
    }

    /// <summary>
    /// « Échéance de retour arrière : 23/10/2026 23:00 → 24/10/2026 02:00 (Europe/Paris) : motif », heures dans le fuseau de la
    /// MEP (<see cref="Fuseaux"/> : identifiant Windows converti, fuseau inconnu remplacé par le fuseau par défaut) ; « aucune »
    /// pour une échéance absente.
    /// </summary>
    public static string DetailEcheance(DateTimeOffset? ancienne, DateTimeOffset? nouvelle, string? fuseau, string motif)
    {
        var nom = Fuseaux.Normaliser(fuseau) ?? Fuseaux.ParDefaut;
        string Texte(DateTimeOffset? d) => d is { } v
            ? Fuseaux.Local(v, nom).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : "aucune";
        var suite = string.IsNullOrWhiteSpace(motif) ? "" : $" : {motif.Trim()}";
        return $"Échéance de retour arrière : {Texte(ancienne)} → {Texte(nouvelle)} ({nom}){suite}";
    }

    /// <summary>Détail du journal quand l'échéance est atteinte pendant la recette (<see cref="SignalerEcheance"/>).</summary>
    public const string DetailEcheanceSignalee =
        "Échéance de retour arrière atteinte pendant la recette : aucun retour arrière automatique, le pilote décide "
        + "(déclencher le retour arrière, ou reporter l'échéance avec un motif).";
}
