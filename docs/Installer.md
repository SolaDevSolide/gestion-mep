# Installer

Cette page s'adresse à l'équipe informatique qui installe l'application sur un serveur. Pour l'essayer sur un poste, voir le [démarrage rapide](Démarrage-rapide.md). Les opérations courantes (mises à jour, sauvegardes, comptes) sont dans [Exploiter](Exploiter.md).

## Vue d'ensemble

L'application tient dans deux conteneurs, décrits par le fichier `docker-compose.yml` du dépôt :

| Conteneur | Contenu | Données |
|---|---|---|
| `app` | API et interface web, image `ghcr.io/soladevsolide/gestion-mep` | volumes `pieces-jointes` (fichiers joints), `cles` (clés de session) et `sauvegardes` (archives de la base et des pièces jointes) |
| `db` | PostgreSQL 16 | volume `db-data` |

L'application écoute en HTTP sur le port 8080. Le chiffrement HTTPS est confié à un proxy inverse placé devant elle. Il n'y a ni autre service, ni tâche planifiée à installer : le schéma de la base est créé et mis à jour automatiquement au démarrage.

## Prérequis

- Une machine virtuelle Linux (ou Windows Server) avec **Docker Engine** et le plugin **Docker Compose v2**.
- 2 vCPU et 4 Go de mémoire suffisent pour une équipe. Prévoir l'espace disque des pièces jointes.
- Un accès sortant à `ghcr.io` pour récupérer l'image, ou une copie de l'image dans un registre interne. Facultatif : un accès sortant à `api.github.com` pour signaler les nouvelles versions aux administrateurs.
- Un proxy inverse HTTPS (nginx, IIS, Traefik, un répartiteur de charge…) et un nom DNS.

### Sur AWS

