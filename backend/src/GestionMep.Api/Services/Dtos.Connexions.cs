using GestionMep.Domain;

namespace GestionMep.Api.Services;

// ---------- Connexions aux API externes (#111) ----------

/// <summary>
/// État d'une connexion : désactivée, en erreur (son dernier appel hors essai est définitivement en échec ; elle repasse
/// active au prochain appel réussi), jamais appelée (hors essais) ou active.
/// </summary>
public enum EtatConnexion { Active, Desactivee, EnErreur, JamaisAppelee }

/// <summary>
/// État d'un secret, jamais sa valeur : enregistré, lisible (false : clés de l'application changées, à ressaisir), date et
/// auteur du dernier remplacement.
/// </summary>
public record SecretEtatDto(bool Enregistre, bool Lisible, DateTimeOffset? RemplaceLe, string? RemplacePar);

public record EnteteAppelDto(string Nom, string Valeur);

/// <summary>En-tête d'une connexion : <c>Valeur</c> (gabarit) pour un en-tête ordinaire, <c>ValeurSecrete</c> (état) pour un secret.</summary>
public record EnteteConnexionDto(string Nom, string Valeur, bool Secret, SecretEtatDto? ValeurSecrete);

/// <summary>Appel dans une liste : historique de la console, dernier appel d'une connexion, envoi suivi depuis une MEP.</summary>
public record AppelResumeDto(Guid Id, Guid ConnexionId, string Connexion, Guid? MepId, string MepNom, string TypeEvenement, string LibelleEvenement,
    string Resume, OrigineAppel Origine, string DemandePar, EtatAppel Etat, int? Code, int Tentatives, int TentativesMax, int DureeMs,
    DateTimeOffset CreeLe, DateTimeOffset? TermineLe, DateTimeOffset? ProchaineTentative, string Erreur);

/// <summary>Tentative d'un appel : code et libellé reçus, ou type d'erreur (adresse, certificat, delai, reseau, redirection, secret).</summary>
public record TentativeDto(DateTimeOffset Debut, int DureeMs, int? Code, string Libelle, string TypeErreur, string Erreur, string ExtraitReponse,
    string EntetesReponse);

/// <summary>
/// Détail d'un appel (console) : requête telle qu'elle est partie, secrets masqués (adresse secrète réduite au domaine), et
/// <c>Curl</c>, la commande équivalente, secrets remplacés par &lt;secret&gt;.
/// </summary>
public record AppelDto(AppelResumeDto Appel, MethodeHttp Methode, string Url, string TypeContenu, List<EnteteAppelDto> Entetes, string Corps,
    List<TentativeDto> Tentatives, Guid? AppelOrigineId, string Curl, bool ConnexionModifiable);

/// <summary>Page de l'historique des appels : <c>Suivant</c>, la valeur de <c>avant</c> pour la page suivante (null : fin).</summary>
public record PageAppelsDto(List<AppelResumeDto> Appels, DateTimeOffset? Suivant, int Echecs);

public record ConnexionApiResumeDto(Guid Id, string Nom, string Description, bool Active, ModeleConnexion Modele, MethodeHttp Methode,
    string Adresse, List<string> Declencheurs, bool EnvoiManuel, string LibelleEnvoiManuel, EtatConnexion Etat, AppelResumeDto? DernierAppel,
    AppelResumeDto? DernierEssai);

/// <summary>
/// Connexion telle que la lit l'éditeur, sans aucun secret. <c>Url</c> : gabarit de l'adresse, ou seulement son origine
/// (« https://hote ») si elle est secrète (<c>AdresseSecrete</c> en donne l'état).
/// </summary>
public record ConnexionApiDto(Guid Id, string Nom, string Description, bool Active, int Version, ModeleConnexion Modele, MethodeHttp Methode,
    string Url, bool UrlSecrete, SecretEtatDto? AdresseSecrete, AuthentificationApi Authentification, string Utilisateur, string NomEntete,
    string TokenUrl, string ClientId, string Scope, SecretEtatDto? Secret, List<EnteteConnexionDto> Entetes, bool Signature,
    SecretEtatDto? SecretSignature, FormatCorps Format, string Corps, List<ChampConnexion> Champs, int DelaiSecondes, int Tentatives,
    List<string> Declencheurs, FiltresConnexion Filtres, bool EnvoiManuel, string LibelleEnvoiManuel, DateTimeOffset CreeeLe,
    DateTimeOffset ModifieeLe, string ModifieePar, EtatConnexion Etat, AppelResumeDto? DernierAppel);

/// <summary>
/// En-tête saisi. <c>ValeurSecrete</c> (en-tête secret) : null ou absent garde la valeur enregistrée, une valeur la remplace.
/// </summary>
public record EnteteConnexionSaisie(string? Nom, string? Valeur, bool Secret, string? ValeurSecrete);

