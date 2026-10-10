namespace GestionMep.Domain;

// Règles métier : droits.
public static partial class Regles
{
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

    /// <summary>
    /// Participant qui exécute l'étape : celui qui l'a démarrée ou qui a pris la main (null avant le démarrage).
    /// Depuis le jalon 4.2, il n'y a plus de responsable prévu : <see cref="Etape.ResponsableId"/> est sans effet.
    /// </summary>
    public static Guid? ExecutantCourant(Etape etape) => etape.ExecutantId;

    public static bool EstExecutant(Mep mep, Etape etape, Guid utilisateurId)
    {
        var executant = ExecutantCourant(etape);
        return executant is not null && mep.Participants.Any(p => p.Id == executant && p.UtilisateurId == utilisateurId);
    }

    /// <summary>Ajout, suppression et modification du plan réservés à la préparation (StructureAutorisee).</summary>
    public static void ExigerPreparation(Mep mep)
    {
        if (mep.Phase != Phase.Preparation)
            throw new RegleMetierException("Modification réservée à la préparation. Contacter le pilote ; ne pas changer la structure pendant l'exécution.");
    }
}
