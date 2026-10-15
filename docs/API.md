# API

L'interface web s'appuie entièrement sur une API HTTP JSON. Cette page en donne les conventions ; le détail de chaque route et de chaque objet est dans le contrat OpenAPI.

## Contrat OpenAPI

- Servi par l'application sur `/api/openapi.json` (session requise).
- Versionné dans le dépôt : [docs/openapi.json](openapi.json). Un test échoue si la copie ne correspond plus à l'API, elle est donc toujours à jour.
- Pour le parcourir, l'ouvrir dans un éditeur OpenAPI (Swagger Editor, Redocly, l'extension OpenAPI de VS Code…).

## Conventions

- Toutes les routes sont sous `/api`, en JSON (`camelCase`). Les énumérations s'écrivent en texte (`"Preparation"`, `"Conforme"`) ; un entier est refusé. Les dates sont en ISO 8601, renvoyées en UTC. Les identifiants sont des GUID.
- **Authentification** par cookie de session : `POST /api/auth/connexion` avec `{ login, motDePasse }`. Toutes les routes l'exigent, sauf la connexion, `/api/sante` et `/api/version`.
- **Protection CSRF** : toute requête qui modifie (autre que `GET`, `HEAD`, `OPTIONS`) doit porter l'en-tête `X-Requested-With` (valeur libre), sinon 403.
- **Réponse des écritures** : une modification d'une MEP renvoie la MEP complète, recalculée pour l'utilisateur courant (états des étapes, anomalies du plan, droits).
- **Concurrence** : chaque MEP porte une `version` et chaque étape une `revision`. Le client renvoie celle qu'il a lue ; si la ressource a changé entre-temps, la réponse est 409 et il faut relire. Une écriture qui croise seulement une autre écriture sur la même MEP (sur une autre étape, par exemple) est rejouée automatiquement par le serveur : elle n'aboutit en 409 que si ce qu'elle exige (révision de l'étape, version de la MEP) a réellement changé.
- **Temps réel** : `GET /api/meps/{id}/flux?session={onglet}` est un flux Server-Sent Events. Il envoie `event: maj` avec `data: {"version": n}` à chaque modification (le client relit alors la MEP) et `event: presence` avec la liste des personnes qui ont la MEP ouverte, à l'ouverture puis à chaque changement : `[{"utilisateurId": "…", "nom": "Bob Martin", "elements": ["etape:…"]}]`. `session` (facultatif, 64 caractères au plus) identifie l'onglet du navigateur.
- **Verrous d'édition** : le flux envoie aussi `event: verrous` avec la liste complète des verrous d'édition des étapes de la MEP, à chaque prise, prise de main, relâche ou expiration (voir « Graphe libre et sections »).
- **Présence** : informative, elle ne verrouille rien et vit avec le flux (fermer l'onglet la retire). `PUT /api/meps/{id}/presence` avec `{ session, elements }` déclare les éléments sur lesquels travaille l'onglet dont le flux est ouvert (liste vide : aucun) : 204, 404 si aucun flux de ce compte n'est ouvert avec cette session, 400 si une clé est invalide (10 au plus, 100 caractères au plus, lettres, chiffres, `:`, `-`, `_`, `.`). Clés utilisées par le front : `etape:{id}`, `preparation:{section}`, `retour:fiche`, `recette:{idFiche}`. `GET /api/meps/{id}/presence` renvoie la même liste que l'événement.
- **Journal** : toute écriture est inscrite dans le journal de la MEP (`GET /api/meps/{id}/journal`).

## Codes de réponse

| Code | Sens |
|---|---|
| 400 | Saisie invalide (champ manquant, JSON mal formé, valeur inconnue). |
| 401 | Pas de session. |
| 403 | Droit insuffisant, ou en-tête `X-Requested-With` absent. |
| 404 | Ressource introuvable. |
| 409 | Conflit de version : la ressource a changé depuis sa lecture. |
| 413 | Fichier ou requête trop volumineux. |
| 422 | Refus par une règle métier (par exemple démarrer une étape qui attend une autre étape). |
| 429 | Trop de tentatives de connexion. |

Les erreurs ont pour corps `{ "message": "…" }`, une phrase en français destinée à l'utilisateur. Quand un refus a plusieurs motifs (import d'un fichier de recette incohérent), le corps porte aussi `"erreurs": ["…"]`, un motif par ligne.

