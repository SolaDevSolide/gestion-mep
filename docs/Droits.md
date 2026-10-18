# Droits par rôle

## Les rôles

Deux sortes de rôles se combinent.

**Dans l'application** :

- **Utilisateur** : tout compte actif.
- **Administrateur** : gère les comptes et les paramètres globaux.

**Dans une MEP** :

- **Pilote** : responsable de la mise en production, il décide des changements de phase.
- **Suppléant** : mêmes droits que le pilote. Le pilote ne peut pas être son propre suppléant. Dans une [MEP simplifiée](Préparer-une-MEP.md#mep-complète-ou-simplifiée), toutes les personnes de la MEP sauf le pilote sont suppléantes, sans avoir à les choisir (dès qu'un pilote est désigné).
- **Responsable technique** : développeur ou sachant, il prépare le contenu. Le créateur d'une MEP le devient automatiquement.
- **Approbateur** : approuve ou refuse le plan. Le pilote ne peut pas l'être, même avec un autre compte relié au sien, et une personne n'est désignée qu'une fois. Un compte désactivé ne peut plus être désigné.
- **Participant** : toute personne de la MEP. Reliée à un compte, elle peut exécuter des étapes.

Un **éditeur** d'une MEP est son pilote, un suppléant, un responsable technique ou un administrateur.

## Matrice

| Action | Qui |
|---|---|
| Créer une MEP, dupliquer, créer un modèle | Tout utilisateur |
| Voir la vue d'ensemble d'une MEP, le tableau de bord « Suivi en direct », le rapport de fin et ses exports | Tout utilisateur |
| Modifier le contenu en préparation (intervention, personnes, systèmes et leurs environnements, objets impactés, sauvegarde, étapes, retour arrière, recette, approbateurs) | Éditeur |
| Changer le pilote ou les suppléants, ou le compte auquel ils sont reliés | Pilote, suppléant ou administrateur (tout éditeur tant qu'aucun pilote n'est désigné) |
| Compléter les informations de sauvegarde après la préparation (sauvegarde réalisée, restauration testée, preuve) | Pilote, suppléant ou administrateur ; plus personne une fois la MEP clôturée |
| Activer ou désactiver le plan de retour arrière | Éditeur, en préparation |
| Passer une MEP simplifiée en complète, ou une MEP complète en simplifiée | Pilote, suppléant ou administrateur, en préparation (tout éditeur tant qu'aucun pilote n'est désigné) |
| Modifier l'échéance de retour arrière, à toute phase (MEP avec plan de retour arrière) | Pilote, suppléant ou administrateur |
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
| Exporter la recette en Excel | Tout utilisateur qui voit la MEP |
| Exporter en Excel la liste des MEP, la préparation, les étapes, le retour arrière, les objets impactés ou le journal | Tout utilisateur qui voit la MEP |
| Importer la validation de la recette depuis Excel | Pilote ou suppléant, en phase de recette |
| Ajouter une pièce jointe (MEP, étape, résolution) | Tout utilisateur, sauf sur une MEP clôturée, dans la limite des [plafonds de stockage](Installer.md#configuration) |
| Supprimer une pièce jointe d'une MEP | Celui qui l'a déposée, pilote, suppléant ou administrateur |
| Créer ou modifier une résolution type | Tout utilisateur |
| Supprimer une résolution type ou une de ses pièces jointes | Administrateur |
| Créer ou modifier un modèle de la bibliothèque de recettes | Tout utilisateur |
| Créer, modifier ou dupliquer une étape modèle, transformer une étape en étape modèle | Tout utilisateur |
| Supprimer une étape modèle | Administrateur |
| Insérer une étape modèle dans le plan | Éditeur de la MEP, en préparation |
| Supprimer un modèle de la bibliothèque de recettes | Administrateur |
| Modifier la communication d'une MEP | Éditeur en préparation ; après le Go, pilote, suppléant ou administrateur ; plus personne une fois la MEP clôturée |
| Copier les textes d'une MEP, consulter les modèles de texte | Tout utilisateur qui voit la MEP |
| Créer, modifier, rétablir ou supprimer un modèle de texte d'une équipe, régler son contact support et sa signature | Membre de l'équipe (par l'un de ses comptes reliés) ou administrateur ; les modèles fournis ne se suppriment pas |
| Catalogue des systèmes (fiches : environnements, catégories, objets ; catégories par défaut), critères go / no-go, nombre minimum d'approbations | Administrateur (lecture pour tous) |
| Comptes et console d'administration, équipes, comptes reliés d'une même personne | Administrateur (équipes : lecture pour tous) |
| Modifier son propre profil et son mot de passe | Chacun |

Un administrateur n'a **pas** les droits du pilote : pour donner le Go ou déclencher un retour arrière, il doit être pilote ou suppléant de la MEP.

## Traçabilité

Chaque action sur une MEP est inscrite dans son journal avec son auteur (nom affiché et identifiant, qui ne change jamais) et l'heure. Un compte relié à un compte principal signe du nom de la personne, avec son propre identifiant. Les droits restent ceux de chaque compte : relier deux comptes ne donne pas à l'un les droits de l'autre dans une MEP. Les modifications des informations de sauvegarde y gardent l'ancienne et la nouvelle valeur. Les actions d'administration sont dans le journal de la console d'administration.
