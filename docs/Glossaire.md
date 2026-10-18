# Glossaire

**Anomalie** : point qui empêche de donner le Go, listé par « Vérifier le plan ». À distinguer d'un avertissement.

**Approbation** : accord donné sur le plan par un approbateur désigné, avant le Go. Toute modification du contenu efface les approbations.

**Avertissement** : point signalé par « Vérifier le plan » qui n'empêche pas le Go (par exemple un système sans recette).

**Bac** : colonne à droite du graphe des étapes qui reçoit les sections pas encore placées dans le plan et les étapes retirées du graphe (« Étapes à placer »). Une section ou une étape restée dans le bac bloque le Go.

**Bilan** : retour d'expérience rédigé à la clôture : ce qui a marché, incidents, actions.

**Cercle** : point d'entrée (en haut) ou de sortie (en bas) d'une section, nommé. Les étapes reliées à un cercle d'entrée attendent ce qui y arrive ; ce qui est relié à un cercle de sortie attend les étapes qui y sont reliées.

**Catalogue des systèmes** : liste globale des systèmes de l'entreprise, dans laquelle on choisit les systèmes d'une MEP. Chaque système y a sa fiche.

**Catégorie** : regroupement des objets d'un système dans sa fiche (tables, vues, traitements…). Des catégories par défaut sont proposées à chaque système.

**Comptes reliés** : comptes d'une même personne reliés par un administrateur, sans rien fusionner ni supprimer. Ils apparaissent sous le nom du compte principal et chacun garde son historique.

**Connexion unique (SSO)** : connexion avec son compte d'entreprise (Microsoft Entra ID ou autre fournisseur OpenID Connect) au lieu d'un mot de passe propre à l'application. Facultative : voir [Connexion Microsoft](Connexion-Microsoft.md).

**Communication** : section de la préparation qui rassemble ce qu'on annonce aux utilisateurs (référence de la demande, risque, bénéfices, arrêt applicatif, créneau annoncé, validation par les utilisateurs). Elle remplit les modèles de texte.

**Critère go / no-go** : condition à cocher avant de démarrer (par exemple « Supervision en place »).

**Échéance de retour arrière** : date et heure limites de l'intervention. Une fois atteinte, le retour arrière se déclenche automatiquement, sauf si le point de non-retour est franchi.

**Environnement** : instance d'un système (recette, préproduction, production…), décrite dans sa fiche. Une MEP choisit les environnements de chacun de ses systèmes, puis chaque étape ceux où elle s'exécute.

**Éditeur** : personne qui peut modifier une MEP en préparation : pilote, suppléant, responsable technique ou administrateur.

**Équipe** : groupe de personnes géré par les administrateurs, qui sert à filtrer les comptes et les MEP, et qui a ses modèles de texte. Une personne peut appartenir à plusieurs équipes ; une MEP appartient à une équipe dès qu'une de ses personnes en fait partie.

