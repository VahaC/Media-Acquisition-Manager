r"""MAM-34 inventory: for every Jellyfin item MAM wrote into or asked to refresh, "item -> what MAM wrote -> what is there now".

STRICTLY READ-ONLY. It changes nothing that matters anywhere: Jellyfin gets GET requests only (the client is created
read-only, so any other method raises before a byte is sent), the MAM database is opened read-only (SQLite mode=ro)
on a copy, and the media host runs read commands only (docker inspect, stat, cp, tar).

Reads:
  - The MAM database in the container's /data volume, over ssh (root@192.168.1.195 asks for its password itself): a
    copy of mam.db, mam.db-wal and mam.db-shm and nothing else, never the .secrets folder. The copy holds password
    hashes and sealed keys; the script selects only the columns it names and never prints or exports those.
  - Jellyfin (asks for its address and, with hidden input, its API key): version, plugins, libraries and their options,
    every item MAM linked, and a name search for the known cases.
Writes: only under D:\Sources\Media-Acquisition-Manager\.claude\mam34-check\ (gitignored): inventory.txt, inventory.json
        and mam-copy\ (the database copy; deleted at the end unless --keep-copy). Nothing is written to Jellyfin or to
        MAM, and the API key is not written anywhere. On the media host the one thing it creates is a throw-away
        folder from mktemp -d that holds the copy while it is streamed back; the same command removes it.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam34\inventory.py

When it ends, tell Claude about inventory.txt and inventory.json (the last line names them).
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
import unicodedata
from collections import Counter
from pathlib import Path

import common
from common import JellyfinError, ToolError

# A pause between Jellyfin requests: the item reads run back to back for a few hundred items, and this is a
# household server that is also playing something.
REQUEST_PAUSE_SECONDS = 0.05
# Three failed reads in a row mean Jellyfin stopped answering; going on would only print the same error N times.
MAX_CONSECUTIVE_READ_FAILURES = 3
# What a name search asks for beside the defaults: the same fields the library sync recorded on the live server.
LIST_FIELDS = "Path,ProviderIds,OriginalTitle,DateCreated"
MAX_SEARCH_HITS = 8

DATABASE_FILES = ("mam.db", "mam.db-wal", "mam.db-shm")

# The order a request records its audit rows in (AuditAction's numbers): rows of one instant are told apart by it,
# exactly as the app's LibraryLinkWriteHistory does.
ACTION_ORDER = {
    "LibraryLinkSet": 17,
    "LibraryLinkReplaced": 18,
    "LibraryLinkRemoved": 19,
    "LibraryIdsWritten": 20,
    "LibraryIdsRestored": 21,
    "LibraryMetadataLookupRequested": 22,
}
TMDB_SCHEMES = ("Tmdb", "TmdbTv")
WRITTEN_STATES = ("Written", "InLibrary")
SENT_STATES = ("Written", "InLibrary", "Lost", "NotWritten")
DISPLAY_STATES = ("Written", "InLibrary", "Lost")
# Provider ids MAM writes (as Jellyfin's Tmdb and Imdb keys); every other key is Jellyfin's own doing.
MAM_PROVIDER_KEYS = ("tmdb", "imdb")
REFRESH_EXTRA_KEYS = ("tvdb", "tvdbslug", "tmdbcollection")

KNOWN_CASES = (
    # (what the owner calls it, search terms tried in order, Jellyfin item type)
    ("Landing Stripling", ("Landing Stripling",), "Movie"),
    ("Penguins of Madagascar", ("Penguins of Madagascar", "Penguins"), "Series"),
    ("Blitzkrieg", ("Blitzkrieg",), "Movie"),
)

GROUP_IDENTIFY = "needs Identify"
GROUP_FINE = "already shows TMDb's name (probably fine, verify)"
GROUP_MISTAKE = "evident mistake - show to owner first"
GROUP_BLOCKED = "cannot be identified (kept in app / library writes files / locked / episode filed as a movie)"
GROUP_UNREAD = "could not be read from Jellyfin (see the error)"
GROUP_ORDER = (GROUP_MISTAKE, GROUP_IDENTIFY, GROUP_BLOCKED, GROUP_FINE, GROUP_UNREAD)

FLAG_ORDER = (
    "ITEM_GONE", "HIDDEN_FROM_ADMIN", "READ_FAILED", "ID_MISMATCH", "FOREIGN_ID_SUSPECT", "DISPLAY_UNCHANGED",
    "NO_OVERVIEW", "NO_PRIMARY_IMAGE", "EXTRA_IDS_FROM_REFRESH", "OLD_REFRESH", "KEPT_IN_APP", "EPISODE_MOVIE",
    "SERIES", "LOCKED", "LIBRARY_WRITES_FILES", "UNATTACHED",
)
FLAG_HELP = {
    "ITEM_GONE": "Jellyfin answers 404 for the item (and a user-less list does not find it either).",
    "HIDDEN_FROM_ADMIN": "404 for the administrator account, but a user-less list finds the item (a parental limit or "
                         "a blocked tag): its details below are the list's, which are fewer.",
    "READ_FAILED": "The item could not be read; the error is in the row.",
    "ID_MISMATCH": "Jellyfin's Tmdb is not the link's value for a link that claims Written/InLibrary, or Jellyfin's "
                   "Imdb differs from the link's ImdbId / EpisodeImdbId.",
    "FOREIGN_ID_SUSPECT": "Jellyfin's Tmdb is neither the link's value nor anything MAM wrote or saw in the item: "
                          "someone or something else put it there.",
    "DISPLAY_UNCHANGED": "Kind Movie/Series, state Written/InLibrary/Lost, and Jellyfin's Name is not TMDb's title: "
                         "the item still shows its own name (the original bug). The note says whether only the "
                         "numbering, year or tags differ.",
    "NO_OVERVIEW": "Jellyfin holds no overview for the item.",
    "NO_PRIMARY_IMAGE": "Jellyfin holds no Primary image for the item.",
    "EXTRA_IDS_FROM_REFRESH": "Jellyfin holds provider ids other than Tmdb and Imdb (Tvdb, TvdbSlug, TmdbCollection...) "
                              "that no audit row wrote: a refresh or Jellyfin's own providers added them, and "
                              "Identify's Apply replaces ALL provider ids.",
    "OLD_REFRESH": "MAM asked Jellyfin for 'Search for missing metadata' for this item (link.LookupRequestedUtc or a "
                   "LibraryMetadataLookupRequested audit row).",
    "KEPT_IN_APP": "The link was never written into Jellyfin (state KeptInApp); WriteError says why.",
    "EPISODE_MOVIE": "Target ShowEpisode, or a Jellyfin Movie linked to a TMDb TV id: an episode filed as a movie. "
                     "Identify by TMDb id would match a film, not the episode.",
    "SERIES": "A series: the Identify is synchronous per episode (minutes), so it needs the background Identify.",
    "LOCKED": "LockData or LockedFields: Jellyfin will not overwrite the locked parts.",
    "LIBRARY_WRITES_FILES": "The item's library saves metadata files beside the media (SaveLocalMetadata, or a metadata saver "
                            "such as Nfo in MetadataSavers).",
    "UNATTACHED": "MediaWorkId is null: the last sync found no item for the link.",
}


# ---------------------------------------------------------------- small helpers

def guid_key(value) -> str:
    """A GUID as a comparison key: SQLite stores EF Core's as upper-case text with dashes, Jellyfin's are 32 lower-case hex."""
    return str(value or "").replace("-", "").strip().lower()


_TIME = re.compile(r"(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?")


def parse_time(text) -> dt.datetime | None:
    """EF Core writes UTC as 'yyyy-MM-dd HH:mm:ss.fffffff' (7 fraction digits, which fromisoformat of 3.10 refuses)."""
    match = _TIME.match(str(text or ""))
    if not match:
        return None
    year, month, day, hour, minute, second = (int(part) for part in match.groups()[:6])
    micro = int((match.group(7) or "0")[:6].ljust(6, "0"))
    return dt.datetime(year, month, day, hour, minute, second, micro, tzinfo=dt.timezone.utc)


def iso(moment: dt.datetime | None) -> str | None:
    return moment.strftime("%Y-%m-%dT%H:%M:%SZ") if moment else None


def show_time(moment_or_text) -> str:
    moment = moment_or_text if isinstance(moment_or_text, dt.datetime) else parse_time(moment_or_text)
    return moment.strftime("%Y-%m-%d %H:%M:%SZ") if moment else "-"


def cut(text, length: int) -> str:
    text = " ".join(str(text or "").split())
    return text if len(text) <= length else text[: max(0, length - 3)] + "..."


def loose_title(text: str) -> str:
    """A title compared as it is written: only Unicode form, apostrophe variants, case and spacing are ignored.
    common.normalize_title also drops numbering, year and tags, which is what tells TMDb's "Landing Stripling" from the
    file-derived "120. Landing Stripling" Jellyfin still shows."""
    text = unicodedata.normalize("NFC", text or "").translate(
        str.maketrans({"\u2018": "'", "\u2019": "'", "\u02bc": "'", "\u00b4": "'", "`": "'"}))
    return " ".join(text.split()).casefold()


def provider_id(provider_ids: dict, key: str) -> str | None:
    for name, value in (provider_ids or {}).items():
        if str(name).lower() == key.lower() and value not in (None, ""):
            return str(value).strip()
    return None


def same_id(first, second) -> bool:
    return str(first or "").strip().lower() == str(second or "").strip().lower()


def say(text: str = "") -> None:
    print(text, flush=True)


# ---------------------------------------------------------------- Step A: the MAM database

# One ssh call, run by `sh` on the media host. It is sent base64-encoded so no quoting of the Windows command line can
# change it. It only reads: docker inspect, cp of three files into a fresh mktemp folder, tar to stdout, rm of that folder.
# The copy counts as consistent when the size and the full-resolution mtime (%y, not the whole seconds of %Y) of mam.db and
# mam.db-wal are the same before and after it: a WAL that restarts rewrites its frames in place at the same size, and
# only the time tells. The -shm is left out of that comparison: it is an index SQLite rebuilds from the WAL, and it
# changes whenever a reader or writer touches the database, which would fail the check for no reason.
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
files=mam.db
[ -f "$tmp/mam.db-wal" ] && files="$files mam.db-wal"
[ -f "$tmp/mam.db-shm" ] && files="$files mam.db-shm"
tar -C "$tmp" -cf - $files
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


def remove_copy(folder: Path) -> list[str]:
    """Deletes the database copy's three files (never anything else) and the folder if that leaves it empty. Returns the
    names it could not delete, and says so: the copy holds password hashes and sealed keys, so "deleted" must be true."""
    left = []
    for name in DATABASE_FILES:
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


def unpack_database_tar(data: bytes, folder: Path) -> None:
    """Writes the three database files of a tar stream into `folder`. The archive comes from another machine, so every
    member must be one of the three names, a plain file, and land directly in `folder`: nothing else is ever written."""
    try:
        archive = tarfile.open(fileobj=io.BytesIO(data), mode="r:")
    except tarfile.TarError:
        raise ToolError("The media host's answer is not a tar stream. Something in its login scripts may print to "
                        "stdout; the first bytes were: " + repr(data[:80])) from None
    with archive:
        wanted: dict[str, tarfile.TarInfo] = {}
        try:
            members = archive.getmembers()
        except (tarfile.TarError, EOFError):
            # An answer cut short (a dropped connection that still ended with exit code 0) fails here, not above.
            raise ToolError("The tar stream from the media host is cut short; run the script again.") from None
        for member in members:
            name = member.name[2:] if member.name.startswith("./") else member.name
            if name not in DATABASE_FILES or not member.isreg():
                raise ToolError(f"Refusing the unexpected tar member {member.name!r}: only {', '.join(DATABASE_FILES)} "
                                "are accepted.")
            wanted[name] = member
        if "mam.db" not in wanted:
            raise ToolError("The tar stream from the media host holds no mam.db.")
        folder.mkdir(parents=True, exist_ok=True)
        root = folder.resolve()
        for name, member in wanted.items():
            target = (folder / name).resolve()
            if target.parent != root:  # belt and braces: the name check above already allows no separator
                raise ToolError(f"Refusing to write {name!r} outside {folder}.")
            try:
                handle = archive.extractfile(member)
                content = handle.read() if handle else b""
            except (tarfile.TarError, EOFError):
                raise ToolError("The tar stream from the media host is cut short; run the script again.") from None
            target.write_bytes(content)


def fetch_database(ssh_host: str, folder: Path) -> None:
    say(f"Step A: copying the MAM database from {ssh_host} (container {common.MAM_CONTAINER}, its /data mount)...")
    script = REMOTE_SCRIPT.replace("@CONTAINER@", shlex.quote(common.MAM_CONTAINER))
    encoded = base64.b64encode(script.encode("utf-8")).decode("ascii")
    # ssh's own messages and the remote script's reasons stay on the console (see common.ssh_run), above this line.
    code, out, _ = common.ssh_run(ssh_host, f"echo {encoded} | base64 -d | sh", timeout=300)
    if code != 0:
        raise ToolError(f"The copy failed (ssh exit code {code}): "
                        + REMOTE_FAILURES.get(code, "see the lines above") + ".")
    if remove_copy(folder):  # a stale -wal beside a new mam.db would be replayed over it
        raise ToolError(f"The previous database copy in {folder} could not be cleared; delete that folder and run again.")
    unpack_database_tar(out, folder)
    say(f"  copied {', '.join(name for name in DATABASE_FILES if (folder / name).exists())} "
        f"({sum((folder / n).stat().st_size for n in DATABASE_FILES if (folder / n).exists()) // 1024} KB) into {folder}")


def copy_given_database(source: Path, folder: Path) -> bool:
    """--db: the owner's copy is never opened in place, because opening a WAL database can create -wal and -shm files
    beside it. It is copied into the work folder like the one fetched over ssh. Returns whether the folder is ours to
    delete (False when the owner pointed at a file already in it)."""
    if not source.is_file():
        raise ToolError(f"--db {source}: no such file.")
    if source.resolve().parent == folder.resolve():
        say(f"Step A: reading the database {source} in place (it is already in the work folder; it is not deleted).")
        return False
    say(f"Step A: reading a copy of {source} (no ssh).")
    folder.mkdir(parents=True, exist_ok=True)
    if remove_copy(folder):
        raise ToolError(f"The previous database copy in {folder} could not be cleared; delete that folder and run again.")
    folder.mkdir(parents=True, exist_ok=True)
    for name in DATABASE_FILES:
        side = source.with_name(source.name + name[len("mam.db"):]) if name != "mam.db" else source
        if side.is_file():
            try:
                shutil.copyfile(side, folder / name)
            except OSError as error:
                # The caller only owns the folder once this returns, so a half copy (it holds password hashes) must go here.
                remove_copy(folder)
                raise ToolError(f"Copying {side} failed: {error}") from None
    return True


class MamDatabase:
    """The copy of mam.db, opened read-only. Every statement goes through select(), which refuses anything but a SELECT
    or the two read-only pragmas the script uses, so even a typo could not write."""

    def __init__(self, path: Path) -> None:
        # mode=ro, never immutable=1: immutable ignores the -wal file, and the rows that matter may still be in it.
        uri = path.resolve().as_uri() + "?mode=ro"
        try:
            self.connection = sqlite3.connect(uri, uri=True, timeout=10)
            self.connection.row_factory = sqlite3.Row
            self.connection.execute("PRAGMA query_only = ON")
        except sqlite3.Error as error:
            raise ToolError(f"The database copy cannot be opened: {error}. Run the script again.") from None

    def select(self, sql: str, params=()) -> list[dict]:
        head = " ".join(sql.split()).lower()
        if not head.startswith(("select ", "pragma table_info(", "pragma quick_check")):
            raise ToolError(f"Refusing a statement that is not a read: {sql[:40]!r}")
        try:
            return [dict(row) for row in self.connection.execute(sql, params)]
        except sqlite3.Error as error:
            raise ToolError(f"Reading the database copy failed: {error}. If the copy was taken while MAM was "
                            "writing, run the script again.") from None

    def quick_check(self) -> list[str]:
        """PRAGMA quick_check's lines ("ok" alone when the copy is sound). A copy of a live WAL database taken while MAM
        was writing can be torn: SQLite may then refuse even to run the check."""
        try:
            return [str(row[0]) for row in self.connection.execute("PRAGMA quick_check")]
        except sqlite3.Error as error:
            raise ToolError(f"PRAGMA quick_check is NOT ok: it could not run on the copy ({error}). The copy was probably "
                            "taken while MAM was writing; run the script again.") from None

    def columns(self, table: str) -> list[str]:
        if not re.fullmatch(r"\w+", table):
            raise ToolError(f"Not a table name: {table!r}")
        return [row["name"] for row in self.select(f'PRAGMA table_info("{table}")')]

    def close(self) -> None:
        self.connection.close()


# The columns read from LibraryItemLinks. The first group exists since AddLibraryLinks; the second only since
# AddLibraryLinkLookups (20261006023045), so an older database lacks it and reads as null.
LINK_COLUMNS = ("Id", "IntegrationConnectionId", "SourceItemId", "ItemKind", "MediaWorkId", "Target", "Scheme", "Value",
                "SeasonNumber", "EpisodeNumber", "CatalogTitle", "ItemTitle", "ImdbId", "SupersededValue",
                "SetByUserName", "SetUtc", "BatchId", "WriteState", "WriteError", "WriteStateUtc", "CreatedUtc")
LINK_LOOKUP_COLUMNS = ("EpisodeImdbId", "LookupRequestedUtc", "LookupError")


def read_mam(db: MamDatabase) -> dict:
    """Everything the inventory needs from the database, as plain dicts. Only named, non-secret columns are selected: the
    copy also holds Users.PasswordHash, UserSessions' token hashes and IntegrationConnections' sealed credentials."""
    notes: list[str] = []
    values = db.quick_check()
    integrity_ok = values == ["ok"]
    if not integrity_ok:
        notes.append("PRAGMA quick_check is NOT ok (" + cut("; ".join(values), 200) + "): a copy of a live WAL database "
                     "can be inconsistent. Run the script again; use the figures below with care.")
    tables = {row["name"] for row in db.select("SELECT name FROM sqlite_master WHERE type = 'table'")}
    migrations = ([row["MigrationId"] for row in db.select('SELECT MigrationId FROM "__EFMigrationsHistory" ORDER BY MigrationId')]
                  if "__EFMigrationsHistory" in tables else [])

    connections = {}
    if "IntegrationConnections" in tables:
        for row in db.select("SELECT Id, Name, AdapterType, Kind, IsEnabled FROM IntegrationConnections"):
            connections[guid_key(row["Id"])] = {"id": row["Id"], "name": row["Name"],
                                                "adapter": row["AdapterType"], "kind": row["Kind"],
                                                "enabled": bool(row["IsEnabled"])}

    links: list[dict] = []
    lookup_columns = False
    if "LibraryItemLinks" in tables:
        present = set(db.columns("LibraryItemLinks"))
        missing = [name for name in LINK_COLUMNS if name not in present]
        if missing:
            raise ToolError("LibraryItemLinks has no column " + ", ".join(missing) + ": the script is older than "
                            "the database. Tell Claude.")
        lookup_columns = all(name in present for name in LINK_LOOKUP_COLUMNS)
        if not lookup_columns:
            notes.append("The database predates migration AddLibraryLinkLookups: EpisodeImdbId, LookupRequestedUtc "
                         "and LookupError do not exist and read as empty.")
        selected = [name for name in (*LINK_COLUMNS, *LINK_LOOKUP_COLUMNS) if name in present]
        sql = "SELECT " + ", ".join(f'"{name}"' for name in selected) + ' FROM "LibraryItemLinks" ORDER BY SetUtc, Id'
        for row in db.select(sql):
            links.append(row)
    else:
        notes.append("The database has no LibraryItemLinks table: no TMDb link was ever saved (migration "
                     "AddLibraryLinks not applied).")

    events: list[dict] = []
    if "AuditEvents" in tables:
        identifiers: dict[str, list[dict]] = {}
        if "AuditEventIdentifiers" in tables:
            for row in db.select("SELECT i.AuditEventId, i.Role, i.Scheme, i.Value, i.Position FROM AuditEventIdentifiers i "
                                 "JOIN AuditEvents e ON e.Id = i.AuditEventId WHERE e.TargetKind = 'LibraryLink' "
                                 "ORDER BY i.AuditEventId, i.Position"):
                identifiers.setdefault(guid_key(row["AuditEventId"]), []).append(row)
        for row in db.select("SELECT Id, Action, ActorUserName, TargetKind, TargetId, TargetName, CreatedUtc FROM AuditEvents "
                             "WHERE TargetKind = 'LibraryLink'"):
            held = identifiers.get(guid_key(row["Id"]), [])
            at = parse_time(row["CreatedUtc"])
            events.append({
                "id": guid_key(row["Id"]), "id_text": row["Id"], "action": row["Action"], "actor": row["ActorUserName"],
                "link_id": guid_key(row["TargetId"]), "link_id_text": row["TargetId"], "target_name": row["TargetName"],
                "at": at,
                "before": [{"scheme": i["Scheme"], "value": i["Value"]} for i in held if i["Role"] == "Before"],
                "after": [{"scheme": i["Scheme"], "value": i["Value"]} for i in held if i["Role"] == "After"],
            })
        events.sort(key=lambda e: (e["at"] or dt.datetime.min.replace(tzinfo=dt.timezone.utc),
                                   ACTION_ORDER.get(e["action"], 99), e["id"]))
    else:
        notes.append("The database has no AuditEvents table.")

    return {"integrity_ok": integrity_ok, "quick_check": values, "migrations": migrations,
            "lookup_columns": lookup_columns, "connections": connections, "links": links, "events": events,
            "notes": notes}


# ---------------------------------------------------------------- what MAM wrote, from the audit rows

def scheme_changes(before: list[dict], after: list[dict]) -> list[tuple[str, str | None, str | None]]:
    """(scheme, value before, value after) for every scheme an audit row left otherwise than it found it, the way the
    app's LibraryLinkWriteHistory reads a row: a scheme the row only lists in one role counts as added or removed."""
    def value_of(rows, scheme):
        return next((r["value"] for r in rows if r["scheme"] == scheme), None)

    schemes = list(dict.fromkeys([r["scheme"] for r in after] + [r["scheme"] for r in before]))
    return [(s, value_of(before, s), value_of(after, s)) for s in schemes
            if not same_id(value_of(before, s), value_of(after, s))]


