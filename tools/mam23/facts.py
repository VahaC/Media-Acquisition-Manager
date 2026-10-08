r"""MAM-23 facts: who owns the media folders and who may write there, read on the media host before Phase C's write
access is chosen (ADR 0033's open part: run the app as the media's owner, or grant it a group or an ACL).

STRICTLY READ-ONLY. One ssh call to the media host runs read commands only: stat, find -printf, findmnt, df, getfacl,
docker ps / inspect / info, and through docker exec only id, cat, grep, ls and the shell's test -w / -x, which ask the
kernel whether the MAM container's user may write somewhere without writing anything. No file, folder, permission,
container or setting is changed, and no credential is read: of each container's environment only PUID, PGID and
UMASK are kept, and of qBittorrent's and Jellyfin's settings only the keys named below.

Reads, over ssh (root@192.168.1.195 asks for its password itself):
  - the host: kernel, fs.protected_hardlinks / protected_regular, the LXC's uid and gid maps (an unprivileged LXC
    maps its root to a high host id), docker's security options;
  - the MAM, Jellyfin and qBittorrent containers: image, user, added groups, mounts and whether each is read-write,
    PUID / PGID / UMASK, and the uid, gid, groups and umask of each one's main process;
  - the folder MAM mounts at /media: its mount, file system, options and free space; owner, group and mode of it and
    of everything directly in it; counts of owner:group and mode over the whole tree; extended ACLs, if any;
  - inside the MAM container: its user, and whether that user has write and search permission on /media, on each
    folder in it and on each library's subfolders (test -w -x); and, since a rename also fails where that test
    can't see it, how many files and folders have an owner the LXC can't map (65534), how many folders are sticky
    and how many files are immutable or append-only;
  - qBittorrent: its disk I/O, file pool, queueing, share-limit and content-removal settings;
  - Jellyfin: the .NET runtime it carries, its library-monitor delay and whether "date added" is a file's
    creation time.
Writes: only D:\Sources\Media-Acquisition-Manager\.claude\mam23-check\facts.txt (gitignored).
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam23\facts.py
When it ends, tell Claude: it reads facts.txt.
"""

from __future__ import annotations

import argparse
import base64
import shlex
import sys
from pathlib import Path

# common.py of the MAM-34 tools: the console setup and the ssh runner. Nothing else is shared.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mam34"))
import common  # noqa: E402
from common import ToolError  # noqa: E402

OUT_DIR = common.REPO_ROOT / ".claude" / "mam23-check"
QBITTORRENT_CONTAINER = "qbittorrent"  # MAM-6's name; the script falls back to the container whose image says qbittorrent

