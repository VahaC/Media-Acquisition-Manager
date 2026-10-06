r"""MAM-34 live check of Jellyfin's Identify on three real items of your library.

What it is for: before MAM sets a TMDb id in Jellyfin through Identify (POST /Items/RemoteSearch/Movie|Series to
find the match by id, then POST /Items/RemoteSearch/Apply/{itemId}?replaceAllImages=true), this records what
Jellyfin 12.1 really answers and what Apply really changes, on a film, a series and a movie that is really an
episode.

Reads:  Jellyfin with GET requests (version, plugins, libraries and their options, the three items, their images,
        and for the series its seasons and first episodes) and with the Identify SEARCH requests, which only ask
        the metadata providers and change nothing. Two Apply requests go to an id no item has (one with a wrong
        key), which Jellyfin refuses with 404 and 401 before it touches anything. (Listing an item's images makes
        Jellyfin store the image sizes it lacked in its own database; no file is written.) With ssh allowed it
        also lists each item's folder inside the jellyfin container with `find` (read-only) before and after,
        to prove Identify touched no media file.
Writes: Jellyfin, with POST /Items/RemoteSearch/Apply/{itemId}?replaceAllImages=true and nothing else. It runs only
        for an item you chose, whose library saves nothing beside the media, after the script printed exactly what
        it will send and what it will replace, and you typed y for that item; the client refuses any other write,
        and each y pays for one Apply (a timeout never repeats it). Apply REPLACES the item's title, overview,
        images and ALL its provider ids; there is no undo.
        On this PC it writes under D:\Sources\Media-Acquisition-Manager\.claude\mam34-check\ (gitignored):
          identify-summary.txt   everything printed, readable
          identify-raw\          UNREDACTED snapshots of the items before and after, and every raw answer: private
          samples-pending\       REDACTED candidates for VendorSamples, plus INDEX.txt (which request made which
                                 file); nothing is ever written into the repository's VendorSamples folder
Asks:   the Jellyfin API key with hidden input (never printed, never saved); then which of your items is which;
        ssh asks for the media host's password itself.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam34\identify_check.py

Python 3.10+, standard library only.
"""

from __future__ import annotations

import argparse
import json
import posixpath
import shlex
import sys
import threading
import time
import traceback
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote, quote_plus

import common
from common import Jellyfin, JellyfinError, Reply, ToolError

# A well-formed key nobody issued: the wrong-key calls must be refused before any handler runs.
DUMMY_KEY = "0" * 32
# No item has this id, so an Apply to it can't change anything: it records the 404 (and, with the dummy key, the 401).
# Not the all-zero id: LibraryManager.GetItemById throws ArgumentException for Guid.Empty, which Jellyfin answers
# with a 400, so it would record the wrong refusal and the 404 would never be seen. A name-based id can't collide.
UNKNOWN_ID = uuid.uuid5(uuid.NAMESPACE_URL, "mam34-identify-check/no-such-item").hex
POLL_SECONDS = 10.0
POLL_LIMIT_SECONDS = 1800.0
HEARTBEAT_SECONDS = 10.0
# Two quiet polls in a row after the Etag moved: a series' children are refreshed after the series itself is saved.
SETTLE_POLLS = 2
FIRST_EPISODES = 3
RAW_DIR = "identify-raw"
PENDING_DIR = "samples-pending"


@dataclass(frozen=True)
class CaseSpec:
    key: str
    label: str
    term: str                # the read-only search that finds the owner's item
    item_type: str           # Jellyfin item type; also the RemoteSearch endpoint
    id_key: str              # provider id key of the expected id
    expected_id: str
    provider: str            # SearchProviderName of the search whose row Apply sends
    apply_timeout: float     # a series refreshes every episode, one TMDb request each, so it may take minutes
    item_sample: str
    images_sample: str
    apply_sample: str


CASES = [
    CaseSpec("film", 'FILM "120. Landing Stripling"', "Landing Stripling", "Movie", "Tmdb", "223021",
             "TheMovieDb", 180.0, "item-after-identify.json", "item-images-after-identify.json",
             "remote-search-apply-response.txt"),
    CaseSpec("series", 'SERIES "The Penguins of Madagascar"', "Penguins of Madagascar", "Series", "Tmdb", "7869",
             "TheMovieDb", 1800.0, "series-item-after-identify.json", "series-images-after-identify.json",
             "series-remote-search-apply-response.txt"),
    # Jellyfin has no episode Identify: an episode kept in a movie library is a Movie item, found through OMDb by its IMDb id.
    CaseSpec("episode-movie", 'EPISODE-MOVIE "Greatest Events of World War II in Colour S01E01 Blitzkrieg"',
             "Blitzkrieg", "Movie", "Imdb", "tt9103946", "The Open Movie Database", 180.0,
             "episode-movie-after-identify.json", "episode-movie-images-after-identify.json",
             "episode-movie-remote-search-apply-response.txt"),
]


@dataclass
class CaseState:
    spec: CaseSpec
    item: dict | None = None            # the entry the owner picked from the search list
    item_id: str = ""
    library: dict | None = None
    refusal: str = ""                   # why Apply is not allowed for this item; empty means it may be asked
    before: dict | None = None
    after: dict | None = None
    episode_ids: list[str] = field(default_factory=list)
    folder_before: dict | None = None
    apply_sent: bool = False            # an Apply left for this item, so its folder is listed again at the end
    source: "SearchRecord | None" = None  # the search whose row Apply would send
    outcome: str = "not reached"
    details: list[str] = field(default_factory=list)


@dataclass
class SearchRecord:
    name: str
    description: str
    path: str
    query: dict | None
    body: object
    reply: Reply | None
    error: str
    rows: list | None


class WriteRefused(Exception):
    """The script tried a write it is not allowed to make: a bug, never an answer from Jellyfin, so it is not a ToolError
    (those are reported as one line and the run goes on)."""


class GatedJellyfin(Jellyfin):
    """The only writes this script can send, enforced where every request passes: the Identify searches (they only ask
    the metadata providers), the Apply on an id no item has, and ONE Apply for the item whose y was just typed. The
    permit is spent before the request leaves, so a timeout can't lead to a second Apply, and a slip in the control
    flow can't send one that was never confirmed."""

    SEARCHES = ("/Items/RemoteSearch/Movie", "/Items/RemoteSearch/Series")
    APPLY = "/Items/RemoteSearch/Apply/"

    def __init__(self, *args, **kwargs) -> None:
        super().__init__(*args, **kwargs)
        self._apply_permit: str | None = None

    def permit_apply(self, item_id: str) -> None:
        self._apply_permit = item_id

    def request(self, method, path, query=None, body=None, timeout=None, api_key_override=None) -> Reply:
        if method.strip().upper() != "GET":
            self._check_write(method.strip().upper(), path)
        return super().request(method, path, query, body, timeout, api_key_override)

    def _check_write(self, method: str, path: str) -> None:
        if method == "POST" and (path in self.SEARCHES or path == self.APPLY + UNKNOWN_ID):
            return
        permit = self._apply_permit
        if method == "POST" and permit and path == self.APPLY + permit:
            self._apply_permit = None
            return
        raise WriteRefused(f"{method} {path} is not a write this script may send now (no typed y for that item).")


class Run:
    """What one run shares: the client, the options, and what was written."""

    def __init__(self, args: argparse.Namespace, client: GatedJellyfin, out_dir: Path) -> None:
        self.args = args
        self.client = client
        self.out_dir = out_dir
        self.states: list[CaseState] = []
        self.library_locations: list[str] = []


# Module state, reset by main(): the summary must hold everything that was printed, and must never hold the key.
_LINES: list[str] = []
_SECRETS: list[str] = []
_WRITTEN: list[Path] = []
_INDEX: list[str] = []


