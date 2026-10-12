#!/usr/bin/env bash
#
# Checks whom an image of the app runs as (MAM-39): the default user, PUID and PGID with the data
# volume's hand-over to them and back, the privileges and groups the app is left with, a user set by
# user: or --user, the ids and the capability-less root the entrypoint refuses, a start with nothing
# to hand over, media folders that keep their owner, and a quick docker stop. Run it against a locally built image before a release:
#
#   docker build -t vahac/media-acquisition-manager:local .
#   bash tools/check-image-user.sh vahac/media-acquisition-manager:local
#
# It pulls, builds and pushes nothing. Every container and volume it makes is named
# mam39-<pid>-<random>-…, and it removes them all when it ends, also after a failure or Ctrl+C. It
# prints PASS or FAIL for each scenario, a to h, with what failed, and exits non-zero when any
# fails. About two minutes: each start waits for the image's health check, which probes every 2 s
# while the container starts.
set -euo pipefail

IMAGE=${1:?Usage: bash tools/check-image-user.sh <image>, e.g. vahac/media-acquisition-manager:local}

# Git Bash on Windows would turn the container paths below (/data, /proc/1/status) into Windows
# ones.
export MSYS_NO_PATHCONV=1

RUN_ID="mam39-$$-$RANDOM"
DATA="$RUN_ID-data"         # the data volume of a, b, c, e and f
MEDIA="$RUN_ID-media"       # a media folder of the reference installation's owner, 1000:100 (g)
USERDATA="$RUN_ID-userdata" # the data volume of d, made 1000:100's beforehand
NESTED="$RUN_ID-nested"     # the data volume of g's start with the media folder mounted inside it
HEALTH_TIMEOUT=180          # seconds; the image's start period is 150 s
STOP_LIMIT_MS=5000          # docker stop kills the app after 10 s; a clean stop takes about one

