#!/bin/sh
# Service de mise à jour de Gestion MEP.
#
# Échanges avec l'application par le volume partagé /echange (aucun port ouvert) :
#   - demande.json : écrit par l'application quand un administrateur clique « Mettre à jour » ; seul le numéro de
#     version y est lu, et il doit être de la forme 1.2.3 ;
#   - etat.json : écrit ici, lu par l'application (statut, message, version, battement de cœur).
#
# Le service ne fait qu'une chose : remplacer l'image de l'application par <MEP_DEPOT_IMAGE>:<version>. Il ne peut
# ni changer de registre, ni lancer une autre commande. La sauvegarde des données est faite par l'application
# elle-même au démarrage de la nouvelle version, avant toute migration.
set -u

ECHANGE=/echange
PROJET=/projet
DEPOT_IMAGE="${MEP_DEPOT_IMAGE:-ghcr.io/soladevsolide/gestion-mep}"
SERVICE="${MEP_SERVICE:-app}"
ATTENTE_SANTE="${MEP_ATTENTE_SANTE:-180}"

mkdir -p "$ECHANGE"
# L'application (utilisateur non root) doit pouvoir y déposer sa demande.
chmod 1777 "$ECHANGE"

maintenant() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# etat <statut> <version> <message> : messages fixes, sans guillemets.
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

# Projet Compose de ce conteneur : la recréation de l'application doit viser le même projet (mêmes volumes).
NOM_PROJET=$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' "$(hostname)" 2>/dev/null)
if [ -z "$NOM_PROJET" ]; then
  etat indisponible "" "Projet Docker Compose introuvable : lancer ce service avec docker compose."
  exec sleep infinity
fi
if [ ! -w "$PROJET/.env" ]; then
  etat indisponible "" "Fichier .env absent ou non modifiable dans /projet."
  exec sleep infinity
fi

compose() { docker compose --project-name "$NOM_PROJET" --project-directory "$PROJET" "$@"; }

image_actuelle() { sed -n 's/^MEP_IMAGE=//p' "$PROJET/.env" | tail -n 1; }

ecrire_image() {
  if grep -q '^MEP_IMAGE=' "$PROJET/.env"; then
    sed "s|^MEP_IMAGE=.*|MEP_IMAGE=$1|" "$PROJET/.env" > "$PROJET/.env.tmp"
    # Réécrit le fichier en place (le fichier monté ne peut pas être remplacé par un renommage).
    cat "$PROJET/.env.tmp" > "$PROJET/.env" && rm -f "$PROJET/.env.tmp"
  else
    printf '\nMEP_IMAGE=%s\n' "$1" >> "$PROJET/.env"
  fi
}

sante() {
  id=$(compose ps -q "$SERVICE")
  [ -n "$id" ] && docker inspect --format '{{ if .State.Health }}{{ .State.Health.Status }}{{ else }}{{ .State.Status }}{{ end }}' "$id"
}

attendre_sante() {
  i=0
  while [ "$i" -lt "$ATTENTE_SANTE" ]; do
    case "$(sante)" in
      healthy) return 0 ;;
      unhealthy|exited|dead) return 1 ;;
    esac
    sleep 5
    i=$((i + 5))
  done
  return 1
}

installer() {
  version="$1"
  precedente=$(image_actuelle)
  case "$precedente" in
    "$DEPOT_IMAGE":*) ;;
    *)
      etat echec "$version" "MEP_IMAGE ne désigne pas une version publiée de $DEPOT_IMAGE : mettre à jour à la main."
      return
      ;;
  esac
  nouvelle="$DEPOT_IMAGE:$version"
  etat en-cours "$version" "Téléchargement de la version $version."
  if ! docker pull "$nouvelle" > /dev/null; then
    etat echec "$version" "Téléchargement impossible de $nouvelle. Rien n'a été changé."
    return
  fi
  etat en-cours "$version" "Redémarrage de l'application en version $version (sauvegarde automatique avant migration)."
  ecrire_image "$nouvelle"
  if compose up -d --no-deps "$SERVICE" && attendre_sante; then
    etat terminee "$version" "Version $version installée."
    return
  fi
  etat en-cours "$version" "La version $version ne démarre pas : retour à la version précédente."
  ecrire_image "$precedente"
  compose up -d --no-deps "$SERVICE"
  if attendre_sante; then
    etat echec "$version" "La version $version n'a pas démarré : version précédente remise. Voir les journaux de l'application et la documentation Exploiter avant de réessayer."
  else
    etat echec "$version" "La version $version n'a pas démarré et la version précédente non plus : intervention de l'équipe informatique nécessaire (documentation Exploiter, Revenir à la version précédente)."
  fi
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
