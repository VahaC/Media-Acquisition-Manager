r"""MAM-22 report: what production's MAM database and container say about the library's files, before and after the
Phase B live check (Refresh all, then the duplicates list and page).

STRICTLY READ-ONLY. One ssh call to the media host runs read commands only (docker inspect, docker exec cat / ffprobe
-version, stat, cp of the database into a mktemp folder, tar); the database copy is opened on this PC with SQLite
mode=ro and every statement must be a SELECT. Nothing in MAM, Jellyfin, qBittorrent or /media is changed.

Reads, over ssh (root@192.168.1.195 asks for its password itself):
  - a copy of mam.db, mam.db-wal and mam.db-shm from the MAM container's /data mount, never the .secrets folder; the
    script selects only the columns it names, never a password hash, a session token or a sealed key;
  - docker inspect of the container: its image, the image's digest, when it started, its mounts and whether each is
    read-write;
  - the container's /proc/self/mountinfo (the table MAM reads a file system's type from) and `ffprobe -version`,
    through docker exec.
Writes: only under D:\Sources\Media-Acquisition-Manager\.claude\mam22-check\ (gitignored): report-<label>.txt and
        report-<label>.json, and while it runs db-copy\ (deleted at the end). On the media host the one thing it
        creates is a mktemp -d folder that holds the copy while it is streamed back; the same command removes it.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam22\report.py before
        python D:\Sources\Media-Acquisition-Manager\tools\mam22\report.py after
"after" also compares itself with report-before.json: which file names, sizes and identities changed, which runs and
audit rows are new. When it ends, tell Claude about the files it names on its last line.
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import io
import json
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

OUT_DIR = common.REPO_ROOT / ".claude" / "mam22-check"
DATABASE_FILES = ("mam.db", "mam.db-wal", "mam.db-shm")
FACTS_FILE = "facts.txt"
LIST_LIMIT = 40  # rows of a list printed in the text report; the JSON keeps them all

# One ssh call, run by `sh` on the media host, sent base64-encoded so no quoting of the Windows command line can change
# it. Every command reads stdin from /dev/null because sh reads this very script from stdin. The database copy is the
# MAM-34 inventory's: it counts as consistent when the size and full-resolution mtime of mam.db and mam.db-wal are the
# same before and after the cp (the -shm is an index SQLite rebuilds, and changes whenever anyone reads).
REMOTE_SCRIPT = r"""
container=@CONTAINER@
mounts=$(docker inspect --type container -f '{{range .Mounts}}{{.Destination}}|{{.Source}}{{println}}{{end}}' "$container" </dev/null) || { echo "docker inspect $container failed (is the container name right and docker running?)" >&2; exit 11; }
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
  rm -f "$tmp/mam.db" "$tmp/mam.db-wal" "$tmp/mam.db-shm"
  before=$(snap </dev/null)
  cp "$src/mam.db" "$tmp/mam.db" </dev/null || { echo "cp of mam.db failed" >&2; exit 15; }
  [ -f "$src/mam.db-wal" ] && cp "$src/mam.db-wal" "$tmp/mam.db-wal" </dev/null
  [ -f "$src/mam.db-shm" ] && cp "$src/mam.db-shm" "$tmp/mam.db-shm" </dev/null
  after=$(snap </dev/null)
  [ "$before" = "$after" ] && break
  if [ "$n" -ge 3 ]; then echo "mam.db changed while it was being copied (3 tries); the copy may be inconsistent" >&2; break; fi
  sleep 1
done
image=$(docker inspect --type container -f '{{.Image}}' "$container" </dev/null)
{
  echo "@@ read"; date -u +%Y-%m-%dT%H:%M:%SZ </dev/null
  echo "@@ host"; uname -sr </dev/null
  echo "@@ container"; docker inspect --type container -f '{{.Config.Image}}|{{.Image}}|{{.State.Status}}|{{.State.StartedAt}}|{{.RestartCount}}' "$container" </dev/null
  echo "@@ digests"; docker image inspect -f '{{range .RepoDigests}}{{.}}{{println}}{{end}}' "$image" </dev/null
  echo "@@ mounts"; docker inspect --type container -f '{{range .Mounts}}{{.Destination}}|{{.Source}}|{{.Type}}|{{.RW}}|{{.Mode}}{{println}}{{end}}' "$container" </dev/null
  echo "@@ mountinfo"; docker exec "$container" cat /proc/self/mountinfo </dev/null
  echo "@@ ffprobe"; docker exec "$container" ffprobe -version </dev/null
  echo "@@ end"
} > "$tmp/facts.txt" 2>&1
files=mam.db
[ -f "$tmp/mam.db-wal" ] && files="$files mam.db-wal"
[ -f "$tmp/mam.db-shm" ] && files="$files mam.db-shm"
tar -C "$tmp" -cf - $files facts.txt
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

