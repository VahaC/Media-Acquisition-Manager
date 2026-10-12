r"""MAM-28 report: what the media host, MAM's container, its database and the media folders say at one step of Phase C's
live check (replacement switched on, a film and an episode upgraded, the old copies quarantined, one restored, one
deleted for good). Run it at every step; each run says what changed since the report before it. The first step,
"before", is also the runbook's read-only pre-flight (.claude/mam28-check/runbook.md, section 0.1) with a verdict on
each read, and lists the candidates to pick the film and the episode from (section D).

STRICTLY READ-ONLY. One ssh call to the media host runs read commands only: docker inspect / image inspect / ps /
compose version / exec (id, stat, command -v, ffprobe -version, cat /proc/self/mountinfo and the shell's test -w, which
asks the kernel without writing), docker cp of two of the app's own files to stdout, stat, df, du, findmnt, find
-printf, awk and grep of files and of /proc, cp of the database into a mktemp folder, tar. The database copy is opened
on this PC with SQLite mode=ro and every statement must be a SELECT that names its columns. Nothing in MAM, Jellyfin,
qBittorrent, the compose project or the media folders is changed, and nothing is sent to any of them.

Reads, over ssh (root@192.168.1.195 asks for its password itself, once):
  - a copy of mam.db, mam.db-wal and mam.db-shm from the MAM container's /data mount, never the .secrets folder: no
    file of it is copied, listed or read, and no password hash, session token or sealed key is selected;
  - the container: image, tag, digest, created and started, Config.User, every mount (type, source, destination,
    read-write, propagation), the main process's uid, gid, groups and umask, `id` inside it, owner and mode of /data
    and mam.db (on the host and as the app sees them), the version the app's files carry (its deps.json and, when the
    build stamped one, the informational version in its dll), its /proc/self/mountinfo, and whether its user may write
    to each media mount, the folders directly in it and its .mam-quarantine (test -w, which a read-only mount fails);
  - the media mounts: df of each mount's source and of every mount inside it, and a file inventory of each mount
    (type, size, mtime, inode, link count, device, uid, gid, mode and path of every entry), taken on the host with find
    as root; the data volume is never part of it;
  - the pre-flight: `docker compose version`; of the compose project's folder (--compose-dir, else the one compose
    recorded on the container, else /srv/docker/media-acquisition-manager) only the lines of the compose file about
    image:, user:, restart:, read_only: and /media, and only the lines of .env that start with APP_ (every other line
    of .env may hold a secret and is never printed, nor is any further line of a quoted value that runs over several
    lines; a compose line that names a secret is left out even then, and an APP_ line that names one in its name or its
    value is printed without its value); the names and
    images of running containers that look like an auto-updater (watchtower, diun, ouroboros, wud) or like Jellyfin;
    findmnt -T of each media mount's source and findmnt -R of the file system holding it; the folders under each media
    mount's source that uid 1000 doesn't own or can't write and search in (find -xdev -type d); command -v chown and
    ffprobe -version in the container; uname -m; du of the data volume without .secrets and df of /root and
    /var/lib/docker (room for the backup). A read that fails prints as unknown; it never stops the run.
Candidates (labels before and candidates, or --candidates): computed on this PC from the database copy and the
  inventory, nothing more is read for them.
Writes: only under D:\Sources\Media-Acquisition-Manager\.claude\mam28-check\ (gitignored): report-<label>.txt and
        report-<label>.json (a report of the same label written before is kept as report-<label>-<UTC time>.*), and
        while it runs report-db-copy\ (deleted at the end). On the media host the one thing it creates is a mktemp -d
        folder that holds the copy while it is streamed back; the same command removes it, also when it fails.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam28\report.py before
        python D:\Sources\Media-Acquisition-Manager\tools\mam28\report.py on
        ... scheduled, moved, restored, deleted (any label of letters, digits, . _ -)
Each run compares itself with the report written before it (--against <label> picks another, --against none skips it):
which files appeared, went, moved (same inode at a new path) or changed, the free space, the container, the compose
lines, the APP_ lines and the folders uid 1000 can't write in, the switch and ticks, every replacement job, copy, new
file and quarantine name, the download jobs and the new audit rows. When it ends, tell Claude about the files it names
on its last line.
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import io
import json
import re
import shlex
import shutil
import sqlite3
import sys
import tarfile
import time
from collections import Counter, defaultdict
from pathlib import Path

# common.py of the MAM-34 tools: the console setup, the ssh runner and the error type. Nothing else is shared.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mam34"))
import common  # noqa: E402
from common import ToolError  # noqa: E402

TOOL = "mam28-report"
OUT_DIR = common.REPO_ROOT / ".claude" / "mam28-check"
COPY_DIR_NAME = "report-db-copy"
DATABASE_FILES = ("mam.db", "mam.db-wal", "mam.db-shm")
FACTS_FILE = "facts.txt"
INVENTORY_INDEX = "inventory.txt"
INVENTORY_ERRORS = "inventory-errors.txt"
INVENTORY_PART = re.compile(r"inventory-([0-9]{1,3})\.bin")
LABEL = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,39}")
KEPT_LABEL = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,39}(-[0-9]{8}T[0-9]{6}Z)?")  # --against may name a kept report
QUARANTINE = ".mam-quarantine"  # QuarantineFolder.Name
LIST_LIMIT = 40     # rows of an ordinary list printed in the text report; the JSON keeps them all
DIFF_LIMIT = 200    # rows of each list of changed files printed in the text report
# Matched in lower case: "Still held by the torrent(s) …" stops a video, "Held by the torrent …" keeps a companion.
HELD = "held by the torrent"

# What the runbook's pre-flight (.claude/mam28-check/runbook.md, section 0.1) expects of the host before anything
# changes: the published 0.3.0 (Docker Hub's 0.3.0 = 0.3 = latest, pushed 2026-10-11 14:12 UTC) running as the image's
# own user, one read-write media mount with nothing mounted inside it, and no auto-updater.
DEFAULT_COMPOSE_DIR = "/srv/docker/media-acquisition-manager"
PUBLISHED_IMAGES = ("vahac/media-acquisition-manager:latest", "vahac/media-acquisition-manager:0.3.0",
                    "vahac/media-acquisition-manager:0.3")
PUBLISHED_DIGEST = "sha256:0b75fdbd7f60"   # the 0.3.0 index's digest, as far as the runbook quotes it
PUBLISHED_VERSION = "0.3.0"
PUBLISHED_REVISION = "2dec720"
PUBLISHED_SINCE = "2026-10-11"
COMPOSE_AT_LEAST = (2, 24)                 # env_file's `required: false` needs it
IMAGE_UID = "10001"                        # the image's own user, mam
MEDIA_OWNER_UID = "1000"                   # who owns the media folders, and whose ids the app takes on in step 2
KNOWN_ROOT_OWNED = 4                       # folders under /media owned by root on 2026-10-08 (runbook section 0)
STRICT_LABEL = "before"                    # the step whose expectations are section 0.1's in full

# The Candidates section (runbook section D): computed for these labels, and for any label with --candidates.
CANDIDATE_LABELS = ("before", "candidates")
CANDIDATE_LIMIT = 15
EPISODES_PER_SERIES = 2
VIDEO_EXTENSIONS = frozenset((
    "3gp", "asf", "avi", "divx", "flv", "iso", "m2ts", "m2v", "m4v", "mk3d", "mkv", "mov", "mp4", "mpeg", "mpg", "mts",
    "ogm", "ogv", "rm", "rmvb", "ts", "vob", "webm", "wmv", "wtv"))
DISC_FOLDERS = ("bdmv", "video_ts")
# A picture attached to a file (a cover) is a video track to ffprobe; it is never the film.
PICTURE_CODECS = frozenset(("mjpeg", "png", "bmp", "gif", "webp"))
# Older codecs first: at one resolution the older one gains most from an upgrade. Anything unknown sits with h264.
CODEC_AGE = {"mpeg1video": 0, "mpeg2video": 0, "h263": 1, "msmpeg4v1": 1, "msmpeg4v2": 1, "msmpeg4v3": 1, "mpeg4": 1,
             "xvid": 1, "divx": 1, "wmv1": 1, "wmv2": 1, "wmv3": 1, "rv30": 1, "rv40": 1, "vc1": 2, "vp8": 2,
             "h264": 2, "hevc": 3, "vp9": 3, "av1": 4}
# A letter glued to the episode number (S03E09a, the a/b halves) and a file of several episodes (S01E01-E02, S01E01E02,
# S01E01-02, but not S01E01-1080p).
HALF_EPISODE = re.compile(r"[Ss]\d{1,3}[Ee]\d{1,4}[A-Za-z](?![A-Za-z0-9])")
MULTI_EPISODE = re.compile(r"[Ss]\d{1,3}[Ee]\d{1,4}(?:[-_ .]?[Ee]\d{1,4}|-\d{1,3}(?![0-9pPiI]))")
# One part of a film split over several files, by the end of its file name without the extension: "Film.a", "Film-b",
# "Film CD1", "Film.part2", "Film_pt1", "Film disc 2".
FILM_PART = re.compile(r"(?i)(?:[-_. ](?:cd|part|pt|disc|disk)[-_. ]?\d{1,2}|[-_. ][ab])$")
# ReplacementJobStates.Open: a copy a job in one of these holds can't be picked by another.
OPEN_REPLACEMENT_STATES = frozenset((
    "Scheduled", "Submitted", "Downloading", "Paused", "Downloaded", "Inspecting", "Verified", "Quarantining",
    "Quarantined", "Restoring", "Deleting", "NeedsReview"))

# AuditAction members MAM-24..27 write for replacements (27..55, stored by name), and the rows around them that the
# live check also reads. Any later action whose name starts with "Replacement" counts as one too.
REPLACEMENT_ACTIONS = (
    "ReplacementSwitchedOn", "ReplacementSwitchedOff", "ReplacementFoldersChanged", "ReplacementScheduled",
    "ReplacementWorkConfirmed", "ReplacementCancelled", "ReplacementCheckStarted", "ReplacementVerified",
    "ReplacementNeedsReview", "ReplacementFailed", "ReplacementQuarantineStarted", "ReplacementNameMoving",
    "ReplacementNameMoved", "ReplacementNameNotMoved", "ReplacementQuarantined", "ReplacementRetried",
    "ReplacementRestoreStarted", "ReplacementNameRestoring", "ReplacementNameRestored", "ReplacementNameNotRestored",
    "ReplacementRestored", "ReplacementDeleteStarted", "ReplacementNameDeleting", "ReplacementNameDeleted",
    "ReplacementNameNotDeleted", "ReplacementCompleted", "ReplacementCheckRetried", "ReplacementCancelledAlone",
    "ReplacementCopyDropped",
)
RELATED_ACTIONS = ("AllowedRootsChanged", "DownloadRequested", "DownloadCancelled", "DestinationCreated",
                   "DestinationUpdated", "DestinationDeleted", "IntegrationUpdated")

# One ssh call, run by `sh` on the media host, sent base64-encoded so no quoting of the Windows command line can change
# it. sh reads this very script from stdin, so the body is one function called with stdin from /dev/null. Its stdout
# is the tar stream and nothing else; every fact goes into a file of the tar. The database copy is mam22's: it counts
# as consistent when the size and full-resolution mtime of mam.db and mam.db-wal are the same before and after the cp.
REMOTE_SCRIPT = r"""
container=@CONTAINER@
asked_dir=@COMPOSE_DIR@
fallback_dir=@DEFAULT_COMPOSE_DIR@
media_uid=@MEDIA_UID@
main() {
  section() { printf '@@ %s\n' "$1"; }
  mounts=$(docker inspect --type container -f '{{range .Mounts}}{{.Destination}}|{{.Source}}{{println}}{{end}}' "$container") || { echo "docker inspect $container failed (is the container name right and docker running?)" >&2; exit 11; }
  src=$(printf '%s\n' "$mounts" | sed -n 's#^/data|##p' | head -n 1)
  [ -n "$src" ] || { echo "container $container has no mount at /data" >&2; exit 12; }
  [ -f "$src/mam.db" ] || { echo "no mam.db in $src" >&2; exit 13; }
  media=$(printf '%s\n' "$mounts" | grep -v '^/data|' | grep '|')
  tmp=$(mktemp -d) || { echo "mktemp failed" >&2; exit 14; }
  trap 'rm -rf "$tmp"' EXIT
  trap 'exit 130' HUP INT TERM
  snap() { for f in mam.db mam.db-wal; do [ -e "$src/$f" ] && stat -c '%n %s %y' "$src/$f"; done; true; }
  n=0
  while :; do
    n=$((n + 1))
    rm -f "$tmp/mam.db" "$tmp/mam.db-wal" "$tmp/mam.db-shm"
    before=$(snap)
    cp "$src/mam.db" "$tmp/mam.db" || { echo "cp of mam.db failed" >&2; exit 15; }
    [ -f "$src/mam.db-wal" ] && cp "$src/mam.db-wal" "$tmp/mam.db-wal"
    [ -f "$src/mam.db-shm" ] && cp "$src/mam.db-shm" "$tmp/mam.db-shm"
    after=$(snap)
    [ "$before" = "$after" ] && break
    if [ "$n" -ge 3 ]; then echo "mam.db changed while it was being copied (3 tries); the copy may be inconsistent" >&2; break; fi
    sleep 1
  done
  image=$(docker inspect --type container -f '{{.Image}}' "$container")
  pid=$(docker inspect --type container -f '{{.State.Pid}}' "$container")
  # The compose project: --compose-dir, else the folder compose recorded on the container, else the known one.
  label_dir=$(docker inspect --type container -f '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$container" 2>/dev/null)
  label_files=$(docker inspect --type container -f '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "$container" 2>/dev/null)
  case "$label_dir" in "<no value>") label_dir=;; esac
  case "$label_files" in "<no value>") label_files=;; esac
  compose_dir=$asked_dir
  [ -n "$compose_dir" ] || compose_dir=$label_dir
  [ -n "$compose_dir" ] || compose_dir=$fallback_dir
  [ -z "$asked_dir" ] || label_files=
  {
    section read; date -u +%Y-%m-%dT%H:%M:%SZ
    section host; uname -sr; hostname
    section docker; docker version -f '{{.Server.Version}}'
    section container
    docker inspect --type container -f 'name={{.Name}}{{println}}image={{.Config.Image}}{{println}}image_id={{.Image}}{{println}}created={{.Created}}{{println}}status={{.State.Status}}{{println}}started_at={{.State.StartedAt}}{{println}}finished_at={{.State.FinishedAt}}{{println}}restart_count={{.RestartCount}}{{println}}user={{.Config.User}}{{println}}health={{if .State.Health}}{{.State.Health.Status}}{{end}}{{println}}pid={{.State.Pid}}{{println}}compose_dir={{index .Config.Labels "com.docker.compose.project.working_dir"}}{{println}}compose_files={{index .Config.Labels "com.docker.compose.project.config_files"}}' "$container"
    section image
    docker image inspect -f 'id={{.Id}}{{println}}created={{.Created}}' "$image"
    section tags; docker image inspect -f '{{range .RepoTags}}{{.}}{{println}}{{end}}' "$image"
    section digests; docker image inspect -f '{{range .RepoDigests}}{{.}}{{println}}{{end}}' "$image"
    section mounts
    docker inspect --type container -f '{{range .Mounts}}{{.Type}}|{{.Source}}|{{.Destination}}|{{.RW}}|{{.Mode}}|{{.Propagation}}|{{.Name}}{{println}}{{end}}' "$container"
    section process
    if [ "${pid:-0}" -gt 0 ] 2>/dev/null; then grep -E '^(Name|Uid|Gid|Groups|Umask):' "/proc/$pid/status"; else echo "not running"; fi
    section exec_id; docker exec "$container" id
    section data_host
    for f in "$src" "$src/mam.db" "$src/mam.db-wal" "$src/mam.db-shm"; do [ -e "$f" ] && stat -c '%n|%u|%g|%U|%G|%a|%F|%s|%y' "$f"; done
    section data_container
    docker exec "$container" stat -c '%n|%u|%g|%U|%G|%a|%F|%s|%y' /data /data/mam.db
    section version
    docker cp "$container:/app/MediaAcquisitionManager.Api.deps.json" - | tar -xOf - | grep -o '"MediaAcquisitionManager.Api/[^"]*"' | head -n 1
    section informational_version
    docker cp "$container:/app/MediaAcquisitionManager.Api.dll" - | tar -xOf - | grep -aoE '[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?\+[0-9a-f]{7,40}' | head -n 5
    section mountinfo; docker exec "$container" cat /proc/self/mountinfo
    section writable
    printf '%s\n' "$media" | while IFS='|' read -r dest msrc; do
      [ -n "$dest" ] || continue
      docker exec "$container" sh -c 'for d in "$1" "$1"/*/ "$1"/.mam-quarantine/; do [ -d "$d" ] || continue; [ "$d" = / ] || d=${d%/}; if [ -w "$d" ]; then echo "yes|$d"; else echo "no|$d"; fi; done' sh "$dest" </dev/null
    done
    section df
    printf '%s\n' "$media" | while IFS='|' read -r dest msrc; do
      [ -n "$dest" ] && [ -d "$msrc" ] || continue
      echo "Q $msrc"
      df -B1 --output=source,fstype,size,used,avail,target "$msrc" </dev/null | tail -n +2
      awk -v s="$msrc/" 'index($5, s) == 1 { print $5 }' /proc/self/mountinfo </dev/null | while read -r t; do
        t=$(printf '%b' "$t")
        echo "Q $t"
        df -B1 --output=source,fstype,size,used,avail,target "$t" </dev/null | tail -n +2
      done
    done
    # The runbook's pre-flight (section 0.1), read in this same call. Tagged lines: F a file read, L a line of it,
    # N a note, E the exit code of the read, Q the mount a block is about, T/R findmnt pairs, J a Jellyfin container.
    section compose
    echo "compose_dir=$compose_dir"
    echo "label_dir=$label_dir"
    echo "label_files=$label_files"
    section compose_version; docker compose version </dev/null
    section compose_file
    files=
    [ -z "$label_files" ] || files=$(printf '%s\n' "$label_files" | tr ',' '\n')
    if [ -z "$files" ]; then
      for f in docker-compose.yml docker-compose.yaml compose.yaml compose.yml; do
        if [ -f "$compose_dir/$f" ]; then files="$compose_dir/$f"; break; fi
      done
    fi
    [ -n "$files" ] || echo "N no compose file in $compose_dir"
    printf '%s\n' "$files" | while IFS= read -r f; do
      [ -n "$f" ] || continue
      echo "F $f"
      if [ ! -e "$f" ]; then
        echo "N no such file $f"
      elif [ -r "$f" ]; then
        # Only the lines about the image, the user, restarts, read-only flags and /media; a line that names a secret
        # (an environment entry such as MAM_Integrations__Key: /media/...) is left out even then.
        awk '{ sub(/\r$/, "") } /^[ \t-]*(image|user|restart|read_only):|\/media/ { if (tolower($0) ~ /secret|pass|token|key|jwt|cred|auth|salt|private|encrypt/) print "L " NR ":(left out: the line names a secret)"; else print "L " NR ":" $0 }' "$f" </dev/null
        echo "E $?"
      else
        echo "N cannot read $f"
      fi
    done
    section env_app
    # Only the lines that start with APP_ (the compose variables): every other line of .env may hold a secret. A
    # quoted value may run over several lines, and none of its further lines is read out, whatever it starts with;
    # an APP_ line that names a secret in its name or its value has its value left out.
    envf="$compose_dir/.env"
    echo "F $envf"
    if [ ! -d "$compose_dir" ]; then
      echo "N no folder $compose_dir"
    elif [ ! -e "$envf" ]; then
      echo "N no .env"
    elif [ -r "$envf" ]; then
      awk '{ sub(/\r$/, "") } inq { if (index($0, quote)) inq = 0; next } match($0, /^[A-Za-z_][A-Za-z0-9_.]*[ \t]*=[ \t]*["\047]/) { quote = substr($0, RLENGTH, 1); if (!index(substr($0, RLENGTH + 1), quote)) inq = 1 } /^APP_/ { name = $0; sub(/[ \t]*=.*/, "", name); if (toupper($0) ~ /SECRET|PASS|TOKEN|KEY|JWT|CRED|AUTH|SALT|PRIVATE|ENCRYPT/) print "L " name "=(value left out: the line names a secret)"; else print "L " $0 }' "$envf" </dev/null
      echo "E $?"
    else
      echo "N cannot read $envf"
    fi
    section updaters
    if running=$(docker ps --format '{{.Names}}|{{.Image}}' </dev/null); then
      printf '%s\n' "$running" | grep -iE 'watchtower|diun|ouroboros|wud' | sed 's/^/L /'
      printf '%s\n' "$running" | grep -i 'jellyfin' | sed 's/^/J /'
      echo "E 0"
    else
      echo "N docker ps failed"
    fi
    section findmnt
    printf '%s\n' "$media" | while IFS='|' read -r dest msrc; do
      [ -n "$dest" ] || continue
      echo "Q $dest|$msrc"
      if ! command -v findmnt >/dev/null 2>&1; then echo "N findmnt is not installed on the host"; continue; fi
      fsroot=$(findmnt -n -o TARGET -T "$msrc" </dev/null)
      [ -n "$fsroot" ] || { echo "N findmnt -T found no mount holding $msrc"; continue; }
      findmnt -P -o TARGET,SOURCE,FSTYPE,OPTIONS -T "$msrc" </dev/null | sed 's/^/T /'
      findmnt -R -P -o TARGET,SOURCE,FSTYPE,OPTIONS "$fsroot" </dev/null | sed 's/^/R /'
    done
    section rootowned
    printf '%s\n' "$media" | while IFS='|' read -r dest msrc; do
      [ -n "$dest" ] && [ -d "$msrc" ] || continue
      case "$src/" in "$msrc"/*) echo "N skipped $dest: it holds the data volume"; continue;; esac
      echo "Q $dest|$msrc"
      find "$msrc" -xdev -type d \( ! -uid "$media_uid" -o ! -perm -u=wx \) -printf 'L %U|%G|%u|%g|%m|%p\n' </dev/null
      echo "E $?"
    done
    section chown; docker exec "$container" sh -c 'command -v chown || echo "(no chown)"'
    section ffprobe; docker exec "$container" sh -c 'ffprobe -version 2>&1 | head -n 1'
    section arch; uname -m
    section backup_room
    du -sb --exclude=.secrets "$src" </dev/null
    df -B1 --output=source,fstype,size,used,avail,target /root /var/lib/docker </dev/null | tail -n +2
    section end
  } > "$tmp/facts.txt" 2>&1
  : > "$tmp/inventory.txt"
  : > "$tmp/inventory-errors.txt"
  i=0
  printf '%s\n' "$media" | while IFS='|' read -r dest msrc; do
    [ -n "$dest" ] && [ -d "$msrc" ] || continue
    case "$src/" in "$msrc"/*) echo "the inventory skips $dest: it holds the data volume" >> "$tmp/inventory-errors.txt"; continue;; esac
    i=$((i + 1))
    printf '%s|%s|%s\n' "$i" "$dest" "$msrc" >> "$tmp/inventory.txt"
    find -H "$msrc" -path "$src" -prune -o -printf '%y %s %T@ %i %n %D %U %G %m %p\0' > "$tmp/inventory-$i.bin" 2>> "$tmp/inventory-errors.txt" </dev/null
  done
  cd "$tmp" && tar -czf - -- *
}
main </dev/null
"""

REMOTE_FAILURES = {
    11: "docker could not inspect the MAM container (check the container name and that docker is running)",
    12: "the MAM container has no mount at /data",
    13: "there is no mam.db in the container's /data mount",
    14: "mktemp failed on the media host",
    15: "copying mam.db failed on the media host",
    124: "ssh did not finish in time and was stopped",
    255: "ssh itself failed (a wrong password, an unreachable host or a refused key)",
}


def say(text: str = "") -> None:
    print(text, flush=True)


# ---------------------------------------------------------------- the copy

def is_copy_member(name: str) -> bool:
    return name in (*DATABASE_FILES, FACTS_FILE, INVENTORY_INDEX, INVENTORY_ERRORS) or bool(INVENTORY_PART.fullmatch(name))


def remove_copy(folder: Path) -> list[str]:
    """Deletes the copy's files (never anything else) and the folder if that leaves it empty. The copy holds password
    hashes and sealed keys, so a file that can't be deleted is named, never passed over."""
    left = []
    if not folder.is_dir():
        return left
    for path in folder.iterdir():
        if not path.is_file() or not is_copy_member(path.name):
            continue
        for _attempt in range(3):
            try:
                path.unlink(missing_ok=True)
                break
            except OSError:
                time.sleep(0.3)  # a virus scanner may hold a freshly written file for a moment
        else:
            left.append(path.name)
    try:
        folder.rmdir()
    except OSError:
        pass
    if left:
        say(f"WARNING: could not delete {', '.join(left)} in {folder}. It holds password hashes and sealed keys: "
            "delete it by hand.")
    return left


def unpack(data: bytes, folder: Path) -> None:
    """Writes the tar stream's files into `folder`. The archive comes from another machine, so every member must be
    one of the expected names, a plain file, and land directly in `folder`."""
    try:
        archive = tarfile.open(fileobj=io.BytesIO(data), mode="r:*")
    except tarfile.TarError:
        raise ToolError("The media host's answer is not a tar stream. Something in its login scripts may print to "
                        "stdout; the first bytes were: " + repr(data[:80])) from None
    with archive:
        try:
            members = archive.getmembers()
        except (tarfile.TarError, EOFError, OSError):
            raise ToolError("The tar stream from the media host is cut short; run the script again.") from None
        wanted = {}
        for member in members:
            name = member.name[2:] if member.name.startswith("./") else member.name
            if not is_copy_member(name) or not member.isreg():
                raise ToolError(f"Refusing the unexpected tar member {member.name!r}.")
            wanted[name] = member
        if "mam.db" not in wanted:
            raise ToolError("The tar stream from the media host holds no mam.db.")
        folder.mkdir(parents=True, exist_ok=True)
        root = folder.resolve()
        for name, member in wanted.items():
            target = (folder / name).resolve()
            if target.parent != root:
                raise ToolError(f"Refusing to write {name!r} outside {folder}.")
            try:
                handle = archive.extractfile(member)
                target.write_bytes(handle.read() if handle else b"")
            except (tarfile.TarError, EOFError, OSError):
                raise ToolError("The tar stream from the media host is cut short; run the script again.") from None


def fetch(ssh_host: str, container: str, folder: Path, compose_dir: str | None = None) -> None:
    say(f"Copying the MAM database and reading the container, the compose project, the media mounts and their files "
        f"on {ssh_host} (container {container}; read-only)...")
    script = (REMOTE_SCRIPT.replace("@CONTAINER@", shlex.quote(container))
              .replace("@COMPOSE_DIR@", shlex.quote(compose_dir or ""))
              .replace("@DEFAULT_COMPOSE_DIR@", shlex.quote(DEFAULT_COMPOSE_DIR))
              .replace("@MEDIA_UID@", shlex.quote(MEDIA_OWNER_UID)))
    encoded = base64.b64encode(script.encode("utf-8")).decode("ascii")
    code, out, _ = common.ssh_run(ssh_host, f"echo {encoded} | base64 -d | sh", timeout=900)
    if code != 0:
        raise ToolError(f"The read failed (ssh exit code {code}): " + REMOTE_FAILURES.get(code, "see the lines above") + ".")
    if remove_copy(folder):  # a stale -wal beside a new mam.db would be replayed over it
        raise ToolError(f"The previous copy in {folder} could not be cleared; delete that folder and run again.")
    unpack(out, folder)


def copy_local(source: Path, folder: Path) -> None:
    """--db: a database file on this PC, copied with its -wal and -shm, never opened in place (opening a WAL database
    can create files beside it). No container, mount or file facts then."""
    if not source.is_file():
        raise ToolError(f"--db {source}: no such file.")
    if remove_copy(folder):
        raise ToolError(f"The previous copy in {folder} could not be cleared; delete that folder and run again.")
    folder.mkdir(parents=True, exist_ok=True)
    for name in DATABASE_FILES:
        side = source if name == "mam.db" else source.with_name(source.name + name[len("mam.db"):])
        if side.is_file():
            shutil.copyfile(side, folder / name)


class Database:
    """The copy, opened read-only; select() refuses anything but a SELECT or a read-only pragma. rows() selects the
    named columns a table has and notes the ones it lacks, so a database older than 0.3.0 reads as one without
    replacements instead of failing."""

    def __init__(self, path: Path) -> None:
        # mode=ro, never immutable=1: immutable ignores the -wal, where the newest rows may still be.
        try:
            self.connection = sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=10)
            self.connection.row_factory = sqlite3.Row
            self.connection.execute("PRAGMA query_only = ON")
        except sqlite3.Error as error:
            raise ToolError(f"The database copy cannot be opened: {error}. Run the script again.") from None
        self.tables = {row["name"] for row in self.select("SELECT name FROM sqlite_master WHERE type = 'table'")}
        self.missing: dict[str, list[str]] = {}
        self._columns: dict[str, set[str]] = {}

    def select(self, sql: str, params=()) -> list[dict]:
        head = " ".join(sql.split()).lower()
        if not head.startswith(("select ", "pragma quick_check")):
            raise ToolError(f"Refusing a statement that is not a read: {sql[:40]!r}")
        try:
            return [dict(row) for row in self.connection.execute(sql, params)]
        except sqlite3.Error as error:
            raise ToolError(f"Reading the database copy failed: {error}. If the copy was taken while MAM was writing, "
                            "run the script again.") from None

    def has(self, *tables: str) -> bool:
        return all(table in self.tables for table in tables)

    def columns(self, table: str) -> set[str]:
        if table not in self._columns:
            self._columns[table] = ({row["name"] for row in self.select("SELECT name FROM pragma_table_info(?)", (table,))}
                                    if table in self.tables else set())
        return self._columns[table]

    def rows(self, table: str, wanted: list[str], where: str | None = None, values=None) -> list[dict] | None:
        """The `wanted` columns of `table` (None when there is no such table), all rows or those whose `where` column
        is one of `values`. A wanted column the table lacks comes back as None and is noted in self.missing."""
        if table not in self.tables:
            return None
        present = self.columns(table)
        chosen = [c for c in wanted if c in present]
        absent = [c for c in wanted if c not in present]
        if absent:
            self.missing[table] = sorted(set(self.missing.get(table, [])) | set(absent))
        if not chosen:
            return []
        names = ", ".join(f'"{c}"' for c in chosen)
        if where is None:
            result = self.select(f'SELECT {names} FROM "{table}"')
        else:
            if where not in present:
                return []
            values = [v for v in dict.fromkeys(values or []) if v is not None]
            result = []
            for start in range(0, len(values), 400):
                chunk = values[start:start + 400]
                marks = ", ".join("?" for _ in chunk)
                result += self.select(f'SELECT {names} FROM "{table}" WHERE "{where}" IN ({marks})', chunk)
        for row in result:
            for column in absent:
                row[column] = None
        return result

    def close(self) -> None:
        self.connection.close()


# ---------------------------------------------------------------- the host's facts

def first(lines: list[str]) -> str | None:
    for line in lines:
        if line.strip():
            return line.strip()
    return None


def key_values(lines: list[str]) -> dict:
    found = {}
    for line in lines:
        if "=" in line:
            key, value = line.split("=", 1)
            found[key.strip()] = value.strip()
    return found


def stat_line(line: str) -> dict:
    """'%n|%u|%g|%U|%G|%a|%F|%s|%y', or the error stat printed instead."""
    parts = line.rsplit("|", 8)
    if len(parts) != 9:
        return {"raw": line}
    return {"path": parts[0], "uid": parts[1], "gid": parts[2], "user": parts[3], "group": parts[4], "mode": parts[5],
            "type": parts[6], "size": parts[7], "modified": parts[8]}


def parse_facts(text: str) -> dict:
    sections: dict[str, list[str]] = defaultdict(list)
    current = None
    for line in text.splitlines():
        if line.startswith("@@ "):
            current = line[3:].strip()
            sections[current]  # noqa: B018 - an empty section still exists
            continue
        if current:
            sections[current].append(line)
    # Reading a section below creates it in the defaultdict, so what the host actually sent is noted first.
    facts: dict = {"sections_sent": sorted(sections)}
    facts.update({"read_utc": first(sections["read"]), "host": [l for l in sections["host"] if l.strip()],
                  "docker": first(sections["docker"])})
    facts["container"] = key_values(sections["container"])
    facts["image"] = key_values(sections["image"])
    facts["tags"] = [l.strip() for l in sections["tags"] if l.strip()]
    facts["digests"] = [l.strip() for l in sections["digests"] if l.strip()]
    facts["mounts"] = []
    for line in sections["mounts"]:
        bits = line.split("|")
        if len(bits) == 7:
            facts["mounts"].append({"type": bits[0], "source": bits[1], "destination": bits[2],
                                    "read_write": bits[3] == "true", "mode": bits[4], "propagation": bits[5],
                                    "name": bits[6]})
    # docker inspect lists .Mounts in no fixed order; sorted, two reports of the same container compare equal.
    facts["mounts"].sort(key=lambda m: m["destination"])
    process = {}
    for line in sections["process"]:
        if ":" in line:
            key, value = line.split(":", 1)
            process[key.strip()] = " ".join(value.split())
    facts["process"] = process or {"State": "not running (no main process)"}
    facts["exec_id"] = first(sections["exec_id"])
    facts["data_host"] = [stat_line(l) for l in sections["data_host"] if l.strip()]
    facts["data_container"] = [stat_line(l) for l in sections["data_container"] if l.strip()]
    version_line = " ".join(sections["version"])
    match = re.search(r'"MediaAcquisitionManager\.Api/([^"]+)"', version_line)
    facts["app_version"] = match.group(1) if match else None
    # The dll may carry other version+sha strings too; the app's own one starts with the version deps.json gives.
    stamped = [line.strip() for line in sections["informational_version"]
               if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?\+[0-9a-f]{7,40}", line.strip())]
    own = [s for s in stamped if facts["app_version"] and s.startswith(facts["app_version"].split("+")[0] + "+")]
    facts["informational_version"] = (own or stamped or [None])[0]
    facts["mountinfo"] = sections["mountinfo"]
    writable = {}
    for line in sections["writable"]:
        if line.startswith(("yes|", "no|")):
            answer, path = line.split("|", 1)
            writable[path] = answer == "yes"  # a mount inside another one is asked about twice
    facts["writable"] = [{"path": path, "writable": answer} for path, answer in sorted(writable.items())]
    facts["df"] = []
    query = None
    for line in sections["df"]:
        if line.startswith("Q "):
            query = line[2:]
            continue
        bits = line.split(None, 5)
        if len(bits) == 6 and all(bit.isdigit() for bit in bits[2:5]):  # df writes "-" for a size it doesn't know
            facts["df"].append({"query": query, "source": bits[0], "fstype": bits[1], "size": int(bits[2]),
                                "used": int(bits[3]), "avail": int(bits[4]), "target": bits[5]})
    facts["preflight"] = parse_preflight(sections)
    facts["complete"] = "end" in sections
    facts["sections"] = {name: lines for name, lines in sections.items()}  # everything, errors included
    return facts


def tagged(lines: list[str]) -> list[tuple[str, str]]:
    """The lines of a pre-flight block as (tag, rest); a line without a tag (an error a command printed) is ("", line)."""
    found = []
    for line in lines:
        line = line.rstrip("\r")
        if len(line) >= 2 and line[1] == " " and line[0] in "EFJLNQRT":
            found.append((line[0], line[2:]))
        elif line.strip():
            found.append(("", line.strip()))
    return found


def exit_code(text: str):
    text = text.strip()
    return int(text) if text.isdigit() else text


def findmnt_pairs(text: str) -> dict:
    """One line of `findmnt -P`: KEY="value" pairs, where findmnt writes an unsafe byte as \\xHH."""
    pairs = {}
    for key, value in re.findall(r'([A-Z_%-]+)="((?:[^"\\]|\\.)*)"', text):
        raw = bytearray()
        for literal, hex_byte in re.findall(r"([^\\]+|\\(?!x[0-9a-fA-F]{2}).)|\\x([0-9a-fA-F]{2})", value):
            raw += bytes([int(hex_byte, 16)]) if hex_byte else literal.encode("utf-8")
        pairs[key] = raw.decode("utf-8", "replace")
    return pairs


def parse_preflight(sections: dict) -> dict:
    """The runbook's pre-flight blocks of facts.txt. A block the remote command never reached is None, which the checks
    print as unknown; so is a read that failed."""
    def block(name: str) -> list[tuple[str, str]] | None:
        return tagged(sections[name]) if name in sections else None

    result: dict = {}
    compose = key_values(sections["compose"]) if "compose" in sections else {}
    result["compose_dir"] = compose.get("compose_dir") or None
    result["compose_label_dir"] = compose.get("label_dir") or None
    result["compose_version"] = first(sections["compose_version"]) if "compose_version" in sections else None

    lines = block("compose_file")
    files, notes = [], []
    for tag, rest in lines or []:
        if tag == "F":
            files.append({"file": rest, "lines": [], "notes": [], "exit": None})
        elif tag == "L" and files:
            number, _, text = rest.partition(":")
            files[-1]["lines"].append({"line": int(number) if number.isdigit() else None, "text": text})
        elif tag == "E" and files:
            files[-1]["exit"] = exit_code(rest)
        else:
            (files[-1]["notes"] if files else notes).append(rest)
    result["compose_files"] = None if lines is None else files
    result["compose_notes"] = notes

    lines = block("env_app")
    env = None
    if lines is not None:
        env = {"file": None, "lines": [], "notes": [], "exit": None}
        for tag, rest in lines:
            if tag == "F":
                env["file"] = rest
            elif tag == "L":
                env["lines"].append(rest)
            elif tag == "E":
                env["exit"] = exit_code(rest)
            else:
                env["notes"].append(rest)
        env["state"] = ("read" if env["exit"] == 0 else "absent" if "no .env" in env["notes"] else "unreadable")
    result["env"] = env

    lines = block("updaters")
    updaters = None
    if lines is not None:
        updaters = {"read": False, "found": [], "jellyfin": [], "notes": []}
        for tag, rest in lines:
            if tag == "L":
                updaters["found"].append(rest.replace("|", " "))
            elif tag == "J":
                updaters["jellyfin"].append(rest.replace("|", " "))
            elif tag == "E":
                updaters["read"] = exit_code(rest) == 0
            else:
                updaters["notes"].append(rest)
    result["updaters"] = updaters

    lines = block("findmnt")
    mounts = None
    if lines is not None:
        mounts = []
        for tag, rest in lines:
            if tag == "Q":
                destination, _, source = rest.partition("|")
                mounts.append({"destination": destination, "source": source, "file_system": None, "tree": [],
                               "notes": []})
            elif not mounts:
                continue
            elif tag == "T":
                mounts[-1]["file_system"] = findmnt_pairs(rest)
            elif tag == "R":
                mounts[-1]["tree"].append(findmnt_pairs(rest))
            else:
                mounts[-1]["notes"].append(rest)
    result["findmnt"] = mounts

    lines = block("rootowned")
    owned = None
    if lines is not None:
        owned = {"mounts": [], "notes": []}
        for tag, rest in lines:
            if tag == "Q":
                destination, _, source = rest.partition("|")
                owned["mounts"].append({"destination": destination, "source": source, "folders": [], "exit": None,
                                        "notes": []})
            elif tag == "L" and owned["mounts"]:
                bits = rest.split("|", 5)
                if len(bits) == 6:
                    m = owned["mounts"][-1]
                    m["folders"].append({"uid": bits[0], "gid": bits[1], "user": bits[2], "group": bits[3],
                                         "mode": bits[4], "path": bits[5],
                                         "app_path": to_container(bits[5], [m]) or bits[5]})
            elif tag == "E" and owned["mounts"]:
                owned["mounts"][-1]["exit"] = exit_code(rest)
            else:
                (owned["mounts"][-1]["notes"] if owned["mounts"] else owned["notes"]).append(rest)
    result["root_owned"] = owned

    for name in ("chown", "ffprobe", "arch"):
        result[name] = first(sections[name]) if name in sections else None

    room = None
    if "backup_room" in sections:
        room = {"data_bytes": None, "df": [], "notes": []}
        for line in sections["backup_room"]:
            head = line.split(None, 1)
            bits = line.split(None, 5)
            if head and head[0].isdigit() and room["data_bytes"] is None:
                room["data_bytes"] = int(head[0])
            elif len(bits) == 6 and all(bit.isdigit() for bit in bits[2:5]):
                room["df"].append({"source": bits[0], "fstype": bits[1], "size": int(bits[2]), "used": int(bits[3]),
                                   "avail": int(bits[4]), "target": bits[5]})
            elif line.strip():
                room["notes"].append(line.strip())
        if len(room["df"]) == 2:  # df answers in the order it was asked, unless one of the two failed
            for row, asked in zip(room["df"], ("/root", "/var/lib/docker")):
                row["query"] = asked
    result["backup_room"] = room
    return result


def mount_lines(mountinfo: list[str]) -> list[dict]:
    """The lines of the mount table as MountTable reads them: the type after the lone '-' that ends the optional
    fields; a space in a path is written \\040."""
    lines = []
    for line in mountinfo:
        fields = line.split()
        if len(fields) <= 6:
            continue
        try:
            separator = fields.index("-", 6)
        except ValueError:
            continue
        if separator + 1 >= len(fields):
            continue
        lines.append({"device": fields[2], "root": fields[3].replace("\\040", " "),
                      "mount_point": fields[4].replace("\\040", " "), "options": fields[5],
                      "optional": fields[6:separator], "type": fields[separator + 1],
                      "source": fields[separator + 2] if separator + 2 < len(fields) else ""})
    return lines


def under(path: str, folder: str) -> bool:
    folder = folder.rstrip("/") or "/"
    return path == folder or path.startswith(folder if folder == "/" else folder + "/")


def media_mounts(facts: dict | None) -> list[dict]:
    return [m for m in (facts or {}).get("mounts", []) if m["destination"] != "/data"]


def to_container(host_path: str, mounts: list[dict]) -> str | None:
    best = None
    for m in mounts:
        if under(host_path, m["source"]) and (best is None or len(m["source"]) > len(best["source"])):
            best = m
    if best is None:
        return None
    rest = host_path[len(best["source"].rstrip("/")):]
    return best["destination"].rstrip("/") + rest if rest else best["destination"]


def to_host(container_path: str, mounts: list[dict]) -> str | None:
    best = None
    for m in mounts:
        if under(container_path, m["destination"]) and (best is None or len(m["destination"]) > len(best["destination"])):
            best = m
    if best is None:
        return None
    rest = container_path[len(best["destination"].rstrip("/")):]
    return best["source"].rstrip("/") + rest if rest else best["source"]


def space_of(container_path: str, facts: dict | None) -> dict | None:
    """The df row of the file system holding `container_path`: the queried host path that is its longest prefix."""
    if not facts:
        return None
    host = to_host(container_path, media_mounts(facts))
    if host is None:
        return None
    best = None
    for row in facts.get("df", []):
        if row["query"] and under(host, row["query"]) and (best is None or len(row["query"]) > len(best["query"])):
            best = row
    return best


# ---------------------------------------------------------------- the file inventory

def parse_inventory(folder: Path) -> dict | None:
    """The find records of each media mount, keyed by the path the app sees (the mount's destination in place of its
    host source)."""
    index_path = folder / INVENTORY_INDEX
    if not index_path.is_file():
        return None
    entries: dict[str, dict] = {}
    parts = []
    index = [bits for bits in (line.split("|", 2) for line in
                                index_path.read_text(encoding="utf-8", errors="replace").splitlines()) if len(bits) == 3]
    # A mount inside another one shows its root twice: as the outer mount's empty mount point folder on the host and as
    # its own root. The deeper mount is what the app sees, so it is read last and wins.
    index.sort(key=lambda bits: (len(bits[1].rstrip("/")), bits[1]))
    for number, destination, source in index:
        data_path = folder / f"inventory-{number}.bin"
        data = data_path.read_bytes() if data_path.is_file() else b""
        part = {"destination": destination, "source": source, "entries": 0, "files": 0, "file_bytes": 0, "unreadable": 0}
        mapping = [{"source": source, "destination": destination}]
        for raw in data.split(b"\0"):
            if not raw:
                continue
            fields = raw.decode("utf-8", "backslashreplace").split(" ", 9)
            if len(fields) != 10 or not fields[1].isdigit():
                part["unreadable"] += 1
                continue
            kind, size, mtime, inode, nlink, dev, owner, group, mode, host_path = fields
            path = to_container(host_path, mapping) or host_path
            entry = {"type": kind, "size": int(size), "mtime": mtime, "inode": int(inode) if inode.isdigit() else inode,
                     "nlink": int(nlink) if nlink.isdigit() else nlink, "dev": int(dev) if dev.isdigit() else dev,
                     "uid": owner, "gid": group, "mode": mode}
            entries[path] = entry
            part["entries"] += 1
            if kind == "f":
                part["files"] += 1
                part["file_bytes"] += entry["size"]
        parts.append(part)
    errors_path = folder / INVENTORY_ERRORS
    errors = errors_path.read_text(encoding="utf-8", errors="replace").splitlines() if errors_path.is_file() else []
    return {"mounts": parts, "errors": [e for e in errors if e.strip()], "entries": entries}


def mtime_text(value: str | None) -> str:
    if not value:
        return "—"
    try:
        return dt.datetime.fromtimestamp(float(value), tz=dt.timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    except (ValueError, OverflowError, OSError):
        return value


def is_quarantine_path(path: str) -> bool:
    return QUARANTINE in path.split("/")


def quarantines(inventory: dict | None, roots: list[str]) -> dict:
    """For each root, every entry under <root>/.mam-quarantine; and any quarantine folder found elsewhere."""
    result: dict = {"roots": [], "elsewhere": []}
    if inventory is None:
        return result
    entries = inventory["entries"]
    prefixes = []
    for root in roots:
        folder = root.rstrip("/") + "/" + QUARANTINE if root != "/" else "/" + QUARANTINE
        prefixes.append(folder)
        files = []
        dirs = []
        for path, e in sorted(entries.items()):
            if not under(path, folder):
                continue
            item = {"path": path, **e}
            (files if e["type"] == "f" else dirs).append(item)
        result["roots"].append({"root": root, "folder": folder, "exists": folder in entries,
                                "folder_entry": entries.get(folder), "files": files, "dirs": dirs,
                                "total_bytes": sum(f["size"] for f in files)})
    for path, e in sorted(entries.items()):
        if path.split("/")[-1] == QUARANTINE and path not in prefixes:
            result["elsewhere"].append({"path": path, **e})
    return result


def folder_summary(inventory: dict | None, folders: list[str]) -> list[dict]:
    if inventory is None:
        return []
    summary = []
    for folder in folders:
        files = [e for p, e in inventory["entries"].items() if e["type"] == "f" and under(p, folder)
                 and not is_quarantine_path(p)]
        summary.append({"folder": folder, "inventoried": any(under(folder, m["destination"]) for m in inventory["mounts"]),
                        "files": len(files), "bytes": sum(e["size"] for e in files),
                        "files_with_links_above_one": sum(1 for e in files if isinstance(e["nlink"], int) and e["nlink"] > 1)})
    return summary


def on_disk(inventory: dict | None, path: str | None, inode) -> str:
    """Where the inventory finds `path`, compared with the inode the database recorded."""
    if inventory is None or not path:
        return "not inventoried"
    if not any(under(path, m["destination"]) for m in inventory["mounts"]):
        return "outside the inventoried mounts"
    entry = inventory["entries"].get(path)
    if entry is None:
        return "absent"
    if inode is None:
        return f"present (inode {entry['inode']}, links {entry['nlink']})"
    if entry["inode"] == inode:
        return f"present, same inode (links {entry['nlink']})"
    return f"present, OTHER inode {entry['inode']} (recorded {inode})"


# ---------------------------------------------------------------- the database

def gid(value) -> str | None:
    return str(value).upper() if value is not None else None


def by_position(rows: list[dict]) -> list[dict]:
    return sorted(rows, key=lambda r: (r.get("Position") is None, r.get("Position") or 0))


def read_database(db: Database, previous_audit_ids: set[str] | None) -> dict:
    report: dict = {"quick_check": [str(row[next(iter(row))]) for row in db.select("PRAGMA quick_check")]}
    report["migrations"] = [row["MigrationId"] for row in
                            db.select('SELECT MigrationId FROM "__EFMigrationsHistory" ORDER BY MigrationId')]
    report["has_replacements"] = db.has("ReplacementJobs")

    connections = {gid(r["Id"]): r for r in db.rows("IntegrationConnections", ["Id", "Name", "AdapterType", "Kind",
                                                                              "IsEnabled"]) or []}
    report["integrations"] = sorted(connections.values(), key=lambda r: str(r.get("Name")))
    report["path_mappings"] = [{"integration": connections.get(gid(r["IntegrationConnectionId"]), {}).get("Name"),
                                "service_prefix": r["ServicePrefix"], "app_prefix": r["AppPrefix"]}
                               for r in by_position(db.rows("PathMappings", ["IntegrationConnectionId", "Position",
                                                                             "ServicePrefix", "AppPrefix"]) or [])]
    report["allowed_roots"] = [r["Path"] for r in by_position(db.rows("AllowedRoots", ["Path", "Position"]) or [])]

    settings = db.rows("ReplacementSettings", ["Id", "IsEnabled"])
    if settings is None:
        report["switch"] = None  # no such table: a database older than 0.3.0
    else:
        report["switch"] = "on" if any(r["IsEnabled"] for r in settings) else ("off" if settings else "off (never saved)")
    folders = db.rows("ReplacementFolders", ["Path", "Position"])
    report["ticked_folders"] = None if folders is None else [r["Path"] for r in by_position(folders)]

    locations = {gid(r["Id"]): r for r in db.rows("LibraryLocations", ["Id", "Name", "SourceConnectionId",
                                                                      "SourceName"]) or []}
    report["library_folders"] = sorted(
        ({"library": locations.get(gid(r["LibraryLocationId"]), {}).get("Name"),
          "integration": locations.get(gid(r["LibraryLocationId"]), {}).get("SourceName"),
          "service_path": r["ServicePath"]}
         for r in db.rows("LibraryLocationFolders", ["LibraryLocationId", "Position", "ServicePath"]) or []),
        key=lambda r: (str(r["service_path"])))
    report["destinations"] = [
        {"name": r["Name"], "client": connections.get(gid(r["DownloadClientConnectionId"]), {}).get("Name"),
         "category": r["ClientDestinationKey"], "default": bool(r["IsDefault"])}
        for r in sorted(db.rows("DestinationMappings", ["Name", "DownloadClientConnectionId", "ClientDestinationKey",
                                                        "IsDefault"]) or [], key=lambda r: str(r["Name"]))]
    owners = {gid(r["OwnerUserId"]) for r in db.rows("Installations", ["OwnerUserId"]) or []}
    report["users"] = [{"name": r["UserName"], "owner": gid(r["Id"]) in owners, "disabled": bool(r["IsDisabled"])}
                       for r in sorted(db.rows("Users", ["Id", "UserName", "IsDisabled"]) or [],
                                       key=lambda r: str(r["UserName"]))]

    # Download jobs, every one: a refusal before sending (the overwrite guard) is a download job with no replacement.
    jobs = db.rows("DownloadJobs", ["Id", "ReleaseId", "RequestedByUserName", "Status", "StatusChangedUtc", "CreatedUtc",
                                    "DownloadClientName", "DestinationName", "ClientDestinationKey", "ClientJobId",
                                    "InfoHashV1", "Progress", "Error", "ErrorOrigin", "BlockReason",
                                    "BlockReasonOrigin", "OverwriteCheck", "PayloadKind"]) or []
    releases = {gid(r["Id"]): r for r in db.rows("Releases", ["Id", "Title", "SizeBytes", "SourceName", "Kind"],
                                                  "Id", [j["ReleaseId"] for j in jobs]) or []}
    for job in jobs:
        release = releases.get(gid(job["ReleaseId"]), {})
        job["ReleaseTitle"] = release.get("Title")
        job["ReleaseSizeBytes"] = release.get("SizeBytes")
        job["ReleaseSource"] = release.get("SourceName")
    jobs.sort(key=lambda r: str(r.get("CreatedUtc")))
    downloads = {gid(j["Id"]): j for j in jobs}

    # Replacement jobs and everything under them.
    replacements = db.rows("ReplacementJobs", ["Id", "DownloadJobId", "RequestedByUserName", "State", "StateChangedUtc",
                                               "Reason", "ReleaseEditions", "CreatedUtc", "InspectionStartedUtc",
                                               "LibraryAskedUtc", "LibraryNoticeDueUtc", "QuarantineCheckedUtc",
                                               "QuarantinedUtc"]) or []
    ids = [r["Id"] for r in replacements]
    children: dict[str, dict[str, list]] = defaultdict(lambda: defaultdict(list))

    def gather(table: str, columns: list[str], kind: str) -> list[dict]:
        found = db.rows(table, columns, "ReplacementJobId", ids) or []
        for row in found:
            children[gid(row["ReplacementJobId"])][kind].append(row)
        return found

    works = gather("ReplacementJobWorks", ["Id", "ReplacementJobId", "MediaWorkId", "Kind", "Title", "Year", "Evidence",
                                           "ConfirmedByOwner", "ConfirmationText"], "works")
    work_ids = defaultdict(list)
    for row in db.rows("ReplacementJobWorkIdentifiers", ["ReplacementJobWorkId", "Scheme", "Value", "SetByOwner"],
                       "ReplacementJobWorkId", [w["Id"] for w in works]) or []:
        work_ids[gid(row["ReplacementJobWorkId"])].append(row)
    for work in works:
        work["Identifiers"] = work_ids.get(gid(work["Id"]), [])
    copies = gather("ReplacementJobFiles", ["Id", "ReplacementJobId", "Position", "PhysicalFileId", "MediaVersionId",
                                            "MediaWorkId", "MatchedWorkId", "SeasonNumber", "EpisodeNumber",
                                            "EpisodeNumberEnd", "ServicePath", "AppPath", "DeviceId", "Inode",
                                            "SizeBytes", "ModifiedUtc", "LinkCount", "EditionLabel",
                                            "DurationMilliseconds", "PairedNewFileId", "Problem", "DroppedUtc",
                                            "OnlyDropSettles"], "copies")
    new_files = gather("ReplacementJobNewFiles", ["Id", "ReplacementJobId", "Position", "RelativePath",
                                                  "ClientSizeBytes", "ClientProgress", "AppPath", "LibraryIntegrationId",
                                                  "LibraryPath", "DeviceId", "Inode", "SizeBytes", "ModifiedUtc",
                                                  "DurationMilliseconds", "VideoCodec", "Width", "Height", "CheckedUtc",
                                                  "LibraryItemId", "Kind", "Refreshed", "SeriesItemId", "SeasonNumber",
                                                  "EpisodeNumber", "EpisodeNumberEnd", "ReadUtc", "MediaWorkId",
                                                  "Problem"], "new_files")
    new_ids = defaultdict(list)
    for row in db.rows("ReplacementJobNewFileIdentifiers", ["ReplacementJobNewFileId", "Scheme", "Value", "OfSeries"],
                       "ReplacementJobNewFileId", [f["Id"] for f in new_files]) or []:
        new_ids[gid(row["ReplacementJobNewFileId"])].append(row)
    for new_file in new_files:
        new_file["Identifiers"] = new_ids.get(gid(new_file["Id"]), [])
    names = gather("ReplacementJobNames", ["Id", "ReplacementJobId", "Position", "ReplacementJobFileId", "Kind",
                                           "AppPath", "QuarantinePath", "AllowedRoot", "DeviceId", "Inode", "SizeBytes",
                                           "ModifiedUtc", "LinkCount", "State", "Problem", "MovedUtc", "RestoredUtc",
                                           "DeletedUtc"], "names")
    new_by_id = {gid(f["Id"]): f for f in new_files}
    for copy in copies:
        paired = new_by_id.get(gid(copy["PairedNewFileId"]))
        copy["PairedNewFile"] = (paired.get("AppPath") or paired.get("RelativePath")) if paired else None

    for job in replacements:
        mine = children[gid(job["Id"])]
        download = downloads.get(gid(job["DownloadJobId"]), {})
        job["Download"] = download
        job["Works"] = mine["works"]
        job["Copies"] = by_position(mine["copies"])
        job["NewFiles"] = by_position(mine["new_files"])
        job["Names"] = by_position(mine["names"])
        download["ReplacementJobId"] = job["Id"]
    replacements.sort(key=lambda r: str(r.get("CreatedUtc")))
    report["replacement_jobs"] = replacements
    report["download_jobs"] = jobs
    report["download_files"] = {
        gid(j_id): by_position(rows) for j_id, rows in _group(
            db.rows("DownloadJobFiles", ["DownloadJobId", "Position", "RelativePath", "SizeBytes", "Progress",
                                         "IsWanted"], "DownloadJobId", [r["DownloadJobId"] for r in replacements]) or [],
            "DownloadJobId").items()}

    # What MAM's library sync last recorded at the paths of the copies and of the new files.
    paths = [c["ServicePath"] for c in copies] + [f["LibraryPath"] for f in new_files]
    report["library_rows"] = sorted(
        db.rows("PhysicalFiles", ["ServicePath", "SourceKind", "SourceName", "SizeBytes", "ReadSizeBytes", "DeviceId",
                                  "Inode", "LinkCount", "InspectionStatus", "MediaVersionId", "ObservedUtc"],
                "ServicePath", paths) or [], key=lambda r: (str(r["ServicePath"]), str(r["SourceKind"])))

    # The audit trail.
    targets: dict[str, str] = {}
    for job in replacements:
        targets[gid(job["Id"])] = f"replacement {short(job['Id'])} ({job['State']} now)"
    for name in names:
        targets[gid(name["Id"])] = f"name {name['AppPath']} ({name['State']} now)"
    for copy in copies:
        targets[gid(copy["Id"])] = f"copy {copy['AppPath']}"
    for job in jobs:
        targets.setdefault(gid(job["Id"]), f"download {short(job['Id'])} ({job['Status']} now)")

    audit_columns = ["Id", "Action", "ActorUserName", "TargetKind", "TargetId", "TargetName", "CreatedUtc"]
    index = db.rows("AuditEvents", ["Id", "Action", "CreatedUtc"]) or []
    report["audit_ids"] = sorted(gid(r["Id"]) for r in index)
    report["audit_by_action"] = dict(sorted(Counter(r["Action"] for r in index).items()))
    trail_ids = [r["Id"] for r in index if r["Action"] in REPLACEMENT_ACTIONS or r["Action"] in RELATED_ACTIONS
                 or str(r["Action"]).startswith("Replacement")]
    latest_ids = [r["Id"] for r in sorted(index, key=lambda r: str(r["CreatedUtc"]), reverse=True)[:20]]
    new_ids_since = None if previous_audit_ids is None else [r["Id"] for r in index if gid(r["Id"]) not in previous_audit_ids]
    wanted = list(dict.fromkeys(trail_ids + latest_ids + (new_ids_since or [])))
    rows = {gid(r["Id"]): r for r in db.rows("AuditEvents", audit_columns, "Id", wanted) or []}
    identifiers = _group(db.rows("AuditEventIdentifiers", ["AuditEventId", "Position", "Role", "Scheme", "Value"],
                                 "AuditEventId", wanted) or [], "AuditEventId")
    for key, row in rows.items():
        row["Identifiers"] = by_position(identifiers.get(key, []))
        row["Target"] = targets.get(gid(row["TargetId"]))

    def ordered(ids_: list) -> list[dict]:
        return sorted((rows[gid(i)] for i in ids_ if gid(i) in rows), key=lambda r: (str(r["CreatedUtc"]), str(r["Id"])))

    report["audit_trail"] = ordered(trail_ids)
    report["audit_latest"] = list(reversed(ordered(latest_ids)))
    report["audit_new"] = None if new_ids_since is None else ordered(new_ids_since)
    report["columns_missing"] = db.missing
    return report


def _group(rows: list[dict], column: str) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = defaultdict(list)
    for row in rows:
        grouped[gid(row[column])].append(row)
    return grouped


def short(value) -> str:
    return str(value or "")[:8].lower()


# ---------------------------------------------------------------- the summary

def summarize(facts: dict | None, inventory: dict | None, database: dict) -> dict:
    roots = database["allowed_roots"] or [m["destination"] for m in media_mounts(facts)]
    summary: dict = {"allowed_roots": database["allowed_roots"], "switch": database["switch"],
                     "ticked_folders": database["ticked_folders"]}
    if facts:
        c = facts["container"]
        summary.update({
            "app_version": facts.get("app_version"), "informational_version": facts.get("informational_version"),
            "image": c.get("image"), "image_id": c.get("image_id"), "digests": facts.get("digests"),
            "status": c.get("status"), "health": c.get("health"), "started_at": c.get("started_at"),
            "config_user": c.get("user") or "(image default: 10001)", "exec_id": facts.get("exec_id"),
            "process_uid": facts["process"].get("Uid"), "process_gid": facts["process"].get("Gid"),
            "process_umask": facts["process"].get("Umask"),
            "data_owner": [f"{d.get('path')}: {d.get('uid')}:{d.get('gid')} mode {d.get('mode')}" if "path" in d
                           else d.get("raw") for d in facts["data_host"]],
            "media_mounts": [{"destination": m["destination"], "source": m["source"], "type": m["type"],
                              "read_write": m["read_write"], "propagation": m["propagation"]}
                             for m in media_mounts(facts)],
        })
        lines = mount_lines(facts.get("mountinfo", []))
        layout = []
        for m in media_mounts(facts):
            at = [l for l in lines if l["mount_point"] == m["destination"]]
            inside = [l for l in lines if l["mount_point"] != m["destination"] and under(l["mount_point"], m["destination"])]
            layout.append({"destination": m["destination"], "mounts_at_it": len(at),
                           "file_systems": [f"{l['type']} {l['source']} ({l['device']}, {l['options']})" for l in at],
                           "mounted_inside": [f"{l['mount_point']}: {l['type']} {l['source']} ({l['device']})"
                                              for l in inside]})
        summary["mount_layout"] = layout
        summary["writable"] = facts.get("writable", [])
        spaces = []
        for root in roots:
            row = space_of(root, facts)
            unknown = ("not under a media mount" if to_host(root, media_mounts(facts)) is None
                       else "df gave nothing for it on the host")
            spaces.append({"root": root, **({k: row[k] for k in ("source", "fstype", "size", "used", "avail", "target")}
                                            if row else {"target": None, "why_unknown": unknown})})
        summary["free_space"] = spaces
    q = quarantines(inventory, roots)
    summary["quarantine"] = {"files": sum(len(r["files"]) for r in q["roots"]),
                             "bytes": sum(r["total_bytes"] for r in q["roots"]),
                             "elsewhere": [e["path"] for e in q["elsewhere"]]}
    summary["replacement_jobs_by_state"] = dict(Counter(j["State"] for j in database["replacement_jobs"]))
    summary["held"] = held_lines(database)
    return summary


def held_lines(database: dict) -> list[str]:
    """Every "Still held by the torrent(s) …" stop of a video, and every "Held by the torrent …" of a companion that
    stayed while its video moved (ReplacementQuarantiner.Reasons)."""
    found = []
    for job in database["replacement_jobs"]:
        for text in [job.get("Reason")] + [c.get("Problem") for c in job["Copies"]] + [n.get("Problem") for n in job["Names"]]:
            line = f"{short(job['Id'])}: {text}"
            if text and HELD in str(text).lower() and line not in found:
                found.append(line)
    return found


# ---------------------------------------------------------------- the pre-flight

def verdict_row(name: str, verdict: str, value: str, expected: str = "", details: list[str] | None = None) -> dict:
    return {"check": name, "verdict": verdict, "value": value, "expected": expected, "details": details or []}


def unknown(why: str | None = None) -> str:
    return f"unknown ({why})" if why else "unknown (not read)"


def gib(value) -> str:
    try:
        return f"{int(value) / 1024 ** 3:,.2f} GiB"
    except (TypeError, ValueError):
        return "—"


def preflight_checks(label: str, facts: dict | None, inventory: dict | None) -> list[dict]:
    """Every read of the runbook's pre-flight (section 0.1) with OK, CHECK (look at it, and on "before" stop and report
    back), UNKNOWN (it could not be read) or INFO (nothing to judge at this step). The settings step 2 changes (the
    compose file's user: line, the APP_ ids, the data owner, the app's ids, the quarantine) are judged on "before"
    only; at a later step they read INFO, and the comparison shows how they changed."""
    if not facts:
        return []
    strict = label == STRICT_LABEL
    pf = facts.get("preflight") or {}
    c = facts.get("container") or {}
    rows: list[dict] = []
    add = rows.append

    line = pf.get("compose_version")
    match = re.search(r"\bv?(\d+)\.(\d+)(?:\.\d+)?", line or "")
    if not match:
        add(verdict_row("docker compose", "UNKNOWN", unknown(line), "2.24 or newer"))
    else:
        add(verdict_row("docker compose", "OK" if (int(match[1]), int(match[2])) >= COMPOSE_AT_LEAST else "CHECK", line,
                        "2.24 or newer (the compose file's env_file `required: false` needs it)"))

    image = c.get("image")
    add(verdict_row("image", "UNKNOWN" if not image else "OK" if image in PUBLISHED_IMAGES else "CHECK",
                    image or unknown(), " or ".join(PUBLISHED_IMAGES[:2])))
    digests = facts.get("digests") or []
    sent = "digests" in (facts.get("sections_sent") or [])
    add(verdict_row("registry digest", "OK" if any(PUBLISHED_DIGEST in d for d in digests) else "CHECK" if sent
                    else "UNKNOWN", "; ".join(digests) or ("none (an image loaded by hand has no registry digest)"
                                                           if sent else unknown()),
                    f"vahac/media-acquisition-manager@{PUBLISHED_DIGEST}… (Docker Hub's 0.3.0)"))
    stamped = facts.get("informational_version")
    if not stamped:
        add(verdict_row("stamped version", "UNKNOWN", unknown(f"deps.json says {facts['app_version']}"
                                                              if facts.get("app_version") else None),
                        f"{PUBLISHED_VERSION}+{PUBLISHED_REVISION}"))
    else:
        version, _, sha = stamped.partition("+")
        add(verdict_row("stamped version", "OK" if version == PUBLISHED_VERSION and sha.startswith(PUBLISHED_REVISION)
                        else "CHECK", f"{stamped} (deps.json {text_or_dash(facts.get('app_version'))})",
                        f"{PUBLISHED_VERSION}+{PUBLISHED_REVISION}; another sha: write it down, the docs then name "
                        "that commit"))
    status, health, started = c.get("status"), c.get("health"), c.get("started_at") or ""
    if not status:
        add(verdict_row("state", "UNKNOWN", unknown(), f"running, healthy, started {PUBLISHED_SINCE} or later"))
    else:
        add(verdict_row("state", "OK" if status == "running" and health == "healthy" and started >= PUBLISHED_SINCE
                        else "CHECK", f"{status}, health {text_or_dash(health)}, started {text_or_dash(started)}",
                        f"running, healthy, started {PUBLISHED_SINCE} or later"))
    chown = pf.get("chown")
    add(verdict_row("chown in the image", "OK" if chown and chown.startswith("/")
                    else "CHECK" if chown == "(no chown)" else "UNKNOWN",
                    chown if chown and chown.startswith("/") else unknown(chown) if chown != "(no chown)" else chown,
                    "/usr/bin/chown (step 2c runs it)"))

    exec_id = facts.get("exec_id") or ""
    uid = re.search(r"\buid=(\d+)", exec_id)
    value = f"Config.User {c.get('user') or '(empty: the image default)'}; id {exec_id or '—'}"
    if not uid:
        add(verdict_row("the app's ids", "UNKNOWN", unknown(exec_id or "docker exec id gave nothing"),
                        f"uid={IMAGE_UID}(mam) gid={IMAGE_UID}(mam)"))
    else:
        ok = uid[1] == IMAGE_UID and (c.get("user") or "") in ("", "mam", IMAGE_UID, f"{IMAGE_UID}:{IMAGE_UID}")
        add(verdict_row("the app's ids", ("OK" if ok else "CHECK") if strict else "INFO", value,
                        f"uid={IMAGE_UID}(mam) gid={IMAGE_UID}(mam), Config.User empty or mam"))

    updaters = pf.get("updaters")
    if not updaters or not updaters["read"]:
        add(verdict_row("auto-updater", "UNKNOWN", unknown("; ".join((updaters or {}).get("notes") or []) or None),
                        "none running"))
    else:
        add(verdict_row("auto-updater", "CHECK" if updaters["found"] else "OK",
                        "; ".join(updaters["found"]) or "none running (watchtower, diun, ouroboros, wud)",
                        "none: one could swap the image in the middle of MAM-28"))

    mounts = facts.get("mounts") or []
    data = [m for m in mounts if m["destination"] == "/data"]
    media = media_mounts(facts)
    if not mounts:
        add(verdict_row("container mounts", "UNKNOWN", unknown(), "/data volume rw; one /media bind mount, rw"))
    else:
        ok = (len(data) == 1 and data[0]["type"] == "volume" and data[0]["read_write"] and len(media) == 1
              and media[0]["destination"] == "/media" and media[0]["type"] == "bind" and media[0]["read_write"])
        add(verdict_row("container mounts", "OK" if ok else "CHECK", "; ".join(
            f"{m['destination']} <- {m['source']} ({m['type']}, {'rw' if m['read_write'] else 'READ-ONLY'})"
            for m in mounts), "/data <- the mam-data volume (rw) and one /media bind mount, rw, no :ro"))

    table = mount_lines(facts.get("mountinfo") or [])
    if not table or not media:
        add(verdict_row("mount table at /media", "UNKNOWN",
                        unknown("the container's /proc/self/mountinfo gave nothing" if media else "no media mount"),
                        "exactly one mount at /media, options rw,…; nothing mounted inside it"))
    else:
        values, ok = [], True
        for m in media:
            at = [l for l in table if l["mount_point"] == m["destination"]]
            inside = [l for l in table if l["mount_point"] != m["destination"] and under(l["mount_point"], m["destination"])]
            ok = ok and len(at) == 1 and at[0]["options"].split(",")[0] == "rw" and not inside
            values.append(f"{m['destination']}: {len(at)} mount(s) ("
                          + "; ".join(f"{l['type']} {l['source']} {l['options']}" for l in at) + "); inside it: "
                          + ("; ".join(f"{l['mount_point']} ({l['type']} {l['source']})" for l in inside) or "nothing"))
        add(verdict_row("mount table at /media", "OK" if ok else "CHECK", " | ".join(values),
                        "exactly one mount at /media, options rw,…; nothing mounted inside it (a nested mount makes "
                        "a move fail with \"another mount\")"))

    found = pf.get("findmnt")
    if not found:
        add(verdict_row("findmnt -T (host)", "UNKNOWN", unknown(),
                        "the file system holding the media mount's source, mounted rw"))
        add(verdict_row("findmnt -R (host)", "UNKNOWN", unknown(), "nothing mounted below the media mount's source"))
    for entry in found or []:
        fs = entry["file_system"]
        if not fs:
            add(verdict_row(f"findmnt -T {entry['source']}", "UNKNOWN", unknown("; ".join(entry["notes"]) or None),
                            "the file system holding it, mounted rw"))
            continue
        add(verdict_row(f"findmnt -T {entry['source']}", "OK" if fs.get("OPTIONS", "").split(",")[0] == "rw" else "CHECK",
                        f"{fs.get('TARGET')} = {fs.get('SOURCE')} {fs.get('FSTYPE')} ({fs.get('OPTIONS')})",
                        "the file system holding it, mounted rw"))
        nested = [t for t in entry["tree"] if t.get("TARGET") and t.get("TARGET") != fs.get("TARGET")
                  and under(t["TARGET"], entry["source"])]
        add(verdict_row(f"findmnt -R {fs.get('TARGET')}", ("OK" if not nested else "CHECK") if entry["tree"] else "UNKNOWN",
                        f"{len(entry['tree'])} mount(s); below {entry['source']}: "
                        + ("; ".join(f"{t.get('TARGET')} ({t.get('FSTYPE')} {t.get('SOURCE')})" for t in nested)
                           or "nothing"),
                        f"{fs.get('TARGET')} and nothing below {entry['source']}",
                        [f"{t.get('TARGET')} = {t.get('SOURCE')} {t.get('FSTYPE')} ({t.get('OPTIONS')})"
                         for t in entry["tree"]] + entry["notes"]))

    files = pf.get("compose_files")
    readable = [f for f in files or [] if f["exit"] == 0]
    where = pf.get("compose_dir") or DEFAULT_COMPOSE_DIR
    if not readable:
        why = "; ".join((pf.get("compose_notes") or []) + [n for f in files or [] for n in f["notes"]]) or None
        add(verdict_row("compose: the /media line", "UNKNOWN", unknown(why), "the media bind mount without :ro"))
        add(verdict_row("compose: user: line", "UNKNOWN", unknown(why), "none yet (step 2 adds it)"))
    else:
        lines = [l for f in readable for l in f["lines"]]
        active = [l for l in lines if not l["text"].lstrip().startswith("#")]
        shown = [f"{f['file']}:{l['line']}: {l['text'].strip()}" for f in readable for l in f["lines"]]
        # /media as a path of its own, not the image's name vahac/media-acquisition-manager.
        media_lines = [l for l in active if re.search(r"(?<![\w.-])/media(?![\w.-])", l["text"])]
        read_only = [l for l in media_lines if re.search(r":/media\S*?:\S*\bro\b", l["text"])]
        read_only += [l for l in active if re.match(r"\s*-?\s*read_only\s*:\s*['\"]?true", l["text"], re.IGNORECASE)]
        add(verdict_row("compose: the /media line", "CHECK" if read_only or not media_lines else "OK",
                        "; ".join(l["text"].strip() for l in media_lines) or "no /media line found",
                        "the media bind mount, without :ro or read_only", shown))
        users = [l for l in active if re.match(r"\s*user\s*:", l["text"])]
        add(verdict_row("compose: user: line", ("CHECK" if users else "OK") if strict else "INFO",
                        "; ".join(l["text"].strip() for l in users) or "none",
                        "none yet: a user: line means someone has started step 2 (stop and report back)"))
        restarts = [l["text"].strip() for l in active if re.match(r"\s*restart\s*:", l["text"])]
        add(verdict_row("compose: restart", "INFO", "; ".join(restarts) or "none", "", [f"read from {where}"]))

    env = pf.get("env")
    if not env or env["state"] == "unreadable":
        add(verdict_row(".env APP_ lines", "UNKNOWN", unknown("; ".join((env or {}).get("notes") or []) or None),
                        "no APP_TAG (latest), no APP_USER_ID/APP_GROUP_ID"))
    else:
        values = {}
        for entry in env["lines"]:
            name, _, value = entry.partition("=")
            values[name.strip()] = value.strip().strip("'\"")
        problems = [n for n in ("APP_USER_ID", "APP_GROUP_ID") if n in values]
        if values.get("APP_TAG") not in (None, "", "latest"):
            problems.append(f"APP_TAG={values['APP_TAG']}")
        add(verdict_row(".env APP_ lines", ("CHECK" if problems else "OK") if strict else "INFO",
                        "; ".join(env["lines"]) or ("no .env" if env["state"] == "absent" else "no APP_ line"),
                        "probably APP_BIND_ADDRESS=0.0.0.0; no APP_TAG (latest); no APP_USER_ID/APP_GROUP_ID "
                        "(only the APP_ lines are read: the rest of .env may hold secrets)"))

    data_rows = [d for d in facts.get("data_host") or [] if "path" in d]
    if not data_rows:
        add(verdict_row("data volume owner", "UNKNOWN", unknown(), f"{IMAGE_UID}:{IMAGE_UID}"))
    else:
        owners = {f"{d['uid']}:{d['gid']}" for d in data_rows}
        add(verdict_row("data volume owner", ("OK" if owners == {f"{IMAGE_UID}:{IMAGE_UID}"} else "CHECK") if strict
                        else "INFO", "; ".join(f"{d['path'].rsplit('/', 1)[-1] or d['path']} {d['uid']}:{d['gid']} "
                                                f"mode {d['mode']}" for d in data_rows),
                        f"the folder and mam.db* owned {IMAGE_UID}:{IMAGE_UID} (1000:100 already means step 2 has "
                        "started: stop and report back)"))

    room = pf.get("backup_room")
    if not room or room["data_bytes"] is None:
        add(verdict_row("room for the backup", "UNKNOWN", unknown("; ".join((room or {}).get("notes") or []) or None),
                        "more free at /root than twice the data volume"))
    else:
        at_root = next((r for r in room["df"] if r.get("query") == "/root"), None)
        add(verdict_row("room for the backup", "UNKNOWN" if at_root is None
                        else "OK" if at_root["avail"] >= 2 * room["data_bytes"] else "CHECK",
                        f"data volume {room['data_bytes']:,} B without .secrets; " + "; ".join(
                            f"{r.get('query') or r['target']}: {gib(r['avail'])} free on {r['target']}"
                            for r in room["df"]), "more free at /root than twice the data volume (B1's backup)"))

    owned = pf.get("root_owned")
    blocks = (owned or {}).get("mounts") or []
    if not blocks:
        add(verdict_row(f"folders uid {MEDIA_OWNER_UID} can't write in", "UNKNOWN",
                        unknown("; ".join((owned or {}).get("notes") or []) or None),
                        f"the {KNOWN_ROOT_OWNED} root-owned folders"))
    else:
        folders = [f for b in blocks for f in b["folders"]]
        by_root = [f for f in folders if f["uid"] == "0"]
        failed = [b for b in blocks if b["exit"] != 0]
        ok = len(by_root) == KNOWN_ROOT_OWNED and len(by_root) == len(folders) and not failed
        add(verdict_row(f"folders uid {MEDIA_OWNER_UID} can't write in", "OK" if ok else "CHECK",
                        f"{len(by_root)} owned by root, {len(folders) - len(by_root)} other(s)"
                        + ("; find stopped with " + ", ".join(f"{b['destination']}: exit {b['exit']}" for b in failed)
                           if failed else ""),
                        f"the {KNOWN_ROOT_OWNED} root-owned folders of 2026-10-08 and nothing else; no copy directly "
                        "inside one can be moved (runbook D)",
                        [f"{f['uid']}:{f['gid']} ({f['user']}:{f['group']}) mode {f['mode']} {f['app_path']}"
                         for f in folders] + [n for b in blocks for n in b["notes"]]))

    if inventory is None:
        add(verdict_row(QUARANTINE, "UNKNOWN", unknown("no file inventory"), "none yet"))
    else:
        found_q = sorted(p for p, e in inventory["entries"].items() if p.split("/")[-1] == QUARANTINE)
        add(verdict_row(QUARANTINE, ("CHECK" if found_q else "OK") if strict else "INFO",
                        ", ".join(found_q) or "none", "none yet: no step has moved anything"))

    host = facts.get("host") or []
    add(verdict_row("host", "INFO", f"{', '.join(host) or '—'}; arch {text_or_dash(pf.get('arch'))}; docker "
                                    f"{text_or_dash(facts.get('docker'))}; compose project "
                                    f"{pf.get('compose_dir') or 'not read'}"))
    add(verdict_row("ffprobe in the image", "INFO", text_or_dash(pf.get("ffprobe"))))
    jellyfin = (pf.get("updaters") or {}).get("jellyfin") or []
    add(verdict_row("Jellyfin container", "INFO", "; ".join(jellyfin) or "none found running by name"))
    return rows


# ---------------------------------------------------------------- the candidates

def clean_path(path: str | None) -> str | None:
    """A full POSIX path in one spelling: no doubled or trailing slash, no "." segment; None for a relative path or
    one with "..", which NormalizedPath would not take either."""
    if not path or not str(path).startswith("/"):
        return None
    parts = [p for p in str(path).split("/") if p not in ("", ".")]
    if ".." in parts:
        return None
    return "/" + "/".join(parts)


def parent_of(path: str) -> str:
    return path.rsplit("/", 1)[0] or "/"


def map_service_path(path: str | None, pairs: list[tuple[str, str]]) -> str | None:
    """PathMapper.Map: no pairs means the service sees the files where the app does; otherwise every pair that covers
    the path is applied and they must agree. None when no pair covers it or they disagree."""
    spelled = clean_path(path)
    if spelled is None or not pairs:
        return spelled
    results = []
    for service, app in pairs:
        service, app = clean_path(service), clean_path(app)
        if service is None or app is None or not under(spelled, service):
            continue
        rest = spelled if service == "/" else spelled[len(service):]
        result = clean_path(app + rest) if rest else app
        if result not in results:
            results.append(result)
    return results[0] if len(results) == 1 else None


def resolution_of(width, height) -> tuple[int, str]:
    """A class by width and height, so a 1920x800 scope film counts as 1080p, not below a 1280x720 one."""
    w, h = width or 0, height or 0
    if w >= 3200 or h >= 1800:
        return 4, "2160p"
    if w >= 1800 or h >= 1000:
        return 3, "1080p"
    if w >= 1200 or h >= 700:
        return 2, "720p"
    return (1, "SD") if w or h else (0, "unknown")


def nominal_height(width, height) -> int:
    """The height a 16:9 frame of the same width has, when that is more than the height measured: a 1920x800 scope film
    ranks with the 1920x1080 ones, not below them, and only a frame really narrower or lower ranks lower."""
    return max(height or 0, round((width or 0) * 9 / 16))


def extension_of(path: str) -> str:
    name = path.rsplit("/", 1)[-1]
    return name.rsplit(".", 1)[-1].lower() if "." in name else ""


def stem_of(path: str) -> str:
    name = path.rsplit("/", 1)[-1]
    return name.rsplit(".", 1)[0] if "." in name else name


def duration_text(milliseconds) -> str:
    if not milliseconds:
        return "—"
    minutes = round(int(milliseconds) / 60000)
    return f"{minutes // 60} h {minutes % 60:02d} min" if minutes >= 60 else f"{minutes} min"


def find_candidates(db: Database, database: dict, inventory: dict | None) -> dict:
    """Runbook section D, from the database copy and the file inventory: the films and the episodes whose copy can
    most likely be ticked for replacement, worst quality first. Everything the Download dialog checks from the disk
    later (still at its path, unchanged since inspected) and what only qBittorrent knows (which torrent holds it) is
    not decided here; the folder checks need the inventory and read "not checked" without it (--db)."""
    result: dict = {"media_owner_uid": MEDIA_OWNER_UID, "inventory": inventory is not None, "libraries": [],
                    "films": None, "episodes": None, "notes": []}
    if not db.has("PhysicalFiles", "MediaVersions", "MediaWorks"):
        result["notes"].append("the database has no library tables")
        return result
    locations = {gid(r["Id"]): r for r in db.rows("LibraryLocations", ["Id", "Name", "SourceConnectionId"]) or []}
    library_folders: dict[str, list[str]] = defaultdict(list)
    for r in by_position(db.rows("LibraryLocationFolders", ["LibraryLocationId", "Position", "ServicePath"]) or []):
        library_folders[gid(r["LibraryLocationId"])].append(r["ServicePath"])
    pairs: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for r in by_position(db.rows("PathMappings", ["IntegrationConnectionId", "Position", "ServicePrefix",
                                                  "AppPrefix"]) or []):
        pairs[gid(r["IntegrationConnectionId"])].append((r["ServicePrefix"], r["AppPrefix"]))
    roots = [p for p in (clean_path(r) for r in database.get("allowed_roots") or []) if p]
    if not roots:
        result["notes"].append("no allowed folder (Media folders) is saved, so no copy lies under one")
    files = db.rows("PhysicalFiles", ["Id", "MediaVersionId", "ServicePath", "LibraryLocationId", "SizeBytes",
                                      "DeviceId", "Inode", "LinkCount", "InspectionStatus", "MediaInspectionId",
                                      "ReadSizeBytes", "ReadModifiedUtc", "SourceKind", "SourceConnectionId"]) or []
    versions = {gid(r["Id"]): r for r in db.rows("MediaVersions", ["Id", "MediaWorkId", "EditionLabel",
                                                                    "SourceConnectionId", "SourceItemId"]) or []}
    kinds = {gid(r["Id"]): r["Kind"] for r in db.rows("MediaWorks", ["Id", "Kind"]) or []}
    ids: dict[str, dict[str, set]] = defaultdict(lambda: defaultdict(set))
    for r in db.rows("MediaWorkIdentifiers", ["MediaWorkId", "Scheme", "Value"]) or []:
        if r["Value"] is not None and str(r["Value"]).strip():
            ids[gid(r["MediaWorkId"])][str(r["Scheme"])].add(str(r["Value"]).strip())
    titles: dict[str, list[dict]] = defaultdict(list)
    for r in db.rows("MediaWorkTitles", ["MediaWorkId", "Kind", "Text", "Year", "SourceKind"]) or []:
        titles[gid(r["MediaWorkId"])].append(r)
    own_items_known = all("SourceItemId" in db.columns(t) for t in ("MediaVersions", "MediaEpisodePositions"))
    positions: dict[str, list[dict]] = defaultdict(list)
    for r in db.rows("MediaEpisodePositions", ["MediaWorkId", "SeriesWorkId", "SeasonNumber", "EpisodeNumber",
                                               "SourceKind", "SourceConnectionId", "SourceItemId"]) or []:
        positions[gid(r["MediaWorkId"])].append(r)

    def work_of(f: dict) -> str | None:
        version = versions.get(gid(f["MediaVersionId"]))
        return gid(version["MediaWorkId"]) if version else None

    def title_of(work: str | None) -> tuple[str | None, int | None]:
        rows = titles.get(work or "", [])
        best = min(rows, key=lambda r: (r["Kind"] != "Display", r["SourceKind"] != "MediaLibrary"), default=None)
        if best is None:
            return None, None
        return best["Text"], best["Year"] or next((r["Year"] for r in rows if r["Year"]), None)

    def ids_text(found: dict[str, set], schemes: tuple[str, ...]) -> str:
        return ", ".join(f"{s.lower()} {v}" for s in schemes for v in sorted(found.get(s, ()))) or "—"

    # Each name once: its work, its app path, the folder of its library that holds it.
    for f in files:
        f["work"] = work_of(f)
        f["kind"] = kinds.get(f["work"]) if f["work"] else None
        f["app"] = map_service_path(f["ServicePath"], [] if f["SourceConnectionId"] is None
                                    else pairs.get(gid(f["SourceConnectionId"]), []))
        f["identity"] = ((f["DeviceId"], f["Inode"]) if f["DeviceId"] is not None and f["Inode"] is not None
                         else ("path", clean_path(f["ServicePath"])))
    reported = [f for f in files if f["SourceKind"] == "MediaLibrary" and f["kind"] in ("Movie", "Episode")]

    # A library holds films, episodes or both; Jellyfin's own library type is not stored, so it is read off the works.
    held_kinds: dict[str, Counter] = defaultdict(Counter)
    for f in reported:
        if f["LibraryLocationId"]:
            held_kinds[gid(f["LibraryLocationId"])][f["kind"]] += 1

    def library_kind(library: str) -> str:
        count = held_kinds.get(library, Counter())
        if count["Movie"] and not count["Episode"]:
            return "films"
        if count["Episode"] and not count["Movie"]:
            return "episodes"
        return "films and episodes" if count["Movie"] else "nothing"

    for library, location in sorted(locations.items(), key=lambda item: str(item[1]["Name"])):
        count = held_kinds.get(library, Counter())
        result["libraries"].append({"name": location["Name"], "holds": library_kind(library),
                                    "films": count["Movie"], "episodes": count["Episode"],
                                    "folders": library_folders.get(library, [])})

    # Indexes over every name: which works a path or an identity belongs to, the copies of each work, the works that
    # share an id or an episode position, and the videos in each folder.
    works_at: dict[tuple, set] = defaultdict(set)
    copies_of: dict[str, list[dict]] = defaultdict(list)
    videos_in_db: dict[str, set] = defaultdict(set)
    for f in files:
        if f["work"]:
            works_at[("path", f["app"] or clean_path(f["ServicePath"]))].add(f["work"])
            works_at[f["identity"]].add(f["work"])
            copies_of[f["work"]].append(f)
        if f["app"] and extension_of(f["app"]) in VIDEO_EXTENSIONS:
            videos_in_db[parent_of(f["app"])].add(f["app"])
    works_with_id: dict[tuple, set] = defaultdict(set)
    for work, found in ids.items():
        for scheme, values in found.items():
            for value in values:
                works_with_id[(kinds.get(work), scheme, value)].add(work)
    episodes_at: dict[tuple, set] = defaultdict(set)
    for work, rows in positions.items():
        for r in rows:
            if r["SeriesWorkId"] and r["SeasonNumber"] is not None and r["EpisodeNumber"] is not None:
                episodes_at[(gid(r["SeriesWorkId"]), r["SeasonNumber"], r["EpisodeNumber"])].add(work)
    entries = inventory["entries"] if inventory else {}
    inventoried_roots = [m["destination"] for m in inventory["mounts"]] if inventory else []
    videos_on_disk: Counter = Counter()
    for path, e in entries.items():
        if e["type"] == "f" and extension_of(path) in VIDEO_EXTENSIONS and not is_quarantine_path(path):
            videos_on_disk[parent_of(path)] += 1

    # ReplacementCandidateReader's AlreadyScheduled: a job not yet ended holds its copies that weren't dropped, by path
    # and by the identity their inspection read.
    held_ids, held_paths, held_identities = set(), set(), set()
    for job in database.get("replacement_jobs") or []:
        if job.get("State") in OPEN_REPLACEMENT_STATES:
            for copy in job.get("Copies") or []:
                if not copy.get("DroppedUtc"):
                    held_ids.add(gid(copy.get("PhysicalFileId")))
                    held_paths.add(clean_path(copy.get("AppPath")))
                    if copy.get("DeviceId") is not None and copy.get("Inode") is not None:
                        held_identities.add((copy["DeviceId"], copy["Inode"]))

    inspection_ids = [f["MediaInspectionId"] for f in reported
                      if f["InspectionStatus"] == "Inspected" and f["MediaInspectionId"]]
    inspections = {gid(r["Id"]): r for r in db.rows("MediaInspections", ["Id", "DeviceId", "Inode", "SizeBytes",
                                                                          "DurationMilliseconds", "BitRate", "Error"],
                                                     "Id", inspection_ids) or []}
    tracks: dict[str, list[dict]] = defaultdict(list)
    for r in db.rows("MediaInspectionTracks", ["MediaInspectionId", "Position", "Kind", "Codec", "Profile", "Width",
                                               "Height", "DynamicRange"], "MediaInspectionId", inspection_ids) or []:
        if r["Kind"] == "Video":
            tracks[gid(r["MediaInspectionId"])].append(r)

    def video_of(inspection: str) -> dict | None:
        found = by_position(tracks.get(inspection, []))
        moving = [t for t in found if str(t.get("Codec") or "").lower() not in PICTURE_CODECS]
        return next((t for t in moving or found if t.get("Height") or t.get("Width")), None)

    def library_folder_of(f: dict) -> str | None:
        """The app path of the folder of the copy's library that holds it (the longest, should two)."""
        library = gid(f["LibraryLocationId"])
        covering = [p for p in (clean_path(s) for s in library_folders.get(library, []))
                    if p and under(clean_path(f["ServicePath"]) or "", p)]
        if not covering:
            return None
        location = locations.get(library) or {}
        connection = gid(location.get("SourceConnectionId") or f["SourceConnectionId"])
        return map_service_path(max(covering, key=len), pairs.get(connection, []) if connection else [])

    def folder_problem(app: str, library_folder: str, entry: dict) -> tuple[str, str] | None:
        """The first folder from the copy's own up to its library folder that uid 1000 doesn't own or can't write and
        search in: a rename needs both, and so does Restore later."""
        folder, chain = parent_of(app), []
        while True:
            chain.append(folder)
            if folder == library_folder or folder == "/" or not under(folder, library_folder):
                break
            folder = parent_of(folder)
        for folder in chain:
            e = entries.get(folder)
            if e is None or e["type"] != "d":
                return "a folder up to the library folder is not in the inventory", folder
            if e["uid"] != MEDIA_OWNER_UID:
                return (f"a folder up to the library folder isn't owned by uid {MEDIA_OWNER_UID}"
                        + (" (a root-owned folder)" if e["uid"] == "0" else ""), f"{folder} ({e['uid']}:{e['gid']})")
            try:
                mode = int(str(e["mode"]), 8)
            except ValueError:
                return "a folder up to the library folder has an unreadable mode", folder
            if mode & 0o300 != 0o300:
                return "a folder up to the library folder lacks owner write and search (u=wx)", f"{folder} ({e['mode']})"
        holder = entries.get(parent_of(app)) or {}
        try:
            sticky = int(str(holder.get("mode") or "0"), 8) & 0o1000
        except ValueError:
            sticky = 0
        if sticky and entry.get("uid") != MEDIA_OWNER_UID:
            return f"a sticky folder, and the file isn't uid {MEDIA_OWNER_UID}'s", app
        return None

    def evaluate(f: dict, as_episode: bool) -> tuple[str | None, str | None, dict | None]:
        """(why it is left out, which path says so, None) or (None, None, its row)."""
        library = gid(f["LibraryLocationId"]) if f["LibraryLocationId"] else None
        if library is None:
            return "in no library", f["ServicePath"], None
        wanted = "episodes" if as_episode else "films"
        if library_kind(library) != wanted:
            return f"its library holds {library_kind(library)}, not {wanted} only", f["ServicePath"], None
        # ReplacementCandidateReader's NotInspected: inspected, with the device, inode, size and time the read found.
        if (f["InspectionStatus"] != "Inspected" or not f["MediaInspectionId"]
                or any(f[c] is None for c in ("DeviceId", "Inode", "ReadSizeBytes", "ReadModifiedUtc"))):
            return "not inspected", f["ServicePath"], None
        inspection = inspections.get(gid(f["MediaInspectionId"]))
        if inspection is None or inspection.get("Error"):
            return "its inspection read no streams", f["ServicePath"], None
        video = video_of(gid(f["MediaInspectionId"]))
        if video is None:
            return "no video track measured", f["ServicePath"], None
        work = f["work"]
        own_ids = ids.get(work, {})
        series = None
        if as_episode:
            placed = {(gid(r["SeriesWorkId"]), r["SeasonNumber"], r["EpisodeNumber"]) for r in positions.get(work, [])
                      if r["SourceKind"] == "MediaLibrary"}
            if len(placed) != 1 or any(v is None for v in next(iter(placed))):
                return "not placed in one series, season and episode", f["ServicePath"], None
            # The place a copy keeps is the one its own library item gives (ReplacementCandidateReader.ListingsOf);
            # without it the dialog says OutsideTheClaim.
            version = versions.get(gid(f["MediaVersionId"])) or {}
            own = {(gid(r["SeriesWorkId"]), r["SeasonNumber"], r["EpisodeNumber"]) for r in positions.get(work, [])
                   if r["SourceKind"] == "MediaLibrary" and r["SourceItemId"] is not None
                   and gid(r["SourceConnectionId"]) == gid(version.get("SourceConnectionId"))
                   and r["SourceItemId"] == version.get("SourceItemId")}
            if own != placed and own_items_known:
                return "its own library item doesn't place it", f["ServicePath"], None
            series, season, episode = next(iter(placed))
            series_ids = ids.get(series, {})
            if not (series_ids.get("TmdbTv") or series_ids.get("Tmdb") or series_ids.get("Imdb")):
                return "its series has no TMDb or IMDb id", f["ServicePath"], None
            if any(len(values) > 1 for values in series_ids.values()):
                return "its series has conflicting ids", f["ServicePath"], None
            if any(other != series for scheme, values in series_ids.items() for value in values
                   for other in works_with_id.get(("Series", scheme, value), ())):
                return "a second series has the same ids", f["ServicePath"], None
        else:
            if not (own_ids.get("Tmdb") or own_ids.get("Imdb")):
                return "no TMDb or IMDb id", f["ServicePath"], None
            if len(own_ids.get("Tmdb", ())) > 1 or len(own_ids.get("Imdb", ())) > 1:
                return "conflicting ids (two TMDb or two IMDb)", f["ServicePath"], None
        path_text = f["app"] or f["ServicePath"]
        segments = [s.lower() for s in path_text.split("/")]
        if any(s in DISC_FOLDERS for s in segments) or extension_of(path_text) in ("iso", "img", "vob"):
            return "a disc folder clip or a disc image", path_text, None
        name = path_text.rsplit("/", 1)[-1]
        if as_episode and MULTI_EPISODE.search(name):
            return "several episodes in one file (S01E01-E02)", path_text, None
        if as_episode and HALF_EPISODE.search(name):
            return "an a/b half (a letter glued to the episode number)", path_text, None
        if (len(works_at[("path", f["app"] or clean_path(f["ServicePath"]))] | works_at[f["identity"]]) > 1):
            return "also a copy of another work", path_text, None
        if f["LinkCount"] != 1:
            return "not exactly one link", path_text, None
        if gid(f["Id"]) in held_ids or (f["app"] and f["app"] in held_paths) or f["identity"] in held_identities:
            return "held by an open replacement", path_text, None
        if f["app"] is None:
            return "no app path (the mappings don't cover it, or disagree)", f["ServicePath"], None
        if not any(under(f["app"], root) for root in roots):
            return "outside every allowed folder", f["app"], None
        library_folder = library_folder_of(f)
        if library_folder is None:
            return "no folder of its library holds it", f["app"], None
        notes = []
        entry = entries.get(f["app"])
        inventoried = any(under(f["app"], root) for root in inventoried_roots)
        if inventory is not None and inventoried:
            if entry is None:
                return "not on disk (absent from the inventory)", f["app"], None
            if entry["type"] != "f":
                return "not a regular file on disk", f["app"], None
            if isinstance(entry["nlink"], int) and entry["nlink"] != 1:
                return "more than one link on disk now", f["app"], None
            if entry["inode"] != f["Inode"] or entry["size"] != f["ReadSizeBytes"]:
                notes.append("changed since its inspection: B7's Refresh all reads it again")
        if not as_episode:
            # Only the chosen video and its same-name companions move, so other films beside it are no obstacle; one
            # part of a film split over several files is (the move would leave the other part behind).
            if FILM_PART.search(stem_of(f["app"])):
                return "one part of a film split over several files (a/b, CD1, part 1)", f["app"], None
            beside = (videos_on_disk[parent_of(f["app"])] if inventory is not None and inventoried
                      else len(videos_in_db[parent_of(f["app"])])) - 1
            if beside > 0:
                notes.append(f"{beside} other video(s) in its folder; only this one and its same-name companions move")
        if inventory is None or not inventoried:
            folders = "not checked (no file inventory)" if inventory is None else "not checked (not inventoried)"
        else:
            problem = folder_problem(f["app"], library_folder, entry or {})
            if problem:
                return problem[0], problem[1], None
            folders = f"every folder up to {library_folder} owned by uid {MEDIA_OWNER_UID}, u=wx"

        # The DB's other copies: other names of the work with another identity, and copies of the works that share its
        # id (a film) or its series, season and episode (an episode).
        related = {work}
        if as_episode:
            related |= episodes_at.get((series, season, episode), set())
        else:
            for scheme in ("Tmdb", "Imdb"):
                for value in own_ids.get(scheme, ()):
                    related |= works_with_id.get(("Movie", scheme, value), set())
        others = {}
        for other_work in related:
            for other in copies_of.get(other_work, []):
                if other["identity"] != f["identity"] and (other["app"] or other["ServicePath"]) != f["app"]:
                    others.setdefault(other["identity"], other["app"] or other["ServicePath"])

        size = inspection.get("SizeBytes") or f["SizeBytes"]
        duration = inspection.get("DurationMilliseconds")
        rate = (size / (duration / 60000) / 1e6 if size and duration
                else inspection["BitRate"] * 60 / 8 / 1e6 if inspection.get("BitRate") else None)
        rank, label = resolution_of(video.get("Width"), video.get("Height"))
        codec = str(video.get("Codec") or "").lower() or None
        title, year = title_of(series if as_episode else work)
        row = {"kind": "episode" if as_episode else "film", "title": title, "year": year,
               "ids": {s: sorted(v) for s, v in own_ids.items()},
               "ids_text": ids_text(own_ids, ("Tmdb", "Imdb", "Tvdb", "TmdbTv")),
               "app_path": f["app"], "service_path": f["ServicePath"],
               "library": (locations.get(library) or {}).get("Name"), "library_folder": library_folder,
               "edition": (versions.get(gid(f["MediaVersionId"])) or {}).get("EditionLabel"),
               "width": video.get("Width"), "height": video.get("Height"), "resolution": label,
               "codec": codec, "profile": video.get("Profile"), "dynamic_range": video.get("DynamicRange"),
               "size_bytes": size, "duration_ms": duration, "mb_per_minute": round(rate, 2) if rate else None,
               "container": extension_of(f["app"]) or None, "link_count": f["LinkCount"], "inode": f["Inode"],
               "folders": folders, "second_copy": {"count": len(others), "paths": sorted(others.values())[:5]},
               "notes": notes, "series_work": series,
               "rank": [rank, nominal_height(video.get("Width"), video.get("Height")), CODEC_AGE.get(codec or "", 2),
                        rate if rate is not None else float("inf")]}
        if as_episode:
            episode_title, _ = title_of(work)
            series_ids = ids.get(series, {})
            row.update({"season": season, "episode": episode, "episode_title": episode_title,
                        "series_ids": {s: sorted(v) for s, v in series_ids.items()},
                        "series_ids_text": ids_text(series_ids, ("TmdbTv", "Tmdb", "Imdb", "Tvdb"))})
        return None, None, row

    for kind, as_episode in (("films", False), ("episodes", True)):
        considered = [f for f in reported if f["kind"] == ("Episode" if as_episode else "Movie")]
        left_out: Counter = Counter()
        examples: dict[str, list[str]] = defaultdict(list)
        kept, seen = [], set()
        for f in considered:
            why, where, row = evaluate(f, as_episode)
            if why:
                left_out[why] += 1
                if len(examples[why]) < 5:
                    examples[why].append(str(where))
                continue
            if row["app_path"] in seen:  # one file the library reported twice for its work
                continue
            seen.add(row["app_path"])
            kept.append(row)
        kept.sort(key=lambda r: (r["rank"], str(r["app_path"])))
        top, per_series = [], Counter()
        for row in kept:
            if as_episode:
                if per_series[row["series_work"]] >= EPISODES_PER_SERIES:
                    continue
                per_series[row["series_work"]] += 1
            top.append(row)
            if len(top) == CANDIDATE_LIMIT:
                break
        for row in kept:
            row["rank"] = [None if isinstance(v, float) and v == float("inf") else v for v in row["rank"]]
        result[kind] = {"considered": len(considered), "kept": len(kept),
                        "left_out": dict(left_out.most_common()), "left_out_examples": dict(examples), "top": top}
    return result


# ---------------------------------------------------------------- the comparison

INVENTORY_FILE_FIELDS = ("type", "size", "mtime", "inode", "nlink", "dev", "uid", "gid", "mode")
INVENTORY_DIR_FIELDS = ("type", "inode", "dev", "uid", "gid", "mode")


def area_of(path: str, ticked: list[str]) -> str:
    if is_quarantine_path(path):
        return "quarantine"
    if any(under(path, t) for t in ticked):
        return "ticked"
    return "other"


def outside_quarantine(path: str) -> str:
    """The path a quarantined name came from: <root>/.mam-quarantine/<job>/<rest> -> <root>/<rest>
    (QuarantineFolder.PathOf); any other path as it is."""
    parts = path.split("/")
    if QUARANTINE not in parts:
        return path
    at = parts.index(QUARANTINE)
    return "/".join(parts[:at] + parts[at + 2:])


def move_source(candidates: list[str], target: str) -> str:
    """Of the paths that went with the inode now at `target`, the one it was renamed from. A file with several links
    (a library copy hard-linked to a seeding one) can lose more than one path at once; the quarantine keeps the path
    a name had below its root, so that one is preferred, then one with the same file name, then the first."""
    plain = outside_quarantine(target)
    name = target.rsplit("/", 1)[-1]

    def score(path: str) -> int:
        if outside_quarantine(path) == plain:
            return 2
        return 1 if path.rsplit("/", 1)[-1] == name else 0

    best = max(candidates, key=score)  # max keeps the first of equal scores
    candidates.remove(best)
    return best


def diff_inventory(before: dict, after: dict, ticked: list[str]) -> dict:
    old, new = before["entries"], after["entries"]
    gone = {p for p in old.keys() - new.keys()}
    appeared = {p for p in new.keys() - old.keys()}
    # A file that went at one path and appeared at another with the same device and inode was renamed: a quarantine
    # move or a restore keeps the inode; a copy would not.
    gone_by_id: dict[tuple, list[str]] = defaultdict(list)
    for p in sorted(gone):
        e = old[p]
        if e["type"] == "f":
            gone_by_id[(e["dev"], e["inode"])].append(p)
    moved = []
    for p in sorted(appeared):
        e = new[p]
        if e["type"] != "f":
            continue
        candidates = gone_by_id.get((e["dev"], e["inode"]))
        if candidates:
            source = move_source(candidates, p)
            moved.append({"from": source, "to": p, "size": e["size"], "inode": e["inode"],
                          "from_area": area_of(source, ticked), "to_area": area_of(p, ticked),
                          "changes": [f"{k} {old[source][k]} -> {e[k]}" for k in ("size", "mtime", "nlink", "uid", "gid",
                                                                                   "mode") if old[source][k] != e[k]]})
            gone.discard(source)
            appeared.discard(p)
    changed = []
    for p in sorted(old.keys() & new.keys()):
        a, b = old[p], new[p]
        fields = INVENTORY_FILE_FIELDS if a["type"] == "f" and b["type"] == "f" else INVENTORY_DIR_FIELDS
        what = [f"{k} {a[k]} -> {b[k]}" for k in fields if a[k] != b[k]]
        if what:
            changed.append({"path": p, "type": b["type"], "area": area_of(p, ticked), "changes": what})

    def listed(paths: set, entries: dict) -> list[dict]:
        return [{"path": p, "type": entries[p]["type"], "size": entries[p]["size"], "inode": entries[p]["inode"],
                 "nlink": entries[p]["nlink"], "area": area_of(p, ticked)} for p in sorted(paths)]

    return {"gone": listed(gone, old), "appeared": listed(appeared, new), "moved": moved, "changed": changed,
            "mounts_before": [m["destination"] for m in before["mounts"]],
            "mounts_after": [m["destination"] for m in after["mounts"]]}


def compare(before: dict, after: dict) -> dict:
    result: dict = {"against": before.get("label"), "against_written_utc": before.get("written_utc")}
    fb, fa = before.get("facts"), after.get("facts")
    if fb and fa:
        container = []
        pairs = [("image", fb["container"].get("image"), fa["container"].get("image")),
                 ("image id", fb["container"].get("image_id"), fa["container"].get("image_id")),
                 ("digests", fb.get("digests"), fa.get("digests")),
                 ("app version", fb.get("app_version"), fa.get("app_version")),
                 ("informational version", fb.get("informational_version"), fa.get("informational_version")),
                 ("Config.User", fb["container"].get("user"), fa["container"].get("user")),
                 ("process Uid", fb["process"].get("Uid"), fa["process"].get("Uid")),
                 ("process Gid", fb["process"].get("Gid"), fa["process"].get("Gid")),
                 ("exec id", fb.get("exec_id"), fa.get("exec_id")),
                 ("created", fb["container"].get("created"), fa["container"].get("created")),
                 ("started", fb["container"].get("started_at"), fa["container"].get("started_at")),
                 ("restart count", fb["container"].get("restart_count"), fa["container"].get("restart_count")),
                 ("status", fb["container"].get("status"), fa["container"].get("status"))]

        def mount_text(m: dict) -> str:
            return (f"{m['source']} ({m['type']}, {'read-write' if m['read_write'] else 'READ-ONLY'}, propagation "
                    f"{m['propagation'] or '—'})")

        def owner_text(d: dict) -> str:
            return f"{d.get('uid')}:{d.get('gid')} mode {d.get('mode')}" if "path" in d else str(d.get("raw"))

        # Keyed by path, so a change reads "mount /media: READ-ONLY -> read-write", not as two whole lists.
        for kind, key, text, old_items, new_items in (
                ("mount", "destination", mount_text, fb["mounts"], fa["mounts"]),
                ("data owner of", "path", owner_text, [d for d in fb["data_host"] if "path" in d],
                 [d for d in fa["data_host"] if "path" in d]),
                ("app's user may write in", "path", lambda w: "yes" if w["writable"] else "no",
                 fb.get("writable") or [], fa.get("writable") or [])):
            old_map = {item[key]: text(item) for item in old_items}
            new_map = {item[key]: text(item) for item in new_items}
            for path in sorted(old_map.keys() | new_map.keys()):
                pairs.append((f"{kind} {path}", old_map.get(path, "(none)"), new_map.get(path, "(none)")))
        pb, pa = fb.get("preflight"), fa.get("preflight")
        if pb is not None and pa is not None:  # a report written before the pre-flight existed has none to compare
            def compose_lines(p: dict) -> list[str]:
                return [l["text"].strip() for f in p.get("compose_files") or [] for l in f["lines"]]

            def owned_paths(p: dict) -> list[str]:
                return [f"{f['uid']}:{f['gid']} {f['mode']} {f['app_path']}"
                        for m in (p.get("root_owned") or {}).get("mounts") or [] for f in m["folders"]]

            pairs += [("compose lines", compose_lines(pb), compose_lines(pa)),
                      ("APP_ lines in .env", (pb.get("env") or {}).get("lines"), (pa.get("env") or {}).get("lines")),
                      ("auto-updaters", (pb.get("updaters") or {}).get("found"), (pa.get("updaters") or {}).get("found")),
                      (f"folders uid {MEDIA_OWNER_UID} can't write in", owned_paths(pb), owned_paths(pa))]
        for name, x, y in pairs:
            if json.dumps(x, default=str) != json.dumps(y, default=str):
                container.append({"what": name, "before": x, "after": y})
        result["container"] = container
        space = []
        old_rows = {(r["query"], r["target"]): r for r in fb.get("df", [])}
        for r in fa.get("df", []):
            o = old_rows.get((r["query"], r["target"]))
            if o:
                space.append({"query": r["query"], "target": r["target"], "avail_before": o["avail"],
                              "avail_after": r["avail"], "avail_delta": r["avail"] - o["avail"],
                              "used_delta": r["used"] - o["used"]})
        result["free_space"] = space
    ib, ia = before.get("inventory"), after.get("inventory")
    db_b, db_a = before["database"], after["database"]
    ticked = sorted(set(db_a.get("ticked_folders") or []) | set(db_b.get("ticked_folders") or []))
    if ib and ia:
        result["files"] = diff_inventory(ib, ia, ticked)
    qb = (before.get("summary") or {}).get("quarantine") or {}
    qa = (after.get("summary") or {}).get("quarantine") or {}
    result["quarantine"] = {"files_before": qb.get("files"), "files_after": qa.get("files"),
                            "bytes_before": qb.get("bytes"), "bytes_after": qa.get("bytes")}

    settings = []
    for name in ("switch", "ticked_folders", "allowed_roots", "path_mappings", "destinations"):
        if json.dumps(db_b.get(name), default=str) != json.dumps(db_a.get(name), default=str):
            settings.append({"what": name, "before": db_b.get(name), "after": db_a.get(name)})
    if db_b["migrations"] != db_a["migrations"]:
        settings.append({"what": "migrations", "before": db_b["migrations"][-1:], "after": db_a["migrations"][-1:],
                         "new": [m for m in db_a["migrations"] if m not in db_b["migrations"]]})
    result["settings"] = settings

    jobs = []
    old_jobs = {gid(j["Id"]): j for j in db_b.get("replacement_jobs", [])}
    for job in db_a.get("replacement_jobs", []):
        old = old_jobs.get(gid(job["Id"]))
        if old is None:
            jobs.append({"job": job["Id"], "release": job["Download"].get("ReleaseTitle"), "new": True,
                         "changes": [f"state {job['State']}"]})
            continue
        changes = field_changes("", old, job, ("State", "StateChangedUtc", "Reason", "QuarantinedUtc"))
        for kind, fields, label in (("Copies", ("Problem", "PairedNewFileId", "DroppedUtc", "OnlyDropSettles"), "copy"),
                                    ("NewFiles", ("AppPath", "SizeBytes", "CheckedUtc", "LibraryItemId", "Refreshed",
                                                  "MediaWorkId", "Problem"), "new file"),
                                    ("Names", ("State", "Problem", "MovedUtc", "RestoredUtc", "DeletedUtc"), "name")):
            before_rows = {gid(r["Id"]): r for r in old.get(kind, [])}
            for row in job.get(kind, []):
                tag = f"{label} #{row.get('Position')} {row.get('AppPath') or row.get('RelativePath')}"
                previous = before_rows.get(gid(row["Id"]))
                if previous is None:
                    changes.append(f"{tag}: new" + (f" ({row.get('State')})" if row.get("State") else ""))
                else:
                    changes += field_changes(tag + ": ", previous, row, fields)
        if changes:
            jobs.append({"job": job["Id"], "release": job["Download"].get("ReleaseTitle"), "new": False,
                         "changes": changes})
    result["replacement_jobs"] = jobs

    downloads = []
    old_downloads = {gid(j["Id"]): j for j in db_b.get("download_jobs", [])}
    for job in db_a.get("download_jobs", []):
        old = old_downloads.get(gid(job["Id"]))
        if old is None:
            downloads.append({"job": job["Id"], "release": job.get("ReleaseTitle"), "new": True,
                              "changes": [f"status {job.get('Status')}"]
                              + [f"{k} {job.get(k)}" for k in ("OverwriteCheck", "Error", "BlockReason") if job.get(k)]})
            continue
        changes = field_changes("", old, job, ("Status", "Error", "BlockReason", "OverwriteCheck"))
        if changes:
            downloads.append({"job": job["Id"], "release": job.get("ReleaseTitle"), "new": False, "changes": changes})
    result["download_jobs"] = downloads
    gone_audit = set(db_b.get("audit_ids", [])) - set(db_a.get("audit_ids", []))
    result["audit_new"] = db_a.get("audit_new") or []
    result["audit_rows_gone"] = len(gone_audit)
    return result


def field_changes(prefix: str, old: dict, new: dict, fields: tuple) -> list[str]:
    return [f"{prefix}{k} {old.get(k)!s} -> {new.get(k)!s}" for k in fields if old.get(k) != new.get(k)]


# ---------------------------------------------------------------- the text

def size_text(value) -> str:
    if value is None:
        return "—"
    try:
        value = int(value)
    except (TypeError, ValueError):
        return str(value)
    return f"{value:,} B ({value / 1024 ** 3:,.2f} GiB)"


def signed_size(value: int) -> str:
    sign = "+" if value > 0 else ("-" if value < 0 else "±")
    return f"{sign}{abs(value):,} B ({sign}{abs(value) / 1024 ** 3:,.2f} GiB)"


def text_or_dash(value) -> str:
    return "—" if value is None or value == "" else str(value)


def dimensions(new_file: dict) -> str:
    width, height = new_file.get("Width"), new_file.get("Height")
    return "—" if width is None and height is None else f"{text_or_dash(width)}x{text_or_dash(height)}"


def render(report: dict) -> str:
    out: list[str] = []
    add = out.append
    facts = report.get("facts")
    inventory = report.get("inventory")
    db = report["database"]
    s = report["summary"]
    add(f"MAM-28 report \"{report['label']}\", written {report['written_utc']} from {report['source']}")
    against = report.get("against")
    add(f"Compared with report \"{against['label']}\" written {against['written_utc']}" if against
        else "Compared with: nothing (the first report, or --against none)")
    if db["columns_missing"]:
        add("Columns this database lacks (an older schema): "
            + "; ".join(f"{t}: {', '.join(c)}" for t, c in sorted(db["columns_missing"].items())))
    add("")

    add("== At a glance (the release note's \"Turning it on\")")
    if facts:
        add(f"App version (deps.json): {text_or_dash(s.get('app_version'))}; informational: "
            f"{text_or_dash(s.get('informational_version'))}")
        add(f"Image {s.get('image')} ({s.get('image_id')}); {s.get('status')}, health {text_or_dash(s.get('health'))}, "
            f"started {s.get('started_at')}")
        if s.get("status") != "running":
            add("NOTE: the container is not running, so its process, `id`, mount table, write checks and /data as the "
                "app sees it could not be read; run the script again once it runs.")
        for digest in s.get("digests") or []:
            add(f"  digest {digest}")
        add(f"Runs as: Config.User {s.get('config_user')}; main process Uid {s.get('process_uid')}, Gid "
            f"{s.get('process_gid')}, umask {s.get('process_umask')}; docker exec id: {s.get('exec_id')}")
        for line in s.get("data_owner") or []:
            add(f"Data volume: {line}")
        for m in s.get("media_mounts") or []:
            add(f"Media mount {m['destination']} <- {m['source']} ({m['type']}, "
                f"{'read-write' if m['read_write'] else 'READ-ONLY'}, propagation {m['propagation'] or '—'})")
        for layout in s.get("mount_layout") or []:
            add(f"  mountinfo {layout['destination']}: {layout['mounts_at_it']} mount(s) at it "
                f"({'; '.join(layout['file_systems']) or '—'}); mounted inside it: "
                f"{'; '.join(layout['mounted_inside']) or 'nothing'}")
        for w in s.get("writable") or []:
            add(f"  the app's user {'MAY' if w['writable'] else 'may not'} write in {w['path']}")
    else:
        add("No container facts: --db reads a local database only.")
    add(f"Replacement switch: {text_or_dash(s['switch']) if s['switch'] is not None else 'no such setting (a database older than 0.3.0)'}")
    ticked = s["ticked_folders"]
    add("Ticked folders: " + ("no such setting (older than 0.3.0)" if ticked is None else (", ".join(ticked) or "none")))
    add("Allowed folders (Media folders): " + (", ".join(s["allowed_roots"]) or "none"))
    for space in s.get("free_space") or []:
        if space.get("target"):
            add(f"Free space at {space['root']}: {size_text(space['avail'])} available of {size_text(space['size'])} "
                f"({space['fstype']} {space['source']} mounted at {space['target']} on the host)")
        else:
            add(f"Free space at {space['root']}: unknown ({space.get('why_unknown') or 'not under a media mount'})")
    q = s["quarantine"]
    add(f"Quarantine: {q['files']} file(s), {size_text(q['bytes'])}"
        + (f"; quarantine folders elsewhere: {', '.join(q['elsewhere'])}" if q["elsewhere"] else ""))
    add("Replacement jobs by state: " + (", ".join(f"{k} {v}" for k, v in sorted(s["replacement_jobs_by_state"].items()))
                                        or "none"))
    for line in s["held"]:
        add(f"HELD: {line}")
    checks = report.get("preflight") or []
    if checks:
        counts = Counter(c["verdict"] for c in checks)
        add("Pre-flight: " + ", ".join(f"{counts[v]} {v}" for v in ("OK", "CHECK", "UNKNOWN", "INFO") if counts[v])
            + (" (see \"Pre-flight\" below)" if counts["CHECK"] or counts["UNKNOWN"] else ""))
    add("")

    render_preflight(add, report)
    if report.get("candidates") is not None:
        render_candidates(add, report["candidates"])

    if facts:
        add("== Container")
        c = facts["container"]
        add(f"Host {', '.join(facts.get('host') or [])}; docker {facts.get('docker')}; read {facts.get('read_utc')}")
        add(f"Container {c.get('name')}: created {c.get('created')}, started {c.get('started_at')}, finished "
            f"{c.get('finished_at')}, restarts {c.get('restart_count')}, pid {c.get('pid')}")
        add(f"Compose project: {text_or_dash(c.get('compose_dir'))} ({text_or_dash(c.get('compose_files'))})")
        add(f"Image {facts['image'].get('id')} created {facts['image'].get('created')}; tags: "
            f"{', '.join(facts.get('tags') or []) or '—'}")
        for m in facts.get("mounts", []):
            add(f"Mount {m['destination']} <- {m['source']} ({m['type']}{', volume ' + m['name'] if m['name'] else ''}, "
                f"{'read-write' if m['read_write'] else 'READ-ONLY'}, mode {m['mode'] or '—'}, propagation "
                f"{m['propagation'] or '—'})")
        add("Process: " + "; ".join(f"{k} {v}" for k, v in facts["process"].items()))
        for d in facts["data_container"]:
            add("/data as the app sees it: " + (f"{d['path']} {d['uid']}:{d['gid']} ({d['user']}:{d['group']}) mode "
                                                 f"{d['mode']}" if "path" in d else d.get("raw", "")))
        for row in facts.get("df", []):
            add(f"df {row['query']}: {row['fstype']} {row['source']} at {row['target']}: size {size_text(row['size'])}, "
                f"used {size_text(row['used'])}, available {size_text(row['avail'])}")
        if not facts.get("complete"):
            add("WARNING: the facts are cut short; see facts.sections in the JSON.")
        add("")

    if inventory is not None:
        add("== Files")
        for m in inventory["mounts"]:
            add(f"Inventory of {m['destination']} ({m['source']} on the host): {m['entries']:,} entries, "
                f"{m['files']:,} files, {size_text(m['file_bytes'])}"
                + (f", {m['unreadable']} unreadable records" if m["unreadable"] else ""))
        for line in inventory["errors"][:LIST_LIMIT]:
            add(f"  find: {line}")
        for f in folder_summary(inventory, (ticked or []) + [r for r in s["allowed_roots"] if r not in (ticked or [])]):
            add(f"  {f['folder']}: {f['files']:,} files outside the quarantine, {size_text(f['bytes'])}, "
                f"{f['files_with_links_above_one']} with more than one link"
                + ("" if f["inventoried"] else " (NOT under an inventoried mount)"))
        roots = s["allowed_roots"] or [m["destination"] for m in inventory["mounts"]]
        for qr in quarantines(inventory, roots)["roots"]:
            if not qr["exists"]:
                add(f"Quarantine {qr['folder']}: absent")
                continue
            fe = qr["folder_entry"] or {}
            add(f"Quarantine {qr['folder']} (owner {fe.get('uid')}:{fe.get('gid')}, mode {fe.get('mode')}): "
                f"{len(qr['files'])} file(s), {len(qr['dirs'])} folder(s), {size_text(qr['total_bytes'])}")
            for f in qr["files"]:
                add(f"  {f['path']}  {f['size']:,} B, mtime {mtime_text(f['mtime'])}, inode {f['inode']}, links "
                    f"{f['nlink']}, owner {f['uid']}:{f['gid']}, mode {f['mode']}")
        add("")

    add("== Settings")
    add(f"Database: quick_check {'ok' if db['quick_check'] == ['ok'] else 'NOT OK: ' + '; '.join(db['quick_check'])}; "
        f"{len(db['migrations'])} migrations, the last {db['migrations'][-1] if db['migrations'] else '—'}")
    add("Integrations: " + ("; ".join(f"{i['Name']} ({i['AdapterType']}, {i['Kind']}, {'on' if i['IsEnabled'] else 'off'})"
                                      for i in db["integrations"]) or "none"))
    add("Path mappings: " + ("; ".join(f"{m['integration']}: {m['service_prefix']} -> {m['app_prefix']}"
                                       for m in db["path_mappings"]) or "none"))
    add("Library folders: " + ("; ".join(f"{f['library']} ({f['integration']}): {f['service_path']}"
                                         for f in db["library_folders"]) or "none"))
    add("Destinations: " + ("; ".join(f"{d['name']} -> {d['client']} category {d['category']}"
                                      f"{' (default)' if d['default'] else ''}" for d in db["destinations"]) or "none"))
    add("Users: " + (", ".join(f"{u['name']}{' (owner)' if u['owner'] else ''}{' (disabled)' if u['disabled'] else ''}"
                               for u in db["users"]) or "none"))
    add("")

    add("== Replacement jobs" + ("" if db["has_replacements"] else ": none (no such table: a database older than 0.3.0)"))
    for job in db["replacement_jobs"]:
        d = job["Download"]
        add(f"[{short(job['Id'])}] {job['State']} since {job['StateChangedUtc']}; scheduled {job['CreatedUtc']} by "
            f"{job['RequestedByUserName']}; release \"{d.get('ReleaseTitle')}\" ({size_text(d.get('ReleaseSizeBytes'))}, "
            f"{d.get('ReleaseSource')}); editions {text_or_dash(job.get('ReleaseEditions'))}")
        add(f"    id {job['Id']}; download {short(d.get('Id'))} {d.get('Status')} since {d.get('StatusChangedUtc')}, "
            f"client {d.get('DownloadClientName')}, destination {d.get('DestinationName')}, hash {d.get('InfoHashV1')}, "
            f"progress {d.get('Progress')}, overwrite check {text_or_dash(d.get('OverwriteCheck'))}"
            + (f", error {d.get('Error')}" if d.get("Error") else "") + (f", blocked {d.get('BlockReason')}" if d.get("BlockReason") else ""))
        add(f"    check started {text_or_dash(job.get('InspectionStartedUtc'))}, library asked "
            f"{text_or_dash(job.get('LibraryAskedUtc'))}, library notice due {text_or_dash(job.get('LibraryNoticeDueUtc'))}, "
            f"quarantine checked {text_or_dash(job.get('QuarantineCheckedUtc'))}, quarantined "
            f"{text_or_dash(job.get('QuarantinedUtc'))}")
        if job.get("Reason"):
            add(f"    reason: {job['Reason']}")
        for w in job["Works"]:
            add(f"    work {w.get('Kind')} \"{w.get('Title')}\" ({w.get('Year')}), evidence {w.get('Evidence')}, confirmed by "
                f"owner {'yes' if w.get('ConfirmedByOwner') else 'no'}; ids "
                + (", ".join(f"{i['Scheme']}:{i['Value']}{' (set by owner)' if i['SetByOwner'] else ''}"
                             for i in w["Identifiers"]) or "—"))
        for c in job["Copies"]:
            episode = (f" S{c.get('SeasonNumber')}E{c.get('EpisodeNumber')}"
                       + (f"-{c.get('EpisodeNumberEnd')}" if c.get("EpisodeNumberEnd") else "")
                       if c.get("SeasonNumber") is not None else "")
            add(f"    copy #{c.get('Position')}{episode}: {c.get('AppPath')} ({size_text(c.get('SizeBytes'))}, inode "
                f"{c.get('Inode')}, links {c.get('LinkCount')}, edition {text_or_dash(c.get('EditionLabel'))})")
            add(f"        paired with {text_or_dash(c.get('PairedNewFile'))}; dropped {text_or_dash(c.get('DroppedUtc'))}; "
                f"only a drop settles it: {'yes' if c.get('OnlyDropSettles') else 'no'}; problem "
                f"{text_or_dash(c.get('Problem'))}; on disk now: {on_disk(inventory, c.get('AppPath'), c.get('Inode'))}")
        for f in job["NewFiles"]:
            add(f"    new #{f.get('Position')}: {f.get('RelativePath')} -> {text_or_dash(f.get('AppPath'))} "
                f"({size_text(f.get('SizeBytes'))}, {text_or_dash(f.get('VideoCodec'))} {dimensions(f)}, "
                f"{text_or_dash(f.get('DurationMilliseconds'))} ms, client progress {f.get('ClientProgress')})")
            add(f"        checked {text_or_dash(f.get('CheckedUtc'))}; library {text_or_dash(f.get('Kind'))} item "
                f"{text_or_dash(f.get('LibraryItemId'))} at {text_or_dash(f.get('LibraryPath'))}, refreshed "
                f"{'yes' if f.get('Refreshed') else 'no'}, read {text_or_dash(f.get('ReadUtc'))}; ids "
                + (", ".join(f"{i['Scheme']}:{i['Value']}{' (series)' if i['OfSeries'] else ''}" for i in f["Identifiers"])
                   or "—")
                + f"; problem {text_or_dash(f.get('Problem'))}; on disk now: {on_disk(inventory, f.get('AppPath'), f.get('Inode'))}")
        for n in job["Names"]:
            add(f"    name #{n.get('Position')} {n.get('Kind')} {n.get('State')}: {n.get('AppPath')} -> {n.get('QuarantinePath')} "
                f"({size_text(n.get('SizeBytes'))}, inode {n.get('Inode')}, root {n.get('AllowedRoot')})")
            add(f"        moved {text_or_dash(n.get('MovedUtc'))}, restored {text_or_dash(n.get('RestoredUtc'))}, deleted "
                f"{text_or_dash(n.get('DeletedUtc'))}; problem {text_or_dash(n.get('Problem'))}")
            add(f"        on disk now: at its place {on_disk(inventory, n.get('AppPath'), n.get('Inode'))}; in the quarantine "
                f"{on_disk(inventory, n.get('QuarantinePath'), n.get('Inode'))}")
        files = db["download_files"].get(gid(job["DownloadJobId"]), [])
        if files:
            add(f"    the torrent's files ({len(files)}): " + "; ".join(
                f"{f['RelativePath']} ({size_text(f['SizeBytes'])}{', not wanted' if f.get('IsWanted') is False else ''})"
                for f in files[:LIST_LIMIT]))
    if db["library_rows"]:
        add("What MAM's library sync last recorded at those paths:")
        for r in db["library_rows"]:
            add(f"  {r['ServicePath']}: {r['SourceKind']} {text_or_dash(r.get('SourceName'))}, size "
                f"{size_text(r['SizeBytes'])}, inode {r['Inode']}, links {r['LinkCount']}, inspection "
                f"{r['InspectionStatus']}, observed {r['ObservedUtc']}")
    add("")

    add(f"== Download jobs ({len(db['download_jobs'])}; the newest {LIST_LIMIT // 2} and every one with a replacement)")
    shown = [j for j in db["download_jobs"] if j.get("ReplacementJobId")]
    shown += [j for j in db["download_jobs"][-(LIST_LIMIT // 2):] if not j.get("ReplacementJobId")]
    for j in sorted(shown, key=lambda r: str(r.get("CreatedUtc"))):
        add(f"  {j.get('CreatedUtc')} [{short(j['Id'])}] {j.get('Status')} by {j.get('RequestedByUserName')}: "
            f"\"{j.get('ReleaseTitle')}\" -> {j.get('DestinationName')}; payload {text_or_dash(j.get('PayloadKind'))}, "
            f"overwrite check {text_or_dash(j.get('OverwriteCheck'))}"
            + (f"; replacement {short(j['ReplacementJobId'])}" if j.get("ReplacementJobId") else "")
            + (f"; error {j['Error']}" if j.get("Error") else "") + (f"; blocked {j['BlockReason']}" if j.get("BlockReason") else ""))
    add("")

    add(f"== Audit trail of replacements and the settings around them ({len(db['audit_trail'])} rows)")
    for r in db["audit_trail"]:
        add("  " + audit_text(r))
    add("Audit rows by action: " + ", ".join(f"{k} {v}" for k, v in db["audit_by_action"].items()))
    add("Latest audit rows:")
    for r in db["audit_latest"][:15]:
        add("  " + audit_text(r))
    add("")

    cmp = report.get("comparison")
    if cmp:
        add(f"== Changes since report \"{cmp['against']}\" ({cmp['against_written_utc']})")
        for item in cmp.get("container", []):
            before, after = item["before"], item["after"]
            if isinstance(before, (list, type(None))) and isinstance(after, (list, type(None))):
                # A list (compose lines, APP_ lines, digests, folders) reads as the lines it gained and lost.
                gained = [x for x in after or [] if x not in (before or [])]
                lost = [x for x in before or [] if x not in (after or [])]
                add(f"Container {item['what']}: " + ("; ".join([f"+ {x}" for x in gained] + [f"- {x}" for x in lost])
                                                     or "the same lines in another order"))
            else:
                add(f"Container {item['what']}: {before} -> {after}")
        for item in cmp.get("free_space", []):
            if item["avail_delta"] or item["used_delta"]:
                asked = "" if item["query"] == item["target"] else f" (df of {item['query']})"
                add(f"Free space at {item['target']}{asked} on the host: {size_text(item['avail_before'])} -> "
                    f"{size_text(item['avail_after'])}, available {signed_size(item['avail_delta'])}, used "
                    f"{signed_size(item['used_delta'])}")
        qc = cmp["quarantine"]
        add(f"Quarantine: {qc['files_before']} -> {qc['files_after']} file(s), {size_text(qc['bytes_before'])} -> "
            f"{size_text(qc['bytes_after'])}")
        files = cmp.get("files")
        if files is None:
            add("Files: not compared (one of the two reports has no inventory)")
        else:
            add(f"Files: moved (same inode) {len(files['moved'])}, gone {len(files['gone'])}, appeared "
                f"{len(files['appeared'])}, changed {len(files['changed'])}")
            for m in files["moved"][:DIFF_LIMIT]:
                add(f"  > [{m['from_area']} -> {m['to_area']}] {m['from']}  ->  {m['to']} ({m['size']:,} B, inode "
                    f"{m['inode']}{'; ' + '; '.join(m['changes']) if m['changes'] else ''})")
            for g in files["gone"][:DIFF_LIMIT]:
                add(f"  - [{g['area']}] {g['type']} {g['path']} ({g['size']:,} B, inode {g['inode']}, links {g['nlink']})")
            for a in files["appeared"][:DIFF_LIMIT]:
                add(f"  + [{a['area']}] {a['type']} {a['path']} ({a['size']:,} B, inode {a['inode']}, links {a['nlink']})")
            for ch in files["changed"][:DIFF_LIMIT]:
                add(f"  ~ [{ch['area']}] {ch['type']} {ch['path']}: {'; '.join(ch['changes'])}")
            for name in ("moved", "gone", "appeared", "changed"):
                if len(files[name]) > DIFF_LIMIT:
                    add(f"  ... {len(files[name]) - DIFF_LIMIT} more {name} in the JSON")
        for item in cmp["settings"]:
            add(f"Setting {item['what']}: {item['before']} -> {item['after']}")
        for job in cmp["replacement_jobs"]:
            add(f"Replacement {short(job['job'])} \"{job['release']}\"{' (new)' if job['new'] else ''}:")
            for change in job["changes"]:
                add(f"    {change}")
        for job in cmp["download_jobs"]:
            add(f"Download {short(job['job'])} \"{job['release']}\"{' (new)' if job['new'] else ''}: "
                + "; ".join(job["changes"]))
        add(f"New audit rows: {len(cmp['audit_new'])}" + (f"; rows gone: {cmp['audit_rows_gone']}" if cmp["audit_rows_gone"] else ""))
        for r in cmp["audit_new"]:
            add("  " + audit_text(r))
    return "\n".join(out) + "\n"


def render_preflight(add, report: dict) -> None:
    add("== Pre-flight (runbook section 0.1, read in the same ssh call; nothing was changed)")
    if not report.get("facts"):
        add("Not read: --db reads a local database only.")
        add("")
        return
    if report["label"] != STRICT_LABEL:
        add(f"What step 2 changes (the user: line, the APP_ lines, the data owner, the app's ids, the quarantine) reads "
            f"INFO at this step; it is judged against section 0.1 on \"{STRICT_LABEL}\" only.")
    add("OK: as section 0.1 expects. CHECK: not as expected; on \"before\" stop and report back. UNKNOWN: it could not "
        "be read. INFO: nothing to judge.")
    for c in report.get("preflight") or []:
        add(f"  {c['verdict']:<7} {c['check']}: {c['value']}"
            + (f" (expected: {c['expected']})" if c["expected"] and c["verdict"] in ("CHECK", "UNKNOWN") else ""))
        for detail in c["details"][:LIST_LIMIT]:
            add(f"            {detail}")
        if len(c["details"]) > LIST_LIMIT:
            add(f"            ... {len(c['details']) - LIST_LIMIT} more in the JSON")
    add("")


def render_candidates(add, cand: dict) -> None:
    add("== Candidates (runbook section D: the film and the episode to upgrade; Vasyl picks)")
    add("Computed on this PC from the database copy and the file inventory. Kept: a copy Jellyfin reported in a library "
        "that holds only films (only episodes), inspected, with a TMDb or IMDb id (an episode: its series' ids and one "
        "season and episode number, its own library item's, no a/b half, no file of several episodes), one link, not a "
        "disc folder clip, not "
        "also a copy of another work, not held by an open replacement, under an allowed folder and on disk; a film "
        f"not one part of a film split over several files (other videos beside it are noted); every folder from its "
        f"own up to the library folder owned by uid "
        f"{cand['media_owner_uid']} with u=wx. Ranked worst first: resolution, then codec age (mpeg2 < mpeg4/xvid < "
        "h264 < hevc < av1), then MB per minute. Not decided here: which torrent holds it (the Download dialog names "
        "it), and whether it is unchanged when the dialog reads it (B7's Refresh all first).")
    if not cand["inventory"]:
        add("No file inventory (--db): the on-disk and folder checks read \"not checked\", and a film's folder is "
            "judged by the names the database knows.")
    for note in cand["notes"]:
        add(f"NOTE: {note}")
    if cand["libraries"]:
        add("Libraries: " + "; ".join(
            f"{lib['name']} ({', '.join(lib['folders']) or 'no folder'}): {lib['films']} film name(s), {lib['episodes']} "
            f"episode name(s) -> " + ("films" if lib["holds"] == "films" else "episodes" if lib["holds"] == "episodes"
                                      else "left out" if lib["holds"] == "films and episodes" else "nothing")
            for lib in cand["libraries"]))
    for kind, noun in (("films", "Films"), ("episodes", f"Episodes (at most {EPISODES_PER_SERIES} per series)")):
        part = cand.get(kind)
        if part is None:
            continue
        add(f"{noun}: {part['considered']} name(s) considered, {part['kept']} kept"
            + ("; left out: " + ", ".join(f"{why} {n}" for why, n in part["left_out"].items()) if part["left_out"] else ""))
        if not part["top"]:
            add("  none")
        for number, row in enumerate(part["top"], 1):
            if kind == "films":
                head = f"\"{text_or_dash(row['title'])}\" ({text_or_dash(row['year'])}); {row['ids_text']}"
            else:
                head = (f"\"{text_or_dash(row['title'])}\" ({text_or_dash(row['year'])}) S{row['season']:02d}E"
                        f"{row['episode']:02d} \"{text_or_dash(row.get('episode_title'))}\"; series "
                        f"{row['series_ids_text']}" + (f"; episode {row['ids_text']}" if row["ids"] else ""))
            second = row["second_copy"]
            add(f"  {number:>2}. {head}; {text_or_dash(row['width'])}x{text_or_dash(row['height'])} {row['resolution']} "
                f"{text_or_dash(row['codec'])}{' ' + row['dynamic_range'] if row.get('dynamic_range') not in (None, 'Sdr', 'SDR') else ''}; "
                f"{gib(row['size_bytes'])}, {duration_text(row['duration_ms'])}, "
                f"{text_or_dash(row['mb_per_minute'])} MB/min, {text_or_dash(row['container'])}; second copy known: "
                + (f"yes, {second['count']} ({'; '.join(second['paths'][:2])})" if second["count"] else "no"))
            add(f"      {row['app_path']} (library {row['library']}, folder {row['library_folder']}; links "
                f"{row['link_count']}; edition {text_or_dash(row['edition'])}; folders: {row['folders']})"
                + "".join(f"; {note}" for note in row["notes"]))
    add("")


def audit_text(r: dict) -> str:
    ids = "; ".join(f"{i.get('Role')} {i.get('Scheme')}:{i.get('Value')}" for i in r.get("Identifiers") or [])
    target = ("(no target)" if not r.get("TargetKind") and not r.get("TargetName")
              else f"{text_or_dash(r.get('TargetKind'))} \"{text_or_dash(r.get('TargetName'))}\"")
    return (f"{r.get('CreatedUtc')} {r.get('Action')} by {r.get('ActorUserName') or '(the app)'}: {target}"
            + (f" = {r['Target']}" if r.get("Target") else "") + (f" [{ids}]" if ids else ""))


# ---------------------------------------------------------------- main

def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="MAM-28: read-only report of the MAM container, its database and the "
                                                 "media folders at one step of Phase C's live check.")
    parser.add_argument("label", help="the step: before, on, scheduled, moved, restored, deleted, ... "
                                      "(letters, digits, . _ -; up to 40)")
    parser.add_argument("--against", help="compare with report-<label>.json (a kept one too: <label>-<UTC time>); "
                                          "default: the report written last; 'none' compares with nothing")
    parser.add_argument("--ssh-host", default=common.DEFAULT_SSH_HOST, help=f"default {common.DEFAULT_SSH_HOST}")
    parser.add_argument("--container", default=common.MAM_CONTAINER, help=f"default {common.MAM_CONTAINER}")
    parser.add_argument("--compose-dir", help="the compose project's folder on the host; default: the one docker "
                                              f"compose recorded on the container, else {DEFAULT_COMPOSE_DIR}")
    parser.add_argument("--candidates", action="store_true",
                        help="add the Candidates section (the film and the episode to pick) at this step too; the "
                             f"labels {' and '.join(CANDIDATE_LABELS)} always have it")
    parser.add_argument("--db", type=Path, help="read this local mam.db (and its -wal/-shm) instead; no ssh, no "
                                                "container, mount or file facts")
    parser.add_argument("--out-dir", type=Path, default=OUT_DIR, help=f"where the reports go; default {OUT_DIR}")
    args = parser.parse_args(argv)
    if not LABEL.fullmatch(args.label) or args.label == "none":
        parser.error(f"label {args.label!r}: use letters, digits, '.', '_' or '-' (up to 40), starting with a letter "
                     "or digit; 'none' is taken by --against")
    if args.against and args.against != "none" and not KEPT_LABEL.fullmatch(args.against):
        parser.error(f"--against {args.against!r} is not a label")
    return args


def load_previous(out_dir: Path, against: str | None) -> tuple[Path | None, dict | None]:
    if against == "none":
        return None, None
    if against:
        path = out_dir / f"report-{against}.json"
        if not path.is_file():
            raise ToolError(f"--against {against}: there is no {path}.")
        candidates = [path]
    else:
        candidates = sorted(out_dir.glob("report-*.json"), key=lambda p: p.stat().st_mtime, reverse=True)
    for path in candidates:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            if against:
                raise ToolError(f"{path} cannot be read as a report.") from None
            continue
        if isinstance(data, dict) and data.get("tool") == TOOL:
            return path, data
        if against:
            raise ToolError(f"{path} is not a MAM-28 report.")
    return None, None


def keep_earlier(out_dir: Path, label: str) -> None:
    """A step run again must not overwrite the report it ran before: the "before" report can't be taken again once
    0.3.0 runs. The earlier one is renamed to report-<label>-<UTC time>.txt/.json, which --against can name."""
    paths = [out_dir / f"report-{label}.txt", out_dir / f"report-{label}.json"]
    if not any(path.exists() for path in paths):
        return
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    for path in paths:
        if path.exists():
            path.replace(path.with_name(f"report-{label}-{stamp}{path.suffix}"))
    say(f"The earlier report \"{label}\" is kept as report-{label}-{stamp}.txt and .json.")


def run(args: argparse.Namespace) -> int:
    out_dir = args.out_dir
    copy_dir = out_dir / COPY_DIR_NAME
    out_dir.mkdir(parents=True, exist_ok=True)
    previous_path, previous = load_previous(out_dir, args.against)
    previous_ids = set(previous["database"].get("audit_ids") or []) if previous else None
    if previous:
        say(f"Comparing with {previous_path} (report \"{previous.get('label')}\", {previous.get('written_utc')}).")
    try:
        if args.db:
            copy_local(args.db, copy_dir)
            source = str(args.db)
        else:
            fetch(args.ssh_host, args.container, copy_dir, args.compose_dir)
            source = f"{args.ssh_host}, container {args.container}"
        facts = None
        facts_path = copy_dir / FACTS_FILE
        if facts_path.is_file():
            facts = parse_facts(facts_path.read_text(encoding="utf-8", errors="replace"))
        inventory = parse_inventory(copy_dir)
        db = Database(copy_dir / "mam.db")
        try:
            database = read_database(db, previous_ids)
            candidates = (find_candidates(db, database, inventory)
                          if args.candidates or args.label in CANDIDATE_LABELS else None)
        finally:
            db.close()
    finally:
        left = remove_copy(copy_dir)
    report = {"tool": TOOL, "label": args.label, "source": source,
              "written_utc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
              "against": ({"label": previous.get("label"), "written_utc": previous.get("written_utc"),
                           "file": str(previous_path)} if previous else None),
              "facts": facts, "inventory": inventory, "database": database}
    report["summary"] = summarize(facts, inventory, database)
    report["preflight"] = preflight_checks(args.label, facts, inventory)
    report["candidates"] = candidates
    if previous:
        report["comparison"] = compare(previous, report)
    text_path = out_dir / f"report-{args.label}.txt"
    json_path = out_dir / f"report-{args.label}.json"
    text = render(report)
    keep_earlier(out_dir, args.label)
    text_path.write_text(text, encoding="utf-8", newline="\n")
    json_path.write_text(json.dumps(report, indent=1, ensure_ascii=False, default=str) + "\n", encoding="utf-8",
                         newline="\n")
    say(text)
    say("The database copy was deleted." if not left else "The database copy could NOT be deleted completely (see above).")
    say(f"Tell Claude about these two files: {text_path} and {json_path}")
    return 0


def main(argv: list[str] | None = None) -> int:
    common.setup_console()
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        return run(args)
    except ToolError as error:
        say(f"ERROR: {error}")
        return 2
    except OSError as error:
        say(f"ERROR: {error}")
        return 2
    except KeyboardInterrupt:
        say("Interrupted. Nothing was changed anywhere.")
        return 130


if __name__ == "__main__":
    sys.exit(main())
