# FAQ et dépannage

## Utilisation

**Le bouton « Go / no-go » est grisé ou le Go est refusé.**
Seuls le pilote et ses suppléants le voient. Le Go exige un plan sans anomalie (« Vérifier le plan »), les approbations requises et tous les critères cochés. Un modèle ne passe jamais en exécution : cliquer « Créer une MEP depuis ce modèle ».

**Je ne peux pas modifier la MEP.**
En préparation, seuls le pilote, les suppléants, les responsables techniques et les administrateurs la modifient. Après le Go, le contenu est figé : le pilote peut revenir en préparation tant que le point de non-retour n'est pas franchi.

**Je ne peux pas démarrer une étape.**
Il faut être une personne de la MEP reliée à son compte (ou administrateur) et que l'étape soit « Disponible ». Pour un point de non-retour, il faut aussi l'accord du pilote (« Autoriser le passage »).

**Je ne peux pas terminer l'étape de quelqu'un d'autre.**
Seuls son exécutant, le pilote et les suppléants peuvent la terminer. Si l'exécutant est injoignable, utiliser « Prendre la main » avec un motif.

**Mes approbations ont disparu.**
Toute modification du contenu en préparation efface les approbations : le plan approuvé n'est plus le même. Les approbateurs doivent se prononcer de nouveau.

**Le retour arrière s'est déclenché tout seul.**
Soit l'échéance de retour arrière était atteinte, soit une correction ou la vérification finale d'une résolution a échoué. Le bandeau rouge et le journal donnent le motif. Pour éviter le premier cas, le pilote peut repousser l'échéance pendant l'exécution.

**L'écran n'est plus à jour ou affiche « Des modifications ont été reçues ».**
Quelqu'un d'autre a modifié la MEP pendant votre saisie. Recharger la récupère, mais perd la saisie en cours : la copier d'abord si besoin.

**Un bandeau propose une nouvelle version de l'application.**
L'application a été mise à jour sur le serveur : cliquer « Recharger ».

## Connexion

**J'ai oublié mon mot de passe.**
Demander à un administrateur de le réinitialiser : il vous transmet un mot de passe provisoire, à changer à la connexion.

**La connexion est refusée alors que le mot de passe est bon.**
Après 5 échecs consécutifs, le compte est bloqué quelques minutes, et le nombre de tentatives par minute est limité par adresse IP. Attendre puis réessayer.

**La connexion semble réussir mais je reste sur la page de connexion.**
L'application doit être servie en HTTPS (sauf sur `http://localhost`) : le cookie de session n'est pas envoyé en HTTP. Voir [Installer](Installer.md#proxy-inverse-https).

**L'administrateur a perdu son mot de passe ou son compte est désactivé.**
L'équipe informatique le débloque en ligne de commande : voir [Exploiter](Exploiter.md#commandes-dadministration).

## Installation et exploitation

**Les écrans ne se mettent plus à jour en direct derrière le proxy.**
Le proxy met en tampon le flux `/api/meps/*/flux` ou le coupe trop tôt. Désactiver la mise en tampon et allonger le délai de lecture : voir [Installer](Installer.md#proxy-inverse-https).

**L'envoi d'une pièce jointe échoue (fichier trop volumineux).**
La taille maximale est réglée par `PIECES_TAILLE_MAX_MO` (20 Mo par défaut). Le proxy doit accepter des requêtes un peu plus grandes.

**Tout le monde est déconnecté après chaque redéploiement.**
Le dossier des clés de session n'est pas conservé : vérifier le volume `cles` (ou `Cles__Dossier`).

**L'application ne démarre pas.**
Lire `docker compose logs app`, puis lancer `docker compose exec app gestion-mep config verifier`, qui contrôle la base, les migrations, les dossiers et la présence d'un administrateur.

**Une sauvegarde `avant-mise-a-jour-…` ou `avant-import-…` est restée dans le volume `sauvegardes`.**
Les contrôles après la mise à jour ou l'importation ont trouvé un écart (table disparue, lignes en moins, fichier joint manquant) : il est décrit dans le journal d'administration (type `donnees.controle-echoue`) et dans `docker compose logs app`. Garder l'archive et, si des données manquent, la recharger : voir [Exploiter](Exploiter.md#restaurer-ou-déménager).

**Peut-on lancer plusieurs instances derrière un répartiteur de charge ?**
Pas encore : le temps réel est diffusé en mémoire et les migrations tournent au démarrage. Une seule instance à la fois.

**Peut-on se connecter avec les comptes de l'entreprise (SSO) ?**
Pas encore : les comptes sont gérés dans l'application.

Une question sans réponse ici ? Ouvrir un ticket sur le dépôt GitHub.