def remove_copy(folder: Path) -> list[str]:
    """Deletes the copy's files (never anything else) and the folder if that leaves it empty. The copy holds password
    hashes and sealed keys, so a file that can't be deleted is named, never passed over."""
    left = []
    for name in (*DATABASE_FILES, FACTS_FILE):
        for _attempt in range(3):
            try:
                (folder / name).unlink(missing_ok=True)
                break
            except OSError:
                time.sleep(0.3)  # a virus scanner may hold a freshly written file for a moment
        else:
            left.append(name)
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
    allowed = (*DATABASE_FILES, FACTS_FILE)
    try:
        archive = tarfile.open(fileobj=io.BytesIO(data), mode="r:")
    except tarfile.TarError:
        raise ToolError("The media host's answer is not a tar stream. Something in its login scripts may print to "
                        "stdout; the first bytes were: " + repr(data[:80])) from None
    with archive:
        try:
            members = archive.getmembers()
        except (tarfile.TarError, EOFError):
            raise ToolError("The tar stream from the media host is cut short; run the script again.") from None
        wanted = {}
        for member in members:
            name = member.name[2:] if member.name.startswith("./") else member.name
            if name not in allowed or not member.isreg():
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
            except (tarfile.TarError, EOFError):
                raise ToolError("The tar stream from the media host is cut short; run the script again.") from None


def fetch(ssh_host: str, folder: Path) -> None:
    say(f"Copying the MAM database and reading the container's facts on {ssh_host} "
        f"(container {common.MAM_CONTAINER})...")
    script = REMOTE_SCRIPT.replace("@CONTAINER@", shlex.quote(common.MAM_CONTAINER))
    encoded = base64.b64encode(script.encode("utf-8")).decode("ascii")
    code, out, _ = common.ssh_run(ssh_host, f"echo {encoded} | base64 -d | sh", timeout=300)
    if code != 0:
        raise ToolError(f"The copy failed (ssh exit code {code}): " + REMOTE_FAILURES.get(code, "see the lines above") + ".")
    if remove_copy(folder):  # a stale -wal beside a new mam.db would be replayed over it
        raise ToolError(f"The previous copy in {folder} could not be cleared; delete that folder and run again.")
    unpack(out, folder)


def copy_local(source: Path, folder: Path) -> None:
    """--db: a database file on this PC, copied with its -wal and -shm, never opened in place (opening a WAL database
    can create files beside it). No container facts then."""
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
    """The copy, opened read-only; select() refuses anything but a SELECT or a read-only pragma."""

    def __init__(self, path: Path) -> None:
        # mode=ro, never immutable=1: immutable ignores the -wal, where the newest rows may still be.
        try:
            self.connection = sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=10)
            self.connection.row_factory = sqlite3.Row
            self.connection.execute("PRAGMA query_only = ON")
        except sqlite3.Error as error:
            raise ToolError(f"The database copy cannot be opened: {error}. Run the script again.") from None
        self.tables = {row["name"] for row in self.select("SELECT name FROM sqlite_master WHERE type = 'table'")}

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

    def close(self) -> None:
        self.connection.close()


# ---------------------------------------------------------------- the container's facts

