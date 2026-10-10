# Gestion MEP

Application web pour préparer et exécuter les mises en production (MEP). L'équipe de développement décrit l'intervention une fois : systèmes concernés, sauvegardes, procédure pas à pas, contrôles et retour arrière. N'importe quel opérateur peut ensuite dérouler la MEP sans avoir participé au développement, avec un journal complet de ce qui a été fait.

- **Préparation** : plan d'étapes en graphe avec dépendances et étapes parallèles, responsables techniques, approbations, go / no-go.
- **Exécution** : étapes à suivre en temps réel, preuves, prise de main tracée, résolutions guidées en cas d'échec.
- **Sécurité de l'intervention** : sauvegardes, point de non-retour, procédure de retour arrière obligatoire, recette et bilan.
- **Administration** : comptes, paramètres globaux et journal, depuis l'interface ou en ligne de commande.
- **Données protégées** : sauvegarde automatique de la base et des pièces jointes avant chaque mise à jour du schéma ou importation, supprimée seulement si les contrôles internes confirment qu'aucune donnée n'a été perdue.

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

L'image est publiée sur `ghcr.io/soladevsolide/gestion-mep`. Il suffit de `docker-compose.yml` et d'un fichier `.env` tiré de `.env.example`, derrière un proxy inverse HTTPS. La base est migrée automatiquement au démarrage, après une sauvegarde automatique. La procédure complète est dans [Installer](docs/Installer.md).

## Documentation

La documentation est dans [docs/](docs/README.md), recopiée dans le wiki du dépôt :

- [Démarrage rapide](docs/Démarrage-rapide.md) et [cycle de vie d'une MEP](docs/Cycle-de-vie.md) ;
- guides : [préparer](docs/Préparer-une-MEP.md) et [exécuter](docs/Exécuter-une-MEP.md) une MEP, [administrer](docs/Administrer.md) ;
- équipe informatique : [installer](docs/Installer.md), [exploiter](docs/Exploiter.md) ;
- développeurs : [contribuer](CONTRIBUTING.md), [architecture](docs/Architecture.md), [API](docs/API.md).

## Sécurité

Une faille de sécurité se signale en privé, sans ticket public : voir [SECURITY.md](SECURITY.md).

## Licence

Distribué sous licence [MIT](LICENSE) : utilisation, modification et redistribution libres, y compris commerciales.
