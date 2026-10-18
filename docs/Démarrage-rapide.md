# Démarrage rapide

Lancer l'application sur un poste avec des données de démonstration, puis faire le tour d'une MEP. Pour une installation sur serveur, voir [Installer](Installer.md).

## Lancer

Prérequis : Docker Desktop (ou Docker Engine avec Docker Compose).

```bash
git clone https://github.com/SolaDevSolide/gestion-mep.git
cd gestion-mep
echo "DEMO=true" > .env
docker compose up --build
```

La première construction prend quelques minutes. Ouvrir ensuite http://localhost:8080.

> [!WARNING]
> `DEMO=true` crée des comptes au mot de passe connu. Ne jamais l'activer sur un serveur.

## Comptes de démonstration

L'écran de connexion propose une **connexion rapide** : un clic sur un compte ouvre directement sa session. Le formulaire reste disponible, avec le mot de passe `demo` pour tous.

| Compte | Rôle dans l'application | Rôle dans les MEP de démonstration |
|---|---|---|
| `alice` | Administratrice | Pilote de la plupart des MEP ; suppléante sur les MEP pilotées par Bob |
| `bob` | Utilisateur | Suppléant du pilote, responsable technique API et MDM ; pilote de « Annuaire des agences », « Extranet partenaires », « Référentiel produits » et « Prise de rendez-vous » |
| `claire` | Utilisateur | Responsable technique WEB, approbatrice ; pilote de « Formulaire de contact » (MEP simplifiée) |

La connexion rapide n'accepte que ces trois comptes, et seulement tant qu'ils ont le mot de passe `demo` : un compte créé ensuite, même administrateur, ne s'y connecte jamais. Elle n'existe qu'en mode démonstration.

Un **bandeau « Mode démonstration — données fictives, réinitialisables »** reste affiché en haut de toutes les pages, écran de connexion compris.

Pour être connecté avec plusieurs comptes en même temps, utiliser une fenêtre normale, une fenêtre de navigation privée et un autre navigateur. Les écrans se mettent à jour tout seuls : ce que fait l'un apparaît chez les autres sans recharger.

## MEP de démonstration

Chaque MEP est dans un état différent, pour essayer chaque écran sans tout dérouler soi-même. Les heures sont relatives au démarrage (ou à la dernière réinitialisation) ; les échéances des MEP en cours sont dans quelques jours, pour qu'aucun retour arrière automatique ne se déclenche pendant les essais.

