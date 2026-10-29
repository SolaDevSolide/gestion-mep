# Droits par rôle

## Les rôles

Deux sortes de rôles se combinent.

**Dans l'application** :

- **Utilisateur** : tout compte actif.
- **Administrateur** : gère les comptes et les paramètres globaux.

**Dans une MEP** :

- **Pilote** : responsable de la mise en production, il décide des changements de phase.
- **Suppléant** : mêmes droits que le pilote. Le pilote ne peut pas être son propre suppléant. Dans une [MEP simplifiée](Préparer-une-MEP.md#mep-complète-ou-simplifiée), toutes les personnes de la MEP sauf le pilote sont suppléantes, sans avoir à les choisir (dès qu'un pilote est désigné), à une exception près : un administrateur ajouté à la MEP parce qu'il a démarré une étape ou pris la main n'y est qu'exécutant.
- **Responsable technique** : développeur ou sachant, il prépare le contenu. Le créateur d'une MEP le devient automatiquement.
- **Approbateur** : approuve ou refuse le plan. Séparation des tâches : le pilote, ses suppléants (toutes les autres personnes d'une MEP simplifiée) et les responsables techniques ne peuvent pas l'être, même avec un autre compte relié au leur ; une personne n'est désignée qu'une fois. Un compte désactivé ne peut plus être désigné. Un approbateur qui devient ensuite suppléant ou responsable technique reste affiché avec la raison (« Bob ne peut pas approuver cette MEP (suppléant du pilote) ») : sa décision ne compte pas et le Go est refusé tant qu'il n'est pas retiré des approbateurs. Qui n'est pas approbateur désigné, ou l'est mais en est empêché, ne peut pas se prononcer (« Droits insuffisants »).
- **Personne de la MEP** : toute personne ajoutée à la MEP (l'API la nomme « participant »). Reliée à un compte, elle peut exécuter des étapes.

Un **éditeur** d'une MEP est son pilote, un suppléant, un responsable technique ou un administrateur.

## Matrice

| Action | Qui |
|---|---|
| Créer une MEP, dupliquer, créer un modèle | Tout utilisateur |
| Voir la vue d'ensemble d'une MEP, le tableau de bord « Suivi en direct », le rapport de fin et ses exports | Tout utilisateur |
| Modifier le contenu en préparation (intervention, personnes, systèmes et leurs environnements, objets impactés, sauvegarde, étapes, retour arrière, recette, approbateurs) | Éditeur |
| Changer le pilote ou les suppléants, ou le compte auquel ils sont reliés | Pilote, suppléant ou administrateur (tout éditeur tant qu'aucun pilote n'est désigné) |
| Compléter les informations de sauvegarde après la préparation (sauvegarde réalisée, restauration testée, preuve) | Pilote, suppléant ou administrateur ; plus personne une fois la MEP clôturée ou annulée |
| Activer ou désactiver le plan de retour arrière | Éditeur, en préparation |
| Passer une MEP simplifiée en complète, ou une MEP complète en simplifiée | Pilote, suppléant ou administrateur, en préparation (tout éditeur tant qu'aucun pilote n'est désigné) |
| Modifier l'échéance de retour arrière (MEP avec plan de retour arrière ; motif obligatoire, sauf première saisie en préparation ; plus possible une fois le retour arrière déclenché ou la MEP clôturée ou annulée) | Éditeur en préparation ; pilote, suppléant ou administrateur en exécution et en recette |
| Cocher « Procédure relue » | Personne de la MEP reliée à un compte |
| Approuver ou refuser | Approbateur désigné, qui n'est ni pilote, ni suppléant, ni responsable technique de la MEP (comptes reliés compris) |
| Go / no-go | Pilote ou suppléant |
| Démarrer une étape disponible | Personne de la MEP reliée à un compte, ou administrateur |
| Terminer une étape, appliquer une résolution | Exécutant de l'étape, pilote ou suppléant |
| Prendre la main sur une étape | Personne de la MEP reliée à un compte, ou administrateur (motif obligatoire) |
| Autoriser le passage d'un point de non-retour, autoriser une reprise | Pilote ou suppléant |
| Revenir en préparation (motif), passer en recette, déclencher le retour arrière, clôturer | Pilote ou suppléant |
| Reporter une MEP (motif, date visée facultative), reprendre la préparation | Pilote ou suppléant, en préparation |
| Annuler une MEP (motif ; état laissé en production après un Go) | Pilote, suppléant ou administrateur, en préparation |
| Revenir de la recette à l'exécution pour corriger (motif) | Pilote ou suppléant, en recette |
| Ajouter une étape corrective | Pilote ou suppléant, en exécution |
| Clôturer en échec (état du service et bilan obligatoires) | Pilote ou suppléant, en retour arrière ; en recette si le retour arrière est impossible |
| Passer le pilotage (motif) | Pilote ou suppléant, de l'exécution au retour arrière |
| Pilotage de secours (motif) | Administrateur qui n'est ni pilote ni suppléant, de l'exécution au retour arrière |
| Ajouter une personne à la MEP après le Go | Pilote, suppléant ou administrateur, de l'exécution au retour arrière |
| Supprimer définitivement une MEP (motif, nom saisi) | Administrateur, pour une MEP qui n'a jamais eu de Go ou un modèle |
| Envoyer un message à une API externe depuis la MEP (« ⋯ Plus d'actions › Envoyer »), renvoyer un envoi en échec | Pilote, suppléant ou administrateur, à toutes les phases |
| Créer, modifier, essayer, activer ou supprimer une connexion à une API externe | Administrateur |
| Saisir un résultat de recette | Personne de la MEP reliée à un compte, ou administrateur |
| Faire refaire un test de recette | Pilote ou suppléant |
| Exporter la recette en Excel | Tout utilisateur qui voit la MEP |
| Exporter en Excel la liste des MEP, la préparation, les étapes, le retour arrière, les objets impactés ou le journal | Tout utilisateur qui voit la MEP |
| Importer la validation de la recette depuis Excel | Pilote ou suppléant, en phase de recette |
| Ajouter une pièce jointe (MEP, étape, résolution) | Tout utilisateur, sauf sur une MEP clôturée ou annulée, dans la limite des [plafonds de stockage](Installer.md#configuration) |
| Supprimer une pièce jointe d'une MEP | Celui qui l'a déposée, pilote, suppléant ou administrateur |
| Créer ou modifier une résolution type | Tout utilisateur |
| Supprimer une résolution type ou une de ses pièces jointes | Administrateur |
| Créer ou modifier un modèle de la bibliothèque de recettes | Tout utilisateur |
| Créer, modifier ou dupliquer une étape modèle, transformer une étape en étape modèle | Tout utilisateur |
| Supprimer une étape modèle | Administrateur |
| Insérer une étape modèle dans le plan | Éditeur de la MEP, en préparation |
| Supprimer un modèle de la bibliothèque de recettes | Administrateur |
| Modifier la communication d'une MEP | Éditeur en préparation ; après le Go, pilote, suppléant ou administrateur ; plus personne une fois la MEP clôturée ou annulée |
| Copier les textes d'une MEP, consulter les modèles de texte | Tout utilisateur qui voit la MEP |
| Créer, modifier, rétablir ou supprimer un modèle de texte d'une équipe, régler son contact support et sa signature | Membre de l'équipe (par l'un de ses comptes reliés) ou administrateur ; les modèles fournis ne se suppriment pas |
| Catalogue des systèmes (fiches : environnements, catégories, objets ; catégories par défaut), critères go / no-go, nombre minimum d'approbations | Administrateur (lecture pour tous) |
| Comptes et console d'administration, équipes, comptes reliés d'une même personne | Administrateur (équipes : lecture pour tous) |
| Modifier son propre profil et son mot de passe | Chacun |

Un administrateur n'a **pas** les droits du pilote : pour donner le Go ou déclencher un retour arrière, il doit être pilote ou suppléant de la MEP. Après le Go, il peut le devenir avec le [pilotage de secours](Cycle-de-vie.md#pilotage-de-secours-et-passation) : motif obligatoire, trace au journal de la MEP et au journal d'administration, bandeau visible de tous. Il peut aussi annuler une MEP en préparation sans la piloter. Démarrer une étape ou prendre la main sur une étape d'une MEP dont il ne fait pas partie l'y ajoute comme **exécutant** seulement, y compris dans une MEP simplifiée : il ne devient pas suppléant (s'il l'était déjà, il le reste). Désigné pilote, il devient une personne de la MEP à part entière.

**Refus** : une action refusée faute de droit répond « Droits insuffisants » (403 dans l'[API](API.md#codes-de-réponse)), avec la phrase qui dit qui peut la faire ; une action permise mais impossible dans l'état de la MEP est une règle métier (422).

## Traçabilité

Chaque action sur une MEP est inscrite dans son journal avec son auteur (nom affiché et identifiant, qui ne change jamais) et l'heure. Un compte relié à un compte principal signe du nom de la personne, avec son propre identifiant. Les droits restent ceux de chaque compte : relier deux comptes ne donne pas à l'un les droits de l'autre dans une MEP. Les modifications des informations de sauvegarde et de l'échéance de retour arrière y gardent l'ancienne et la nouvelle valeur (l'échéance avec son motif, à l'heure du fuseau de la MEP). Les actions d'administration sont dans le journal de la console d'administration, comme le pilotage de secours et la suppression d'une MEP. Une MEP annulée garde son journal ; une MEP supprimée ne laisse qu'une ligne « MEP supprimée » au journal d'administration.