## Groupes de routes

| Préfixe | Contenu |
|---|---|
| `/api/auth` | Connexion, déconnexion, utilisateur courant, changement de mot de passe ; options de la page de connexion et connexion unique (`/api/auth/sso/…` : départ vers le fournisseur, liaison depuis le profil). |
| `/api/utilisateurs` | Liste des comptes (pour relier une personne à un compte) et profils. Chaque compte porte `comptePrincipalId` (compte principal de la même personne, ou null), `nomPersonne` (nom affiché de la personne) et `equipeIds`. |
| `/api/equipes` | Liste des équipes, de leurs membres (`membreIds`), de leur contact support et de leur signature, pour tout utilisateur connecté. |
| `/api/meps` | Liste (`?modele=true|false`, `?equipe=<id>` : MEP dont une personne fait partie de l'équipe ; chaque résumé porte `equipeIds` et `personnes`, noms et identifiants des personnes et de leurs comptes reliés). Chaque résumé porte aussi `toucheProduction`, vrai quand un système de la MEP touche un environnement de production. MEP : création, duplication, préparation (participants, systèmes, environnements d'un système `PUT /api/meps/{id}/systemes/{sid}/environnements`, objets impactés d'un système `PUT /api/meps/{id}/systemes/{sid}/objets` et `DELETE /api/meps/{id}/objets/{oid}`, classeur Excel des objets `GET /api/meps/{id}/objets/excel` (`?modele=true` pour le modèle vide) et import `POST /api/meps/{id}/objets/excel` en `multipart/form-data`, champ `fichier`, qui renvoie les objets ajoutés ou mis à jour et les lignes refusées, sauvegarde, étapes et graphe (voir « Graphe libre et sections » plus bas), approbations, recette : fiches, insertion d'un modèle `POST /api/meps/{id}/fiches-recette/depuis-modele`, export Excel `GET /api/meps/{id}/recette/excel`, aperçu puis import de la validation `POST /api/meps/{id}/recette/excel/apercu` et `…/import` en `multipart/form-data`, champ `fichier`, 5 Mo au plus), exécution (démarrer, terminer, prendre la main, résolutions), phases, retour arrière, clôture, pièces jointes, journal, flux et présence. |
| `/api/meps/{id}/communication`, `/api/meps/{id}/variables-texte` | Section Communication (`PUT`, avec `version`) et valeurs des variables des modèles de texte pour la MEP (`GET ?equipe=<id>` : équipe dont on prend la signature et le contact ; la réponse indique l'équipe proposée, celle du pilote). |
| `/api/modeles-texte` | Modèles de texte : catalogue des variables (`GET /api/modeles-texte/variables`), liste d'une équipe (`?equipe=<id>`), création, modification (`version`, 409 en cas de conflit), `POST /api/modeles-texte/{id}/retablir` (modèles fournis), suppression (422 pour un modèle fourni). Contact support et signature d'une équipe : `PUT /api/equipes/{id}/communication`. Voir [Modèles de texte](Modèles-de-texte.md). |
| `/api/catalogue/systemes` | Catalogue global des systèmes, avec les environnements, les catégories et le nombre d'objets de leur fiche. Fiche : objets `GET`/`POST /api/catalogue/systemes/{id}/objets`, `PUT`/`DELETE …/objets/{oid}`, environnements `PUT …/environnements`, catégories `PUT …/categories` (renommage par `ancien`). Catégories par défaut : `GET`/`PUT /api/catalogue/categories`. Les écritures sont réservées aux administrateurs. |
| `/api/resolutions` | Résolutions types et leurs versions. Chaque résolution porte `utilisations` (nombre de MEP où elle est liée à une étape) et `creeeLe` ; la liste est triée par utilisations, puis par création. |
| `/api/modeles-recette` | Bibliothèque de recettes : modèles de fiches par système du catalogue, création depuis une fiche (`POST /api/modeles-recette/depuis-fiche`), suppression réservée aux administrateurs. Chaque modèle porte `utilisations` (nombre de MEP où il a été inséré) et `creeLe` ; la liste est triée par utilisations, puis par création. |
| `/api/etapes-modeles` | Bibliothèque d'étapes : étapes modèles par systèmes du catalogue (`?systemeCatalogueId=` : celles qui touchent ce système), triées par `utilisations` puis `creeLe`. Création et modification par tout utilisateur (`version` obligatoire en modification, 409 si périmée), création depuis une étape d'une MEP (`POST /api/etapes-modeles/depuis-etape { mepId, etapeId, action }`), suppression réservée aux administrateurs. Insertion d'une copie dans le plan : `POST /api/meps/{id}/etapes/depuis-modele { modeleId, plan, apres, entre, paralleleA, sectionId }` (même place qu'une étape créée, voir « Graphe libre et sections »), qui renvoie la MEP ; l'étape copiée porte `modeleEtapeId`. |
| `/api/parametres`, `/api/criteres-go-no-go` | Paramètres globaux. |
| `/api/pieces-jointes` | Téléchargement d'une pièce jointe. |
| `/api/admin` | Console d'administration : comptes (dont `POST /api/admin/utilisateurs/{id}/relier { comptePrincipalId, desactiver }` et `POST …/{id}/delier` pour les comptes d'une même personne), équipes (`POST /api/admin/equipes`, `PUT` et `DELETE /api/admin/equipes/{id}`, saisie `{ nom, description, membreIds }`, `membreIds` absent : membres inchangés), journal d'administration, vérification des mises à jour, réglages de la connexion unique (`/api/admin/sso`) (administrateurs seulement). |
| `/api/application` | Version en cours, lien de signalement et, pour un administrateur, dernière version publiée. |
| `/api/demo` | Mode démonstration uniquement (`DEMO=true` ; sinon 404 comme une route inconnue) : `GET /api/demo` (sans session) donne les comptes de démonstration ; `POST /api/demo/connexion { login }` ouvre la session d'un compte de démonstration, avec les mêmes protections que `/api/auth/connexion` (en-tête anti-CSRF, limite par adresse, blocage progressif), 403 pour tout autre login ; `POST /api/demo/reinitialiser` (administrateurs) remet la base dans son état initial et ferme la session. |
| `/api/sante`, `/api/version` | Sonde de santé et empreinte du front servi (pour proposer de recharger un onglet après un déploiement). |

