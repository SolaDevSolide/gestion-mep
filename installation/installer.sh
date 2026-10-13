#!/bin/sh
# Installation et mise à jour de Gestion MEP sans Docker, sur un serveur Linux avec systemd (x64).
#
#   sudo ./installer.sh                       installe la version de l'archive où se trouve ce script
#   sudo ./installer.sh --archive <fichier>   installe une archive déjà téléchargée (serveur sans accès à Internet)
#   sudo ./installer.sh --version 1.4.0       télécharge puis installe une version publiée (« derniere » : la plus récente) ;
#                                             une version encore présente dans /opt/gestion-mep/versions n'est pas retéléchargée
#
# Une fois installé, le script est aussi dans /opt/gestion-mep/installer.sh (mises à jour, retour à une version précédente).
# Mise à jour : sauvegarde de la base et des pièces jointes, arrêt, bascule, démarrage, contrôle de santé ; si la nouvelle
# version ne démarre pas, retour automatique à la précédente avec ses données. PostgreSQL n'est pas installé par ce script.
set -eu

DEPOT="${GESTION_MEP_DEPOT:-SolaDevSolide/gestion-mep}"
RACINE=/opt/gestion-mep
DONNEES=/var/lib/gestion-mep
CONF_DIR=/etc/gestion-mep
CONF="$CONF_DIR/gestion-mep.conf"
COMPTE=gestion-mep
ATTENTE_SANTE="${GESTION_MEP_ATTENTE_SANTE:-180}"
VERSIONS_GARDEES=3

info() { printf '[gestion-mep] %s\n' "$*"; }
erreur() { printf '[gestion-mep] Erreur : %s\n' "$*" >&2; exit 1; }

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
  info "Version $VERSION déjà présente dans $RACINE/versions."
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
  info "Téléchargement de la version $VERSION."
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

# 2. Compte de service, dossiers, configuration
if ! id "$COMPTE" > /dev/null 2>&1; then
  info "Création du compte de service $COMPTE."
  useradd --system --home-dir "$DONNEES" --no-create-home --shell /usr/sbin/nologin --user-group "$COMPTE"
fi
install -d -m 755 "$RACINE" "$RACINE/versions"
install -d -m 750 -o "$COMPTE" -g "$COMPTE" "$DONNEES"
for d in pieces-jointes cles sauvegardes mise-a-jour; do
  install -d -m 750 -o "$COMPTE" -g "$COMPTE" "$DONNEES/$d"
done
install -d -m 750 -g "$COMPTE" "$CONF_DIR"
NOUVELLE_CONF=false
if [ ! -f "$CONF" ]; then
  install -m 640 -g "$COMPTE" "$SOURCE/gestion-mep.conf.exemple" "$CONF"
  NOUVELLE_CONF=true
fi
# Lisible par root et le compte du service seulement (il contient le mot de passe de la base).
chown root:"$COMPTE" "$CONF"
chmod 640 "$CONF"

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
cat > /usr/local/bin/gestion-mep <<'COMMANDE'
#!/bin/sh
# Commandes d'exploitation de Gestion MEP (« sudo gestion-mep aide »), lancées avec le compte du service.
cd /opt/gestion-mep/actuelle || exit 1
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
  info "Version $NOUVELLE copiée. Avant le premier démarrage :"
  info "  1. créer la base PostgreSQL (voir docs/Installer.md, Installation sans Docker) ;"
  info "  2. renseigner BASE_DE_DONNEES (et les autres réglages) dans $CONF ;"
  info "  3. relancer : sudo $RACINE/installer.sh"
  exit 0
fi

sante() {
  i=0
  while [ "$i" -lt "$ATTENTE_SANTE" ]; do
    if systemctl is-active --quiet gestion-mep && /usr/local/bin/gestion-mep --sante > /dev/null 2>&1; then return 0; fi
    if systemctl is-failed --quiet gestion-mep; then return 1; fi
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
  info "Arrêt de la version $(cat "$PRECEDENTE/VERSION" 2>/dev/null || basename "$PRECEDENTE")."
  systemctl stop gestion-mep || true
  SAUVEGARDE="$DONNEES/sauvegardes/avant-$NOUVELLE-$(date -u +%Y%m%d-%H%M%S).tar.gz"
  info "Sauvegarde de la base et des pièces jointes : $SAUVEGARDE"
  if ! /usr/local/bin/gestion-mep sauvegarder "$SAUVEGARDE"; then
    systemctl start gestion-mep || true
    erreur "sauvegarde impossible : rien n'a été changé, la version précédente est relancée."
  fi
elif [ "$PRECEDENTE" = "$CIBLE" ]; then
  info "Version $NOUVELLE déjà en place : redémarrage."
fi

ln -sfn "$CIBLE" "$RACINE/actuelle.tmp" && mv -Tf "$RACINE/actuelle.tmp" "$RACINE/actuelle"
info "Démarrage de la version $NOUVELLE (migration de la base si nécessaire)."
systemctl restart gestion-mep || true
if sante; then
  info "Version $NOUVELLE installée et en marche."
  # Nettoyage : versions gardées (dont l'actuelle et la précédente) ; la sauvegarde avant mise à jour est conservée.
  ls -1dt "$RACINE"/versions/*/ 2>/dev/null | sed 's:/$::' | while read -r v; do
    [ "$v" = "$CIBLE" ] || [ "$v" = "$PRECEDENTE" ] && continue
    echo "$v"
  done | tail -n +"$((VERSIONS_GARDEES - 1))" | while read -r v; do rm -rf "$v"; done
  if grep -Eq '^ADMIN_MOT_DE_PASSE=.+' "$CONF"; then
    info "Pensez à retirer ADMIN_MOT_DE_PASSE de $CONF (compte administrateur créé)."
  fi
  info "Journal : journalctl -u gestion-mep -f ; commandes : sudo gestion-mep aide"
  exit 0
fi

journalctl -u gestion-mep -n 30 --no-pager 2>/dev/null || true
if [ -z "$SAUVEGARDE" ]; then
  erreur "la version $NOUVELLE ne démarre pas : voir le journal ci-dessus (journalctl -u gestion-mep) et $CONF."
fi
info "La version $NOUVELLE ne démarre pas : retour à la version précédente et à ses données."
systemctl stop gestion-mep || true
ln -sfn "$PRECEDENTE" "$RACINE/actuelle.tmp" && mv -Tf "$RACINE/actuelle.tmp" "$RACINE/actuelle"
/usr/local/bin/gestion-mep sauvegardes restaurer "$SAUVEGARDE" --oui || true
systemctl start gestion-mep || true
if sante; then
  erreur "la version $NOUVELLE n'a pas démarré : version précédente remise, avec les données d'avant la mise à jour. Voir le journal ci-dessus avant de réessayer."
fi
erreur "la version $NOUVELLE n'a pas démarré et la version précédente non plus : voir docs/Installer-sans-Docker.md ; sauvegarde d'avant la mise à jour : $SAUVEGARDE"
