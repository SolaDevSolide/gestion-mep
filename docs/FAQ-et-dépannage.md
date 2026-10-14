# FAQ et dépannage

## Utilisation

**Où signaler un problème ou proposer une idée ?**
Menu utilisateur (en bas de la barre latérale) › « Signaler un problème ou une idée ». Le lien ouvre le formulaire des *issues* du dépôt GitHub, qui est public : n'y mettez aucune information interne. La version à indiquer est en bas du même menu.

**Le bouton « Go / no-go » est grisé ou le Go est refusé.**
Seuls le pilote et ses suppléants le voient. Le Go exige un plan sans anomalie (« Vérifier le plan »), les approbations requises et tous les critères cochés. Un modèle ne passe jamais en exécution : cliquer « Créer une MEP depuis ce modèle ».

**Je ne peux pas modifier la MEP.**
En préparation, seuls le pilote, les suppléants, les responsables techniques et les administrateurs la modifient. Après le Go, le contenu est figé : le pilote peut revenir en préparation tant que le point de non-retour n'est pas franchi.

**Je ne peux pas démarrer une étape.**
Il faut être une personne de la MEP reliée à son compte (ou administrateur) et que l'étape soit « Disponible ». Pour un point de non-retour, il faut aussi l'accord du pilote (« Autoriser le passage »).

**Je ne peux pas terminer l'étape de quelqu'un d'autre.**
Seuls son exécutant, le pilote et les suppléants peuvent la terminer. Si l'exécutant est injoignable, utiliser « Prendre la main » avec un motif.

**« Réussie » reste grisé dans la confirmation.**
L'étape se termine par un test unitaire : saisir le résultat constaté. Si l'étape ne demande qu'un contrôle sans résultat à noter, la préparer en « simple vérification ».

**« Vérifier le plan » réclame l'emplacement ou la méthode de récupération d'une sauvegarde.**
Toute sauvegarde requise (avant ou pendant la MEP) et toute étape de type Sauvegarde doivent dire où se trouve la sauvegarde et comment la récupérer. Sans ces deux informations, le Go est refusé.

**Mes approbations ont disparu.**
Toute modification du contenu en préparation efface les approbations : le plan approuvé n'est plus le même. Les approbateurs doivent se prononcer de nouveau.

**Le retour arrière s'est déclenché tout seul.**
Soit l'échéance de retour arrière était atteinte, soit une correction ou la vérification finale d'une résolution a échoué. Le bandeau rouge et le journal donnent le motif. Pour éviter le premier cas, le pilote peut repousser ou retirer l'échéance pendant l'exécution (sans échéance, pas de déclenchement automatique).

**La MEP n'a pas de sous-onglet Plan › Retour arrière.**
La case « Plan de retour arrière » de la section « Intervention » a été décochée : la MEP n'a pas de retour arrière (le journal indique qui l'a désactivé et quand). La recocher, en préparation, fait revenir le sous-onglet et les étapes déjà saisies.

**« Modifié pendant votre saisie » s'affiche au-dessus d'un formulaire.**
Quelqu'un d'autre a enregistré cette section pendant que vous la modifiiez. « Abandonner ma saisie » affiche sa version ; « Garder ma saisie » permet d'enregistrer la vôtre, qui remplacera la sienne. L'encadré « Prénom modifie » signale à l'avance qui travaille sur quoi.

**« Quitter sans enregistrer ? » s'ouvre quand je change d'onglet.**
Un formulaire de la vue a des modifications non enregistrées, listées dans la fenêtre. « Rester » permet de les enregistrer d'abord ; « Quitter sans enregistrer » les abandonne.

**Un bandeau propose une nouvelle version de l'application.**
L'application a été mise à jour sur le serveur : cliquer « Recharger ».

## Connexion

**J'ai oublié mon mot de passe.**
Demander à un administrateur de le réinitialiser : il vous transmet un mot de passe provisoire, à changer à la connexion.

**La connexion est refusée alors que le mot de passe est bon.**
Après 5 échecs consécutifs depuis votre poste, le compte y est bloqué quelques minutes, et le nombre de tentatives par minute est limité par adresse IP. Attendre puis réessayer. Les échecs d'une autre personne, depuis un autre poste, ne vous bloquent pas.