/// <summary>
/// Création ou modification d'une connexion. Secrets en écriture seule : <c>AdresseSecrete</c>, <c>Secret</c>,
/// <c>SecretSignature</c> et les <c>ValeurSecrete</c> des en-têtes ; null ou absent garde la valeur enregistrée, une valeur
/// la remplace. Changer le domaine (ou le port) de l'adresse efface les secrets enregistrés, à ressaisir.
/// <c>Url</c> : gabarit de l'adresse ; ignoré si <c>UrlSecrete</c> (l'adresse complète est alors dans <c>AdresseSecrete</c>).
/// <c>Version</c> : obligatoire en modification (409 si elle est périmée).
/// </summary>
public record ConnexionApiSaisie(int? Version, string? Nom, string? Description, ModeleConnexion? Modele, MethodeHttp? Methode, string? Url,
    bool UrlSecrete, string? AdresseSecrete, AuthentificationApi? Authentification, string? Utilisateur, string? NomEntete, string? TokenUrl,
    string? ClientId, string? Scope, string? Secret, List<EnteteConnexionSaisie>? Entetes, bool Signature, string? SecretSignature,
    FormatCorps? Format, string? Corps, List<ChampConnexion>? Champs, int? DelaiSecondes, int? Tentatives, List<string>? Declencheurs,
    FiltresConnexion? Filtres, bool EnvoiManuel, string? LibelleEnvoiManuel, bool? Active = null);

public record DeclencheurDto(string Cle, string Libelle, string Groupe, bool Frequent);

public record ModeleConnexionDto(ModeleConnexion Modele, string Libelle, string Description, List<string> Etiquettes, MethodeHttp Methode,
    FormatCorps Format, AuthentificationApi Authentification, bool UrlSecrete, bool Signature, string UrlExemple, string Corps,
    List<string> Declencheurs, bool EnvoiManuel, string LibelleEnvoiManuel);

/// <summary>
/// Ce dont l'éditeur a besoin : déclencheurs, modèles de départ, variables (groupe Événement puis catalogue des modèles de
/// texte), version (en-tête User-Agent) et réglages réseau (réseaux autorisés, proxy, adresse publique, envois actifs).
/// </summary>
public record CatalogueConnexionsDto(List<DeclencheurDto> Declencheurs, List<ModeleConnexionDto> Modeles, List<VariableTexteDto> Variables,
    string Version, List<ReglageDto> Reglages);

/// <summary>Événement proposé pour l'aperçu : une ligne du journal de la MEP (Id) ou un événement simulé (Id null).</summary>
public record EvenementProposeDto(long? Id, string Cle, string Libelle, string Detail, DateTimeOffset? Quand);

/// <summary>
/// Aperçu ou essai d'un brouillon : avec une MEP (<c>MepId</c>, sinon des valeurs d'exemple) et un événement (ligne du journal
/// <c>EvenementId</c>, sinon le dernier de type <c>Evenement</c>, sinon un événement simulé). <c>ConnexionId</c> : connexion
/// enregistrée dont reprendre les secrets laissés vides (si le domaine n'a pas changé).
/// </summary>
public record ApercuConnexionSaisie(ConnexionApiSaisie Brouillon, Guid? ConnexionId, Guid? MepId, string? Evenement, long? EvenementId,
    string? Commentaire);

/// <summary>
/// Requête qui partirait, secrets masqués : <c>Lisible</c>, le message sans la syntaxe ; <c>Manquantes</c>, les variables
/// sans valeur dans cette MEP (envoyées vides) ; <c>Inconnues</c> et <c>Erreurs</c> empêchent l'envoi.
/// </summary>
public record ApercuConnexionDto(MethodeHttp Methode, string Url, List<EnteteAppelDto> Entetes, string TypeContenu, string Corps, string Lisible,
    List<string> Manquantes, List<string> Inconnues, List<ErreurChamp> Erreurs, string? MepNom, EvenementProposeDto Evenement,
    List<EvenementProposeDto> Evenements);

/// <summary>Résultat de « Essayer » : la requête, la tentative (code ou erreur, extrait de réponse de 4 Ko au plus).</summary>
public record EssaiConnexionDto(ApercuConnexionDto Requete, Guid AppelId, bool Reussi, TentativeDto Tentative, bool Tronque);

/// <summary>Estimation : appels qu'auraient donnés les déclencheurs et filtres sur les journaux des 30 derniers jours.</summary>
public record EstimationConnexionDto(int Appels, int Meps, int Jours);

/// <summary>Réception d'un renvoi ou d'un envoi depuis une MEP : l'appel mis en file (suivi par GET …/appels/{appelId}).</summary>
public record AppelCreeDto(Guid AppelId);

// ---------- Côté MEP ----------

/// <summary>Connexion à envoi manuel, telle que la voit une MEP : ni adresse ni réglage.</summary>
public record ConnexionManuelleDto(Guid Id, string Nom, string Libelle, string Description);

/// <summary>Envoi définitivement en échec, pas encore suivi d'un envoi réussi de la même connexion pour cette MEP.</summary>
public record EchecEnvoiDto(Guid AppelId, Guid ConnexionId, string Connexion, string LibelleEvenement, string Resume, int? Code,
    int Tentatives, string Erreur, DateTimeOffset Quand, bool RenvoiEnCours);

/// <summary>
/// Connexions dans une MEP : envois manuels proposés (pilote, suppléants, administrateurs : <c>PeutEnvoyer</c>) et échecs à
/// montrer dans « Blocages et échecs ».
/// </summary>
public record ConnexionsMepDto(List<ConnexionManuelleDto> Envois, List<EchecEnvoiDto> Echecs, bool PeutEnvoyer);

public record EnvoiManuelSaisie(string? Commentaire);
