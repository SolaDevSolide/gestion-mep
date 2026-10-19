#!/bin/sh
# Installation et mise à jour de Gestion MEP sans Docker, sur un serveur Linux avec systemd (x64).
#
#   sudo ./installer.sh                       installe la version de l'archive où se trouve ce script
#   sudo ./installer.sh --archive <fichier>   installe une archive déjà téléchargée (serveur sans accès à Internet)
#   sudo ./installer.sh --version 1.4.0       télécharge puis installe une version publiée (« derniere » : la plus récente) ;
#                                             une version encore présente dans /opt/gestion-mep/versions n'est pas retéléchargée
#
# Une fois installé, le script est aussi dans /opt/gestion-mep/installer.sh (mises à jour, retour à une version précédente).
# Mise à jour : arrêt, sauvegarde de la base et des pièces jointes, bascule, migration de la base (attendue jusqu'au bout),
# démarrage, contrôle « prête » ; si la nouvelle version ne migre pas ou ne démarre pas, retour automatique à la précédente
# avec ses données. PostgreSQL n'est pas installé par ce script.
# Variables : GESTION_MEP_ATTENTE_SANTE (secondes d'attente du démarrage, 600 par défaut), GESTION_MEP_DEPOT (dépôt GitHub
# des versions, pour un miroir).
set -eu

printf '\nGestion MEP : installation sans Docker (Linux)\n'
DEPOT="${GESTION_MEP_DEPOT:-SolaDevSolide/gestion-mep}"
RACINE=/opt/gestion-mep
# Dossier des données par défaut ; DOSSIER_DONNEES dans la configuration le remplace.
DONNEES=/var/lib/gestion-mep
CONF_DIR=/etc/gestion-mep
CONF="$CONF_DIR/gestion-mep.conf"
COMPTE=gestion-mep
ATTENTE_SANTE="${GESTION_MEP_ATTENTE_SANTE:-600}"
VERSIONS_GARDEES=3

# Messages : une étape par bloc, détails en retrait, résultat en couleur (dans un terminal seulement).
if [ -t 1 ]; then CYAN=$(printf '\033[36m'); VERT=$(printf '\033[32m'); JAUNE=$(printf '\033[33m'); ROUGE=$(printf '\033[31m'); NORMAL=$(printf '\033[0m')
else CYAN=""; VERT=""; JAUNE=""; ROUGE=""; NORMAL=""; fi
etape() { printf '\n%s== %s%s\n' "$CYAN" "$*" "$NORMAL"; }
info() { printf '   %s\n' "$*"; }
attention() { printf '   %s%s%s\n' "$JAUNE" "$*" "$NORMAL"; }
succes() { printf '\n   %s%s%s\n' "$VERT" "$*" "$NORMAL"; }
erreur() { printf '\n   %sERREUR : %s%s\n\n' "$ROUGE" "$*" "$NORMAL" >&2; exit 1; }

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

ARCHIVE=""
VERSION=""
while [ $# -gt 0 ]; do
  case "$1" in
    --archive) [ $# -ge 2 ] || usage 2; ARCHIVE="$2"; shift 2 ;;
    --version) [ $# -ge 2 ] || usage 2; VERSION="${2#v}"; shift 2 ;;
    -h|--help|aide) usage 0 ;;
    *) printf 'Option inconnue : %s\n\n' "$1" >&2; usage 2 ;;
  esac
done

[ "$(id -u)" -eq 0 ] || erreur "lancer avec sudo (création du compte de service, du service systemd et des dossiers)."
command -v systemctl > /dev/null || erreur "systemd introuvable : cette installation suppose un serveur Linux avec systemd."
[ "$(uname -m)" = x86_64 ] || erreur "seuls les serveurs x64 sont pris en charge (machine : $(uname -m))."

TRAVAIL=$(mktemp -d)
trap 'rm -rf "$TRAVAIL"' EXIT

