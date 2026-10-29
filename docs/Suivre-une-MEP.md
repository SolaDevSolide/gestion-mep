# Suivre une MEP

Pour qui veut savoir où en est une intervention sans la dérouler : chef de projet, responsable d'application, astreinte. Des écrans ouverts à toute personne connectée : la **vue d'ensemble** d'une MEP, le tableau de bord **Suivi en direct** de toutes les MEP lancées, une fois la MEP clôturée son **rapport de fin**, et pour retrouver les MEP passées la **recherche** de la liste des MEP et l'**historique** de chaque système. Tout se met à jour en direct, sans recharger.

## Vue d'ensemble d'une MEP

Premier onglet de chaque MEP (pas des modèles). Pendant l'exécution, un point vert clignote à côté de son nom : la vue est en direct. Elle existe aussi en préparation, sans le point : le plan y est déjà coloré, mais les chiffres de suivi ne s'animent qu'au Go.

**Onglet d'arrivée.** Pendant l'exécution et le retour arrière, la MEP s'ouvre sur la vue d'ensemble pour qui n'exécute aucune étape du plan en cours ; le pilote et les personnes qui ont une étape à faire (responsables ou exécutants) arrivent, comme avant, sur l'onglet Exécution (ou sur le plan de retour arrière). L'adresse `?onglet=ensemble` ouvre la vue d'ensemble pour tout le monde.

### Chiffres clés

| Chiffre | Ce qu'il dit |
|---|---|
| Avancement | Étapes réussies sur le total du plan suivi, avec la barre réussies / en cours / échecs / restantes. |
| Temps passé | Depuis le Go, et la durée prévue du plan. |
| Fin estimée | Heure à laquelle le plan devrait se finir, et l'heure prévue au Go. Une fois toutes les étapes réussies : la fin réelle. |
| Retard | Écart entre la fin estimée et la fin prévue : vert jusqu'à 5 minutes, orange jusqu'à 30, rouge au-delà. |
| À traiter | Étapes en échec et étapes bloquées. |

