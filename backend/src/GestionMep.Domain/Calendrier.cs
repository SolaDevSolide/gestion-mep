namespace GestionMep.Domain;

/// <summary>
/// Répétition d'une période de gel. Stockée en entier : toute nouvelle valeur s'ajoute à la fin.
/// <see cref="Mensuelle"/> garde le même jour du mois (31 → dernier jour des mois plus courts) ; <see cref="FinDeMois"/> garde
/// l'écart avec le dernier jour du mois (« les trois derniers jours » de chaque mois, clôture comptable).
/// </summary>
public enum RecurrenceGel { Aucune, Hebdomadaire, Mensuelle, FinDeMois, Annuelle }

/// <summary>
/// État d'une demande de dérogation à une période de gel. Stocké en texte dans le JSON de la MEP. <see cref="Caduque"/> : la
/// date de la MEP a changé après la demande ou l'accord ; la dérogation ne vaut plus et se redemande.
/// </summary>
public enum StatutDerogation { Demandee, Accordee, Refusee, Retiree, Caduque }

/// <summary>
/// Période de gel (#129) définie par un administrateur : aucune MEP n'y reçoit le Go sans dérogation accordée. Les bornes
/// sont des instants (enregistrés en UTC) ; une période récurrente se répète dans son fuseau horaire (<see cref="FuseauHoraire"/>,
/// celui de l'instance par défaut), à la même heure locale, changements d'heure compris.
/// </summary>
public class PeriodeGel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    /// <summary>Début de la (première) période, inclus.</summary>
    public DateTimeOffset Debut { get; set; }
    /// <summary>Fin de la (première) période, exclue.</summary>
    public DateTimeOffset Fin { get; set; }
    public string Motif { get; set; } = "";
    /// <summary>Systèmes du catalogue concernés ; vide : tous les systèmes (gel global).</summary>
    public List<Guid> SystemeCatalogueIds { get; set; } = [];
    /// <summary>Noms d'environnements concernés (sans casse ni accents) ; vide : tous.</summary>
    public List<string> Environnements { get; set; } = [];
    /// <summary>Seulement les MEP qui touchent un environnement marqué « production ».</summary>
    public bool ProductionSeulement { get; set; }
    public RecurrenceGel Recurrence { get; set; } = RecurrenceGel.Aucune;
    /// <summary>Période récurrente : dernier jour (local) où une occurrence peut commencer ; null : sans fin.</summary>
    public DateOnly? FinRecurrence { get; set; }
    /// <summary>Fuseau IANA dans lequel la période se répète et s'affiche.</summary>
    public string FuseauHoraire { get; set; } = Fuseaux.ParDefaut;
    /// <summary>
    /// Comptes qui peuvent accorder ou refuser une dérogation à cette période. Vide, comme <see cref="EquipeApprobatriceIds"/> :
    /// les administrateurs.
    /// </summary>
    public List<Guid> ApprobateurIds { get; set; } = [];
    /// <summary>Équipes dont chaque membre peut accorder ou refuser une dérogation à cette période.</summary>
    public List<Guid> EquipeApprobatriceIds { get; set; } = [];
    /// <summary>Compteur de révision : deux administrateurs qui modifient la même période, le second reçoit 409.</summary>
    public int Version { get; set; }
    public DateTimeOffset CreeeLe { get; set; }
    public DateTimeOffset ModifieeLe { get; set; }
}

/// <summary>Occurrence d'une période de gel : la période elle-même, ou l'une de ses répétitions. Fin exclue.</summary>
public sealed record OccurrenceGel(PeriodeGel Periode, DateTimeOffset Debut, DateTimeOffset Fin);