telecharger() { # telecharger <url> <fichier>
  if command -v curl > /dev/null; then curl -fsSL -o "$2" "$1"
  elif command -v wget > /dev/null; then wget -q -O "$2" "$1"
  else erreur "ni curl ni wget : télécharger l'archive à la main puis utiliser --archive."
  fi
}

# 1. Archive de la version à installer
ICI=$(cd "$(dirname "$0")" && pwd)
if [ -n "${GESTION_MEP_SOURCE:-}" ]; then
  SOURCE="$GESTION_MEP_SOURCE"
elif [ -n "$VERSION" ] && [ -x "$RACINE/versions/$VERSION/GestionMep.Api" ]; then
  # Version déjà présente sur le serveur (retour à une version précédente) : rien à télécharger.
  etape "Version $VERSION"
  info "Déjà présente dans $RACINE/versions : rien à télécharger."
  SOURCE="$RACINE/versions/$VERSION"
elif [ -n "$VERSION" ]; then
  if [ "$VERSION" = derniere ]; then
    telecharger "https://api.github.com/repos/$DEPOT/releases/latest" "$TRAVAIL/derniere.json"
    VERSION=$(sed -n 's/.*"tag_name" *: *"v\{0,1\}\([^"]*\)".*/\1/p' "$TRAVAIL/derniere.json" | head -n 1)
    [ -n "$VERSION" ] || erreur "version la plus récente introuvable sur https://github.com/$DEPOT/releases."
  fi
  echo "$VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$' || erreur "numéro de version attendu, par exemple 1.4.0 (reçu : $VERSION)."
  nom="gestion-mep-$VERSION-linux-x64.tar.gz"
  base="https://github.com/$DEPOT/releases/download/v$VERSION"
  etape "Téléchargement de la version $VERSION"
  telecharger "$base/$nom" "$TRAVAIL/$nom" || erreur "téléchargement impossible de $base/$nom."
  telecharger "$base/SHA256SUMS" "$TRAVAIL/SHA256SUMS" || erreur "téléchargement impossible de $base/SHA256SUMS."
  (cd "$TRAVAIL" && grep " $nom\$" SHA256SUMS | sha256sum -c --quiet -) || erreur "empreinte SHA-256 incorrecte : archive corrompue ou modifiée."
  ARCHIVE="$TRAVAIL/$nom"
fi
if [ -n "${SOURCE:-}" ]; then
  :
elif [ -n "$ARCHIVE" ]; then
  [ -f "$ARCHIVE" ] || erreur "archive introuvable : $ARCHIVE"
  mkdir "$TRAVAIL/archive"
  tar -xzf "$ARCHIVE" -C "$TRAVAIL/archive"
  SOURCE=$(find "$TRAVAIL/archive" -mindepth 1 -maxdepth 2 -name VERSION -type f | head -n 1)
  [ -n "$SOURCE" ] || erreur "$ARCHIVE n'est pas une archive de Gestion MEP pour Linux."
  SOURCE=$(dirname "$SOURCE")
elif [ -f "$ICI/VERSION" ] && [ -f "$ICI/GestionMep.Api" ]; then
  SOURCE="$ICI"
elif [ -f "$RACINE/actuelle/VERSION" ]; then
  # Sans option, depuis /opt/gestion-mep : réinstalle et redémarre la version en place (après avoir rempli la configuration).
  SOURCE=$(readlink -f "$RACINE/actuelle")
else
  erreur "indiquer --version <numéro> ou --archive <fichier>, ou lancer le script depuis l'archive décompressée."
fi
[ -f "$SOURCE/GestionMep.Api" ] || erreur "$SOURCE ne contient pas l'application."
# Le script de la version à installer fait la suite : chaque version installe avec son propre script.
if [ -z "${GESTION_MEP_SOURCE:-}" ] && [ -f "$SOURCE/installer.sh" ] && ! cmp -s "$SOURCE/installer.sh" "$0"; then
  GESTION_MEP_SOURCE="$SOURCE" sh "$SOURCE/installer.sh"
  exit $?
