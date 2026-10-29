namespace GestionMep.Domain;

/// <summary>Modèle de départ d'une connexion (origine, informatif : tout reste modifiable).</summary>
public enum ModeleConnexion { Teams, Slack, WebhookJson, Tickets, Libre }

/// <summary>Méthode HTTP d'une connexion. Pas de DELETE : aucun besoin identifié, et une erreur de réglage serait destructrice.</summary>
public enum MethodeHttp { Post, Put, Patch, Get }

/// <summary>Identification auprès de l'outil appelé.</summary>
public enum AuthentificationApi
{
    /// <summary>Aucune (la clé est souvent dans l'adresse : Teams Workflows, Slack).</summary>
    Aucune,
    /// <summary>Jeton envoyé en « Authorization: Bearer ».</summary>
    Jeton,
    /// <summary>Identifiant et mot de passe (« Authorization: Basic »).</summary>
    Basic,
    /// <summary>Clé dans un en-tête nommé (X-Api-Key…).</summary>
    CleEntete,
    /// <summary>OAuth2, identifiants client : jeton demandé au point de jeton, gardé en mémoire jusqu'à son expiration.</summary>
    OAuth2,
}

/// <summary>Format du corps de la requête.</summary>
public enum FormatCorps { Json, Formulaire, Texte, Aucun }

/// <summary>État d'un appel : en file, en cours d'envoi, réussi (réponse 2xx) ou définitivement en échec.</summary>
public enum EtatAppel { EnAttente, EnCours, Reussi, Echec }

/// <summary>Origine d'un appel : événement de la MEP, envoi depuis la MEP, essai depuis l'éditeur, renvoi d'un appel.</summary>
public enum OrigineAppel { Automatique, Manuel, Essai, Renvoi }

/// <summary>
/// Secret d'une connexion (adresse, authentification, signature, en-tête) : chiffré avec les clés de l'application, jamais
/// renvoyé par l'API ; seuls la date et l'auteur du dernier remplacement se lisent.
/// </summary>
public class SecretConnexion
{
    public string Chiffre { get; set; } = "";
    public DateTimeOffset? RemplaceLe { get; set; }
    public string RemplacePar { get; set; } = "";
}

/// <summary>En-tête ajouté à chaque appel. Une valeur peut contenir des variables ; un en-tête secret n'en contient pas.</summary>
public class EnteteConnexion
{
    public string Nom { get; set; } = "";
    /// <summary>Valeur (gabarit) d'un en-tête ordinaire ; vide pour un en-tête secret.</summary>
    public string Valeur { get; set; } = "";
    public bool Secret { get; set; }
    /// <summary>Valeur d'un en-tête secret.</summary>
    public SecretConnexion? ValeurSecrete { get; set; }
}

/// <summary>Champ d'un corps au format formulaire (<c>application/x-www-form-urlencoded</c>) ; la valeur est un gabarit.</summary>
public class ChampConnexion
{
    public string Nom { get; set; } = "";
    public string Valeur { get; set; } = "";
}

/// <summary>
/// Filtres d'une connexion : seulement les MEP qui correspondent (liste vide : tout). Évalués au moment de l'événement. Les
/// modèles de MEP sont toujours exclus.
/// </summary>
public class FiltresConnexion
{
    /// <summary>La MEP appartient à l'une des équipes (une de ses personnes en est membre).</summary>
    public List<Guid> EquipeIds { get; set; } = [];
    /// <summary>La MEP touche l'un des systèmes du catalogue.</summary>
    public List<Guid> SystemeCatalogueIds { get; set; } = [];
    /// <summary>La MEP touche l'un de ces environnements (noms, sans casse ni accents, comme la recherche des MEP).</summary>
    public List<string> Environnements { get; set; } = [];
    public List<NiveauRisque> NiveauxRisque { get; set; } = [];
    /// <summary>Criticité de l'étape, pour les événements d'étape seulement.</summary>
    public List<Criticite> CriticitesEtape { get; set; } = [];
    public List<TypeMep> TypesMep { get; set; } = [];
}