def fmt_ids(rows: list[dict]) -> str:
    return ", ".join(f"{r['scheme']}={r['value']}" for r in rows) or "(none)"


def fmt_changes(before: list[dict], after: list[dict]) -> str:
    changes = scheme_changes(before, after)
    if not changes:
        return "no id changed (after: " + fmt_ids(after) + ")"
    return "; ".join(f"{s} {b or '-'} -> {a or '-'}" for s, b, a in changes)


def event_view(event: dict) -> dict:
    return {"id": event["id_text"], "action": event["action"], "at": iso(event["at"]), "actor": event["actor"],
            "target_name": event["target_name"], "before": event["before"], "after": event["after"]}


def summarize_history(events: list[dict]) -> dict:
    """The audit trail of one link: what it was set to, what MAM wrote, whether a refresh was asked or a restore made."""
    saves = [e for e in events if e["action"] in ("LibraryLinkSet", "LibraryLinkReplaced")]
    writes = [e for e in events if e["action"] == "LibraryIdsWritten"]
    lookups = [e for e in events if e["action"] == "LibraryMetadataLookupRequested"]
    restores = [e for e in events if e["action"] == "LibraryIdsRestored"]
    removed = [e for e in events if e["action"] == "LibraryLinkRemoved"]

    # A restore put the item back as MAM found it, so only the writes after the last one say what MAM left in it now (the
    # app's LibraryLinkWriteHistory reads the rows the same way); without this a replaced link would list an Imdb id
    # that was taken out again.
    last_restore = max((i for i, e in enumerate(events) if e["action"] == "LibraryIdsRestored"), default=-1)
    wrote: dict[str, str | None] = {}
    for write in (e for e in events[last_restore + 1:] if e["action"] == "LibraryIdsWritten"):
        # oldest first, so the newest change of a scheme wins (the app's LastWritten)
        for scheme, _before, after in scheme_changes(write["before"], write["after"]):
            wrote[scheme] = after

    # Tmdb values MAM set or wrote (the link over time and what its writes left), and the ones the library held that MAM
    # only saw. Both are "known" to FOREIGN_ID_SUSPECT.
    authored, held = set(), set()
    for event in saves + writes:
        for row in event["after"]:
            if row["scheme"] in TMDB_SCHEMES:
                authored.add(str(row["value"]).strip().lower())
        for row in event["before"]:
            if row["scheme"] in TMDB_SCHEMES:
                held.add(str(row["value"]).strip().lower())
    written_schemes = {row["scheme"] for write in writes for row in write["after"]}

    return {
        "events": [event_view(e) for e in events],
        "last_save": event_view(saves[-1]) if saves else None,
        "library_held_when_saved": saves[-1]["before"] if saves else None,
        "first_write": event_view(writes[0]) if writes else None,
        "last_write": event_view(writes[-1]) if writes else None,
        "write_rows": len(writes),
        "wrote": wrote,
        "refresh_requests": [iso(e["at"]) for e in lookups],
        "restores": [iso(e["at"]) for e in restores],
        "removed": [iso(e["at"]) for e in removed],
        "_authored_tmdb": authored,
        "_held_tmdb": held,
        "_written_schemes": written_schemes,
    }


