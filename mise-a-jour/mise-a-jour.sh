#!/bin/sh
# Service de mise à jour de Gestion MEP.
#
# Échanges avec l'application par le volume partagé /echange (aucun port ouvert) :
#   - demande.json : écrit par l'application quand un administrateur clique « Mettre à jour » ; seul le numéro de
#     version y est lu, et il doit être de la forme 1.2.3 ;
#   - etat.json : écrit ici, lu par l'application (statut, message, version, battement de cœur toutes les 5 secondes au
#     plus, y compris pendant les étapes longues).
#
# Une mise à jour, dans l'ordre :
#   1. téléchargement de <MEP_DEPOT_IMAGE>:<version> ; en cas d'échec, rien n'est changé ;
#   2. arrêt de l'application, puis sauvegarde de la base et des pièces jointes par la version en place
#      (avant-<version>-AAAAMMJJ-HHMMSS.tar.gz dans le volume sauvegardes) ; en cas d'échec, la version en place est
#      relancée et rien n'est changé ;
#   3. migration de la base par la nouvelle version (« migrer », avec sa propre sauvegarde automatique), attendue
#      jusqu'au bout, sans limite de durée : une migration n'est jamais interrompue ;
#   4. démarrage de la nouvelle version, attendue jusqu'à ce que sa sonde « prête » réponde (MEP_ATTENTE_SANTE secondes
#      au plus) ; un arrêt ou un redémarrage du conteneur pendant ce temps est un échec immédiat ;
#   5. si l'étape 3 ou 4 échoue : arrêt, retour à l'image précédente, restauration de la sauvegarde de l'étape 2, puis
#      redémarrage. Le message final dit ce qui a réellement été fait.
# Après une mise à jour réussie, le service passe lui-même à la version installée (s'il utilise une image du même
# dépôt), en se recréant en dernier.
#
# Le dossier du projet Compose est monté sur /projet : les commandes Compose sont lancées depuis le même chemin que sur
# l'hôte, si bien que docker-compose.override.yml, .env et les montages relatifs (./charte.json) sont pris en compte
# comme lors d'un « docker compose up -d » à la main.
#
# Le service ne fait que cela : il ne peut ni changer de registre, ni lancer une autre commande.
set -u

ECHANGE=/echange
PROJET=/projet
DEPOT_IMAGE="${MEP_DEPOT_IMAGE:-ghcr.io/soladevsolide/gestion-mep}"
SERVICE="${MEP_SERVICE:-app}"
ATTENTE_SANTE="${MEP_ATTENTE_SANTE:-300}"
# Dossier des sauvegardes dans le conteneur de l'application (volume « sauvegardes »).
SAUVEGARDES="${MEP_DOSSIER_SAUVEGARDES:-/data/sauvegardes}"

mkdir -p "$ECHANGE"
# L'application (utilisateur non root) doit pouvoir y déposer sa demande.
chmod 1777 "$ECHANGE"

maintenant() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# etat <statut> <version> <message> : messages fixes, sans guillemets ni barre oblique inverse.
etat() {
  printf '{"statut":"%s","version":"%s","message":"%s","le":"%s","battement":"%s"}\n' \
    "$1" "$2" "$3" "$(maintenant)" "$(maintenant)" > "$ECHANGE/etat.json.tmp"
  mv -f "$ECHANGE/etat.json.tmp" "$ECHANGE/etat.json"
  echo "[mise-a-jour] $1 $2 : $3"
}

# Battement : met à jour la date sans changer le dernier statut.
battement() {
  if [ -f "$ECHANGE/etat.json" ]; then
    sed "s/\"battement\":\"[^\"]*\"/\"battement\":\"$(maintenant)\"/" "$ECHANGE/etat.json" > "$ECHANGE/etat.json.tmp" \
      && mv -f "$ECHANGE/etat.json.tmp" "$ECHANGE/etat.json"
  else
    etat pret "" "Service de mise à jour prêt."
  fi
}

# pendant <commande…> : lance la commande en entretenant le battement (l'onglet Version ne croit pas le service absent
# pendant un téléchargement, une sauvegarde ou une migration), et renvoie son code de retour.
pendant() {
  "$@" &
  pid=$!
  while kill -0 "$pid" 2> /dev/null; do
    battement
    sleep 3
  done
  wait "$pid"
}

# Projet Compose et service de ce conteneur : la recréation de l'application doit viser le même projet (mêmes volumes).
CONTENEUR=$(hostname)
NOM_PROJET=$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' "$CONTENEUR" 2> /dev/null)
NOM_SERVICE=$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.service" }}' "$CONTENEUR" 2> /dev/null)
if [ -z "$NOM_PROJET" ]; then
  etat indisponible "" "Projet Docker Compose introuvable : lancer ce service avec docker compose."
  exec sleep infinity