def scrub_key(text: str) -> str:
    """The typed API key, in every spelling it can take, becomes REDACTED. Raw files stay otherwise unredacted (the
    owner reads exact paths and ids in them), but the key must not reach the screen, a file or the summary even if a
    server echoes it back in an error."""
    for secret in _SECRETS:
        for spelling in (secret, quote(secret, safe=""), quote_plus(secret), json.dumps(secret)[1:-1]):
            text = text.replace(spelling, common.REDACTED)
    return text


def say(text: str = "") -> None:
    text = scrub_key(text)
    print(text, flush=True)
    _LINES.append(text)


def heading(text: str) -> None:
    say()
    say("=" * 78)
    say(text)
    say("=" * 78)


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def short(value, limit: int = 90) -> str:
    if value is None:
        return "(none)"
    if isinstance(value, str):
        if not value:
            return "(empty)"
        text = " ".join(value.split())
        if len(text) > limit:
            return repr(text[:limit - 3] + "...") + f" [{len(value)} chars]"
        return repr(text)
    text = json.dumps(value, ensure_ascii=False)
    return text if len(text) <= limit * 2 else text[:limit * 2 - 3] + "..."


def dashed(item_id: str) -> str:
    # System.Text.Json's own Guid reader takes only the dashed form; Jellyfin's converter takes both, so dashes are safe.
    return str(uuid.UUID(item_id))


# ---------------------------------------------------------------- output files

def write_raw(run: Run, name: str, obj) -> Path:
    path = common.write_text(run.out_dir / RAW_DIR / name, scrub_key(json.dumps(obj, indent=2, ensure_ascii=False) + "\n"))
    _WRITTEN.append(path)
    return path


def offer_sample(run: Run, filename: str, text: str, request_note: str) -> bool:
    """Writes a REDACTED candidate sample, or says why it was not written: a sample that fails the leak rules
    would fail VendorSamplesTests as soon as someone copied it into the repository."""
    text = common.redact_text(text, secrets=_SECRETS)
    problems = common.leaks(text, _SECRETS)
    if problems:
        say(f"  NOT WRITTEN samples-pending/{filename}: {', '.join(sorted(set(problems)))}")
        _INDEX.append(f"{filename}\n  NOT WRITTEN, it would leak: {', '.join(sorted(set(problems)))}\n  {request_note}\n")
        return False
    path = common.write_text(run.out_dir / PENDING_DIR / filename, text)
    _WRITTEN.append(path)
    _INDEX.append(f"{filename}\n  {request_note}\n  leak check: clean\n")
    return True


def offer_json_sample(run: Run, filename: str, obj, request_note: str) -> bool:
    redacted = common.redact_json(obj, secrets=_SECRETS)
    return offer_sample(run, filename, json.dumps(redacted, indent=2, ensure_ascii=False) + "\n", request_note)


def write_summary(out_dir: Path) -> None:
    if not _LINES:
        return
    text = common.redact_text("\n".join(_LINES) + "\n", secrets=_SECRETS)
    path = common.write_text(out_dir / "identify-summary.txt", text)
    if _INDEX:
        header = ("samples-pending: candidates for tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/"
                  "Jellyfin.\nREDACTED, but read each one before it is copied; the README of VendorSamples says how "
                  "to name it and which row to add.\nRecorded " + now_iso() + ".\n\n")
        common.write_text(out_dir / PENDING_DIR / "INDEX.txt", common.redact_text(header + "\n".join(_INDEX), secrets=_SECRETS))
    print(f"\nSummary written to {path}", flush=True)


# ---------------------------------------------------------------- Phase 0: choosing the items, libraries

def find_candidates(client: Jellyfin, spec: CaseSpec) -> list[dict]:
    query = {"searchTerm": spec.term, "includeItemTypes": spec.item_type, "recursive": True,
             # ProductionYear is not an ItemFields value, and an unknown field name may be refused: it needs no asking.
             "fields": "Path,ProviderIds,OriginalTitle", "limit": 25,
             # Without a user Jellyfin otherwise replaces a movie of a collection by its BoxSet.
             "collapseBoxSetItems": False, "enableImages": False, "enableUserData": False}
    data = client.list_items(query).data()
    items = data.get("Items") if isinstance(data, dict) else None
    return [item for item in items or [] if isinstance(item, dict) and item.get("Id")]


def choose_item(spec: CaseSpec, items: list[dict]) -> dict | None:
    for number, item in enumerate(items, 1):
        year = f" ({item['ProductionYear']})" if item.get("ProductionYear") else ""
        say(f"  {number}. {item.get('Name')}{year}")
        say(f"       Path:        {item.get('Path') or '(none)'}")
        say(f"       ProviderIds: {json.dumps(item.get('ProviderIds') or {}, ensure_ascii=False)}")
    while True:
        answer = common.ask(f"Which one is the {spec.label}? Number, or Enter to skip this case")
        if not answer:
            say("  -> skipped")
            return None
        if answer.isdigit() and 1 <= int(answer) <= len(items):
            chosen = items[int(answer) - 1]
            say(f"  -> {answer}: {chosen.get('Name')}")
            return chosen
        say(f"  {answer!r} is not a number from 1 to {len(items)}.")


def pick_cases(run: Run) -> None:
    heading("Choosing your items (read-only searches)")
    say("Each search lists what Jellyfin has under that name. Check the Path and ProviderIds, then type the number.")
    for spec in CASES:
        if run.args.only and spec.key not in run.args.only:
            continue
        say()
        say(f"{spec.label}: searching Jellyfin for {spec.term!r} (type {spec.item_type})")
        try:
            items = find_candidates(run.client, spec)
        except ToolError as error:
            say(f"  search failed: {error}")
            continue
        if not items:
            say("  Jellyfin has no such item; this case is skipped.")
            continue
        item = choose_item(spec, items)
        if item is None:
            continue
        state = CaseState(spec, item=item)
        try:
            state.item_id = common.guid32(item["Id"])
        except JellyfinError as error:
            say(f"  {error} This case is skipped.")
            continue
        run.states.append(state)


def file_writing_reasons(options: dict) -> list[str]:
    """Why a library writes files beside its media, or may (empty: it doesn't, and the options say so). SaveLocalMetadata
    is the switch for images and metadata; a non-empty MetadataSavers (Jellyfin 12 has only Nfo) writes .nfo files; a
    null (or absent) MetadataSavers means Jellyfin's default saver, which writes them with SaveLocalMetadata true and
    still rewrites existing ones without it; and options that don't say are treated like options that do."""
    reasons = []
    savers = options.get("MetadataSavers")
    save_local = options.get("SaveLocalMetadata")
    if not isinstance(save_local, bool):
        # Jellyfin always sends the flag; if it is missing, nothing shows that the library writes no files, and
        # "no reason found" would be read as "safe".
        reasons.append("SaveLocalMetadata is missing from its library options, so whether files are saved beside the media is unknown")
    elif save_local:
        reasons.append("SaveLocalMetadata is true (images and metadata are saved beside the media)"
                       + (", with MetadataSavers null, so Jellyfin's default Nfo saver applies" if savers is None else ""))
    elif savers is None:
        # ProviderManager.IsSaverEnabledForItem: with null savers and local saving off, a save that counts as an edit (a
        # series' children moving its genres or rating does) still rewrites an .nfo that already exists beside the media.
        # Only an explicit empty list (what every library here has) rules the saver out.
        reasons.append("MetadataSavers is null, so Jellyfin follows its defaults and rewrites an .nfo file that already "
                       "exists beside the media when a save counts as an edit")
    if savers is not None and not isinstance(savers, list):
        reasons.append(f"MetadataSavers is {savers!r}, not a list, so whether .nfo files are written beside the media is unknown")
    elif savers:
        reasons.append(f"MetadataSavers contains {', '.join(str(s) for s in savers)} (.nfo files beside the media)")
    # TrickplayProvider runs during a refresh only when the scan option is on; then the trickplay folder goes beside the media.
    if options.get("SaveTrickplayWithMedia") is True and options.get("ExtractTrickplayImagesDuringLibraryScan") is True:
        reasons.append("SaveTrickplayWithMedia and ExtractTrickplayImagesDuringLibraryScan are both true (a refresh may write "
                       "a trickplay folder beside the media)")
    return reasons