# ---------------------------------------------------------------- Step B: Jellyfin

def describe_library(folder: dict) -> dict:
    options = folder.get("LibraryOptions") or {}
    savers = options.get("MetadataSavers")
    reasons = []
    if options.get("SaveLocalMetadata") is True:
        reasons.append("SaveLocalMetadata is true" + (" and MetadataSavers is null (Jellyfin then uses its default savers)"
                                                      if savers is None else ""))
    if isinstance(savers, list) and savers:
        # Jellyfin runs every saver the list names, whatever SaveLocalMetadata says; Nfo is the one it ships with, but a
        # plugin's saver writes beside the media just as well.
        reasons.append("MetadataSavers contains " + ", ".join(f'"{saver}"' for saver in savers))
    languages = options.get("SubtitleDownloadLanguages") or []
    if languages:
        subtitles = ("subtitle downloads are ON for " + ", ".join(map(str, languages))
                     + (" and are saved beside the media (SaveSubtitlesWithMedia)" if options.get("SaveSubtitlesWithMedia")
                        else " and kept in Jellyfin's own folder"))
    else:
        subtitles = "no subtitle download languages (nothing is downloaded)"
    return {
        "name": folder.get("Name"), "item_id": folder.get("ItemId"), "collection_type": folder.get("CollectionType"),
        "locations": folder.get("Locations") or [],
        "SaveLocalMetadata": options.get("SaveLocalMetadata"), "MetadataSavers": savers,
        "LocalMetadataReaderOrder": options.get("LocalMetadataReaderOrder"),
        "SubtitleDownloadLanguages": options.get("SubtitleDownloadLanguages"),
        "SaveSubtitlesWithMedia": options.get("SaveSubtitlesWithMedia"),
        "SaveLyricsWithMedia": options.get("SaveLyricsWithMedia"),
        "SaveTrickplayWithMedia": options.get("SaveTrickplayWithMedia"),
        "PreferredMetadataLanguage": options.get("PreferredMetadataLanguage"),
        "MetadataCountryCode": options.get("MetadataCountryCode"),
        "type_options": [{"Type": t.get("Type"), "MetadataFetchers": t.get("MetadataFetchers"),
                          "ImageFetchers": t.get("ImageFetchers")} for t in options.get("TypeOptions") or []],
        "writes_files": bool(reasons), "writes_files_reasons": reasons, "subtitles": subtitles,
        "linked_items": 0,
    }


