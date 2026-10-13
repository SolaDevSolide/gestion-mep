# Installer sans Docker

Cette page s'adresse à l'équipe informatique qui ne peut pas, ou ne veut pas, utiliser Docker. L'application s'installe alors directement sur un serveur **Linux** (avec systemd) ou **Windows Server**, comme un service système. L'installation Docker reste la référence : voir [Installer](Installer.md). Tout ce que fait l'application est identique dans les deux cas ; seules changent la façon de l'installer, de la mettre à jour et de lancer les commandes.

## Vue d'ensemble

Chaque version publiée sur la page *Releases* du dépôt est accompagnée de deux archives autonomes, qui ne demandent pas d'installer .NET :

| Archive | Pour | Contenu |
|---|---|---|
| `gestion-mep-<version>-linux-x64.tar.gz` | Linux x64 avec systemd (Ubuntu, Debian, RHEL, Rocky…) | application, `installer.sh`, `gestion-mep.service`, `gestion-mep.conf.exemple` |
| `gestion-mep-<version>-win-x64.zip` | Windows Server x64 (2019 ou plus récent) | application, `installer.ps1`, `gestion-mep.conf.exemple` |

Le fichier `SHA256SUMS` de la même page donne leur empreinte : le script la vérifie lui-même quand il télécharge une version.

Ce que pose le script d'installation :

| | Linux | Windows |
|---|---|---|
| Application (une version par dossier) | `/opt/gestion-mep/versions/<version>`, version en service : `/opt/gestion-mep/actuelle` | `C:\Program Files\GestionMep\versions\<version>`, version en service : `C:\Program Files\GestionMep\actuelle` |
| Configuration | `/etc/gestion-mep/gestion-mep.conf` (root et le compte du service seulement) | `C:\ProgramData\GestionMep\gestion-mep.conf` (administrateurs et le service seulement) |
| Données (pièces jointes, clés de session, sauvegardes) | `/var/lib/gestion-mep` | `C:\ProgramData\GestionMep\donnees` |
| Service | `gestion-mep` (systemd), compte système `gestion-mep` sans connexion | `GestionMep`, compte `NT AUTHORITY\LocalService` |
| Commande d'exploitation | `sudo gestion-mep …` | `gestion-mep …` (console en administrateur) |
| Journaux | `journalctl -u gestion-mep` | Observateur d'événements, journal *Application* |

