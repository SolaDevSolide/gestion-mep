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
- **Temps réel** : `GET /api/meps/{id}/flux` est un flux Server-Sent Events. Il envoie `event: maj` avec `data: {"version": n}` à chaque modification ; le client relit alors la MEP.
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

Les erreurs ont pour corps `{ "message": "…" }`, une phrase en français destinée à l'utilisateur.

## Groupes de routes

| Préfixe | Contenu |
|---|---|
| `/api/auth` | Connexion, déconnexion, utilisateur courant, changement de mot de passe ; options de la page de connexion et connexion unique (`/api/auth/sso/…` : départ vers le fournisseur, liaison depuis le profil). |
| `/api/utilisateurs` | Liste des comptes (pour relier une personne à un compte) et profils. |
| `/api/meps` | MEP : création, duplication, préparation (participants, systèmes, livrables, sauvegarde, étapes, approbations, recette), exécution (démarrer, terminer, prendre la main, résolutions), phases, retour arrière, clôture, pièces jointes, journal, flux. |
| `/api/catalogue/systemes` | Catalogue global des systèmes. |
| `/api/resolutions` | Résolutions types et leurs versions. |
| `/api/parametres`, `/api/criteres-go-no-go` | Paramètres globaux. |
| `/api/pieces-jointes` | Téléchargement d'une pièce jointe. |
| `/api/admin` | Console d'administration : comptes, journal d'administration, vérification des mises à jour, réglages de la connexion unique (`/api/admin/sso`) (administrateurs seulement). |
| `/api/application` | Version en cours, lien de signalement et, pour un administrateur, dernière version publiée. |
| `/api/demo` | Mode démonstration uniquement (`DEMO=true` ; sinon 404 comme une route inconnue) : `GET /api/demo` (sans session) donne les comptes de démonstration ; `POST /api/demo/connexion { login }` ouvre la session d'un compte de démonstration, avec les mêmes protections que `/api/auth/connexion` (en-tête anti-CSRF, limite par adresse, blocage progressif), 403 pour tout autre login ; `POST /api/demo/reinitialiser` (administrateurs) remet la base dans son état initial et ferme la session. |
| `/api/sante`, `/api/version` | Sonde de santé et empreinte du front servi (pour proposer de recharger un onglet après un déploiement). |

L'API ne propose pas encore de jeton d'accès : un script doit se connecter et conserver le cookie de session.