# Run by `sh` on the media host, sent base64-encoded so no quoting of the Windows command line can change it. sh reads
# this very script from stdin, so the whole body is one function called with stdin from /dev/null. Nothing stops on
# an error: a failed read prints its error in its own section and the next section still runs.
REMOTE_SCRIPT = r"""
mam=@MAM@
jf=@JELLYFIN@
qb=@QBITTORRENT@
main() {
  section() { printf '\n@@ %s\n' "$1"; }
  section read; date -u +%Y-%m-%dT%H:%M:%SZ
  section host; uname -sr
  echo "fs.protected_hardlinks=$(cat /proc/sys/fs/protected_hardlinks 2>&1) fs.protected_regular=$(cat /proc/sys/fs/protected_regular 2>&1)"
  echo "uid_map:"; cat /proc/self/uid_map
  echo "gid_map:"; cat /proc/self/gid_map
  command -v systemd-detect-virt >/dev/null 2>&1 && echo "virt: $(systemd-detect-virt 2>&1)"
  docker info -f 'docker security options: {{.SecurityOptions}}' 2>&1

  section containers
  docker ps -a --format '{{.Names}}|{{.Image}}|{{.Status}}' 2>&1
  if ! docker inspect --type container "$qb" >/dev/null 2>&1; then
    qb=$(docker ps -a --format '{{.Names}}|{{.Image}}' | grep -i qbittorrent | head -n 1 | cut -d'|' -f1)
  fi
  echo "qBittorrent container: ${qb:-not found}"
  media=$(docker inspect --type container -f '{{range .Mounts}}{{if eq .Destination "/media"}}{{.Source}}{{end}}{{end}}' "$mam" 2>/dev/null)
  echo "MAM's /media is the host folder: ${media:-unknown (no such container, or no /media mount)}"

  for c in "$mam" "$jf" "$qb"; do
    [ -n "$c" ] || continue
    section "container $c"
    docker inspect --type container -f 'image={{.Config.Image}} user={{if .Config.User}}{{.Config.User}}{{else}}(image default){{end}} group_add={{.HostConfig.GroupAdd}} userns={{.HostConfig.UsernsMode}} privileged={{.HostConfig.Privileged}} cap_add={{.HostConfig.CapAdd}}' "$c" 2>&1
    docker inspect --type container -f '{{range .Mounts}}mount {{.Destination}} <- {{.Source}} type={{.Type}} rw={{.RW}} mode={{.Mode}}{{println}}{{end}}' "$c" 2>&1
    docker inspect --type container -f '{{range .Config.Env}}{{println .}}{{end}}' "$c" 2>/dev/null | grep -E '^(PUID|PGID|UMASK|UMASK_SET)=' || echo "(no PUID, PGID or UMASK in its environment)"
  done

  section "main processes: uid, gid, groups, umask"
  for pair in "$qb:qbittorrent" "$jf:jellyfin" "$mam:dotnet"; do
    c=${pair%%:*}; name=${pair#*:}
    [ -n "$c" ] || continue
    echo "-- $c ($name*)"
    docker exec "$c" sh -c 'for p in /proc/[0-9]*; do comm=$(cat "$p/comm" 2>/dev/null) || continue; case "$comm" in "$1"*) grep -E "^(Name|Umask|Uid|Gid|Groups):" "$p/status";; esac; done' sh "$name" 2>&1
  done
  echo "-- id inside $mam"; docker exec "$mam" id 2>&1

  if [ -n "$media" ] && [ -d "$media" ]; then
    section "the media folder's mount and free space"
    findmnt -T "$media" -o TARGET,SOURCE,FSTYPE,OPTIONS 2>&1
    df -h --output=size,used,avail,pcent,target "$media" 2>&1

    section "owner and mode: path|owner|uid|group|gid|mode"
    stat -c '%n|%U|%u|%G|%g|%a|%A' "$media" 2>&1
    for e in "$media"/* "$media"/.[!.]*; do [ -e "$e" ] && stat -c '%n|%U|%u|%G|%g|%a|%A' "$e" 2>&1; done

    section "the whole tree: count, type (d folder, f file), owner:group, mode"
    find "$media" -xdev \( -type d -o -type f \) -printf '%y %u:%g %m\n' 2>&1 | sort | uniq -c | sort -rn | head -n 60

    section "what a rename refuses whatever the permissions say"
    echo "owner or group the LXC can't map (65534): $(find "$media" -xdev \( -uid 65534 -o -gid 65534 \) 2>/dev/null | wc -l)"
    echo "sticky folders (a rename there needs the file's or the folder's owner): $(find "$media" -xdev -type d -perm -1000 2>/dev/null | wc -l)"
    if command -v lsattr >/dev/null 2>&1; then
      echo "immutable or append-only files and folders: $(lsattr -R "$media" 2>/dev/null | awk 'NF >= 2 && $1 ~ /^[-A-Za-z]+$/ && $1 ~ /[ia]/' | wc -l)"
    else
      echo "lsattr is not installed: immutable files not counted"
    fi

    section "extended ACLs"
    if command -v getfacl >/dev/null 2>&1; then
      echo "entries with an ACL beyond owner/group/other: $(getfacl -R -s -p "$media" 2>/dev/null | grep -c '^# file:')"
      getfacl -p "$media" 2>&1
      for e in "$media"/*/; do getfacl -p "${e%/}" 2>&1; done
    else
      echo "getfacl is not installed (the acl package): ACLs not read"
    fi
  else
    section "the media folder"
    echo "MAM has no /media mount, or its source $media is not a folder here"
  fi

  section "write and search permission for the MAM container's user (test -w -x asks the kernel; nothing is written; the section above lists what it can't see)"
  docker exec "$mam" sh -c '
    check() { if [ -w "$1" ] && [ -x "$1" ]; then echo "yes $1"; else echo "no  $1"; fi; }
    check /media
    for d in /media/*/; do check "${d%/}"; done
    for lib in /media/*/; do
      y=0; n=0
      for d in "$lib"*/; do
        [ -d "$d" ] || continue
        if [ -w "$d" ] && [ -x "$d" ]; then y=$((y + 1)); else n=$((n + 1)); fi
      done
      echo "subfolders of ${lib%/}: $y may be renamed in, $n may not"
    done' 2>&1

  if [ -n "$qb" ]; then
    section "qBittorrent settings (unset = qBittorrent's default)"
    docker exec "$qb" sh -c '
      for f in /config/qBittorrent/qBittorrent.conf /config/qBittorrent/config/qBittorrent.conf; do
        [ -f "$f" ] || continue
        echo "file: $f"
        grep -E "^(BitTorrent\\\\)?Session\\\\(DiskIOType|DiskIOReadMode|DiskIOWriteMode|FilePoolSize|QueueingSystemEnabled|TorrentContentRemoveOption|DefaultSavePath|TempPathEnabled|GlobalMaxRatio|GlobalMaxSeedingMinutes|GlobalMaxInactiveSeedingMinutes|ShareLimitAction|MaxActiveCheckingTorrents)=" "$f"
        exit 0
      done
      echo "qBittorrent.conf not found under /config"' 2>&1
  fi

  section "Jellyfin"
  docker exec "$jf" sh -c '
    grep -o "Microsoft.NETCore.App.Runtime[^\"]*" /jellyfin/jellyfin.deps.json 2>/dev/null | sort -u | head -n 3
    f=/config/config/system.xml
    if [ -f "$f" ]; then grep -E "<LibraryMonitorDelay>" "$f" || echo "(LibraryMonitorDelay unset: default 60)"; else echo "system.xml not found at $f"; fi
    f=/config/config/metadata.xml
    if [ -f "$f" ]; then grep -E "<UseFileCreationTimeForDateAdded>" "$f" || echo "(UseFileCreationTimeForDateAdded unset: default true)"; else echo "metadata.xml not found at $f"; fi' 2>&1

  section end
}
main </dev/null
"""


