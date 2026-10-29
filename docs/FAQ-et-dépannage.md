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
Toute modification du contenu en préparation efface les approbations : le plan approuvé n'est plus le même. Les approbateurs doivent se prononcer de nouveau. La case « Procédure relue » est aussi effacée : le plan modifié est à relire.

**« Bob ne peut pas approuver cette MEP (suppléant du pilote) ».**
Séparation des tâches : le pilote, ses suppléants et les responsables techniques (comptes reliés compris) n'approuvent pas la MEP. Bob est devenu suppléant ou responsable technique après avoir été désigné approbateur : sa décision ne compte pas et le Go est refusé. Le retirer des approbateurs et en désigner un autre.

**Le retour arrière s'est déclenché tout seul.**
L'échéance de retour arrière était atteinte pendant l'exécution. Le bandeau rouge et le journal donnent le motif. Pour l'éviter, le pilote peut repousser ou retirer l'échéance avant (« Modifier l'échéance », motif obligatoire ; sans échéance, pas de déclenchement automatique). L'échec d'une résolution ne déclenche jamais le retour arrière : l'étape reste en échec et le pilote décide. En recette, l'échéance atteinte est seulement signalée.

**L'échéance affichée n'est pas à mon heure.**
L'échéance de retour arrière se saisit et s'affiche à l'heure du fuseau horaire de la MEP (section « Intervention »), indiqué entre parenthèses : c'est l'heure du site où se fait l'intervention, pas celle de votre ordinateur.

**La MEP n'a pas de sous-onglet Plan › Retour arrière.**
La case « Plan de retour arrière » de la section « Intervention » a été décochée : la MEP n'a pas de retour arrière (le journal indique qui l'a désactivé et quand). La recocher, en préparation, fait revenir le sous-onglet et les étapes déjà saisies.

**« Modifié pendant votre saisie » s'affiche au-dessus d'un formulaire.**
Quelqu'un d'autre a enregistré cette section pendant que vous la modifiiez. « Abandonner ma saisie » affiche sa version ; « Garder ma saisie » permet d'enregistrer la vôtre, qui remplacera la sienne. L'encadré « Prénom modifie » signale à l'avance qui travaille sur quoi.

**« Quitter sans enregistrer ? » s'ouvre quand je change d'onglet.**
Un formulaire de la vue a des modifications non enregistrées, listées dans la fenêtre. « Rester » permet de les enregistrer d'abord ; « Quitter sans enregistrer » les abandonne.

**Une partie de l'écran affiche « non chargé » avec un bouton « Réessayer ».**
Une liste n'a pas pu être lue ; le message en donne la cause. « Le serveur ne répond pas (502) » arrive pendant une mise à jour de Gestion MEP : patienter un instant puis cliquer « Réessayer ». « Le serveur est injoignable » : vérifier la connexion réseau. Le reste de l'écran reste utilisable : par exemple, une personne s'ajoute sans compte si la liste des comptes n'est pas chargée. Une liste vide (« Aucun… ») veut toujours dire que le serveur a répondu qu'il n'y avait rien. Pour un administrateur, « Détails techniques » donne la requête, le code et l'heure, à copier dans un signalement.

**« Session expirée » s'ouvre pendant que je travaille.**
La session a pris fin (déconnexion depuis un autre onglet, redémarrage du serveur sans conservation des clés, longue inactivité). La page reste affichée derrière, avec votre saisie : saisir le mot de passe et cliquer « Se reconnecter ». Ce qui était en cours d'envoi repart alors une fois, et le message « Reconnecté. Votre saisie est intacte. » le confirme. Avec la connexion Microsoft, le navigateur passe par la page de Microsoft puis revient sur la même page, saisie reprise. « Changer de compte » abandonne la saisie, après confirmation.

**« Rangement automatique indisponible » s'affiche en haut du plan.**
Le rangement des cartes a échoué ou a pris plus de 15 secondes. Les cartes restent à leurs places et le plan se modifie normalement. « Réessayer » relance le rangement ; si le message revient, recharger la page.

**Sur mon portable, le plan d'étapes est très petit.**
Sur une fenêtre de moins de 800 px de haut, le plan passe en vue compacte : la page défile jusqu'au graphe, qui prend toute la hauteur, et une étape s'ouvre en tiroir par-dessus le graphe. Le plein écran du plan (bouton en haut à droite du graphe) donne encore plus de place. Voir [Sur un petit écran](Préparer-une-MEP.md#sur-un-petit-écran).

**Un bandeau propose une nouvelle version de l'application.**
L'application a été mise à jour sur le serveur (nouveau numéro de version, ou nouvelle construction de l'interface, par exemple une image reconstruite depuis une branche) : cliquer « Recharger ». Un onglet resté ouvert garde sinon l'ancienne interface ; le bandeau s'affiche aussi quand une partie de l'interface ne se charge plus parce que le serveur a été mis à jour.

**Après avoir reconstruit l'image, l'application semble inchangée.**
`docker compose up -d` réutilise l'image déjà construite : reconstruire avec `docker compose up -d --build` (ou `docker compose build app` puis `docker compose up -d`), puis recharger la page (le bandeau ci-dessus le propose aussi dans les onglets ouverts).

## Connexion

**J'ai oublié mon mot de passe.**
Demander à un administrateur de le réinitialiser : il vous transmet un mot de passe provisoire, à changer à la connexion.