def parse_facts(text: str) -> dict:
    sections: dict[str, list[str]] = defaultdict(list)
    current = None
    for line in text.splitlines():
        if line.startswith("@@ "):
            current = line[3:].strip()
            continue
        if current:
            sections[current].append(line)
    facts: dict = {"read_utc": first(sections["read"]), "host": first(sections["host"])}
    parts = (first(sections["container"]) or "").split("|")
    if len(parts) == 5:
        facts["container"] = {"image": parts[0], "image_id": parts[1], "status": parts[2], "started_at": parts[3],
                              "restart_count": parts[4]}
    else:
        facts["container"] = {"raw": sections["container"]}
    facts["digests"] = [line for line in sections["digests"] if line.strip()]
    facts["mounts"] = []
    for line in sections["mounts"]:
        bits = line.split("|")
        if len(bits) == 5:
            facts["mounts"].append({"destination": bits[0], "source": bits[1], "type": bits[2],
                                    "read_write": bits[3] == "true", "mode": bits[4]})
    facts["mountinfo"] = sections["mountinfo"]
    facts["ffprobe"] = first(sections["ffprobe"])
    facts["complete"] = "end" in sections or any(line.startswith("@@ end") for line in text.splitlines())
    return facts


def first(lines: list[str]) -> str | None:
    for line in lines:
        if line.strip():
            return line.strip()
    return None


def device_number(device_id: int) -> str:
    """The stored st_dev as the mount table writes it, major:minor: MountTable.DeviceNumberOf, the inverse of glibc's
    makedev on the unsigned bits."""
    device = device_id & 0xFFFFFFFFFFFFFFFF
    major = ((device >> 8) & 0xFFF) | ((device >> 32) & 0xFFFFF000)
    minor = (device & 0xFF) | ((device >> 12) & 0xFFFFFF00)
    return f"{major}:{minor}"


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
        lines.append({"device": fields[2], "root": fields[3], "mount_point": fields[4].replace("\\040", " "),
                      "options": fields[5], "type": fields[separator + 1],
                      "source": fields[separator + 2] if separator + 2 < len(fields) else ""})
    return lines


def file_system_type(lines: list[dict], device_id: int) -> str | None:
    """MountTable.FileSystemTypeOf: the type every line of the device agrees on, else none."""
    wanted = device_number(device_id)
    found = None
    for line in lines:
        if line["device"] != wanted:
            continue
        if found is None:
            found = line["type"]
        elif found != line["type"]:
            return None
    return found


# ---------------------------------------------------------------- the database

