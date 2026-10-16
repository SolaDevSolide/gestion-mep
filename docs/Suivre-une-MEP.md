# Suivre une MEP

Pour qui veut savoir où en est une intervention sans la dérouler : chef de projet, responsable d'application, astreinte. Trois écrans, ouverts à toute personne connectée : la **vue d'ensemble** d'une MEP, le tableau de bord **Suivi en direct** de toutes les MEP lancées et, une fois la MEP clôturée, son **rapport de fin**. Tout se met à jour en direct, sans recharger.

## Vue d'ensemble d'une MEP

Premier onglet de chaque MEP (pas des modèles). Pendant l'exécution, un point vert clignote à côté de son nom : la vue est en direct. Elle existe aussi en préparation, sans le point : le plan y est déjà coloré, mais les chiffres de suivi ne s'animent qu'au Go.

**Onglet d'arrivée.** Pendant l'exécution et le retour arrière, la MEP s'ouvre sur la vue d'ensemble pour qui n'exécute aucune étape du plan en cours ; le pilote et les personnes qui ont une étape à faire (responsables ou exécutants) arrivent, comme avant, sur l'onglet Exécution (ou sur le plan de retour arrière). L'adresse `?onglet=ensemble` ouvre la vue d'ensemble pour tout le monde.

### Chiffres clés

| Chiffre | Ce qu'il dit |
|---|---|
| Avancement | Étapes faites sur le total du plan suivi, avec la barre réussies / en cours / échecs / restantes. |
| Temps passé | Depuis le Go, et la durée prévue du plan. |
| Fin estimée | Heure à laquelle le plan devrait se finir, et l'heure prévue au Go. Une fois toutes les étapes faites : la fin réelle. |
| Retard | Écart entre la fin estimée et la fin prévue : vert jusqu'à 5 minutes, orange jusqu'à 30, rouge au-delà. |
| À traiter | Étapes en échec et étapes bloquées. |

