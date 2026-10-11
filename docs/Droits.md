# Droits par rôle

## Les rôles

Deux sortes de rôles se combinent.

**Dans l'application** :

- **Utilisateur** : tout compte actif.
- **Administrateur** : gère les comptes et les paramètres globaux.

**Dans une MEP** :

- **Pilote** : responsable de la mise en production, il décide des changements de phase.
- **Suppléant** : mêmes droits que le pilote. Le pilote ne peut pas être son propre suppléant.
- **Responsable technique** : développeur ou sachant, il prépare le contenu. Le créateur d'une MEP le devient automatiquement.
- **Approbateur** : approuve ou refuse le plan. Le pilote ne peut pas l'être.
- **Participant** : toute personne de la MEP. Reliée à un compte, elle peut exécuter des étapes.

Un **éditeur** d'une MEP est son pilote, un suppléant, un responsable technique ou un administrateur.

## Matrice

| Action | Qui |
|---|---|
| Créer une MEP, dupliquer, créer un modèle | Tout utilisateur |
| Modifier le contenu en préparation (intervention, personnes, systèmes, sauvegarde, étapes, retour arrière, recette, approbateurs) | Éditeur |
| Changer le pilote ou les suppléants, ou le compte auquel ils sont reliés | Pilote, suppléant ou administrateur (tout éditeur tant qu'aucun pilote n'est désigné) |
| Compléter les informations de sauvegarde après la préparation (sauvegarde réalisée, restauration testée, preuve) | Pilote, suppléant ou administrateur ; plus personne une fois la MEP clôturée |
| Modifier l'échéance de retour arrière, à toute phase | Pilote, suppléant ou administrateur |
| Cocher « Procédure relue » | Participant relié à un compte |
| Approuver ou refuser | Approbateur désigné |
| Go / no-go | Pilote ou suppléant |
| Démarrer une étape disponible | Participant relié à un compte, ou administrateur |
| Terminer une étape, appliquer une résolution | Exécutant de l'étape, pilote ou suppléant |
| Prendre la main sur une étape | Participant relié à un compte, ou administrateur (motif obligatoire) |
| Autoriser le passage d'un point de non-retour, autoriser une reprise | Pilote ou suppléant |
| Revenir en préparation, passer en recette, déclencher le retour arrière, clôturer | Pilote ou suppléant |
| Saisir un résultat de recette | Participant relié à un compte, ou administrateur |
| Faire refaire un test de recette | Pilote ou suppléant |
| Ajouter une pièce jointe (MEP, étape, résolution) | Tout utilisateur, sauf sur une MEP clôturée, dans la limite des [plafonds de stockage](Installer.md#configuration) |
| Supprimer une pièce jointe d'une MEP | Celui qui l'a déposée, pilote, suppléant ou administrateur |
| Créer ou modifier une résolution type | Tout utilisateur |
| Supprimer une résolution type ou une de ses pièces jointes | Administrateur |
| Catalogue des systèmes, critères go / no-go, nombre minimum d'approbations | Administrateur (lecture pour tous) |
| Comptes et console d'administration | Administrateur |
| Modifier son propre profil et son mot de passe | Chacun |

Un administrateur n'a **pas** les droits du pilote : pour donner le Go ou déclencher un retour arrière, il doit être pilote ou suppléant de la MEP.

## Traçabilité

Chaque action sur une MEP est inscrite dans son journal avec son auteur (nom affiché et identifiant, qui ne change jamais) et l'heure. Les modifications des informations de sauvegarde y gardent l'ancienne et la nouvelle valeur. Les actions d'administration sont dans le journal de la console d'administration.