**Comment se calcule la fin estimée.** La fin prévue est le Go plus le plus long chemin des durées estimées à travers les liens du plan (deux branches parallèles ne s'additionnent pas). La fin estimée est maintenant plus le plus long chemin des **durées restantes** : une étape réussie ne compte plus ; une étape en cours compte ce qui reste de sa durée, ou **5 minutes** si elle a déjà dépassé sa durée ; une étape en échec compte sa durée plus **10 minutes** (la résolution avant de la refaire) ; une étape à venir compte sa durée. Une MEP sans durées estimées (souvent une MEP simplifiée) n'a ni fin estimée ni retard. Pendant un retour arrière, la vue suit le plan de retour arrière, depuis son déclenchement.

### Plan coloré

Le graphe du plan, en lecture seule, avec les mêmes cartes et le même rangement que l'onglet Plan. Les sections sont repliées : un clic en déplie une. Chaque carte et chaque section prend la couleur de son état :

| État | Sens |
|---|---|
| Réussie | Étape réussie (section : toutes ses étapes réussies). |
| En cours | Étape démarrée (section : au moins une étape réussie ou en cours). |
| Échec | Étape en échec, sans résolution lancée. |
| Résolution en cours | Étape en échec dont une résolution type est en cours. |
| À venir | Étape qui attend son tour. |
| Bloquée | Étape à venir qui attend, directement ou par d'autres étapes à venir, une étape en échec : cadre rouge en pointillé et hachures, avec « Attend E0005 (en échec) ». |

La mini-carte est masquée par défaut (bouton en bas à droite, mémorisé pour la vue d'ensemble). Le bouton plein écran du graphe emporte toute la vue : chiffres, plan et panneau de droite.

### Panneau de droite

- **Blocages et échecs** : chaque étape en échec, avec son motif, sa résolution en cours (corrections faites, vérification) et les étapes qu'elle bloque ; chaque étape en cours qui a dépassé sa durée ; chaque envoi à une [API externe](API-externes.md) définitivement en échec (connexion, événement, résultat), avec **Renvoyer** pour le pilote, ses suppléants et les administrateurs.
- **En ce moment** : le pilote et les personnes qui ont des étapes dans le plan, ce que chacune exécute ou sa prochaine étape, et où se trouvent celles qui ont la MEP ouverte (« sur E0004 », « dans la section Sauvegardes »), d'après la même présence que les avatars du graphe.
- **Événements marquants** : le journal filtré (Go, échecs et réussites d'étape, résolutions, prises de main, point de non-retour, retour arrière, clôture), avec un lien vers le journal complet.

## Suivi en direct

Entrée **Suivi en direct** du menu, ouverte à tous. En haut, quatre chiffres : MEP en cours, échecs à traiter, retours arrière et prochaine MEP (la première MEP en préparation, non reportée, dont la date annoncée, section Communication, n'est pas passée).

Puis une carte par MEP lancée (exécution, recette ou retour arrière ; ni les MEP annulées ni les reportées) : avancement, sections colorées comme dans la vue d'ensemble (une pastille par étape hors section), fin estimée et retard, échecs et ce qu'ils bloquent, qui exécute quoi, compte à rebours avant l'échéance de retour arrière. Les **retours arrière** viennent en premier, puis les MEP qui ont des **échecs** à traiter, puis les autres, la plus ancienne lancée d'abord. « Vue d'ensemble » ouvre la MEP sur son premier onglet ; les puces des systèmes touchés (une alerte signale celui d'une étape en échec) ouvrent leur historique.

En bas, deux listes : les MEP **prévues aujourd'hui et demain** (date et heure annoncées dans la section Communication) et les MEP **clôturées dans les dernières 24 heures**, avec un lien vers leur rapport de fin. Le bouton « Plein écran » affiche le tableau seul, pour un écran de supervision.

Le tableau suit les MEP au fil de l'eau sans tout recharger : une action sur une MEP lancée ne met à jour que sa carte ; le tableau entier n'est relu que lorsqu'une MEP est lancée, clôturée, annoncée ou retirée. Une modification qui n'y apparaît pas (la préparation d'une MEP pas encore lancée, par exemple) ne le fait pas bouger.

## Rapport de fin

Quand une MEP est clôturée, son **rapport de fin** est figé : ce qu'il montre ne change plus, même si la MEP est modifiée ensuite. On l'ouvre par le bouton « Rapport de fin » du bilan de clôture, le lien de la vue d'ensemble ou celui du Suivi en direct (adresse `/meps/{id}/rapport`). Il n'y a pas de rapport, même provisoire, avant la clôture.

Il contient :

1. la **vue d'ensemble à la clôture** : l'issue (« réussie », « réussie avec incidents » si une étape ou un test de recette a échoué en route, « clôturée après retour arrière », ou « échec » avec l'état du service laissé en production et la référence de l'incident), l'heure du Go (et du premier Go si la MEP est revenue en préparation, avec le nombre de retours en préparation), le nombre d'étapes correctives ajoutées après le Go, étapes réussies, durée réelle du déploiement et durée prévue, échecs (et combien ont été repris avec succès), recette, et le plan coloré tel qu'à la clôture ;
2. les **points bloquants** : chaque échec d'étape, tiré du journal (une étape refaite garde son échec), avec son motif, la personne, la résolution type utilisée et sa durée, l'heure de reprise et de réussite, et le temps d'arrêt (de l'échec à la reprise) ;
3. les **résolutions utilisées**, avec corrections et vérification ;
4. les **tests unitaires** : résultat attendu et constaté de chaque étape terminée par un test ;
5. **qui a fait chaque étape**, avec les heures et la durée réelle face à la durée prévue ;
6. le **bilan** ;
7. en annexe : la **recette** (fiches et résultats), le **retour arrière** s'il a eu lieu, et le **journal complet**.

**Exporter.** « Exporter en Excel » télécharge le rapport en classeur (`rapport-Outil-Version.xlsx` : synthèse, points bloquants, résolutions, tests unitaires, étapes avec durées réelles et prévues, puis les annexes ; mêmes tableaux filtrables et même mise en page d'impression que les autres classeurs). « Télécharger en PDF » ouvre l'impression du navigateur : choisir « Enregistrer au format PDF ». La page a une mise en page d'impression soignée (menu, bandeau et boutons masqués, tableaux qui ne se coupent pas au milieu d'une ligne) ; « Imprimer » fait de même vers une imprimante. Aucune bibliothèque PDF n'est embarquée côté serveur : le navigateur produit un PDF fidèle à l'écran, sans dépendance à la licence incertaine. En ligne de commande : `gestion-mep meps excel <id> rapport` (voir [Exploiter](Exploiter.md#mep-et-journal)).

**MEP clôturées avant cette version.** Leur rapport est établi à la première consultation, à partir de la MEP et de son journal, puis figé à son tour ; il le signale en bas de page.

## Rechercher des MEP

La liste des MEP (page d'accueil) se filtre par une rangée de critères au-dessus des résultats : **Système** (ceux du catalogue, puis ceux saisis à la main dans les MEP, sous « Hors catalogue »), **Environnement** (sur les systèmes choisis), **Équipe**, **Personne** (avec, au besoin, son rôle : pilote, suppléant, responsable technique, exécutant, approbateur), **État** et **Période**. Les critères se combinent ; dans un même critère, une valeur suffit. Sous le critère Système, la case « Avec un échec d'étape sur ces systèmes » ne garde que les MEP qui ont eu un échec sur l'un d'eux, même repris. Sur téléphone, les critères sont dans le bouton « Filtres ».

| État | MEP retenues |
|---|---|
| Préparation, Exécution, Recette, Retour arrière en cours | les MEP dans cette phase (« Préparation » sans les MEP reportées) |
| Reportée | en préparation, avec un report en cours |
| Réussie | clôturées sans retour arrière, sans aucun échec d'étape ni test de recette en échec |
| Réussie avec incidents | clôturées sans retour arrière, avec au moins un échec d'étape ou un test de recette en échec (même repris ensuite) |
| Après retour arrière | clôturées après un retour arrière (« Clôturée après retour arrière ») |
| Échec (service dégradé) | clôturées en échec |
| Annulée | annulées (groupe « Arrêtées ») |

Les MEP annulées ne sont pas masquées : leur carte, en texte atténué, indique « Annulée le … » et le nombre d'étapes réussies avant l'annulation, sans barre de progression ni compte à rebours. Une MEP reportée affiche « Reportée · 05/11/2026 » (sa date visée).

La **période** porte sur le jour du Go de la MEP (à défaut, celui de sa création), bornes incluses ; des raccourcis donnent les 30 derniers jours, les 3 ou 12 derniers mois et l'année en cours. La recherche par texte porte aussi sur le nom des systèmes.

Dès qu'un critère est actif, les résultats passent en **tableau** (état, jour, systèmes et environnements, pilote ou rôle de la personne choisie, échecs, lien vers le rapport de fin), les plus récents d'abord ; le choix « Cartes » / « Tableau » se garde ensuite d'une visite à l'autre. Les critères s'écrivent dans l'adresse de la page : « Copier le lien » la partage, un favori du navigateur la garde. Quand rien ne correspond, la page propose de retirer un critère, avec le nombre de MEP retrouvées. « Exporter en Excel » télécharge toutes les MEP trouvées (pas seulement celles affichées), avec une feuille « Échecs » (limitée aux systèmes choisis).

La liste affiche les 30 MEP les plus récentes qui correspondent ; « Afficher plus », en bas, ajoute les suivantes (leur place est tenue par des lignes grisées le temps qu'elles arrivent). Les nombres de la barre de filtres (« 12 MEP sur 340 », le nombre à côté de chaque valeur) portent sur toutes les MEP, pas seulement sur celles affichées. Pendant un changement de critère, les résultats précédents restent affichés, estompés, jusqu'à l'arrivée des nouveaux.

Partout où un système apparaît (liste, en-tête d'une MEP, section Systèmes de la préparation, Suivi en direct, rapport de fin), sa puce ouvre un menu : **Historique des MEP**, **MEP sur ce système** (la liste filtrée) et **Fiche du système**. Un système saisi à la main n'a pas d'historique : son menu ne propose que la liste filtrée.

La palette de commandes (`Ctrl K`) cherche aussi : « Rechercher « … » dans les MEP » ouvre la liste filtrée par le texte tapé, les systèmes du catalogue ouvrent leur historique et les personnes la liste de leurs MEP.

En ligne de commande : `gestion-mep meps lister` avec les mêmes critères (voir [Exploiter](Exploiter.md#mep-et-journal)).

## Historique d'un système

Onglet **« Historique des MEP »** de la fiche d'un système du catalogue (Paramètres › Systèmes, ou la puce du système) : toutes les MEP qui l'ont touché, avec leur date, leurs environnements sur ce système et leur issue.

- En haut, la synthèse : nombre de MEP (dont celles des 12 derniers mois et celles en cours ou à venir), dernière MEP lancée, part des MEP clôturées avec problème (en échec, clôturées après retour arrière ou avec incidents ; les annulées sont comptées à part) et échecs d'étape survenus sur ce système, repris ou non.
- Une frise des 12 derniers mois place chaque MEP à sa date, de la couleur de son état.
- Puis les MEP, mois par mois (celles en préparation sans Go d'abord, sous « À venir »), chacune avec ses environnements sur ce système, les autres systèmes touchés, son pilote, un lien vers son rapport de fin et ses **échecs d'étape sur ce système** (repris ou non, avec la résolution type appliquée).
- Les MEP se filtrent par environnement et par « Avec un échec sur ce système » ; « Ouvrir dans la liste des MEP » reprend ces critères dans la liste. « Exporter en Excel » télécharge tout l'historique (feuilles Synthèse, MEP et Échecs).

Le catalogue des systèmes montre aussi, pour chacun, sa **dernière MEP** et le nombre de MEP qui l'ont touché. En ligne de commande : `gestion-mep systemes historique <SYSTÈME>`.

## Dans les textes à copier

Trois [variables de texte](Modèles-de-texte.md#suivi-de-lexécution) reprennent ces informations : `{{avancement}}` (« 4/14 étapes réussies (29 %) »), `{{fin_estimee}}` (« 16h33 (prévue à 15h18, +1 h 15) », calculée comme la vue d'ensemble) et `{{lien_rapport}}` (adresse du rapport de fin, une fois la MEP clôturée).