**État d'une MEP** : ce que la recherche des MEP retient d'une MEP : sa phase tant qu'elle n'est pas clôturée (préparation, exécution, recette, retour arrière en cours), puis son issue : **réussie** (sans aucun échec d'étape), **réussie avec incidents** (au moins un échec d'étape, même repris ensuite) ou **repliée** (clôturée après un retour arrière). Voir [Rechercher des MEP](Suivre-une-MEP.md#rechercher-des-mep).

**Étape** : action unitaire du plan, avec des instructions et un résultat attendu. Codes `E0001…` pour le déploiement, `R0001…` pour le retour arrière.

**Étape modèle** : étape type de la bibliothèque d'étapes (fiche complète, sans point de non-retour). L'insérer dans une MEP en fait une copie indépendante, posée à la main dans le graphe ; on peut aussi transformer une étape d'une MEP en étape modèle.

**Exécutant** : personne qui a démarré une étape, ou qui a pris la main dessus.

**Bibliothèque d'étapes** : étapes modèles rattachées à un ou plusieurs systèmes du catalogue, insérées par copie dans le plan des MEP. Voir aussi *Étape modèle*.

**Bibliothèque de recettes** : modèles de fiches de recette rattachés à un système du catalogue, insérés par copie dans les MEP.

**Fiche du système** : description d'un système du catalogue : ses environnements, ses catégories et ses objets connus, seuls proposés comme objets impactés.

**Fiche de recette** : liste d'étapes de test qui vérifient un système après le déploiement. Elle appartient à un seul système ; chaque étape a un déroulé en blocs Texte ou Code et un résultat attendu.

**Go / no-go** : décision du pilote de démarrer l'exécution (Go) ou de la reporter (no-go, avec un motif).

**Historique d'un système** : onglet « Historique des MEP » de la fiche d'un système du catalogue : toutes les MEP qui l'ont touché, avec leur date, leurs environnements sur ce système, leur état et leurs échecs d'étape sur ce système. Voir [Historique d'un système](Suivre-une-MEP.md#historique-dun-système).

**Hors catalogue** : système saisi à la main dans une MEP, sans fiche au catalogue. La recherche des MEP le retrouve par son nom, mais il n'a pas d'historique.

**Journal** : historique horodaté et attribué de toutes les actions sur une MEP.

**Lien** : dans le graphe des étapes, « B attend A ». Seuls les liens règlent le déroulement : deux étapes qui ne s'attendent pas peuvent se dérouler en parallèle. Le rangement du graphe est automatique.

**MEP** : mise en production, c'est-à-dire l'intervention qui déploie un changement sur un ou plusieurs environnements de ses systèmes.

**Objet impacté** : objet d'un système (table, traitement, fichier…) modifié par la MEP, choisi dans la fiche du système, avec sa version ou sa correction. « Repris » signale un ancien livrable converti.

**MEP simplifiée** : MEP pour une petite intervention : un pilote dont toutes les autres personnes sont suppléantes, ni versions, ni durée, criticité ou résolutions liées aux étapes (le jour J, toutes les résolutions des systèmes de l'étape sont proposées). Le contraire est une MEP complète, le type par défaut.

**Modèle** : MEP qui sert de point de départ à d'autres et qui ne s'exécute jamais.

**Modèle de texte** : courriel type d'une équipe (annonce, rappel, demande de MEP, compte rendu), rempli avec les informations de la MEP puis copié dans sa messagerie. Voir [Modèles de texte](Modèles-de-texte.md).

**Pilote** : responsable de la MEP, qui décide des changements de phase.

**Plan de déploiement** : graphe des étapes qui réalisent le changement.

**Plan de retour arrière** : étapes qui ramènent le service à son état d'avant, et fiche qui dit quand et comment les déclencher.

**Point de non-retour** : étape après laquelle on ne peut plus revenir en arrière. Son démarrage exige l'accord du pilote.

**Prendre la main** : le jour J, reprendre une étape démarrée par quelqu'un d'autre, avec un motif tracé. En préparation, reprendre l'édition d'une étape ouverte par quelqu'un d'autre (verrou d'édition), sans confirmation.

**Preuve** : ce que l'exécutant a constaté en terminant une étape.

**Rapport de fin** : compte rendu figé à la clôture d'une MEP : vue d'ensemble, points bloquants, résolutions, tests unitaires, qui a fait quoi, bilan, annexes. Exportable en Excel et en PDF. Voir [Suivre une MEP](Suivre-une-MEP.md#rapport-de-fin).

**Recette** : phase de vérification après le déploiement, avant la clôture.

**Recherche des MEP** : critères de la liste des MEP (système, environnement, équipe, personne et son rôle, état, période, échec d'étape sur un système), inscrits dans l'adresse de la page pour la partager. Les mêmes critères servent à l'export Excel, à l'API et à `gestion-mep meps lister`.

**Résolution type** : fiche de dépannage réutilisable (symptômes, diagnostic, corrections, vérification finale) liée aux étapes qui peuvent échouer.

**Responsable technique** : développeur ou sachant qui a préparé une partie du changement et reste joignable le jour J.

**Résultat constaté** : réponse d'un test unitaire, saisie par l'exécutant en terminant l'étape.

**Section** : groupe d'étapes du plan sous un titre, avec des systèmes et des cercles. C'est un repère : elle ne s'exécute pas, seuls ses cercles donnent des préalables à ses étapes.

**Simple vérification** : fin d'une étape où l'exécutant confirme le résultat attendu sans saisir de résultat constaté.

**Suppléant** : remplaçant du pilote, avec les mêmes droits. Dans une MEP simplifiée, toutes les personnes sauf le pilote le sont.

**Suivi en direct** : tableau de bord de toutes les MEP lancées, avec les MEP prévues aujourd'hui et demain et celles clôturées dans les dernières 24 heures. Voir [Suivre une MEP](Suivre-une-MEP.md#suivi-en-direct).

**Système** : composant touché par la MEP (base de données, API, portail web…).

**Test unitaire** : fin d'une étape par défaut. Le résultat attendu est l'instruction de test ; le résultat constaté est saisi en terminant l'étape.

**Vue d'ensemble** : premier onglet d'une MEP : chiffres clés, fin estimée, plan coloré par état, blocages et qui fait quoi, en lecture seule. Voir [Suivre une MEP](Suivre-une-MEP.md#vue-densemble-dune-mep).