def read_database(db: Database, facts: dict | None) -> dict:
    check = [str(row[next(iter(row))]) for row in db.select("PRAGMA quick_check")]
    report: dict = {"quick_check": check}
    report["migrations"] = [row["MigrationId"] for row in
                            db.select('SELECT MigrationId FROM "__EFMigrationsHistory" ORDER BY MigrationId')]

    connections = {row["Id"]: row for row in
                   db.select("SELECT Id, Name, AdapterType, Kind, IsEnabled FROM IntegrationConnections ORDER BY Name")}
    report["integrations"] = list(connections.values())
    report["allowed_roots"] = [row["Path"] for row in db.select("SELECT Path FROM AllowedRoots ORDER BY Position")]
    report["path_mappings"] = [
        {"integration": connections.get(row["IntegrationConnectionId"], {}).get("Name"),
         "service_prefix": row["ServicePrefix"], "app_prefix": row["AppPrefix"]}
        for row in db.select("SELECT IntegrationConnectionId, ServicePrefix, AppPrefix FROM PathMappings "
                             "ORDER BY IntegrationConnectionId, Position")]

    report["sync_runs"] = db.select(
        "SELECT Id, IntegrationName, Status, RequestedByUserName, CreatedUtc, StartedUtc, FinishedUtc, ItemsRead, "
        "ItemsTotal, WorksCount, SeriesCount, VersionsCount, FilesCount, Error FROM LibrarySyncRuns "
        "ORDER BY CreatedUtc DESC LIMIT 12")
    report["scan_runs"] = db.select(
        "SELECT Id, Status, RequestedByUserName, CreatedUtc, StartedUtc, FinishedUtc, FilesFound, FilesUnreported, "
        "ProblemCount, Problem, Error FROM FolderScanRuns ORDER BY CreatedUtc DESC LIMIT 6") if db.has("FolderScanRuns") else []
    report["inspection_runs"] = db.select(
        "SELECT Id, Trigger, Status, RequestedByUserName, CreatedUtc, StartedUtc, FinishedUtc, FilesTotal, "
        "FilesChecked, FilesProbed, HashCandidates, HashCandidatesChecked, HashCandidatesRead, HashProblemCount, "
        "HashProblem, Error FROM FileInspectionRuns ORDER BY CreatedUtc DESC LIMIT 8")

    locations = {row["Id"]: row["Name"] for row in db.select("SELECT Id, Name FROM LibraryLocations")}
    rows = db.select(
        "SELECT Id, SourceKind, SourceConnectionId, LibraryLocationId, MediaVersionId, ServicePath, SizeBytes, "
        "DeviceId, Inode, LinkCount, ReadSizeBytes, ReadModifiedUtc, InspectionStatus, InspectionError "
        "FROM PhysicalFiles")
    report["files"] = files_report(rows, connections, locations, facts)

    hashes = db.select("SELECT DeviceId, Inode, SizeBytes, ModifiedUtc FROM FileHashes") if db.has("FileHashes") else []
    current = {(row["DeviceId"], row["Inode"], row["ReadSizeBytes"], row["ReadModifiedUtc"]) for row in rows
               if row["DeviceId"] is not None}
    report["hashes"] = {"rows": len(hashes),
                        "for_a_current_name": sum(1 for h in hashes
                                                  if (h["DeviceId"], h["Inode"], h["SizeBytes"], h["ModifiedUtc"]) in current)}
    readings = db.select("SELECT COUNT(*) AS n, SUM(Error IS NOT NULL) AS errors FROM MediaInspections")[0]
    report["readings"] = {"rows": readings["n"], "with_error": readings["errors"] or 0}
    report["dynamic_ranges"] = db.select(
        "SELECT DynamicRange, COUNT(*) AS n FROM MediaInspectionTracks WHERE Kind = 'Video' GROUP BY DynamicRange "
        "ORDER BY n DESC")

    report["works"] = db.select("SELECT Kind, COUNT(*) AS n FROM MediaWorks GROUP BY Kind ORDER BY Kind")
    report["works_with_two_copies"] = db.select(
        "SELECT COUNT(*) AS n FROM (SELECT v.MediaWorkId FROM MediaVersions v "
        "WHERE EXISTS (SELECT 1 FROM PhysicalFiles f WHERE f.MediaVersionId = v.Id) "
        "GROUP BY v.MediaWorkId HAVING COUNT(*) > 1)")[0]["n"]

    report["links"] = db.select("SELECT WriteState, COUNT(*) AS n FROM LibraryItemLinks GROUP BY WriteState "
                                "ORDER BY WriteState") if db.has("LibraryItemLinks") else []
    report["decisions"] = db.select("SELECT GroupKey, Decision, DecidedByUserName, DecidedUtc FROM "
                                    "DuplicateMatchDecisions ORDER BY DecidedUtc") if db.has("DuplicateMatchDecisions") else None
    report["audit_by_action"] = db.select("SELECT Action, COUNT(*) AS n FROM AuditEvents GROUP BY Action ORDER BY Action")
    report["audit_ids"] = [row["Id"] for row in db.select("SELECT Id FROM AuditEvents")]
    report["audit_latest"] = db.select("SELECT Id, Action, ActorUserName, TargetKind, TargetName, CreatedUtc "
                                       "FROM AuditEvents ORDER BY CreatedUtc DESC LIMIT 15")
    report["run_ids"] = {
        "sync": [row["Id"] for row in db.select("SELECT Id FROM LibrarySyncRuns")],
        "scan": [row["Id"] for row in db.select("SELECT Id FROM FolderScanRuns")] if db.has("FolderScanRuns") else [],
        "inspection": [row["Id"] for row in db.select("SELECT Id FROM FileInspectionRuns")],
    }
    return report