**Comment se calcule la fin estimée.** La fin prévue est le Go plus le plus long chemin des durées estimées à travers les liens du plan (deux branches parallèles ne s'additionnent pas). La fin estimée est maintenant plus le plus long chemin des **durées restantes** : une étape réussie ne compte plus ; une étape en cours compte ce qui reste de sa durée, ou **5 minutes** si elle a déjà dépassé sa durée ; une étape en échec compte sa durée plus **10 minutes** (la résolution avant de la refaire) ; une étape à venir compte sa durée. Une MEP sans durées estimées (souvent une MEP simplifiée) n'a ni fin estimée ni retard. Pendant un retour arrière, la vue suit le plan de retour arrière, depuis son déclenchement.

### Plan coloré

Le graphe du plan, en lecture seule, avec les mêmes cartes et le même rangement que l'onglet Plan. Les sections sont repliées : un clic en déplie une. Chaque carte et chaque section prend la couleur de son état :

| État | Sens |
|---|---|
| Faite | Étape réussie (section : toutes ses étapes réussies). |
| En cours | Étape démarrée (section : au moins une étape faite ou en cours). |
| Échec | Étape en échec, sans résolution lancée. |
| Résolution en cours | Étape en échec dont une résolution type est en cours. |
| À venir | Étape qui attend son tour. |
| Bloquée | Étape à venir qui attend, directement ou par d'autres étapes à venir, une étape en échec : cadre rouge en pointillé et hachures, avec « Attend E0005 (en échec) ». |

La mini-carte est masquée par défaut (bouton en bas à droite, mémorisé pour la vue d'ensemble). Le bouton plein écran du graphe emporte toute la vue : chiffres, plan et panneau de droite.

### Panneau de droite

- **Blocages et échecs** : chaque étape en échec, avec son motif, sa résolution en cours (corrections faites, vérification) et les étapes qu'elle bloque ; chaque étape en cours qui a dépassé sa durée.
- **En ce moment** : le pilote et les personnes qui ont des étapes dans le plan, ce que chacune exécute ou sa prochaine étape, et où se trouvent celles qui ont la MEP ouverte (« sur E0004 », « dans la section Sauvegardes »), d'après la même présence que les avatars du graphe.
- **Événements marquants** : le journal filtré (Go, échecs et réussites d'étape, résolutions, prises de main, point de non-retour, retour arrière, clôture), avec un lien vers le journal complet.

## Suivi en direct

Entrée **Suivi en direct** du menu, ouverte à tous. En haut, quatre chiffres : MEP en cours, échecs à traiter, retours arrière et prochaine MEP (la première MEP en préparation dont la date annoncée, section Communication, n'est pas passée).

Puis une carte par MEP lancée (exécution, recette ou retour arrière) : avancement, sections colorées comme dans la vue d'ensemble (une pastille par étape hors section), fin estimée et retard, échecs et ce qu'ils bloquent, qui exécute quoi, compte à rebours avant l'échéance de retour arrière. Les **retours arrière** viennent en premier, puis les MEP qui ont des **échecs** à traiter, puis les autres, la plus ancienne lancée d'abord. « Vue d'ensemble » ouvre la MEP sur son premier onglet.

En bas, deux listes : les MEP **prévues aujourd'hui et demain** (date et heure annoncées dans la section Communication) et les MEP **clôturées dans les dernières 24 heures**, avec un lien vers leur rapport de fin. Le bouton « Plein écran » affiche le tableau seul, pour un écran de supervision.

## Rapport de fin

Quand une MEP est clôturée, son **rapport de fin** est figé : ce qu'il montre ne change plus, même si la MEP est modifiée ensuite. On l'ouvre par le bouton « Rapport de fin » du bilan de clôture, le lien de la vue d'ensemble ou celui du Suivi en direct (adresse `/meps/{id}/rapport`). Il n'y a pas de rapport, même provisoire, avant la clôture.

Il contient :

1. la **vue d'ensemble à la clôture** : étapes réussies, durée réelle du déploiement et durée prévue, échecs (et combien ont été repris avec succès), recette, et le plan coloré tel qu'à la clôture ;
2. les **points bloquants** : chaque échec d'étape, tiré du journal (une étape refaite garde son échec), avec son motif, la personne, la résolution type utilisée et sa durée, l'heure de reprise et de réussite, et le temps d'arrêt (de l'échec à la reprise) ;
3. les **résolutions utilisées**, avec corrections et vérification ;
4. les **tests unitaires** : résultat attendu et constaté de chaque étape terminée par un test ;
5. **qui a fait chaque étape**, avec les heures et la durée réelle face à la durée prévue ;
6. le **bilan** ;
7. en annexe : la **recette** (fiches et résultats), le **retour arrière** s'il a eu lieu, et le **journal complet**.

**Exporter.** « Exporter en Excel » télécharge le rapport en classeur (`rapport-Outil-Version.xlsx` : synthèse, points bloquants, résolutions, tests unitaires, étapes, puis les annexes). « Télécharger en PDF » ouvre l'impression du navigateur : choisir « Enregistrer au format PDF ». La page a une mise en page d'impression soignée (menu, bandeau et boutons masqués, tableaux qui ne se coupent pas au milieu d'une ligne) ; « Imprimer » fait de même vers une imprimante. Aucune bibliothèque PDF n'est embarquée côté serveur : le navigateur produit un PDF fidèle à l'écran, sans dépendance à la licence incertaine. En ligne de commande : `gestion-mep meps excel <id> rapport` (voir [Exploiter](Exploiter.md#mep-et-journal)).

**MEP clôturées avant cette version.** Leur rapport est établi à la première consultation, à partir de la MEP et de son journal, puis figé à son tour ; il le signale en bas de page.

## Dans les textes à copier

Trois [variables de texte](Modèles-de-texte.md#suivi-de-lexécution) reprennent ces informations : `{{avancement}}` (« 4/14 étapes faites (29 %) »), `{{fin_estimee}}` (« 16h33 (prévue à 15h18, +1 h 15) », calculée comme la vue d'ensemble) et `{{lien_rapport}}` (adresse du rapport de fin, une fois la MEP clôturée).
