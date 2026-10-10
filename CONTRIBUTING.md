# Contribuer

Ce guide s'adresse à un développeur qui reprend le code. L'architecture est décrite dans [docs/architecture.md](docs/architecture.md).

## Prérequis

- SDK .NET 10 (la bande exacte est fixée par `backend/global.json`)
- Node.js 22
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

- **Une règle métier va dans `Regles`** (`GestionMep.Domain`, un fichier `Regles.<Domaine>.cs` par domaine), en fonction statique pure qui lève `RegleMetierException` (traduite en 422). Un handler ne contient pas de règle métier : il charge, vérifie les droits et la version, appelle `Regles`, puis enregistre.
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

### Tests

- Une nouvelle règle métier a son test dans `backend/tests` (fichiers `Regles…Tests.cs` pour les règles pures, `Api…Tests.cs` pour l'API de bout en bout).
- Un nouveau droit a aussi son test négatif : l'appel refusé renvoie bien 403 ou 422.
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
- La migration s'applique toute seule au prochain démarrage de l'API.

## Contrat d'API (OpenAPI)

L'API publie sa description OpenAPI sur `/api/openapi.json` (session requise). Une copie est versionnée dans `docs/openapi.json` et le front en génère ses types dans `frontend/src/api/schema.d.ts`.

Après avoir modifié un DTO (`Services/Dtos.cs`) ou une route :

```bash
cd backend && MAJ_OPENAPI=1 dotnet test --filter OpenApi   # met à jour docs/openapi.json
cd ../frontend && npm run api:types                        # régénère src/api/schema.d.ts
```

Le test `OpenApiTests` échoue tant que `docs/openapi.json` ne correspond pas à l'API. Ne jamais modifier `schema.d.ts` à la main.

## Documentation

- Une route ajoutée ou modifiée est décrite dans [docs/api.md](docs/api.md).
- Un changement d'installation, de configuration ou de sauvegarde est reporté dans [docs/exploitation.md](docs/exploitation.md).
- Les spécifications d'un nouveau jalon vont dans [docs/specs/](docs/specs/).

## Branches et pull requests

- `main` est toujours déployable : chaque fusion publie une image.
- Travailler sur une branche, ouvrir une pull request vers `main`, attendre la CI verte avant de fusionner.
- Messages de commit en français, à l'impératif ou au nominal, qui disent ce qui change pour l'utilisateur.