def files_report(rows: list[dict], connections: dict, locations: dict, facts: dict | None) -> dict:
    """The names, the files behind them and the room they take, by FileSpace's rule: a name counts only with a device,
    an inode and a size the app read; each (device, inode) once, at the larger size two looks disagree on."""
    def space(selected: list[dict]) -> dict:
        measured = [r for r in selected if r["DeviceId"] is not None and r["Inode"] is not None
                    and r["ReadSizeBytes"] is not None]
        sizes: dict = {}
        for r in measured:
            key = (r["DeviceId"], r["Inode"])
            sizes[key] = max(sizes.get(key, 0), r["ReadSizeBytes"])
        return {"names": len(selected), "measured_names": len(measured), "unmeasured_names": len(selected) - len(measured),
                "files": len(sizes), "bytes": sum(sizes.values()),
                "library_reported_bytes": sum(r["SizeBytes"] or 0 for r in selected if r["SourceKind"] == "MediaLibrary")}

    library = [r for r in rows if r["SourceKind"] == "MediaLibrary"]
    scanned = [r for r in rows if r["SourceKind"] != "MediaLibrary"]
    by_location: dict = defaultdict(list)
    for r in library:
        by_location[(connections.get(r["SourceConnectionId"], {}).get("Name"), locations.get(r["LibraryLocationId"]))].append(r)

    identities: dict = defaultdict(set)
    for r in rows:
        if r["DeviceId"] is not None and r["Inode"] is not None:
            identities[(r["DeviceId"], r["Inode"])].add(r["ServicePath"])
    sizes: dict = defaultdict(set)
    for r in rows:
        if r["DeviceId"] is not None and r["Inode"] is not None and r["ReadSizeBytes"] is not None:
            sizes[r["ReadSizeBytes"]].add((r["DeviceId"], r["Inode"]))
    shared_sizes = []
    for size, keys in sorted(sizes.items()):
        if len(keys) > 1:
            shared_sizes.append({"size": size, "files": [sorted(identities[k]) for k in sorted(keys)]})

    lines = mount_lines(facts["mountinfo"]) if facts else []
    devices = Counter(r["DeviceId"] for r in rows if r["DeviceId"] is not None)
    by_device = [{"device_id": device, "device": device_number(device), "names": n,
                  "file_system": file_system_type(lines, device) if facts else None}
                 for device, n in devices.most_common()]

    status = Counter(r["InspectionStatus"] for r in rows)
    errors = [{"path": r["ServicePath"], "status": r["InspectionStatus"], "error": r["InspectionError"]}
              for r in rows if r["InspectionError"]]
    size_differs = [{"path": r["ServicePath"], "library": r["SizeBytes"], "read": r["ReadSizeBytes"]} for r in library
                    if r["ReadSizeBytes"] is not None and r["SizeBytes"] is not None and r["ReadSizeBytes"] != r["SizeBytes"]]
    return {
        "all": space(rows),
        "library": space(library),
        "scanned_only": space(scanned),
        "scanned_only_paths": sorted(r["ServicePath"] for r in scanned),
        "by_location": [{"integration": k[0], "location": k[1], **space(v)} for k, v in sorted(by_location.items(),
                                                                                                key=lambda kv: (str(kv[0][0]), str(kv[0][1])))],
        "distinct_paths": len({r["ServicePath"] for r in rows}),
        "link_count": dict(Counter("unknown" if r["LinkCount"] is None else ("1" if r["LinkCount"] == 1 else ">1")
                                   for r in rows)),
        "names_with_links_above_one": sorted({r["ServicePath"] for r in rows if (r["LinkCount"] or 0) > 1}),
        "files_with_two_known_paths": [sorted(p) for p in identities.values() if len(p) > 1],
        "sizes_shared_by_two_files": shared_sizes,
        "devices": by_device,
        "inspection_status": dict(status),
        "inspection_errors": errors,
        "library_size_differs_from_read": size_differs,
        "records": [{"key": f"{r['SourceKind']}|{r['SourceConnectionId'] or ''}|{r['ServicePath']}",
                     "size": r["SizeBytes"], "read_size": r["ReadSizeBytes"], "read_modified": r["ReadModifiedUtc"],
                     "device": r["DeviceId"], "inode": r["Inode"], "links": r["LinkCount"],
                     "status": r["InspectionStatus"]} for r in rows],
    }


# ---------------------------------------------------------------- the comparison

