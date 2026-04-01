#!/usr/bin/env bash
# redeploy.sh — Rebuild and restart Aegis app containers without touching data.
#
# Usage:
#   ./redeploy.sh              — rebuild server + botapi, keep all data volumes
#   ./redeploy.sh --server     — rebuild only the main TCP server
#   ./redeploy.sh --botapi     — rebuild only the Bot API / developer portal
#   ./redeploy.sh --all        — same as default (both)
#   ./redeploy.sh --help       — show this help
#
# Data volumes (postgres, redis, minio, elasticsearch) are NEVER touched.
# Running ./redeploy.sh is safe to call at any time during development.

set -euo pipefail

COMPOSE_FILE="$(dirname "$0")/docker-compose.yml"
ENV_FILE="$(dirname "$0")/.env"

REBUILD_SERVER=true
REBUILD_BOTAPI=true

for arg in "$@"; do
  case "$arg" in
    --server) REBUILD_SERVER=true;  REBUILD_BOTAPI=false ;;
    --botapi) REBUILD_SERVER=false; REBUILD_BOTAPI=true  ;;
    --all)    REBUILD_SERVER=true;  REBUILD_BOTAPI=true  ;;
    --help|-h)
      sed -n '2,13p' "$0" | sed 's/^# \?//'
      exit 0
      ;;
    *)
      echo "Unknown option: $arg (use --help)"
      exit 1
      ;;
  esac
done

# Resolve which services to rebuild
SERVICES=()
$REBUILD_SERVER && SERVICES+=(aegis-server)
$REBUILD_BOTAPI && SERVICES+=(aegis-botapi)

if [ ${#SERVICES[@]} -eq 0 ]; then
  echo "Nothing to rebuild."
  exit 0
fi

echo ""
echo "╔══════════════════════════════════════════════════╗"
echo "║          Aegis — Hot Redeploy Script             ║"
echo "╚══════════════════════════════════════════════════╝"
echo ""
echo "Services to rebuild: ${SERVICES[*]}"
echo "Data volumes will NOT be removed."
echo ""

# ── 1. Stop and remove only the app containers (not infra) ──────────────
echo "► Stopping app containers…"
docker-compose \
  --file "$COMPOSE_FILE" \
  --env-file "$ENV_FILE" \
  stop "${SERVICES[@]}"

docker-compose \
  --file "$COMPOSE_FILE" \
  --env-file "$ENV_FILE" \
  rm -f "${SERVICES[@]}"

# ── 2. Rebuild images without cache ────────────────────────────────────
echo ""
echo "► Building images (no cache)…"
docker-compose \
  --file "$COMPOSE_FILE" \
  --env-file "$ENV_FILE" \
  build --no-cache --pull "${SERVICES[@]}"

# ── 3. Start the fresh containers ──────────────────────────────────────
echo ""
echo "► Starting containers…"
docker-compose \
  --file "$COMPOSE_FILE" \
  --env-file "$ENV_FILE" \
  up -d "${SERVICES[@]}"

# ── 4. Follow logs for a few seconds to confirm startup ────────────────
echo ""
echo "► Container logs (last 30 lines each, Ctrl-C to exit):"
echo ""
sleep 3
docker-compose \
  --file "$COMPOSE_FILE" \
  --env-file "$ENV_FILE" \
  logs --tail=30 "${SERVICES[@]}"

echo ""
echo "✔  Redeploy complete."
echo ""
echo "   Server:      tcp://localhost:8888"
echo "   Bot API:     http://localhost:5000"
echo "   Dev Portal:  http://localhost:5000/portal"
echo "   Swagger:     http://localhost:5000/swagger"
echo ""