cleanup() {
  local containers
  mapfile -t containers < <(docker ps -aq --filter "name=^$RUN_ID-" | tr -d '\r') || true
  if ((${#containers[@]} > 0)); then docker rm -f "${containers[@]}" > /dev/null 2>&1 || true; fi
  docker volume rm -f "$DATA" "$MEDIA" "$USERDATA" "$NESTED" > /dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ── verdicts ──────────────────────────────────────────────────────────────────────────────────────
FAILURES=() # what failed in the scenario being checked
FAILED=0    # scenarios that failed

# shown VALUE: VALUE on one line, cut short when it is a whole log.
shown() {
  local value=${1//$'\n'/ | }
  if ((${#value} > 400)); then value="${value:0:400}…"; fi
  echo "$value"
}
# expect WHAT ACTUAL EXPECTED
expect() {
  [[ $2 == "$3" ]] || FAILURES+=("$1: expected \"$(shown "$3")\", got \"$(shown "$2")\"")
}
# expect_match WHAT ACTUAL REGEX: one line of ACTUAL matches REGEX (extended, whole line).
expect_match() {
  grep -Eqx -- "$3" <<< "$2" || FAILURES+=("$1: expected a line like /$3/, got \"$(shown "$2")\"")
}
# verdict ID TITLE: PASS or FAIL for the scenario, with what failed.
verdict() {
  if ((${#FAILURES[@]} == 0)); then
    echo "PASS $1: $2"
  else
    echo "FAIL $1: $2"
    printf '       - %s\n' "${FAILURES[@]}"
    FAILED=$((FAILED + 1))
  fi
  FAILURES=()
}

# ── containers ────────────────────────────────────────────────────────────────────────────────────
# app ID [docker run options...]: starts the image's app in container $RUN_ID-ID, detached.
app() {
  local id=$1
  shift
  docker run -d --name "$RUN_ID-$id" "$@" "$IMAGE" > /dev/null
}

logs() { docker logs "$RUN_ID-$1" 2>&1 | tr -d '\r'; }

# entrypoint_lines ID: what the entrypoint printed, as opposed to the app.
entrypoint_lines() { logs "$1" | grep '^\[entrypoint\]' || true; }

# healthy ID: waits until the image's health check passes; shows the log when it doesn't.
healthy() {
  local name="$RUN_ID-$1" state='' i
  for ((i = 0; i < HEALTH_TIMEOUT; i++)); do
    state=$(docker inspect -f '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{end}}' "$name" 2>&1 | tr -d '\r') || true
    case $state in
      'running healthy') return 0 ;;
      'running starting') sleep 1 ;;
      *) break ;;
    esac
  done
  echo "  $1 is not healthy ($state); the end of its log:" >&2
  logs "$1" | tail -n 15 | sed 's/^/    /' >&2 || true
  return 1
}
ensure_healthy() { healthy "$1" || FAILURES+=("not healthy"); }

# status ID FIELD: FIELD of /proc/1/status in the container (the app is PID 1), tabs as spaces.
status() {
  docker exec "$RUN_ID-$1" cat /proc/1/status | tr -d '\r' |
    awk -v field="$2:" '$1 == field { $1 = ""; sub(/^ /, ""); print }'
}

# home ID USER: HOME in the app's environment, read as the app's own user (root in the container has
# no CAP_SYS_PTRACE, which reading another user's environment takes).
home() {
  docker exec -u "$2" "$RUN_ID-$1" sh -c "tr '\\0' '\\n' < /proc/1/environ" | tr -d '\r' | sed -n 's/^HOME=//p'
}

# expect_ids ID UID GID: the app is PID 1 and runs as UID:GID, real, effective, saved and fs ids.
expect_ids() {
  expect "PID 1" "$(status "$1" Name)" dotnet
  expect "uid" "$(status "$1" Uid)" "$2 $2 $2 $2"
  expect "gid" "$(status "$1" Gid)" "$3 $3 $3 $3"
}

declare -A STOP_MS=() STOP_CODE=()
STOPPED=() # IDs in the order they were stopped
# timed_stop ID: docker stop, timed, keeping the app's exit code.
timed_stop() {
  local start end
  start=$(date +%s%N)
  docker stop "$RUN_ID-$1" > /dev/null || true
  end=$(date +%s%N)
  STOP_MS[$1]=$(((end - start) / 1000000))
  STOP_CODE[$1]=$(docker inspect -f '{{.State.ExitCode}}' "$RUN_ID-$1" | tr -d '\r')
  STOPPED+=("$1")
}

# refused ID LOG [docker run options...]: a start the entrypoint must refuse with a non-zero exit and
# LOG as its whole output, before it hands anything over or starts the app.
refused() {
  local id=$1 log=$2 name="$RUN_ID-$1" state='' i
  shift 2
  docker run -d --name "$name" "$@" -v "$DATA:/data" "$IMAGE" > /dev/null
  for ((i = 0; i < 30; i++)); do
    state=$(docker inspect -f '{{.State.Status}}' "$name" | tr -d '\r')
    [[ $state == created || $state == running ]] || break
    sleep 1
  done
  expect "$*: state" "$state" exited
  [[ $(docker inspect -f '{{.State.ExitCode}}' "$name" | tr -d '\r') != 0 ]] || FAILURES+=("$*: exit code 0")
  expect "$*: log" "$(logs "$id")" "$log"
  # An app that started after all must not share the data volume with the scenarios after this one.
  if [[ $state != exited ]]; then docker rm -f "$name" > /dev/null; fi
}

# ── volumes ───────────────────────────────────────────────────────────────────────────────────────
# in_volume VOLUME COMMAND [ARGS...]: runs COMMAND as root in a throwaway container of the image
# with VOLUME at /v and the entrypoint bypassed, to prepare or look at a volume whoever owns it.
in_volume() {
  local volume=$1 command=$2
  shift 2
  docker run --rm --network none --user 0:0 -v "$volume:/v" --entrypoint "$command" "$IMAGE" "$@" | tr -d '\r'
}
# listing VOLUME: "uid:gid mode path" for the volume (/v) and everything in it, by path.
listing() { in_volume "$1" find /v -printf '%U:%G %m %p\n' | sort -k 3; }
# entry LISTING PATH: "uid:gid mode" of PATH.
entry() { awk -v path="$2" '$3 == path { print $1 " " $2 }' <<< "$1"; }
# not_owned_by LISTING IDS: the entries anyone else owns.
not_owned_by() { awk -v ids="$2" '$1 != ids { print $3 " " $1 }' <<< "$1"; }
# mode_changes BEFORE AFTER: the paths in both listings whose mode differs.
mode_changes() {
  awk 'NR == FNR { mode[$3] = $2; next } ($3 in mode) && mode[$3] != $2 { print $3 " " mode[$3] "->" $2 }' \
    <(echo "$1") <(echo "$2")
}
# handed_over LINE: the number of entries a hand-over line reports.
handed_over() { sed -nE 's/.*: ([0-9]+) files? (and|or) folders? changed owner.*/\1/p' <<< "$1"; }
entries() { wc -l <<< "$1" | tr -d ' '; }

HANDED='\[entrypoint\] Handed /data over to'
UNCHANGED='changed owner; permissions are unchanged\.'

echo "Checking $IMAGE with containers and volumes named $RUN_ID-…"

# A media folder as the reference installation has it: its owner's 1000:100, a film inside, modes
# that aren't the defaults. Mounted read-write wherever it appears, so only the entrypoint's own
# restraint keeps it as it is (g).
in_volume "$MEDIA" sh -c 'mkdir /v/Films && echo film > /v/Films/film.mkv &&
  chown -R 1000:100 /v && chmod 0750 /v /v/Films && chmod 0640 /v/Films/film.mkv'
MEDIA_BEFORE=$(listing "$MEDIA")

# ── a ─────────────────────────────────────────────────────────────────────────────────────────────
app a -v "$DATA:/data" -v "$MEDIA:/media"
ensure_healthy a
expect_ids a 10001 10001
expect "HOME" "$(home a 10001:10001)" /nonexistent
expect "entrypoint lines on a fresh volume" "$(entrypoint_lines a)" ""
timed_stop a
AFTER_A=$(listing "$DATA")
expect "/data" "$(entry "$AFTER_A" /v)" "10001:10001 700"
expect "entries not 10001:10001" "$(not_owned_by "$AFTER_A" 10001:10001)" ""
verdict a "default start: the app runs as 10001:10001 and is healthy; /data is 10001:10001, mode 700"

# ── b ─────────────────────────────────────────────────────────────────────────────────────────────
# --group-add: how an installation lets the app read media only a group may read; it stays the
# app's, and root's own group 0 doesn't come along.
app b -e PUID=4242 -e PGID=4343 --group-add 4444 -v "$DATA:/data" -v "$MEDIA:/media"
ensure_healthy b
expect_ids b 4242 4343
expect "supplementary groups" "$(status b Groups)" 4444
for field in CapInh CapPrm CapEff CapBnd CapAmb; do
  expect "$field" "$(status b "$field")" 0000000000000000
done
expect "NoNewPrivs" "$(status b NoNewPrivs)" 1
expect "HOME" "$(home b 4242:4343)" /nonexistent
LINE=$(entrypoint_lines b)
expect_match "hand-over line" "$LINE" "$HANDED 4242:4343: [0-9]+ files and folders $UNCHANGED"
expect "entries handed over" "$(handed_over "$LINE")" "$(entries "$AFTER_A")"
timed_stop b
AFTER_B=$(listing "$DATA")
expect "/data" "$(entry "$AFTER_B" /v)" "4242:4343 700"
expect "/data/.secrets" "$(entry "$AFTER_B" /v/.secrets)" "4242:4343 700"
expect "encryption-key" "$(entry "$AFTER_B" /v/.secrets/encryption-key)" "4242:4343 600"
expect "jwt-secret" "$(entry "$AFTER_B" /v/.secrets/jwt-secret)" "4242:4343 600"
expect "entries not 4242:4343" "$(not_owned_by "$AFTER_B" 4242:4343)" ""
expect "modes changed" "$(mode_changes "$AFTER_A" "$AFTER_B")" ""
verdict b "PUID=4242 PGID=4343 on that volume: all of /data handed over, modes kept; the app runs as 4242:4343 with no capabilities, only the group_add group, and is healthy"

# ── c ─────────────────────────────────────────────────────────────────────────────────────────────
app c -v "$DATA:/data" -v "$MEDIA:/media"
ensure_healthy c
expect_ids c 10001 10001
expect "supplementary groups" "$(status c Groups)" ""
LINE=$(entrypoint_lines c)
expect_match "hand-over line" "$LINE" "$HANDED 10001:10001: [0-9]+ files and folders $UNCHANGED"
expect "entries handed over" "$(handed_over "$LINE")" "$(entries "$AFTER_B")"
timed_stop c
AFTER_C=$(listing "$DATA")
expect "/data" "$(entry "$AFTER_C" /v)" "10001:10001 700"
expect "entries not 10001:10001" "$(not_owned_by "$AFTER_C" 10001:10001)" ""
expect "modes changed" "$(mode_changes "$AFTER_B" "$AFTER_C")" ""
verdict c "PUID and PGID removed again: /data handed back to 10001:10001, modes kept, healthy"

# ── d ─────────────────────────────────────────────────────────────────────────────────────────────
# Not empty, so Docker doesn't copy the image's /data owner into it on the first mount.
in_volume "$USERDATA" sh -c 'touch /v/made-beforehand && chown -R 1000:100 /v && chmod 0700 /v'
app d --user 1000:100 -e PUID=4242 -v "$USERDATA:/data"
ensure_healthy d
expect_ids d 1000 100
expect "entrypoint lines" "$(entrypoint_lines d)" \
  "[entrypoint] PUID and PGID are ignored: the container already runs as 1000:100, set by user: in docker-compose.yml or by docker run --user. Remove that to use PUID and PGID."
timed_stop d
AFTER_D=$(listing "$USERDATA")
expect "/data" "$(entry "$AFTER_D" /v)" "1000:100 700"
expect "entries not 1000:100" "$(not_owned_by "$AFTER_D" 1000:100)" ""
verdict d "--user 1000:100 with PUID=4242: the app runs as 1000:100, the log says PUID and PGID are ignored, healthy"

# ── e ─────────────────────────────────────────────────────────────────────────────────────────────
refused e1 "[entrypoint] PUID is 0: the app never runs as root. Set PUID and PGID to the owner of the media folders, or remove them to run as the image's own 10001." -e PUID=0
refused e2 "[entrypoint] PGID is 0: the app never runs as root. Set PUID and PGID to the owner of the media folders, or remove them to run as the image's own 10001." -e PGID=0
refused e3 '[entrypoint] PGID must be a number from 1 to 4294967294, such as 100, not "abc".' -e PGID=abc
refused e4 '[entrypoint] PUID must be a number from 1 to 4294967294, such as 1000, not "4294967295".' -e PUID=4294967295
# cap_drop: ALL, harmless while the image started as its own user: refused up front, with the way out.
refused e5 '[entrypoint] The container runs as root without the capabilities it takes to switch to 10001:10001 (CHOWN, DAC_OVERRIDE, SETGID, SETUID and SETPCAP; cap_drop: ALL removes them), so the app was not started. Keep Docker'"'"'s default capabilities, or run the container as a user instead: user: "10001:10001" in docker-compose.yml, with the data volume already theirs.' --cap-drop ALL
expect "data volume after the refused starts" "$(listing "$DATA")" "$AFTER_C"
verdict e "PUID=0, PGID=0, PGID=abc, PUID=4294967295 and root without capabilities (cap_drop: ALL) are refused with a non-zero exit, and /data stays as it was"

# ── f ─────────────────────────────────────────────────────────────────────────────────────────────
app f -v "$DATA:/data" -v "$MEDIA:/media"
ensure_healthy f
expect "entrypoint lines" "$(entrypoint_lines f)" ""
docker restart "$RUN_ID-f" > /dev/null
ensure_healthy f
expect_ids f 10001 10001
expect "entrypoint lines after docker restart" "$(entrypoint_lines f)" ""
timed_stop f
expect "data volume" "$(not_owned_by "$(listing "$DATA")" 10001:10001)" ""
verdict f "a start and a docker restart with nothing to hand over: no hand-over line, healthy"

# ── g ─────────────────────────────────────────────────────────────────────────────────────────────
# The media folder mounted inside the data directory as well, on a start that hands /data over:
# only /data itself may change hands. Docker creates the mount point in the volume as root, so the
# volume's own listing isn't checked here.
app g -e PUID=4242 -e PGID=4343 -v "$NESTED:/data" -v "$MEDIA:/data/media"
ensure_healthy g
expect "entrypoint lines" "$(entrypoint_lines g)" \
  "[entrypoint] Handed /data over to 4242:4343: 1 file or folder changed owner; permissions are unchanged."
timed_stop g
expect "media folder" "$(listing "$MEDIA")" "$MEDIA_BEFORE"
verdict g "a media folder (1000:100, modes 750/640) mounted at /media in a, b, c and f and inside /data on a hand-over keeps its owner and modes"

# ── h ─────────────────────────────────────────────────────────────────────────────────────────────
TIMES=''
for id in "${STOPPED[@]}"; do
  ms=${STOP_MS[$id]}
  ((ms <= STOP_LIMIT_MS)) || FAILURES+=("$id: docker stop took $ms ms")
  expect "$id: the app's exit code" "${STOP_CODE[$id]}" 0
  TIMES+="$id $ms ms, "
done
verdict h "docker stop ends the app within $((STOP_LIMIT_MS / 1000)) s and it exits cleanly (${TIMES%, })"

if ((FAILED > 0)); then
  echo "$FAILED of 8 scenarios failed."
  exit 1
fi
echo "All 8 scenarios passed."
