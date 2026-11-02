using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GestionMep.Api.Services;

/// <summary>
/// Période de gel (#129). <c>Debut</c> et <c>Fin</c> (exclue) : première occurrence, en UTC. <c>SystemeCatalogueIds</c> vide :
/// tous les systèmes ; <c>Environnements</c> vide : tous. <c>Recurrence</c> : répétition dans <c>FuseauHoraire</c>, jusqu'au
/// jour local <c>FinRecurrence</c> (null : sans fin). <c>Systemes</c> : noms des systèmes concernés, pour l'affichage.
/// <c>ApprobateurIds</c>, <c>EquipeApprobatriceIds</c> : comptes et équipes qui décident des dérogations (vides : les
/// administrateurs) ; <c>Decideurs</c> : qui décide, en clair (« Claire Durand, équipe Comptabilité », « les administrateurs »).
/// </summary>
public record PeriodeGelDto(Guid Id, string Nom, DateTimeOffset Debut, DateTimeOffset Fin, string Motif, List<Guid> SystemeCatalogueIds,
    List<string> Systemes, List<string> Environnements, bool ProductionSeulement, RecurrenceGel Recurrence, DateOnly? FinRecurrence,
    string FuseauHoraire, List<Guid> ApprobateurIds, List<Guid> EquipeApprobatriceIds, string Decideurs, int Version,
    DateTimeOffset CreeeLe, DateTimeOffset ModifieeLe);

/// <summary>
/// Création ou modification d'une période de gel (administrateurs). Instants ISO 8601 avec leur décalage (enregistrés en UTC).
/// Absents : <c>SystemeCatalogueIds</c> et <c>Environnements</c> vides (tous), <c>Recurrence</c> Aucune, <c>FuseauHoraire</c>
/// celui de l'instance, <c>ApprobateurIds</c> et <c>EquipeApprobatriceIds</c> vides (les administrateurs décident).
/// <c>Version</c> : obligatoire en modification (409 si la période a changé depuis sa lecture).
/// </summary>
public record PeriodeGelSaisie(string? Nom, DateTimeOffset? Debut, DateTimeOffset? Fin, string? Motif, List<Guid>? SystemeCatalogueIds,
    List<string>? Environnements, bool? ProductionSeulement, RecurrenceGel? Recurrence, DateOnly? FinRecurrence, string? FuseauHoraire,
    int? Version = null, List<Guid>? ApprobateurIds = null, List<Guid>? EquipeApprobatriceIds = null);

/// <summary>
/// Occurrence d'une période de gel (fin exclue, UTC). <c>Derogation</c> : état de la dernière demande de dérogation de la MEP
/// pour cette occurrence (null : aucune, ou hors d'une MEP). <c>Decideurs</c> : qui peut accorder une dérogation, en clair
/// (détail d'une MEP seulement ; null ailleurs).
/// </summary>
public record OccurrenceGelDto(Guid PeriodeGelId, string Nom, string Motif, DateTimeOffset Debut, DateTimeOffset Fin, string FuseauHoraire,
    bool Globale, StatutDerogation? Derogation = null, string? Decideurs = null);

/// <summary>Autre MEP sur un même système dont le créneau chevauche celui de la MEP (signalement, jamais bloquant).</summary>
public record ConflitDto(Guid MepId, string Outil, string VersionCible, EtatMep Etat, DateTimeOffset Debut, DateTimeOffset? Fin, List<string> Systemes);

/// <summary>
/// Demande de dérogation au gel. <c>DemandeePar</c>, <c>DecideePar</c> : noms affichés. <c>PeutDecider</c> : la personne qui
/// lit peut l'accorder ou la refuser (approbateur de la période, ni pilote, ni suppléant, ni responsable technique, ni demandeur).
/// <c>Statut</c> Caduque : la date de la MEP a changé depuis la demande ; elle se redemande.
/// </summary>
public record DerogationGelDto(Guid Id, Guid PeriodeGelId, string NomPeriode, DateTimeOffset Occurrence, DateTimeOffset OccurrenceFin,
    string Motif, StatutDerogation Statut, string? DemandeePar, DateTimeOffset DemandeeLe, string? DecideePar, DateTimeOffset? DecideeLe,
    string Commentaire, bool PeutDecider);

/// <summary>
/// Signalements du calendrier d'une MEP de la liste (cartes et tableau) : <c>Gels</c>, occurrences de gel qui touchent son
/// créneau (avec l'état de la dérogation) ; <c>Conflits</c>, autres MEP sur un même système au même moment. Seules les MEP
/// qui ont au moins un signalement figurent dans <see cref="PageMepsDto.Signalements"/>.
/// </summary>
public record SignalementCalendrierDto(Guid MepId, List<OccurrenceGelDto> Gels, List<ConflitDto> Conflits);

/// <summary>
/// Dérogation au gel qui attend la décision de la personne qui lit (« Suivi en direct ») : la MEP, son créneau prévu
/// (<c>Debut</c> null : sans date) et ses systèmes, et la demande elle-même (<c>PeutDecider</c> toujours vrai ici).
/// </summary>
public record DerogationEnAttenteDto(Guid MepId, string Outil, string VersionCible, TypeMep Type, DateTimeOffset? Debut, DateTimeOffset? Fin,
    bool JourneeEntiere, string FuseauHoraire, string? Pilote, List<SystemeResumeDto> Systemes, int NbEtapes, bool RetourArriereActive,
    DerogationGelDto Derogation);

