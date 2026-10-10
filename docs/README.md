# Documentation de Gestion MEP

Gestion MEP est une application web pour préparer et exécuter les mises en production (MEP). L'équipe qui a développé un changement décrit l'intervention une fois : systèmes touchés, sauvegardes, étapes à suivre, contrôles et retour arrière. Le jour J, n'importe quel opérateur peut dérouler la MEP pas à pas sans avoir participé au développement. Tout est tracé dans un journal, et l'application déclenche elle-même le retour arrière si l'intervention dépasse son échéance.

Elle s'installe sur un serveur avec Docker, sans autre dépendance que PostgreSQL, fourni dans le même fichier.

## Je veux…

| Je veux… | Page |
|---|---|
| Essayer l'application sur mon poste en cinq minutes | [Démarrage rapide](Démarrage-rapide.md) |
| Comprendre les étapes de vie d'une MEP | [Cycle de vie d'une MEP](Cycle-de-vie.md) |
| Préparer une MEP (je suis développeur ou responsable technique) | [Préparer une MEP](Préparer-une-MEP.md) |
| Conduire ou exécuter une MEP le jour J (je suis pilote ou opérateur) | [Exécuter une MEP](Exécuter-une-MEP.md) |
| Gérer les comptes et les paramètres (je suis administrateur) | [Administrer](Administrer.md) |
| Installer l'application sur un serveur | [Installer](Installer.md) |
| Mettre à jour, sauvegarder, restaurer, gérer les comptes en ligne de commande | [Exploiter](Exploiter.md) |
| Savoir qui a le droit de faire quoi | [Droits par rôle](Droits.md) |
| Trouver le sens d'un mot | [Glossaire](Glossaire.md) |
| Résoudre un problème | [FAQ et dépannage](FAQ-et-dépannage.md) |
| Modifier le code | [Contribuer](../CONTRIBUTING.md), [Architecture](Architecture.md), [API](API.md) |

## En bref

- **Préparation** : identité de l'intervention, personnes, systèmes, sauvegarde de chaque système, plan d'étapes en graphe (les étapes d'une même ligne peuvent se dérouler en parallèle), plan de retour arrière, fiches de recette, relecture et approbations.
- **Exécution** : chacun voit ce qui est disponible, démarre une étape, la termine avec une preuve, ou applique une résolution guidée en cas d'échec. Les écrans se mettent à jour en direct pour tous.
- **Sécurité de l'intervention** : go / no-go sur critères, point de non-retour soumis à l'accord du pilote, échéance de retour arrière surveillée en permanence, recette avant clôture, bilan.
- **Traçabilité** : chaque action est horodatée et attribuée, y compris quand quelqu'un prend la main sur l'étape d'un autre.
