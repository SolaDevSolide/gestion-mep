# Gestion MEP

Application web pour préparer et exécuter les mises en production (MEP) : le développeur décrit l'intervention, les personnes et systèmes, les livrables, la sauvegarde et le plan d'étapes ; n'importe quel opérateur peut ensuite réaliser la MEP pas à pas, avec dépendances, preuves, échéance de retour arrière et journal d'audit.

Elle reprend le modèle Excel `Modele_MEP_test_navigation.xlsm` et ses règles (voir [docs/api.md](docs/api.md) pour le contrat d'API).

## Démarrer en local (Docker Desktop)

```bash
git clone https://github.com/SolaDevSolide/gestion-mep.git
cd gestion-mep
docker compose up --build
```

Ouvrir http://localhost:8080. Au premier démarrage, la base est créée avec une MEP de démonstration et trois comptes : `alice` (pilote, admin), `bob`, `claire`, mot de passe `demo`.

Remettre la base à zéro : `docker compose down -v`.

## Déployer sur un autre environnement

Seuls Docker et Docker Compose sont nécessaires.

1. Copier `docker-compose.yml` et `.env.example` (renommé `.env`) sur le serveur.
2. Dans `.env` : `DEMO=false`, un mot de passe PostgreSQL, un compte `ADMIN_LOGIN` / `ADMIN_MOT_DE_PASSE`, et `MEP_IMAGE=ghcr.io/soladevsolide/gestion-mep:latest` pour utiliser l'image publiée par la CI.
3. `docker compose pull && docker compose up -d`.

La base est migrée automatiquement au démarrage. Placer un reverse proxy HTTPS (IIS, nginx, Traefik…) devant le port 8080.

## Développer sans Docker

Prérequis : SDK .NET 10, Node.js 22, PostgreSQL 16 (`postgres` / `postgres` sur localhost:5432).

```bash
# API (http://localhost:5080), crée la base et la démo au premier lancement
cd backend && dotnet run --project src/GestionMep.Api

# Front (http://localhost:5173, appelle l'API via un proxy)
cd frontend && npm install && npm run dev
```

Tests : `cd backend && dotnet test` et `cd frontend && npm test`.

## Organisation

| Dossier | Contenu |
|---|---|
| `backend/src/GestionMep.Domain` | Modèle et règles métier (disponibilité, graphe de dépendances, phases, transitions) |
| `backend/src/GestionMep.Api` | API ASP.NET Core, EF Core + PostgreSQL, authentification, flux temps réel |
| `backend/tests` | Tests des règles et de l'API |
| `frontend` | Application React + Vite |
| `docs` | Contrat d'API |
| `.github/workflows/ci.yml` | Tests, puis image Docker publiée sur GitHub Container Registry à chaque fusion sur `main` |

## Feuille de route

- **Jalon 1** (ce dépôt) : préparation complète, plan d'étapes avec dépendances, exécution avec preuves, temps réel, journal.
- **Jalon 2** : fiches de résolution et reprise après échec, contrôles et réouverture, retour arrière, pièces jointes, graphe visuel.
- **Jalon 3** : équipes et modèles de MEP, SSO Microsoft (Entra ID), export PDF, notifications.