def compare(before: dict, after: dict) -> dict:
    old = {r["key"]: r for r in before["database"]["files"]["records"]}
    new = {r["key"]: r for r in after["database"]["files"]["records"]}
    changed = []
    for key in sorted(old.keys() & new.keys()):
        a, b = old[key], new[key]
        what = []
        if a["size"] != b["size"]:
            what.append(f"library size {a['size']} -> {b['size']}")
        if a["read_size"] is not None and b["read_size"] is not None:
            for field in ("read_size", "read_modified", "device", "inode", "links"):
                if a[field] != b[field]:
                    what.append(f"{field} {a[field]} -> {b[field]}")
        if what:
            changed.append({"key": key, "changes": what})
    measured_in_both = sum(1 for key in old.keys() & new.keys()
                           if old[key]["read_size"] is not None and new[key]["read_size"] is not None)
    ids_before = set(before["database"]["audit_ids"])
    new_audit = [row for row in after["database"]["audit_latest"] if row["Id"] not in ids_before]
    audit_after_ids = set(after["database"]["audit_ids"])
    runs = {kind: sorted(set(after["database"]["run_ids"][kind]) - set(before["database"]["run_ids"][kind]))
            for kind in ("sync", "scan", "inspection")}
    return {
        "names_in_both": len(old.keys() & new.keys()),
        "measured_in_both": measured_in_both,
        "only_before": sorted(old.keys() - new.keys()),
        "only_after": sorted(new.keys() - old.keys()),
        "changed": changed,
        "new_audit_rows": len(audit_after_ids - ids_before),
        "new_audit_latest": new_audit,
        "audit_rows_gone": len(ids_before - audit_after_ids),
        "new_runs": runs,
        "links_before": before["database"]["links"],
        "links_after": after["database"]["links"],
        "decisions_before": before["database"]["decisions"],
        "decisions_after": after["database"]["decisions"],
    }


# ---------------------------------------------------------------- the text

def size_text(value: int | None) -> str:
    if value is None:
        return "—"
    return f"{value:,} B ({value / 1024 ** 4:.2f} TiB, {value / 1024 ** 3:,.1f} GiB)"