def describe_library(run: Run, state: CaseState) -> None:
    spec, item = state.spec, state.item
    path = item.get("Path") or ""
    say()
    say(f"-- {spec.label}: {item.get('Name')!r}")
    say(f"   Id {state.item_id}   Path {path or '(none)'}")
    library = None
    try:
        library = run.client.library_for(path) if path else None
    except ToolError as error:
        say(f"   could not read the libraries: {error}")
    state.library = library
    if library is None:
        # Unknown options can't be shown to be harmless, and files are never to be touched.
        state.refusal = "its library was not found among the Locations of GET /Library/VirtualFolders, so what it writes is unknown"
        say("   Library: NOT FOUND for this path")
    else:
        options = library.get("LibraryOptions") or {}
        say(f"   Library: {library.get('Name')!r}   Locations {json.dumps(library.get('Locations') or [], ensure_ascii=False)}")
        for name in ("SaveLocalMetadata", "MetadataSavers", "LocalMetadataReaderOrder", "SubtitleDownloadLanguages",
                     "SaveSubtitlesWithMedia", "PreferredMetadataLanguage", "MetadataCountryCode"):
            say(f"     {name:<26}: {json.dumps(options.get(name), ensure_ascii=False)}")
        for type_option in options.get("TypeOptions") or []:
            if type_option.get("Type") == spec.item_type:
                say(f"     {spec.item_type} metadata fetchers      : {', '.join(type_option.get('MetadataFetchers') or []) or '(none)'}")
                say(f"     {spec.item_type} image fetchers         : {', '.join(type_option.get('ImageFetchers') or []) or '(none)'}")
        reasons = file_writing_reasons(options)
        if reasons:
            state.refusal = "its library WRITES FILES BESIDE MEDIA: " + "; ".join(reasons)
        elif options.get("SaveSubtitlesWithMedia") and options.get("SubtitleDownloadLanguages"):
            say("   note: subtitles may be downloaded beside the media by Jellyfin's subtitle task; Apply itself does not ask for them.")
    try:
        reply = run.client.get(f"/Items/{state.item_id}/ExternalIdInfos")
        if reply.ok and isinstance(reply.json, list):
            names = [f"{info.get('Name')} ({info.get('Key')})" for info in reply.json if isinstance(info, dict)]
            say(f"   Id providers for this item (ExternalIdInfos): {', '.join(names) or '(none)'}")
        else:
            say(f"   ExternalIdInfos: HTTP {reply.status}")
    except ToolError as error:
        say(f"   ExternalIdInfos failed: {error}")
    if state.refusal:
        say()
        say("   !!! " + state.refusal.upper())
        say("   !!! Apply is REFUSED for this item (the rule: media folders are never touched). Searches still run.")


# ---------------------------------------------------------------- Phase 1: snapshots and folder listings

def episode_brief(episode: dict) -> dict:
    overview = episode.get("Overview") or ""
    return {"Id": episode.get("Id"), "Name": episode.get("Name"), "Season": episode.get("ParentIndexNumber"),
            "Episode": episode.get("IndexNumber"), "OverviewPresent": bool(overview), "OverviewChars": len(overview),
            "ImageTagKeys": sorted((episode.get("ImageTags") or {}).keys()), "ProviderIds": episode.get("ProviderIds") or {}}


def child_page(client: Jellyfin, query: dict) -> dict:
    data = client.list_items(query).data()
    return data if isinstance(data, dict) else {}


def series_info(client: Jellyfin, series_id: str, episode_ids: list[str]) -> dict:
    """Seasons and episodes counted, and the same first episodes brief: Apply on a series refreshes them all."""
    seasons = child_page(client, {"parentId": series_id, "recursive": True, "includeItemTypes": "Season", "limit": 1})
    episodes = child_page(client, {"parentId": series_id, "recursive": True, "includeItemTypes": "Episode", "limit": 1})
    if episode_ids:
        # By id, not by position: an episode that moved in the order is still the one compared.
        first = child_page(client, {"ids": episode_ids, "fields": "Overview,ProviderIds"})
        by_id = {item.get("Id", "").replace("-", "").lower(): item for item in first.get("Items") or []}
        listed = [by_id[i] for i in episode_ids if i in by_id]
    else:
        first = child_page(client, {"parentId": series_id, "recursive": True, "includeItemTypes": "Episode",
                                    "sortBy": "ParentIndexNumber,IndexNumber", "sortOrder": "Ascending",
                                    "limit": FIRST_EPISODES, "fields": "Overview,ProviderIds"})
        listed = first.get("Items") or []
    return {"SeasonCount": seasons.get("TotalRecordCount"), "EpisodeCount": episodes.get("TotalRecordCount"),
            "FirstEpisodes": [episode_brief(e) for e in listed]}


def take_snapshot(run: Run, state: CaseState) -> dict:
    client = run.client
    item = client.item(state.item_id).data()
    if not isinstance(item, dict):
        raise JellyfinError("Jellyfin answered the item read with something that is not an item.")
    images = client.get(f"/Items/{state.item_id}/Images").data()
    snapshot = {"taken": now_iso(), "case": state.spec.key, "item": item,
                "images": images if isinstance(images, list) else []}
    if state.spec.item_type == "Series":
        snapshot["series"] = series_info(client, state.item_id, state.episode_ids)
        if not state.episode_ids:
            state.episode_ids = [common.guid32(e["Id"]) for e in snapshot["series"]["FirstEpisodes"] if e.get("Id")]
    return snapshot


def image_count(snapshot: dict) -> int:
    return len(snapshot.get("images") or [])


def state_line(snapshot: dict) -> str:
    item = snapshot["item"]
    return (f"Name={short(item.get('Name'), 60)} ProductionYear={item.get('ProductionYear')} "
            f"ProviderIds={json.dumps(item.get('ProviderIds') or {}, ensure_ascii=False)} images={image_count(snapshot)}")


def listing_command(directory: str, max_depth: int | None) -> str:
    depth = f" -maxdepth {max_depth}" if max_depth else ""
    # Single quotes keep the remote shell from touching \t and \n: find itself expands them.
    return (f"docker exec {common.JELLYFIN_CONTAINER} find {shlex.quote(directory)}{depth} "
            r"-printf '%p\t%s\t%T@\n'")


def folder_target(state: CaseState, run: Run) -> tuple[str, int | None]:
    """The whole tree of a series; for a film or an episode-movie its own folder, two levels deep (sidecar files and
    their folders), or one level when the file sits straight in a library root, which holds everything."""
    path = (state.item.get("Path") or "").replace("\\", "/")
    if state.spec.item_type == "Series":
        return path, None
    parent = posixpath.dirname(path)
    roots = {posixpath.normpath(p.replace("\\", "/")) for p in run.library_locations}
    return parent, 1 if posixpath.normpath(parent) in roots else 2


LISTING_MARK = "@@MAM34@@ "