Exports Excel des vues, en lecture seule (classeur `.xlsx`, tout utilisateur connecté) : `GET /api/meps/excel` (liste, avec les filtres de la page d'accueil : `?modele=true`, `?equipe=<id>`, `?phase=Preparation|Execution|Recette|RetourArriere|Cloturee`, `?type=complete|simplifiee`, `?recherche=` sans accents ni casse ; 400 pour une phase ou un type inconnu, 404 pour une équipe inconnue), `GET /api/meps/{id}/preparation/excel`, `GET /api/meps/{id}/etapes/excel`, `GET /api/meps/{id}/retour-arriere/excel` et `GET /api/meps/{id}/journal/excel` (`?type=<famille>` : préfixe du type d'événement, `etape`, `recette`… ; `?recherche=` : dans le détail, l'auteur et son compte). La recette et les objets impactés gardent leurs routes d'export et d'import ci-dessus.

`GET /api/meps/{id}/tests-unitaires` donne le suivi des tests unitaires d'une MEP : nombre de tests faits et non faits, nombre de simples vérifications, puis pour chaque test l'étape, son état, le résultat attendu, le résultat constaté et l'exécutant. Chaque étape de la MEP porte aussi `fin` (`TestUnitaire` ou `Verification`) et `resultatConstate` ; `gestion-mep meps exporter` reprend ce suivi (`testsUnitaires`).

Plan de retour arrière facultatif : `PUT /api/meps/{id}` accepte `retourArriereActive` (absent : inchangé ; modifiable seulement en préparation) et `echeanceRepli` à `null` (sans échéance). Une MEP (`GET /api/meps/{id}`) et chaque ligne de `GET /api/meps` portent `retourArriereActive`. Sans plan de retour arrière, `POST /api/meps/{id}/retour-arriere` répond 422. L'activation et la désactivation sont inscrites au journal de la MEP (types `repli.active` et `repli.desactive`).

MEP complète ou simplifiée : une MEP (`GET /api/meps/{id}`) et chaque ligne de `GET /api/meps` portent `type` (`Complete` ou `Simplifiee` ; les MEP existantes sont complètes). `POST /api/meps` accepte `type` (absent : `Complete` ; 400 pour un modèle simplifié). `PUT /api/meps/{id}/type { version, type }` change le type en préparation (422 après, ou pour un modèle ; 403 pour qui n'est ni pilote, ni suppléant, ni administrateur, quand un pilote est désigné) : vers `Complete`, tout est conservé et les suppléants implicites deviennent `suppleantIds` ; vers `Simplifiee`, durées, criticités, résolutions liées des étapes, versions et suppléants choisis sont effacés. Le journal reçoit un événement `mep.type`. Dans une MEP simplifiée, `suppleantIds` liste les suppléants implicites (toutes les personnes sauf le pilote, aucune sans pilote), `PUT /api/meps/{id}` ignore `versionActuelle`, `versionCible` et `suppleantIds`, une étape enregistrée perd sa durée, sa criticité et ses résolutions liées, et `POST /api/meps/{id}/etapes/{eid}/resolutions` accepte toute résolution type active d'un système de l'étape ou sans système.

L'API ne propose pas encore de jeton d'accès : un script doit se connecter et conserver le cookie de session.

## Graphe libre et sections

Le plan est un graphe libre : seuls les liens comptent (« B attend A »). `ligne` et `ordre` d'une étape sont en lecture seule : ils sont recalculés après chaque opération (profondeur dans le graphe, puis place précédente) et servent d'indice d'ordre à l'affichage et aux exports. Il n'y a plus de règle « un lien descend toujours » : seuls un cycle, une étape qui s'attend elle-même et un lien vers l'autre plan sont refusés (422, rien n'est modifié). Les opérations par ligne (`…/placer`, `ligne`, `position`, `nouvelleLigne`) n'existent plus.

Une étape porte `dependances` (préalables **effectifs**, seuls utilisés par l'exécution), `dependancesDerivees` (la part qui vient des cercles de section), `sectionId` (null : hors section) et `portsAttendus` (cercles de sortie d'autres sections qu'elle attend). Une MEP porte `sections` : `{ id, plan, titre, description, systemeIds, auteurId, auteur, creeLe, placee, revision, ports }`, chaque cercle (`ports`) valant `{ id, sens: Entree|Sortie, titre, description, ordre, etapes, sources }`. Une section est un repère : elle ne s'exécute pas ; ses cercles donnent des préalables à ses étapes. Préalables effectifs d'une étape = ses préalables directs, plus, pour chaque cercle d'entrée de sa section qui la contient, ce qui y arrive (`sources` : une étape, ou les étapes d'un cercle de sortie d'une autre section), plus les étapes de chaque cercle de sortie qu'elle attend.

| Requête | Effet |
|---|---|
| `POST /api/meps/{id}/etapes` | Crée une étape (champs de la fiche) à une place, au plus une : `apres` (Id d'étape ou de cercle : elle l'attend), `entre { de, vers }` (insertion sur ce lien : `vers` attend la nouvelle étape, qui attend `de`), `paralleleA` (mêmes préalables et mêmes suivants que cette étape) ; rien : isolée. `sectionId` : sa section (absente : celle de la place). Dans une section, une étape sans préalable dans la section attend le premier cercle d'entrée et une étape que rien n'attend est attendue par le premier cercle de sortie. `dependances` (préalables explicites) ne se combine pas avec une place (400). |
| `PUT /api/meps/{id}/etapes/{eid}` | Fiche de l'étape (`revision` obligatoire). `dependances` : préalables directs ; ceux qui viennent des cercles restent. |
| `POST /api/meps/{id}/etapes/{eid}/deplacer` | `{ apres, surLien { de, vers }, paralleleA, sectionId, sortirDeSection }` : la carte quitte sa place (ce qui l'attendait attend ce qu'elle attendait, sans lien redondant), change de section si demandé (sinon celle de sa nouvelle place), puis s'insère. |
| `DELETE /api/meps/{id}/etapes/{eid}` | Supprime l'étape : ce qui l'attendait attend ce qu'elle attendait. |
| `POST /api/meps/{id}/liens` et `…/liens/retirer` | `{ de, vers }` : ajoute ou retire le lien « `vers` attend `de` ». Liens possibles : étape → étape ; étape hors de la section, ou cercle de sortie d'une autre section → cercle d'entrée ; cercle d'entrée → étape de sa section ; étape de la section → cercle de sortie ; cercle de sortie → étape hors de sa section. 404 si un Id est inconnu ; 422 pour un lien impossible, un cycle, ou un préalable qui ne vient que d'un cercle (« délier le cercle »). |
| `POST /api/meps/{id}/sections` | `{ plan, titre, description, systemeIds, placee }` (`placee` absent : dans le bac). 201 `{ sectionId, mep }`. La section reçoit un cercle d'entrée « Début » et un cercle de sortie « Fin ». |
| `PUT /api/meps/{id}/sections/{sid}` | `{ revision, titre, description, systemeIds }` (409 si la révision est périmée). |
| `PUT /api/meps/{id}/sections/{sid}/placement` | `{ placee }` : placer dans le plan, ou remettre dans le bac. Une section restée dans le bac bloque le Go (anomalie de « Vérifier le plan »). |
| `DELETE /api/meps/{id}/sections/{sid}` | Ses étapes restent, hors section, avec leurs préalables effectifs (ceux des cercles deviennent directs). |
| `POST /api/meps/{id}/sections/{sid}/ports` | `{ sens, titre, description }` (titre absent : « Entrée 2 »…). |
| `PUT /api/meps/{id}/sections/{sid}/ports/{pid}` | `{ titre, description }`. |
| `PUT /api/meps/{id}/sections/{sid}/ports/ordre` | `{ sens, ports }` : tous les cercles de ce bord, de gauche à droite. |
| `DELETE /api/meps/{id}/sections/{sid}/ports/{pid}` | Les liens qui passaient par le cercle disparaissent. |
| `POST /api/meps/{id}/etapes/{eid}/verrou` | `{ session, prendreLaMain }` : verrou d'édition de l'étape pour l'onglet `session` (celui du flux). Renvoie `{ pris, verrou }` ; tenu par un autre onglet, `pris` vaut faux sauf avec `prendreLaMain` (sans confirmation, le verrou garde alors `reprisDeNom`, `reprisDeSession`). Renouveler toutes les 20 s environ : il expire après une minute sans signe de vie, et à la fermeture du dernier flux de l'onglet. Indicatif : la révision protège toujours l'enregistrement. |
| `DELETE /api/meps/{id}/etapes/{eid}/verrou?session=` | Relâche le verrou s'il est tenu par cet onglet (204). |
| `GET /api/meps/{id}/verrous` | Verrous en cours (même liste que l'événement `verrous`). |

Ces opérations se font en préparation (422 ensuite), avec les droits d'édition du contenu (403), remettent les approbations à zéro et sont inscrites au journal (`etape.creee`, `etape.deplacee`, `plan.lien-ajoute`, `plan.lien-retire`, `section.creee`, `section.renommee`, `section.modifiee`, `section.placee`, `section.bac`, `section.supprimee`, `section.cercle-ajoute`, `section.cercle-modifie`, `section.cercle-supprime`). Les étapes dont les préalables effectifs changent prennent une révision de plus ; une section prend une révision de plus à chaque changement (cercles et liens compris). Avertissements de « Vérifier le plan » : cercle sans étape, cercle d'entrée où rien n'arrive.
