# API externes

Une **connexion à une API** envoie les événements des MEP à un autre outil : une carte dans un canal Teams, un message Slack, un ticket de changement, une alerte de supervision. Chaque connexion décrit une requête HTTP (adresse, identification, message) et les moments où elle part : automatiquement à certains événements, ou à la demande du pilote depuis la MEP.

Les connexions se règlent dans la **console d'administration › onglet API externes** (administrateurs seulement), ou en ligne de commande (`gestion-mep connexions`, voir [Exploiter](Exploiter.md#connexions-aux-api-externes)). L'équipe informatique règle le réseau sortant : réseaux internes autorisés, proxy, autorité de certification interne (voir [Exploiter](Exploiter.md#réseau-sortant-des-connexions-aux-api)).

## L'onglet API externes

- **Connexions** : nom, modèle de départ, adresse (seulement le domaine si elle est secrète), déclencheurs, état et dernier appel. Le menu ⋯ d'une ligne permet de la modifier, de l'essayer, de la dupliquer, de l'activer ou de la désactiver, ou de la supprimer avec son historique.
- **Bandeau des connexions en erreur** en tête de l'onglet : une connexion passe « En erreur » quand son dernier appel a définitivement échoué, et redevient « Active » au premier appel réussi. Les autres états : « Jamais appelée » et « Désactivée » (plus aucun appel ne part).
- **Derniers appels** de toutes les connexions, filtrables (tous, échecs, essais). Un clic ouvre le détail : la requête telle qu'elle est partie (secrets masqués), chaque tentative avec le code reçu et un extrait de la réponse, la commande `curl` équivalente (secrets remplacés par `<secret>`) et **Renvoyer**.
- **Réseau** : réseaux internes autorisés, proxy sortant et adresse publique, à modifier dans l'onglet Paramètres.
- En [démonstration](Démarrage-rapide.md), une carte **Récepteur de démonstration** montre les messages reçus par les deux connexions d'exemple, qui visent un récepteur intégré (`recepteur.demo.invalid`) : rien ne part sur le réseau.

## Créer une connexion

**Nouvelle connexion** propose un modèle de départ, puis ouvre l'éditeur :

| Modèle | Ce qu'il prépare |
|---|---|
| Microsoft Teams | Carte dans un canal, par un flux Workflows « Lors de la réception d'une requête webhook ». Adresse secrète. |
| Slack | Message dans un canal, par un webhook entrant. Adresse secrète. |
| Webhook JSON | Toutes les informations de l'événement en JSON, signées (HMAC) pour que le destinataire vérifie l'origine. |
| Outil de tickets | Crée ou complète un ticket de changement avec un compte de service (identifiant et mot de passe). |
| Partir de zéro | Requête vide : méthode, adresse, en-têtes et message à écrire. |

L'éditeur tient en quatre cartes, avec à droite l'aperçu exact de la requête et le bouton **Essayer**. **Dupliquer** ouvre l'éditeur sur une copie, sans les secrets.

### 1. Connexion

Nom (unique) et description, affichée aussi au pilote quand il envoie le message depuis la MEP.

### 2. Requête

- **Méthode** : POST, PUT, PATCH ou GET (sans message).
- **Adresse** : complète, en `https://`. Elle peut contenir des variables (`https://outil.entreprise.fr/mep/{{mep_id}}`), encodées pour l'adresse. **Adresse secrète** : pour Teams Workflows ou Slack, dont l'adresse contient la clé ; elle est chiffrée et seul son domaine reste affiché.
- **Identification** :

  | Choix | Ce qui part |
  |---|---|
  | Aucune | Rien (la clé est souvent dans l'adresse secrète) |
  | Jeton | `Authorization: Bearer <jeton>` |
  | Identifiant et mot de passe | `Authorization: Basic …` (compte de service) |
  | Clé dans un en-tête | La clé dans l'en-tête choisi (`X-Api-Key`…) |
  | OAuth2 (identifiants client) | Un jeton demandé au point de jeton (`client_credentials`, ID client, secret, *scope* facultatif), gardé jusqu'à son expiration puis redemandé |

- **En-têtes** supplémentaires, éventuellement **secrets** (valeur chiffrée). `Host`, `Content-Type`, `User-Agent`, `Cookie` et les en-têtes de transport sont réservés.
- **Signature** facultative : l'en-tête `X-Gestion-Mep-Signature: sha256=<hex>` contient le HMAC-SHA256 du corps envoyé, calculé avec un secret partagé ; le destinataire le recalcule pour vérifier que le message vient bien de l'application.
- **Délai d'attente** (1 à 30 secondes, 10 par défaut) et **tentatives** (1 à 5, 3 par défaut).

### 3. Message

Format **JSON**, **formulaire** (champs `application/x-www-form-urlencoded`, valeurs encodées), **texte** ou **aucun**. Le message s'écrit avec les [variables](Modèles-de-texte.md#variables) des modèles de texte, plus celles du groupe [Événement](Modèles-de-texte.md#événement) (événement, étape, motif, commentaire, identifiants…). Taper `{{` ouvre la liste des variables (↑ ↓ pour choisir, Entrée pour insérer, Échap pour fermer) ; le panneau « Insérer une variable » les propose aussi par groupe, avec une recherche.

En JSON :

- une variable s'écrit **entre guillemets** (`"pilote": "{{pilote}}"`) : sa valeur y est échappée (guillemets, retours à la ligne) ;
- seules les variables de liste (tableau JSON de textes) et `{{donnees_json}}` (objet de toutes les variables) s'écrivent hors guillemets ;
- une information absente de la MEP part vide (`""`, ou `[]` pour une liste) ;
- `**gras**` et les puces des modèles de texte ne sont pas interprétés.

Le JSON est vérifié à la frappe : une erreur est montrée à sa ligne et à sa colonne, avec « Aller à la ligne ». Une erreur, ou une variable inconnue (avec la variable la plus proche à « Corriger »), empêche d'enregistrer et d'essayer. **Mettre en forme le JSON** réindente le message.

### 4. Quand appeler

Les déclencheurs, par groupe :

| Groupe | Déclencheur | Clé (`{{evenement_cle}}`) |
|---|---|---|
| Préparation | Go donné | `gonogo.go` |
| Préparation | No-go | `gonogo.nogo` |
| Préparation | MEP reportée | `mep.reportee` |
| Préparation | MEP annulée | `mep.annulee` |
| Exécution | Étape démarrée (fréquent) | `etape.demarree` |
| Exécution | Étape réussie (fréquent) | `etape.reussie` |
| Exécution | Étape en échec | `etape.echec` |
| Exécution | Point de non-retour autorisé | `etape.non-retour-autorise` |
| Exécution | Échéance de retour arrière dépassée | `echeance.depassee` |
| Exécution | Pilotage passé ou pilotage de secours | `pilotage.passe`, `pilotage.secours` |
| Fin | Passage en recette | `phase.recette` |
| Fin | Test de recette non conforme | `recette.non-conforme` |
| Fin | Retour arrière déclenché | `repli.declenche` |
| Fin | MEP clôturée | `mep.cloturee` |

« Fréquent » : une MEP de 14 étapes donne 28 appels pour « Étape démarrée » et « Étape réussie ». Leurs envois réussis ne sont pas inscrits au journal de la MEP.

**Seulement pour les MEP…** limite les appels aux MEP d'une équipe, qui touchent certains systèmes, sur certains environnements, d'un niveau de risque, d'un type (complète, simplifiée), et aux étapes d'une criticité. Rien de coché : toutes les MEP. Une phrase résume la règle (« Appelée pour les MEP de l'équipe Paiement : go donné et étape en échec (criticité élevée ou bloquante). »), suivie, quand c'est possible, du nombre d'appels que la règle aurait donnés sur les 30 derniers jours.

**Envoi depuis une MEP** : la connexion est proposée dans la MEP, sous le libellé choisi (voir [ci-dessous](#envoyer-depuis-une-mep)). Une connexion peut n'avoir que ce mode d'envoi.

## Essayer

L'aperçu à droite de l'éditeur montre la requête exacte (secrets masqués) qui partirait pour une MEP choisie et un événement de son journal (ou simulé), en forme lisible ou brute, avec les informations absentes de cette MEP.

**Essayer** envoie **vraiment** cette requête, avant même d'enregistrer : `{{essai}}` vaut alors « [Essai] » (à mettre en tête d'un titre, comme dans les modèles fournis). Le résultat donne le code reçu, le temps de réponse et un extrait de la réponse (4 Ko), ou l'erreur expliquée (adresse refusée, certificat, délai, identification refusée…). Un essai ne compte pas comme un appel : il n'est pas réessayé, ne change pas l'état de la connexion et ne s'inscrit pas au journal de la MEP. L'essai d'une connexion enregistrée apparaît dans « Derniers appels » (filtre Essais) ; tout essai est inscrit au journal d'administration.

## Ce qui part

| En-tête | Valeur |
|---|---|
| `Content-Type` | Selon le format : `application/json; charset=utf-8`, `application/x-www-form-urlencoded`, `text/plain; charset=utf-8` |
| `User-Agent` | `GestionMep/<version>` |
| `X-Gestion-Mep-Appel` | Identifiant unique de l'appel, le même à chaque tentative et pour un renvoi : le destinataire s'en sert pour écarter un doublon |
| `X-Gestion-Mep-Evenement` | Clé de l'événement (`etape.echec`, `envoi.manuel`…) |
| `X-Gestion-Mep-Signature` | Si la signature est activée : `sha256=<HMAC du corps>` |

Le message est rendu une fois, au moment de l'événement : un renvoi repart avec le même message. Les secrets, eux, sont relus à chaque tentative.

## Envoi, nouvelles tentatives et échecs

- L'appel est **enregistré avec l'événement**, dans la même transaction : une action refusée ou annulée n'envoie rien, et un arrêt du serveur ne perd aucun appel (les appels en cours sont repris au redémarrage).
- Il part dans les secondes qui suivent, en arrière-plan : l'action du pilote n'attend jamais l'outil appelé. Pour une même connexion et une même MEP, les appels partent **dans l'ordre** des événements.
- **Nouvelles tentatives** après une erreur passagère (réseau, délai dépassé, 408, 425, 429, 5xx) : 30 secondes, 2 minutes, puis 10 minutes, dans la limite des tentatives de la connexion. Un en-tête `Retry-After` est respecté, jusqu'à 10 minutes.
- Une autre réponse (400, 401, 403, 404…) est un **échec définitif**, sans nouvelle tentative, avec une explication (« L'outil refuse l'identification (401) : vérifiez l'identifiant et le mot de passe du compte de service. »). Une **redirection n'est jamais suivie** : c'est un échec, dont le message donne l'adresse proposée par l'outil, à recopier dans la connexion si c'est la bonne.
- L'appel est réussi pour toute réponse 2xx.

Chaque fin d'appel s'inscrit au **journal de la MEP** (famille « API externes », auteur « Système ») : « Message « Canal Teams » envoyé : Go donné (202) », ou « Échec de l'envoi « Canal Teams » : E0005 en échec (503 après 3 tentatives) ». La MEP elle-même ne change pas, ni son rapport de fin. Un **échec définitif** s'inscrit aussi au journal d'administration (auteur « Application ») et apparaît dans la vue d'ensemble de la MEP, panneau **Blocages et échecs**, avec **Renvoyer** pour le pilote, ses suppléants et les administrateurs. La carte disparaît au premier envoi réussi de la même connexion pour cette MEP.

L'historique des appels est gardé 90 jours (réglage `api-appels-conservation-jours`). Le réglage `api-envois-actifs` à `false` suspend tous les envois sans rien perdre : les appels restent en file et partent à la réactivation (utile après une restauration ou sur un serveur de recette).

## Envoyer depuis une MEP

Pour une connexion avec « Envoi depuis une MEP », le pilote, ses suppléants et les administrateurs trouvent le libellé choisi dans le menu **⋯ Plus d'actions › Envoyer** de la MEP, à toutes les phases, clôture comprise. Une fenêtre demande un **commentaire** facultatif (repris par `{{commentaire}}`) et montre l'aperçu du message ; `{{evenement}}` vaut « Point d'étape ». L'aperçu ne montre ni l'adresse ni les en-têtes, sauf à un administrateur. Le résultat est suivi quelques secondes, puis inscrit au journal de la MEP.

## Sécurité

- **Secrets** (adresse secrète, jeton, mot de passe, clé, secret client, en-têtes secrets, secret de signature) : chiffrés en base avec les clés de l'application, jamais réaffichés ni renvoyés par l'API, jamais écrits dans les journaux ni dans les exports. Ils sont liés au **domaine** de l'adresse (et du point de jeton OAuth2) : changer de domaine les efface, il faut les ressaisir. Après une restauration sur un serveur aux clés différentes, ils sont signalés « illisibles », à ressaisir.
- **Réseau interne** : les connexions ne joignent ni les adresses de bouclage, ni les réseaux privés, ni les adresses de lien local (métadonnées des clouds, `169.254.169.254`), ni les autres plages réservées, sauf celles déclarées dans le réglage `api-reseaux-autorises`. Le contrôle porte sur l'adresse réellement jointe, au moment de l'envoi : un nom qui change d'adresse entre la saisie et l'envoi ne passe pas. `http://` n'est accepté que vers ces réseaux autorisés.
- Pas de cookies, certificat toujours vérifié, redirections jamais suivies.
- Toute création, modification (« jeton remplacé », jamais la valeur), activation, suppression et tout essai sont inscrits au journal d'administration.

Une connexion dont un secret manque (importée d'un autre serveur, par exemple) reste désactivée : l'activer demande d'abord de saisir ses secrets.

## Pour les développeurs

Les routes sont sous `/api/admin/connexions` (administrateurs) et `/api/meps/{id}/connexions` (voir [API](API.md#connexions-aux-api-externes)). Le rendu des gabarits est fait par le serveur (`RenduConnexion`) et repris à l'identique par l'éditeur (`frontend/src/textes/gabarits.ts`) : les cas de `frontend/src/vecteurs-gabarits.json` sont vérifiés des deux côtés. Un nouveau déclencheur s'ajoute dans `DeclencheursConnexion.Tous` et dans le tableau ci-dessus (un test vérifie que chacun y figure) ; une nouvelle variable du groupe Événement, dans `VariablesEvenement.Toutes` et dans [Modèles de texte](Modèles-de-texte.md#événement).