def render(report: dict) -> str:
    out: list[str] = []
    add = out.append
    db = report["database"]
    facts = report.get("facts")
    add(f"MAM-22 report \"{report['label']}\", written {report['written_utc']} from {report['source']}")
    if facts:
        c = facts.get("container", {})
        add(f"Read on the host at {facts.get('read_utc')}; host {facts.get('host')}")
        add(f"Container: image {c.get('image')} ({c.get('image_id')}), {c.get('status')}, started {c.get('started_at')}, "
            f"restarts {c.get('restart_count')}")
        for digest in facts.get("digests", []):
            add(f"  digest {digest}")
        for m in facts.get("mounts", []):
            add(f"Mount {m['destination']} <- {m['source']} ({m['type']}, {'read-write' if m['read_write'] else 'READ-ONLY'}"
                f"{', mode ' + m['mode'] if m['mode'] else ''})")
        for line in mount_lines(facts.get("mountinfo", [])):
            if line["mount_point"] in ("/data", "/media") or line["mount_point"].startswith("/media/"):
                add(f"  mountinfo {line['mount_point']}: {line['type']} (device {line['device']}, source {line['source']}, "
                    f"options {line['options']})")
        add(f"ffprobe: {facts.get('ffprobe')}")
        if not facts.get("complete"):
            add("WARNING: the facts section is cut short; see facts in the JSON.")
    add("")
    add(f"Database: quick_check {'ok' if db['quick_check'] == ['ok'] else 'NOT OK: ' + '; '.join(db['quick_check'])}; "
        f"{len(db['migrations'])} migrations, the last {db['migrations'][-1] if db['migrations'] else '—'}")
    add("Integrations: " + "; ".join(f"{i['Name']} ({i['AdapterType']}, {i['Kind']}, {'on' if i['IsEnabled'] else 'off'})"
                                     for i in db["integrations"]))
    add("Allowed folders: " + (", ".join(db["allowed_roots"]) or "none"))
    add("Path mappings: " + ("; ".join(f"{m['integration']}: {m['service_prefix']} -> {m['app_prefix']}"
                                       for m in db["path_mappings"]) or "none"))
    add("")
    add("Library syncs (newest first):")
    for r in db["sync_runs"]:
        add(f"  {r['CreatedUtc']} {r['IntegrationName']}: {r['Status']}, {r['StartedUtc']} -> {r['FinishedUtc']}, "
            f"items {r['ItemsRead']}/{r['ItemsTotal']}, works {r['WorksCount']}, series {r['SeriesCount']}, "
            f"versions {r['VersionsCount']}, files {r['FilesCount']}{', error ' + r['Error'] if r['Error'] else ''} "
            f"(by {r['RequestedByUserName']})")
    add("Folder scans (newest first):" if db["scan_runs"] else "Folder scans: none ever ran.")
    for r in db["scan_runs"]:
        add(f"  {r['CreatedUtc']}: {r['Status']}, {r['StartedUtc']} -> {r['FinishedUtc']}, found {r['FilesFound']}, "
            f"in no library {r['FilesUnreported']}, problems {r['ProblemCount']}"
            f"{' (' + r['Problem'] + ')' if r['Problem'] else ''}{', error ' + r['Error'] if r['Error'] else ''}")
    add("File inspections (newest first):")
    for r in db["inspection_runs"]:
        add(f"  {r['CreatedUtc']} {r['Trigger']}: {r['Status']}, {r['StartedUtc']} -> {r['FinishedUtc']}, "
            f"checked {r['FilesChecked']}/{r['FilesTotal']}, ffprobe ran on {r['FilesProbed']}; hashing: candidates "
            f"{r['HashCandidates']}, checked {r['HashCandidatesChecked']}, read whole {r['HashCandidatesRead']}, "
            f"problems {r['HashProblemCount']}{', error ' + r['Error'] if r['Error'] else ''}")
    add("")
    f = db["files"]
    for title, key in (("All names", "all"), ("Library names", "library"), ("Names only the scan found", "scanned_only")):
        s = f[key]
        add(f"{title}: {s['names']:,} names, {s['measured_names']:,} measured, {s['unmeasured_names']:,} not measured; "
            f"{s['files']:,} files (device+inode) take {size_text(s['bytes'])}")
    add(f"  library-reported size of the library names: {size_text(f['library']['library_reported_bytes'])}")
    add(f"  distinct paths: {f['distinct_paths']:,}")
    for loc in f["by_location"]:
        add(f"  {loc['integration']} / {loc['location']}: {loc['names']:,} names, {loc['files']:,} files, "
            f"{size_text(loc['bytes'])}, not measured {loc['unmeasured_names']}")
    add(f"Link counts: {f['link_count']}")
    add(f"Names whose file has more than one link: {len(f['names_with_links_above_one'])}")
    for path in f["names_with_links_above_one"][:LIST_LIMIT]:
        add(f"  {path}")
    add(f"Files the app knows under two paths or more: {len(f['files_with_two_known_paths'])}")
    for paths in f["files_with_two_known_paths"][:LIST_LIMIT]:
        add(f"  {' | '.join(paths)}")
    add(f"Sizes two files or more share: {len(f['sizes_shared_by_two_files'])}")
    for item in f["sizes_shared_by_two_files"][:LIST_LIMIT]:
        add(f"  {item['size']:,} B: " + " || ".join(" | ".join(p) for p in item["files"]))
    add(f"Devices: " + "; ".join(f"{d['device']} ({d['file_system'] or 'type unknown'}): {d['names']:,} names"
                                 for d in f["devices"]))
    add(f"Inspection status: {f['inspection_status']}")
    add(f"Names with an inspection error: {len(f['inspection_errors'])}")
    for item in f["inspection_errors"][:LIST_LIMIT]:
        add(f"  {item['status']}: {item['path']} — {item['error']}")
    add(f"Library names whose read size differs from the library's: {len(f['library_size_differs_from_read'])}")
    for item in f["library_size_differs_from_read"][:LIST_LIMIT]:
        add(f"  {item['path']}: library {item['library']:,}, read {item['read']:,}")
    add(f"Names only the scan found: {len(f['scanned_only_paths'])}")
    for path in f["scanned_only_paths"][:LIST_LIMIT]:
        add(f"  {path}")
    add(f"Hashes: {db['hashes']['rows']} rows, {db['hashes']['for_a_current_name']} of a current name; readings "
        f"{db['readings']['rows']:,} ({db['readings']['with_error']} with an error)")
    add("Video dynamic ranges: " + ", ".join(f"{r['DynamicRange']}: {r['n']}" for r in db["dynamic_ranges"]))
    add("Works: " + ", ".join(f"{r['Kind']} {r['n']:,}" for r in db["works"])
        + f"; works with two versions holding files or more: {db['works_with_two_copies']}")
    add("TMDb links by state: " + (", ".join(f"{r['WriteState']} {r['n']}" for r in db["links"]) or "none"))
    queued = sum(r["n"] for r in db["links"] if r["WriteState"] == "Queued")
    add(f"  Queued links (would be sent to Jellyfin at the next signal): {queued}")
    if db["decisions"] is None:
        add("Duplicate decisions: the table doesn't exist (a database older than 0.2.1).")
    else:
        add(f"Duplicate decisions: {len(db['decisions'])}")
        for d in db["decisions"]:
            add(f"  {d['GroupKey']}: {d['Decision']} by {d['DecidedByUserName']} at {d['DecidedUtc']}")
    add(f"Audit rows: {len(db['audit_ids']):,}; by action: "
        + ", ".join(f"{r['Action']} {r['n']}" for r in db["audit_by_action"]))
    for r in db["audit_latest"][:10]:
        add(f"  {r['CreatedUtc']} {r['Action']} by {r['ActorUserName']}: {r['TargetKind']} {r['TargetName']}")
    cmp = report.get("comparison")
    if cmp:
        add("")
        add("Compared with report-before.json:")
        add(f"  names in both {cmp['names_in_both']:,} (measured in both {cmp['measured_in_both']:,}); only before "
            f"{len(cmp['only_before'])}, only after {len(cmp['only_after'])}; changed {len(cmp['changed'])}")
        for key in cmp["only_before"][:LIST_LIMIT]:
            add(f"  - {key}")
        for key in cmp["only_after"][:LIST_LIMIT]:
            add(f"  + {key}")
        for item in cmp["changed"][:LIST_LIMIT]:
            add(f"  ~ {item['key']}: {'; '.join(item['changes'])}")
        add(f"  new runs: sync {len(cmp['new_runs']['sync'])}, scan {len(cmp['new_runs']['scan'])}, "
            f"inspection {len(cmp['new_runs']['inspection'])}")
        add(f"  new audit rows {cmp['new_audit_rows']}, audit rows gone {cmp['audit_rows_gone']}")
        for r in cmp["new_audit_latest"]:
            add(f"    {r['CreatedUtc']} {r['Action']} by {r['ActorUserName']}: {r['TargetKind']} {r['TargetName']}")
        add(f"  links before {cmp['links_before']}; after {cmp['links_after']}")
        add(f"  decisions before {cmp['decisions_before']}; after {cmp['decisions_after']}")
    return "\n".join(out) + "\n"