def summarize_item(raw: dict, complete: bool) -> dict:
    """The columns of "what is there now". `complete` is False for an item taken from a list, which lacks the overview
    and the lock fields: those read as unknown (None), never as absent."""
    provider_ids = {str(k): str(v) for k, v in (raw.get("ProviderIds") or {}).items() if v not in (None, "")}
    overview = raw.get("Overview")
    images = sorted((raw.get("ImageTags") or {}).keys())
    return {
        "id": raw.get("Id"), "name": raw.get("Name"), "original_title": raw.get("OriginalTitle"),
        "year": raw.get("ProductionYear"), "type": raw.get("Type"), "path": raw.get("Path"),
        "provider_ids": provider_ids,
        "has_overview": (bool(isinstance(overview, str) and overview.strip()) if complete else None),
        "overview_chars": len(overview) if isinstance(overview, str) else 0,
        "image_tags": images, "backdrop_count": len(raw.get("BackdropImageTags") or []),
        "lock_data": (bool(raw.get("LockData")) if complete else None),
        "locked_fields": (list(raw.get("LockedFields") or []) if complete else None),
        "date_created": raw.get("DateCreated"), "child_count": raw.get("ChildCount"),
        "recursive_item_count": raw.get("RecursiveItemCount"),
    }


def search_items(jf: common.Jellyfin, term: str, item_types: str, limit: int = 25) -> list[dict]:
    """A name search the way the library sync lists, read-only. The field list is the one recorded on the live server; if
    this Jellyfin still answers 400 the search is repeated with none."""
    query = {"searchTerm": term, "recursive": True, "includeItemTypes": item_types, "collapseBoxSetItems": False,
             "fields": LIST_FIELDS, "limit": limit, "enableUserData": False}
    reply = jf.list_items(query)
    if reply.status == 400:
        reply = jf.list_items({k: v for k, v in query.items() if k != "fields"})
    time.sleep(REQUEST_PAUSE_SECONDS)
    if not reply.ok or not isinstance(reply.json, dict):
        raise JellyfinError(f"The search for {term!r} answered HTTP {reply.status}.")
    return [item for item in reply.json.get("Items") or [] if isinstance(item, dict)]


def count_children(jf: common.Jellyfin, parent_id: str, item_type: str) -> int | None:
    # limit=1, not 0: a limit of 0 may mean "no limit" and would send every episode; the total is in the envelope either way.
    reply = jf.list_items({"parentId": parent_id, "recursive": True, "includeItemTypes": item_type, "limit": 1,
                           "enableImages": False, "enableUserData": False, "enableTotalRecordCount": True})
    time.sleep(REQUEST_PAUSE_SECONDS)
    if reply.ok and isinstance(reply.json, dict) and isinstance(reply.json.get("TotalRecordCount"), int):
        return reply.json["TotalRecordCount"]
    return None


def read_item(jf: common.Jellyfin, item_id: str, folders: list[dict]) -> dict:
    """One item as Jellyfin has it now. Transport failures raise JellyfinError (the caller counts them); an HTTP status
    becomes the row's `status`: ok, gone, hidden_from_admin, bad_id or error."""
    try:
        item_id = common.guid32(item_id)
    except JellyfinError as error:
        return {"status": "bad_id", "http": None, "error": str(error)}
    reply = jf.item(item_id)
    time.sleep(REQUEST_PAUSE_SECONDS)
    if reply.status == 200 and isinstance(reply.json, dict):
        info = summarize_item(reply.json, complete=True)
        info.update(status="ok", http=200, error=None)
        if info["type"] == "Series":
            info["seasons"] = count_children(jf, item_id, "Season")
            info["episodes"] = count_children(jf, item_id, "Episode")
    elif reply.status == 404:
        # The administrator may simply not be allowed to see it (the app tells the two apart the same way).
        listed = jf.list_items({"ids": item_id, "recursive": True, "collapseBoxSetItems": False, "fields": LIST_FIELDS})
        time.sleep(REQUEST_PAUSE_SECONDS)
        found = [i for i in (listed.json or {}).get("Items", []) if isinstance(i, dict)] if listed.ok and isinstance(listed.json, dict) else []
        if not listed.ok:
            # An item is "gone" only when the list answered and did not hold it; a refused list says nothing about it.
            info = {"status": "error", "http": listed.status,
                    "error": f"404 for the administrator, and the user-less list answered HTTP {listed.status}"}
        elif found:
            info = summarize_item(found[0], complete=False)
            info.update(status="hidden_from_admin", http=404,
                        error="404 for the administrator, but a user-less list finds the item")
        else:
            info = {"status": "gone", "http": 404, "error": "404 Not Found"}
    else:
        info = {"status": "error", "http": reply.status, "error": f"HTTP {reply.status} {reply.reason}: {cut(reply.text, 160)}"}
    path = info.get("path")
    library = common.library_for_path(folders, path) if path else None
    info["library"] = library.get("Name") if library else None
    return info


# ---------------------------------------------------------------- Step C: the rows and their flags

def build_records(mam: dict, jellyfin_by_link: dict[str, dict], folders: list[dict], libraries: dict[str, dict]) -> list[dict]:
    events_by_link: dict[str, list[dict]] = {}
    for event in mam["events"]:
        events_by_link.setdefault(event["link_id"], []).append(event)

    records = []
    for number, row in enumerate(mam["links"], start=1):
        link_id = guid_key(row["Id"])
        history = summarize_history(events_by_link.get(link_id, []))
        connection = mam["connections"].get(guid_key(row["IntegrationConnectionId"]))
        record = {
            # link_id is the id as the database stores it (upper case, dashes), so the repair plan can use it in SQL.
            "n": number, "link_id": row["Id"], "item_id": row["SourceItemId"],
            "integration": connection or {"id": row["IntegrationConnectionId"], "name": None, "adapter": None},
            "kind": row["ItemKind"], "target": row["Target"], "state": row["WriteState"],
            "write_error": row["WriteError"], "write_state_utc": iso(parse_time(row["WriteStateUtc"])),
            "set_by": row["SetByUserName"], "set_utc": iso(parse_time(row["SetUtc"])),
            "link": {
                "scheme": row["Scheme"], "value": row["Value"], "imdb_id": row["ImdbId"],
                "episode_imdb_id": row.get("EpisodeImdbId"), "season": row["SeasonNumber"], "episode": row["EpisodeNumber"],
                "catalog_title": row["CatalogTitle"], "item_title": row["ItemTitle"],
                "superseded_value": row["SupersededValue"], "unattached": row["MediaWorkId"] is None,
                "batch": row["BatchId"] is not None,
                "lookup_requested_utc": iso(parse_time(row.get("LookupRequestedUtc"))), "lookup_error": row.get("LookupError"),
            },
            "name_equal_exact": None, "name_equal_normalized": None,
            "mam": {k: v for k, v in history.items() if not k.startswith("_")},
            "jellyfin": jellyfin_by_link.get(link_id) or {"status": "not_read", "error": "not read"},
            "_history": history,
        }
        evaluate(record, libraries)
        del record["_history"]
        records.append(record)
    return records


