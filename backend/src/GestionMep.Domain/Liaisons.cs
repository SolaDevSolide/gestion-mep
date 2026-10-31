using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestionMep.Domain;

/// <summary>
/// État d'une liaison avec Noosphere (contrat : pending, active, cut). <see cref="EnAttente"/> : appairée, empreinte pas encore
/// confirmée ici ; <see cref="Active"/> : confirmée ici (les échanges de données attendent aussi la confirmation de Noosphere) ;
/// <see cref="Coupee"/> : coupée par l'un des côtés, clés effacées, gardée pour l'historique.
/// </summary>
public enum EtatLiaison { EnAttente, Active, Coupee }

/// <summary>Capacités d'une liaison, choisies dans Noosphere à la création du code (valeurs du contrat).</summary>
public static class CapacitesLiaison
{
    public const string LectureCatalogue = "catalog:read";
    public const string EnvoiAvis = "notices:send";
    public const string ReceptionDeploiements = "deployments:receive";
    public static readonly IReadOnlyList<string> Toutes = [LectureCatalogue, EnvoiAvis, ReceptionDeploiements];
}

/// <summary>Clés de la liaison : privées chiffrées au repos (Data Protection, PKCS#8), publiques en JWK.</summary>
public class ClesLocales
{
    public string SignaturePrivee { get; set; } = "";
    public JwkPublique Signature { get; set; } = new("EC", CryptoLiaison.Courbe, "", "");
    public string ChiffrementPrivee { get; set; } = "";
    public JwkPublique Chiffrement { get; set; } = new("EC", CryptoLiaison.Courbe, "", "");
    /// <summary>Clés remplacées par un renouvellement : encore acceptées jusqu'à cette date (messages en route).</summary>
    public DateTimeOffset? ValablesJusquA { get; set; }
}

/// <summary>Clés publiques de Noosphere pour une liaison (JWK).</summary>
public class ClesPair
{
    public JwkPublique Signature { get; set; } = new("EC", CryptoLiaison.Courbe, "", "");
    public JwkPublique Chiffrement { get; set; } = new("EC", CryptoLiaison.Courbe, "", "");
    /// <summary>Anciennes clés après un renouvellement : la signature reste acceptée jusqu'à cette date.</summary>
    public DateTimeOffset? ValablesJusquA { get; set; }
}

/// <summary>
/// Liaison avec une instance de Noosphere. L'identifiant est celui choisi par Noosphere à l'appairage (<c>linkId</c>). Une
/// liaison coupée garde son historique (échanges, empreinte) mais plus aucune clé.
/// </summary>
public class Liaison
{
    public Guid Id { get; set; }
    public string Produit { get; set; } = "noosphere";
    public string NomInstance { get; set; } = "";
    /// <summary>Adresse de Noosphere saisie à l'appairage (sans « / » final) ; les appels visent {Url}/link/v1/….</summary>
    public string Url { get; set; } = "";
    public string VersionPair { get; set; } = "";
    public EtatLiaison Etat { get; set; } = EtatLiaison.EnAttente;
    public bool ConfirmeeIci { get; set; }
    public DateTimeOffset? ConfirmeeLe { get; set; }
    public string ConfirmeePar { get; set; } = "";
    /// <summary>Confirmation côté Noosphere, d'après son dernier <c>/status</c> (null : pas encore connue).</summary>
    public bool? ConfirmeeParPair { get; set; }
    public List<string> Capacites { get; set; } = [];
    public ClesLocales? Cles { get; set; }
    public ClesLocales? AnciennesCles { get; set; }
    public ClesPair? ClesPair { get; set; }
    public ClesPair? AnciennesClesPair { get; set; }
    /// <summary>Identifiant (empreinte RFC 7638) de la clé de signature de Noosphere : retrouve la liaison d'une requête.</summary>
    public string KidPair { get; set; } = "";
    public string AncienKidPair { get; set; } = "";
    public string Empreinte { get; set; } = "";
    public DateTimeOffset AppaireeLe { get; set; }
    public string AppaireePar { get; set; } = "";
    public DateTimeOffset? ClesRenouveleesLe { get; set; }
    public DateTimeOffset? DernierEchangeLe { get; set; }
    public DateTimeOffset? CoupeeLe { get; set; }
    public string CoupeePar { get; set; } = "";

    // Réglages de gestion-mep
    /// <summary>« Envoyer les MEP à Noosphere » (activé par défaut).</summary>
    public bool EnvoyerMeps { get; set; } = true;
    /// <summary>« Suivre automatiquement les nouvelles applications des domaines suivis » (activé par défaut).</summary>
    public bool SuiviAutomatique { get; set; } = true;
    public List<string> DomainesSuivis { get; set; } = [];
    /// <summary>Applications déjà vues dans le catalogue : seules les autres sont « nouvelles » pour le suivi automatique.</summary>
    public List<string> ApplicationsConnues { get; set; } = [];