# ---------------------------------------------------------------- main

def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="MAM-22: read-only report of production's MAM database and container.")
    parser.add_argument("label", choices=("before", "after"), help="before or after the live check")
    parser.add_argument("--ssh-host", default=common.DEFAULT_SSH_HOST, help=f"default {common.DEFAULT_SSH_HOST}")
    parser.add_argument("--db", type=Path, help="read this local mam.db (and its -wal/-shm) instead; no ssh, no facts")
    parser.add_argument("--out-dir", type=Path, default=OUT_DIR, help=f"where the reports go; default {OUT_DIR}")
    return parser.parse_args(argv)


def run(args: argparse.Namespace) -> int:
    out_dir = args.out_dir
    copy_dir = out_dir / "db-copy"
    out_dir.mkdir(parents=True, exist_ok=True)
    if args.db:
        copy_local(args.db, copy_dir)
        source = str(args.db)
    else:
        fetch(args.ssh_host, copy_dir)
        source = f"{args.ssh_host}, container {common.MAM_CONTAINER}"
    facts = None
    facts_path = copy_dir / FACTS_FILE
    if facts_path.is_file():
        facts = parse_facts(facts_path.read_text(encoding="utf-8", errors="replace"))
    db = Database(copy_dir / "mam.db")
    try:
        database = read_database(db, facts)
    finally:
        db.close()
        remove_copy(copy_dir)
    report = {"label": args.label, "source": source,
              "written_utc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
              "facts": facts, "database": database}
    before_path = out_dir / "report-before.json"
    if args.label == "after" and before_path.is_file():
        report["comparison"] = compare(json.loads(before_path.read_text(encoding="utf-8")), report)
    text_path = out_dir / f"report-{args.label}.txt"
    json_path = out_dir / f"report-{args.label}.json"
    text = render(report)
    text_path.write_text(text, encoding="utf-8")
    json_path.write_text(json.dumps(report, indent=1, ensure_ascii=False, default=str) + "\n", encoding="utf-8")
    say(text)
    say("The database copy was deleted.")
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
