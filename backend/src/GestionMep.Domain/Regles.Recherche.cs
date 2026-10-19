namespace GestionMep.Domain;

/// <summary>
/// État d'une MEP pour la recherche et l'historique : sa phase tant qu'elle n'est pas clôturée, puis son issue.
/// <c>AvecIncidents</c> : clôturée réussie alors qu'au moins une étape a échoué en route (même reprise ensuite).
/// </summary>
public enum EtatMep { Preparation, Execution, Recette, RetourArriere, Reussie, AvecIncidents, Repliee }

/// <summary>Rôle d'une personne dans une MEP (une personne peut en cumuler plusieurs ; sans rôle, elle est simple participante).</summary>
public enum RoleMep { Pilote, Suppleant, ResponsableTechnique, Executant, Approbateur }

public static partial class Regles
{
    /// <summary>Type d'événement du journal qui marque l'échec d'une étape (« E0003 en échec : motif »).</summary>
    public const string EvenementEchecEtape = "etape.echec";

    /// <summary>Code d'étape d'un événement d'étape du journal : le premier mot du détail (« E0003 en échec : … » → « E0003 »).</summary>
    public static string CodeEvenementEtape(string detail) => detail.Split(' ', 2)[0];

    /// <summary>
    /// État d'une MEP : la phase, puis l'issue une fois clôturée. <paramref name="echecsSurvenus"/> : échecs d'étape tirés du
    /// journal (une étape reprise redevient réussie, son échec reste dans le journal).
    /// </summary>
    public static EtatMep EtatDe(Phase phase, IssueMep? issue, int echecsSurvenus) => phase switch
    {
        Phase.Preparation => EtatMep.Preparation,
        Phase.Execution => EtatMep.Execution,
        Phase.Recette => EtatMep.Recette,
        Phase.RetourArriere => EtatMep.RetourArriere,
        _ => issue == IssueMep.Repliee ? EtatMep.Repliee : echecsSurvenus > 0 ? EtatMep.AvecIncidents : EtatMep.Reussie,
    };

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