    // Dernier catalogue lu
    public string EtagCatalogue { get; set; } = "";
    public CatalogueNoosphere? Catalogue { get; set; }
    public DateTimeOffset? DerniereSynchro { get; set; }
    public string DerniereErreur { get; set; } = "";
}

/// <summary>Échange avec Noosphere, entrant ou sortant, réussi ou refusé (journal gardé 90 jours).</summary>
public class EchangeLiaison
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Null pour une requête refusée dont la clé n'est rattachée à aucune liaison.</summary>
    public Guid? LiaisonId { get; set; }
    public DateTimeOffset Quand { get; set; }
    /// <summary>« entrant » ou « sortant ».</summary>
    public string Sens { get; set; } = "";
    /// <summary>pair, status, catalog, notices, deployments, rekey, unpair.</summary>
    public string Operation { get; set; } = "";
    /// <summary>« ok », « refuse », « echec » ou « demo ».</summary>
    public string Resultat { get; set; } = "";
    public int? Code { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>Nonce d'une requête signée reçue, gardé 10 minutes : une requête rejouée est refusée.</summary>
public class NonceLiaison
{
    public string Nonce { get; set; } = "";
    public DateTimeOffset VuLe { get; set; }
}

/// <summary>Dernier événement de déploiement envoyé à une liaison pour une MEP : évite les envois sans changement.</summary>
public class DeploiementLiaison
{
    public Guid LiaisonId { get; set; }
    public Guid MepId { get; set; }
    public string Empreinte { get; set; } = "";
    public EvenementDeploiement? Contenu { get; set; }
    public DateTimeOffset EnvoyeLe { get; set; }
}

/// <summary>
/// Source d'un système du catalogue suivi depuis Noosphere : nom et description y sont gérés (lecture seule ici), le reste
/// (environnements, cibles, objets) reste local.
/// </summary>
public class SourceNoosphere
{
    public Guid LiaisonId { get; set; }
    public string ApplicationId { get; set; } = "";
    /// <summary>Adresse de la fiche dans Noosphere (« Ouvrir dans Noosphere »).</summary>
    public string Url { get; set; } = "";
    public string Domaine { get; set; } = "";
    public string Equipe { get; set; } = "";
    /// <summary>Cycle de vie Noosphere : planned, live, retiring, retired.</summary>
    public string Cycle { get; set; } = "";
    public DateTimeOffset? DerniereSynchro { get; set; }
    /// <summary>Application absente du dernier catalogue (supprimée ou devenue restreinte) : système désactivé.</summary>
    public bool Absente { get; set; }
    /// <summary>Système désactivé par la synchronisation (retirée ou absente) : réactivé si l'application revient.</summary>
    public bool DesactiveParLiaison { get; set; }
}

// ---------- Messages du protocole (clés JSON en anglais, contrat partagé) ----------

public sealed record DomaineNoosphere([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string Nom);

public sealed record EquipeNoosphere([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string Nom,
    [property: JsonPropertyName("contact")] string? Contact);

public sealed record ApplicationNoosphere(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Nom,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("domainId")] string? DomaineId,
    [property: JsonPropertyName("teamId")] string? EquipeId,
    [property: JsonPropertyName("kind")] string? Type,
    [property: JsonPropertyName("lifecycle")] string? Cycle,
    [property: JsonPropertyName("tech")] JsonElement? Technologie,
    [property: JsonPropertyName("url")] string? Url);

public sealed record FluxNoosphere(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Nom,
    [property: JsonPropertyName("fromId")] string? DeId,
    [property: JsonPropertyName("toId")] string? VersId,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("status")] string? Statut,
    [property: JsonPropertyName("schedule")] JsonElement? Planification,
    [property: JsonPropertyName("url")] string? Url);

/// <summary>Catalogue de Noosphere (<c>GET /link/v1/catalog</c>) : seuls les enregistrements visibles de tous.</summary>
public sealed record CatalogueNoosphere(
    [property: JsonPropertyName("generatedAt")] string? GenereLe,
    [property: JsonPropertyName("instanceName")] string? NomInstance,
    [property: JsonPropertyName("baseUrl")] string? UrlBase,
    [property: JsonPropertyName("domains")] List<DomaineNoosphere>? Domaines,
    [property: JsonPropertyName("teams")] List<EquipeNoosphere>? Equipes,
    [property: JsonPropertyName("applications")] List<ApplicationNoosphere>? Applications,
    [property: JsonPropertyName("flows")] List<FluxNoosphere>? Flux);

public sealed record ChangementNoosphere(
    [property: JsonPropertyName("kind")] string? Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("operation")] string? Operation,
    [property: JsonPropertyName("at")] string? Le);

