# Gestion MEP

Application web pour préparer et exécuter les mises en production (MEP). L'équipe de développement décrit l'intervention une fois : systèmes concernés, sauvegardes, procédure pas à pas, contrôles et retour arrière. N'importe quel opérateur peut ensuite dérouler la MEP sans avoir participé au développement, avec un journal complet de ce qui a été fait.

- **Préparation** : plan d'étapes en graphe avec dépendances et étapes parallèles, bibliothèque d'étapes modèles à insérer dans le graphe, environnements par système et par étape, objets impactés choisis dans la fiche de chaque système (échange en Excel), responsables techniques, approbations, go / no-go ; MEP complète, ou simplifiée pour une petite intervention (suppléants implicites, étapes sans durée ni criticité, toutes les résolutions des systèmes proposées le jour J).
- **Travail à plusieurs** : on voit qui a ouvert la MEP et quel élément chacun modifie, les changements des autres arrivent sans recharger, et aucune saisie n'est perdue sans confirmation.
- **Communication** : référence de la demande, risque, bénéfices, créneau annoncé et validation par les utilisateurs ; modèles de texte par équipe (annonce, rappel, demande de MEP, compte rendu) remplis avec les informations de la MEP et copiés en un clic, mis en forme ou en texte simple.
- **Exécution** : étapes à suivre en temps réel, preuves, prise de main tracée, résolutions guidées en cas d'échec.
- **Sécurité de l'intervention** : sauvegardes, point de non-retour, procédure de retour arrière et échéance surveillée (facultatives pour les environnements qui n'en ont pas besoin), recette (par système, avec bibliothèque de fiches types et validation par les métiers dans Excel) et bilan.
- **Connexion Microsoft (SSO)** facultative, réglée depuis la console ou en ligne de commande ; le mot de passe local reste possible.
- **Administration** : comptes et expertises, équipes (filtre des personnes et des MEP), comptes d'une même personne reliés sans rien perdre, catalogue des systèmes (environnements, catégories et objets connus de chacun), critères go / no-go, résolutions types (export et import JSON), bibliothèques d'étapes et de recettes (par système, triées par utilisations), réglages et journal, depuis l'interface ou en ligne de commande (`gestion-mep`, sortie JSON pour les scripts) ; export d'une MEP complète avec son journal pour l'archive ou l'audit ; indicateur de nouvelle version et, si l'équipe informatique l'active, mise à jour en un clic ou par une commande.
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

Ouvrir http://localhost:8080 et cliquer sur un compte de démonstration : `alice` (administratrice), `bob` ou `claire` (mot de passe `demo` pour le formulaire). Neuf MEP de démonstration couvrent chaque état : préparation incomplète ou prête pour le Go, exécution, recette, retour arrière en cours, clôturées réussie et repliée, plus une MEP simplifiée en cours d'exécution ([Démarrage rapide](docs/Démarrage-rapide.md#mep-de-démonstration)). Le bouton « Réinitialiser la démonstration » du bandeau (ou `gestion-mep demo reinitialiser --oui`) remet la base dans son état initial ; `docker compose down -v` efface tout.

## Installer sur un serveur

L'image est publiée sur `ghcr.io/soladevsolide/gestion-mep`, avec un numéro par version (`1.0.0`). Il suffit de `docker-compose.yml` et d'un fichier `.env` tiré de `.env.example` (lisible par son seul propriétaire), derrière un proxy inverse HTTPS. La base est migrée automatiquement au démarrage, après une sauvegarde automatique. La procédure complète est dans [Installer](docs/Installer.md).

Sans Docker, chaque version publie aussi une archive pour Linux (service systemd) et pour Windows Server (service Windows), avec un script qui installe, met à jour et revient tout seul à la version précédente en cas d'échec : voir [Installer sans Docker](docs/Installer-sans-Docker.md). Il faut alors un serveur PostgreSQL 16.

## Documentation

La documentation est dans [docs/](docs/README.md), recopiée dans le wiki du dépôt :

- [Démarrage rapide](docs/Démarrage-rapide.md) et [cycle de vie d'une MEP](docs/Cycle-de-vie.md) ;
- guides : [préparer](docs/Préparer-une-MEP.md) et [exécuter](docs/Exécuter-une-MEP.md) une MEP, [modèles de texte](docs/Modèles-de-texte.md), [administrer](docs/Administrer.md) ;
- équipe informatique : [installer](docs/Installer.md) (ou [sans Docker](docs/Installer-sans-Docker.md)), [exploiter](docs/Exploiter.md) ;
- développeurs : [contribuer](CONTRIBUTING.md), [architecture](docs/Architecture.md), [API](docs/API.md).

## Signaler un problème ou proposer une idée

Par les [issues](https://github.com/SolaDevSolide/gestion-mep/issues/new/choose) du dépôt, aussi accessibles depuis le menu utilisateur de l'application.

## Sécurité

Une faille de sécurité se signale en privé, sans ticket public : voir [SECURITY.md](SECURITY.md).

## Licence

Distribué sous licence [MIT](LICENSE) : utilisation, modification et redistribution libres, y compris commerciales.