def evaluate(record: dict, libraries: dict[str, dict]) -> None:
    """Sets record["flags"], ["flag_notes"], ["group"] and ["group_why"]. See FLAG_HELP for what each flag means."""
    link, jf, history = record["link"], record["jellyfin"], record["_history"]
    state, kind, target = record["state"], record["kind"], record["target"]
    flags: dict[str, str] = {}
    status = jf.get("status")

    if status == "gone":
        flags["ITEM_GONE"] = jf.get("error") or "404"
    elif status == "hidden_from_admin":
        flags["HIDDEN_FROM_ADMIN"] = jf.get("error") or "404 for the administrator"
    elif status != "ok":
        flags["READ_FAILED"] = jf.get("error") or "not read"

    if state == "KeptInApp":
        flags["KEPT_IN_APP"] = record["write_error"] or "no reason recorded"
    if link["unattached"]:
        flags["UNATTACHED"] = "MediaWorkId is null"
    if link["lookup_requested_utc"] or history["refresh_requests"]:
        when = history["refresh_requests"][-1] if history["refresh_requests"] else link["lookup_requested_utc"]
        flags["OLD_REFRESH"] = (f"a metadata lookup was asked for at {when}"
                                + (f"; the library did not take it: {link['lookup_error']}" if link["lookup_error"] else ""))

    if status in ("ok", "hidden_from_admin"):
        ids = jf["provider_ids"]
        jf_tmdb, jf_imdb = provider_id(ids, "tmdb"), provider_id(ids, "imdb")
        library_info = libraries.get(jf.get("library") or "")
        item_type = jf.get("type")

        if target == "ShowEpisode" or (item_type == "Movie" and link["scheme"] == "TmdbTv"):
            flags["EPISODE_MOVIE"] = (f"Target {target}, Jellyfin type {item_type}, TMDb show {link['value']}"
                                      + (f" S{link['season']}E{link['episode']}" if link["season"] is not None else ""))
        if kind == "Series" or item_type == "Series":
            flags["SERIES"] = "Jellyfin series" + (f" with {jf['seasons']} seasons and {jf['episodes']} episodes"
                                                    if jf.get("episodes") is not None else "")
        locked = (["LockData"] if jf.get("lock_data") else []) + (
            ["LockedFields=" + ",".join(jf["locked_fields"])] if jf.get("locked_fields") else [])
        if locked:
            flags["LOCKED"] = " ".join(locked)
        if library_info and library_info["writes_files"]:
            flags["LIBRARY_WRITES_FILES"] = f"{library_info['name']}: " + "; ".join(library_info["writes_files_reasons"])
        if jf.get("has_overview") is False:
            flags["NO_OVERVIEW"] = "no Overview"
        if "Primary" not in jf["image_tags"] and status == "ok":
            flags["NO_PRIMARY_IMAGE"] = "ImageTags: " + (", ".join(jf["image_tags"]) or "none")

        # DISPLAY_UNCHANGED: the name Jellyfin shows against TMDb's title kept on the link.
        if kind in ("Movie", "Series") and state in DISPLAY_STATES and link["catalog_title"] and status == "ok":
            name = jf.get("name") or ""
            record["name_equal_exact"] = loose_title(name) == loose_title(link["catalog_title"])
            record["name_equal_normalized"] = common.normalize_title(name) == common.normalize_title(link["catalog_title"])
            if not record["name_equal_exact"]:
                flags["DISPLAY_UNCHANGED"] = (
                    f"Jellyfin shows {name!r}, TMDb's title is {link['catalog_title']!r}"
                    + (" (equal once numbering, year and tags are dropped: the item shows the name taken from its file)"
                       if record["name_equal_normalized"] else " (a different title, or TMDb's in another language: verify)"))

        # ID_MISMATCH and FOREIGN_ID_SUSPECT.
        mismatches, conflict = [], False
        if target == "Work" and state in WRITTEN_STATES and not same_id(jf_tmdb, link["value"]):
            mismatches.append(f"Tmdb: Jellyfin {jf_tmdb or 'none'}, link {link['value']}")
            conflict |= bool(jf_tmdb)
        expected_imdb = link["imdb_id"] if target == "Work" else link["episode_imdb_id"]
        if expected_imdb and state not in ("Queued", "Writing"):
            if jf_imdb and not same_id(jf_imdb, expected_imdb):
                mismatches.append(f"Imdb: Jellyfin {jf_imdb}, link {expected_imdb}")
                conflict = True
            elif not jf_imdb and state in WRITTEN_STATES:
                mismatches.append(f"Imdb: Jellyfin none, link {expected_imdb}")
        if mismatches:
            flags["ID_MISMATCH"] = "; ".join(mismatches)
            record["id_mismatch_conflict"] = conflict
        if (target == "Work" and jf_tmdb and (history["write_rows"] or state in SENT_STATES)):
            known = {str(link["value"]).strip().lower()} | history["_authored_tmdb"] | history["_held_tmdb"]
            if link["superseded_value"]:
                known.add(str(link["superseded_value"]).strip().lower())
            if jf_tmdb.lower() not in known:
                flags["FOREIGN_ID_SUSPECT"] = (f"Jellyfin's Tmdb {jf_tmdb} is not the link's {link['value']} and appears "
                                               "in no audit row of this link")

        extras = [k for k in ids if k.lower() not in MAM_PROVIDER_KEYS
                  and not (k.lower() == "tvdb" and "Tvdb" in history["_written_schemes"])]
        if extras:
            known_extras = [k for k in extras if k.lower() in REFRESH_EXTRA_KEYS]
            flags["EXTRA_IDS_FROM_REFRESH"] = ", ".join(f"{k}={ids[k]}" for k in extras) + (
                "" if known_extras == extras else " (beyond Tvdb/TvdbSlug/TmdbCollection: Jellyfin's own providers)")

    record["flags"] = [flag for flag in FLAG_ORDER if flag in flags]
    record["flag_notes"] = {flag: flags[flag] for flag in record["flags"]}
    record["group"], record["group_why"] = classify(record["flags"], state)
    if "ID_MISMATCH" in record["group_why"] and record.get("id_mismatch_conflict") is False:
        # Still shown to the owner first (the rule), but he should know nothing contradicts the link: an id is just missing.
        record["group_why"] = ["ID_MISMATCH (an id is missing in Jellyfin; none conflicts)" if why == "ID_MISMATCH" else why
                               for why in record["group_why"]]


def classify(flags: list[str], state: str) -> tuple[str, list[str]]:
    """The action the new Identify would need, by the first rule that holds. A mistake outranks a block: the owner sees
    the mistake first, and the other flags stay on the row."""
    if "READ_FAILED" in flags:
        return GROUP_UNREAD, ["READ_FAILED"]
    mistakes = [f for f in ("ITEM_GONE", "ID_MISMATCH", "FOREIGN_ID_SUSPECT") if f in flags]
    if mistakes:
        return GROUP_MISTAKE, mistakes
    blocks = [f for f in ("KEPT_IN_APP", "EPISODE_MOVIE", "LIBRARY_WRITES_FILES", "LOCKED", "HIDDEN_FROM_ADMIN") if f in flags]
    if blocks:
        return GROUP_BLOCKED, blocks
    needs = [f for f in ("DISPLAY_UNCHANGED", "NO_OVERVIEW", "NO_PRIMARY_IMAGE") if f in flags]
    if state in ("Queued", "Writing", "NotWritten", "Lost"):
        needs.append(f"state {state}")
    if needs:
        return GROUP_IDENTIFY, needs
    return GROUP_FINE, []


# ---------------------------------------------------------------- Step D and E

def link_summary(record: dict | None) -> dict | None:
    if record is None:
        return None
    return {"n": record["n"], "link_id": record["link_id"], "state": record["state"], "kind": record["kind"],
            "target": record["target"], "group": record["group"], "flags": record["flags"]}


def known_cases(jf: common.Jellyfin, folders: list[dict], by_item: dict[str, dict]) -> list[dict]:
    cases = []
    for title, terms, item_type in KNOWN_CASES:
        entry = {"case": title, "search_terms": list(terms), "item_type": item_type, "matched_term": None,
                 "hits": [], "more_hits": 0, "error": None}
        try:
            hits: list[dict] = []
            for term in terms:
                hits = search_items(jf, term, item_type)
                if hits:
                    entry["matched_term"] = term
                    break
            entry["more_hits"] = max(0, len(hits) - MAX_SEARCH_HITS)
            for hit in hits[:MAX_SEARCH_HITS]:
                now = read_item(jf, hit.get("Id", ""), folders)
                if now["status"] != "ok":  # the list's own fields are all there is
                    now = {**summarize_item(hit, complete=False), "status": now["status"], "http": now.get("http"),
                           "error": now.get("error"), "library": now.get("library")}
                entry["hits"].append({"now": now, "link": link_summary(by_item.get(guid_key(hit.get("Id"))))})
        except common.ReadOnlyError:
            raise  # a write attempt is a bug in this script: it must stop the run, not become a row's error text
        except JellyfinError as error:
            entry["error"] = str(error)
        cases.append(entry)
    return cases


