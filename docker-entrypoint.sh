#!/bin/sh
#
# The image's entrypoint (MAM-39): runs the app as PUID and PGID, the user and group ids of the
# media folders' owner, the convention the linuxserver.io and hotio images of the rest of a media
# stack (qBittorrent, Jellyfin, Sonarr) already follow. Replacement (0.3.0) renames files in the
# media folders, and only their owner may. The container starts as root for one thing: handing the
# data directory over to those ids. Then this script drops to them, with every capability gone, no
# supplementary group but those Docker gave the container (group_add), and no way back, and replaces
# itself with the app. Unset, both are 10001, the image's own mam user, so an installation that
# never sets them runs as it always has.
# docs/adr/0036 says why.
#
# Arguments: the app's command line (ENTRYPOINT in the Dockerfile). Every line printed starts with
# [entrypoint], so it stands apart from the app's own log.
set -eu

say() { printf '[entrypoint] %s\n' "$*" >&2; }

# Started as another user, by user: in docker-compose.yml (the compose file of 0.3.0 has the line)
# or by docker run --user. That user can neither change owners nor switch ids, so the ids it was
# given stand, and saying so beats ignoring PUID and PGID in silence.
if [ "$(id -u)" != 0 ]; then
  if [ -n "${PUID:-}${PGID:-}" ]; then
    say "PUID and PGID are ignored: the container already runs as $(id -u):$(id -g), set by user: in docker-compose.yml or by docker run --user. Remove that to use PUID and PGID."
  fi
  exec "$@"
fi

# Prints the id NAME holds (VALUE, 10001 when empty) without leading zeros, or says what is wrong
# with it and fails. Digits only: setpriv, chown and find must all read the same number, and none of
# them gets a name to look up. 4294967295 is refused too: it is (uid_t)-1, which setresuid() reads
# as "keep the current id", and the current id is root's.
parse_id() {
  name=$1 value=${2:-10001} example=$3
  case $value in
    '' | *[!0-9]*) ;;
    *)
      digits=${value#"${value%%[!0]*}"}
      if [ -z "$digits" ]; then
        say "$name is 0: the app never runs as root. Set PUID and PGID to the owner of the media folders, or remove them to run as the image's own 10001."
        return 1
      fi
      if [ "${#digits}" -le 10 ] && [ "$digits" -le 4294967294 ]; then
        echo "$digits"
        return 0
      fi
      ;;
  esac
  say "$name must be a number from 1 to 4294967294, such as $example, not \"$value\"."
  return 1
}

uid=$(parse_id PUID "${PUID:-}" 1000) || exit 1
gid=$(parse_id PGID "${PGID:-}" 100) || exit 1

# Root without the capabilities this takes — cap_drop: ALL in the compose file, which was harmless
# while the image started as its own user — can neither look into the 0700 data directory nor switch
# ids, so it would fail half-way with a puzzling error. Said up front instead, with the way out:
# CHOWN and DAC_OVERRIDE for the hand-over, SETGID, SETUID and SETPCAP for setpriv.
needed=$(( (1 << 0) | (1 << 1) | (1 << 6) | (1 << 7) | (1 << 8) ))
effective=$(sed -n 's/^CapEff:[[:space:]]*//p' /proc/self/status)
if [ $(( 0x$effective & needed )) -ne "$needed" ]; then
  say "The container runs as root without the capabilities it takes to switch to $uid:$gid (CHOWN, DAC_OVERRIDE, SETGID, SETUID and SETPCAP; cap_drop: ALL removes them), so the app was not started. Keep Docker's default capabilities, or run the container as a user instead: user: \"$uid:$gid\" in docker-compose.yml, with the data volume already theirs."
  exit 1
fi

# The data directory changes hands, and nothing else. Whatever is mounted inside it (a media folder
# by mistake, a bind-mounted secret file) keeps its owner, and so does all it holds: mountpoint
# skips each mount, which -xdev alone would miss when both come from one disk and share a device
# number. find doesn't follow symbolic links, and chown -h changes a link itself, never what it
# points at. Only what another id owns is touched, so a start with nothing to change writes nothing;
# modes stay as they are (0700 folders, 0600 secrets). Every folder is asked whether it is a mount,
# a file only when it is about to change hands: the data directory holds a poster cache that can
# run to thousands of files. A data directory that doesn't exist yet is the app's to create.
dir=${MAM_Storage__DataDirectory:-/data}
if [ -d "$dir" ]; then
  if ! changed=$(cd "$dir" && find . -xdev \
      \( -type d ! -name . -exec mountpoint -q --nofollow {} \; -prune \) -o \
      \( ! -uid "$uid" -o ! -gid "$gid" \) \( -type d -o ! -exec mountpoint -q --nofollow {} \; \) \
      -printf x -exec chown -h -- "$uid:$gid" {} +); then
    say "Could not hand $dir over to $uid:$gid; the app was not started."
    exit 1
  fi
  if [ "${#changed}" -eq 1 ]; then
    say "Handed $dir over to $uid:$gid: 1 file or folder changed owner; permissions are unchanged."
  elif [ "${#changed}" -gt 1 ]; then
    say "Handed $dir over to $uid:$gid: ${#changed} files and folders changed owner; permissions are unchanged."
  fi
fi

# HOME still names root's home, which the app's user can't read. /nonexistent is what the image's
# own user has always had: the app keeps nothing outside the data directory, and ASP.NET Core's
# data protection keeps its keys in memory, as before.
export HOME=/nonexistent

# The supplementary groups Docker gave the container — compose's group_add, docker run --group-add —
# stay the app's, as they were while the image started as its own user: they are how an installation
# lets the app read media that only a group may read. Root's own group 0 never does.
groups=$(id -G | tr ' ' '\n' | grep -vx 0 | paste -sd, -) || true
if [ -n "$groups" ]; then
  set -- --groups="$groups" -- "$@"
else
  set -- --clear-groups -- "$@"
fi

# exec all the way: the app becomes PID 1 and gets docker stop's SIGTERM itself. --no-new-privs and
# the emptied bounding set mean nothing the app runs can gain a privilege back, setuid binaries
# included.
exec setpriv --reuid="$uid" --regid="$gid" --inh-caps=-all --bounding-set=-all --no-new-privs "$@"
