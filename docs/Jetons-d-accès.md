# Jetons d'accès à l'API

Un script, un outil de CI/CD ou une supervision appelle l'API de Gestion MEP avec un **jeton d'accès**, sans ouvrir de session ni garder de cookie. Le jeton se présente dans l'en-tête `Authorization` :

```sh
curl -H "Authorization: Bearer $GMEP_JETON" https://mep.entreprise.fr/api/meps
```

Un jeton ne sert qu'à l'API : il ne permet pas de se connecter à l'interface.

## Deux sortes de jetons

| | Jeton personnel | Jeton de service |
|---|---|---|
| Créé par | chaque personne, pour elle-même (depuis une session : `POST /api/jetons`) | un administrateur (`POST /api/admin/jetons`, ou `gestion-mep jetons creer`) |
| Agit au nom de | la personne qui l'a créé | un **compte technique** qui porte le nom du jeton (« CI GitLab », « Supervision ») |
| Droits | ceux de la personne, réduits à ses portées ; jamais ceux d'administrateur | ceux du compte technique, réduits à ses portées ; administrateur avec la portée `admin` |
| Journal | les actions sont signées du nom de la personne | les actions sont signées du nom du jeton |

Le compte technique d'un jeton de service n'a pas de mot de passe et ne peut pas se connecter. Il apparaît dans la liste des comptes : ajoutez-le à une MEP comme une personne pour qu'il puisse la modifier ou l'exécuter (par exemple démarrer et terminer les étapes de déploiement automatique). Révoquer le jeton désactive aussi son compte technique ; son nom reste dans l'historique des MEP.

## Portées

Chaque jeton reçoit une ou plusieurs portées. Une route que ses portées ne couvrent pas répond **403**, avant même les droits habituels. Les portées ne donnent jamais de droit : la personne (ou le compte technique) doit aussi avoir le droit de faire l'action, exactement comme dans l'interface (pilote, responsable technique, administrateur…).