def list_folders(run: Run, states: list[CaseState]) -> dict[str, dict]:
    """The folders of several items in ONE ssh call: ssh asks for the password on every call, so the 'before' listings
    cost one prompt and the 'after' listings one more, however many items were chosen."""
    results: dict[str, dict] = {}
    targets = []
    for state in states:
        directory, depth = folder_target(state, run)
        if directory.startswith("/"):
            targets.append((state.spec.key, directory, depth))
        else:
            results[state.spec.key] = {"ok": False, "error": f"the item's path {directory!r} is not an absolute path",
                                       "entries": {}}
    if not targets:
        return results
    command = "; ".join(f"echo {shlex.quote(LISTING_MARK + key)}; {listing_command(directory, depth)}"
                        for key, directory, depth in targets)
    try:
        code, out, _ = common.ssh_run(run.args.ssh_host, command, timeout=300 * len(targets))
    except ToolError as error:
        for key, _, _ in targets:
            results[key] = {"ok": False, "error": str(error), "entries": {}}
        return results
    sections: dict[str, dict] = {key: {} for key, _, _ in targets}
    current = None
    for line in out.decode("utf-8", errors="replace").split("\n"):
        if line.startswith(LISTING_MARK):
            current = line[len(LISTING_MARK):].strip()
            continue
        parts = line.rsplit("\t", 2)  # a file name may hold a tab; the two numbers are the last two fields
        if current in sections and len(parts) == 3:
            sections[current][parts[0]] = (parts[1], parts[2])
    note = f"ssh exit code {code}"
    for key, directory, depth in targets:
        entries = sections[key]
        results[key] = {"ok": bool(entries), "directory": directory, "depth": depth, "entries": entries,
                        "error": "" if entries else f"no listing came back ({note})", "note": note if code else ""}
    return results


def guard_existing_images(run: Run, state: CaseState) -> None:
    """Apply replaces ALL images and Jellyfin deletes the old image files first. An image of the item (or, for a series,
    of its first episodes) that lies beside the media would be a media-folder file deleted, which the owner rules out,
    so such an item is refused like one whose library saves files there."""
    lists = [state.before["images"]]
    try:
        for episode_id in state.episode_ids:
            data = run.client.get(f"/Items/{episode_id}/Images").data()
            lists.append(data if isinstance(data, list) else [])
    except ToolError as error:
        state.refusal = f"the images of its episodes could not be read to check where they are stored: {error}"
        return
    beside = [image.get("Path") for images in lists for image in images if beside_media(run, image.get("Path") or "")]
    if beside:
        state.refusal = (f"an existing image file lies beside the media ({beside[0]}"
                         + (f" and {len(beside) - 1} more" if len(beside) > 1 else "")
                         + "); Apply replaces all images and deletes the old image files, so it could delete a media-folder file")


def phase1(run: Run) -> None:
    heading("PHASE 1 - the 'before' snapshots (read-only)")
    listable: list[CaseState] = []
    for state in run.states:
        say()
        say(f"-- {state.spec.label}")
        try:
            state.before = take_snapshot(run, state)
        except ToolError as error:
            state.refusal = state.refusal or f"the item could not be read: {error}"
            say(f"   snapshot failed: {error}")
            continue
        path = write_raw(run, f"{state.spec.key}-before.json", state.before)
        say(f"   before: {state_line(state.before)}")
        series = state.before.get("series")
        if series:
            say(f"   seasons {series['SeasonCount']}, episodes {series['EpisodeCount']}; the first {len(series['FirstEpisodes'])}:")
            for brief in series["FirstEpisodes"]:
                say(f"     S{brief['Season']}E{brief['Episode']} {short(brief['Name'], 50)} overview={brief['OverviewPresent']} "
                    f"images={brief['ImageTagKeys']} ids={json.dumps(brief['ProviderIds'], ensure_ascii=False)}")
        say(f"   saved UNREDACTED (private): {path}")
        if not state.refusal:
            guard_existing_images(run, state)
            if state.refusal:
                say(f"   !!! {state.refusal}")
                say("   !!! Apply is REFUSED for this item (the rule: media folders are never touched). Searches still run.")
        if state.refusal:
            continue  # nothing will be applied, so no folder baseline is needed
        if run.args.no_ssh:
            say("   folder listing: skipped (--no-ssh)")
        else:
            listable.append(state)
    if listable:
        say()
        say("-- folder listings of the items that may be applied (one ssh call)")
        listings = list_folders(run, listable)
        for state in listable:
            state.folder_before = listings[state.spec.key]
            if state.folder_before["ok"]:
                say(f"   {state.spec.key}: {len(state.folder_before['entries'])} entries under {state.folder_before['directory']}")
                write_raw(run, f"{state.spec.key}-folder-before.json", state.folder_before["entries"])
            else:
                say(f"   {state.spec.key}: folder listing UNAVAILABLE ({state.folder_before['error']}); the files-unchanged proof will be missing")


# ---------------------------------------------------------------- Phase 2: Identify searches

def search_body(ids: dict, item_id: str | None, provider: str | None) -> dict:
    body: dict = {"SearchInfo": {"ProviderIds": ids}}
    if item_id:
        body["ItemId"] = dashed(item_id)
    if provider:
        body["SearchProviderName"] = provider
    return body


def describe_rows(rows: list) -> str:
    names = [f"{row.get('Name')} ({row.get('ProductionYear') or '?'})" for row in rows[:4] if isinstance(row, dict)]
    id_keys = sorted({str(k) for row in rows if isinstance(row, dict) for k in (row.get("ProviderIds") or {})})
    providers = sorted({str(row.get("SearchProviderName")) for row in rows if isinstance(row, dict)})
    more = f", +{len(rows) - 4} more" if len(rows) > 4 else ""
    return f"{len(rows)} row(s): {'; '.join(names)}{more}; id keys {id_keys}; providers {providers}"


def send_recorded(run: Run, name: str, description: str, path: str, body, *, query: dict | None = None,
                  key: str | None = None, expect_status: int = 200, expect_rows: str | None = None) -> SearchRecord:
    """Sends one POST that changes nothing, prints a one-line verdict and records the raw answer. A transport failure
    is recorded as 'no answer', not raised: one dead search must not end the whole check."""
    reply, error = None, ""
    try:
        # A provider lookup goes out to the internet (OMDb can be slow), so it gets longer than the default 30 s.
        reply = run.client.post(path, body, query, api_key_override=key, timeout=60)
    except ToolError as failure:
        error = str(failure)
    rows = reply.json if reply is not None and reply.ok and isinstance(reply.json, list) else None
    record = SearchRecord(name, description, path, query, body, reply, error, rows)
    say(f"  [{name}] {description}")
    if reply is None:
        say(f"      -> NO ANSWER: {error}")
        return record
    verdict = f"HTTP {reply.status} in {reply.seconds:.2f}s"
    if rows is not None:
        verdict += "; " + (describe_rows(rows) if rows else "no rows")
    elif not reply.ok:
        verdict += f"; {' '.join(reply.text.split())[:120] or '(empty body)'}"
    flags = []
    if reply.status != expect_status:
        flags.append(f"UNEXPECTED, expected HTTP {expect_status}")
    if expect_rows == "empty" and rows:
        flags.append("UNEXPECTED, expected no rows")
    say(f"      -> {verdict}" + (f"   [{'; '.join(flags)}]" if flags else ""))
    request_line = f"POST {path}" + ("?" + "&".join(f"{k}={str(v).lower()}" for k, v in query.items()) if query else "")
    write_raw(run, f"{name}.json", {"request": {"method": "POST", "path": path, "query": query, "body": body,
                                                       "wrong_key_sent": key is not None},
                                           "status": reply.status, "seconds": round(reply.seconds, 3),
                                           "headers": reply.headers, "body": reply.text})
    note = (f"Request: {request_line}" + (" (with a wrong API key)" if key else "") + f"\n  Body: "
            f"{json.dumps(body, ensure_ascii=False)}\n  Answer: HTTP {reply.status} after {reply.seconds:.2f} s")
    offer_sample(run, f"{name}-response.txt", reply.as_sample(), note)
    return record


