# Architecture

Ce document décrit comment l'application est construite. Le contrat d'API est dans [api.md](api.md), l'installation et la maintenance en production dans [exploitation.md](exploitation.md).

## Vue d'ensemble

```
Navigateur ──HTTPS──▶ Proxy inverse (IIS, nginx, Traefik…) ──HTTP :8080──▶ Conteneur « app »
                                                                          │  ASP.NET Core (.NET 10)
                                                                          │  ├─ /api/…      API JSON + flux SSE
                                                                          │  └─ /…          front React (wwwroot)
                                                                          ├─▶ PostgreSQL 16 (conteneur « db »)
                                                                          ├─▶ volume « pieces-jointes »
                                                                          └─▶ volume « cles » (clés du cookie de session)
```

- **Une seule image Docker** contient l'API et le front déjà construit : l'API sert les fichiers statiques et renvoie `index.html` pour toute route qui n'est pas sous `/api`.
- **PostgreSQL** est la seule dépendance externe. Le schéma est migré automatiquement au démarrage (EF Core).
- **Pièces jointes** : fichiers sur disque (volume Docker), métadonnées en base.
- **Instance unique** : le flux temps réel est diffusé en mémoire et les migrations tournent au démarrage. Pour plusieurs instances, il faudrait remplacer `DiffuseurMep` par `LISTEN/NOTIFY` PostgreSQL et sortir les migrations du démarrage.

## Back-end

Deux projets dans `backend/src`, testés par `backend/tests/GestionMep.Tests`.

### `GestionMep.Domain` : modèle et règles métier

Aucune dépendance vers ASP.NET ni EF Core.

- `Modele.cs` : entités (`Mep`, `Etape`, `Participant`, `Systeme`, `ResolutionType`, `FicheRecette`, `Approbation`…) et énumérations.
- `Regles.cs` et `Regles.*.cs` : toutes les règles, en fonctions statiques pures d'une même classe partielle, un fichier par domaine (`Regles.Graphe.cs`, `Regles.Disponibilite.cs`, `Regles.Droits.cs`, `Regles.Phases.cs`, `Regles.Recette.cs`, `Regles.Resolutions.cs`…). Elles couvrent la disponibilité d'une étape, la validité du graphe, la vérification du plan, les transitions de phase, les approbations, le go / no-go, le point de non-retour, la recette, le retour arrière, les résolutions types et la duplication.
- Un refus lève `RegleMetierException` avec un message en français destiné à l'utilisateur. L'API le traduit en **422**.
- L'heure courante est toujours passée en paramètre (`maintenant`) : les règles se testent sans horloge.
- `DonneesDemo.cs` : jeu de démonstration (comptes `alice`, `bob`, `claire`, MEP d'exemple).

### `GestionMep.Api` : API HTTP

| Dossier | Rôle |
|---|---|
| `Program.cs` | Composition : services, middlewares, montage des groupes de routes, amorçage. |
| `Endpoints/` | Une classe statique par domaine (`MapMeps`, `MapEtapes`, `MapPhase`…), en Minimal API. Toutes les routes sont sous `/api` et exigent une session, sauf la connexion, `/api/sante` et `/api/version`. |
| `Services/ServiceMep.cs` | Chargement d'une MEP (avec application de l'échéance de retour arrière), contrôle de version, journal d'audit, enregistrement et notification. |
| `Services/MepMapping.cs` | Seul endroit où une entité devient un DTO : calcul des disponibilités, des erreurs du plan et des droits de l'utilisateur courant. |
| `Services/Dtos.cs` | Contrats d'entrée (`…Saisie`) et de sortie (`…Dto`). |
| `Services/DocumentationOpenApi.cs` | Description OpenAPI servie sur `/api/openapi.json`, copiée dans `docs/openapi.json` pour générer les types du front. |
| `Services/ErreurHttp.cs` | Erreurs HTTP explicites et middleware qui traduit les exceptions en `{ "message": "…" }`. |
| `Services/DiffuseurMep.cs` | Diffusion en mémoire de la dernière version de chaque MEP aux abonnés du flux SSE. |
| `Services/SurveillanceEcheances.cs` | Tâche de fond : chaque minute, bascule en retour arrière les MEP dont l'échéance est atteinte. |
| `Services/StockagePiecesJointes.cs` | Écriture, lecture et suppression des fichiers joints. |
| `Securite/` | Protections HTTP : en-têtes du proxy inverse, limite des tentatives de connexion (par IP et par compte), en-tête anti-CSRF exigé, en-têtes de sécurité (CSP…), taille maximale des requêtes, sonde `--sante` de l'image Docker. |
| `Auth/` | Comptes locaux, cookie de session, `Acteur` (utilisateur courant). C'est le seul dossier à modifier pour brancher un SSO OpenID Connect. |
| `Donnees/` | `MepDbContext`, migrations EF Core, `Amorcage` (migration, démonstration ou compte administrateur initial). |

