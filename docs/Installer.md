# Installer

Cette page s'adresse à l'équipe informatique qui installe l'application sur un serveur. Pour l'essayer sur un poste, voir le [démarrage rapide](Démarrage-rapide.md). Les opérations courantes (mises à jour, sauvegardes, comptes) sont dans [Exploiter](Exploiter.md). Sans Docker, sur un serveur Linux ou Windows, voir [Installer sans Docker](Installer-sans-Docker.md).

## Vue d'ensemble

L'application tient dans deux conteneurs, décrits par le fichier `docker-compose.yml` joint à chaque version publiée :

| Conteneur | Contenu | Données |
|---|---|---|
| `app` | API et interface web, image `ghcr.io/soladevsolide/gestion-mep` | volumes `pieces-jointes` (fichiers joints), `cles` (clés de session et du secret de la connexion Microsoft) et `sauvegardes` (archives de la base et des pièces jointes) |
| `db` | PostgreSQL 16 | volume `db-data` |
| `mise-a-jour` (facultatif, désactivé par défaut) | Service du bouton « Mettre à jour » de la console, image `ghcr.io/soladevsolide/gestion-mep-mise-a-jour` (voir [Mettre à jour depuis la console](Exploiter.md#mettre-à-jour-depuis-la-console)) | volume `mise-a-jour` (échanges avec l'application) |

L'application écoute en HTTP sur le port 8080. Le chiffrement HTTPS est confié à un proxy inverse placé devant elle. Le schéma de la base est créé et mis à jour automatiquement au démarrage. Seule tâche à prévoir : la [sauvegarde de chaque nuit](Exploiter.md#sauvegarder), copiée hors du serveur.

**Une seule instance** de l'application doit tourner à la fois sur une même base : les verrous d'édition des étapes, le flux temps réel, les limites de connexion et les plafonds de dépôt sont tenus en mémoire, et les pièces jointes sont sur le disque du conteneur (voir [Limites connues](#limites-connues)).

## Prérequis

- Une machine virtuelle **Linux** avec **Docker Engine** et le plugin **Docker Compose v2**. Les images sont des images Linux : sur Windows Server, Docker Engine n'exécute que des conteneurs Windows et ne peut pas les lancer ; utiliser alors l'[installation sans Docker](Installer-sans-Docker.md). Docker Desktop (WSL 2) convient pour un essai sur un poste de travail, pas pour un serveur.
- 2 vCPU et 4 Go de mémoire suffisent pour une équipe. Prévoir l'espace disque des pièces jointes.
- Un accès sortant à `ghcr.io` pour récupérer l'image, ou une copie de l'image dans un registre interne. Facultatif : un accès sortant à `api.github.com` pour signaler les nouvelles versions aux administrateurs.
- Un proxy inverse HTTPS (nginx, IIS, Traefik, un répartiteur de charge…) et un nom DNS.

### Sur AWS

- Une instance EC2 (par exemple `t3.medium`) avec Docker suffit, avec un volume EBS sauvegardé par instantanés.
- Le HTTPS peut être porté par un Application Load Balancer avec un certificat ACM : déclarer alors le sous-réseau du répartiteur dans `PROXY_RESEAUX` et régler son délai d'inactivité à 3600 secondes pour le flux temps réel (voir [Proxy inverse HTTPS](#proxy-inverse-https)).
- Pour utiliser Amazon RDS for PostgreSQL au lieu du conteneur `db`, voir [Base de données externe](#base-de-données-externe).

## Installation

1. Télécharger, dans un dossier du serveur (par exemple `/opt/gestion-mep`), les deux fichiers joints à la version voulue sur la page *Releases* : `docker-compose.yml` et `env.example` (GitHub renomme les fichiers joints dont le nom commence par un point). Ils tirent les images de cette version précise, sans rien construire ; leur empreinte est dans `SHA256SUMS`.

   ```bash
   v=1.9.0
   curl -fLO https://github.com/SolaDevSolide/gestion-mep/releases/download/v$v/docker-compose.yml
   curl -fL -o .env.example https://github.com/SolaDevSolide/gestion-mep/releases/download/v$v/env.example
   ```

   Le `docker-compose.yml` de la branche `main` du dépôt sert au développement : il construit les images sur place. Les versions publiées jusqu'à la 1.8.0 n'ont pas ces fichiers joints : prendre alors ceux du dépôt à l'étiquette de la version (`https://raw.githubusercontent.com/SolaDevSolide/gestion-mep/v1.8.0/docker-compose.yml`) et fixer `MEP_IMAGE` dans `.env`.
2. Créer `.env` à partir de `.env.example`, lisible seulement par le compte qui lance Docker (il contient des mots de passe), puis le compléter (voir [Configuration](#configuration)). Au minimum : `POSTGRES_PASSWORD` et `ADMIN_LOGIN` (`MEP_IMAGE` y est déjà fixée à la version téléchargée).

   ```bash
   install -m 600 .env.example .env          # Linux : droits réservés au propriétaire
   ```
3. Démarrer :

   ```bash
   docker compose pull
   docker compose up -d
   ```

4. Vérifier :

   ```bash
   curl http://localhost:8080/api/sante/prete      # doit répondre {"statut":"prete",…} : base joignable et à jour
   docker compose ps                                # colonne STATUS : healthy
   docker compose exec app gestion-mep config verifier
   ```

5. Configurer le [proxy inverse HTTPS](#proxy-inverse-https), puis se connecter avec le compte administrateur défini dans `.env`. Ce compte n'est créé qu'au premier démarrage, sur une base vide. Avec `ADMIN_MOT_DE_PASSE` vide (conseillé), son mot de passe provisoire est écrit dans le journal : `docker compose logs app | grep provisoire`. Dans tous les cas, il est à changer à la première connexion ; un mot de passe d'exemple (`changer-moi`, `admin`…) ou de moins de 10 caractères est refusé et le compte n'est pas créé. Créer ensuite les comptes des utilisateurs depuis la console d'administration (voir [Administrer](Administrer.md)), ou activer la [connexion Microsoft](Connexion-Microsoft.md) pour que chacun entre avec son compte d'entreprise.

## Configuration

Les variables se règlent dans `.env`, lu par `docker-compose.yml`. Celles marquées **(console)** sont aussi modifiables ensuite, sans redémarrage, dans la console d'administration (onglet Paramètres) ou par `gestion-mep reglages` (voir [Exploiter](Exploiter.md#modifier-un-réglage)) : la valeur modifiée remplace celle de `.env`, jusqu'à ce qu'on la rétablisse. Les autres ne sont lues qu'au démarrage : modifier `.env`, puis `docker compose up -d`.

| Variable `.env` | Défaut | Rôle |
|---|---|---|
| `MEP_IMAGE` | `gestion-mep:local` | Image à lancer. En production, une version publiée : `ghcr.io/soladevsolide/gestion-mep:1.0.0` (voir [Choisir une version](Exploiter.md#choisir-une-version)). |
| `PORT` | `8080` | Port publié sur l'hôte. |
| `ADRESSE_ECOUTE` | `127.0.0.1` | Adresse de l'hôte sur laquelle le port est publié. Par défaut, seule la machine elle-même y accède : le proxy HTTPS doit être installé dessus. Si le proxy est sur une autre machine, mettre l'adresse de la carte réseau qu'il joint (ou `0.0.0.0`) et n'ouvrir le port qu'au proxy dans le pare-feu. |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | `mep` / `mep` | Compte de la base. **Choisir un mot de passe long et aléatoire** avant le premier démarrage (`openssl rand -base64 24`). |
| `ADMIN_LOGIN` / `ADMIN_MOT_DE_PASSE` | vides | Compte administrateur créé au premier démarrage si la base est vide. Mot de passe vide : un mot de passe provisoire est écrit dans le journal. Retirer `ADMIN_MOT_DE_PASSE` de `.env` après le premier démarrage. |
| `DEMO` | `false` | `true` crée les comptes de démonstration et des MEP d'exemple dans chaque état, affiche le bandeau « Mode démonstration », la connexion rapide et la réinitialisation (voir [Démarrage rapide](Démarrage-rapide.md)). **Jamais en production.** |
| `PIECES_TAILLE_MAX_MO` (console) | `20` | Taille maximale d'une pièce jointe, en Mo. |
| `PIECES_QUOTA_MEP_MO` (console) | `1024` | Total des pièces jointes d'une MEP, en Mo (`0` : sans limite). |
| `PIECES_QUOTA_COMPTE_JOUR_MO` (console) | `1024` | Pièces jointes qu'un compte peut déposer en 24 heures, en Mo (`0` : sans limite). |
| `PIECES_ESPACE_LIBRE_MIN_MO` (console) | `2048` | Espace laissé libre sur le disque des pièces jointes : en dessous, les dépôts sont refusés pour protéger la base et les sauvegardes. |
| `PROXY_RESEAUX` (console) | `172.16.0.0/12` | Réseaux (CIDR, séparés par des virgules) du proxy HTTPS, seuls autorisés à transmettre l'adresse du client. Le défaut couvre un proxy sur le même hôte Docker ; sinon, indiquer l'adresse du proxy (`10.0.5.12/32`). |
| `VERIFIER_MISES_A_JOUR` (console) | `true` | Consulte les versions publiées sur GitHub pour signaler une mise à jour aux administrateurs. `false` sur un serveur sans accès à Internet. |
| `LIEN_SIGNALEMENT` (console) | formulaire GitHub du dépôt | Lien « Signaler un problème ou une idée » du menu utilisateur. Vide : lien masqué. Peut pointer vers un outil interne. |
| `COMPOSE_PROFILES` | vide | `mise-a-jour` active le bouton « Mettre à jour » de la console (voir [Mettre à jour depuis la console](Exploiter.md#mettre-à-jour-depuis-la-console)). |
| `MEP_IMAGE_MISE_A_JOUR` | image de la version (fichiers de la version publiée) | Image du service de mise à jour, à la même version que l'application. Le service la fait suivre lui-même après chaque mise à jour (si elle vient du même dépôt que l'application). |
| `MEP_DEPOT_IMAGE` | `ghcr.io/soladevsolide/gestion-mep` | Seul dépôt d'images que le service de mise à jour accepte d'installer. Pour un registre interne, y mettre le dépôt du miroir (voir [Registre interne](Exploiter.md#registre-interne-sans-github)). |
| `MEP_ATTENTE_SANTE` | `300` | Secondes pendant lesquelles le service de mise à jour attend que la nouvelle version soit prête, une fois la base migrée, avant de revenir à la précédente. La sauvegarde et la migration, faites avant, sont attendues sans limite. |
| `URL_PUBLIQUE` (console) | vide | Adresse publique de l'application (`https://mep.entreprise.fr`). Facultative : sert à l'adresse de retour de la [connexion Microsoft](Connexion-Microsoft.md) ; vide, elle est déduite de la requête. |
| `HTTPS_OBLIGATOIRE` (console) | `true` | `false` : mode d'essai, la session fonctionne aussi en http simple (voir [Essayer depuis un autre poste](#essayer-depuis-un-autre-poste-sans-https)). Les mots de passe circulent alors en clair. |
| `FLUX_MAX` (console) | `1000` | Écrans suivis en temps réel en même temps sur le serveur (20 au plus par compte). Au-delà, l'écran se met à jour à la reconnexion suivante. |
| `CONNEXIONS_PAR_MINUTE` (console) | `20` | Tentatives de connexion autorisées par minute et par adresse IP. Au-delà : refus temporaire. Un compte est en plus bloqué quelques minutes, depuis l'adresse qui a échoué, après 5 échecs consécutifs. |

**Charte visuelle** (facultatif) : pour donner à l'application les couleurs de l'entreprise dès l'installation, poser un fichier `charte.json` à côté de `docker-compose.yml` et décommenter la ligne `./charte.json:/etc/gestion-mep/charte.json:ro` du service `app`. Format, exemple et commandes : [Exploiter](Exploiter.md#charte-visuelle). Les administrateurs peuvent aussi tout régler dans la console (onglet Charte visuelle), sans fichier.

Pour un déploiement sans Docker Compose (Kubernetes, ECS…) :

- **une seule réplique**, et un remplacement complet lors d'une mise à jour (stratégie `Recreate` sous Kubernetes, `minimumHealthyPercent` à 0 et `maximumPercent` à 100 sous ECS) : deux instances en même temps, même pendant un déploiement progressif, laisseraient deux personnes prendre le verrou de la même étape, cesseraient de diffuser les mises à jour d'écran entre instances, multiplieraient les limites de connexion, et feraient tourner l'ancienne version sur une base déjà migrée ;
- des volumes persistants pour les pièces jointes, les clés et les sauvegardes ;
- sonde de vivacité (*liveness*) sur `GET /api/sante`, sonde de disponibilité (*readiness*) et contrôle du répartiteur de charge sur `GET /api/sante/prete` (503 tant que la base est injoignable ou pas au schéma de cette version) ; laisser un délai de démarrage suffisant pour la migration ;
- l'image se règle par ses variables d'environnement :

| Variable | Rôle |
|---|---|
| `ConnectionStrings__Mep` | Chaîne de connexion PostgreSQL (`Host=…;Port=5432;Database=…;Username=…;Password=…`). |
| `Demo__Activer` | Voir `DEMO`. |
| `Admin__Login`, `Admin__MotDePasse` | Voir `ADMIN_LOGIN`, `ADMIN_MOT_DE_PASSE`. |
| `Stockage__Dossier` | Dossier des pièces jointes dans le conteneur (`/data/pieces-jointes`), à monter sur un volume persistant. |
| `Stockage__TailleMaxMo` | Voir `PIECES_TAILLE_MAX_MO`. |
| `Stockage__QuotaMepMo`, `Stockage__QuotaCompteJourMo`, `Stockage__EspaceLibreMinMo` | Voir `PIECES_QUOTA_MEP_MO`, `PIECES_QUOTA_COMPTE_JOUR_MO`, `PIECES_ESPACE_LIBRE_MIN_MO`. |
| `Flux__ConnexionsMax` | Voir `FLUX_MAX`. |
| `Securite__HttpsObligatoire` | Voir `HTTPS_OBLIGATOIRE`. |
| `Sauvegardes__Dossier` | Dossier des sauvegardes (`/data/sauvegardes`), à monter sur un volume persistant avec assez de place pour une copie de la base et des pièces jointes : sauvegardes automatiques avant une mise à jour du schéma ou une importation, et défaut de `gestion-mep sauvegarder`. |
| `Cles__Dossier` | Dossier des clés qui chiffrent le cookie de session (`/data/cles`), à monter sur un volume persistant. Sans lui, chaque recréation du conteneur déconnecte tout le monde. |
| `ProxyInverse__Reseaux` | Voir `PROXY_RESEAUX`. |
| `Application__UrlPublique` | Voir `URL_PUBLIQUE`. |
| `Connexion__TentativesParMinute` | Voir `CONNEXIONS_PAR_MINUTE`. |
| `MisesAJour__Verifier` | Voir `VERIFIER_MISES_A_JOUR`. `MisesAJour__Depot` (`SolaDevSolide/gestion-mep`) et `MisesAJour__IntervalleHeures` (`12`) changent le dépôt consulté et la fréquence. |
| `Signalement__Lien` | Voir `LIEN_SIGNALEMENT`. |
| `MisesAJour__Dossier` | Dossier partagé avec le service de mise à jour (`/data/mise-a-jour`, volume `mise-a-jour`). |
| `CHARTE_FICHIER` (ou `Charte__Fichier`) | Fichier `charte.json` de la charte visuelle, s'il n'est pas monté sur `/etc/gestion-mep/charte.json`. |
| `Surveillance__Activer` | `true` par défaut : bascule automatique en retour arrière quand l'échéance d'une MEP est dépassée. Ne pas désactiver en production. |

### Base de données externe

Pour utiliser un serveur PostgreSQL 16 ou plus récent existant (ou Amazon RDS), créer une base et un compte propriétaire de cette base, puis, dans `docker-compose.yml`, supprimer le service `db` (et la dépendance `depends_on` du service `app`) et remplacer `ConnectionStrings__Mep` par la chaîne de connexion de ce serveur. `gestion-mep sauvegarder` reste la sauvegarde de référence : elle lit la base et les pièces jointes au même instant ; les instantanés du serveur (RDS) la complètent sans la remplacer.

## Proxy inverse HTTPS

Hors développement, le cookie de session est marqué `Secure` : **l'application ne fonctionne qu'à travers HTTPS** (seul `http://localhost` fait exception dans les navigateurs). Points à respecter :

- Transmettre les en-têtes `Host`, `X-Forwarded-For` et `X-Forwarded-Proto`, et déclarer le proxy dans `PROXY_RESEAUX` s'il n'est pas sur le même hôte Docker.
- **Ne pas mettre en tampon les réponses** de `/api/meps/*/flux` : c'est un flux d'événements (Server-Sent Events) qui reste ouvert. Sans cela, les écrans ne se mettent plus à jour tout seuls.
- Autoriser des connexions longues sur ce flux (délai de lecture d'au moins une heure).
- Autoriser des corps de requête un peu plus grands que la taille maximale d'une pièce jointe (21 Mo par défaut). L'application limite elle-même les autres requêtes à 1 Mo.
- Ne pas ajouter d'en-têtes `Content-Security-Policy` ou `X-Frame-Options` différents : l'application pose déjà les siens (politique de contenu, interdiction d'affichage dans un cadre, `nosniff`, `Referrer-Policy`, et HSTS sur les réponses HTTPS).
- Par défaut, le port n'est publié que sur `127.0.0.1` : l'application n'est joignable qu'à travers un proxy installé sur la même machine. Si le proxy est sur une autre machine, régler `ADRESSE_ECOUTE` (voir [Configuration](#configuration)) et n'autoriser que lui dans le pare-feu, pour que personne ne contourne le HTTPS.

### Essayer depuis un autre poste, sans HTTPS

Pour un essai sur le réseau avant d'installer le proxy :

1. Dans `.env`, `ADRESSE_ECOUTE=0.0.0.0`, puis `docker compose up -d` : le port est ouvert sur toutes les cartes réseau.
2. Désactiver « HTTPS obligatoire » dans la console (onglet Paramètres), ou `docker compose exec app gestion-mep reglages definir https-obligatoire non`.
3. Ouvrir `http://<adresse-du-serveur>:8080` depuis l'autre poste.

Les mots de passe circulent alors en clair sur le réseau. Une fois l'essai terminé, réactiver « HTTPS obligatoire » et remettre `ADRESSE_ECOUTE` à `127.0.0.1` (ou installer le proxy). `gestion-mep config verifier` signale ce mode tant qu'il est actif. La connexion Microsoft, elle, exige toujours une adresse en https.

Exemple nginx :

```nginx
server {
    listen 443 ssl;
    server_name mep.exemple.fr;
    # ssl_certificate … ; ssl_certificate_key … ;

    client_max_body_size 25m;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location ~ ^/api/meps/[^/]+/flux$ {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        proxy_read_timeout 1h;
    }
}
```

Avec IIS (Application Request Routing + URL Rewrite) : désactiver la mise en tampon des réponses (`responseBufferLimit="0"`) pour le chemin du flux et augmenter le délai d'expiration du proxy.

## Sécurité intégrée

- Mots de passe hachés (PBKDF2), cookie de session `HttpOnly`, `SameSite=Lax` et `Secure`.
- Tentatives de connexion limitées par adresse IP ; après 5 échecs consécutifs, un compte est bloqué quelques minutes depuis l'adresse qui a échoué seulement (un tiers ne peut pas bloquer la personne depuis son propre poste). Des échecs répétés sur un compte depuis plusieurs adresses sont signalés dans le journal d'administration.
- Port de l'application publié sur `127.0.0.1` par défaut : on n'y accède qu'à travers le proxy HTTPS.
- Pièces jointes plafonnées par MEP, par compte et par jour, avec un espace disque minimal préservé ; flux temps réel limités par compte et pour le serveur.
- Mot de passe administrateur initial jamais celui de l'exemple, et toujours à changer à la première connexion.
- Protection contre la falsification de requêtes (en-tête exigé sur toute écriture), en-têtes de sécurité HTTP, taille des requêtes limitée.
- Un mot de passe provisoire (compte créé ou réinitialisé) doit être changé avant toute autre action.
- Le rôle administrateur et l'état d'un compte sont relus à chaque requête : une désactivation s'applique immédiatement.

## Limites connues

- **Une seule instance** de l'application à la fois sur une même base : les verrous d'édition des étapes, le flux temps réel, les limites de tentatives de connexion et les plafonds de dépôt de pièces jointes sont tenus en mémoire, les pièces jointes sont sur un disque local, et les migrations tournent au démarrage. Pas de réplicas, ni de déploiement progressif (voir Kubernetes et ECS ci-dessus).
- Connexion unique par OpenID Connect seulement (Microsoft Entra ID et autres fournisseurs OIDC) ; SAML n'est pas pris en charge. Les comptes ne sont pas synchronisés avec l'annuaire (pas de SCIM) : une personne retirée de l'annuaire ne peut plus entrer par Microsoft, mais son compte reste à désactiver dans l'application.