def search(run: Run, name: str, description: str, endpoint: str, ids: dict, item_id: str | None,
           provider: str | None, **options) -> SearchRecord:
    return send_recorded(run, name, description, f"/Items/RemoteSearch/{endpoint}", search_body(ids, item_id, provider),
                         **options)


def search_film(run: Run, state: CaseState) -> None:
    spec, item_id = state.spec, state.item_id
    ids = {"Tmdb": spec.expected_id}
    say(f"\n-- {spec.label}: Movie searches for Tmdb {spec.expected_id}")
    state.source = search(run, "remote-search-movie-by-tmdb", "(a) with ItemId and SearchProviderName TheMovieDb",
                          "Movie", ids, item_id, "TheMovieDb")
    search(run, "remote-search-movie-by-tmdb-without-item", "(b) the same without ItemId", "Movie", ids, None,
           "TheMovieDb")
    everyone = search(run, "remote-search-movie-by-tmdb-all-providers", "(c) the same without SearchProviderName",
                      "Movie", ids, item_id, None)
    if everyone.rows is not None:
        answered = sorted({str(r.get("SearchProviderName")) for r in everyone.rows if isinstance(r, dict)})
        say(f"      providers that answered when none was named: {answered or '(none)'}")
    search(run, "remote-search-movie-by-tmdb-lowercase-key", '(d) the key spelled "tmdb"', "Movie",
           {"tmdb": spec.expected_id}, item_id, "TheMovieDb")
    search(run, "remote-search-movie-unknown-tmdb-id", "(e) an id TMDb has not got (999999999)", "Movie",
           {"Tmdb": "999999999"}, item_id, "TheMovieDb", expect_rows="empty")
    search(run, "remote-search-wrong-key", "(f) a wrong API key", "Movie", ids, item_id, "TheMovieDb", key=DUMMY_KEY,
           expect_status=401)
    say("  (g) namespace check: a movie id and a TV id are different numbers")
    series_state = next((s for s in run.states if s.spec.key == "series"), None)
    tv_id = next((s.spec.expected_id for s in run.states if s.spec.key == "series"), "7869")
    as_movie = search(run, "remote-search-movie-by-tv-id", f"(g1) Movie search with the series' TV id {tv_id}", "Movie",
                      {"Tmdb": tv_id}, item_id, "TheMovieDb")
    as_series = search(run, "remote-search-series-by-movie-id", f"(g2) Series search with the film's id {spec.expected_id}",
                       "Series", ids, series_state.item_id if series_state else None, "TheMovieDb")
    for label, record in (("movie search with a TV id", as_movie), ("series search with a movie id", as_series)):
        if record.rows:
            say(f"      NAMESPACE: the {label} answered {[r.get('Name') for r in record.rows if isinstance(r, dict)]}; "
                "an id belongs to one kind of work, so the app must send it only to the search of that kind")
        elif record.rows is not None:
            say(f"      NAMESPACE: the {label} answered no rows")


def search_series(run: Run, state: CaseState) -> None:
    spec = state.spec
    say(f"\n-- {spec.label}: Series search for Tmdb {spec.expected_id}")
    state.source = search(run, "remote-search-series-by-tmdb", "Series search with ItemId and SearchProviderName TheMovieDb",
                          "Series", {"Tmdb": spec.expected_id}, state.item_id, "TheMovieDb")


def search_episode_movie(run: Run, state: CaseState) -> None:
    spec, item_id = state.spec, state.item_id
    ids = {"Imdb": spec.expected_id}
    say(f"\n-- {spec.label}: Movie searches for Imdb {spec.expected_id}")
    state.source = search(run, "remote-search-movie-by-imdb-omdb", '(a) SearchProviderName "The Open Movie Database"',
                          "Movie", ids, item_id, "The Open Movie Database")
    tmdb = search(run, "remote-search-movie-by-imdb-tmdb", '(b) SearchProviderName "TheMovieDb"', "Movie", ids, item_id,
                  "TheMovieDb")
    everyone = search(run, "remote-search-movie-by-imdb-all-providers", "(c) no SearchProviderName", "Movie", ids,
                      item_id, None)
    if state.source.rows is not None and tmdb.rows is not None:
        say(f"      COMPARISON: OMDb answered {len(state.source.rows)} row(s), TheMovieDb {len(tmdb.rows)}"
            + (f", all providers {len(everyone.rows)}" if everyone.rows is not None else ""))


SEARCHES = {"film": search_film, "series": search_series, "episode-movie": search_episode_movie}


def phase2(run: Run) -> None:
    heading("PHASE 2 - Identify searches")
    say("These POST /Items/RemoteSearch/Movie|Series calls only ASK the metadata providers: they change nothing in Jellyfin.")
    for state in run.states:
        SEARCHES[state.spec.key](run, state)
    say("\n-- Apply on an item that does not exist (no item, so no side effect)")
    dummy_row = {"Name": "MAM-34 dummy", "ProviderIds": {"Tmdb": "1"}, "SearchProviderName": "TheMovieDb"}
    path = f"/Items/RemoteSearch/Apply/{UNKNOWN_ID}"
    send_recorded(run, "remote-search-apply-unknown-item", "Apply to an id no item has", path, dummy_row,
                  query={"replaceAllImages": True}, expect_status=404)
    send_recorded(run, "remote-search-apply-wrong-key", "Apply to an id no item has, with a wrong API key", path, dummy_row,
                  query={"replaceAllImages": True}, key=DUMMY_KEY, expect_status=401)


# ---------------------------------------------------------------- Phase 3: Apply, with a typed y per item

class Heartbeat:
    """A dot every few seconds while the owner waits on a synchronous call, so a quiet console doesn't look frozen."""

    def __init__(self, interval: float) -> None:
        self.interval = interval
        self.dots = 0
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)

    def _run(self) -> None:
        while not self._stop.wait(self.interval):
            print(".", end="", flush=True)
            self.dots += 1

    def __enter__(self) -> "Heartbeat":
        self._thread.start()
        return self

    def __exit__(self, *exc) -> None:
        self._stop.set()
        self._thread.join()
        if self.dots:
            print(flush=True)


def candidate_rows(state: CaseState) -> tuple[list[dict], str]:
    spec, source = state.spec, state.source
    if source is None or source.reply is None:
        return [], "the search that finds the row did not run or got no answer"
    if source.rows is None:
        return [], f"the search answered HTTP {source.reply.status}, not a list of rows"
    wanted = spec.expected_id.lower()
    rows = [row for row in source.rows if isinstance(row, dict)
            and {str(k).lower(): str(v).lower() for k, v in (row.get("ProviderIds") or {}).items()}.get(spec.id_key.lower()) == wanted]
    if len(rows) != 1:
        return rows, f"{len(rows)} of its {len(source.rows)} row(s) carry {spec.id_key} {spec.expected_id}; exactly one is needed"
    return rows, ""


