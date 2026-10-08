r"""MAM-37 copy: production's MAM database copied to this PC and kept, so the MAM-37 build can be run on it locally and
its same-work groups read through GET /api/library/duplicates and on the Duplicates page, without touching production.

READ-ONLY ON PRODUCTION. The one ssh call is tools/mam22/report.py's, imported rather than repeated: read commands only
(docker inspect, docker exec cat / ffprobe -version, stat, cp of the database into a mktemp folder, tar).

Reads, over ssh (root@192.168.1.195 asks for its password itself):
  - a copy of mam.db, mam.db-wal and mam.db-shm from the MAM container's /data mount, never the .secrets folder;
  - the container's facts report.py reads with it: image, digest, start time, mounts, mount table, ffprobe -version.
Writes: only under D:\Sources\Media-Acquisition-Manager\.claude\mam37-check\ (gitignored), and keeps it there:
          data\         the local app's data directory: mam.db, with the -wal and -shm it came with (SQLite folds
                        the -wal in when the copy is prepared);
          facts.txt     the container's facts at the time of the copy;
          baseline.json fingerprints of the tables the duplicates are read from, which --verify compares with.
        In the copy only, before the local app first opens it, two changes the local run needs:
          - the allowed media folders are removed. The app's start inspects every name under them, and /media is no
            path on Windows: every name would be written "outside the allowed folders", with the device, inode and
            size it read cleared, which drops the durations and identities the duplicates are grouped by;
          - each integration's address becomes http://127.0.0.1:9, so the local app reaches none of production's
            services whatever it tries (its new .secrets can't open their sealed keys either).
        GET /api/library/duplicates reads neither table. On the media host only report.py's mktemp -d folder, removed
        by the same command.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam37\copy_db.py
        python D:\Sources\Media-Acquisition-Manager\tools\mam37\copy_db.py --verify   (while or after the local app runs)
The copy holds password hashes and sealed keys: delete .claude\mam37-check\data when the check is done.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import sqlite3
import sys
from collections import Counter
from pathlib import Path

# Imported, not run, so Python would write report.py's bytecode beside it; tools/mam22 has no .gitignore for
# __pycache__ (tools/mam34 has), and the folder would show up in git status.
sys.dont_write_bytecode = True
# report.py of the MAM-22 check: its one ssh call (fetch), its --db copy (copy_local), its read-only Database and its
# facts parser. It puts tools/mam34 on the path itself, for common.py.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mam22"))
import report  # noqa: E402
import common  # noqa: E402
from common import ToolError  # noqa: E402

OUT_DIR = common.REPO_ROOT / ".claude" / "mam37-check"
DATA_FOLDER = "data"
BASELINE_FILE = "baseline.json"
RUNBOOK = OUT_DIR / "RUNBOOK.md"
API_PROJECT = common.REPO_ROOT / "src" / "MediaAcquisitionManager.Api"
FRONTEND = common.REPO_ROOT / "frontend"

# The discard address of the house rule for local checks: nothing listens there, so a call fails at once.
UNREACHABLE_URL = "http://127.0.0.1:9"

# What GET /api/library/duplicates is computed from (LibraryDuplicateService.Load.cs, the link reader, LibraryAppPaths),
# and LibraryItemLinkFiles, which an inspection's link pass adds to. None holds a credential; Users and
# IntegrationConnections, which do, are never read whole.
FINGERPRINTED_TABLES = (
    "PhysicalFiles",
    "MediaInspections",
    "MediaInspectionTracks",
    "FileHashes",
    "MediaVersions",
    "MediaWorks",
    "MediaWorkTitles",
    "MediaWorkIdentifiers",
    "MediaEpisodePositions",
    "LibraryItemLinks",
    "LibraryItemLinkFiles",
    "LibraryLocations",
    "PathMappings",
    "DuplicateMatchDecisions",
)

# The background work a start picks up from the rows (the reconcilers, the link writer, the download tracker).
PENDING_RUNS = ("FileInspectionRuns", "FolderScanRuns", "LibrarySyncRuns", "RecommendationRuns", "LibraryLinkSuggestionRuns")
FOLLOWED_DOWNLOADS = ("Created", "Submitted", "Downloading", "Paused")  # DownloadJobFollowing.FollowedStatuses


def say(text: str = "") -> None:
    print(text, flush=True)


# ---------------------------------------------------------------- the copy

def existing_copy(data_dir: Path) -> list[str]:
    return [name for name in report.DATABASE_FILES if (data_dir / name).exists()]


def take_copy(args: argparse.Namespace, out_dir: Path, data_dir: Path) -> str:
    """Copies the database into `data_dir` and moves the container's facts beside it; returns where it came from."""
    if args.db:
        if args.db.resolve() == (data_dir / "mam.db").resolve():
            raise ToolError("--db names the copy itself; point it at another mam.db.")
        report.copy_local(args.db, data_dir)
        # A local file has no container: facts an earlier ssh copy left would be read as this copy's.
        (out_dir / report.FACTS_FILE).unlink(missing_ok=True)
        return str(args.db)

    # fetch clears the folder only once ssh has answered, so a failed ssh leaves an earlier copy as it was.
    report.fetch(args.ssh_host, data_dir)
    facts = data_dir / report.FACTS_FILE
    if facts.is_file():
        # Out of the data directory: the app has no use for it there, and the folder stays the database alone.
        os.replace(facts, out_dir / report.FACTS_FILE)
    return f"{args.ssh_host}, container {common.MAM_CONTAINER}"


