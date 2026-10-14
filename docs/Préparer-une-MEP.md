# Préparer une MEP

Cette page s'adresse aux développeurs et aux **responsables techniques** qui décrivent une intervention. L'objectif : qu'un opérateur qui n'a pas travaillé sur le changement puisse la dérouler seul, et que l'équipe sache revenir en arrière.

Peuvent modifier une MEP en préparation : son pilote, ses suppléants, ses responsables techniques et les administrateurs (voir [Droits par rôle](Droits.md)).

## Créer la MEP

Dans « Mises en production » :

- **Nouvelle MEP** : saisir l'outil ou l'application concerné et l'objet de l'intervention. Le créateur devient responsable technique de la MEP (pas pilote).
- **Nouvelle MEP depuis un modèle** : choisir un modèle et l'environnement, puis « Créer la MEP ». Voir [Partir d'une MEP existante](#partir-dune-mep-existante).
- **Nouveau modèle** (vue « Modèles ») : comme une nouvelle MEP, mais le résultat est un modèle. Voir [Modèles](#modèles).

## Les onglets d'une MEP

Une MEP s'organise en quatre onglets :

- **Préparation** : la configuration de la MEP, en huit sections (ci-dessous) ;
- **Plan** : ce qui sera déroulé, en trois sous-onglets affichés sous la barre d'onglets, chacun avec son nombre d'éléments : **Étapes** (le graphe du déploiement), **Retour arrière** (sa fiche et ses étapes) et **Recette** (les fiches de recette) ;
- **Exécution** : le déroulé du jour J (absent pour un modèle) ;
- **Journal** : tout ce qui a été fait, par qui et quand.

La MEP s'ouvre sur l'onglet qui correspond à sa phase : Préparation, puis Exécution, puis Plan › Recette pendant la recette et Plan › Retour arrière pendant un retour arrière. Les anciens liens vers les onglets Étapes, Retour arrière ou Recette mènent au bon sous-onglet de Plan.

## L'onglet Préparation

Il est découpé en huit sections. Chacune affiche son état (complète ou à compléter), et un compteur indique combien sont complètes.

1. **Intervention** : outil, objet, version actuelle et version cible, environnement, fuseau horaire, indisponibilité prévue, impacts attendus, pilote et suppléants, case **« Plan de retour arrière »** avec, en dessous, l'**échéance de retour arrière** (facultative), et option « Bilan de clôture ». Les dates de début et de fin ne se saisissent pas : ce sont celles du Go et de la clôture. Voir [Retour arrière facultatif](#retour-arrière-facultatif).
2. **Personnes** : toutes les personnes de l'intervention. Relier chaque personne à son compte permet de pré-remplir son nom, son rôle et son contact, et lui donne le droit d'exécuter des étapes.
3. **Systèmes** : les systèmes touchés, ajoutés depuis le catalogue global ou librement.
4. **Responsables techniques** : les développeurs et sachants, avec ce qu'ils ont développé, leur disponibilité le jour J et leur contact. L'application suggère les comptes dont le profil indique une expertise sur les systèmes de la MEP.
5. **Livrables** : paquets, versions ou artefacts déployés.
6. **Sauvegarde** : pour **chaque système**, indiquer si une sauvegarde est requise. Si oui : avant ou pendant la MEP, quoi sauvegarder, et obligatoirement l'**emplacement** de la sauvegarde et la **méthode de récupération** (comment la restaurer). Si non : une justification. Une sauvegarde faite pendant la MEP doit correspondre à une étape de type « Sauvegarde » sur ce système. S'y ajoutent les informations communes : responsable, copies, test de restauration.
7. **Validation** : relecture de la procédure, approbations, et rappel du Go.
8. **Pièces jointes** : documents utiles (procédures, captures, scripts).

![Onglet Préparation et ses huit sections](images/preparation.png)

## Le plan d'étapes (Plan › Étapes)

Le plan de déploiement se dessine comme un graphe, de haut en bas :

- chaque **ligne** contient des étapes qui peuvent se dérouler **en parallèle** ;
- par défaut, une étape attend toutes celles de la ligne au-dessus ; on peut aussi tracer un lien particulier en tirant le rond du bas d'une étape vers celle qui doit l'attendre ;
- **glisser-déposer** une étape la déplace sur une autre ligne ou entre deux étapes ;
- « + Étape » au bout d'une ligne et « + Nouvelle ligne » en bas ajoutent des étapes ;
- un lien remonte toujours vers une ligne au-dessus : le plan ne peut pas boucler.

![Graphe des étapes : lignes parallèles et barrière du point de non-retour](images/graphe-des-etapes.png)

Cliquer une étape ouvre son **espace d'édition** :

- **action** (titre court) et **systèmes** concernés ;
- **criticité**, **durée estimée** (facultative), **type** (Standard ou Sauvegarde). Une étape de type Sauvegarde indique aussi, obligatoirement, l'**emplacement** de la sauvegarde et la **méthode de récupération** ;
- **fin de l'étape** : **test unitaire** (par défaut) ou **simple vérification** (voir ci-dessous) ;
- **instructions** : blocs de texte et blocs de code. Les blocs de code s'affichent avec un bouton « Copier » le jour J : y mettre les commandes exactes ;
- **résultat attendu** : ce que l'opérateur doit constater pour déclarer l'étape réussie. Pour un test unitaire, c'est l'instruction de test ;
- **liens** : étapes préalables et étapes débloquées ;
- **résolutions types** liées à l'étape (voir plus bas) ;
- **point de non-retour** : à cocher pour l'étape après laquelle on ne peut plus revenir en arrière ;
- pièces jointes de l'étape.

![Espace d'édition d'une étape : systèmes, criticité, blocs Texte et Code](images/edition-etape.png)

Les codes d'étape (`E0001` pour le déploiement, `R0001` pour le retour arrière) ne sont jamais renumérotés.

Chaque étape se termine par :

- un **test unitaire** (choix par défaut) : le résultat attendu sert d'instruction de test, et l'exécutant saisit le **résultat constaté** en terminant l'étape. On sait ainsi, après coup, quels tests ont été faits et ce qu'ils ont donné (onglet Journal, [API](API.md) et export de la MEP) ;
- une **simple vérification** : l'exécutant confirme avoir obtenu le résultat attendu, sans saisir de résultat.

Les étapes des MEP créées avant ce choix sont des tests unitaires, sans résultat constaté pour celles déjà terminées.

Le responsable d'une étape n'est pas choisi à l'avance : c'est celui qui la démarre le jour J.

## Le plan de retour arrière (Plan › Retour arrière)

Exigé pour le Go tant que la case « Plan de retour arrière » est cochée (c'est le défaut). Il comprend :

- la **fiche du plan** : quand déclencher le retour arrière, durée testée, version ou copies à restaurer, données créées depuis la sauvegarde, que faire si le retour arrière échoue ;
- les **étapes de retour arrière**, éditées comme celles du déploiement. Elles ne peuvent être ni point de non-retour, ni de type Sauvegarde.

### Retour arrière facultatif

Certains environnements (une préproduction qu'on réinstalle à volonté, par exemple) n'ont pas besoin de retour arrière. Dans la section « Intervention », la case **« Plan de retour arrière »** est cochée par défaut, y compris pour les MEP créées avant son apparition.

- **Échéance facultative** : sous la case, l'échéance de retour arrière peut rester vide. Sans échéance, pas de compte à rebours ni de déclenchement automatique ; le pilote peut toujours déclencher le retour arrière à la main. Renseignée, elle doit être future pour donner le Go.
- **Décocher la case** ouvre une fenêtre de confirmation qui en rappelle les conséquences ; la désactivation n'est prise en compte qu'après confirmation, puis « Enregistrer » :
  - le sous-onglet Plan › Retour arrière est masqué ; les étapes et la fiche déjà saisies sont gardées et reviennent si on recoche la case ;
  - « Vérifier le plan » ne réclame plus ni plan, ni échéance de retour arrière pour le Go ;
  - aucun retour arrière ne peut être déclenché : ni automatiquement (échéance, résolution en échec), ni à la main. Une résolution en échec laisse l'étape en échec, et le pilote décide de la suite (reprise).
- **Recocher la case** se fait sans confirmation.

L'activation et la désactivation sont inscrites au journal de la MEP, avec leur auteur et leur date. Le choix ne se modifie qu'en préparation, par un éditeur ; il est repris par la duplication et par les MEP créées depuis un modèle.

Le point de non-retour reste porté par les étapes de déploiement (voir [Cycle de vie](Cycle-de-vie.md#point-de-non-retour)).

## Les fiches de recette (Plan › Recette)

La recette vérifie, après le déploiement, que tout fonctionne. Elle est organisée par système : pour chaque système, ajouter une ou plusieurs fiches (« Ajouter une recette »), chacune faite d'étapes de test avec des instructions et un résultat attendu. Le jour J, les responsables techniques du système servent de contact.

## Préparer à plusieurs

Plusieurs personnes peuvent préparer la même MEP en même temps :

- en haut de la MEP, **« Sur cette MEP : »** montre les avatars des autres personnes qui l'ont ouverte, chacune avec sa couleur ;
- l'élément qu'une autre personne est en train de modifier (une étape ouverte en édition, une section de la préparation, la fiche du retour arrière, une fiche de recette) est **encadré de sa couleur**, avec l'étiquette « Prénom modifie ». Dans le sommaire de la préparation, son avatar apparaît à côté de la section. C'est une information : rien n'est verrouillé ;
- les modifications enregistrées par les autres s'affichent aussitôt, sans recharger. Votre saisie en cours est conservée : si la section que vous modifiez a changé entre-temps, un avertissement vous laisse reprendre la version à jour ou garder votre saisie ;
- une personne qui ferme la MEP (ou son navigateur) disparaît d'elle-même de la liste.

Les formulaires s'enregistrent avec leur bouton « Enregistrer ». Quitter une vue avec des modifications non enregistrées (autre onglet ou sous-onglet, autre MEP, autre page du menu) ouvre la fenêtre **« Quitter sans enregistrer ? »**, qui liste ce qui serait perdu : « Rester » revient à la saisie, « Quitter sans enregistrer » l'abandonne. Fermer ou recharger l'onglet du navigateur affiche l'avertissement habituel du navigateur.

## Les résolutions types

Une résolution type est une fiche de dépannage réutilisable pour un problème connu : symptômes, diagnostic, corrections dans l'ordre, vérification finale. Elle peut porter sur plusieurs systèmes.

- Les lier aux étapes qui risquent d'échouer, depuis l'espace d'édition de l'étape. Les résolutions des mêmes systèmes sont proposées en premier.
- « + Créer une nouvelle fiche de résolution » depuis une étape crée la fiche et la lie à l'étape.
- Toutes les résolutions sont dans « Paramètres › Résolutions types », avec l'historique de leurs versions. Tout le monde peut en créer et en modifier.

Le jour J, une étape en échec propose ses résolutions liées et guide l'opérateur pas à pas (voir [Exécuter une MEP](Exécuter-une-MEP.md#quand-une-étape-échoue)).

## Vérifier le plan

Le bouton « Vérifier le plan » liste :

- les **anomalies**, qui bloquent le Go : échéance de retour arrière passée, pilote manquant ou non relié à un compte, plan de déploiement vide, plan de retour arrière vide ou sans « quand le déclencher » (si la case « Plan de retour arrière » est cochée), graphe invalide, étape incomplète (action, système, criticité, instructions, résultat attendu ; emplacement et méthode de récupération pour une étape de sauvegarde), sauvegarde non renseignée ou non justifiée, sauvegarde requise sans emplacement ou sans méthode de récupération, sauvegarde pendant la MEP sans étape de sauvegarde, recette absente ou incomplète, procédure non relue, approbations manquantes ou refusées ;
- les **avertissements**, qui ne bloquent pas : aucun responsable technique, responsable technique absent le jour J, pilote qui est aussi responsable technique, aucun système sauvegardé, système sans recette.

![« Vérifier le plan » : il manque une approbation](images/verifier-le-plan.png)

## Relecture et approbations

Dans la section Validation :

- **Procédure relue** : à cocher par une personne de la MEP reliée à un compte, idéalement un opérateur qui n'a pas écrit le plan.
- **Approbations** : les approbateurs désignés approuvent ou refusent (commentaire obligatoire en cas de refus). Le pilote ne peut pas être approbateur. Le nombre minimum d'approbations est un paramètre global (1 par défaut). Un approbateur ne peut se prononcer que lorsqu'il ne reste plus d'autre anomalie.
- Toute modification du contenu après une approbation l'efface : « Approbations à refaire ».

![Section Validation vue par l'approbatrice](images/approbation.png)

Quand le plan est complet, le pilote ou un suppléant donne le Go (voir [Cycle de vie](Cycle-de-vie.md#go--no-go--passage-en-exécution)).

## Partir d'une MEP existante

- **Dupliquer** (bouton de la liste ou de l'en-tête) copie une MEP : identité, personnes, pilote et suppléants, systèmes et leur sauvegarde, responsables techniques, livrables, étapes et liens, résolutions liées, plan de retour arrière (et le choix de l'avoir ou non), fiches de recette (sans les résultats) et approbateurs (sans leurs décisions). Ne sont pas copiés : l'échéance, la relecture, le Go, les résultats d'exécution (dont les résultats constatés des tests unitaires), le bilan et les pièces jointes. L'environnement peut être changé au passage.
- Cocher **« En faire un modèle »** à la duplication crée un modèle, à réutiliser avec « Nouvelle MEP depuis un modèle ».

## Modèles

Un modèle se prépare comme une MEP (intervention, personnes, systèmes, responsables techniques, livrables, sauvegarde, étapes, retour arrière, fiches de recette, approbateurs, pièces jointes), mais il ne s'exécute jamais. Ce qui se décide pour chaque intervention n'y apparaît donc pas :

| Masqué dans un modèle | Où le renseigner |
|---|---|
| Phase, dates de démarrage et de fin | Fixées par le Go et la clôture de la MEP |
| Pilote et suppléants | Section « Intervention » de la MEP |
| Échéance de retour arrière | Section « Intervention » de la MEP |
| Disponibilité le jour J des responsables techniques | Section « Responsables techniques » de la MEP |
| Date de réalisation de la sauvegarde, test de restauration | Section « Sauvegarde » de la MEP |
| Relecture de la procédure, démarrage autorisé, décisions d'approbation | Section « Validation » de la MEP (les approbateurs, eux, se choisissent dans le modèle) |
| Bouton Go / no-go et onglet Exécution | MEP en préparation, puis en exécution |

« Vérifier le plan » ne réclame dans un modèle ni échéance, ni pilote, ni relecture, ni approbation. Le bouton **« Créer une MEP depuis ce modèle »**, en haut du modèle, crée la MEP et l'ouvre.
