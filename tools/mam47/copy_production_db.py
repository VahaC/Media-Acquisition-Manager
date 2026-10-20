r"""MAM-47: copy production's database to this PC, so the conversion of its stored English sentences can be tried on it.

STRICTLY READ-ONLY on the media host. One ssh call (root@192.168.1.195 asks for its password itself, once) finds the MAM
container's /data mount with `docker inspect`, copies mam.db and mam.db-wal into a `mktemp -d` folder until a copy is
taken while neither file changed (size and full-resolution mtime the same before and after, as tools/mam22 and
tools/mam28 do), streams them back as tar, and removes the folder in the same command, also on failure. It never lists,
copies or reads `.secrets`, and it never opens the database on the host: SQLite opened as root there could leave a
root-owned -shm or -wal behind that the app's own user can't write.

On this PC the two files are opened, checked with `PRAGMA quick_check`, and saved as one file (the WAL folded in) at
%USERPROFILE%\mam47-production-copy\mam.db — outside the repository, out of reach of git and graphify. The raw files are
deleted. The copy holds password and token hashes and sealed integration keys (without the key that opens them): it is
deleted after the MAM-47 conversion run (run this script with --delete to do it).

Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam47\copy_production_db.py
        python D:\Sources\Media-Acquisition-Manager\tools\mam47\copy_production_db.py --delete
Python 3.10+, standard library only.
"""

from __future__ import annotations

import argparse
import base64
import io
import os
import shlex
import sqlite3
import sys
import tarfile
import time
from pathlib import Path

# common.py of the MAM-34 tools: the console setup, the ssh runner and the error type. Nothing else is shared.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mam34"))
import common  # noqa: E402
from common import ToolError  # noqa: E402

DEFAULT_SSH_HOST = "root@192.168.1.195"
DEFAULT_CONTAINER = "media-acquisition-manager"
RAW_FILES = ("mam.db", "mam.db-wal")

# One ssh call, run by `sh` on the media host, sent base64-encoded so no quoting of the Windows command line can change
# it. Its stdout is the tar stream and nothing else.
REMOTE_SCRIPT = r"""
container=@CONTAINER@
main() {
  mounts=$(docker inspect --type container -f '{{range .Mounts}}{{.Destination}}|{{.Source}}{{println}}{{end}}' "$container") || { echo "docker inspect $container failed" >&2; exit 11; }
  src=$(printf '%s\n' "$mounts" | sed -n 's#^/data|##p' | head -n 1)
  [ -n "$src" ] || { echo "container $container has no mount at /data" >&2; exit 12; }
  [ -f "$src/mam.db" ] || { echo "no mam.db in $src" >&2; exit 13; }
  tmp=$(mktemp -d) || { echo "mktemp failed" >&2; exit 14; }
  trap 'rm -rf "$tmp"' EXIT
  trap 'exit 130' HUP INT TERM
  snap() { for f in mam.db mam.db-wal; do [ -e "$src/$f" ] && stat -c '%n %s %y' "$src/$f"; done; true; }
  n=0
  while :; do
    n=$((n + 1))
    rm -f "$tmp/mam.db" "$tmp/mam.db-wal"
    before=$(snap)
    cp "$src/mam.db" "$tmp/mam.db" || { echo "cp of mam.db failed" >&2; exit 15; }
    [ -f "$src/mam.db-wal" ] && cp "$src/mam.db-wal" "$tmp/mam.db-wal"
    after=$(snap)
    [ "$before" = "$after" ] && break
    [ "$n" -ge 5 ] && { echo "mam.db changed while it was being copied (5 tries); run again in a quiet minute" >&2; exit 16; }
    sleep 1
  done
  cd "$tmp" && tar -cf - -- *
}
main </dev/null
"""

REMOTE_FAILURES = {
    11: "docker could not inspect the MAM container (check the container name and that docker is running)",
    12: "the MAM container has no mount at /data",
    13: "there is no mam.db in the container's /data mount",
    14: "mktemp failed on the media host",
    15: "copying mam.db failed on the media host",
    16: "mam.db kept changing while it was copied; run the script again in a quiet minute",
    124: "ssh did not finish in time and was stopped",
    255: "ssh itself failed (a wrong password, an unreachable host or a refused key)",
}


def say(text: str = "") -> None:
    print(text, flush=True)


def out_dir() -> Path:
    return Path(os.environ.get("USERPROFILE") or Path.home()) / "mam47-production-copy"


def delete_files(folder: Path, names: tuple[str, ...]) -> list[str]:
    """Deletes the named files (never anything else); a file that can't be deleted is named, never passed over."""
    left = []
    for name in names:
        path = folder / name
        for _attempt in range(3):
            try:
                path.unlink(missing_ok=True)
                break
            except OSError:
                time.sleep(0.3)  # a virus scanner may hold a freshly written file for a moment
        else:
            left.append(str(path))
    return left