**Le dépôt d'une pièce jointe est refusé (« limite de pièces jointes », « 24 heures », « espace disque »).**
Des plafonds protègent le serveur : total par MEP, dépôts d'un compte sur 24 heures, espace disque minimal. Supprimer des pièces inutiles, réessayer plus tard, ou demander à un administrateur de relever le plafond (console, onglet Paramètres ; voir aussi [Installer](Installer.md#configuration)).

**La connexion semble réussir mais je reste sur la page de connexion.**
L'application doit être servie en HTTPS (sauf sur `http://localhost`) : le cookie de session n'est pas envoyé en HTTP. Voir [Installer](Installer.md#proxy-inverse-https). Pour un simple essai depuis un autre poste, sans proxy : voir [Essayer depuis un autre poste](Installer.md#essayer-depuis-un-autre-poste-sans-https).

**« Un compte local porte déjà cet identifiant » en me connectant avec Microsoft.**
Vous avez déjà un compte avec mot de passe. Connectez-vous avec, puis « Mon profil › Connexion unique › Lier mon compte ». Ensuite, le bouton Microsoft ouvre ce compte.

**« Aucun compte relié à ce compte Microsoft ».**
La création automatique des comptes est désactivée : un administrateur doit vous créer un compte, que vous reliez ensuite depuis « Mon profil ».

**La connexion Microsoft échoue pour tout le monde.**
Le secret client a peut-être expiré. Un administrateur lance « Tester » dans la console (onglet Connexion unique) ou `gestion-mep sso tester` : voir [Connexion Microsoft](Connexion-Microsoft.md#dépannage). En attendant, la connexion par mot de passe reste possible pour les comptes qui en ont un.

**L'administrateur a perdu son mot de passe ou son compte est désactivé.**
L'équipe informatique le débloque en ligne de commande : voir [Exploiter](Exploiter.md#commandes-dadministration).

## Installation et exploitation

**Les écrans ne se mettent plus à jour en direct derrière le proxy.**
Le proxy met en tampon le flux `/api/meps/*/flux` ou le coupe trop tôt. Désactiver la mise en tampon et allonger le délai de lecture : voir [Installer](Installer.md#proxy-inverse-https).

**L'envoi d'une pièce jointe échoue (fichier trop volumineux).**
La taille maximale (20 Mo par défaut) se règle dans la console (onglet Paramètres), par `gestion-mep reglages definir pieces-taille-max-mo <Mo>` ou par `PIECES_TAILLE_MAX_MO` dans `.env`. Le proxy doit accepter des requêtes un peu plus grandes.

**Tout le monde est déconnecté après chaque redéploiement.**
Le dossier des clés de session n'est pas conservé : vérifier le volume `cles` (ou `Cles__Dossier`).

**L'application ne démarre pas.**
Lire `docker compose logs app`, puis lancer `docker compose exec app gestion-mep config verifier`, qui contrôle la base, les migrations, les dossiers et la présence d'un administrateur.

**Une sauvegarde `avant-mise-a-jour-…` ou `avant-import-…` est restée dans le volume `sauvegardes`.**
Les contrôles après la mise à jour ou l'importation ont trouvé un écart (table disparue, lignes en moins, fichier joint manquant) : il est décrit dans le journal d'administration (type `donnees.controle-echoue`) et dans `docker compose logs app`. Garder l'archive et, si des données manquent, la recharger : voir [Exploiter](Exploiter.md#restaurer-ou-déménager).

Cas particulier : en 1.6.0, le contrôle signalait à tort « table Livrables disparue » sur toute installation qui avait des livrables. Ce n'était pas une perte (les livrables sont repris dans les objets impactés, voir [Exploiter](Exploiter.md#mettre-à-jour)) et les versions suivantes ne le signalent plus. Si c'est le seul écart, vérifier que les livrables apparaissent bien dans les objets impactés des MEP, puis supprimer l'archive.

**Peut-on lancer plusieurs instances derrière un répartiteur de charge ?**
Pas encore : le temps réel est diffusé en mémoire et les migrations tournent au démarrage. Une seule instance à la fois.

**Peut-on se connecter avec les comptes de l'entreprise (SSO) ?**
Oui, avec Microsoft Entra ID ou un autre fournisseur OpenID Connect, sans que ce soit obligatoire : voir [Connexion Microsoft](Connexion-Microsoft.md).

Une question sans réponse ici ? Ouvrir un ticket sur le dépôt GitHub.