def orphan_events(mam: dict) -> list[dict]:
    """Audit rows of links that no longer exist (removed, or taken away with their integration), grouped by link."""
    live = {guid_key(row["Id"]) for row in mam["links"]}
    groups: dict[str, list[dict]] = {}
    for event in mam["events"]:
        if event["link_id"] and event["link_id"] not in live:
            groups.setdefault(event["link_id"], []).append(event)
    return [{"link_id": events[0]["link_id_text"], "events": events} for events in groups.values()]


def search_orphan_candidates(jf: common.Jellyfin, orphan: dict, folders: list[dict]) -> list[dict]:
    """Best effort: items whose name matches what the removed link was called. A name match is a hint, not proof; a
    candidate whose Tmdb or Imdb equals an id the link wrote is marked."""
    written = {str(row["value"]).strip().lower() for e in orphan["events"] if e["action"] == "LibraryIdsWritten"
               for row in e["after"]}
    names = list(dict.fromkeys(e["target_name"] for e in orphan["events"] if e["target_name"]))
    found: dict[str, dict] = {}
    for name in names[:3]:
        if re.fullmatch(r"[0-9a-fA-F]{32}", name):  # TargetName falls back to the item id when a link has no title
            now = read_item(jf, name, folders)
            if now["status"] == "ok":
                found[guid_key(name)] = {**now, "matched_by": "the item id the audit row names"}
            continue
        term = common.normalize_title(name) or name
        for hit in search_items(jf, term, "Movie,Series"):
            found.setdefault(guid_key(hit.get("Id")), {
                **summarize_item(hit, complete=False), "status": "listed", "matched_by": f"name search {term!r}"})
    candidates = []
    for candidate in found.values():
        ids = candidate["provider_ids"]
        candidate["ids_match_written"] = any(same_id(v, w) for v in ids.values() for w in written)
        library = common.library_for_path(folders, candidate.get("path") or "") if candidate.get("path") else None
        candidate["library"] = library.get("Name") if library else candidate.get("library")
        candidates.append(candidate)
    candidates.sort(key=lambda c: (not c["ids_match_written"], str(c.get("name"))))
    return candidates


# ---------------------------------------------------------------- rendering

def title_of(record: dict) -> str:
    return record["jellyfin"].get("name") or record["link"]["item_title"] or record["link"]["catalog_title"] or record["item_id"]


def link_says(record: dict) -> str:
    link = record["link"]
    parts = [f"{link['scheme']} {link['value']}"]
    if link["catalog_title"]:
        parts.append(f"TMDb title {link['catalog_title']!r}")
    if link["imdb_id"]:
        parts.append(f"Imdb {link['imdb_id']}")
    if link["episode_imdb_id"]:
        parts.append(f"episode Imdb {link['episode_imdb_id']}")
    if link["season"] is not None:
        parts.append(f"S{link['season']}E{link['episode']}" if link["episode"] is not None else f"S{link['season']}")
    if link["superseded_value"]:
        parts.append(f"replaces {link['superseded_value']}")
    return ", ".join(parts)


def now_lines(now: dict, indent: str) -> list[str]:
    """The "what is there now" columns, as lines."""
    if now["status"] in ("gone", "bad_id", "error", "not_read"):
        return [f"{indent}Jellyfin now: {now['status'].upper()} - {now.get('error')}"]
    ids = now.get("provider_ids") or {}
    head = f"{indent}Jellyfin now: {now.get('name')!r}"
    if now.get("year"):
        head += f" ({now['year']})"
    head += f" [{now.get('type')}]"
    if now.get("original_title"):
        head += f" original {now['original_title']!r}"
    images = ", ".join(now.get("image_tags") or []) or "none"
    if now.get("backdrop_count"):
        images += f", Backdrop x{now['backdrop_count']}"
    overview = {None: "unknown (list view)", True: f"yes ({now.get('overview_chars')} chars)", False: "no"}[now.get("has_overview")]
    lock = "unknown (list view)" if now.get("lock_data") is None else (
        "no" if not now["lock_data"] and not now["locked_fields"] else
        ("LockData " if now["lock_data"] else "") + ("LockedFields=" + ",".join(now["locked_fields"]) if now["locked_fields"] else ""))
    lines = [head,
             f"{indent}  ids: " + (", ".join(f"{k}={v}" for k, v in ids.items()) or "none"),
             f"{indent}  overview: {overview} | images: {images} | lock: {lock.strip()} | created: {now.get('date_created') or '-'}",
             f"{indent}  library: {now.get('library') or '?'} | path: {now.get('path') or '-'}"]
    if now.get("type") == "Series":
        lines.append(f"{indent}  seasons: {now.get('seasons')} episodes: {now.get('episodes')} "
                     f"(item says ChildCount {now.get('child_count')}, RecursiveItemCount {now.get('recursive_item_count')})")
    return lines


def mam_lines(record: dict, indent: str) -> list[str]:
    mam, link = record["mam"], record["link"]
    lines = []
    if mam["last_save"]:
        save = mam["last_save"]
        lines.append(f"{indent}saved:     {show_time(save['at'])} by {save['actor'] or '?'} ({save['action']}); the library held then: "
                     f"{fmt_ids(mam['library_held_when_saved'] or [])}; link: {fmt_ids(save['after'])}")
    else:
        lines.append(f"{indent}saved:     no LibraryLinkSet/Replaced audit row (link set {show_time(record['set_utc'])} by {record['set_by']})")
    if mam["first_write"]:
        first, last = mam["first_write"], mam["last_write"]
        lines.append(f"{indent}wrote:     {show_time(first['at'])} by {first['actor'] or '?'}: {fmt_changes(first['before'], first['after'])}")
        if last["id"] != first["id"]:
            lines.append(f"{indent}           ... {mam['write_rows']} write rows; last {show_time(last['at'])}: "
                         f"{fmt_changes(last['before'], last['after'])}")
        lines.append(f"{indent}           net written" + (" since the last restore" if mam["restores"] else "") + ": "
                     + (", ".join(f"{s}={v or '(removed)'}" for s, v in mam["wrote"].items()) or "nothing changed"))
    else:
        lines.append(f"{indent}wrote:     no LibraryIdsWritten audit row (nothing is known to have left the app)")
    refresh = (f"asked {len(mam['refresh_requests'])}x, last {show_time(mam['refresh_requests'][-1])}" if mam["refresh_requests"]
               else "no audit row")
    lines.append(f"{indent}refresh:   {refresh}; link.LookupRequestedUtc {show_time(link['lookup_requested_utc'])}"
                 + (f"; lookup error: {link['lookup_error']}" if link["lookup_error"] else ""))
    lines.append(f"{indent}restore:   " + (f"yes, last {show_time(mam['restores'][-1])}" if mam["restores"] else "no"))
    return lines


def record_block(record: dict) -> list[str]:
    jf = record["jellyfin"]
    head = (f"[{record['n']}] {cut(title_of(record), 90)}  |  {record['kind']}/{record['target']}  |  state {record['state']}"
            f"  |  library {jf.get('library') or '?'}")
    lines = [head,
             f"    link id {record['link_id']}  item {record['item_id']}  integration {record['integration'].get('name') or '?'}",
             f"    set by {record['set_by']} at {show_time(record['set_utc'])}; state since {show_time(record['write_state_utc'])}"
             + (f"; WriteError: {record['write_error']}" if record["write_error"] else ""),
             f"    link says: {link_says(record)}",
             "    MAM wrote:"]
    lines += mam_lines(record, "      ")
    lines += now_lines(jf, "    ")
    lines.append("    flags: " + (", ".join(record["flags"]) or "none"))
    for flag in record["flags"]:
        lines.append(f"      {flag}: {record['flag_notes'][flag]}")
    lines.append(f"    action: {record['group']}" + (f"  (because {', '.join(record['group_why'])})" if record["group_why"] else ""))
    return lines


def overview_table(records: list[dict]) -> list[str]:
    rows = [("#", "state", "kind/target", "library", "title", "flags")]
    for r in records:
        rows.append((str(r["n"]), r["state"], f"{r['kind']}/{r['target']}", cut(r["jellyfin"].get("library") or "?", 18),
                     cut(title_of(r), 38), ",".join(r["flags"]) or "-"))
    widths = [max(len(row[i]) for row in rows) for i in range(len(rows[0]) - 1)]
    return ["  ".join(cell.ljust(widths[i]) for i, cell in enumerate(row[:-1])) + "  " + row[-1] for row in rows]


