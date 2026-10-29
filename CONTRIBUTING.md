# Contribuer

Ce guide s'adresse à un développeur qui reprend le code. L'architecture est décrite dans [docs/Architecture.md](docs/Architecture.md).

## Prérequis

- SDK .NET 10 (la bande exacte est fixée par `backend/global.json`)
- Node.js 26 (version de la CI et de l'image ; 22.22 ou plus fonctionne aussi)
- PostgreSQL 16 sur `localhost:5432`, utilisateur `postgres`, mot de passe `postgres` (ou Docker : `docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:16`)

```bash
cd backend && dotnet tool restore     # outils .NET du dépôt (dotnet-ef)
cd ../frontend && npm ci
```

## Lancer en développement

```bash
# API sur http://localhost:5080 : crée la base et les données de démonstration au premier lancement
cd backend && dotnet run --project src/GestionMep.Api

# Front sur http://localhost:5173, avec rechargement à chaud (les appels /api passent par un proxy vers l'API)
cd frontend && npm run dev
```

Comptes de démonstration : `alice` (administratrice), `bob`, `claire`, mot de passe `demo`.

Pour voir l'application comme avec une base lointaine (squelettes de chargement, rechargements en arrière-plan), l'API peut retarder chacune de ses réponses, en développement seulement (jamais en production, inactif par défaut) :

```bash
cd backend && Developpement__LatenceMs=800 dotnet run --project src/GestionMep.Api
```

## Avant de pousser

| Côté | Commande | Vérifie |
|---|---|---|
| Back | `cd backend && dotnet build` | Compilation, analyseurs .NET. Tout avertissement est une erreur. |
| Back | `cd backend && dotnet test` | Règles métier et API sur PostgreSQL. |
| Front | `cd frontend && npm run lint` | Oxlint : règles React, accessibilité, promesses non attendues. |
| Front | `cd frontend && npm run format:check` | Formatage Prettier (`npm run format` corrige). |
| Front | `cd frontend && npm test` | Tests Vitest. |
| Front | `cd frontend && npm run build` | Typage strict et construction. |

La CI lance les mêmes vérifications sur chaque pull request.

## Conventions

### Langue

- Le **domaine est en français** : classes, propriétés, méthodes, variables, messages, commentaires (`Etape`, `PasserEnExecution`, `ErreursPlan`). Les mots techniques du framework restent en anglais (`Endpoints`, `DbContext`, `Dto`).
- Pas d'accents dans les identifiants (`Etape`, `Recette`), accents partout dans les textes affichés.
- Tout message destiné à l'utilisateur est une phrase complète en français.

### Back-end

- **Une règle métier va dans `Regles`** (`GestionMep.Domain`, un fichier `Regles.<Domaine>.cs` par domaine), en fonction statique pure qui lève `RegleMetierException` (traduite en 422), `DroitInsuffisantException` pour un refus de droits (403) ou `SaisieInvalideException` pour une valeur vide, inconnue ou en double (400) : voir les règles des [codes de réponse](docs/API.md#codes-de-réponse). Une route réservée aux administrateurs se déclare par `.ExigerAdmin()`. Les heures locales passent par `Fuseaux` (domaine), jamais par `TimeZoneInfo` directement. Un handler ne contient pas de règle métier : il charge, vérifie les droits et la version, appelle `Regles`, puis enregistre.
- L'heure courante passe par `TimeProvider` côté API et par un paramètre `maintenant` côté domaine.
- Toute écriture passe par `ServiceMep.Enregistrer` (version, journal, notification temps réel). Une modification du contenu en préparation passe par `EnregistrerContenu`, qui remet les approbations à zéro.
- Les codes d'étape (`E0001`, `R0001`) ne sont jamais renumérotés.
- Le style est fixé par `backend/.editorconfig` ; les analyseurs écartés y sont listés avec leur raison.
- Commentaires XML `///` sur les membres publics dont le rôle n'est pas évident.

### Front-end

- TypeScript strict, pas de `any`.
- La logique sans affichage va dans un module pur et testé (`domaine.ts`, `graphe.ts`, `recherche.ts`…), pas dans un composant.
- Une action sur une MEP passe par `useMep().run(...)`, qui gère les conflits et les messages. Voir [frontend/README.md](frontend/README.md).
- Couleurs et espacements par les jetons CSS de `src/index.css`, jamais en dur.
- Données qui arrivent du serveur : un squelette aux dimensions du contenu (`ui/Squelette.tsx`, dans `EnChargement` ou `Apparition`), jamais un écran vide ni un état provisoire faux (« Aucun… » avant la réponse) ; pendant un rechargement, l'ancien contenu reste affiché (`useAsync` : `rechargement`).

### Tests

- Une nouvelle règle métier a son test dans `backend/tests` (fichiers `Regles…Tests.cs` pour les règles pures, `Api…Tests.cs` pour l'API de bout en bout).
- Un nouveau droit a aussi son test négatif : l'appel refusé renvoie bien 403.
- Les noms de tests sont des phrases : `Demo_plan_valide_en_attente_de_l_approbation_de_claire`.

## Créer une migration

Après avoir modifié le modèle (`Modele.cs`) ou `MepDbContext.cs` :

```bash
cd backend
dotnet ef migrations add AjoutCommentaireEtape --project src/GestionMep.Api --output-dir Donnees/Migrations
```

- Donner un **nom descriptif** du changement (`AjoutCommentaireEtape`), pas un numéro de jalon.
- Relire le fichier généré : une colonne renommée apparaît souvent comme une suppression puis un ajout, ce qui perd les données.
- Une migration qui transforme des données existantes a son test (exemple : `MigrationJalon4Tests.cs`).
- La migration s'applique toute seule au prochain démarrage de l'API, après une sauvegarde automatique (`Services/Sauvegardes.cs`). Les contrôles qui suivent exigent qu'aucune table ne disparaisse et qu'aucune ne perde de lignes : une migration qui supprime des données volontairement laissera la sauvegarde en place et un écart dans le journal d'administration.
- Une migration qui reprend les lignes d'une table dans une autre puis supprime la table d'origine déclare cette reprise dans `Sauvegardes.Reprises` (table d'origine → table d'arrivée, exemple : `Livrables` → `ObjetsImpactes`) : les contrôles comptent alors les lignes de la table d'origine avec celles de la table d'arrivée au lieu de la signaler disparue.

## Contrat d'API (OpenAPI)

L'API publie sa description OpenAPI sur `/api/openapi.json` (session requise). Une copie est versionnée dans `docs/openapi.json` et le front en génère ses types dans `frontend/src/api/schema.d.ts`.

Après avoir modifié un DTO (`Services/Dtos.cs`) ou une route :

```bash
cd backend && MAJ_OPENAPI=1 dotnet test --filter OpenApi   # met à jour docs/openapi.json
cd ../frontend && npm run api:types                        # régénère src/api/schema.d.ts
```

Le test `OpenApiTests` échoue tant que `docs/openapi.json` ne correspond pas à l'API. Ne jamais modifier `schema.d.ts` à la main.

## Documentation

La documentation vit dans `docs/` et ce fichier. À chaque fusion sur `main`, l'action `Wiki` la recopie dans le wiki GitHub du dépôt : on modifie toujours `docs/`, jamais le wiki directement (il serait écrasé).

- Une route ajoutée ou modifiée est décrite par le contrat OpenAPI (voir ci-dessus) ; une nouvelle convention de l'API va dans [docs/API.md](docs/API.md).
- Un changement d'installation ou de configuration est reporté dans [docs/Installer.md](docs/Installer.md), un changement de mise à jour, de sauvegarde ou de commande dans [docs/Exploiter.md](docs/Exploiter.md).
- Un changement visible par les utilisateurs met à jour la page de guide concernée, et [docs/Droits.md](docs/Droits.md) s'il touche aux droits.
- Une nouvelle page s'ajoute à plat dans `docs/` (le nom du fichier devient le titre de la page du wiki) et dans [docs/_Sidebar.md](docs/_Sidebar.md).

### Animations et captures

Chaque explication des guides est illustrée par une courte animation (`docs/images/*.webp`), placée juste après le texte qu'elle illustre avec un texte alternatif et une légende en italique ; quelques captures (`*.png`) montrent un écran entier. Toutes sont produites par un script Playwright qui pilote l'application de démonstration : on ne les fait jamais à la main, on les régénère.

```bash
# L'API en développement (démonstration active) sert le front construit, sur http://localhost:5080
cd frontend && npm run build
cd ../backend && dotnet run --project src/GestionMep.Api -- --webroot ../frontend/dist

# Puis, dans frontend (ffmpeg doit être installé) :
ANIMATIONS_URL=http://localhost:5080 npm run docs:animations                       # tout (une demi-heure environ)
ANIMATIONS_URL=http://localhost:5080 npm run docs:animations -- go-no-go journal   # seulement celles-ci
npm run docs:animations -- --liste                                                   # page et section de chacune
```

Sans `ANIMATIONS_URL`, le script vise le serveur de développement Vite (http://localhost:5173). Chaque scénario part d'une **démonstration réinitialisée** : ne jamais viser une installation réelle. Fenêtre de 1200 × 800, thème clair, bandeau de démonstration masqué (sauf là où il est le sujet), transitions coupées et curseur dessiné ; les images brutes passent par `frontend/test-results/animations-doc`, vidé à la fin (`--garder` les conserve, et une capture de l'écran est faite en cas d'échec).

- Les scénarios sont dans `frontend/animations-doc/scenarios/`, un fichier par page de guide ; `outils.mjs` contient le curseur, les gestes (`Scene`) et l'encodage.
- Format : WebP animé avec perte (qualité 72, 12 images par seconde, couleurs arrondies pour éviter les traces des fondus). Mesuré sur les mêmes enregistrements : 25 à 60 % plus léger qu'un GIF (limité à 256 couleurs, tramé) et 4 à 5 fois plus qu'un APNG ; il s'affiche dans GitHub et dans le wiki (une vidéo `<video>` ne s'y affiche pas depuis un fichier du dépôt). Viser 10 à 20 secondes et moins de 300 Ko par animation.
- Un changement d'interface qui touche un geste montré : régénérer seulement les animations concernées, puis les regarder avant de committer.

## Branches et pull requests

- `main` est toujours déployable : chaque fusion publie une image.
- Travailler sur une branche nommée selon sa nature (`fonctionnalites/…`, `correctifs/…`, `docs/…`) et ouvrir une pull request vers `main`. La branche `main` est protégée : pas de push direct, CI verte (`backend`, `frontend`, `image` ; la CI lance aussi `archives`, `codeql`, `dependances`, `scripts` et `image-controles`) et branche à jour exigées avant la fusion, qui se fait en « Squash and merge » (un commit par PR, branche supprimée ensuite).
- Après un clone : `git config blame.ignoreRevsFile .git-blame-ignore-revs`, pour que `git blame` saute le commit de formatage global (GitHub l'applique tout seul).
- Publier une version : créer l'étiquette `vX.Y.Z` sur `main` (`git tag v1.1.0 && git push origin v1.1.0`). La CI publie l'image `X.Y.Z`, `X.Y` et `latest`, puis la version GitHub avec ses notes, les archives de l'installation sans Docker (Linux et Windows), le `docker-compose.yml` et l'`env.example` de l'installation Docker, et `SHA256SUMS`, que l'indicateur de mise à jour des applications installées consulte.
- Messages de commit en français, à l'impératif ou au nominal, qui disent ce qui change pour l'utilisateur.
