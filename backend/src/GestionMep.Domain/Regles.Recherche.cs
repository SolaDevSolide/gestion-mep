namespace GestionMep.Domain;

/// <summary>
/// État d'une MEP pour la recherche et l'historique : sa phase tant qu'elle n'est pas clôturée, puis son issue.
/// <c>AvecIncidents</c> : clôturée réussie alors qu'au moins une étape a échoué en route (même reprise ensuite) ou qu'un test
/// de recette a été en échec (même refait ensuite).
/// <c>Reportee</c> : en préparation avec un report en cours ; <c>Echec</c> : clôturée en échec ; <c>Annulee</c> : annulée.
/// Toute nouvelle valeur s'ajoute à la fin.
/// </summary>
public enum EtatMep { Preparation, Execution, Recette, RetourArriere, Reussie, AvecIncidents, Repliee, Annulee, Reportee, Echec }

/// <summary>Rôle d'une personne dans une MEP (une personne peut en cumuler plusieurs ; sans rôle, elle est simple participante).</summary>
public enum RoleMep { Pilote, Suppleant, ResponsableTechnique, Executant, Approbateur }

public static partial class Regles
{
    /// <summary>Type d'événement du journal qui marque l'échec d'une étape (son étape : <see cref="EvenementJournal.EtapeId"/>).</summary>
    public const string EvenementEchecEtape = "etape.echec";

    /// <summary>
    /// État d'une MEP : la phase, puis l'issue une fois clôturée. <paramref name="incidents"/> : échecs survenus pendant la MEP,
    /// étapes et tests de recette (<see cref="Incidents"/>, compteurs tenus à jour quand l'échec survient) ; une clôture réussie
    /// avec au moins un incident est « réussie avec incidents ».
    /// </summary>
    /// <param name="reportee">MEP en préparation avec un report en cours (<see cref="Mep.Report"/>).</param>
    public static EtatMep EtatDe(Phase phase, IssueMep? issue, int incidents, bool reportee = false) => phase switch
    {
        Phase.Preparation => reportee ? EtatMep.Reportee : EtatMep.Preparation,
        Phase.Execution => EtatMep.Execution,
        Phase.Recette => EtatMep.Recette,
        Phase.RetourArriere => EtatMep.RetourArriere,
        Phase.Annulee => EtatMep.Annulee,
        _ => issue switch
        {
            IssueMep.Repliee => EtatMep.Repliee,
            IssueMep.Echec => EtatMep.Echec,
            _ => incidents > 0 ? EtatMep.AvecIncidents : EtatMep.Reussie,
        },
    };

    /// <summary>Incidents survenus : échecs d'étape et tests de recette en échec (<see cref="Mep.EchecsEtapes"/>, <see cref="Mep.EchecsRecette"/>).</summary>
    public static int Incidents(Mep mep) => mep.EchecsEtapes + mep.EchecsRecette;

    /// <summary>État d'une MEP, d'après sa phase, son issue et ses compteurs d'incidents.</summary>
    public static EtatMep EtatDeLaMep(Mep mep) => EtatDe(mep.Phase, mep.Issue, Incidents(mep), mep.Report is not null);

    /// <summary>
    /// Issue d'une MEP clôturée pour le journal : « réussie », « réussie avec incidents », « après retour arrière » ou
    /// « échec (service dégradé) ».
    /// </summary>
    public static string IssueEnClair(Mep mep) => EtatDe(mep.Phase, mep.Issue, Incidents(mep)) switch
    {
        EtatMep.AvecIncidents => "réussie avec incidents",
        EtatMep.Repliee => "après retour arrière",
        EtatMep.Echec => "échec (service dégradé)",
        _ => "réussie",
    };

    /// <summary>MEP terminée (clôturée ou annulée) : en lecture seule.</summary>
    public static bool EstTerminee(Mep mep) => mep.Phase is Phase.Cloturee or Phase.Annulee;

    /// <summary>
    /// Rôles de chaque participant de la MEP : pilote, suppléants (implicites pour une MEP simplifiée), responsables
    /// techniques, exécutants (ont démarré ou repris au moins une étape). Les approbateurs sont des comptes, pas des
    /// participants : voir <see cref="Mep.Approbations"/>.
    /// </summary>
    public static Dictionary<Guid, HashSet<RoleMep>> RolesDesParticipants(Mep mep)
    {
        var roles = mep.Participants.ToDictionary(p => p.Id, _ => new HashSet<RoleMep>());
        void Ajouter(Guid? id, RoleMep r)
        {
            if (id is { } i && roles.TryGetValue(i, out var s)) s.Add(r);
        }
        Ajouter(mep.PiloteId, RoleMep.Pilote);
        foreach (var s in Suppleants(mep)) Ajouter(s, RoleMep.Suppleant);
        foreach (var rt in mep.ResponsablesTechniques) Ajouter(rt.ParticipantId, RoleMep.ResponsableTechnique);
        foreach (var e in mep.Etapes) Ajouter(e.ExecutantId, RoleMep.Executant);
        return roles;
    }
}