- Une instance EC2 (par exemple `t3.medium`) avec Docker suffit, avec un volume EBS sauvegardé par instantanés.
- Le HTTPS peut être porté par un Application Load Balancer avec un certificat ACM : déclarer alors le sous-réseau du répartiteur dans `PROXY_RESEAUX` et régler son délai d'inactivité à 3600 secondes pour le flux temps réel (voir [Proxy inverse HTTPS](#proxy-inverse-https)).
- Pour utiliser Amazon RDS for PostgreSQL au lieu du conteneur `db`, voir [Base de données externe](#base-de-données-externe).

## Installation

1. Copier `docker-compose.yml` et `.env.example` dans un dossier du serveur, par exemple `/opt/gestion-mep`.
2. Renommer `.env.example` en `.env` et le compléter (voir [Configuration](#configuration)). Au minimum : `POSTGRES_PASSWORD`, `ADMIN_LOGIN`, `ADMIN_MOT_DE_PASSE` et `MEP_IMAGE`.
3. Démarrer :

   ```bash
   docker compose pull
   docker compose up -d
   ```

4. Vérifier :

   ```bash
   curl http://localhost:8080/api/sante            # doit répondre {"statut":"ok"}
   docker compose exec app gestion-mep config verifier
   ```

5. Configurer le [proxy inverse HTTPS](#proxy-inverse-https), puis se connecter avec le compte administrateur défini dans `.env`. Ce compte n'est créé qu'au premier démarrage, sur une base vide. Créer ensuite les comptes des utilisateurs depuis la console d'administration (voir [Administrer](Administrer.md)).

## Configuration

Les variables se règlent dans `.env`, lu par `docker-compose.yml`.

| Variable `.env` | Défaut | Rôle |
|---|---|---|
| `MEP_IMAGE` | `gestion-mep:local` | Image à lancer. En production, une version publiée : `ghcr.io/soladevsolide/gestion-mep:1.0.0` (voir [Choisir une version](Exploiter.md#choisir-une-version)). |
| `PORT` | `8080` | Port publié sur l'hôte. |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | `mep` / `mep` | Compte de la base. **Changer le mot de passe** avant le premier démarrage. |
| `ADMIN_LOGIN` / `ADMIN_MOT_DE_PASSE` | vides | Compte administrateur créé au premier démarrage si la base est vide. |
| `DEMO` | `false` | `true` crée les comptes de démonstration et une MEP d'exemple. **Jamais en production.** |
| `PIECES_TAILLE_MAX_MO` | `20` | Taille maximale d'une pièce jointe, en Mo. |
| `PROXY_RESEAUX` | `172.16.0.0/12` | Réseaux (CIDR, séparés par des virgules) du proxy HTTPS, seuls autorisés à transmettre l'adresse du client. Le défaut couvre un proxy sur le même hôte Docker ; sinon, indiquer l'adresse du proxy (`10.0.5.12/32`). |
| `VERIFIER_MISES_A_JOUR` | `true` | Consulte les versions publiées sur GitHub pour signaler une mise à jour aux administrateurs. `false` sur un serveur sans accès à Internet. |
| `LIEN_SIGNALEMENT` | formulaire GitHub du dépôt | Lien « Signaler un problème ou une idée » du menu utilisateur. Vide : lien masqué. Peut pointer vers un outil interne. |
| `COMPOSE_PROFILES` | vide | `mise-a-jour` active le bouton « Mettre à jour » de la console (voir [Mettre à jour depuis la console](Exploiter.md#mettre-à-jour-depuis-la-console)). |
| `MEP_IMAGE_MISE_A_JOUR` | `ghcr.io/soladevsolide/gestion-mep-mise-a-jour:latest` | Image du service de mise à jour. |
| `MEP_DEPOT_IMAGE` | `ghcr.io/soladevsolide/gestion-mep` | Seul dépôt d'images que le service de mise à jour accepte d'installer (à changer pour un registre interne). |
| `CONNEXIONS_PAR_MINUTE` | `20` | Tentatives de connexion autorisées par minute et par adresse IP. Au-delà : refus temporaire. Un compte est en plus bloqué quelques minutes après 5 échecs consécutifs. |

Pour un déploiement sans Docker Compose (Kubernetes, ECS…), l'image se règle par ses variables d'environnement :

| Variable | Rôle |
|---|---|
| `ConnectionStrings__Mep` | Chaîne de connexion PostgreSQL (`Host=…;Port=5432;Database=…;Username=…;Password=…`). |
| `Demo__Activer` | Voir `DEMO`. |
| `Admin__Login`, `Admin__MotDePasse` | Voir `ADMIN_LOGIN`, `ADMIN_MOT_DE_PASSE`. |
| `Stockage__Dossier` | Dossier des pièces jointes dans le conteneur (`/data/pieces-jointes`), à monter sur un volume persistant. |
| `Stockage__TailleMaxMo` | Voir `PIECES_TAILLE_MAX_MO`. |
| `Sauvegardes__Dossier` | Dossier des sauvegardes (`/data/sauvegardes`), à monter sur un volume persistant avec assez de place pour une copie de la base et des pièces jointes : sauvegardes automatiques avant une mise à jour du schéma ou une importation, et défaut de `gestion-mep sauvegarder`. |
| `Cles__Dossier` | Dossier des clés qui chiffrent le cookie de session (`/data/cles`), à monter sur un volume persistant. Sans lui, chaque recréation du conteneur déconnecte tout le monde. |
| `ProxyInverse__Reseaux` | Voir `PROXY_RESEAUX`. |
| `Connexion__TentativesParMinute` | Voir `CONNEXIONS_PAR_MINUTE`. |
| `MisesAJour__Verifier` | Voir `VERIFIER_MISES_A_JOUR`. `MisesAJour__Depot` (`SolaDevSolide/gestion-mep`) et `MisesAJour__IntervalleHeures` (`12`) changent le dépôt consulté et la fréquence. |
| `Signalement__Lien` | Voir `LIEN_SIGNALEMENT`. |
| `MisesAJour__Dossier` | Dossier partagé avec le service de mise à jour (`/data/mise-a-jour`, volume `mise-a-jour`). |
| `Surveillance__Activer` | `true` par défaut : bascule automatique en retour arrière quand l'échéance d'une MEP est dépassée. Ne pas désactiver en production. |

### Base de données externe

Pour utiliser un serveur PostgreSQL 16 existant (ou Amazon RDS), créer une base et un compte propriétaire de cette base, puis, dans `docker-compose.yml`, supprimer le service `db` (et la dépendance `depends_on` du service `app`) et remplacer `ConnectionStrings__Mep` par la chaîne de connexion de ce serveur. Les sauvegardes de la base se font alors avec les outils de ce serveur.

## Proxy inverse HTTPS

Hors développement, le cookie de session est marqué `Secure` : **l'application ne fonctionne qu'à travers HTTPS** (seul `http://localhost` fait exception dans les navigateurs). Points à respecter :

- Transmettre les en-têtes `Host`, `X-Forwarded-For` et `X-Forwarded-Proto`, et déclarer le proxy dans `PROXY_RESEAUX` s'il n'est pas sur le même hôte Docker.
- **Ne pas mettre en tampon les réponses** de `/api/meps/*/flux` : c'est un flux d'événements (Server-Sent Events) qui reste ouvert. Sans cela, les écrans ne se mettent plus à jour tout seuls.
- Autoriser des connexions longues sur ce flux (délai de lecture d'au moins une heure).
- Autoriser des corps de requête un peu plus grands que la taille maximale d'une pièce jointe (21 Mo par défaut). L'application limite elle-même les autres requêtes à 1 Mo.
- Ne pas ajouter d'en-têtes `Content-Security-Policy` ou `X-Frame-Options` différents : l'application pose déjà les siens (politique de contenu, interdiction d'affichage dans un cadre, `nosniff`, `Referrer-Policy`, et HSTS sur les réponses HTTPS).
- Si le proxy tourne sur la même machine, publier le port uniquement en local (`127.0.0.1:8080:8080` dans `docker-compose.yml`) pour que l'application ne soit joignable qu'à travers lui.

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
- Tentatives de connexion limitées par adresse IP, compte bloqué quelques minutes après 5 échecs consécutifs.
- Protection contre la falsification de requêtes (en-tête exigé sur toute écriture), en-têtes de sécurité HTTP, taille des requêtes limitée.
- Un mot de passe provisoire (compte créé ou réinitialisé) doit être changé avant toute autre action.
- Le rôle administrateur et l'état d'un compte sont relus à chaque requête : une désactivation s'applique immédiatement.

## Limites connues

- **Une seule instance** de l'application à la fois : le flux temps réel est diffusé en mémoire et les migrations tournent au démarrage.
- Les comptes sont locaux à l'application. La connexion par un annuaire d'entreprise (SSO) n'est pas encore disponible.