| MEP | État | À quoi elle sert |
|---|---|---|
| Portail de démonstration | Préparation, approbation attendue | Le parcours complet ci-dessous : approbation de Claire, Go, exécution, point de non-retour, recette, clôture. |
| Facturation électronique | Préparation incomplète | La base passe en préproduction puis en production, et l'étape E0005 vérifie la base en préproduction mais l'export et le portail en production : une incohérence à repérer à la relecture. « Vérifier le plan » liste les anomalies : plan de retour arrière sans « quand le déclencher », sauvegarde à renseigner ou non justifiée, étape sans instructions, action d'exemple, fiche de recette vide, procédure non relue, approbation manquante ; avertissement pour une responsable absente. |
| Annuaire des agences | Prête pour le Go | Plan complet, relu et approuvé : Bob (pilote) ou Alice (suppléante) peut cliquer « Go / no-go » tout de suite. |
| Paiement mobile | Exécution en cours | Plan rangé en cinq sections (sauvegardes, base de données, service paiement, écran de paiement, contrôles de bout en bout). Sauvegardes, index et table des portefeuilles réussis ; déploiement de l'API en cours (Bob), qui dépasse sa durée, et contrôle des volumes en cours (Alice) ; publication de l'écran en échec, résolution type en cours (Claire), qui bloque la suite ; point de non-retour à venir, à autoriser par le pilote. Sa **vue d'ensemble** montre tout cela d'un coup d'œil. |
| Portail RH | Recette en cours | Déploiement terminé, dont une migration réparée par une résolution type ; fiches de recette en partie validées, une étape en échec à faire refaire, d'autres à tester avant la clôture. |
| Messagerie interne | Retour arrière en cours | Résolution en échec : retour arrière déclenché automatiquement ; une étape de repli réussie, une en cours (Alice), la dernière à faire, puis « Service rétabli : clôturer ». |
| Gestion des stocks | Clôturée réussie | Il y a six jours : déploiement du service en échec, résolu par une résolution type puis refait ; point de non-retour franchi avec l'accord du pilote, recette conforme, bilan qui revient sur l'incident. Son **rapport de fin** en montre le point bloquant. |
| Extranet partenaires | Clôturée repliée | Il y a dix jours : échec sans résolution connue, retour arrière déclenché par Bob, service rétabli, bilan. |
| Formulaire de contact | MEP simplifiée, exécution en cours | Petit correctif : Claire pilote, Alice et Bob sont suppléants sans avoir été choisis ; ni versions, ni durée, criticité ou résolutions liées. Le redémarrage du service (API et BDD) est en échec : toutes les résolutions types de ces deux systèmes sont proposées, regroupées par système. Voir [MEP complète ou simplifiée](Préparer-une-MEP.md#mep-complète-ou-simplifiée). |
| Portail clients, Référentiel produits, Batch de nuit comptable, Prise de rendez-vous | Clôturées des mois passés | L'historique des derniers mois, pour la [recherche des MEP](Suivre-une-MEP.md#rechercher-des-mep) et l'[historique d'un système](Suivre-une-MEP.md#historique-dun-système). Portail clients 4.0 (il y a trois mois et demi, WEB et API) et Référentiel produits 1.2 (deux mois et demi, MDM et BDD) ont réussi ; Batch de nuit comptable 7.0 (sept semaines, BDD et BATCH) a réussi **avec incidents** : une migration en échec sur BDD, reprise ensuite ; Prise de rendez-vous 2.1 (trois semaines, API et BDD) a été **repliée** après un échec sur API. Essayer : filtrer la liste sur BDD avec « Avec un échec d'étape », ou ouvrir l'onglet « Historique des MEP » de la fiche de BDD. |

Les MEP ont leur section **Communication** remplie (référence de la demande, risque, bénéfices, créneau annoncé ; « Gestion des stocks » est validée par les utilisateurs), sauf « Facturation électronique », commencée, et « Portail de démonstration », sans indisponibilité prévue : les textes à copier y signalent les informations manquantes. Deux équipes ont leurs [modèles de texte](Modèles-de-texte.md) : « Exploitation applicative » (Alice et Bob), qui a adapté l'annonce fournie et créé « Prévenir l'astreinte », et « Développement web » (Claire), avec les modèles fournis.

Le « Modèle : déploiement API standard » sert à créer une MEP par duplication. Toutes les commandes affichées sont fictives : rien n'est à lancer.

Les bibliothèques sont remplies elles aussi : une vingtaine d'étapes modèles réparties sur les six systèmes du catalogue (sauvegardes, migrations, déploiements, contrôles…), des modèles de fiches de recette et des résolutions types, avec des compteurs d'utilisations réalistes. Plusieurs étapes et fiches des MEP de démonstration sont des copies de ces modèles.

## Premier parcours

1. **Alice** : dans « Mises en production », ouvrir « Portail de démonstration ». La fiche s'ouvre sur l'onglet Préparation : ses sections, leur état de complétude et le bouton « Vérifier le plan », qui liste ce qui manque avant de pouvoir démarrer. Il reste l'approbation de Claire.

   ![Liste des mises en production](images/liste-des-mep.png)
2. Parcourir l'onglet **Plan**, sous-onglet **Étapes** : le plan de déploiement en graphe, de haut en bas. Chaque flèche dit qu'une étape attend la précédente, et deux étapes sans flèche entre elles peuvent se dérouler en parallèle ; l'étape encadrée de rouge est un point de non-retour. Cliquer une étape ouvre son espace d'édition (instructions, résultat attendu, liens, résolutions).

   ![Graphe des étapes de la MEP de démonstration](images/graphe-des-etapes.png)
3. **Claire** : ouvrir la même MEP, Préparation, section « Validation », cliquer « Approuver ».
4. **Alice** : cliquer « Go / no-go », cocher les critères, puis « Go : passer en exécution ». La MEP passe en exécution et un compte à rebours indique le temps restant avant le retour arrière automatique.
5. Dans l'onglet **Exécution**, chacun démarre les étapes disponibles, saisit une preuve et déclare « Réussie » ou « Échec ». Une étape en échec propose les résolutions prévues pour la corriger.
6. Pour l'étape point de non-retour, Alice doit d'abord cliquer « Autoriser le passage ».
7. Quand toutes les étapes ont réussi : « Passer en recette », dérouler les fiches de **Plan › Recette**, puis « Clôturer : MEP réussie » avec le bilan.
8. L'onglet **Journal** retrace toute l'intervention.

Pour essayer le retour arrière, ouvrir « Paiement mobile » (exécution en cours) et cliquer « Déclencher le retour arrière », ou reprendre « Messagerie interne », déjà en retour arrière.

## Suivre les MEP en cours

Le menu **Suivi en direct** montre toutes les MEP lancées : « Messagerie interne » (retour arrière) en premier, puis « Paiement mobile » et « Formulaire de contact » (échecs à traiter), puis « Portail RH ». Ouvrir « Paiement mobile » sur sa **vue d'ensemble** : chiffres clés, fin estimée et retard, plan coloré (un clic déplie une section ; les étapes qui attendent l'échec sont hachurées en rouge), blocages et qui fait quoi. « Gestion des stocks » (clôturée) a son **rapport de fin** : bouton « Rapport de fin » du bilan, export Excel ou PDF. Voir [Suivre une MEP](Suivre-une-MEP.md).

## Voir travailler des collègues

Dans le bandeau de démonstration, **« Simuler des collègues »** fait travailler Bob et Claire, pour de vrai, sur « Facturation électronique » pendant quelques minutes. Ouvrir son plan (Plan › Étapes, « Tout déplier ») : leurs avatars apparaissent sur les sections et les cartes où ils se trouvent, leurs modifications arrivent en direct et, si vous avez ouvert une étape, Claire finit par y prendre la main (« Claire a pris la main sur E0005 à Alice »). « Arrêter la simulation » les fait partir.

## Repartir de zéro

Avec le compte d'Alice (administratrice), cliquer **« Réinitialiser la démonstration »** dans le bandeau, puis confirmer. La base revient exactement à son état initial : comptes, MEP, catalogue, résolutions types, critères, paramètres, réglages et journaux sont recréés, les pièces jointes supprimées. Tout le monde est déconnecté et revient à l'écran de connexion.

Même chose en ligne de commande, application en marche :

```bash
docker compose exec app gestion-mep demo reinitialiser --oui
```

Pour tout effacer, sauvegardes comprises, et repartir d'une base neuve :

```bash
docker compose down -v
docker compose up --build
```
