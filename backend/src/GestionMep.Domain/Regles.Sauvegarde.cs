namespace GestionMep.Domain;

// Règles métier : sauvegarde par système.
public static partial class Regles
{
    /// <summary>
    /// Sauvegarde d'un système de la MEP (préparation uniquement). Les textes sont rognés ; la justification d'une sauvegarde
    /// non requise, l'emplacement et la méthode de récupération d'une sauvegarde requise et l'étape de sauvegarde sont vérifiés
    /// par <see cref="ErreursSauvegarde"/>, pas ici.
    /// </summary>
    /// <param name="emplacement">Où se trouve la sauvegarde (null : inchangé).</param>
    /// <param name="methodeRecuperation">Comment la récupérer (null : inchangée).</param>
    public static void DefinirSauvegardeSysteme(Mep mep, Systeme systeme, StatutSauvegarde statut, ModeSauvegarde moment, string contenu, string justification,
        string? emplacement = null, string? methodeRecuperation = null)
    {
        ExigerPreparation(mep);
        if (!Enum.IsDefined(statut) || !Enum.IsDefined(moment)) throw new RegleMetierException("Sauvegarde : valeur inconnue.");
        var s = systeme.Sauvegarde;
        s.Statut = statut;
        s.Moment = moment;
        s.Contenu = (contenu ?? "").Trim();
        s.Justification = (justification ?? "").Trim();
        if (emplacement is not null) s.Emplacement = emplacement.Trim();
        if (methodeRecuperation is not null) s.MethodeRecuperation = methodeRecuperation.Trim();
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