fi
NOUVELLE=$(cat "$SOURCE/VERSION")
echo "$NOUVELLE" | grep -Eq '^[0-9A-Za-z.+-]+$' || erreur "numéro de version illisible dans l'archive."
etape "Installation de la version $NOUVELLE"
info "Fichiers : $RACINE/versions/$NOUVELLE"
info "Configuration : $CONF"

# 2. Compte de service, configuration, dossiers
if ! id "$COMPTE" > /dev/null 2>&1; then
  info "Création du compte de service $COMPTE."
  useradd --system --home-dir "$DONNEES" --no-create-home --shell /usr/sbin/nologin --user-group "$COMPTE"
fi
install -d -m 755 "$RACINE" "$RACINE/versions"
install -d -m 750 -g "$COMPTE" "$CONF_DIR"
NOUVELLE_CONF=false
if [ ! -f "$CONF" ]; then
  install -m 640 -g "$COMPTE" "$SOURCE/gestion-mep.conf.exemple" "$CONF"
  NOUVELLE_CONF=true
fi
# Lisible par root et le compte du service seulement (il contient le mot de passe de la base).
chown root:"$COMPTE" "$CONF"
chmod 640 "$CONF"
# Dossier des données choisi dans la configuration (disque dédié, par exemple) : créé ici, et seul dossier modifiable par
# le service durci (ReadWritePaths, fichier complémentaire de systemd).
choisi=$(sed -n 's/^[[:space:]]*DOSSIER_DONNEES[[:space:]]*=[[:space:]]*//p' "$CONF" | tail -n 1 | tr -d '\r' | sed 's/[[:space:]]*$//; s/^"\(.*\)"$/\1/')
if [ -n "$choisi" ]; then
  case "$choisi" in
    *[[:space:]]*) erreur "DOSSIER_DONNEES ne doit pas contenir d'espace ($choisi) : choisir un autre dossier dans $CONF." ;;
    /*) DONNEES="${choisi%/}" ;;
    *) erreur "DOSSIER_DONNEES doit être un chemin absolu ($choisi) : corriger $CONF." ;;
  esac
fi
info "Données : $DONNEES"
install -d -m 750 -o "$COMPTE" -g "$COMPTE" "$DONNEES"
for d in pieces-jointes cles sauvegardes mise-a-jour; do
  install -d -m 750 -o "$COMPTE" -g "$COMPTE" "$DONNEES/$d"
done

# 3. Fichiers de la version, service et commande gestion-mep
CIBLE="$RACINE/versions/$NOUVELLE"
if [ "$SOURCE" != "$CIBLE" ]; then
  rm -rf "$CIBLE.tmp"
  cp -a "$SOURCE" "$CIBLE.tmp"
  rm -rf "$CIBLE"
  mv "$CIBLE.tmp" "$CIBLE"
fi
chown -R root:root "$CIBLE"
chmod -R u=rwX,go=rX "$CIBLE"
chmod 755 "$CIBLE/GestionMep.Api" "$CIBLE/installer.sh"
install -m 755 "$CIBLE/installer.sh" "$RACINE/installer.sh.tmp" && mv -f "$RACINE/installer.sh.tmp" "$RACINE/installer.sh"
install -m 644 "$CIBLE/gestion-mep.service" /etc/systemd/system/gestion-mep.service
install -d -m 755 /etc/systemd/system/gestion-mep.service.d
if [ "$DONNEES" = /var/lib/gestion-mep ]; then
  rm -f /etc/systemd/system/gestion-mep.service.d/donnees.conf
else
  printf '# Posé par installer.sh : dossier des données choisi par DOSSIER_DONNEES (%s).\n[Service]\nReadWritePaths=%s\n' \
    "$CONF" "$DONNEES" > /etc/systemd/system/gestion-mep.service.d/donnees.conf
fi
# Commande sans changement de dossier courant : les chemins relatifs (fichiers lus, exports Excel) partent du dossier où
# elle est tapée. Ils doivent être accessibles au compte du service ; sinon, passer par l'entrée ou la sortie standard (-).
cat > /usr/local/bin/gestion-mep <<'COMMANDE'
#!/bin/sh
# Commandes d'exploitation de Gestion MEP (« sudo gestion-mep aide »), lancées avec le compte du service.
if [ "$(id -u)" -eq 0 ]; then
  exec runuser -u gestion-mep -- /opt/gestion-mep/actuelle/GestionMep.Api "$@"
fi
exec /opt/gestion-mep/actuelle/GestionMep.Api "$@"
COMMANDE
chmod 755 /usr/local/bin/gestion-mep
systemctl daemon-reload
systemctl enable gestion-mep > /dev/null 2>&1 || true

if [ "$NOUVELLE_CONF" = true ] || grep -Eq '^BASE_DE_DONNEES=.*Password=;?$' "$CONF"; then
  if [ ! -L "$RACINE/actuelle" ]; then ln -sfn "$CIBLE" "$RACINE/actuelle"; fi
  etape "À faire avant le premier démarrage"
  info "1. Créer la base PostgreSQL (voir docs/Installer-sans-Docker.md)."
  info "2. Renseigner BASE_DE_DONNEES (et les autres réglages) dans :"
  attention "     $CONF"
  info "3. Relancer :"
  attention "     sudo $RACINE/installer.sh"
  echo
  exit 0
fi

# Attend que l'application soit prête (sonde « prête » : base joignable et au bon schéma). Un échec du service, ou un
# redémarrage automatique (arrêt brutal pendant le démarrage), arrête l'attente tout de suite.
redemarrages() { systemctl show -p NRestarts --value gestion-mep 2> /dev/null || echo 0; }
sante() {
  depart=$(redemarrages)
  i=0
  while [ "$i" -lt "$ATTENTE_SANTE" ]; do
    if systemctl is-active --quiet gestion-mep && /usr/local/bin/gestion-mep --sante --prete > /dev/null 2>&1; then return 0; fi
    if systemctl is-failed --quiet gestion-mep; then return 1; fi
    [ "$(redemarrages)" = "$depart" ] || return 1
    sleep 3
    i=$((i + 3))
  done
  return 1
}

# 4. Bascule vers la nouvelle version
PRECEDENTE=""
if [ -L "$RACINE/actuelle" ]; then PRECEDENTE=$(readlink -f "$RACINE/actuelle"); fi
SAUVEGARDE=""
if [ -n "$PRECEDENTE" ] && [ "$PRECEDENTE" != "$CIBLE" ] && [ -x "$PRECEDENTE/GestionMep.Api" ]; then
  etape "Mise à jour depuis la version $(cat "$PRECEDENTE/VERSION" 2>/dev/null || basename "$PRECEDENTE")"
  info "Arrêt du service."
  systemctl stop gestion-mep || true
  SAUVEGARDE="$DONNEES/sauvegardes/avant-$NOUVELLE-$(date -u +%Y%m%d-%H%M%S).tar.gz"
  info "Sauvegarde de la base et des pièces jointes :"
  info "  $SAUVEGARDE"
  if ! /usr/local/bin/gestion-mep sauvegarder "$SAUVEGARDE" > "$TRAVAIL/sauvegarde.txt" 2>&1; then
    sed 's/^/     /' "$TRAVAIL/sauvegarde.txt"
    systemctl start gestion-mep || true
    erreur "sauvegarde impossible : rien n'a été changé, la version précédente est relancée."
  fi
  info "Sauvegarde écrite."
elif [ "$PRECEDENTE" = "$CIBLE" ]; then
  etape "Version $NOUVELLE déjà en place : redémarrage"
fi

ln -sfn "$CIBLE" "$RACINE/actuelle.tmp" && mv -Tf "$RACINE/actuelle.tmp" "$RACINE/actuelle"
# Migration hors du service (service arrêté) : attendue jusqu'au bout, quelle que soit sa durée, sans délai de démarrage
# de systemd qui l'interromprait. Le service démarre ensuite sur une base à jour.
MIGREE=true
if [ -n "$SAUVEGARDE" ]; then
  etape "Migration de la base par la version $NOUVELLE"
  info "Avec sa propre sauvegarde automatique ; attendue jusqu'au bout, quelle que soit sa durée."
  { if /usr/local/bin/gestion-mep migrer 2>&1; then echo 0; else echo 1; fi > "$TRAVAIL/migration.code"; } | sed 's/^/     /'
  [ "$(cat "$TRAVAIL/migration.code" 2> /dev/null)" = 0 ] || MIGREE=false
fi
if [ "$MIGREE" = true ]; then
  etape "Démarrage de la version $NOUVELLE"
  info "Vérification que l'application est prête ($((ATTENTE_SANTE / 60)) minutes au plus, GESTION_MEP_ATTENTE_SANTE pour changer)."
  systemctl restart gestion-mep || true
fi
if [ "$MIGREE" = true ] && sante; then
  succes "Version $NOUVELLE installée et en marche."
  # Nettoyage : versions gardées (dont l'actuelle et la précédente) ; la sauvegarde avant mise à jour est conservée.
  ls -1dt "$RACINE"/versions/*/ 2>/dev/null | sed 's:/$::' | while read -r v; do
    [ "$v" = "$CIBLE" ] || [ "$v" = "$PRECEDENTE" ] && continue
    echo "$v"
  done | tail -n +"$((VERSIONS_GARDEES - 1))" | while read -r v; do rm -rf "$v"; done
  if grep -Eq '^ADMIN_MOT_DE_PASSE=.+' "$CONF"; then
    attention "Pensez à retirer ADMIN_MOT_DE_PASSE de $CONF (compte administrateur créé)."
  fi
  echo
  info "Adresse locale : http://127.0.0.1:$(sed -n 's/^PORT=\([0-9]*\).*/\1/p' "$CONF" | head -n 1 | grep . || echo 8080)"
  info "Commandes      : sudo gestion-mep aide"
  info "Journal        : sudo journalctl -u gestion-mep -f"
  echo
  exit 0