| Portée | Ouvre |
|---|---|
| `meps:lecture` | Lire les MEP (liste, détail, journal, suivi en direct, rapports, exports Excel, pièces jointes), les bibliothèques, l'annuaire, les équipes et les paramètres. |
| `meps:ecriture` | Créer et modifier les MEP et les bibliothèques (résolutions types, étapes modèles, modèles de recette et de texte). Comprend `meps:lecture`. |
| `execution` | Dérouler une MEP : démarrer, terminer, prendre la main sur une étape, la commenter, autoriser le passage du point de non-retour ou la reprise, saisir les résultats de recette, appliquer une résolution, déposer une preuve. Ne comprend pas la lecture : l'associer à `meps:lecture`. |
| `catalogue:lecture` | Lire le catalogue des systèmes (environnements, fiches d'objets, historique). |
| `catalogue:ecriture` | Modifier le catalogue (réservé aux administrateurs : avec un jeton de service qui a aussi `admin`). Comprend `catalogue:lecture`. |
| `admin` | Routes d'administration (`/api/admin/…` et les autres routes réservées aux administrateurs). **Jetons de service seulement.** |

Quelques routes restent fermées à tous les jetons (403) : connexion, mot de passe, profil, connexion unique, mode démonstration, la gestion des jetons elle-même (un jeton ne crée ni ne révoque de jeton) et celle de l'adresse d'abonnement au calendrier (`/api/calendrier/abonnement`). Le flux `.ics` de cette adresse reste public : il est protégé par son secret. `GET /api/auth/moi` répond à tout jeton valide : il dit au nom de quel compte le jeton agit.

## Durée, révocation, sécurité

- **Expiration** : 90 jours par défaut, 1 an au plus, pour les deux sortes de jetons. Un jeton expiré répond 401 : en créer un nouveau.
- **Révocation** : immédiate et définitive, par son propriétaire (`POST /api/jetons/{id}/revoquer`) ou par un administrateur (`POST /api/admin/jetons/{id}/revoquer`, ou `gestion-mep jetons revoquer`). Un administrateur peut révoquer n'importe quel jeton.
- **Compte désactivé** : les jetons d'une personne désactivée cessent de fonctionner tout de suite, et reprennent si le compte est réactivé (sauf expiration ou révocation entre-temps).
- **Affiché une seule fois** : le jeton complet n'est montré qu'à sa création. Le serveur n'en garde que l'empreinte (SHA-256) et le début visible, `gmep_` suivi de 8 caractères, qui sert à le reconnaître dans les listes et le journal. Un jeton perdu ne se retrouve pas : le révoquer et en créer un autre.
- **Reconnaissable** : tous les jetons commencent par `gmep_`. Les outils de détection de secrets (dépôts de code, journaux) peuvent chercher ce préfixe ; un jeton qui a fui se révoque aussitôt.
- **Dernière utilisation** : date et adresse IP du dernier appel, enregistrées à 5 minutes près et visibles dans les listes.
- **Limite de débit** : 600 requêtes par minute et par jeton par défaut (réglage `jetons-par-minute`, modifiable sans redémarrage) ; au-delà, 429 jusqu'à la minute suivante.
- **Pas de session, pas de CSRF** : une requête qui porte un jeton est authentifiée par lui seul (le cookie éventuel est ignoré), n'ouvre jamais de session et n'a pas besoin de l'en-tête `X-Requested-With`.
- **Journal d'administration** : chaque création et chaque révocation y est inscrite (`jeton.cree`, `jeton.revoque`), avec le préfixe du jeton, jamais son secret.

Bonnes pratiques : un jeton par outil, les seules portées utiles, la durée la plus courte possible, et le jeton rangé dans le coffre de secrets de l'outil (variables protégées de la CI), jamais dans le code.

## Erreurs

Les refus suivent le format habituel de l'API (*problem details*, voir [API](API.md#codes-de-réponse)) :

| Code | Cause |
|---|---|
| 401 | Jeton inconnu ou mal formé, expiré, révoqué, ou compte du jeton désactivé (le message dit lequel). Aussi sur les routes publiques : un jeton refusé n'est jamais ignoré. |
| 403 | Portée manquante (« Ce jeton n'a pas la portée nécessaire : « meps:ecriture ». »), route fermée aux jetons, ou droit habituel refusé (pilote exigé, réservé aux administrateurs…). |
| 429 | Limite de requêtes par minute du jeton dépassée. |

## Créer un jeton

**Jeton personnel** : par l'API, depuis une session ouverte (`POST /api/jetons`) ; l'écran « Mon profil › Jetons d'accès » est en préparation. **Jeton de service** : `POST /api/admin/jetons` (administrateur), ou sur le serveur :

```sh
gestion-mep jetons creer "CI GitLab" --portees meps:lecture,execution --duree 180
gestion-mep jetons liste --type service
gestion-mep jetons revoquer gmep_3fK9aQ2x --oui
```

Le détail des routes (`/api/jetons`, `/api/admin/jetons`) est dans [API](API.md#jetons-daccès), celui des commandes dans [Ligne de commande](Ligne-de-commande.md).

## Exemples

Lire les MEP en préparation (portée `meps:lecture`) :

```sh
curl -s -H "Authorization: Bearer $GMEP_JETON" "https://mep.entreprise.fr/api/meps?modele=false&phase=Preparation"
```

Démarrer puis terminer une étape depuis la CI (portées `meps:lecture` et `execution`, compte technique ajouté à la MEP) :

```sh
curl -s -X POST -H "Authorization: Bearer $GMEP_JETON" -H "Content-Type: application/json" \
  -d '{"revision": 3}' "https://mep.entreprise.fr/api/meps/$MEP/etapes/$ETAPE/demarrer"
```

Vérifier au nom de qui agit un jeton :

```sh
curl -s -H "Authorization: Bearer $GMEP_JETON" https://mep.entreprise.fr/api/auth/moi
```