/// <summary>
/// Connexion à une API externe (#111) : où envoyer (méthode, adresse, authentification, en-têtes), quoi (message rempli avec
/// les variables des modèles de texte et de l'événement) et quand (déclencheurs et filtres, envoi depuis une MEP). Réglée par
/// les administrateurs ; les secrets sont chiffrés et liés à l'origine de l'adresse (<see cref="OrigineSecrets"/>).
/// </summary>
public class ConnexionApi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Version { get; set; } = 1;
    public ModeleConnexion Modele { get; set; } = ModeleConnexion.Libre;
    public MethodeHttp Methode { get; set; } = MethodeHttp.Post;
    /// <summary>
    /// Adresse (gabarit : variables permises dans le chemin et les paramètres, pas dans le domaine). Adresse secrète : seulement
    /// le schéma, le domaine et le port, l'adresse complète étant dans <see cref="UrlSecrete"/>.
    /// </summary>
    public string Url { get; set; } = "";
    /// <summary>Adresse complète chiffrée (Teams Workflows, Slack : la clé est dans l'adresse) ; null pour une adresse ordinaire.</summary>
    public SecretConnexion? UrlSecrete { get; set; }
    /// <summary>Schéma, domaine et port auxquels les secrets sont liés : les changer efface les secrets à l'enregistrement.</summary>
    public string OrigineSecrets { get; set; } = "";
    public AuthentificationApi Authentification { get; set; } = AuthentificationApi.Aucune;
    /// <summary>Identifiant (Basic).</summary>
    public string Utilisateur { get; set; } = "";
    /// <summary>Nom de l'en-tête qui porte la clé (CleEntete).</summary>
    public string NomEntete { get; set; } = "";
    /// <summary>Point de jeton, ID client et portée (OAuth2).</summary>
    public string TokenUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Scope { get; set; } = "";
    /// <summary>Jeton, mot de passe, valeur de la clé ou secret client, selon <see cref="Authentification"/>.</summary>
    public SecretConnexion? Secret { get; set; }
    public List<EnteteConnexion> Entetes { get; set; } = [];
    /// <summary>Secret de la signature HMAC SHA-256 du corps (en-tête X-Gestion-Mep-Signature) ; null : pas de signature.</summary>
    public SecretConnexion? SecretSignature { get; set; }
    public FormatCorps Format { get; set; } = FormatCorps.Json;
    public string Corps { get; set; } = "";
    public List<ChampConnexion> Champs { get; set; } = [];
    public int DelaiSecondes { get; set; } = 10;
    public int Tentatives { get; set; } = 3;
    /// <summary>Clés des déclencheurs (<see cref="DeclencheursConnexion"/>).</summary>
    public List<string> Declencheurs { get; set; } = [];
    public FiltresConnexion Filtres { get; set; } = new();
    /// <summary>Le pilote, ses suppléants et les administrateurs envoient le message depuis la MEP (« ⋯ Plus d'actions »).</summary>
    public bool EnvoiManuel { get; set; }
    public string LibelleEnvoiManuel { get; set; } = "";
    public DateTimeOffset CreeeLe { get; set; }
    public DateTimeOffset ModifieeLe { get; set; }
    public string ModifieePar { get; set; } = "";
}

/// <summary>Tentative d'envoi d'un appel : code HTTP reçu, ou erreur (réseau, délai, adresse refusée…).</summary>
public class TentativeAppel
{
    public DateTimeOffset Debut { get; set; }
    public int DureeMs { get; set; }
    public int? Code { get; set; }
    /// <summary>Libellé du code (« Service Unavailable »), vide sans réponse.</summary>
    public string Libelle { get; set; } = "";
    /// <summary>Type d'erreur sans réponse utilisable : adresse, certificat, delai, reseau, redirection ; vide sinon.</summary>
    public string TypeErreur { get; set; } = "";
    /// <summary>Message d'erreur en français (dit quoi faire).</summary>
    public string Erreur { get; set; } = "";
    /// <summary>Début du corps de la réponse (2 Ko au plus).</summary>
    public string ExtraitReponse { get; set; } = "";
    /// <summary>En-têtes de réponse gardés : Content-Type, Retry-After, Location.</summary>
    public string EntetesReponse { get; set; } = "";
}

/// <summary>En-tête non secret tel qu'il est parti (valeur rendue).</summary>
public class EnteteAppel
{
    public string Nom { get; set; } = "";
    public string Valeur { get; set; } = "";
}

/// <summary>
/// Appel d'une connexion : file d'envoi (écrit dans la même transaction que l'événement de la MEP) et historique (purgé après
/// le réglage <c>api-appels-conservation-jours</c>). Le message est rendu une fois, à la création : il ne contient aucun
/// secret ; les secrets sont relus et déchiffrés à chaque tentative.
/// </summary>
public class AppelApi
{
    /// <summary>Identifiant, envoyé dans l'en-tête X-Gestion-Mep-Appel (celui de l'appel d'origine pour un renvoi).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnexionId { get; set; }
    public Guid? MepId { get; set; }
    /// <summary>« Paiement mobile 4.2 » : nom de la MEP au moment de l'appel (reste lisible si la MEP est supprimée).</summary>
    public string MepNom { get; set; } = "";
    /// <summary>Événement du journal de la MEP qui a déclenché l'appel.</summary>
    public long? EvenementId { get; set; }
    /// <summary>Le même, pour l'enregistrer avec l'événement (identifiant attribué par la base dans la même transaction).</summary>
    public EvenementJournal? Evenement { get; set; }
    /// <summary>Clé de l'événement (« etape.echec », « envoi.manuel »…).</summary>
    public string TypeEvenement { get; set; } = "";
    /// <summary>« Étape en échec ».</summary>
    public string LibelleEvenement { get; set; } = "";
    /// <summary>Résumé court pour le journal de la MEP (« E0005 en échec »).</summary>
    public string Resume { get; set; } = "";
    public OrigineAppel Origine { get; set; }
    public Guid? AppelOrigineId { get; set; }
    /// <summary>Compte qui a demandé l'appel (envoi manuel, essai, renvoi) ; null pour un appel automatique.</summary>
    public Guid? DemandeParId { get; set; }
    public string DemandePar { get; set; } = "";
    public MethodeHttp Methode { get; set; }
    /// <summary>Adresse rendue ; adresse secrète : réduite au domaine (l'adresse complète est relue sur la connexion).</summary>
    public string Url { get; set; } = "";
    public bool UrlSecrete { get; set; }
    public string TypeContenu { get; set; } = "";
    public string Corps { get; set; } = "";
    public List<EnteteAppel> Entetes { get; set; } = [];
    public EtatAppel Etat { get; set; } = EtatAppel.EnAttente;
    public int TentativesMax { get; set; } = 1;
    public int DelaiSecondes { get; set; } = 10;
    public List<TentativeAppel> Tentatives { get; set; } = [];
    public DateTimeOffset? ProchaineTentative { get; set; }
    public DateTimeOffset CreeLe { get; set; }
    public DateTimeOffset? TermineLe { get; set; }
    /// <summary>Dernier code HTTP reçu (null sans réponse).</summary>
    public int? DernierCode { get; set; }
}