def prepare(database: Path) -> dict:
    """The two changes the local run needs, made in the copy in one transaction (see the module's docstring); returns
    what they replaced, for the record."""
    try:
        connection = sqlite3.connect(database, timeout=10)
    except sqlite3.Error as error:
        raise ToolError(f"The copy cannot be opened to prepare it: {error}.") from None
    try:
        with connection:
            roots = [row[0] for row in connection.execute("SELECT Path FROM AllowedRoots ORDER BY Position")]
            addresses = [{"name": row[0], "address": row[1]} for row in
                         connection.execute("SELECT Name, BaseUrl FROM IntegrationConnections ORDER BY Name")]
            connection.execute("DELETE FROM AllowedRoots")
            connection.execute("UPDATE IntegrationConnections SET BaseUrl = ?", (UNREACHABLE_URL,))
    except sqlite3.Error as error:
        raise ToolError(f"Preparing the copy failed: {error}. The copy is deleted; run the script again.") from None
    finally:
        connection.close()
    return {"allowed_roots_removed": roots, "integration_addresses_replaced": addresses}


# ---------------------------------------------------------------- what the copy says

def fingerprint(db: report.Database, table: str) -> dict:
    """The table's rows in a stable order, hashed: any written column of any row changes it (an inspection rewrites
    InspectedUtc on every name it looks at, whatever it finds)."""
    if not db.has(table):
        return {"rows": None, "sha256": None}
    rows = db.select(f'SELECT * FROM "{table}"')
    lines = sorted(json.dumps(row, sort_keys=True, ensure_ascii=False, default=str) for row in rows)
    return {"rows": len(rows), "sha256": hashlib.sha256("\n".join(lines).encode("utf-8")).hexdigest()}


def read_state(db: report.Database) -> dict:
    status = Counter(row["InspectionStatus"] for row in db.select("SELECT InspectionStatus FROM PhysicalFiles"))
    # LibraryDuplicateService.Load.cs's rule for a name's duration: its last look read the media, and the reading is
    # no cached failure.
    measured = db.select(
        "SELECT COUNT(*) AS n FROM PhysicalFiles f JOIN MediaInspections m ON m.Id = f.MediaInspectionId "
        "WHERE f.InspectionStatus = 'Inspected' AND m.Error IS NULL AND m.DurationMilliseconds IS NOT NULL")[0]["n"]
    pending = {}
    for table in PENDING_RUNS:
        if db.has(table):
            rows = db.select(f'SELECT Status, COUNT(*) AS n FROM "{table}" '
                             "WHERE Status IN ('Queued', 'Running') GROUP BY Status ORDER BY Status")
            pending[table] = {row["Status"]: row["n"] for row in rows}
    links = db.select("SELECT WriteState, COUNT(*) AS n FROM LibraryItemLinks "
                      "WHERE WriteState IN ('Queued', 'Writing') GROUP BY WriteState") if db.has("LibraryItemLinks") else []
    marks = ", ".join("?" for _ in FOLLOWED_DOWNLOADS)
    followed = db.select(f"SELECT COUNT(*) AS n FROM DownloadJobs WHERE Status IN ({marks})",
                         FOLLOWED_DOWNLOADS)[0]["n"] if db.has("DownloadJobs") else 0
    return {
        "quick_check": [str(row[next(iter(row))]) for row in db.select("PRAGMA quick_check")],
        "migrations": [row["MigrationId"] for row in
                       db.select('SELECT MigrationId FROM "__EFMigrationsHistory" ORDER BY MigrationId')],
        "allowed_roots": [row["Path"] for row in db.select("SELECT Path FROM AllowedRoots ORDER BY Position")],
        "integration_addresses": sorted({row["BaseUrl"] for row in db.select("SELECT BaseUrl FROM IntegrationConnections")}),
        "names": sum(status.values()),
        "names_by_inspection_status": dict(sorted((str(key), value) for key, value in status.items())),
        "names_with_a_measured_duration": measured,
        "pending_runs": pending,
        "links_queued_or_writing": {row["WriteState"]: row["n"] for row in links},
        "followed_downloads": followed,
        "tables": {table: fingerprint(db, table) for table in FINGERPRINTED_TABLES},
    }