def print_plan(run: Run, state: CaseState, row: dict) -> None:
    spec = state.spec
    item = state.before["item"]
    provider = "OMDb's" if spec.provider.startswith("The Open") else "TMDb's"
    lines = [
        f"APPLY: {spec.label}",
        "",
        f"Request:  POST {run.client.base_url}/Items/RemoteSearch/Apply/{state.item_id}?replaceAllImages=true",
        "          Authorization header: MediaBrowser Token (your API key, never shown)",
        "Body (the matched row, exactly as the search returned it):",
        *("  " + line for line in json.dumps(row, ensure_ascii=False, indent=2).split("\n")),
        "",
        "The item now:",
        f"  Name         {short(item.get('Name'))}",
        f"  Year         {item.get('ProductionYear')}",
        f"  ProviderIds  {json.dumps(item.get('ProviderIds') or {}, ensure_ascii=False)}",
        f"  Images       {image_count(state.before)}",
        "",
        f"Jellyfin will REPLACE this item's title, overview, images and ALL provider ids with {provider}.",
        "There is no undo; old images are deleted first.",
    ]
    if spec.item_type == "Series":
        lines.append("Every season and episode is refreshed (one TMDb request each): the call may take minutes.")
    lines.append("Files and folders are not touched"
                 + (f" (library {state.library.get('Name')!r} saves nothing beside the media)." if state.library else "."))
    say()
    say("+" + "-" * 76)
    for line in lines:
        say(("| " + line).rstrip())
    say("+" + "-" * 76)


def wait_for_etag(run: Run, state: CaseState, started: float) -> str:
    """After a client timeout the server may still be working. Never repeat the write: poll the item until its Etag
    moves, then until the item (and a series' first episodes) stop changing. `started` is when Apply was sent; the
    poll limit counts from now, because a series' own timeout may already have used half an hour."""
    before_etag = state.before["item"].get("Etag")
    limit, interval = run.args.poll_limit, run.args.poll_interval
    say(f"   Apply is NOT repeated. Polling the item every {interval:g}s (up to {limit / 60:g} min) until its Etag changes.")
    polling_since = time.perf_counter()
    changed_at: float | None = None
    last, quiet, failures = None, 0, 0
    with Heartbeat(run.args.heartbeat):
        while time.perf_counter() - polling_since < limit:
            time.sleep(interval)
            try:
                if changed_at is None:
                    # Only the item itself while nothing has happened: a series' refresh is busy enough already.
                    current = run.client.item(state.item_id).data()
                    if isinstance(current, dict) and current.get("Etag") != before_etag:
                        changed_at = time.perf_counter() - started
                    continue
                snapshot = take_snapshot(run, state)
            except ToolError:
                failures += 1
                if failures >= 3:
                    return "Jellyfin stopped answering the polls: the outcome is unknown. Check the item in Jellyfin; Apply was not repeated."
                continue
            failures = 0
            item = snapshot["item"]
            fingerprint = json.dumps([item.get("Etag"), item.get("ImageTags"), snapshot.get("series")], sort_keys=True,
                                     default=str)
            quiet = quiet + 1 if fingerprint == last else 0
            last = fingerprint
            if quiet >= SETTLE_POLLS:
                break
    if changed_at is None:
        return (f"the Etag did not change in {limit / 60:g} min: the Apply may not have been applied, or changed nothing. "
                "Apply was NOT repeated.")
    if quiet < SETTLE_POLLS:
        return (f"the Etag changed {changed_at:.1f}s after Apply was sent, but the item was still changing when the "
                "polling stopped, so what is read next may be partial. Apply was NOT repeated.")
    return f"the Etag changed {changed_at:.1f}s after Apply was sent, and the item then stopped changing. Apply was NOT repeated."


def send_apply(run: Run, state: CaseState, row: dict) -> tuple[Reply | None, str, float]:
    spec = state.spec
    timeout = run.args.apply_timeout if run.args.apply_timeout else spec.apply_timeout
    say(f"   Sending Apply now (waiting up to {timeout:g}s; a dot every {run.args.heartbeat:g}s means it is still working)...")
    started = time.perf_counter()
    reply, error = None, ""
    with Heartbeat(run.args.heartbeat):
        try:
            reply = run.client.post(f"/Items/RemoteSearch/Apply/{state.item_id}", row, {"replaceAllImages": True},
                                    timeout=timeout)
        except ToolError as failure:
            error = str(failure)
    return reply, error, time.perf_counter() - started


# -- the field-by-field difference

# Path is there as evidence that the item still points at the same media file.
SIMPLE_FIELDS = ("Name", "OriginalTitle", "ProductionYear", "Overview", "Genres", "Studios", "Taglines", "Tags",
                 "LockData", "DateCreated", "Etag", "Path")
# Jellyfin keys watch data by an item's ids (UserData.Key moved when the ids were written by hand), so whether Apply
# keeps what was watched is read from these.
USER_DATA_FIELDS = ("Played", "PlayCount", "PlaybackPositionTicks", "IsFavorite", "Key", "UnplayedItemCount", "PlayedPercentage")


def field_view(item: dict, name: str):
    value = item.get(name)
    if name == "Studios":
        return [s.get("Name") if isinstance(s, dict) else s for s in value or []]
    return value


def image_key(image: dict) -> tuple:
    return image.get("ImageType"), image.get("ImageIndex") or 0, image.get("ImageTag")


def image_text(image: dict) -> str:
    tag = str(image.get("ImageTag") or "")[:8]
    return (f"{image.get('ImageType')}[{image.get('ImageIndex') or 0}] tag={tag} "
            f"{image.get('Width')}x{image.get('Height')} {image.get('Size')} B  {image.get('Path') or '(no path)'}")


def beside_media(run: Run, path: str) -> bool:
    plain = (path or "").replace("\\", "/")
    return any(plain.startswith(posixpath.normpath(root.replace("\\", "/")) + "/") for root in run.library_locations)


def print_diff(run: Run, state: CaseState) -> None:
    before, after = state.before, state.after
    bi, ai = before["item"], after["item"]
    say("   Field by field (before -> after):")
    for name in SIMPLE_FIELDS:
        b, a = field_view(bi, name), field_view(ai, name)
        if a == b:
            say(f"     {name:<15} unchanged   {short(a, 60)}")
        else:
            say(f"     {name:<15} CHANGED     {short(b, 60)} -> {short(a, 60)}")
    people = (len(bi.get("People") or []), len(ai.get("People") or []))
    say(f"     {'People':<15} {'unchanged' if people[0] == people[1] else 'CHANGED  '}   count {people[0]} -> {people[1]}")
    b_data, a_data = ({k: v for k, v in (item.get("UserData") or {}).items() if k in USER_DATA_FIELDS} for item in (bi, ai))
    say(f"     {'UserData':<15} {'unchanged' if b_data == a_data else 'CHANGED  '}   {short(b_data, 80)} -> {short(a_data, 80)}")
    b_ids, a_ids = bi.get("ProviderIds") or {}, ai.get("ProviderIds") or {}
    appeared, gone = sorted(set(a_ids) - set(b_ids)), sorted(set(b_ids) - set(a_ids))
    moved = sorted(k for k in set(a_ids) & set(b_ids) if a_ids[k] != b_ids[k])
    say(f"     {'ProviderIds':<15} {json.dumps(b_ids, ensure_ascii=False)} -> {json.dumps(a_ids, ensure_ascii=False)}")
    say(f"       appeared {appeared or '-'}; disappeared {gone or '-'}; value changed {moved or '-'}")
    for name in ("ImageTags", "BackdropImageTags"):
        b, a = bi.get(name), ai.get(name)
        say(f"     {name:<17} {'unchanged' if a == b else 'CHANGED  '}   {short(b, 70)} -> {short(a, 70)}")
    b_images, a_images = before["images"], after["images"]
    b_keys, a_keys = {image_key(i) for i in b_images}, {image_key(i) for i in a_images}
    say(f"     {'Images list':<15} {len(b_images)} -> {len(a_images)}; removed {len(b_keys - a_keys)}, added {len(a_keys - b_keys)}, kept {len(b_keys & a_keys)}")
    for image in a_images:
        flag = "   <-- BESIDE THE MEDIA" if beside_media(run, image.get("Path") or "") else ""
        say(f"       now   {image_text(image)}{flag}")
    for image in b_images:
        if image_key(image) not in a_keys:
            say(f"       gone  {image_text(image)}")
    series_before, series_after = before.get("series"), after.get("series")
    if series_before and series_after:
        say(f"     Seasons {series_before['SeasonCount']} -> {series_after['SeasonCount']}; episodes "
            f"{series_before['EpisodeCount']} -> {series_after['EpisodeCount']}; "
            f"ChildCount {bi.get('ChildCount')} -> {ai.get('ChildCount')}; RecursiveItemCount {bi.get('RecursiveItemCount')} -> {ai.get('RecursiveItemCount')}")
        after_by_id = {str(e.get("Id")).replace("-", "").lower(): e for e in series_after["FirstEpisodes"]}
        for b in series_before["FirstEpisodes"]:
            a = after_by_id.get(str(b.get("Id")).replace("-", "").lower())
            if a is None:
                say(f"       S{b['Season']}E{b['Episode']}: not found after")
                continue
            say(f"       S{b['Season']}E{b['Episode']}: Name {short(b['Name'], 40)} -> {short(a['Name'], 40)}; "
                f"overview {b['OverviewChars']} -> {a['OverviewChars']} chars; images {b['ImageTagKeys']} -> {a['ImageTagKeys']}; "
                f"ids {json.dumps(b['ProviderIds'], ensure_ascii=False)} -> {json.dumps(a['ProviderIds'], ensure_ascii=False)}")


