namespace GestionMep.Domain;

// Règles métier : sauvegarde par système.
public static partial class Regles
{
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
}
