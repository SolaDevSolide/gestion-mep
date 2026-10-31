using GestionMep.Domain;

namespace GestionMep.Api.Services;

// ---------- Liaison avec Noosphere (#123), console d'administration ----------

/// <summary>
/// Liaison avec une instance de Noosphere. <c>ConfirmeeParNoosphere</c> : confirmation de l'empreinte côté Noosphere d'après
/// son dernier <c>/status</c> (null : pas encore connue) ; les données ne s'échangent qu'une fois confirmée des deux côtés.
/// <c>ARapprocher</c> : nouvelles applications des domaines suivis mises de côté parce qu'un système local porte un nom proche
/// (ni créées ni rattachées tant que l'administrateur n'a pas choisi). <c>Demo</c> : liaison fictive de la démonstration (aucun appel ne part).
/// </summary>
public record LiaisonDto(Guid Id, string Produit, string NomInstance, string Url, string VersionNoosphere, EtatLiaison Etat, bool ConfirmeeIci,
    DateTimeOffset? ConfirmeeLe, string ConfirmeePar, bool? ConfirmeeParNoosphere, List<string> Capacites, string Empreinte, DateTimeOffset AppaireeLe,
    string AppaireePar, DateTimeOffset? ClesRenouveleesLe, DateTimeOffset? DernierEchangeLe, DateTimeOffset? CoupeeLe, string CoupeePar,
    bool EnvoyerMeps, bool SuiviAutomatique, List<string> DomainesSuivis, DateTimeOffset? DerniereSynchro, string DerniereErreur, int SystemesSuivis,
    int ARapprocher, bool Demo);

/// <summary>Appairage : adresse de Noosphere et code à usage unique créé par un administrateur de Noosphere.</summary>
public record AppairageLiaisonSaisie(string? Url, string? Code);

/// <summary>Empreinte en quatre mots lue sur l'écran de Noosphere (casse et espaces indifférents).</summary>
public record ConfirmationLiaisonSaisie(string? Empreinte);

/// <summary>Interrupteurs « envoyer les MEP à Noosphere » et « suivre automatiquement les nouvelles applications des domaines suivis ».</summary>
public record OptionsLiaisonSaisie(bool? EnvoyerMeps, bool? SuiviAutomatique);

/// <summary>
/// Applications suivies. <c>DomainesSuivis</c> (facultatif) remplace la liste des domaines suivis : un domaine ajouté suit
/// toutes ses applications, un domaine retiré cesse de les suivre. <c>Applications</c> : choix une par une, appliqués ensuite.
/// </summary>
public record SuiviLiaisonSaisie(List<string>? DomainesSuivis, List<SuiviApplicationSaisie>? Applications);

/// <summary>
/// <c>Suivre</c> false : l'application n'est plus suivie, son système redevient local. true : suivie, rattachée au système
/// <c>SystemeId</c> (local) s'il est donné, sinon à un système local du même nom, sinon un nouveau système est créé.
/// <c>Creer</c> true (sans <c>SystemeId</c>) : un nouveau système est créé même si un système local porte déjà ce nom.
/// </summary>
public record SuiviApplicationSaisie(string? ApplicationId, bool? Suivre, Guid? SystemeId, bool? Creer = null);

public record CoupureLiaisonSaisie(bool? Confirmer);

public record DomaineLiaisonDto(string Id, string Nom, bool Suivi, int NbApplications);

/// <summary>Système local proposé pour rattacher une application (nom identique ou proche).</summary>
public record SuggestionSystemeDto(Guid Id, string Nom, bool MemeNom);

/// <summary>Application du dernier catalogue lu : suivie ou non, système rattaché, systèmes proposés pour la rattacher.</summary>
public record ApplicationLiaisonDto(string Id, string Nom, string Description, string? DomaineId, string Domaine, string Equipe, string Type, string Cycle,
    string Url, bool Suivie, bool ARapprocher, Guid? SystemeId, string? Systeme, List<SuggestionSystemeDto> Suggestions);

/// <summary>Dernier catalogue de Noosphere lu (null : pas encore lu) et choix de suivi.</summary>
public record CatalogueLiaisonDto(DateTimeOffset? LuLe, string NomInstance, List<DomaineLiaisonDto> Domaines, List<ApplicationLiaisonDto> Applications);

/// <summary>Échange du journal (90 jours) : sens « entrant » ou « sortant », résultat « ok », « refuse », « echec » ou « demo ».</summary>
public record EchangeLiaisonDto(Guid Id, Guid? LiaisonId, DateTimeOffset Quand, string Sens, string Operation, string Resultat, int? Code, string Detail);

/// <summary>Résultat d'une synchronisation : <c>Inchange</c> quand Noosphere répond 304 (catalogue identique au dernier lu).</summary>
public record SynchroLiaisonDto(bool Inchange, string Resume, List<string> Crees, List<string> Rattaches, List<string> MisAJour, List<string> Desactives,
    List<string> Reactives, List<string> Avertissements, LiaisonDto Liaison);

/// <summary>Système suivi depuis Noosphere (« Géré par Noosphere ») : nom et description en lecture seule ; <c>Url</c> : « Ouvrir dans Noosphere ».</summary>
public record SourceNoosphereDto(Guid LiaisonId, string ApplicationId, string Url, string Domaine, string Equipe, string Cycle,
    DateTimeOffset? DerniereSynchro, bool Absente);

/// <summary>Application de Noosphere touchée par une MEP, par un système suivi : « Voir dans Noosphere » (<c>Url</c>).</summary>
public record ApplicationMepNoosphereDto(Guid SystemeId, string Systeme, string ApplicationId, string Url, string Domaine, string Equipe);

/// <summary>
/// MEP envoyée à Noosphere (« Envoyée à Noosphere »), pour une liaison. <c>EnvoyeeLe</c> : dernier envoi (null : pas encore
/// envoyée) ; <c>Phase</c> : phase du contrat de ce dernier envoi. <c>EnvoiActif</c> : la liaison envoie les MEP (option et
/// confirmation des deux côtés). <c>DatePrevue</c> : la MEP a une date ; sans date, elle n'est pas encore annoncée.
/// </summary>
public record EnvoiMepNoosphereDto(Guid LiaisonId, string NomInstance, bool EnvoiActif, bool Demo, DateTimeOffset? EnvoyeeLe, string Phase,
    bool DatePrevue, List<ApplicationMepNoosphereDto> Applications);