def verdict_for(state: CaseState, row: dict) -> str:
    bi, ai = state.before["item"], state.after["item"]
    checks = []
    same_name = ai.get("Name") == row.get("Name")
    checks.append(("title", same_name, "the item's Name is the row's" if same_name else
                   f"row {short(row.get('Name'), 40)}, item now {short(ai.get('Name'), 40)}"
                   + (" (unchanged)" if ai.get("Name") == bi.get("Name") else "")))
    row_overview, overview = row.get("Overview") or "", ai.get("Overview") or ""
    if not row_overview:
        checks.append(("overview", bool(overview) and overview != (bi.get("Overview") or ""),
                       f"the row has none; the item has {len(overview)} chars (before {len(bi.get('Overview') or '')})"))
    else:
        checks.append(("overview", overview == row_overview, "the item's Overview is the row's" if overview == row_overview
                       else f"row {len(row_overview)} chars, item {len(overview)} chars"))
    if row.get("ProductionYear"):
        checks.append(("year", ai.get("ProductionYear") == row["ProductionYear"],
                       f"row {row['ProductionYear']}, item {ai.get('ProductionYear')}"))
    row_ids = {str(k).lower(): str(v) for k, v in (row.get("ProviderIds") or {}).items()}
    item_ids = {str(k).lower(): str(v) for k, v in (ai.get("ProviderIds") or {}).items()}
    missing = {k: v for k, v in row_ids.items() if item_ids.get(k) != v}
    held = sorted(ai.get("ProviderIds") or {})
    checks.append(("ids", not missing, f"the row's ids are all on the item, which now holds {held}" if not missing
                   else f"the item lacks {missing}; it holds {held}"))
    before_tags, after_tags = bi.get("ImageTags") or {}, ai.get("ImageTags") or {}
    # A new Primary tag means the old image was replaced; the same tag means Jellyfin kept it.
    changed_primary = bool(after_tags.get("Primary")) and after_tags.get("Primary") != before_tags.get("Primary")
    checks.append(("images", changed_primary,
                   f"Primary tag {str(before_tags.get('Primary'))[:8]} -> {str(after_tags.get('Primary'))[:8]}"))
    passed = sum(1 for _, ok, _ in checks if ok)
    overall = "yes" if passed == len(checks) else "partly" if passed else "no"
    detail = "; ".join(f"{name} {'OK' if ok else 'DIFFERENT'} ({why})" for name, ok, why in checks)
    return f"{overall}. {detail}"


def compare_folder(run: Run, state: CaseState, after: dict) -> str:
    """The proof that Identify touched no media file: the same listing, taken again."""
    if state.folder_before is None or not state.folder_before["ok"]:
        return "not checked (no 'before' listing)"
    if not after["ok"]:
        say(f"   folder listing after: UNAVAILABLE ({after['error']})")
        return "not checked (the 'after' listing failed)"
    write_raw(run, f"{state.spec.key}-folder-after.json", after["entries"])
    before_entries, after_entries = state.folder_before["entries"], after["entries"]
    added = sorted(set(after_entries) - set(before_entries))
    removed = sorted(set(before_entries) - set(after_entries))
    changed = sorted(p for p in set(before_entries) & set(after_entries) if before_entries[p] != after_entries[p])
    if not (added or removed or changed):
        say(f"   files/folders unchanged: yes ({len(after_entries)} entries compared under {after['directory']})")
        return "yes"
    say(f"   files/folders unchanged: NO ({len(added)} added, {len(removed)} removed, {len(changed)} changed)")
    for path in added:
        say(f"     added    {path}  size {after_entries[path][0]}")
    for path in removed:
        say(f"     removed  {path}")
    for path in changed:
        say(f"     changed  {path}  size {before_entries[path][0]} -> {after_entries[path][0]}, mtime {before_entries[path][1]} -> {after_entries[path][1]}")
    return "NO"


def apply_case(run: Run, state: CaseState) -> None:
    spec = state.spec
    say()
    say("#" * 78)
    say(f"CASE {spec.label}")
    say("#" * 78)
    if state.refusal:
        state.outcome = f"refused: {state.refusal}"
        say(f"   Apply REFUSED: {state.refusal}")
        return
    if state.before is None:
        state.outcome = "stopped: no 'before' snapshot"
        say("   Stopped: no 'before' snapshot was taken.")
        return
    rows, problem = candidate_rows(state)
    if problem:
        state.outcome = f"stopped: {problem}"
        say(f"   Stopped, nothing sent: {problem}.")
        return
    row = rows[0]
    print_plan(run, state, row)
    if not common.confirm(f"Apply this to the {spec.key} item in Jellyfin NOW? Type y to apply, anything else to skip"):
        state.outcome = "not applied: you did not answer y"
        say("   Not applied. Nothing was written to Jellyfin for this item.")
        return
    say("   y typed: applying.")
    run.client.permit_apply(state.item_id)  # the one Apply this y pays for
    state.apply_sent = True
    reply, error, seconds = send_apply(run, state, row)
    if reply is not None:
        say(f"   Apply answered HTTP {reply.status} {reply.reason} after {seconds:.1f}s"
            + ("" if reply.ok else f": {' '.join(reply.text.split())[:200] or '(empty body)'}"))
        offer_sample(run, spec.apply_sample, reply.as_sample(),
                     f"Request: POST /Items/RemoteSearch/Apply/{{itemId}}?replaceAllImages=true\n"
                     f"  Body: {json.dumps(row, ensure_ascii=False)}\n"
                     f"  Answer: HTTP {reply.status} after {seconds:.1f} s")
        write_raw(run, f"apply-{spec.key}.json", {"status": reply.status, "seconds": round(seconds, 3),
                                                  "headers": reply.headers, "body": reply.text, "sent_row": row})
        applied_note = f"HTTP {reply.status} after {seconds:.1f}s"
    else:
        say(f"   Apply did not get an answer after {seconds:.1f}s: {error}")
        applied_note = "no answer: " + wait_for_etag(run, state, time.perf_counter() - seconds)
        say(f"   {applied_note}")
        write_raw(run, f"apply-{spec.key}.json", {"error": error, "seconds": round(seconds, 3), "after_wait": applied_note,
                                                  "sent_row": row})
    say("   Reading the item again...")
    try:
        state.after = take_snapshot(run, state)
    except ToolError as failure:
        state.outcome = f"applied ({applied_note}) but the 'after' read failed: {failure}"
        say(f"   after-snapshot failed: {failure}")
        return
    path = write_raw(run, f"{spec.key}-after.json", state.after)
    say(f"   after: {state_line(state.after)}")
    say(f"   saved UNREDACTED (private): {path}")
    print_diff(run, state)
    offer_json_sample(run, spec.item_sample, state.after["item"],
                      "Request: GET /Items/{id}?userId={administrator}, read after the Apply")
    offer_json_sample(run, spec.images_sample, state.after["images"], "Request: GET /Items/{id}/Images, read after the Apply")
    verdict = verdict_for(state, row)
    state.outcome = f"applied ({applied_note})"
    state.details = [f"became the matched result's: {verdict}"]
    for line in state.details:
        say(f"   VERDICT {spec.key}: {line}")


