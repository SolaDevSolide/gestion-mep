# Préparer une MEP

Cette page s'adresse aux développeurs et aux **responsables techniques** qui décrivent une intervention. L'objectif : qu'un opérateur qui n'a pas travaillé sur le changement puisse la dérouler seul, et que l'équipe sache revenir en arrière.

Peuvent modifier une MEP en préparation : son pilote, ses suppléants, ses responsables techniques et les administrateurs (voir [Droits par rôle](Droits.md)).

## Créer la MEP

Dans « Mises en production » :

- **Nouvelle MEP** : saisir l'outil ou l'application concerné et l'objet de l'intervention. Le créateur devient responsable technique de la MEP (pas pilote).
- **Nouvelle MEP depuis un modèle** : choisir un modèle et l'environnement, puis « Créer la MEP ». Voir [Partir d'une MEP existante](#partir-dune-mep-existante).

## L'onglet Préparation

Il est découpé en huit sections. Chacune affiche son état (complète ou à compléter), et un compteur indique combien sont complètes.

1. **Intervention** : outil, objet, version actuelle et version cible, environnement, fuseau horaire, indisponibilité prévue, impacts attendus, pilote et suppléants, **échéance de retour arrière** (obligatoire et future pour donner le Go) et option « Bilan de clôture ». Les dates de début et de fin ne se saisissent pas : ce sont celles du Go et de la clôture.
2. **Personnes** : toutes les personnes de l'intervention. Relier chaque personne à son compte permet de pré-remplir son nom, son rôle et son contact, et lui donne le droit d'exécuter des étapes.
3. **Systèmes** : les systèmes touchés, ajoutés depuis le catalogue global ou librement.
4. **Responsables techniques** : les développeurs et sachants, avec ce qu'ils ont développé, leur disponibilité le jour J et leur contact. L'application suggère les comptes dont le profil indique une expertise sur les systèmes de la MEP.
5. **Livrables** : paquets, versions ou artefacts déployés.
6. **Sauvegarde** : pour **chaque système**, indiquer si une sauvegarde est requise. Si oui : avant ou pendant la MEP, et quoi sauvegarder. Si non : une justification. Une sauvegarde faite pendant la MEP doit correspondre à une étape de type « Sauvegarde » sur ce système. S'y ajoutent les informations communes : responsable, copies, test de restauration.
7. **Validation** : relecture de la procédure, approbations, et rappel du Go.
8. **Pièces jointes** : documents utiles (procédures, captures, scripts).

## Le plan d'étapes (onglet Étapes)

Le plan de déploiement se dessine comme un graphe, de haut en bas :

- chaque **ligne** contient des étapes qui peuvent se dérouler **en parallèle** ;
- par défaut, une étape attend toutes celles de la ligne au-dessus ; on peut aussi tracer un lien particulier en tirant le rond du bas d'une étape vers celle qui doit l'attendre ;
- **glisser-déposer** une étape la déplace sur une autre ligne ou entre deux étapes ;
- « + Étape » au bout d'une ligne et « + Nouvelle ligne » en bas ajoutent des étapes ;
- un lien remonte toujours vers une ligne au-dessus : le plan ne peut pas boucler.

Cliquer une étape ouvre son **espace d'édition** :

- **action** (titre court) et **systèmes** concernés ;
- **criticité**, **durée estimée** (facultative), **type** (Standard ou Sauvegarde) ;
- **instructions** : blocs de texte et blocs de code. Les blocs de code s'affichent avec un bouton « Copier » le jour J : y mettre les commandes exactes ;
- **résultat attendu** : ce que l'opérateur doit constater pour déclarer l'étape réussie ;
- **liens** : étapes préalables et étapes débloquées ;
- **résolutions types** liées à l'étape (voir plus bas) ;
- **point de non-retour** : à cocher pour l'étape après laquelle on ne peut plus revenir en arrière ;
- pièces jointes de l'étape.

Les codes d'étape (`E0001` pour le déploiement, `R0001` pour le retour arrière) ne sont jamais renumérotés.

Le responsable d'une étape n'est pas choisi à l'avance : c'est celui qui la démarre le jour J.

## Le plan de retour arrière (onglet Retour arrière)

Obligatoire. Il comprend :

- la **fiche du plan** : quand déclencher le retour arrière, durée testée, version ou copies à restaurer, données créées depuis la sauvegarde, que faire si le retour arrière échoue ;
- les **étapes de retour arrière**, éditées comme celles du déploiement. Elles ne peuvent être ni point de non-retour, ni de type Sauvegarde.

## Les fiches de recette (onglet Recette)

La recette vérifie, après le déploiement, que tout fonctionne. Elle est organisée par système : pour chaque système, ajouter une ou plusieurs fiches (« Ajouter une recette »), chacune faite d'étapes de test avec des instructions et un résultat attendu. Le jour J, les responsables techniques du système servent de contact.

## Les résolutions types

Une résolution type est une fiche de dépannage réutilisable pour un problème connu : symptômes, diagnostic, corrections dans l'ordre, vérification finale. Elle peut porter sur plusieurs systèmes.

- Les lier aux étapes qui risquent d'échouer, depuis l'espace d'édition de l'étape. Les résolutions des mêmes systèmes sont proposées en premier.
- « + Créer une nouvelle fiche de résolution » depuis une étape crée la fiche et la lie à l'étape.
- Toutes les résolutions sont dans « Paramètres › Résolutions types », avec l'historique de leurs versions. Tout le monde peut en créer et en modifier.

Le jour J, une étape en échec propose ses résolutions liées et guide l'opérateur pas à pas (voir [Exécuter une MEP](Exécuter-une-MEP.md#quand-une-étape-échoue)).

## Vérifier le plan

Le bouton « Vérifier le plan » liste :

- les **anomalies**, qui bloquent le Go : échéance absente ou passée, pilote manquant ou non relié à un compte, plan de déploiement ou de retour arrière vide, graphe invalide, étape incomplète (action, système, criticité, instructions, résultat attendu), sauvegarde non renseignée ou non justifiée, sauvegarde pendant la MEP sans étape de sauvegarde, recette absente ou incomplète, procédure non relue, approbations manquantes ou refusées ;
- les **avertissements**, qui ne bloquent pas : aucun responsable technique, responsable technique absent le jour J, pilote qui est aussi responsable technique, aucun système sauvegardé, système sans recette.

## Relecture et approbations

Dans la section Validation :

- **Procédure relue** : à cocher par une personne de la MEP reliée à un compte, idéalement un opérateur qui n'a pas écrit le plan.
- **Approbations** : les approbateurs désignés approuvent ou refusent (commentaire obligatoire en cas de refus). Le pilote ne peut pas être approbateur. Le nombre minimum d'approbations est un paramètre global (1 par défaut). Un approbateur ne peut se prononcer que lorsqu'il ne reste plus d'autre anomalie.
- Toute modification du contenu après une approbation l'efface : « Approbations à refaire ».

Quand le plan est complet, le pilote ou un suppléant donne le Go (voir [Cycle de vie](Cycle-de-vie.md#go--no-go--passage-en-exécution)).

## Partir d'une MEP existante

- **Dupliquer** (bouton de la liste ou de l'en-tête) copie une MEP : identité, personnes, pilote et suppléants, systèmes et leur sauvegarde, responsables techniques, livrables, étapes et liens, résolutions liées, plan de retour arrière, fiches de recette (sans les résultats) et approbateurs (sans leurs décisions). Ne sont pas copiés : l'échéance, la relecture, le Go, les résultats d'exécution, le bilan et les pièces jointes. L'environnement peut être changé au passage.
- Cocher **« En faire un modèle »** à la duplication crée un modèle, à réutiliser avec « Nouvelle MEP depuis un modèle ».