PostgreSQL n'est pas installé par le script : il faut un serveur PostgreSQL 16 ou plus récent, sur la même machine ou ailleurs (Amazon RDS compris). Comme avec Docker, l'application écoute en HTTP (par défaut sur `127.0.0.1:8080`) derrière un [proxy inverse HTTPS](Installer.md#proxy-inverse-https).

## Prérequis

- Linux x64 avec systemd, ou Windows Server x64. 2 vCPU et 4 Go de mémoire suffisent pour une équipe ; prévoir l'espace disque des pièces jointes et des sauvegardes.
- Un serveur PostgreSQL 16 ou plus récent, avec une base vide et un compte qui en est propriétaire.
- Un proxy inverse HTTPS (nginx, IIS, Apache, un répartiteur de charge…) et un nom DNS.
- Facultatif : un accès sortant à `github.com` pour télécharger les versions (sinon, copier l'archive sur le serveur) et à `api.github.com` pour signaler les nouvelles versions aux administrateurs.

## Préparer la base PostgreSQL

Par exemple, sur Ubuntu ou Debian, avec PostgreSQL sur la même machine :

```bash
sudo apt install postgresql
sudo -u postgres psql -c "CREATE USER mep PASSWORD '<mot de passe long et aléatoire>'"
sudo -u postgres psql -c "CREATE DATABASE gestion_mep OWNER mep"
```

Sous Windows, installer PostgreSQL avec l'installeur officiel, puis créer le compte et la base de la même façon avec `psql` ou pgAdmin. Le schéma est créé par l'application à son premier démarrage.

## Installer sur Linux

1. Télécharger l'archive de la version voulue depuis la page *Releases*, la copier sur le serveur, puis la décompresser :

   ```bash
   tar -xzf gestion-mep-1.4.0-linux-x64.tar.gz
   sudo gestion-mep-1.4.0-linux-x64/installer.sh
   ```

   Le script crée le compte de service `gestion-mep`, les dossiers, le service systemd et la commande `gestion-mep`, et copie la configuration d'exemple dans `/etc/gestion-mep/gestion-mep.conf`. Il s'arrête là tant que la base n'est pas renseignée.
2. Compléter `/etc/gestion-mep/gestion-mep.conf` (voir [Configuration](#configuration)). Au minimum : `BASE_DE_DONNEES` et `ADMIN_LOGIN`.

   ```bash
   sudo nano /etc/gestion-mep/gestion-mep.conf
   ```

3. Démarrer :

   ```bash
   sudo /opt/gestion-mep/installer.sh
   ```

   Le script démarre le service, attend que l'application réponde, puis l'indique. Le service redémarre tout seul avec le serveur.
4. Vérifier :

   ```bash
   curl http://127.0.0.1:8080/api/sante          # doit répondre {"statut":"ok"}
   sudo gestion-mep config verifier
   ```

5. Configurer le [proxy inverse HTTPS](Installer.md#proxy-inverse-https) (l'exemple nginx s'applique tel quel), puis se connecter avec le compte administrateur. Avec `ADMIN_MOT_DE_PASSE` vide (conseillé), le mot de passe provisoire est dans le journal : `sudo journalctl -u gestion-mep | grep provisoire`.

## Installer sur Windows

Dans une console PowerShell **lancée en administrateur** :

1. Télécharger l'archive `.zip` de la version voulue depuis la page *Releases*, puis :

   ```powershell
   Expand-Archive gestion-mep-1.4.0-win-x64.zip -DestinationPath .
   powershell -ExecutionPolicy Bypass -File .\gestion-mep-1.4.0-win-x64\installer.ps1
   ```

   Le script copie l'application, crée le service `GestionMep` (démarrage automatique, compte `LocalService`), les dossiers avec leurs droits, et la configuration d'exemple dans `C:\ProgramData\GestionMep\gestion-mep.conf`. Il s'arrête là tant que la base n'est pas renseignée.
2. Compléter `C:\ProgramData\GestionMep\gestion-mep.conf` (voir [Configuration](#configuration)) : `notepad C:\ProgramData\GestionMep\gestion-mep.conf`.
3. Démarrer :

   ```powershell
   powershell -ExecutionPolicy Bypass -File "$env:ProgramFiles\GestionMep\installer.ps1"
   ```

4. Vérifier, dans une nouvelle console en administrateur (la commande `gestion-mep` vient d'être ajoutée au `PATH`) :

   ```powershell
   Invoke-RestMethod http://127.0.0.1:8080/api/sante
   gestion-mep config verifier
   ```

5. Configurer le proxy inverse HTTPS (IIS avec Application Request Routing, voir [Proxy inverse HTTPS](Installer.md#proxy-inverse-https)), puis se connecter avec le compte administrateur. Avec `ADMIN_MOT_DE_PASSE` vide, le mot de passe provisoire est dans l'Observateur d'événements, journal *Application*, source *GestionMep.Api* (ou `gestion-mep comptes reinitialiser-mdp admin` pour en obtenir un nouveau).

## Configuration

Le fichier de configuration reprend **les mêmes noms que le fichier `.env`** de l'installation Docker (voir le tableau de [Configuration](Installer.md#configuration)), avec ces différences :

| Nom | Défaut | Rôle |
|---|---|---|
| `BASE_DE_DONNEES` | | Chaîne de connexion PostgreSQL complète : `Host=localhost;Port=5432;Database=gestion_mep;Username=mep;Password=…`. Remplace `POSTGRES_USER` et `POSTGRES_PASSWORD`. |
| `DOSSIER_DONNEES` | voir ci-dessus | Dossier des données, découpé en `pieces-jointes`, `cles`, `sauvegardes` et `mise-a-jour` (les volumes Docker). |
| `ADRESSE_ECOUTE` | `127.0.0.1` | Adresse d'écoute de l'application. `0.0.0.0` (toutes les cartes réseau) si le proxy est sur une autre machine, ou pour un essai depuis un autre poste avec `HTTPS_OBLIGATOIRE=false`. |
| `PORT` | `8080` | Port d'écoute. |
| `PROXY_RESEAUX` | vide | Vide : le proxy est sur la même machine (`127.0.0.1` ou `::1`). Sinon, l'adresse du proxy (`10.0.5.12/32`). |

`MEP_IMAGE` et `COMPOSE_PROFILES` n'ont pas de sens hors Docker. Un réglage avancé de l'application peut aussi s'écrire sous sa forme complète, avec deux tirets bas (`Logging__LogLevel__Default=Warning`).

Les réglages marqués **(console)** se modifient ensuite, sans redémarrage, dans la console d'administration ou par `gestion-mep reglages`, comme avec Docker. Les autres ne sont lus qu'au démarrage : modifier le fichier, puis `sudo systemctl restart gestion-mep` (Linux) ou `Restart-Service GestionMep` (Windows). La console et `gestion-mep reglages lister` indiquent eux-mêmes la bonne commande selon l'installation.

## Mettre à jour

Le bouton « Mettre à jour » de la console est réservé à l'installation Docker : hors Docker, l'onglet *Version* et `gestion-mep mettre-a-jour` donnent la commande à lancer sur le serveur.

```bash
sudo /opt/gestion-mep/installer.sh --version 1.4.1          # Linux, télécharge la version depuis GitHub
sudo /opt/gestion-mep/installer.sh --version derniere       # la plus récente
sudo /opt/gestion-mep/installer.sh --archive gestion-mep-1.4.1-linux-x64.tar.gz   # serveur sans accès à Internet
```

```powershell
& "$env:ProgramFiles\GestionMep\installer.ps1" -Version 1.4.1                     # Windows
& "$env:ProgramFiles\GestionMep\installer.ps1" -Archive .\gestion-mep-1.4.1-win-x64.zip
```

Déroulé :

1. Le script vérifie l'archive (empreinte SHA-256 quand il la télécharge) et la copie dans un dossier propre à la version, sans toucher à celle en service.
2. Il arrête le service et sauvegarde la base et les pièces jointes avec la version en place (`avant-<version>-AAAAMMJJ-HHMMSS.tar.gz`, heure UTC, dans le dossier des sauvegardes). Si la sauvegarde échoue, il relance la version en place et s'arrête : rien n'a changé.
3. Il bascule sur la nouvelle version et démarre le service. L'application migre la base avec sa [sauvegarde automatique](Exploiter.md#mettre-à-jour).
4. Il attend que l'application réponde (3 minutes au plus). Sinon, il remet la version précédente, recharge la sauvegarde de l'étape 2 et relance le service : l'application revient à l'état d'avant la mise à jour.

Les trois dernières versions restent sur le serveur. La sauvegarde de l'étape 2 est conservée : la supprimer une fois la nouvelle version validée.

## Revenir à la version précédente

```bash
sudo /opt/gestion-mep/installer.sh --version 1.4.0     # version encore présente : pas de téléchargement
```

Si la version plus récente a modifié le schéma de la base, recharger ensuite la sauvegarde faite avant la mise à jour (voir ci-dessous) : les migrations ne sont pas annulées automatiquement, et les données saisies depuis seront perdues.

## Sauvegarder et restaurer

Les commandes sont celles de [Exploiter](Exploiter.md#sauvegarder), sans le préfixe Docker :

```bash
sudo gestion-mep sauvegarder                      # écrit /var/lib/gestion-mep/sauvegardes/sauvegarde-AAAAMMJJ-HHMMSS.tar.gz
sudo gestion-mep sauvegardes lister
sudo systemctl stop gestion-mep
sudo gestion-mep sauvegardes restaurer sauvegarde-AAAAMMJJ-HHMMSS.tar.gz --oui
sudo systemctl start gestion-mep
```

Sous Windows : `gestion-mep sauvegarder`, puis `Stop-Service GestionMep`, `gestion-mep sauvegardes restaurer … --oui` et `Start-Service GestionMep`. Pour une sauvegarde chaque nuit, planifier `gestion-mep sauvegarder` (cron ou tâche planifiée, en root ou en administrateur) et copier le dossier des sauvegardes hors du serveur.

## Commandes et surveillance

- Toutes les [commandes d'administration](Exploiter.md#commandes-dadministration) s'utilisent avec `sudo gestion-mep …` (Linux) ou `gestion-mep …` (Windows, console en administrateur) : `sudo gestion-mep aide` donne la liste.
- Santé : `GET /api/sante`, ou `gestion-mep --sante` (code de retour 0 si l'application répond).
- Journaux : `sudo journalctl -u gestion-mep -f` (Linux), Observateur d'événements (Windows).
- État du service : `systemctl status gestion-mep` (Linux), `Get-Service GestionMep` (Windows).

## Désinstaller

Linux :

```bash
sudo systemctl disable --now gestion-mep
sudo rm -rf /opt/gestion-mep /etc/systemd/system/gestion-mep.service /usr/local/bin/gestion-mep
sudo rm -rf /etc/gestion-mep /var/lib/gestion-mep      # configuration et données : seulement après une dernière sauvegarde copiée ailleurs
sudo userdel gestion-mep
```

Windows : `Stop-Service GestionMep; sc.exe delete GestionMep`, puis supprimer `C:\Program Files\GestionMep` et, après une dernière sauvegarde copiée ailleurs, `C:\ProgramData\GestionMep`. La base PostgreSQL n'est jamais supprimée par ces commandes.
