# Gestion MEP

Application web pour préparer et exécuter les mises en production (MEP). L'équipe de développement décrit l'intervention une fois : systèmes concernés, sauvegardes, procédure pas à pas, contrôles et retour arrière. N'importe quel opérateur peut ensuite dérouler la MEP sans avoir participé au développement, avec un journal complet de ce qui a été fait.

- **Préparation** : plan d'étapes en graphe avec dépendances et étapes parallèles, responsables techniques, approbations, go / no-go.
- **Exécution** : étapes à suivre en temps réel, preuves, prise de main tracée, résolutions guidées en cas d'échec.
- **Sécurité de l'intervention** : sauvegardes, point de non-retour, procédure de retour arrière obligatoire, recette et bilan.
- **Administration** : comptes, paramètres globaux et journal, depuis l'interface ou en ligne de commande.

React, .NET 10 et PostgreSQL, livrés dans une image Docker.

## Essayer en local

Prérequis : Docker et Docker Compose.

```bash
git clone https://github.com/SolaDevSolide/gestion-mep.git
cd gestion-mep
echo "DEMO=true" > .env
docker compose up --build
```

Ouvrir http://localhost:8080 et se connecter avec `alice` (administratrice), `bob` ou `claire`, mot de passe `demo`. `docker compose down -v` remet la base à zéro.

## Installer sur un serveur

L'image est publiée sur `ghcr.io/soladevsolide/gestion-mep`. Il suffit de `docker-compose.yml` et d'un fichier `.env` tiré de `.env.example`, derrière un proxy inverse HTTPS. La base est migrée automatiquement au démarrage.

La procédure complète (configuration, HTTPS, sauvegardes, mises à jour, commandes d'administration) est dans le [guide d'exploitation](docs/exploitation.md).

## Documentation

| Document | Pour qui |
|---|---|
| [Guide d'exploitation](docs/exploitation.md) | Équipe informatique qui installe et maintient l'application |
| [Contribuer](CONTRIBUTING.md) | Développeurs : lancer sans Docker, tests, conventions, migrations |
| [Architecture](docs/architecture.md) | Organisation du code et principes |
| [API](docs/api.md) | Contrat de l'API ([OpenAPI](docs/openapi.json)) |