### Déroulé d'une requête d'écriture

1. Le handler charge la MEP par `ServiceMep.Charger`, qui applique d'abord l'échéance de retour arrière si elle est dépassée.
2. Il vérifie les droits (`ServiceMep.ExigerEditeur`, `Regles.EstPilote`…) et la version envoyée par le client (`ExigerVersion`, `ExigerRevision`).
3. Il appelle une ou plusieurs fonctions de `Regles`, qui modifient l'entité ou lèvent `RegleMetierException`.
4. `ServiceMep.Enregistrer` incrémente la version, ajoute l'événement au journal, enregistre, puis notifie le flux SSE.
5. La réponse est **la MEP complète** (`MepDto`), recalculée pour l'utilisateur courant. Le front remplace son état par cette réponse.

### Concurrence

- Chaque MEP porte un numéro de `Version`, chaque étape une `Revision`. Le client renvoie celui qu'il a lu ; un écart donne **409** et le front recharge.
- EF Core utilise aussi la `Version` de la MEP (et celle des résolutions types) comme jeton de concurrence, ce qui protège deux écritures simultanées sur le serveur.

### Codes d'erreur

| Code | Sens |
|---|---|
| 400 | Saisie invalide (champ manquant, JSON mal formé). |
| 401 | Pas de session. |
| 403 | Droit insuffisant. |
| 404 | Ressource introuvable. |
| 409 | Conflit de version : la ressource a changé depuis sa lecture. |
| 413 | Fichier ou requête trop volumineux. |
| 429 | Trop de tentatives de connexion. |
| 422 | Refus par une règle métier. |

### Base de données

- PostgreSQL via Npgsql. Les listes et petits objets attachés à une entité (dépendances, systèmes d'une étape, responsables techniques, expertises…) sont stockés en `jsonb` via `AsJson` / `AsJsonObjet` (`MepDbContext.cs`).
- Les migrations sont dans `Donnees/Migrations`. Voir [CONTRIBUTING.md](../CONTRIBUTING.md#créer-une-migration) pour en créer une.

### Authentification

- Comptes locaux, mots de passe hachés par `PasswordHasher` (PBKDF2), cookie `HttpOnly` `SameSite=Lax`, `Secure` hors développement. Les clés qui chiffrent le cookie sont conservées dans un volume (`Cles:Dossier`) : un redéploiement ne déconnecte personne.
- Connexion limitée par adresse IP (fenêtre d'une minute) et compte bloqué quelques minutes après 5 échecs consécutifs.
- Toute requête `/api` qui modifie doit porter l'en-tête `X-Requested-With` (protection CSRF) ; le front l'ajoute toujours.
- Toutes les réponses portent une politique de contenu (CSP) qui interdit l'affichage dans un cadre, ainsi que `nosniff` et `Referrer-Policy`. Les corps de requête sont limités à 1 Mo, sauf sur les routes de dépôt de pièces jointes.
- Le reste de l'API ne lit que les claims `NameIdentifier`, `Name` et `Role` via `Acteur.Depuis(user)`. Un SSO Microsoft Entra ID s'ajoutera dans `Auth/AuthentificationLocale.cs` sans toucher aux endpoints.

## Front-end

React 18 + TypeScript 7 + Vite, Tailwind CSS 4 et primitives Radix. Le détail est dans [frontend/README.md](../frontend/README.md).

- Une page par route (`src/pages`, `src/parametres`), les onglets d'une MEP dans `src/mep`.
- La logique pure (graphe, disponibilité, recherche, blocs d'instructions, formats) est isolée dans des modules testés sans DOM : `graphe.ts`, `domaine.ts`, `recherche.ts`, `blocs.ts`, `format.ts`.
- Toute action passe par `MepContext.run`, qui appelle l'API, remplace la MEP par la réponse et gère les conflits.
- Temps réel : un `EventSource` par MEP ouverte reçoit la nouvelle version, et le front recharge la MEP si elle est plus récente que la sienne. Une saisie en cours n'est jamais écrasée : un bandeau propose de recharger.

## Tests

| Projet | Contenu | Commande |
|---|---|---|
| `backend/tests` | Règles métier (sans base) et API de bout en bout sur une vraie base PostgreSQL (`WebApplicationFactory`) | `cd backend && dotnet test` |
| `frontend` | Logique pure et composants (Vitest + Testing Library, jsdom) | `cd frontend && npm test` |

## Intégration continue

`.github/workflows/ci.yml` lance les tests back (avec un service PostgreSQL) et front à chaque PR et à chaque fusion sur `main`. Sur `main`, il construit et publie l'image sur `ghcr.io/soladevsolide/gestion-mep`.