def open_copy(data_dir: Path) -> report.Database:
    database = data_dir / "mam.db"
    if not database.is_file():
        raise ToolError(f"There is no copy in {data_dir}; run the script without --verify first.")
    return report.Database(database)


# ---------------------------------------------------------------- the text

def count_text(value: int | None) -> str:
    return "-" if value is None else f"{value:,}"


def render_state(state: dict) -> list[str]:
    lines = [
        f"Database: quick_check {'ok' if state['quick_check'] == ['ok'] else 'NOT OK: ' + '; '.join(state['quick_check'])}; "
        f"{len(state['migrations'])} migrations, the last {state['migrations'][-1] if state['migrations'] else '-'}",
        f"Names: {state['names']:,}; by inspection status: "
        + ", ".join(f"{key} {value:,}" for key, value in state["names_by_inspection_status"].items()),
        f"Names with a measured duration (Inspected, a reading without an error): {state['names_with_a_measured_duration']:,}",
        "Allowed folders in the copy: " + (", ".join(state["allowed_roots"]) or "none"),
        "Integration addresses in the copy: " + (", ".join(state["integration_addresses"]) or "none"),
    ]
    pending = "; ".join(f"{table} {counts}" for table, counts in state["pending_runs"].items() if counts) or "none"
    lines.append(f"Runs a start picks up (queued or running): {pending}")
    lines.append(f"TMDb links queued or being written: {state['links_queued_or_writing'] or 'none'}; "
                 f"download jobs followed: {state['followed_downloads']}")
    return lines


def render_next(out_dir: Path, data_dir: Path) -> list[str]:
    elsewhere = "" if out_dir == OUT_DIR.resolve() else f" --out {out_dir}"
    return [
        f"Next (the details are in {RUNBOOK}):",
        "  1. Stop any MAM API on port 5080 (the \"api\" preview or a dotnet run).",
        # The launch profile's Development settings log every SQL command, and the download tracker queries every 2 s.
        f"  2. dotnet run --project {API_PROJECT} -c Release -- --Storage:DataDirectory={data_dir} "
        "--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Warning",
        f"  3. npm --prefix {FRONTEND} run dev, open http://localhost:5173/library/duplicates and sign in with your "
        "production user name and password.",
        f"  4. python {Path(__file__).resolve()} --verify{elsewhere}",
        f"The copy holds password hashes and sealed keys: delete {data_dir} when the check is done.",
    ]


# ---------------------------------------------------------------- the two modes