def unpack(data: bytes, folder: Path) -> None:
    """Writes the tar stream's files into `folder`. The archive comes from another machine, so every member must be one of
    the expected names, a plain file, and land directly in `folder`."""
    try:
        archive = tarfile.open(fileobj=io.BytesIO(data), mode="r:")
    except tarfile.TarError:
        raise ToolError("The media host's answer is not a tar stream. Something in its login scripts may print to "
                        "stdout; the first bytes were: " + repr(data[:80])) from None
    with archive:
        members = {}
        for member in archive.getmembers():
            name = member.name[2:] if member.name.startswith("./") else member.name
            if name not in RAW_FILES or not member.isreg():
                raise ToolError(f"Refusing the unexpected tar member {member.name!r}.")
            members[name] = member
        if "mam.db" not in members:
            raise ToolError("The tar stream from the media host holds no mam.db.")
        folder.mkdir(parents=True, exist_ok=True)
        for name, member in members.items():
            handle = archive.extractfile(member)
            (folder / name).write_bytes(handle.read() if handle else b"")


def fold(raw: Path, target: Path) -> str:
    """Opens the raw copy (its WAL replays here, on this PC), checks it, and saves it as one file at `target`."""
    source = sqlite3.connect(raw / "mam.db")
    try:
        verdict = source.execute("PRAGMA quick_check").fetchone()[0]
        if verdict != "ok":
            raise ToolError(f"The copy failed SQLite's quick_check ({verdict}); run the script again.")
        destination = sqlite3.connect(target)
        try:
            source.backup(destination)
        finally:
            destination.close()
        migration = source.execute(
            'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1').fetchone()
        return migration[0] if migration else "(none)"
    finally:
        source.close()


def run(args: argparse.Namespace) -> int:
    folder = out_dir()
    if args.delete:
        # The -wal and -shm a reader of the copy left beside it, and the conversion report ProductionCopyConversionTests
        # writes there by default: both may quote production's own words.
        left = delete_files(folder, ("mam.db", "mam.db-wal", "mam.db-shm", "mam47-conversion-report.md",
                                     *(f"raw/{n}" for n in RAW_FILES), "raw/mam.db-shm"))
        for sub in (folder / "raw", folder):
            try:
                sub.rmdir()
            except OSError:
                pass
        if left:
            raise ToolError("Could not delete " + ", ".join(left) + ". They hold password hashes: delete them by hand.")
        say(f"DONE deleted the production copy in {folder}")
        return 0

    say(f"Copying the MAM database from {args.host} (container {args.container}; read-only, about a minute)...")
    script = REMOTE_SCRIPT.replace("@CONTAINER@", shlex.quote(args.container))
    encoded = base64.b64encode(script.encode("utf-8")).decode("ascii")
    code, out, _ = common.ssh_run(args.host, f"echo {encoded} | base64 -d | sh", timeout=900)
    if code != 0:
        raise ToolError(f"The copy failed (ssh exit code {code}): " + REMOTE_FAILURES.get(code, "see the lines above") + ".")

    raw = folder / "raw"
    if delete_files(folder, ("mam.db",)) or delete_files(raw, (*RAW_FILES, "mam.db-shm")):
        raise ToolError(f"The previous copy in {folder} could not be cleared; delete that folder and run again.")
    unpack(out, raw)
    try:
        migration = fold(raw, folder / "mam.db")
    finally:
        delete_files(raw, (*RAW_FILES, "mam.db-shm"))
        try:
            raw.rmdir()
        except OSError:
            pass
    size = (folder / "mam.db").stat().st_size
    say(f"DONE {folder / 'mam.db'} ({size / 1024 / 1024:.1f} MiB, last migration {migration}, quick_check ok)")
    return 0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--host", default=DEFAULT_SSH_HOST, help=f"ssh target (default {DEFAULT_SSH_HOST})")
    parser.add_argument("--container", default=DEFAULT_CONTAINER, help=f"MAM container (default {DEFAULT_CONTAINER})")
    parser.add_argument("--delete", action="store_true", help="delete the copy this script made, and nothing else")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    common.setup_console()
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        return run(args)
    except ToolError as error:
        say(f"ERROR: {error}")
        return 2
    except (OSError, sqlite3.Error) as error:
        say(f"ERROR: {error}")
        return 2
    except KeyboardInterrupt:
        say("Interrupted. Nothing was changed on the media host.")
        return 130


if __name__ == "__main__":
    sys.exit(main())
