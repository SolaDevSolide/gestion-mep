# Gestion MEP

Application web pour préparer et exécuter les mises en production (MEP) : le développeur décrit l'intervention, les personnes et systèmes, les livrables, la sauvegarde et le plan d'étapes ; n'importe quel opérateur peut ensuite réaliser la MEP pas à pas, avec dépendances, preuves, échéance de retour arrière et journal d'audit.

Elle reprend le modèle Excel `Modele_MEP_test_navigation.xlsm` et ses règles.

| Document | Pour qui |
|---|---|
| [docs/exploitation.md](docs/exploitation.md) | Équipe informatique : installation, proxy HTTPS, mises à jour, sauvegardes |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Développeurs : conventions, tests, migrations |
| [docs/architecture.md](docs/architecture.md) | Développeurs : organisation du code et principes |
| [docs/api.md](docs/api.md) | Contrat de l'API |
| [frontend/README.md](frontend/README.md) | Développeurs front |
| [docs/specs/](docs/specs/) | Spécifications par jalon |

## Démarrer en local (Docker Desktop)

```bash
git clone https://github.com/SolaDevSolide/gestion-mep.git
cd gestion-mep
docker compose up --build
```

Pour avoir les données de démonstration, créer d'abord à la racine un fichier `.env` contenant la ligne `DEMO=true`.

Ouvrir http://localhost:8080. Au premier démarrage, la base est créée avec une MEP de démonstration et trois comptes : `alice` (pilote, admin), `bob`, `claire`, mot de passe `demo`. Sans `DEMO=true`, la démonstration est désactivée (c'est le défaut) et il faut définir `ADMIN_LOGIN` / `ADMIN_MOT_DE_PASSE` dans `.env`.

Remettre la base à zéro : `docker compose down -v`.

## Déployer sur un autre environnement

Seuls Docker et Docker Compose sont nécessaires.

1. Copier `docker-compose.yml` et `.env.example` (renommé `.env`) sur le serveur.
2. Dans `.env` : `DEMO=false`, un mot de passe PostgreSQL, un compte `ADMIN_LOGIN` / `ADMIN_MOT_DE_PASSE`, et `MEP_IMAGE=ghcr.io/soladevsolide/gestion-mep:latest` pour utiliser l'image publiée par la CI.
3. `docker compose pull && docker compose up -d`.

La base est migrée automatiquement au démarrage. Placer un reverse proxy HTTPS (IIS, nginx, Traefik…) devant le port 8080 : hors développement, le cookie de session est marqué `Secure` et n'est envoyé qu'en HTTPS (seul `http://localhost` fait exception dans les navigateurs). Le proxy doit transmettre `X-Forwarded-For` et `X-Forwarded-Proto` ; s'il n'est pas sur la même machine que Docker, indiquer son adresse dans `PROXY_RESEAUX`.

Sécurité intégrée : les clés de session sont conservées dans le volume `cles` (un redéploiement ne déconnecte personne), les tentatives de connexion sont limitées par adresse IP (`CONNEXIONS_PAR_MINUTE`) et un compte est bloqué quelques minutes après 5 échecs consécutifs.

Détails, sauvegardes et mises à jour : [docs/exploitation.md](docs/exploitation.md).

## Développer sans Docker

Prérequis : SDK .NET 10, Node.js 22, PostgreSQL 16 (`postgres` / `postgres` sur localhost:5432).

```bash
# API (http://localhost:5080), crée la base et la démo au premier lancement
cd backend && dotnet run --project src/GestionMep.Api

# Front (http://localhost:5173, appelle l'API via un proxy)
cd frontend && npm install && npm run dev
```

Tests : `cd backend && dotnet test` et `cd frontend && npm test`. Lint et formatage : voir [CONTRIBUTING.md](CONTRIBUTING.md).

## Organisation

| Dossier | Contenu |
|---|---|
| `backend/src/GestionMep.Domain` | Modèle et règles métier (disponibilité, graphe de dépendances, phases, droits, recette, résolutions) |
| `backend/src/GestionMep.Api` | API ASP.NET Core, EF Core + PostgreSQL, authentification, flux temps réel |
| `backend/tests` | Tests des règles et de l'API |
| `frontend` | Application React + Vite |
| `docs` | Architecture, contrat d'API, exploitation, spécifications (`specs/`) et maquettes (`maquettes/`) |
| `.github/workflows/ci.yml` | Tests, puis image Docker publiée sur GitHub Container Registry à chaque fusion sur `main` |

## Feuille de route

Livré :

- **Jalon 1** : préparation complète, plan d'étapes avec dépendances, exécution avec preuves, temps réel, journal.
- **Jalon 2** : résolutions et reprise après échec, contrôles et réouverture, retour arrière, pièces jointes, graphe visuel.
- **Jalon 3** : suppléants, circuit d'approbation, go / no-go, point de non-retour, recette, bilan, modèles de MEP et duplication.
- **Jalon 3.1** : responsables techniques distincts du pilote, droits d'édition, avertissements du plan.
- **Jalon 4** : éditeur d'étapes en graphe, recette et sauvegarde par système, profils et expertises.
- **Jalon 4.2** : édition et exécution en trois colonnes, résolutions guidées.

À venir :

- Connexion Microsoft (Entra ID).
- Tableau de bord d'administration et intégrations (Teams, Outlook), kit d'installation pour l'équipe informatique.