fi
if [ ! -w "$PROJET/.env" ]; then
  etat indisponible "" "Fichier .env absent ou non modifiable dans /projet."
  exec sleep infinity
fi

# Chemin du dossier du projet sur l'hôte (source du montage /projet), rendu visible ici sous le même nom.
DOSSIER=$(docker inspect --format '{{ range .Mounts }}{{ if eq .Destination "/projet" }}{{ .Source }}{{ end }}{{ end }}' "$CONTENEUR" 2> /dev/null)
case "$DOSSIER" in
  /projet | "") DOSSIER="$PROJET" ;;
  /*)
    if [ -L "$DOSSIER" ] || [ ! -e "$DOSSIER" ]; then
      mkdir -p "$(dirname "$DOSSIER")" && ln -sfn "$PROJET" "$DOSSIER"
    fi
    [ "$(cd "$DOSSIER" 2> /dev/null && pwd -P)" = "$(cd "$PROJET" && pwd -P)" ] || DOSSIER="$PROJET"
    ;;
  *) DOSSIER="$PROJET" ;;
esac
if [ ! -f "$PROJET/docker-compose.yml" ]; then
  etat indisponible "" "docker-compose.yml absent de /projet : reprendre le docker-compose.yml de la version installée (le dossier du projet y est monté)."
  exec sleep infinity
fi
if [ "$DOSSIER" = "$PROJET" ]; then
  echo "[mise-a-jour] Attention : dossier du projet introuvable sur l'hôte ; les montages relatifs (./charte.json) seraient mal résolus."
fi

# Compose lancé depuis le dossier du projet, comme à la main : docker-compose.yml, docker-compose.override.yml et .env.
compose() { (cd "$DOSSIER" && docker compose --project-name "$NOM_PROJET" "$@"); }

# Mode « --recreer » : conteneur ponctuel lancé par le service lui-même après une mise à jour réussie, qui le remplace
# par sa nouvelle image (le conteneur du service ne peut pas se recréer lui-même : il serait arrêté en chemin).
if [ "${1:-}" = --recreer ]; then
  sleep 2
  compose up -d --no-deps --no-build "$NOM_SERVICE"
  exit $?
fi

valeur() { sed -n "s/^$1=//p" "$PROJET/.env" | tail -n 1; }

# ecrire <NOM> <valeur> : remplace (ou ajoute) la ligne NOM= de .env, en place (le fichier garde ses droits).
ecrire() {
  if grep -q "^$1=" "$PROJET/.env"; then
    sed "s|^$1=.*|$1=$2|" "$PROJET/.env" > "$PROJET/.env.tmp"
    cat "$PROJET/.env.tmp" > "$PROJET/.env" && rm -f "$PROJET/.env.tmp"
  else
    printf '\n%s=%s\n' "$1" "$2" >> "$PROJET/.env"
  fi
}

# Attend que l'application soit prête (HEALTHCHECK de l'image : sonde « prête », base joignable et schéma à jour).
# « unhealthy » n'arrête pas l'attente (démarrage encore en cours) ; un conteneur arrêté ou redémarré, si.
attendre_prete() {
  id=$(compose ps -a -q "$SERVICE" | head -n 1)
  [ -n "$id" ] || return 1
  i=0
  while [ "$i" -lt "$ATTENTE_SANTE" ]; do
    # shellcheck disable=SC2046 # trois mots attendus : état, nombre de redémarrages, santé
    set -- $(docker inspect --format '{{ .State.Status }} {{ .RestartCount }} {{ if .State.Health }}{{ .State.Health.Status }}{{ else }}aucune{{ end }}' "$id" 2> /dev/null)
    case "${1:-absent}" in
      running | created) ;;
      *) return 1 ;;
    esac
    [ "${2:-0}" = 0 ] || return 1
    [ "${3:-}" = healthy ] && return 0
    battement
    sleep 5
    i=$((i + 5))
  done
  return 1
}

# revenir <version> <raison> : retour à l'image précédente avec les données de la sauvegarde d'avant la mise à jour.
revenir() {
  etat en-cours "$1" "$2 : retour à la version précédente et restauration de la sauvegarde $NOM_ARCHIVE."
  pendant compose stop "$SERVICE"
  ecrire MEP_IMAGE "$PRECEDENTE"
  if ! pendant compose run --rm --no-deps -T "$SERVICE" sauvegardes restaurer "$SAUVEGARDES/$NOM_ARCHIVE" --oui; then
    etat echec "$1" "$2. Image précédente remise dans .env, mais la restauration de la sauvegarde $NOM_ARCHIVE a échoué : l'application reste arrêtée pour ne pas tourner sur une base modifiée. Recharger cette sauvegarde à la main (documentation Exploiter, Revenir à la version précédente), puis docker compose up -d."
    return
  fi
  pendant compose up -d --no-deps --no-build "$SERVICE"
  if attendre_prete; then
    etat echec "$1" "$2. Version précédente remise, avec les données d'avant la mise à jour (sauvegarde $NOM_ARCHIVE rechargée). Voir les journaux de l'application avant de réessayer."
  else
    etat echec "$1" "$2. Données d'avant la mise à jour rechargées (sauvegarde $NOM_ARCHIVE), mais la version précédente ne démarre pas non plus : intervention de l'équipe informatique nécessaire (documentation Exploiter)."
  fi
}

# Le service passe à la version installée s'il utilise une image du même dépôt (latest ou numérotée) ; une image propre
# au site (autre registre, construite sur place) est laissée telle quelle.
suivre_version() {
  image=$(docker inspect --format '{{ .Config.Image }}' "$CONTENEUR" 2> /dev/null)
  case "$image" in
    "$DEPOT_IMAGE-mise-a-jour":*) ;;
    *) return ;;
  esac
  cible="$DEPOT_IMAGE-mise-a-jour:$1"
  [ "$image" = "$cible" ] && return
  if ! pendant docker pull -q "$cible" > /dev/null; then
    echo "[mise-a-jour] $cible introuvable : le service garde l'image $image."
    return
  fi
  ecrire MEP_IMAGE_MISE_A_JOUR "$cible"
  echo "[mise-a-jour] Service de mise à jour remplacé par $cible."
  compose run -d --rm --no-deps "$NOM_SERVICE" --recreer > /dev/null
}

installer() {
  version="$1"
  PRECEDENTE=$(valeur MEP_IMAGE)
  case "$PRECEDENTE" in
    "$DEPOT_IMAGE":*) ;;
    *)
      etat echec "$version" "MEP_IMAGE ne désigne pas une version publiée de $DEPOT_IMAGE : mettre à jour à la main."
      return
      ;;
  esac
  nouvelle="$DEPOT_IMAGE:$version"

  etat en-cours "$version" "Téléchargement de la version $version."
  if ! pendant docker pull -q "$nouvelle" > /dev/null; then
    etat echec "$version" "Téléchargement impossible de $nouvelle (version absente du registre ?). Rien n'a été changé."
    return
  fi

  NOM_ARCHIVE="avant-$version-$(date -u +%Y%m%d-%H%M%S).tar.gz"
  etat en-cours "$version" "Arrêt de l'application et sauvegarde de la base et des pièces jointes ($NOM_ARCHIVE)."
  pendant compose stop "$SERVICE"
  if ! pendant compose run --rm --no-deps -T "$SERVICE" sauvegarder "$SAUVEGARDES/$NOM_ARCHIVE"; then
    pendant compose up -d --no-deps --no-build "$SERVICE"
    etat echec "$version" "Sauvegarde impossible (détail : docker compose logs mise-a-jour) : rien n'a été changé, la version en place est relancée."
    return
  fi

  ecrire MEP_IMAGE "$nouvelle"
  etat en-cours "$version" "Migration de la base par la version $version (attendue jusqu'au bout, quelle que soit sa durée)."
  if ! pendant compose run --rm --no-deps -T "$SERVICE" migrer; then
    revenir "$version" "La migration de la base par la version $version a échoué"
    return
  fi

  etat en-cours "$version" "Démarrage de la version $version."
  if pendant compose up -d --no-deps --no-build "$SERVICE" && attendre_prete; then
    etat terminee "$version" "Version $version installée. Sauvegarde d'avant la mise à jour conservée : $NOM_ARCHIVE (à supprimer une fois la version validée)."
    suivre_version "$version"
    return
  fi
  revenir "$version" "La version $version ne démarre pas"
}

battement
while true; do
  if [ -f "$ECHANGE/demande.json" ]; then
    version=$(sed -n 's/.*"version" *: *"\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$ECHANGE/demande.json" | head -n 1)
    rm -f "$ECHANGE/demande.json"
    if [ -n "$version" ]; then
      installer "$version"
    else
      etat echec "" "Demande invalide : numéro de version attendu (1.2.3)."
    fi
  fi
  battement
  sleep 5
done
