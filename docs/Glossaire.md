# Glossaire

**Anomalie** : point qui empêche de donner le Go, listé par « Vérifier le plan ». À distinguer d'un avertissement.

**Approbation** : accord donné sur le plan par un approbateur désigné, avant le Go. Toute modification du contenu efface les approbations.

**Avertissement** : point signalé par « Vérifier le plan » qui n'empêche pas le Go (par exemple un système sans recette).

**Bilan** : retour d'expérience rédigé à la clôture : ce qui a marché, incidents, actions.

**Catalogue des systèmes** : liste globale des systèmes de l'entreprise, dans laquelle on choisit les systèmes d'une MEP.

**Comptes reliés** : comptes d'une même personne reliés par un administrateur, sans rien fusionner ni supprimer. Ils apparaissent sous le nom du compte principal et chacun garde son historique.

**Connexion unique (SSO)** : connexion avec son compte d'entreprise (Microsoft Entra ID ou autre fournisseur OpenID Connect) au lieu d'un mot de passe propre à l'application. Facultative : voir [Connexion Microsoft](Connexion-Microsoft.md).

**Critère go / no-go** : condition à cocher avant de démarrer (par exemple « Supervision en place »).

**Échéance de retour arrière** : date et heure limites de l'intervention. Une fois atteinte, le retour arrière se déclenche automatiquement, sauf si le point de non-retour est franchi.

**Éditeur** : personne qui peut modifier une MEP en préparation : pilote, suppléant, responsable technique ou administrateur.

**Équipe** : groupe de personnes géré par les administrateurs, qui sert à filtrer les comptes et les MEP. Une personne peut appartenir à plusieurs équipes ; une MEP appartient à une équipe dès qu'une de ses personnes en fait partie.

**Étape** : action unitaire du plan, avec des instructions et un résultat attendu. Codes `E0001…` pour le déploiement, `R0001…` pour le retour arrière.

**Exécutant** : personne qui a démarré une étape, ou qui a pris la main dessus.

**Fiche de recette** : liste d'étapes de test qui vérifient un système après le déploiement.

**Go / no-go** : décision du pilote de démarrer l'exécution (Go) ou de la reporter (no-go, avec un motif).

**Journal** : historique horodaté et attribué de toutes les actions sur une MEP.

**Ligne** : rangée du graphe des étapes. Les étapes d'une même ligne peuvent se dérouler en parallèle.

**MEP** : mise en production, c'est-à-dire l'intervention qui déploie un changement sur un environnement.

**Modèle** : MEP qui sert de point de départ à d'autres et qui ne s'exécute jamais.

**Pilote** : responsable de la MEP, qui décide des changements de phase.

**Plan de déploiement** : graphe des étapes qui réalisent le changement.

**Plan de retour arrière** : étapes qui ramènent le service à son état d'avant, et fiche qui dit quand et comment les déclencher.

**Point de non-retour** : étape après laquelle on ne peut plus revenir en arrière. Son démarrage exige l'accord du pilote.

**Prendre la main** : reprendre une étape démarrée par quelqu'un d'autre, avec un motif tracé.

**Preuve** : ce que l'exécutant a constaté en terminant une étape.

**Recette** : phase de vérification après le déploiement, avant la clôture.

**Résolution type** : fiche de dépannage réutilisable (symptômes, diagnostic, corrections, vérification finale) liée aux étapes qui peuvent échouer.

**Responsable technique** : développeur ou sachant qui a préparé une partie du changement et reste joignable le jour J.

**Résultat constaté** : réponse d'un test unitaire, saisie par l'exécutant en terminant l'étape.

**Simple vérification** : fin d'une étape où l'exécutant confirme le résultat attendu sans saisir de résultat constaté.

**Suppléant** : remplaçant du pilote, avec les mêmes droits.

**Système** : composant touché par la MEP (base de données, API, portail web…).

**Test unitaire** : fin d'une étape par défaut. Le résultat attendu est l'instruction de test ; le résultat constaté est saisi en terminant l'étape.
