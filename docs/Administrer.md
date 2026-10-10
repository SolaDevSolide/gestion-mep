# Administrer

Cette page s'adresse aux **administrateurs** de l'application. Les mêmes opérations sur les comptes existent en ligne de commande pour l'équipe informatique : voir [Exploiter](Exploiter.md#commandes-dadministration).

Être administrateur donne accès à la console et aux paramètres globaux, mais ne donne pas les droits du pilote dans une MEP (go / no-go, retour arrière, clôture…). Voir [Droits par rôle](Droits.md).

## Le premier administrateur

Il est créé au premier démarrage, sur une base vide, à partir des variables `ADMIN_LOGIN` et `ADMIN_MOT_DE_PASSE` du fichier `.env` (voir [Installer](Installer.md)). Il crée ensuite les autres comptes.

## Console d'administration

Menu « Administration › Console d'administration », visible des seuls administrateurs. Trois onglets.

### Comptes

- **Nouveau compte** : identifiant, nom affiché, rôle (par exemple « Développeur API »), contact, case « Administrateur ».
- **Modifier**, **Réinitialiser le mot de passe**, **Désactiver**, **Réactiver**.
- Badges : Admin, Actif ou Désactivé, Mot de passe à changer.

À la création et à la réinitialisation, l'application génère un **mot de passe provisoire**, affiché une seule fois. Le transmettre par un canal sûr, puis cliquer « J'ai transmis le mot de passe ». À sa connexion, l'utilisateur doit choisir son propre mot de passe (10 caractères au moins) avant de pouvoir faire quoi que ce soit.

Règles de sécurité :

- désactiver un compte ou réinitialiser son mot de passe ferme immédiatement ses sessions ;
- on ne peut pas désactiver son propre compte ;
- le dernier administrateur actif ne peut être ni désactivé ni rétrogradé.

### Paramètres

- **Nombre minimum d'approbations** exigées avant le Go (0 = aucune).
- Lien vers les critères go / no-go.

### Journal

Les 200 derniers événements d'administration : comptes créés, modifiés, désactivés, réactivés, mots de passe réinitialisés ou changés, paramètres et critères go / no-go modifiés. Les actions faites en ligne de commande y apparaissent avec l'auteur « Ligne de commande ».

## Paramètres partagés

Le menu « Paramètres » est visible de tous, en lecture seule pour les non-administrateurs, sauf les résolutions types.

| Page | Contenu | Qui modifie |
|---|---|---|
| Systèmes | Catalogue des systèmes de l'entreprise (nom, description, cible). Un système déjà utilisé par une MEP est désactivé au lieu d'être supprimé. | Administrateurs |
| Résolutions types | Fiches de dépannage réutilisables, avec l'historique de leurs versions. Une fiche déjà utilisée est désactivée au lieu d'être supprimée. | Tout le monde crée et modifie ; les administrateurs suppriment |
| Go / no-go | Critères à cocher avant chaque Go, et nombre minimum d'approbations. | Administrateurs |

## Profils

Chaque utilisateur gère son profil dans « Mon profil » : nom affiché, rôle, contact, **expertises** (systèmes du catalogue, avec un détail comme « Expert de la zone facturation ») et mot de passe. Les expertises servent à suggérer des responsables techniques lors de la préparation d'une MEP.
