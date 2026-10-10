# Jalon 3.1 : responsables techniques et corrections

Demandé par Hugo le 2026-10-09. Le pilote est la personne responsable de la mise en production ; les développeurs deviennent des « responsables techniques », distincts du pilote. But : qu'une MEP se passe bien même quand le développeur n'est pas disponible.

## A. Responsables techniques
- `Mep.ResponsablesTechniques : List<ResponsableTechnique { ParticipantId, SystemeIds : List<Guid> (facultatif : ce qu'il a développé), Perimetre (texte libre), Disponibilite : DisponibiliteJourJ { Joignable, Absent }, ContactJourJ (texte) }>` (jsonb). Un participant n'apparaît qu'une fois.
- Création d'une MEP : le créateur devient participant relié à son compte et responsable technique (Joignable). Il n'est PLUS pilote automatiquement : le pilote est choisi ensuite (ErreursPlan exige déjà un pilote).
- Droits d'édition du contenu en préparation : comptes des responsables techniques, du pilote, des suppléants, et admins. Sinon 403 avec message clair (« Seuls les responsables techniques, le pilote, ses suppléants ou un administrateur peuvent modifier cette MEP. »). Exceptions inchangées : approbation (approbateur), « procédure relue » (n'importe quel opérateur), pièces jointes.
- Avertissements (non bloquants), renvoyés par la vérification du plan dans une liste séparée `avertissements` à côté des anomalies :
  - « X est à la fois responsable technique et pilote : prévoir un suppléant qui connaît le plan. » si le pilote est aussi responsable technique.
  - Pour chaque responsable technique Absent : chaque étape dont il est responsable, ou rattachée à l'un de ses systèmes, doit avoir instructions, résultat attendu et au moins une résolution type ; sinon « E000x : Y sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre. »
  - Aucun responsable technique : « Aucun responsable technique renseigné. »
- Pendant l'exécution, dans le panneau d'une étape : encart « À appeler en cas de problème » listant les responsables techniques du système de l'étape (sinon tous), avec contact et disponibilité.
- Duplication : copie les responsables techniques. Démo : Bob responsable technique API (joignable), Claire responsable technique WEB (joignable) ; Alice reste pilote.

## B. Corrections relevées pendant les essais du guide de test
1. « Prendre la main » : motif obligatoire (« Pourquoi prenez-vous la main ? »), conservé dans le journal (« Alice a pris la main sur E0002 (prévu : Bob) : motif »).
2. « Revenir en préparation » refusé côté serveur et désactivé à l'écran une fois le point de non-retour franchi (message explicite).
3. Fenêtre « Dupliquer la MEP » : champ Environnement vide par défaut (l'aide dit « laissez vide pour reprendre celui de la source »).
4. La copie d'une MEP s'appelle « <outil> (copie) ». Une MEP créée depuis un modèle reprend l'outil sans le préfixe « Modèle : » et le message dit « MEP créée depuis le modèle ».
5. Version cible vide : l'en-tête n'affiche pas « ? » mais rien (pas de flèche) ou « version à définir » en discret.
6. Badge « À VOUS » masqué pour une étape terminée et pour toute MEP clôturée.
7. Libellé unique « Service rétabli : clôturer » partout.
8. Indicateurs de complétude des sections (Intervention, Sauvegarde…) alignés sur les anomalies de la vérification du plan : un champ facultatif ne rend pas une section orange.
