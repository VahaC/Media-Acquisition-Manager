#!/usr/bin/env bash
#
# Updates a Docker Compose installation of Media Acquisition Manager (MAM-3): pulls the image that
# APP_TAG names (default latest), recreates the container and waits until it reports healthy. Run it
# from the directory that holds docker-compose.yml (and your .env, if you have one):
#
#   bash deploy.sh
#
# HEALTH_TIMEOUT=<seconds> overrides how long it waits (default 200). The app migrates its database on
# startup, so there is no separate migration step; the data stays on the mam-data volume.
set -euo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
info()  { echo -e "${GREEN}[deploy]${NC} $*"; }
warn()  { echo -e "${YELLOW}[deploy]${NC} $*"; }
error() { echo -e "${RED}[deploy]${NC} $*" >&2; exit 1; }

SERVICE=app
# The image probes every 2 s during its 150 s start period (long enough for the app's wait on a
# migration lock a killed container left behind) and every 30 s after it. A start that ends just after
# the start period is therefore seen only at the next 30 s probe; 200 s covers that with some slack.
HEALTH_TIMEOUT=${HEALTH_TIMEOUT:-200}

# ── pre-flight ────────────────────────────────────────────────────────────────────────────────────
command -v docker > /dev/null || error "Docker is not installed or not on PATH."
docker compose version > /dev/null 2>&1 || error "Docker Compose v2 ('docker compose') is required."
[[ -f docker-compose.yml ]] || error "Run this from the directory that holds docker-compose.yml."
# .env is optional: the compose file has a default for everything and the app generates its secrets.
[[ -f .env ]] || warn "No .env here, so the defaults apply: tag latest, port 8118 on 127.0.0.1, secrets generated on the first start."

# ── pull and recreate ─────────────────────────────────────────────────────────────────────────────
info "Pulling the image..."
docker compose pull "$SERVICE"

info "Starting the container..."
docker compose up -d "$SERVICE"

container=$(docker compose ps -q "$SERVICE")
[[ -n "$container" ]] || error "The container was not created."

# ── wait for healthy ──────────────────────────────────────────────────────────────────────────────
# The image's HEALTHCHECK probes /api/health/ready: healthy means the app answers and its database is
# open and fully migrated.
fail() {
  warn "$1"
  # Docker keeps the last five probes; a failing readiness check names its reason in the JSON.
  warn "Recent health probes (exit code, output):"
  docker inspect --format '{{if .State.Health}}{{range .State.Health.Log}}[{{.ExitCode}}] {{.Output}}{{println}}{{end}}{{end}}' "$container" || true
  warn "Container logs (last 50 lines):"
  docker compose logs --tail=50 "$SERVICE" || true
  error "Deployment failed."
}

info "Waiting up to ${HEALTH_TIMEOUT}s for the container to report healthy..."
SECONDS=0
while true; do
  state=$(docker inspect --format '{{.State.Status}}' "$container")
  health=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$container")

  [[ "$health" == healthy ]] && break
  [[ "$health" == unhealthy ]] && fail "The container reports unhealthy."
  [[ "$state" == exited || "$state" == dead ]] && fail "The container stopped (state: $state)."
  (( SECONDS >= HEALTH_TIMEOUT )) && fail "The container did not report healthy within ${HEALTH_TIMEOUT}s (state: $state, health: ${health:-none})."

  sleep 2
done

info "Deployed $(docker inspect --format '{{.Config.Image}}' "$container"), healthy after ${SECONDS}s."