/// <summary>Avis de changement (<c>POST /link/v1/notices</c>).</summary>
public sealed record AvisNoosphere([property: JsonPropertyName("changes")] List<ChangementNoosphere>? Changements);

public sealed record EnvironnementDeploiement([property: JsonPropertyName("name")] string Nom, [property: JsonPropertyName("production")] bool Production);

/// <summary>Événement de déploiement d'une MEP (<c>POST /link/v1/deployments</c>).</summary>
public sealed record EvenementDeploiement(
    [property: JsonPropertyName("deploymentId")] Guid DeploiementId,
    [property: JsonPropertyName("title")] string Titre,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("outcome")] string? Issue,
    [property: JsonPropertyName("postponed")] bool Reportee,
    [property: JsonPropertyName("plannedStart")] string? DebutPrevu,
    [property: JsonPropertyName("plannedEnd")] string? FinPrevue,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("applicationIds")] List<string> ApplicationIds,
    [property: JsonPropertyName("environments")] List<EnvironnementDeploiement> Environnements,
    [property: JsonPropertyName("updatedAt")] string MisAJourLe);

public sealed record LotDeploiements([property: JsonPropertyName("events")] List<EvenementDeploiement> Evenements);

public sealed record ReponseDeploiements([property: JsonPropertyName("accepted")] int Acceptes,
    [property: JsonPropertyName("ignoredApplicationIds")] List<string>? ApplicationsIgnorees);

/// <summary>État d'une liaison (<c>GET /link/v1/status</c>), servi par les deux côtés.</summary>
public sealed record StatutLiaison(
    [property: JsonPropertyName("linkId")] Guid LiaisonId,
    [property: JsonPropertyName("product")] string Produit,
    [property: JsonPropertyName("instanceName")] string NomInstance,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("confirmed")] bool Confirmee,
    [property: JsonPropertyName("capabilities")] List<string> Capacites,
    [property: JsonPropertyName("keysRenewedAt")] string? ClesRenouveleesLe);

/// <summary>Nouvelles clés publiques de l'émetteur (<c>POST /link/v1/rekey</c>).</summary>
public sealed record NouvellesCles([property: JsonPropertyName("signingKey")] JwkPublique Signature,
    [property: JsonPropertyName("encryptionKey")] JwkPublique Chiffrement);

/// <summary>Raisons de refus d'une requête signée (fin du <c>type</c> du problème : https://noosphere.dev/problems/&lt;raison&gt;).</summary>
public static class RefusLiaison
{
    public const string CleInconnue = "unknown-key";
    public const string LiaisonCoupee = "link-cut";
    public const string NonConfirmee = "link-not-confirmed";
    public const string Horloge = "clock-skew";
    public const string Rejeu = "replay";
    public const string Empreinte = "bad-digest";
    public const string Signature = "bad-signature";
    public static readonly IReadOnlyList<string> Toutes = [CleInconnue, LiaisonCoupee, NonConfirmee, Horloge, Rejeu, Empreinte, Signature];
    /// <summary>Hors des sept refus de signature : capacité absente de la liaison (403).</summary>
    public const string CapaciteManquante = "capability-missing";
    /// <summary>Corps qui n'est pas un JWE pour la clé de chiffrement de ce côté, ou pas le JSON attendu (400).</summary>
    public const string CorpsInvalide = "bad-body";
    /// <summary>Appairage : code d'authentification faux (401, le code est brûlé après 5 échecs).</summary>
    public const string MacInvalide = "bad-mac";
    public static string Type(string raison) => $"https://noosphere.dev/problems/{raison}";

    public static string Libelle(string raison) => raison switch
    {
        CleInconnue => "clé inconnue",
        LiaisonCoupee => "liaison coupée",
        NonConfirmee => "liaison pas encore confirmée ici",
        Horloge => "horloges trop éloignées (plus de 300 s)",
        Rejeu => "requête rejouée (nonce déjà vu)",
        Empreinte => "Content-Digest ne correspond pas au corps",
        Signature => "signature invalide",
        CapaciteManquante => "capacité absente de la liaison",
        CorpsInvalide => "corps illisible (JWE ou JSON)",
        MacInvalide => "code d'authentification de l'appairage faux",
        _ => raison,
    };
}