/// <summary>
/// Créneau d'une MEP dans le calendrier, recalculé à chaque enregistrement (<see cref="Regles.MettreAJourCalendrier"/>) pour
/// que le calendrier se lise par une requête indexée. Instants en UTC ; <see cref="Debut"/> null : MEP sans date (absente du
/// calendrier). Colonnes Calendrier_Debut, Calendrier_Fin…
/// </summary>
public class FenetreCalendrier
{
    public DateTimeOffset? Debut { get; set; }
    /// <summary>Fin exclue ; null : durée inconnue (instant, ou jusqu'à maintenant pour une MEP lancée).</summary>
    public DateTimeOffset? Fin { get; set; }
    /// <summary>Date prévue sans heure de début : la journée locale entière de la MEP (Debut et Fin à minuit dans son fuseau).</summary>
    public bool JourneeEntiere { get; set; }
    /// <summary>Version de la règle de calcul : une ligne plus ancienne est recalculée au démarrage.</summary>
    public int Format { get; set; }
}

/// <summary>
/// Demande de dérogation à une occurrence de période de gel, pour donner le Go pendant le gel : motif obligatoire, accord d'un
/// approbateur de la période (<see cref="PeriodeGel.ApprobateurIds"/>, les administrateurs par défaut) qui n'est ni pilote, ni
/// suppléant, ni responsable technique de la MEP (séparation des tâches), jamais le demandeur lui-même. Elle tombe si la date
/// de la MEP change ensuite (<see cref="StatutDerogation.Caduque"/>). Chaque étape est inscrite au journal de la MEP.
/// </summary>
public class DerogationGel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PeriodeGelId { get; set; }
    /// <summary>Nom de la période au moment de la demande (pour le journal, si elle est supprimée ensuite).</summary>
    public string NomPeriode { get; set; } = "";
    /// <summary>Début de l'occurrence visée : une dérogation ne vaut que pour cette occurrence d'une période récurrente.</summary>
    public DateTimeOffset Occurrence { get; set; }
    public DateTimeOffset OccurrenceFin { get; set; }
    public string Motif { get; set; } = "";
    public StatutDerogation Statut { get; set; } = StatutDerogation.Demandee;
    public Guid DemandeeParId { get; set; }
    public DateTimeOffset DemandeeLe { get; set; }
    public Guid? DecideeParId { get; set; }
    public DateTimeOffset? DecideeLe { get; set; }
    public string Commentaire { get; set; } = "";
    /// <summary>
    /// Créneau prévu de la MEP au moment de la demande (UTC, fin exclue) : s'il change ensuite, la demande ou l'accord tombe.
    /// Null : demande antérieure à cette règle.
    /// </summary>
    public DateTimeOffset? CreneauDebut { get; set; }
    public DateTimeOffset? CreneauFin { get; set; }
}

/// <summary>
/// Lien d'abonnement au calendrier d'un compte : le secret de l'adresse n'est jamais enregistré, seulement son empreinte
/// SHA-256. Régénérer le lien remplace l'empreinte (l'ancienne adresse cesse de répondre) ; le supprimer révoque l'abonnement.
/// </summary>
public class AbonnementCalendrier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UtilisateurId { get; set; }
    /// <summary>Empreinte SHA-256 (hexadécimal minuscule) du secret de l'adresse.</summary>
    public string Empreinte { get; set; } = "";
    public DateTimeOffset CreeLe { get; set; }
    public DateTimeOffset? DernierAcces { get; set; }
}

/// <summary>
/// Go refusé pendant une période de gel sans dérogation accordée (HTTP 422, problème de type
/// <see cref="Type"/>) : <see cref="Gels"/> donne les occurrences en cause.
/// </summary>
public class GoBloqueParGelException(string message, IReadOnlyList<OccurrenceGel> gels) : RegleMetierException(message)
{
    public const string Type = "urn:gestion-mep:probleme:go-pendant-un-gel";
    public IReadOnlyList<OccurrenceGel> Gels { get; } = gels;
}

/// <summary>MEP placée dans le calendrier, réduite à ce que lisent les conflits : créneau et systèmes (clés de <see cref="Regles.CleSysteme"/>).</summary>
public sealed record ElementCalendrier(Guid MepId, DateTimeOffset Debut, DateTimeOffset? Fin, bool EnCours, IReadOnlyCollection<string> Systemes);

/// <summary>Conflit d'une MEP avec une autre : même système (clés), créneaux qui se chevauchent.</summary>
public sealed record Conflit(Guid AutreMepId, IReadOnlyList<string> Systemes);