/// <summary>
/// Place de la MEP dans le calendrier (détail d'une MEP). <c>Debut</c> null : MEP sans date prévue (absente du calendrier).
/// <c>Prevu</c> : créneau prévu (pas encore lancée) ; <c>EnCours</c> : lancée, elle s'étend au moins jusqu'à maintenant.
/// <c>Gels</c> : occurrences de gel qui touchent le créneau ; <c>GelsAuGo</c> : celles qu'un Go donné maintenant toucherait sans
/// dérogation accordée (non vide : le Go serait refusé) ; <c>Conflits</c> : MEP sur un même système au même moment.
/// </summary>
public record CalendrierMepDto(DateTimeOffset? Debut, DateTimeOffset? Fin, bool JourneeEntiere, bool Prevu, bool EnCours,
    List<OccurrenceGelDto> Gels, List<OccurrenceGelDto> GelsAuGo, List<ConflitDto> Conflits, List<DerogationGelDto> Derogations);

/// <summary>
/// MEP du calendrier. <c>Debut</c>, <c>Fin</c> (exclue, null : durée inconnue) en UTC ; <c>JourneeEntiere</c> : date prévue sans
/// heure (minuit à minuit dans <c>FuseauHoraire</c>). Couleurs : <c>Phase</c>, <c>Etat</c> et <c>ToucheProduction</c>.
/// </summary>
public record ElementCalendrierDto(Guid Id, string Outil, string VersionCible, string Objet, TypeMep Type, Phase Phase, EtatMep Etat,
    DateTimeOffset Debut, DateTimeOffset? Fin, bool JourneeEntiere, bool Prevu, bool EnCours, string FuseauHoraire, string? Pilote,
    bool ToucheProduction, List<SystemeResumeDto> Systemes, List<Guid> EquipeIds, List<OccurrenceGelDto> Gels, List<ConflitDto> Conflits);

/// <summary>
/// Page du calendrier (<c>GET /api/calendrier</c>) : MEP dont le créneau touche la plage [<c>Du</c>, <c>Au</c>] (jours inclus,
/// dans <c>FuseauHoraire</c>), par début puis identifiant ; <c>Gels</c> : occurrences des périodes de gel de la plage ;
/// <c>Total</c> : MEP qui correspondent ; <c>Suivant</c> : curseur de la page suivante (null : dernière page).
/// </summary>
public record PageCalendrierDto(List<ElementCalendrierDto> Meps, List<OccurrenceGelDto> Gels, int Total, string? Suivant, DateOnly Du, DateOnly Au,
    string FuseauHoraire);

/// <summary>
/// Abonnement au calendrier du compte courant. <c>Url</c> : adresse secrète, donnée une seule fois, à sa création ou sa
/// régénération (seule son empreinte est gardée) ; null ensuite.
/// </summary>
public record AbonnementCalendrierDto(bool Actif, DateTimeOffset? CreeLe, DateTimeOffset? DernierAcces, string? Url);

/// <summary>
/// Demande de dérogation : <c>PeriodeGelId</c> et, pour une période répétée, <c>Occurrence</c> (début de l'occurrence visée ;
/// absent : la première qui concerne la MEP) ; <c>Motif</c> obligatoire.
/// </summary>
public record DerogationGelSaisie(Guid PeriodeGelId, DateTimeOffset? Occurrence, string? Motif);

/// <summary>Décision d'un approbateur sur une dérogation : accordée ou refusée (commentaire obligatoire pour un refus).</summary>
public record DecisionDerogationSaisie(bool Accorder, string? Commentaire);

/// <summary>
/// Paramètres du calendrier et de l'export iCalendar. <c>du</c>, <c>au</c> : jours inclus (AAAA-MM-JJ) dans <c>fuseau</c>
/// (celui de l'instance par défaut), 366 jours au plus ; autres filtres : ceux de la liste des MEP (<see cref="FiltreMeps"/>).
/// <c>gels</c> : 0 pour ne pas inclure les périodes de gel dans l'export.
/// </summary>
public record ParametresCalendrier(
    [property: FromQuery(Name = "du")] string? Du = null,
    [property: FromQuery(Name = "au")] string? Au = null,
    [property: FromQuery(Name = "fuseau")] string? Fuseau = null,
    [property: FromQuery(Name = "recherche")] string? Recherche = null,
    [property: FromQuery(Name = "systeme")] string? Systeme = null,
    [property: FromQuery(Name = "environnement")] string? Environnement = null,
    [property: FromQuery(Name = "equipe")] string? Equipe = null,
    [property: FromQuery(Name = "personne")] string? Personne = null,
    [property: FromQuery(Name = "role")] string? Role = null,
    [property: FromQuery(Name = "etat")] string? Etat = null,
    [property: FromQuery(Name = "type")] string? Type = null,
    [property: FromQuery(Name = "gels")] string? Gels = null);