def say(text: str = "") -> None:
    print(text, flush=True)


def remote_script() -> str:
    return (REMOTE_SCRIPT
            .replace("@MAM@", shlex.quote(common.MAM_CONTAINER))
            .replace("@JELLYFIN@", shlex.quote(common.JELLYFIN_CONTAINER))
            .replace("@QBITTORRENT@", shlex.quote(QBITTORRENT_CONTAINER)))


def main() -> int:
    common.setup_console()
    parser = argparse.ArgumentParser(description="Read, without changing anything, who owns the media folders on the "
                                                 "media host and whether MAM's container may write there (MAM-23).")
    parser.add_argument("--host", default=common.DEFAULT_SSH_HOST, help=f"ssh target (default {common.DEFAULT_SSH_HOST})")
    parser.add_argument("--out-dir", type=Path, default=OUT_DIR, help=f"where facts.txt goes (default {OUT_DIR})")
    args = parser.parse_args()

    say(f"Reading the media folders' owners and permissions on {args.host} (read-only)...")
    encoded = base64.b64encode(remote_script().encode("utf-8")).decode("ascii")
    code, out, _ = common.ssh_run(args.host, f"echo {encoded} | base64 -d | sh", timeout=600)
    text = out.decode("utf-8", errors="replace")
    if code == 255:
        raise ToolError("ssh itself failed (a wrong password, an unreachable host or a refused key).")
    if code == 124:
        raise ToolError("ssh did not finish in 10 minutes and was stopped.")
    if "@@ end" not in text:
        raise ToolError(f"The host's answer stopped early (ssh exit code {code}); see the lines above.")

    args.out_dir.mkdir(parents=True, exist_ok=True)
    target = args.out_dir / "facts.txt"
    target.write_text(text, encoding="utf-8", newline="\n")
    say(text)
    say(f"Saved to {target}. Tell Claude it is there.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ToolError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        sys.exit(130)