**La connexion est refusée alors que le mot de passe est bon.**
Après 5 échecs consécutifs depuis votre poste, le compte y est bloqué quelques minutes, et le nombre de tentatives par minute est limité par adresse IP. Attendre puis réessayer. Les échecs d'une autre personne, depuis un autre poste, ne vous bloquent pas.

**Le dépôt d'une pièce jointe est refusé (« limite de pièces jointes », « 24 heures », « espace disque »).**
Des plafonds protègent le serveur : total par MEP, dépôts d'un compte sur 24 heures, espace disque minimal. Supprimer des pièces inutiles, réessayer plus tard, ou demander à un administrateur de relever le plafond (console, onglet Paramètres ; voir aussi [Installer](Installer.md#configuration)).

**J'ai ouvert un lien vers une MEP et l'écran de connexion s'est affiché.**
Après la connexion, l'application ouvre la page du lien (aussi avec Microsoft).

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

**La taille affichée d'une pièce jointe ne correspond pas à la place qu'elle prend sur le serveur.**
Les pièces jointes qui s'y prêtent (journaux, textes, CSV, PDF, captures d'écran…) sont compressées sans perte sur le serveur ; la liste affiche la taille du fichier déposé, et l'info-bulle de cette taille la place qu'il occupe. Les quotas comptent la place occupée ; le téléchargement rend toujours le fichier d'origine, à l'octet près. Voir [Compression des pièces jointes](Exploiter.md#compression-des-pièces-jointes).

**Un fichier du volume `pieces-jointes` est illisible quand je l'ouvre directement.**
Il est stocké compressé (gzip) : `zcat <identifiant> > fichier` le rend tel qu'il a été déposé. Pour tout remettre en clair sur le disque : `gestion-mep pieces-jointes decompresser`, puis `gestion-mep reglages definir pieces-compression false`.

**L'envoi d'une pièce jointe échoue (fichier trop volumineux).**
La taille maximale (20 Mo par défaut) se règle dans la console (onglet Paramètres), par `gestion-mep reglages definir pieces-taille-max-mo <Mo>` ou par `PIECES_TAILLE_MAX_MO` dans `.env`. Le proxy doit accepter des requêtes un peu plus grandes.

**Une connexion à une API externe échoue : « adresse refusée », « certificat refusé » ou « outil injoignable ».**
« Adresse refusée » : l'outil est sur le réseau interne ; déclarer son réseau dans `api-reseaux-autorises` (console, onglet Paramètres, ou `gestion-mep reglages definir api-reseaux-autorises 10.20.0.0/16`). « Certificat refusé » : le certificat de l'outil est signé par l'autorité de certification de l'entreprise, à ajouter au serveur (voir [Autorité de certification interne](Exploiter.md#autorité-de-certification-interne)). « Outil injoignable » ou « pas de réponse » : pare-feu sortant, ou proxy à régler (`api-proxy`). « Essayer » dans l'éditeur de la connexion vérifie le résultat. Voir [API externes](API-externes.md).

**Les appels aux API externes restent « en attente ».**
Le réglage `api-envois-actifs` est peut-être à `false` (la carte Réseau de l'onglet API externes le signale) : le remettre à `true`, les appels en file partent aussitôt. Sinon, l'outil appelé répond en erreur passagère et l'appel attend sa prochaine tentative (heure indiquée dans le détail de l'appel).

**Tout le monde est déconnecté après chaque redéploiement.**
Le dossier des clés de session n'est pas conservé : vérifier le volume `cles` (ou `Cles__Dossier`).

**L'application ne démarre pas.**
Lire `docker compose logs app`, puis lancer `docker compose exec app gestion-mep config verifier`, qui contrôle la base, les migrations, les dossiers et la présence d'un administrateur.

**Une sauvegarde `avant-mise-a-jour-…` ou `avant-import-…` est restée dans le volume `sauvegardes`.**
Les contrôles après la mise à jour ou l'importation ont trouvé un écart (table disparue, lignes en moins, fichier joint manquant) : il est décrit dans le journal d'administration (type `donnees.controle-echoue`) et dans `docker compose logs app`. Garder l'archive et, si des données manquent, la recharger : voir [Exploiter](Exploiter.md#restaurer-ou-déménager).

Cas particulier : en 1.6.0, le contrôle signalait à tort « table Livrables disparue » sur toute installation qui avait des livrables. Ce n'était pas une perte (les livrables sont repris dans les objets impactés, voir [Exploiter](Exploiter.md#mettre-à-jour)) et les versions suivantes ne le signalent plus. Si c'est le seul écart, vérifier que les livrables apparaissent bien dans les objets impactés des MEP, puis supprimer l'archive.

**Peut-on lancer plusieurs instances derrière un répartiteur de charge ?**
Non : une seule instance à la fois sur une même base, y compris pendant une mise à jour (pas de déploiement progressif). Les verrous d'édition des étapes, le temps réel, les limites de connexion et les plafonds de dépôt sont tenus en mémoire, les pièces jointes sont sur un disque local, et les migrations tournent au démarrage. Voir [Installer](Installer.md#limites-connues).

**Peut-on se connecter avec les comptes de l'entreprise (SSO) ?**
Oui, avec Microsoft Entra ID ou un autre fournisseur OpenID Connect, sans que ce soit obligatoire : voir [Connexion Microsoft](Connexion-Microsoft.md).

Une question sans réponse ici ? Ouvrir un ticket sur le dépôt GitHub.
