# Administrer

Cette page s'adresse aux **administrateurs** de l'application. Les mêmes opérations sur les comptes existent en ligne de commande pour l'équipe informatique : voir [Exploiter](Exploiter.md#commandes-dadministration).

Être administrateur donne accès à la console et aux paramètres globaux, mais ne donne pas les droits du pilote dans une MEP (go / no-go, retour arrière, clôture…). Voir [Droits par rôle](Droits.md).

## Le premier administrateur

Il est créé au premier démarrage, sur une base vide, à partir des variables `ADMIN_LOGIN` et `ADMIN_MOT_DE_PASSE` du fichier `.env` (voir [Installer](Installer.md)). Sans `ADMIN_MOT_DE_PASSE`, un mot de passe provisoire est écrit dans le journal de l'application. Dans tous les cas, il est à changer à la première connexion. Il crée ensuite les autres comptes.

## Console d'administration

Menu « Administration › Console d'administration », visible des seuls administrateurs. Cinq onglets.

![Console d'administration, onglet Comptes](images/console-admin.png)

### Comptes

- **Nouveau compte** : identifiant, nom affiché, rôle (par exemple « Développeur API »), contact, case « Administrateur ».
- **Modifier**, **Réinitialiser le mot de passe**, **Désactiver**, **Réactiver**.
- Badges : Admin, Actif ou Désactivé, Mot de passe à changer.

À la création et à la réinitialisation, l'application génère un **mot de passe provisoire**, affiché une seule fois. Le transmettre par un canal sûr, puis cliquer « J'ai transmis le mot de passe ». À sa connexion, l'utilisateur doit choisir son propre mot de passe (10 caractères au moins) avant de pouvoir faire quoi que ce soit.

Règles de sécurité :

- désactiver un compte ou réinitialiser son mot de passe ferme immédiatement ses sessions ;
- on ne peut pas désactiver son propre compte ;
- le dernier administrateur actif ne peut être ni désactivé ni rétrogradé ;
- un nom affiché ne peut pas reprendre le nom affiché ou l'identifiant d'un autre compte, pour que personne ne signe les journaux à la place d'un autre.

### Connexion unique

Connexion Microsoft (SSO) facultative : adresse de retour à déclarer dans Microsoft Entra, ID de l'annuaire, ID d'application, secret client (chiffré, jamais réaffiché, effacé si l'annuaire ou l'application change) et sa date d'expiration, création automatique des comptes, rôle d'application qui donne le rôle administrateur. **Tester** vérifie les réglages auprès de Microsoft sans connecter personne ; **Activer** affiche le bouton sur la page de connexion, sans redémarrage. La liste « Comptes liés » permet de supprimer une liaison. Le pas-à-pas pour l'équipe informatique est dans [Connexion Microsoft](Connexion-Microsoft.md).

### Paramètres

- **Nombre minimum d'approbations** exigées avant le Go (0 = aucune).
- **Réglages du serveur**, appliqués sans redémarrage : adresse publique, réseaux du reverse proxy, HTTPS obligatoire, tailles et plafonds des pièces jointes, tentatives de connexion par minute, écrans suivis en temps réel, vérification des nouvelles versions, lien « Signaler un problème ou une idée ». Chaque réglage indique d'où vient sa valeur : fichier `.env`, valeur par défaut, ou modifiée dans la console (date et auteur). **Rétablir** revient à la valeur du fichier `.env`. Les mêmes réglages se modifient en ligne de commande (`gestion-mep reglages`, voir [Exploiter](Exploiter.md#modifier-un-réglage)).
- Rappel des réglages lus seulement au démarrage (port, adresse d'écoute, base de données, démonstration…), à changer dans le fichier `.env`.
- Lien vers les critères go / no-go.

### Journal

Les 200 derniers événements d'administration : comptes créés, modifiés, désactivés, réactivés, renommés par leur titulaire, mots de passe réinitialisés ou changés, paramètres, réglages du serveur et critères go / no-go modifiés, réglages de la connexion unique, comptes liés ou déliés, comptes créés à la première connexion par Microsoft, contrôles des données après une mise à jour ou une importation. Chaque auteur est suivi de son identifiant, qui ne change jamais. Les actions faites en ligne de commande y apparaissent avec l'auteur « Ligne de commande ».

L'application y signale aussi, avec l'auteur « Application », **20 échecs de connexion en moins d'une heure sur un même compte**, toutes adresses confondues : signe probable qu'on essaie de deviner son mot de passe.

### Version

Version en cours, dernière version publiée sur GitHub et date de la dernière vérification. Quand une version plus récente existe, la barre latérale affiche « Mise à jour … disponible » et l'onglet donne le lien vers ses nouveautés. « Vérifier maintenant » interroge GitHub sans attendre.

Si l'équipe informatique a activé le service de mise à jour, le bouton **« Mettre à jour vers … »** installe la nouvelle version après confirmation : l'application redémarre (les personnes connectées sont déconnectées environ une minute), une sauvegarde est faite avant toute migration, et la version actuelle est remise si la nouvelle ne démarre pas. Le bouton est refusé tant qu'une MEP est en cours. Sans ce service, l'onglet renvoie à la marche à suivre manuelle ([Mettre à jour](Exploiter.md#mettre-à-jour)).

L'onglet rappelle aussi le lien « Signaler un problème ou une idée », proposé à tout le monde dans le menu utilisateur.

## Paramètres partagés

Le menu « Paramètres » est visible de tous, en lecture seule pour les non-administrateurs, sauf les résolutions types.

| Page | Contenu | Qui modifie |
|---|---|---|
| Systèmes | Catalogue des systèmes de l'entreprise (nom, description, cible). Un système déjà utilisé par une MEP est désactivé au lieu d'être supprimé. | Administrateurs |
| Résolutions types | Fiches de dépannage réutilisables, avec l'historique de leurs versions. Une fiche déjà utilisée est désactivée au lieu d'être supprimée. | Tout le monde crée et modifie ; les administrateurs suppriment |
| Go / no-go | Critères à cocher avant chaque Go, et nombre minimum d'approbations. | Administrateurs |

## Profils

Chaque utilisateur gère son profil dans « Mon profil » : nom affiché, rôle, contact, **expertises** (systèmes du catalogue, avec un détail comme « Expert de la zone facturation ») et mot de passe. Quand la connexion unique est active, « Lier mon compte » relie un compte local à son compte Microsoft ; ensuite le bouton « Se connecter avec Microsoft » ouvre ce compte. Les expertises servent à suggérer des responsables techniques lors de la préparation d'une MEP.
