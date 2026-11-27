#!/usr/bin/env bash
# Manuell uppgradering av Resurs & Kapacitet på servern.
#   ./upgrade.sh            -> senaste versionen (:latest)
#   ./upgrade.sh 1.2.0      -> specifik version (används även för att rulla tillbaka)
#
# Databasen säkerhetskopieras automatiskt av appen innan schemaändringar (se /data/backups i volymen).
set -euo pipefail

IMAGE_REPO="${IMAGE_REPO:-docker.io/DITT-KONTO/resurs-kapacitet}"
TAG="${1:-latest}"
SERVICE="${SERVICE:-resurs-kapacitet}"
SYSTEMCTL="systemctl --user"
[ "$(id -u)" -eq 0 ] && SYSTEMCTL="systemctl"

echo "==> Hämtar ${IMAGE_REPO}:${TAG}"
podman pull "${IMAGE_REPO}:${TAG}"

if [ "$TAG" != "latest" ]; then
  echo "==> Taggar ${TAG} som latest lokalt (för att tjänsten ska använda den)"
  podman tag "${IMAGE_REPO}:${TAG}" "${IMAGE_REPO}:latest"
fi

echo "==> Startar om ${SERVICE}"
$SYSTEMCTL restart "${SERVICE}"

echo "==> Väntar på hälsokontroll"
for i in $(seq 1 30); do
  if curl -fsS "http://localhost:${PORT:-8080}/api/health" >/dev/null 2>&1; then
    echo "==> OK! Version: $(curl -fsS "http://localhost:${PORT:-8080}/api/version")"
    exit 0
  fi
  sleep 2
done
echo "!! Appen svarar inte. Kontrollera: podman logs ${SERVICE}" >&2
exit 1