fi

if [ "$MIGREE" = true ]; then
  journalctl -u gestion-mep -n 30 --no-pager 2>/dev/null || true
  RAISON="la version $NOUVELLE ne démarre pas"
else
  RAISON="la migration de la base par la version $NOUVELLE a échoué"
fi
if [ -z "$SAUVEGARDE" ]; then
  erreur "$RAISON : voir le journal ci-dessus (journalctl -u gestion-mep) et $CONF."
fi
etape "Retour à la version précédente"
attention "$RAISON : retour à la version précédente et à ses données."
systemctl stop gestion-mep || true
ln -sfn "$PRECEDENTE" "$RACINE/actuelle.tmp" && mv -Tf "$RACINE/actuelle.tmp" "$RACINE/actuelle"
if ! /usr/local/bin/gestion-mep sauvegardes restaurer "$SAUVEGARDE" --oui; then
  # Base peut-être déjà migrée : l'ancienne version ne doit pas tourner dessus.
  erreur "$RAISON, et la restauration de la sauvegarde a échoué (message ci-dessus) : service laissé arrêté. Version précédente remise dans $RACINE/actuelle ; recharger à la main « sudo gestion-mep sauvegardes restaurer $SAUVEGARDE --oui », puis « sudo systemctl start gestion-mep »."
fi
systemctl start gestion-mep || true
if sante; then
  erreur "$RAISON : version précédente remise, avec les données d'avant la mise à jour (sauvegarde rechargée). Voir le journal ci-dessus avant de réessayer."
fi
erreur "$RAISON, et la version précédente ne démarre pas non plus (données d'avant la mise à jour pourtant rechargées) : voir docs/Installer-sans-Docker.md ; sauvegarde d'avant la mise à jour : $SAUVEGARDE"