def phase3(run: Run) -> None:
    heading("PHASE 3 - Apply (WRITES; one item at a time, only after you type y)")
    for state in run.states:
        apply_case(run, state)
    sent = [state for state in run.states if state.apply_sent]
    if not sent:
        return
    say()
    say("-- the folders again, after the Applies (the proof that no media file changed)")
    if run.args.no_ssh:
        for state in sent:
            state.details.append("files/folders unchanged: not checked (--no-ssh)")
            say(f"   VERDICT {state.spec.key}: files/folders unchanged: not checked (--no-ssh)")
        return
    listings = list_folders(run, [state for state in sent if state.folder_before and state.folder_before["ok"]])
    for state in sent:
        say(f"   {state.spec.key}:")
        after = listings.get(state.spec.key)
        files = compare_folder(run, state, after) if after is not None else "not checked (no 'before' listing)"
        state.details.append(f"files/folders unchanged: {files}")
        say(f"   VERDICT {state.spec.key}: files/folders unchanged: {files}")


# ---------------------------------------------------------------- the run

def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Live check of Jellyfin's Identify on three of your items. The searches change nothing; each "
                    "Apply writes into Jellyfin and runs only after you type y for that item.",
        epilog=f"The API key is asked with hidden input. The environment variable {common.ENV_JELLYFIN_KEY} can stand in "
               "for it, for unattended tests only.")
    parser.add_argument("--jellyfin-url", default=common.DEFAULT_JELLYFIN_URL, help="default: %(default)s")
    parser.add_argument("--ssh-host", default=common.DEFAULT_SSH_HOST,
                        help="the media host, for the before/after folder listing; default: %(default)s")
    parser.add_argument("--no-ssh", action="store_true", help="skip the folder listings (no proof that files are untouched)")
    parser.add_argument("--out", default=None, help=f"output folder; default: {common.OUT_DIR}")
    parser.add_argument("--only", action="append", choices=[c.key for c in CASES], metavar="CASE",
                        help="run only this case: film, series or episode-movie (repeatable)")
    # Hidden: they exist so the tests can make a timeout, a poll and a heartbeat happen in seconds, not minutes.
    parser.add_argument("--apply-timeout", type=float, default=0.0, help=argparse.SUPPRESS)
    parser.add_argument("--poll-interval", type=float, default=POLL_SECONDS, help=argparse.SUPPRESS)
    parser.add_argument("--poll-limit", type=float, default=POLL_LIMIT_SECONDS, help=argparse.SUPPRESS)
    parser.add_argument("--heartbeat", type=float, default=HEARTBEAT_SECONDS, help=argparse.SUPPRESS)
    return parser.parse_args(argv)


def run_checks(args: argparse.Namespace, out_dir: Path) -> int:
    say("MAM-34 Identify check")
    say("Reads Jellyfin; its Apply calls WRITE into Jellyfin and run only after you type y for that item.")
    say(f"Output: {out_dir}")
    key = common.ask_secret("Jellyfin API key (typing is hidden)", common.ENV_JELLYFIN_KEY)
    if not key:
        say("No API key given; nothing was done.")
        return 1
    _SECRETS.append(key)
    client = GatedJellyfin(args.jellyfin_url, key, timeout=30.0)
    run = Run(args, client, out_dir)

    heading("PHASE 0 - server, plugins, items and libraries (read-only)")
    say(f"Jellyfin: {client.base_url}")
    info = client.system_info()
    if info.status == 401:
        say("Jellyfin refused the API key (HTTP 401). Create or copy one in Dashboard > API Keys and run again.")
        return 1
    version = (info.data() or {}).get("Version", "?")
    say(f"Version: {version}" + ("" if str(version).startswith("12.") else "   (this check's facts were verified on 12.1)"))
    plugins = client.plugins()
    if plugins.ok and isinstance(plugins.json, list):
        say("Plugins: " + (", ".join(f"{p.get('Name')} {p.get('Version')}" for p in plugins.json if isinstance(p, dict)) or "(none)"))
    else:
        say(f"Plugins: HTTP {plugins.status}")
    try:
        folders = client.virtual_folders().data()
        run.library_locations = [loc for f in folders or [] for loc in f.get("Locations") or []]
    except ToolError as error:
        say(f"Libraries could not be read: {error}")

    pick_cases(run)
    if not run.states:
        say("\nNo item chosen; nothing more to check.")
        return 0
    say()
    say("Folder listings over ssh: " + ("OFF (--no-ssh)" if args.no_ssh else f"{args.ssh_host}, one call before and one after all Applies"))
    for state in run.states:
        describe_library(run, state)
    phase1(run)
    phase2(run)
    phase3(run)

    heading("RESULT")
    for state in run.states:
        say(f"{state.spec.key:<14} {state.outcome}")
        for line in state.details:
            say(f"{'':<14}   {line}")
    say()
    say(f"Files written, under {out_dir}:")
    for path in _WRITTEN:
        say(f"  {path.relative_to(out_dir) if path.is_relative_to(out_dir) else path}")
    say("Nothing was written into the repository: samples-pending holds candidates only, and INDEX.txt says which "
        "request made each one. Review them before anything is copied into VendorSamples.")
    return 0


def inside_repo_outside_claude(path: Path) -> bool:
    """Only .claude/ (gitignored) may hold what this script writes: a candidate sample written into the repository, say
    into VendorSamples, would be one `git add` from a commit."""
    root = common.REPO_ROOT.resolve()
    if path != root and root not in path.parents:
        return False
    claude = root / ".claude"
    return path != claude and claude not in path.parents


def main(argv: list[str] | None = None) -> int:
    common.setup_console()
    args = parse_args(argv)
    _LINES.clear()
    _SECRETS.clear()
    _WRITTEN.clear()
    _INDEX.clear()
    out_dir = Path(args.out).resolve() if args.out else common.OUT_DIR
    if inside_repo_outside_claude(out_dir):
        print(f"--out {out_dir} is inside the repository but not under .claude\\ (gitignored); choose a folder outside it.",
              flush=True)
        return 2
    common.OUT_DIR = out_dir  # the helpers that take a relative path follow --out
    status = 1
    try:
        status = run_checks(args, out_dir)
    except KeyboardInterrupt:
        say("\nInterrupted. If an Apply was in flight, Jellyfin may still finish it; it was not repeated.")
        status = 130
    except ToolError as error:
        say(f"\nSTOPPED: {error}")
    except Exception:  # noqa: BLE001 - a bug in this script: say where, without ever printing the key
        say("\nUNEXPECTED ERROR (a bug in this script; nothing was repeated):")
        say(common.redact_text(traceback.format_exc(), secrets=_SECRETS))
    finally:
        write_summary(out_dir)
    return status


if __name__ == "__main__":
    sys.exit(main())