def copy(args: argparse.Namespace) -> int:
    out_dir = args.out.resolve()
    data_dir = out_dir / DATA_FOLDER
    found = existing_copy(data_dir)
    if found and not args.force:
        raise ToolError(f"A copy is already in {data_dir} ({', '.join(found)}). Run with --force to replace it (stop the "
                        "local app first), or delete that folder.")
    out_dir.mkdir(parents=True, exist_ok=True)
    source = take_copy(args, out_dir, data_dir)
    sizes = {name: (data_dir / name).stat().st_size for name in existing_copy(data_dir)}
    try:
        prepared = prepare(data_dir / "mam.db")
    except ToolError:
        # Never left behind unprepared: a start on it would inspect /media from Windows and clear what is checked.
        report.remove_copy(data_dir)
        raise

    db = open_copy(data_dir)
    try:
        state = read_state(db)
    finally:
        db.close()
    facts_path = out_dir / report.FACTS_FILE
    facts = report.parse_facts(facts_path.read_text(encoding="utf-8", errors="replace")) if facts_path.is_file() else None
    baseline = {"source": source, "copied_utc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "copied_sizes": sizes, "facts": facts, "prepared": prepared, "state": state}
    (out_dir / BASELINE_FILE).write_text(json.dumps(baseline, indent=1, ensure_ascii=False, default=str) + "\n",
                                         encoding="utf-8")

    say(f"MAM-37 copy of {source}, taken {baseline['copied_utc']}, kept in {data_dir}")
    say("Copied: " + ", ".join(f"{name} ({size:,} B)" for name, size in sizes.items()))
    if facts:
        container = facts.get("container", {})
        say(f"Production at the time: image {container.get('image')} ({container.get('image_id')}), "
            f"{container.get('status')}, started {container.get('started_at')}; read on the host at {facts.get('read_utc')}")
        for digest in facts.get("digests", []):
            say(f"  digest {digest}")
    say("Prepared in the copy only: allowed folders removed ("
        + (", ".join(prepared["allowed_roots_removed"]) or "there were none")
        + f"); integration addresses set to {UNREACHABLE_URL} ("
        + (", ".join(f"{item['name']}: {item['address']}" for item in prepared["integration_addresses_replaced"]) or "none")
        + ")")
    for line in render_state(state):
        say(line)
    say(f"Baseline written: {out_dir / BASELINE_FILE}")
    say()
    for line in render_next(out_dir, data_dir):
        say(line)
    return 0


def verify(args: argparse.Namespace) -> int:
    out_dir = args.out.resolve()
    data_dir = out_dir / DATA_FOLDER
    baseline_path = out_dir / BASELINE_FILE
    if not baseline_path.is_file():
        raise ToolError(f"There is no {baseline_path}; run the script without --verify first.")
    before = json.loads(baseline_path.read_text(encoding="utf-8"))["state"]
    db = open_copy(data_dir)
    try:
        now = read_state(db)
    finally:
        db.close()

    say(f"MAM-37 copy in {data_dir}, compared with {baseline_path}:")
    changed = []
    for table in FINGERPRINTED_TABLES:
        old, new = before["tables"].get(table), now["tables"][table]
        if old == new:
            say(f"  {table}: unchanged ({new['rows']:,} rows)" if new["rows"] is not None else f"  {table}: not in the copy")
        else:
            changed.append(table)
            say(f"  {table}: CHANGED ({count_text(old['rows'] if old else None)} rows before, {count_text(new['rows'])} now)")
    if before["names_by_inspection_status"] != now["names_by_inspection_status"]:
        say(f"  Names by inspection status: before {before['names_by_inspection_status']}, now "
            f"{now['names_by_inspection_status']}")
    if before["names_with_a_measured_duration"] != now["names_with_a_measured_duration"]:
        say(f"  Names with a measured duration: before {before['names_with_a_measured_duration']:,}, now "
            f"{now['names_with_a_measured_duration']:,}")
    added = [migration for migration in now["migrations"] if migration not in before["migrations"]]
    say("  Migrations the local app applied: " + (", ".join(added) or "none"))
    if now["allowed_roots"]:
        changed.append("AllowedRoots")
        say("  Allowed folders: CHANGED, the copy allows " + ", ".join(now["allowed_roots"])
            + " again; the next start inspects them. Stop the app and take a new copy.")
    if now["integration_addresses"] not in ([], [UNREACHABLE_URL]):
        changed.append("IntegrationConnections")
        say("  Integration addresses: CHANGED to " + ", ".join(now["integration_addresses"]))
    say()
    if changed or added:
        say("The duplicates the local app serves may no longer be production's: " + ", ".join(changed + added)
            + ". Stop the app, take a new copy with --force and start again.")
        return 1
    say("Unchanged: the local app serves the duplicates from production's data as it was copied.")
    return 0


# ---------------------------------------------------------------- main

def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="MAM-37: copy production's MAM database to this PC for a local run of the MAM-37 build (read-only on "
                    "production; the copy is kept and prepared for the local run).")
    parser.add_argument("--ssh-host", default=common.DEFAULT_SSH_HOST, help=f"default {common.DEFAULT_SSH_HOST}")
    parser.add_argument("--db", type=Path, help="copy this local mam.db (and its -wal/-shm) instead; no ssh, no facts")
    parser.add_argument("--out", type=Path, default=OUT_DIR,
                        help=f"the check's folder; the copy goes to its {DATA_FOLDER} folder. Default {OUT_DIR}")
    parser.add_argument("--force", action="store_true", help="replace a copy already in the folder")
    parser.add_argument("--verify", action="store_true",
                        help="copy nothing: compare the copy with its baseline (the local app may be running)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    common.setup_console()
    args = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        return verify(args) if args.verify else copy(args)
    except ToolError as error:
        say(f"ERROR: {error}")
        return 2
    except OSError as error:
        say(f"ERROR: {error}")
        return 2
    except KeyboardInterrupt:
        say("Interrupted. Nothing was changed on production.")
        return 130


if __name__ == "__main__":
    sys.exit(main())