def counts(records: list[dict], libraries: dict[str, dict]) -> dict:
    return {
        "links": len(records),
        "by_state": dict(Counter(r["state"] for r in records)),
        "by_kind": dict(Counter(f"{r['kind']}/{r['target']}" for r in records)),
        "by_library": dict(Counter(r["jellyfin"].get("library") or "?" for r in records)),
        "by_flag": dict(Counter(f for r in records for f in r["flags"])),
        "by_group": {g: sum(1 for r in records if r["group"] == g) for g in GROUP_ORDER},
    }


def short_line(record: dict) -> str:
    return (f"#{record['n']} {record['state']} {record['kind']}/{record['target']} {cut(title_of(record), 50)!r} "
            f"[{', '.join(record['flags']) or 'no flags'}]")


def render_text(report: dict) -> str:
    out: list[str] = []
    add = out.append
    meta, jfin, mam = report["meta"], report["jellyfin"], report["mam"]
    add("MAM-34 INVENTORY (read-only)")
    add(f"generated {meta['generated']}  |  Jellyfin {meta['jellyfin_url']}  {jfin['version']}  |  database: {meta['database_source']}")
    add(f"quick_check: {'ok' if mam['integrity_ok'] else 'NOT OK (' + '; '.join(mam['quick_check']) + ')'}  |  "
        f"migrations applied: {len(mam['migrations'])}, last {mam['migrations'][-1] if mam['migrations'] else '-'}  |  "
        f"lookup columns: {'yes' if mam['lookup_columns'] else 'NO'}")
    for note in mam["notes"]:
        add(f"NOTE: {note}")
    add("")
    add("== JELLYFIN ==")
    add(f"server {jfin['server_name']} {jfin['version']}; read as administrator {jfin['admin_user_id']}")
    add("plugins: " + (", ".join(jfin["plugins"]) or "none"))
    for lib in jfin["libraries"]:
        add(f"library {lib['name']!r} [{lib['collection_type'] or 'mixed'}] {', '.join(lib['locations'])}  "
            f"({lib['linked_items']} linked items)")
        add(f"    SaveLocalMetadata={lib['SaveLocalMetadata']} MetadataSavers={lib['MetadataSavers']} "
            f"LocalMetadataReaderOrder={lib['LocalMetadataReaderOrder']} PreferredMetadataLanguage={lib['PreferredMetadataLanguage']!r} "
            f"MetadataCountryCode={lib['MetadataCountryCode']!r}")
        add(f"    SubtitleDownloadLanguages={lib['SubtitleDownloadLanguages']} SaveSubtitlesWithMedia={lib['SaveSubtitlesWithMedia']} "
            f"SaveLyricsWithMedia={lib['SaveLyricsWithMedia']} SaveTrickplayWithMedia={lib['SaveTrickplayWithMedia']}")
        add("    " + ("WRITES FILES BESIDE MEDIA: " + "; ".join(lib["writes_files_reasons"]) if lib["writes_files"]
                      else "does not write metadata files beside the media") + f"; {lib['subtitles']}")
    add("")
    add("== COUNTS ==")
    for key, label in (("by_state", "state"), ("by_kind", "kind/target"), ("by_library", "library"), ("by_flag", "flag"), ("by_group", "needs")):
        add(f"{label}: " + (", ".join(f"{k} {v}" for k, v in report["counts"][key].items()) or "-"))
    add("")
    add("== LINKS (one line each; the detail follows) ==")
    out.extend(overview_table(report["links"]) if report["links"] else ["no links in the database"])
    add("")
    add("== BY THE ACTION THE NEW IDENTIFY WOULD NEED ==")
    for group in GROUP_ORDER:
        members = [r for r in report["links"] if r["group"] == group]
        add(f"-- {group}: {len(members)}")
        for r in members:
            add("   " + short_line(r) + (f"  <- {', '.join(r['group_why'])}" if r["group_why"] else ""))
    add("")
    add("== LINK DETAIL: item -> what MAM wrote -> what is there now ==")
    for r in report["links"]:
        out.extend(record_block(r))
        add("")
    add("== KNOWN CASES (searched by name in Jellyfin, whatever the links say) ==")
    for case in report["known_cases"]:
        add(f"-- {case['case']} ({case['item_type']}): searched {case['search_terms']}"
            + (f", matched {case['matched_term']!r}" if case["matched_term"] else ", no item found")
            + (f"; {case['more_hits']} more hits not shown" if case["more_hits"] else ""))
        if case["error"]:
            add(f"   ERROR: {case['error']}")
        for hit in case["hits"]:
            link = hit["link"]
            add("   " + (f"link row: #{link['n']} state {link['state']} ({link['group']})" if link else "link row: NONE (MAM never linked this item)"))
            out.extend(now_lines(hit["now"], "   "))
    add("")
    add("== ORPHANS: audit rows of links that no longer exist ==")
    if not report["orphans"]:
        add("none")
    for orphan in report["orphans"]:
        names = ", ".join(sorted({e['target_name'] for e in orphan["events"] if e["target_name"]}))
        add(f"-- removed link {orphan['link_id']}  ({names})")
        for e in orphan["events"]:
            add(f"   {show_time(e['at'])} {e['action']} by {e['actor'] or '?'}"
                + (f": {fmt_changes(e['before'], e['after'])}" if e["action"] != "LibraryMetadataLookupRequested" else " (a refresh was asked)"))
        if orphan.get("candidates") is not None:
            add("   Jellyfin items with a matching NAME (a name match is a hint, not proof):")
            for c in orphan["candidates"] or []:
                add(f"     {c.get('name')!r} ({c.get('year') or '-'}) [{c.get('type')}] {c.get('id')} ids: "
                    + (", ".join(f"{k}={v}" for k, v in c["provider_ids"].items()) or "none")
                    + (" <- its ids match what the removed link wrote" if c.get("ids_match_written") else "")
                    + f" ({c.get('matched_by')})")
            if not orphan["candidates"]:
                add("     none found")
        if orphan.get("search_error"):
            add(f"   search failed: {orphan['search_error']}")
    add("")
    add("== FLAGS ==")
    for flag in FLAG_ORDER:
        add(f"{flag}: {FLAG_HELP[flag]}")
    return "\n".join(out) + "\n"


def print_summary(report: dict, cap: int = 12) -> None:
    say()
    say("== SUMMARY ==")
    c = report["counts"]
    say(f"links: {c['links']}")
    for key, label in (("by_state", "by state"), ("by_kind", "by kind/target"), ("by_library", "by library"), ("by_flag", "by flag")):
        say(f"  {label}: " + (", ".join(f"{k} {v}" for k, v in c[key].items()) or "-"))
    for lib in report["jellyfin"]["libraries"]:
        if lib["writes_files"]:
            say(f"  library {lib['name']!r} WRITES FILES BESIDE MEDIA ({'; '.join(lib['writes_files_reasons'])}); {lib['linked_items']} linked items")
    say()
    for group in GROUP_ORDER:
        members = [r for r in report["links"] if r["group"] == group]
        if not members:
            continue
        say(f"-- {group}: {len(members)}")
        for r in members[:cap]:
            say("   " + short_line(r))
        if len(members) > cap:
            say(f"   ... and {len(members) - cap} more, see the file")
    say()
    say("known cases:")
    for case in report["known_cases"]:
        if case["error"]:
            say(f"  {case['case']}: search failed ({case['error']})")
            continue
        for hit in case["hits"][:3]:
            link = hit["link"]
            now = hit["now"]
            say(f"  {case['case']}: {cut(now.get('name'), 50)!r} [{now.get('type')}] ids {now.get('provider_ids') or 'none'}; "
                + (f"link #{link['n']} {link['state']}" if link else "no link row"))
        if not case["hits"]:
            say(f"  {case['case']}: not found in Jellyfin")
        if case["more_hits"]:
            say(f"  ... and {case['more_hits']} more hits for {case['case']}, see the file")
    orphans = report["orphans"]
    say(f"orphan audit trails (removed links): {len(orphans)}"
        + (f", of which {sum(1 for o in orphans if any(e['action'] == 'LibraryIdsWritten' for e in o['events']))} wrote ids" if orphans else ""))
    for orphan in orphans[:cap]:
        names = ", ".join(sorted({e['target_name'] for e in orphan["events"] if e["target_name"]}))
        say(f"   {cut(names, 60)!r}: " + ", ".join(e["action"] for e in orphan["events"])
            + (f"; {len(orphan.get('candidates') or [])} candidate item(s) by name" if orphan.get("candidates") is not None else ""))
    if len(orphans) > cap:
        say(f"   ... and {len(orphans) - cap} more, see the file")


def jsonable(value):
    """The report holds no datetimes or sets by the time it is written, but a stray one must not lose the whole run."""
    if isinstance(value, dt.datetime):
        return iso(value)
    if isinstance(value, (set, frozenset)):
        return sorted(value, key=str)
    raise TypeError(f"{type(value).__name__} is not JSON serializable")


# ---------------------------------------------------------------- main

