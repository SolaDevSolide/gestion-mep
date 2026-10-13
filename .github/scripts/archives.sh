#!/bin/sh
# Archives de l'installation sans Docker (Linux x64 et Windows x64), autonomes (sans .NET à installer) :
#   .github/scripts/archives.sh <version> <dossier de sortie>
# Le front doit déjà être construit (frontend/dist). Produit gestion-mep-<version>-linux-x64.tar.gz,
# gestion-mep-<version>-win-x64.zip et SHA256SUMS.
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
  cp "$RACINE/installation/gestion-mep.conf.exemple" "$RACINE/LICENSE" "$dossier/"
  if [ "$rid" = linux-x64 ]; then
    cp "$RACINE/installation/installer.sh" "$RACINE/installation/gestion-mep.service" "$dossier/"
    chmod 755 "$dossier/installer.sh" "$dossier/GestionMep.Api"
    tar -czf "$SORTIE/$nom.tar.gz" -C "$TRAVAIL" --owner=0 --group=0 "$nom"
  else
    cp "$RACINE/installation/installer.ps1" "$dossier/"
    (cd "$TRAVAIL" && zip -qr "$SORTIE/$nom.zip" "$nom")
  fi
done
(cd "$SORTIE" && sha256sum "gestion-mep-$VERSION-linux-x64.tar.gz" "gestion-mep-$VERSION-win-x64.zip" > SHA256SUMS)
ls -l "$SORTIE"
