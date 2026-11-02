using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>
/// Jeton d'accès à l'API, sans son secret. <c>Prefixe</c> : début visible du jeton (<c>gmep_</c> et 8 caractères) ;
/// <c>Etat</c> : <c>Actif</c>, <c>Expire</c> ou <c>Revoque</c> ; <c>DerniereUtilisationLe</c> et <c>DerniereAdresseIp</c> :
/// dernier appel, à 5 minutes près ; <c>CompteId</c>, <c>CompteLogin</c>, <c>CompteNom</c> : compte au nom duquel il agit
/// (la personne, ou le compte technique d'un jeton de service) ; <c>RequetesParMinute</c> : limite de débit propre à un jeton
/// de service (null : réglage global <c>jetons-par-minute</c>).
/// </summary>
public record JetonDto(Guid Id, string Nom, TypeJeton Type, EtatJeton Etat, string Prefixe, List<string> Portees, DateTimeOffset CreeLe, string CreePar,
    DateTimeOffset ExpireLe, DateTimeOffset? RevoqueLe, string? RevoquePar, DateTimeOffset? DerniereUtilisationLe, string? DerniereAdresseIp,
    Guid CompteId, string CompteLogin, string CompteNom, int? RequetesParMinute);

/// <summary>Jeton créé : <c>Secret</c> est le jeton complet (<c>gmep_…</c>), renvoyé cette seule fois et jamais conservé.</summary>
public record JetonCreeDto(JetonDto Jeton, string Secret);

/// <summary>
/// Nouveau jeton : nom (100 caractères au plus), portées (au moins une ; <c>admin</c> pour un jeton de service seulement)
/// et durée de validité en jours (1 à 365, 90 si absente).
/// </summary>
public record JetonSaisie(string? Nom, List<string>? Portees, int? DureeJours);

/// <summary>
/// Nouveau jeton de service : comme <see cref="JetonSaisie"/>, plus le compte technique à réutiliser (<c>CompteTechniqueId</c> ;
/// absent : un compte technique est créé au nom du jeton) et une limite de débit propre (<c>RequetesParMinute</c>, 1 à
/// 100 000 ; absente : réglage global).
/// </summary>
public record JetonServiceSaisie(string? Nom, List<string>? Portees, int? DureeJours, Guid? CompteTechniqueId, int? RequetesParMinute);

/// <summary>
/// Portée d'un jeton (<c>GET /api/jetons/portees</c>) : nom, description et types de jetons qui peuvent la porter.
/// </summary>
public record PorteeJetonDto(string Nom, string Description, List<TypeJeton> Types);
