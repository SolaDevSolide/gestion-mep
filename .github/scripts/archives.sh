#!/bin/sh
# Archives de l'installation sans Docker (Linux x64 et Windows x64), autonomes (sans .NET à installer) :
#   .github/scripts/archives.sh <version> <dossier de sortie>
# Le front doit déjà être construit (frontend/dist). Produit gestion-mep-<version>-linux-x64.tar.gz,
# gestion-mep-<version>-win-x64.zip, les fichiers de l'installation Docker de cette version (docker-compose.yml et
# env.example, à renommer .env.example : GitHub renomme les fichiers joints dont le nom commence par un point ; images
# fixées à la version, sans construction locale) et SHA256SUMS.
set -eu
VERSION="$1"
SORTIE=$(mkdir -p "$2" && cd "$2" && pwd)
RACINE=$(cd "$(dirname "$0")/../.." && pwd)
[ -f "$RACINE/frontend/dist/index.html" ] || { echo "frontend/dist absent : lancer npm run build dans frontend." >&2; exit 1; }
TRAVAIL=$(mktemp -d)
trap 'rm -rf "$TRAVAIL"' EXIT

for rid in linux-x64 win-x64; do
  nom="gestion-mep-$VERSION-$rid"
  dossier="$TRAVAIL/$nom"
  dotnet publish "$RACINE/backend/src/GestionMep.Api/GestionMep.Api.csproj" -c Release -r "$rid" --self-contained \
    -p:Version="$VERSION" -p:DebugType=none -o "$dossier" --nologo -v q
  mkdir -p "$dossier/wwwroot" && cp -r "$RACINE/frontend/dist/." "$dossier/wwwroot/"
  printf '%s\n' "$VERSION" > "$dossier/VERSION"
  cp "$RACINE/installation/gestion-mep.conf.exemple" "$RACINE/installation/charte.exemple.json" "$RACINE/LICENSE" "$dossier/"
  if [ "$rid" = linux-x64 ]; then
    cp "$RACINE/installation/installer.sh" "$RACINE/installation/gestion-mep.service" "$dossier/"
    chmod 755 "$dossier/installer.sh" "$dossier/GestionMep.Api"
    tar -czf "$SORTIE/$nom.tar.gz" -C "$TRAVAIL" --owner=0 --group=0 "$nom"
  else
    cp "$RACINE/installation/installer.ps1" "$dossier/"
    (cd "$TRAVAIL" && zip -qr "$SORTIE/$nom.zip" "$nom")
  fi
done
# Installation Docker : le docker-compose.yml de la version tire les images publiées de cette même version (application et
# service de mise à jour) au lieu de les construire ; son env.example fixe MEP_IMAGE et MEP_IMAGE_MISE_A_JOUR à cette version.
IMAGE=ghcr.io/soladevsolide/gestion-mep
{
  printf '# Gestion MEP %s : installation Docker (docs/Installer.md). Copier env.example en .env, le compléter, puis\n' "$VERSION"
  printf '# docker compose up -d\n'
  # En-tête du fichier du dépôt (lancement de développement) retiré, images de la version, aucune construction.
  sed -e '1,/^services:/{/^#/d;}' \
    -e "s|\${MEP_IMAGE:-gestion-mep:local}|\${MEP_IMAGE:-$IMAGE:$VERSION}|" \
    -e "s|\${MEP_IMAGE_MISE_A_JOUR:-gestion-mep-mise-a-jour:local}|\${MEP_IMAGE_MISE_A_JOUR:-$IMAGE-mise-a-jour:$VERSION}|" \
    -e '/^    build: /d' \
    "$RACINE/docker-compose.yml"
} > "$SORTIE/docker-compose.yml"
sed -e "s|^# MEP_IMAGE=$IMAGE:1.0.0\$|MEP_IMAGE=$IMAGE:$VERSION|" \
  -e "s|^# MEP_IMAGE_MISE_A_JOUR=$IMAGE-mise-a-jour:1.0.0\$|MEP_IMAGE_MISE_A_JOUR=$IMAGE-mise-a-jour:$VERSION|" \
  "$RACINE/.env.example" > "$SORTIE/env.example"
for f in docker-compose.yml env.example; do
  if grep -Eq 'gestion-mep(-mise-a-jour)?:local|^    build: ' "$SORTIE/$f"; then
    echo "$f : image locale ou construction restée dans le fichier de la version." >&2
    exit 1
  fi
done
grep -q "^MEP_IMAGE=$IMAGE:$VERSION\$" "$SORTIE/env.example" || { echo "env.example : MEP_IMAGE non fixée à $VERSION." >&2; exit 1; }
grep -q "^MEP_IMAGE_MISE_A_JOUR=$IMAGE-mise-a-jour:$VERSION\$" "$SORTIE/env.example" || { echo "env.example : MEP_IMAGE_MISE_A_JOUR non fixée à $VERSION." >&2; exit 1; }

(cd "$SORTIE" && sha256sum "gestion-mep-$VERSION-linux-x64.tar.gz" "gestion-mep-$VERSION-win-x64.zip" docker-compose.yml env.example > SHA256SUMS)
ls -l "$SORTIE"