def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Read-only inventory of what MAM wrote into Jellyfin and what Jellyfin shows now.",
        epilog=(f"The Jellyfin API key is asked with hidden input. For unattended tests only, it is taken from the "
                f"environment variable {common.ENV_JELLYFIN_KEY} when that is set."),
    )
    parser.add_argument("--jellyfin-url", help=f"Jellyfin address (default: asked, offering {common.DEFAULT_JELLYFIN_URL})")
    parser.add_argument("--ssh-host", default=common.DEFAULT_SSH_HOST,
                        help="the media host whose MAM container's database is copied (default: %(default)s)")
    parser.add_argument("--db", metavar="PATH", help="read this copy of mam.db (its -wal and -shm beside it are read too) "
                                                     "instead of copying one over ssh; the file itself is never opened in place")
    parser.add_argument("--keep-copy", action="store_true", help="keep the database copy under OUT_DIR/mam-copy "
                                                                 "(it holds password hashes and sealed keys; deleted by default)")
    parser.add_argument("--out", metavar="DIR", help=f"where inventory.txt and inventory.json go (default: {common.OUT_DIR})")
    return parser.parse_args(argv)


def run(args: argparse.Namespace) -> int:
    out_dir = (Path(args.out).expanduser() if args.out else common.OUT_DIR).resolve()
    copy_dir = out_dir / "mam-copy"
    say("MAM-34 inventory. Read-only: nothing is changed in Jellyfin or in MAM; the files it writes go to " + str(out_dir))
    base_url = args.jellyfin_url or common.ask("Jellyfin address", common.DEFAULT_JELLYFIN_URL)
    api_key = common.ask_secret("Jellyfin API key (typing is hidden)", common.ENV_JELLYFIN_KEY)
    if not api_key:
        raise ToolError("No Jellyfin API key was given.")
    jf = common.Jellyfin(base_url, api_key, timeout=30, read_only=True)

    # Jellyfin is asked first: a wrong key or address should fail before the ssh password is asked for.
    say(f"Checking Jellyfin at {jf.base_url} first (GET requests only)...")
    info_reply = jf.system_info()
    if info_reply.status in (401, 403):
        raise ToolError(f"Jellyfin refused the API key (HTTP {info_reply.status}).")
    info = info_reply.data()
    admin_id = jf.admin_user_id()
    say(f"  Jellyfin {info.get('Version')} ({info.get('ServerName')}); items are read as the first enabled administrator.")
    plugins_reply = jf.plugins()
    plugin_names = ([f"{p.get('Name')} {p.get('Version')}".strip() + (f" ({p.get('Status')})" if p.get("Status") else "")
                     for p in plugins_reply.json if isinstance(p, dict)]
                    if plugins_reply.ok and isinstance(plugins_reply.json, list) else [])
    say("  plugins: " + (", ".join(plugin_names) or "none (or not listed)"))
    folders = jf.virtual_folders().data()
    folders = folders if isinstance(folders, list) else []
    libraries = {lib["name"]: lib for lib in (describe_library(f) for f in folders)}
    for lib in libraries.values():
        say(f"  library {lib['name']!r}: " + ("WRITES FILES BESIDE MEDIA (" + "; ".join(lib["writes_files_reasons"]) + ")"
                                              if lib["writes_files"] else "no metadata files beside media") + f"; {lib['subtitles']}")

    # With --db the copy is ours only once copy_given_database says so: a mistyped path must not delete an earlier
    # --keep-copy folder.
    owned_copy = not args.db
    copy_left: list[str] = []
    db = None
    try:
        if args.db:
            owned_copy = copy_given_database(Path(args.db).expanduser(), copy_dir)
            database_source = f"{args.db} (a copy given with --db)"
        else:
            fetch_database(args.ssh_host, copy_dir)
            database_source = f"{args.ssh_host}, container {common.MAM_CONTAINER}, /data/mam.db"
        db = MamDatabase(copy_dir / "mam.db")
        mam = read_mam(db)
    finally:
        # The copy holds password hashes and sealed keys: it goes as soon as it is read, or as soon as anything fails.
        if db is not None:
            db.close()
        if owned_copy and not args.keep_copy:
            copy_left = remove_copy(copy_dir)
    say(f"  database: quick_check {'ok' if mam['integrity_ok'] else 'NOT OK'}; {len(mam['links'])} links, "
        f"{len(mam['events'])} audit rows about links")
    for note in mam["notes"]:
        say(f"  NOTE: {note}")
    jellyfin_connections = [c for c in mam["connections"].values() if (c["adapter"] or "").lower() == "jellyfin"]
    if len(jellyfin_connections) > 1:
        mam["notes"].append(f"{len(jellyfin_connections)} Jellyfin integrations exist; every link is read from the one Jellyfin "
                            "asked for here, so items of the others may show as gone.")
        say("  NOTE: " + mam["notes"][-1])

    links = mam["links"]
    say(f"Step B: reading {len(links)} linked item(s) from Jellyfin...")
    read: dict[str, dict] = {}
    failures = 0
    for index, row in enumerate(links, start=1):
        link_id = guid_key(row["Id"])
        label = cut(row["ItemTitle"] or row["CatalogTitle"] or row["SourceItemId"], 60)
        if failures >= MAX_CONSECUTIVE_READ_FAILURES:
            read[link_id] = {"status": "not_read", "http": None, "error": "Jellyfin stopped answering; not read"}
            continue
        try:
            read[link_id] = read_item(jf, row["SourceItemId"], folders)
        except common.ReadOnlyError:
            raise  # see known_cases
        except JellyfinError as error:
            read[link_id] = {"status": "error", "http": None, "error": str(error)}
        # A 401, a 5xx or a timeout three times running is Jellyfin (or the key) failing, not three bad items; a 404 is an answer.
        failures = failures + 1 if read[link_id]["status"] == "error" else 0
        say(f"  {index}/{len(links)}  {label}  -> {read[link_id]['status']}")
    if failures >= MAX_CONSECUTIVE_READ_FAILURES:
        say("  Jellyfin stopped answering; the remaining items were not read. Run the script again when it is back.")

    for item in read.values():
        if item.get("library") in libraries:
            libraries[item["library"]]["linked_items"] += 1
    say("Step C: building the table and the flags...")
    records = build_records(mam, read, folders, libraries)
    by_item = {guid_key(r["item_id"]): r for r in records}

    say("Step D: looking up the known cases by name...")
    cases = known_cases(jf, folders, by_item)

    say("Step E: looking for the traces of removed links...")
    orphans = []
    for orphan in orphan_events(mam):
        entry = {"link_id": orphan["link_id"], "events": [event_view(e) for e in orphan["events"]], "candidates": None}
        if any(e["action"] == "LibraryIdsWritten" for e in orphan["events"]):
            try:
                entry["candidates"] = search_orphan_candidates(jf, orphan, folders)
            except common.ReadOnlyError:
                raise  # see known_cases
            except JellyfinError as error:
                entry["search_error"] = str(error)
        orphans.append(entry)

    report = {
        "meta": {"generated": iso(dt.datetime.now(dt.timezone.utc)), "tool": "tools/mam34/inventory.py", "read_only": True,
                 "jellyfin_url": jf.base_url, "database_source": database_source},
        "jellyfin": {"version": info.get("Version"), "server_name": info.get("ServerName"), "admin_user_id": admin_id,
                     "plugins": plugin_names, "libraries": list(libraries.values())},
        "mam": {k: mam[k] for k in ("integrity_ok", "quick_check", "migrations", "lookup_columns", "notes")}
               | {"connections": list(mam["connections"].values())},
        "counts": counts(records, libraries),
        "links": records, "known_cases": cases, "orphans": orphans,
        "legend": FLAG_HELP,
    }
    # Written as text first, then scrubbed: the report holds no secret by construction (named columns only, no key
    # anywhere), and this makes sure a key pasted into a field it should never be in cannot reach the files.
    text = common.redact_text(render_text(report), secrets=(api_key,))
    payload = common.redact_text(json.dumps(report, indent=2, ensure_ascii=False, default=jsonable) + "\n", secrets=(api_key,))
    out_dir.mkdir(parents=True, exist_ok=True)
    text_path = common.write_text(out_dir / "inventory.txt", text)
    json_path = common.write_text(out_dir / "inventory.json", payload)

    print_summary(report)
    say()
    if owned_copy and args.keep_copy:
        say(f"The database copy was kept in {copy_dir} (--keep-copy); it holds password hashes and sealed keys: delete it when done.")
    elif owned_copy and not copy_left:  # a copy that could not be deleted was already reported where it happened
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
        # A full disk or a locked file ends a long run: one line, not a traceback.
        say(f"ERROR: {error}")
        return 2
    except KeyboardInterrupt:
        say("Interrupted. Nothing was changed anywhere.")
        return 130


if __name__ == "__main__":
    sys.exit(main())
