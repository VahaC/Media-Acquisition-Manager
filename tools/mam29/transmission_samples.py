r"""MAM-29 samples of Transmission's RPC, legacy dialect (Transmission 4.0 and 4.1).

What it is for: MAM-30's Transmission adapter speaks the legacy RPC dialect (methods like torrent-add, fields like
hashString) and sends a torrent as ONE torrent-add with download-dir and a label. This records what Transmission
really answers to each request the adapter will make, and to the refusals it must understand, so the adapter is tested
against recorded answers rather than against the documentation (docs/rpc-spec.md of 4.0.6; 4.1 documents only its
new JSON-RPC dialect and still serves the legacy one).

Reads:  Transmission's RPC with session-get (only version, rpc-version, rpc-version-minimum, rpc-version-semver,
        download-dir, incomplete-dir, incomplete-dir-enabled, start-added-torrents, rename-partial-files), torrent-get
        and free-space (the default download folder, the scratch folder, one library folder and a missing folder).
        The .torrent files: by default big-buck-bunny.torrent and sintel.torrent, downloaded from webtorrent.io over
        HTTPS into this PC's memory (never saved), or local files given with --torrent / --second-torrent.
Writes: Transmission, and each only after you type y for it: torrent-add (Big Buck Bunny into the scratch folder with
        the label "MAM29 Samples"; the same again; a corrupt .torrent and a relative folder, which Transmission refuses;
        a magnet with no metadata; one add with a stale session id, which Transmission refuses before reading it) and
        torrent-stop (Big Buck Bunny, once it is complete). It never removes a torrent and never changes Transmission's
        settings: at the end it prints which torrents to delete, with their data, in Transmission's web UI. No write
        is offered while the scratch folder overlaps the library folder, Transmission can't see it, or Transmission's
        temporary folder (incomplete-dir-enabled) is on outside it.
        On this PC: D:\Sources\Media-Acquisition-Manager\.claude\mam29-check\transmission-<version>\ (gitignored):
          raw\              every request and answer; password, Authorization, session ids and metainfo removed
          samples-pending\  REDACTED candidates for VendorSamples\Transmission\, named like the future files
          INDEX.txt         one line per file: what it shows and the Transmission version
          console.txt       everything printed
Asks:   the RPC address, the user name, the password (hidden), the scratch folder and a library folder as Transmission
        sees them, and y before each write.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --full
          all 16 steps, against Transmission 4.1.3 (--full is the default)
        python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --short
          steps 01, 02, 04, 06, 08, 07, against the temporary 4.0.x container (its 9092 address is the default)
        python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --only 13,14
          steps 13 and 14 again, when Big Buck Bunny wasn't complete within --wait
        tools\mam29\README.md says how to set up each container.

Python 3.10+, standard library only.
"""

from __future__ import annotations

import argparse
import base64
import json
import posixpath
import re
import secrets as token_source
import sys
import time
import traceback
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path

import common
from common import HttpFailure, Reply, Stop, ToolError, box, heading, say

LABEL = "MAM29 Samples"
DEFAULT_TORRENT_URL = "https://webtorrent.io/torrents/big-buck-bunny.torrent"
DEFAULT_SECOND_TORRENT_URL = "https://webtorrent.io/torrents/sintel.torrent"
DEFAULT_SCRATCH = "/scratch"
DEFAULT_LIBRARY = "/downloads"
MISSING_FOLDER_NAME = "mam29-missing-folder"
HOST_NOT_ALLOWED = "mam29-not-whitelisted.invalid"
WRONG_PASSWORD = "mam29-wrong-password"
MAX_TORRENT_BYTES = 10 * 1024 * 1024
FIRST_LEGACY_LABELS_RPC = 17  # Transmission 4.0.0: torrent-add takes labels (docs/rpc-spec.md, 4.0.6)
# Bytes that don't parse as metainfo: libtransmission/rpcimpl.cc torrentAdd at 4.0.6 and 4.1.3 (Error::UNRECOGNIZED_INFO
# turned into the legacy result by api-compat.cc). "invalid or corrupt torrent file" is only for metainfo that parses.
CORRUPT_EXPECTED = "unrecognized info"

# Every name below is checked against docs/rpc-spec.md of Transmission 4.0.6 (the legacy dialect; 4.1.3's spec points
# there for it).
SESSION_FIELDS = ["version", "rpc-version", "rpc-version-minimum", "rpc-version-semver", "download-dir",
                  "incomplete-dir", "incomplete-dir-enabled", "start-added-torrents", "rename-partial-files"]
SMALL_FIELDS = ["id", "name", "hashString", "status", "percentDone", "downloadDir", "labels"]
FULL_FIELDS = ["id", "name", "hashString", "downloadDir", "status", "percentDone", "leftUntilDone", "sizeWhenDone",
               "totalSize", "isFinished", "isStalled", "error", "errorString", "labels", "metadataPercentComplete",
               "addedDate", "doneDate", "files", "fileStats"]
PROGRESS_FIELDS = ["percentDone", "leftUntilDone", "status", "error", "errorString", "rateDownload", "eta",
                   "peersConnected", "isStalled"]

# The only methods this script can send. A write needs the permit a typed y gives, spent before the request leaves.
READ_METHODS = frozenset({"session-get", "torrent-get", "free-space"})
WRITE_METHODS = frozenset({"torrent-add", "torrent-stop"})

STATUS_NAMES = {0: "stopped", 1: "queued to verify", 2: "verifying", 3: "queued to download", 4: "downloading",
                5: "queued to seed", 6: "seeding"}

STEP_TITLES = {
    "01": "handshake: session-get with no session id (409 expected)",
    "02": "session-get with a fields allowlist",
    "03": "free-space: default download folder, scratch folder, library folder, missing folder",
    "04": "torrent-get of a hash Transmission doesn't have",
    "05": "torrent-get of every torrent",
    "06": "WRITE torrent-add of Big Buck Bunny into the scratch folder, with a label",
    "07": "WRITE the same torrent-add again (torrent-duplicate expected)",
    "08": "torrent-get of Big Buck Bunny right after the add",
    "09": "WRITE torrent-add of a corrupt .torrent (refusal expected)",
    "10": "WRITE torrent-add with a relative download-dir (refusal expected)",
    "11": "WRITE torrent-add of a magnet with no metadata, then torrent-get of it",
    "12": "WRITE attempt: torrent-add with a stale session id (409 expected), then proof nothing was added",
    "13": "waiting for Big Buck Bunny to complete, then torrent-get while seeding",
    "14": "WRITE torrent-stop of Big Buck Bunny, then torrent-get",
    "15": "one request with a wrong password (401 expected)",
    "16": "one request with a Host name Transmission doesn't know",
}
FULL_STEPS = list(STEP_TITLES)
SHORT_STEPS = ["01", "02", "04", "06", "08", "07"]


class WriteRefused(Exception):
    """The script tried a request it may not send: a bug, never an answer from Transmission, so it is not a ToolError
    (those are said in one sentence and the run goes on or ends cleanly)."""


@dataclass
class Torrent:
    title: str
    data: bytes
    facts: common.TorrentFacts
    source: str

    @property
    def metainfo(self) -> str:
        return base64.b64encode(self.data).decode("ascii")

    @property
    def hash(self) -> str:
        return self.facts.v1_hash or ""

    def describe(self) -> str:
        return (f"{self.facts.name!r}: {self.facts.file_count} file(s), {common.human_size(self.facts.total_length)}, "
                f"v1 info hash {self.hash}, .torrent {self.facts.size} bytes")


@dataclass
class Exchange:
    method: str
    request_text: str
    reply: Reply | None = None
    error: str = ""
    first_409: Reply | None = None

    @property
    def body(self) -> dict:
        parsed = self.reply.json if self.reply is not None else None
        return parsed if isinstance(parsed, dict) else {}

    @property
    def result(self):
        return self.body.get("result")

    @property
    def arguments(self) -> dict:
        found = self.body.get("arguments")
        return found if isinstance(found, dict) else {}

    @property
    def torrents(self) -> list[dict]:
        found = self.arguments.get("torrents")
        return [t for t in found if isinstance(t, dict)] if isinstance(found, list) else []


_CURRENT = object()


def shown_body(envelope: dict) -> str:
    """The request body for raw/: a .torrent's base64 is replaced by its size, so a file never lands there."""
    shown = json.loads(json.dumps(envelope))
    arguments = shown.get("arguments") or {}
    if isinstance(arguments.get("metainfo"), str):
        size = len(base64.b64decode(arguments["metainfo"]))
        arguments["metainfo"] = f"<base64, {size} bytes>"
    return json.dumps(shown, indent=2, ensure_ascii=False)


class Rpc:
    """The legacy RPC: POST {"method", "arguments", "tag"} to the RPC address with HTTP Basic credentials and the CSRF
    session id. A 409 names the session id to use and means the request was refused before it was read, so it is sent
    once more with that id (the protocol, docs/rpc-spec.md 4.0.6 §2.3.1; §2.2.1 at 4.1.3); nothing else is ever
    repeated, a write included."""

    def __init__(self, url: str, user: str, password: str, timeout: float = 30.0) -> None:
        parts = common.check_address(url, "The RPC address")
        self.url = urllib.parse.urlunsplit(parts)
        self.parts = parts
        self.user = user
        self.timeout = timeout
        self.session_id: str | None = None
        self._permit: str | None = None
        self._tag = 0
        self._auth: str | None = None
        self._wrong_auth: str | None = None
        if user:
            token = base64.b64encode(f"{user}:{password}".encode("utf-8")).decode("ascii")
            common.SECRETS.add(token)  # the header carries the password, base64 encoded
            self._auth = f"Basic {token}"
            wrong = WRONG_PASSWORD if password != WRONG_PASSWORD else WRONG_PASSWORD + "-2"
            self._wrong_auth = "Basic " + base64.b64encode(f"{user}:{wrong}".encode("utf-8")).decode("ascii")

    def __repr__(self) -> str:  # never the password: a repr ends up in tracebacks
        return f"Rpc({self.url!r}, user={self.user!r})"

    @property
    def wrong_auth(self) -> str | None:
        return self._wrong_auth

    def permit(self, method: str) -> None:
        if method not in WRITE_METHODS:
            raise WriteRefused(f"{method} is not a write this script sends.")
        self._permit = method

    def call(self, method: str, arguments: dict, *, session_id=_CURRENT, auth=_CURRENT, host: str | None = None,
             handshake: bool = True, timeout: float | None = None) -> Exchange:
        if method not in READ_METHODS and method not in WRITE_METHODS:
            raise WriteRefused(f"{method} is not a method this script may send.")
        if method in WRITE_METHODS:
            if self._permit != method:
                raise WriteRefused(f"{method} without a typed y for it.")
            self._permit = None  # spent before the request leaves: a timeout can't lead to a second write
        self._tag += 1
        envelope = {"method": method, "arguments": arguments, "tag": self._tag}
        payload = json.dumps(envelope, ensure_ascii=False).encode("utf-8")
        headers = {"Content-Type": "application/json", "Accept": "application/json"}
        sent_id = self.session_id if session_id is _CURRENT else session_id
        if sent_id:
            headers["X-Transmission-Session-Id"] = sent_id
        credential = self._auth if auth is _CURRENT else auth
        if credential:
            headers["Authorization"] = credential
        exchange = Exchange(method, common.describe_request("POST", self.url, headers, shown_body(envelope), host))
        wait = timeout or self.timeout
        try:
            reply = common.http_request("POST", self.url, headers=headers, body=payload, timeout=wait, host_header=host)
            self._learn(reply)
            if (reply.status == 409 and handshake and session_id is _CURRENT and self.session_id
                    and self.session_id != sent_id):
                exchange.first_409 = reply
                headers["X-Transmission-Session-Id"] = self.session_id
                reply = common.http_request("POST", self.url, headers=headers, body=payload, timeout=wait,
                                            host_header=host)
                self._learn(reply)
            exchange.reply = reply
        except HttpFailure as failure:
            exchange.error = str(failure)
        return exchange

    def _learn(self, reply: Reply) -> None:
        session_id = reply.header("X-Transmission-Session-Id")
        if session_id:
            common.SECRETS.add(session_id)  # registered before anything can print or write it
            self.session_id = session_id


class Run:
    """What one run shares."""

    def __init__(self, args: argparse.Namespace, rpc: Rpc, rec: common.Recorder, scratch: str, library: str) -> None:
        self.args = args
        self.rpc = rpc
        self.rec = rec
        self.scratch = scratch
        self.library = library
        self.bbb: Torrent | None = None
        self.second: Torrent | None = None
        self.session: dict = {}
        self.version = ""
        self.rpc_version: int | None = None
        self.semver = ""
        self.path_refusal = ""                 # why writes are refused because of the folders typed
        self.scratch_seen: bool | None = None  # what free-space said about the scratch folder
        self.scratch_answer = ""
        self.scratch_unclear = False           # free-space of it got no answer from Transmission's RPC (a 500, none)
        self.magnet_hash = token_source.token_hex(20)
        self.cleanup: dict[str, tuple[str, str]] = {}  # hash -> (name, where)
        self.outcomes: list[tuple[str, str]] = []


# ---------------------------------------------------------------- small helpers

def excerpt(text: str, limit: int = 160) -> str:
    # Redacted before it is cut: a cut through a secret (a proxy page that quotes the request's headers) would leave a
    # piece that no longer matches the secret, and say() would print it.
    plain = " ".join(common.redact_text(re.sub(r"<[^>]+>", " ", text)).split())
    return plain if len(plain) <= limit else plain[:limit - 3] + "..."


def verdict(ex: Exchange) -> str:
    if ex.reply is None:
        return f"NO ANSWER: {ex.error}"
    reply = ex.reply
    text = ("409, then with the session id it named: " if ex.first_409 is not None else "")
    text += f"HTTP {reply.status} {reply.reason} in {reply.seconds:.2f} s"
    if ex.result is not None:
        text += f"; result {ex.result!r}"
    elif not reply.ok:
        text += f"; {excerpt(reply.text) or '(empty body)'}"
    return text


def outcome(run: Run, step: str, text: str) -> None:
    run.outcomes.append((step, text))


def skip(run: Run, step: str, reason: str) -> None:
    say(f"  SKIPPED: {reason}")
    outcome(run, step, f"skipped: {reason}")


def record(run: Run, raw_name: str, ex: Exchange, what: str, pending: str | None = None, note: str = "") -> None:
    """Prints the verdict and writes raw/<raw_name>.txt (the request as sent and the whole answer) and, when given, the
    candidate: the JSON body of a 200 as .json, anything else as -response.txt (status line, headers, body)."""
    say(f"  -> {verdict(ex)}")
    head = [f"# MAM-29 {raw_name}: {what}", f"# {common.now_iso()}, {run.rec.version_label}"]
    if note:
        head.append(f"# {note}")
    text = "\n".join(head) + "\n\n" + ex.request_text
    if ex.first_409 is not None:
        text += ("\n--- first answer: 409, so the same request went again with the session id it named "
                 "(docs/rpc-spec.md 4.0.6 §2.3.1)\n" + ex.first_409.as_sample())
    if ex.reply is not None:
        text += f"\n--- answer after {ex.reply.seconds:.3f} s\n" + ex.reply.as_sample()
    else:
        text += f"\n--- no answer: {ex.error}\n"
    run.rec.raw(raw_name + ".txt", text, what)
    if pending and ex.reply is not None:
        parsed = ex.reply.json
        if pending.endswith(".json") and ex.reply.status == 200 and parsed is not None:
            run.rec.pending(pending, json.dumps(parsed, indent=2, ensure_ascii=False) + "\n", what)
        else:
            name = pending if pending.endswith("-response.txt") else pending.rsplit(".", 1)[0] + "-response.txt"
            run.rec.pending(name, ex.reply.as_sample(), what)


def add_outcome(ex: Exchange) -> tuple[str | None, str]:
    """('torrent-added' | 'torrent-duplicate' | None, a description) of a torrent-add answer."""
    if ex.reply is None or ex.reply.status != 200:
        return None, verdict(ex)
    for key in ("torrent-added", "torrent-duplicate"):
        torrent = ex.arguments.get(key)
        if isinstance(torrent, dict):
            return key, (f"result {ex.result!r}, {key}: id {torrent.get('id')}, name {torrent.get('name')!r}, "
                         f"hashString {torrent.get('hashString')}")
    shown = common.redact_text(json.dumps(ex.arguments, ensure_ascii=False))  # redacted before it is cut, as in excerpt()
    return None, f"result {ex.result!r}, arguments {shown[:200]}"


def status_text(torrent: dict) -> str:
    status = torrent.get("status")
    name = STATUS_NAMES.get(status, "?") if isinstance(status, int) else "?"
    percent = torrent.get("percentDone")
    shown = f"{percent * 100:.1f} %" if isinstance(percent, (int, float)) else "? %"
    error = torrent.get("error")
    flag = f", error {error}: {torrent.get('errorString')!r}" if error else ""
    return f"status {status} ({name}), {shown}{flag}"


def stop_if_refused(run: Run, ex: Exchange) -> None:
    """The answers after which no request can succeed: said in one sentence each, with the fix."""
    if ex.reply is None:
        raise Stop(f"Transmission did not answer: {ex.error}.")
    status = ex.reply.status
    if status == 401:
        if run.rpc.user:
            raise Stop("Transmission refused the user name or password (HTTP 401). Check USER and PASS of the "
                       "transmission container in Stashboard and run again; the script tried once and stopped, so "
                       "Transmission's lock after repeated wrong passwords (only when anti-brute-force-enabled is on in "
                       "its settings.json; it is off by default) is never reached.")
        raise Stop("Transmission asks for a user name and password (HTTP 401): run again and type the USER and PASS "
                   "of the transmission container.")
    if status == 403:
        # libtransmission/rpc-server.cc: the brute-force lock (only with anti-brute-force-enabled, off by default) or
        # the IP whitelist, which the linuxserver image switches on only when WHITELIST is set.
        raise Stop("Transmission refused this PC (HTTP 403). Most likely its IP whitelist is on and doesn't list this "
                   "PC's address: in Stashboard, add this PC's address to the WHITELIST variable of the transmission "
                   "container (comma-separated; empty means no whitelist) and restart it. Otherwise it is the lock "
                   "after repeated wrong passwords, which exists only when anti-brute-force-enabled is switched on in "
                   "its settings.json (off by default): restarting the container clears it. The script never changes "
                   "either.")
    if status == 404:
        raise Stop("Nothing at that address answers as Transmission's RPC (HTTP 404). The address ends in "
                   f"/transmission/rpc, e.g. {common.DEFAULT_TRANSMISSION_URL}.")
    if status == 421:
        raise Stop("Transmission refused the host name in the address (HTTP 421): use its IP address, e.g. "
                   f"{common.DEFAULT_TRANSMISSION_URL}.")
    if 300 <= status < 400:
        raise Stop(f"Transmission answered with a redirect (HTTP {status}) to {ex.reply.header('Location')!r}: "
                   "the RPC address ends in /transmission/rpc.")


def answered(ex: Exchange) -> bool:
    """Transmission read the request and did it: HTTP 200 and result 'success'."""
    return ex.reply is not None and ex.reply.status == 200 and ex.result == "success"


def find_torrent(run: Run, info_hash: str) -> tuple[dict | None, str]:
    """(the torrent with that hash or None, why Transmission could not be asked — empty when it answered), read without
    recording, to decide whether a step makes sense. A failed read is not "no such torrent": a step that would add the
    torrent again, or prove that nothing was added, must not go on from it."""
    ex = run.rpc.call("torrent-get", {"ids": [info_hash], "fields": SMALL_FIELDS + ["error", "errorString"]})
    if not answered(ex):
        why = verdict(ex)
        say(f"  (could not check whether {info_hash} is in Transmission: {why})")
        return None, why
    return (ex.torrents[0] if ex.torrents else None), ""


def scratch_problem(run: Run) -> str:
    """Why nothing may be written: a scratch folder typed inside the media folders, or one Transmission can't see. An
    unmounted folder would be created inside the container instead of on the host."""
    if run.path_refusal:
        return run.path_refusal
    if run.scratch_seen is None:
        learn_scratch(run, run.rpc.call("free-space", {"path": run.scratch}), None)
    if not run.scratch_seen and run.scratch_unclear:
        # Not "the folder is missing": Transmission was not heard, so the mount advice would send the owner astray.
        return (f"Transmission could not be asked whether it sees the scratch folder {run.scratch} (free-space: "
                f"{run.scratch_answer}), so no write is sent. Run again once it answers.")
    if not run.scratch_seen:
        return (f"Transmission can't see the scratch folder {run.scratch} (free-space answered: {run.scratch_answer}). "
                "Add the scratch mount to the transmission container in Stashboard, outside the media folders, "
                "restart it and run again.")
    return ""


def learn_scratch(run: Run, ex: Exchange, text: str | None) -> None:
    run.scratch_seen = answered(ex)
    run.scratch_unclear = not (ex.reply is not None and ex.reply.status == 200 and ex.result is not None)
    run.scratch_answer = text if text is not None else verdict(ex)


def check_paths(run: Run) -> None:
    """The samples must never land in the media folders: production MAM would list them under "Files in no library"."""
    scratch, library = run.scratch.rstrip("/") or "/", run.library.rstrip("/") or "/"
    if scratch == "/" or library == "/":
        run.path_refusal = "a folder typed is the root folder, which holds the media folders and every other one"
    elif scratch == library or scratch.startswith(library + "/") or library.startswith(scratch + "/"):
        run.path_refusal = (f"the scratch folder {scratch} and the library folder {library} overlap: the samples must "
                            "stay outside the media folders. Run again with a scratch folder of its own, e.g. /scratch.")
    download_dir = str(run.session.get("download-dir") or "").rstrip("/")
    if download_dir and not run.path_refusal and (scratch == download_dir or scratch.startswith(download_dir + "/")):
        say(f"  note: the scratch folder lies in Transmission's default download folder {download_dir}; make sure that "
            "folder is not one of the media folders.")
    # A torrent downloads into the temporary folder whatever its download-dir, and moves only once complete. The
    # linuxserver image turns it on (/downloads/incomplete, inside the shared media folder), so the owner switches it
    # off before the run; until then the samples could sit among the media files.
    incomplete = str(run.session.get("incomplete-dir") or "").rstrip("/") or "/"
    if (run.session.get("incomplete-dir-enabled") is True and not run.path_refusal
            and incomplete != scratch and not incomplete.startswith(scratch + "/")):
        run.path_refusal = (f"Transmission's temporary folder is on (incomplete-dir-enabled true, as the linuxserver "
                            f"image sets it), so Big Buck Bunny would download into {incomplete} and reach the scratch "
                            f"folder {scratch} only once complete. In Transmission's web UI ({web_ui_address(run)}) "
                            "choose Edit preferences, Torrents, untick \"Use temporary folder:\" and run again.")
    if run.path_refusal:
        say(f"  !!! Writes are refused for this run: {run.path_refusal}")


def ask_write(run: Run, method: str, arguments: dict, effect: list[str]) -> bool:
    shown = []
    for key, value in arguments.items():
        if key == "metainfo" and isinstance(value, str):
            value = f"<base64 of a {len(base64.b64decode(value))}-byte .torrent>"
        else:
            value = json.dumps(value, ensure_ascii=False)
        shown.append(f"  {key:<14}{value}")
    say()
    box([f"WRITE to Transmission: {method}", *shown, "", *effect])
    return common.confirm("Send it? Type y to send, anything else skips this step")


# ---------------------------------------------------------------- .torrent files

def fetch_torrent(url: str) -> bytes:
    """A public .torrent over HTTPS onto this PC, into memory. urllib, not http.client: this goes to the internet, where
    a system proxy, if any, is right, and webtorrent.io may redirect."""
    if not url.lower().startswith("https://"):
        raise ToolError(f"{url} is not an https address; the .torrent is fetched over HTTPS only.")
    request = urllib.request.Request(url, headers={"User-Agent": "MAM-29-samples"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            if not response.geturl().lower().startswith("https://"):
                raise ToolError(f"{url} redirected to a plain http address; download it in a browser and pass it with "
                                "--torrent PATH.")
            data = response.read(MAX_TORRENT_BYTES + 1)
    except urllib.error.HTTPError as error:
        raise ToolError(f"{urllib.parse.urlsplit(url).hostname} answered HTTP {error.code} for {url}; download it in a "
                        "browser and pass it with --torrent PATH.") from None
    except (urllib.error.URLError, OSError) as error:
        reason = getattr(error, "reason", None) or error
        raise ToolError(f"could not download {url} ({reason}); download it in a browser and pass it with "
                        "--torrent PATH.") from None
    if len(data) > MAX_TORRENT_BYTES:
        raise ToolError(f"{url} is larger than {common.human_size(MAX_TORRENT_BYTES)}, which no .torrent is.")
    return data


def load_torrent(title: str, path: str | None, default_url: str, option: str) -> Torrent:
    if path:
        try:
            data = Path(path).read_bytes()
        except OSError as error:
            raise Stop(f"{option} {path}: {error.strerror or error}.") from None
        source = f"the file {path}"
    else:
        say(f"Downloading {default_url} over HTTPS (memory only, never saved)...")
        data = fetch_torrent(default_url)
        source = default_url
    try:
        facts = common.read_torrent(data)
    except common.BencodeError as error:
        raise Stop(f"{title} from {source} is not a .torrent: {error}.") from None
    if facts.v2_only or not facts.v1_hash:
        raise Stop(f"{title} from {source} is a BitTorrent v2-only torrent, which has no v1 info hash for the samples; "
                   f"give a v1 or hybrid .torrent with {option}.")
    torrent = Torrent(title, data, facts, source)
    say(f"{title}: {torrent.describe()}, from {source}")
    return torrent


# ---------------------------------------------------------------- the steps

def step_01(run: Run) -> None:
    ex = run.rpc.call("session-get", {"fields": ["version"]}, session_id=None, handshake=False)
    status = ex.reply.status if ex.reply is not None else None
    if status == 409:
        found = "named" if run.rpc.session_id else "did NOT name"
        text = f"409 Conflict; it {found} the session id in X-Transmission-Session-Id (value REDACTED) and in the page"
        # 4.1 adds its RPC semver to the 409 (rpc-server.cc), 4.0 sends Access-Control-Expose-Headers instead.
        semver = ex.reply.header("X-Transmission-Rpc-Version")
        text += (f"; it also sent X-Transmission-Rpc-Version {semver}" if semver
                 else "; no X-Transmission-Rpc-Version header (4.1 sends one, 4.0 doesn't)")
    elif status == 200 and not isinstance(ex.reply.json, dict):
        text = f"{verdict(ex)}, but the body is not JSON: this address does not answer as Transmission's RPC"
    elif status == 200:
        text = "200 at once: this Transmission asks for no session id"
    else:
        text = verdict(ex)
    # The words before the colon must not be the header's name: its redaction would take the status for its value.
    record(run, "01-handshake", ex, f"session-get sent with no session id header, {text}", "handshake-response.txt")
    stop_if_refused(run, ex)
    if status == 409 and not run.rpc.session_id:
        raise Stop("Transmission answered 409 without an X-Transmission-Session-Id header, so no request can be made.")
    outcome(run, "01", text)


def step_02(run: Run) -> None:
    ex = run.rpc.call("session-get", {"fields": SESSION_FIELDS})
    if not answered(ex):
        # The answer that stops the run is kept in raw/ (it is how the owner sees what came back); no candidate,
        # because it is not the session-get sample.
        record(run, "02-session-get", ex, f"session-get with the fields {', '.join(SESSION_FIELDS)}: {verdict(ex)}")
    stop_if_refused(run, ex)
    if ex.reply.status == 200 and not isinstance(ex.reply.json, dict):
        raise Stop(f"The address answered session-get with {verdict(ex)} but not with JSON, so it is not Transmission's "
                   "RPC (a proxy's login page?). The RPC address ends in /transmission/rpc, e.g. "
                   f"{common.DEFAULT_TRANSMISSION_URL}.")
    if ex.reply.status >= 500:
        raise Stop(f"Transmission answered session-get with {verdict(ex)}: it, or a proxy in front of it, failed. Check "
                   "that the transmission container runs in Stashboard, look at its log there, and run again.")
    if ex.reply.status != 200 or ex.result != "success":
        raise Stop(f"Transmission answered session-get with {verdict(ex)}; HTTP 200 and result 'success' were expected.")
    args = ex.arguments
    run.session = args
    run.version = str(args.get("version") or "")
    run.rpc_version = args.get("rpc-version") if isinstance(args.get("rpc-version"), int) else None
    run.semver = str(args.get("rpc-version-semver") or "")
    first_word = run.version.split()[0] if run.version else "unknown"
    run.rec.version_label = (f"Transmission {run.version or '(version not given)'}, rpc-version {run.rpc_version}, "
                             f"rpc-version-semver {run.semver or '?'}")
    folder = run.rec.open(f"transmission-{first_word}")
    say(f"  Transmission {run.version}; rpc-version {run.rpc_version} (minimum {args.get('rpc-version-minimum')}), "
        f"rpc-version-semver {run.semver}")
    for key in SESSION_FIELDS[4:]:
        say(f"  {key:<24}{json.dumps(args.get(key), ensure_ascii=False)}")
    say(f"  Output folder: {folder}")
    missing = [key for key in SESSION_FIELDS if key not in args]
    if missing:
        say(f"  note: session-get did not answer {', '.join(missing)}")
    major = re.match(r"(\d+)\.(\d+)", run.version)
    too_old = (run.rpc_version is not None and run.rpc_version < FIRST_LEGACY_LABELS_RPC) or (major and int(major[1]) < 4)
    if too_old:
        say(f"  !!! Transmission {run.version} found: MAM needs 4.0 or newer (torrent-add takes labels from 4.0, "
            "rpc-version 17). The samples are recorded all the same.")
    # --only reruns steps of a set already recorded (13 and 14 after a long download), so no note then.
    if major and not run.args.short and not run.args.only and (int(major[1]), int(major[2])) < (4, 1):
        say("  note: the full set is meant for 4.1.3; --short is the set for a 4.0.x container.")
    if major and run.args.short and (int(major[1]), int(major[2])) >= (4, 1):
        # Its files would land in the full set's folder and replace what the full run recorded there.
        raise Stop(f"--short is the set for the temporary 4.0.x container, and this is Transmission {run.version}. "
                   f"Run again and type {common.DEFAULT_TRANSMISSION_40_URL} (the default) at the first question.")
    text = (f"Transmission {run.version}, rpc-version {run.rpc_version}, download-dir {args.get('download-dir')!r}, "
            f"incomplete-dir-enabled {args.get('incomplete-dir-enabled')}")
    record(run, "02-session-get", ex, f"session-get with the fields {', '.join(SESSION_FIELDS)}: {text}", "session-get.json")
    outcome(run, "02", text)
    check_paths(run)


def describe_free_space(ex: Exchange) -> str:
    if ex.reply is None or ex.reply.status != 200:
        return verdict(ex)
    if ex.result != "success":
        return f"result {ex.result!r}"
    free, total = ex.arguments.get("size-bytes"), ex.arguments.get("total_size")
    return (f"size-bytes {free} ({common.human_size(free)} free), total_size {total} ({common.human_size(total)})")


def step_03(run: Run) -> None:
    targets = []
    download_dir = run.session.get("download-dir")
    if isinstance(download_dir, str) and download_dir:
        targets.append(("a", "download-dir", download_dir, "Transmission's default download folder"))
    targets += [("b", "scratch", run.scratch, "the scratch folder"),
                ("c", "library", run.library, "the library folder typed"),
                ("d", "missing-folder", posixpath.join(run.scratch, MISSING_FOLDER_NAME), "a folder that doesn't exist")]
    texts = []
    for letter, slug, path, label in targets:
        say(f"  free-space {path} ({label})")
        ex = run.rpc.call("free-space", {"path": path})
        text = describe_free_space(ex)
        record(run, f"03{letter}-free-space-{slug}", ex, f"free-space of {path}, {label}: {text}", f"free-space-{slug}.json")
        if slug == "scratch":
            learn_scratch(run, ex, text)
        texts.append(f"{slug}: {text}")
    outcome(run, "03", "; ".join(texts))


def step_04(run: Run) -> None:
    unknown = token_source.token_hex(20)
    ex = run.rpc.call("torrent-get", {"ids": [unknown], "fields": SMALL_FIELDS})
    # An error answer has no torrents either; it must not be filed as "0 torrent(s)".
    text = f"{len(ex.torrents)} torrent(s) for the made-up hash {unknown}" if answered(ex) else verdict(ex)
    record(run, "04-torrent-get-unknown-hash", ex, f"torrent-get of a hash no torrent has: {text}",
           "torrent-get-unknown-hash.json")
    outcome(run, "04", text)


def step_05(run: Run) -> None:
    ex = run.rpc.call("torrent-get", {"fields": SMALL_FIELDS})
    count = len(ex.torrents)
    text = f"{count} torrent(s)" if answered(ex) else verdict(ex)
    if count:
        text += " (the sample lists them all: read it before it is copied)"
    record(run, "05-torrent-get-all", ex, f"torrent-get of every torrent, fields {', '.join(SMALL_FIELDS)}: {text}",
           "torrent-get-all.json")
    outcome(run, "05", text)


def bbb_add_arguments(run: Run) -> dict:
    return {"metainfo": run.bbb.metainfo, "download-dir": run.scratch, "labels": [LABEL], "paused": False}


def remember(run: Run, info_hash: str, name: str, where: str) -> None:
    run.cleanup[info_hash] = (name, where)


def unanswered_add(run: Run, ex: Exchange, info_hash: str, name: str, where: str) -> None:
    """A torrent-add that got no answer (a timeout, a dropped connection) may have been added all the same: Transmission
    can read it and fail to answer in time. It is never sent again; it goes on the list of torrents to delete, so the
    list is right even when Transmission can't be read at the end."""
    if ex.reply is None:
        say(f"  !!! No answer to the add: Transmission may have added {name} all the same. It is not sent again; look "
            "for it in the web UI (it is listed at the end).")
        remember(run, info_hash, name, f"{where} (perhaps: the add got no answer)")


def step_06(run: Run) -> None:
    reason = scratch_problem(run)
    if reason:
        return skip(run, "06", reason)
    arguments = bbb_add_arguments(run)
    effect = [f"Adds {run.bbb.facts.name!r} ({run.bbb.facts.file_count} files, "
              f"{common.human_size(run.bbb.facts.total_length)}) and STARTS downloading it into {run.scratch} at once.",
              "It stays in Transmission until you delete it in the web UI; this script never removes a torrent."]
    if not ask_write(run, "torrent-add", arguments, effect):
        return skip(run, "06", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-add")
    ex = run.rpc.call("torrent-add", arguments)
    key, text = add_outcome(ex)
    if key:
        remember(run, run.bbb.hash, run.bbb.facts.name, run.scratch)
    unanswered_add(run, ex, run.bbb.hash, run.bbb.facts.name, run.scratch)
    pending = {"torrent-added": "torrent-add-added.json", "torrent-duplicate": "torrent-add-duplicate.json"}.get(
        key or "", "torrent-add-refused.json")
    note = ("Big Buck Bunny was already in Transmission (an earlier run), so the answer is torrent-duplicate; delete it "
            "with its data and run again to record torrent-added" if key == "torrent-duplicate" else "")
    if note:
        say(f"  note: {note}")
    record(run, "06-torrent-add", ex, f"torrent-add of Big Buck Bunny (metainfo, download-dir {run.scratch}, labels "
           f"[\"{LABEL}\"], paused false): {text}", pending, note)
    outcome(run, "06", text)


def step_07(run: Run) -> None:
    reason = scratch_problem(run)
    if reason:
        return skip(run, "07", reason)
    found, unreadable = find_torrent(run, run.bbb.hash)
    if unreadable:
        return skip(run, "07", "Transmission could not be asked whether it has Big Buck Bunny, so the same add was not "
                               "sent: it could add it instead of showing the duplicate answer. Run again with --only 07")
    if found is None:
        return skip(run, "07", "Big Buck Bunny is not in Transmission, so the same add would add it instead of "
                               "showing the duplicate answer (run step 06 first)")
    arguments = bbb_add_arguments(run)
    if not ask_write(run, "torrent-add", arguments, ["The same add as step 06. Transmission already has this torrent, "
                                                     "so it should answer torrent-duplicate and change nothing."]):
        return skip(run, "07", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-add")
    ex = run.rpc.call("torrent-add", arguments)
    key, text = add_outcome(ex)
    pending = "torrent-add-duplicate.json" if key == "torrent-duplicate" else "torrent-add-second-time.json"
    record(run, "07-torrent-add-again", ex, f"the same torrent-add of Big Buck Bunny again: {text}", pending)
    outcome(run, "07", text)


def step_08(run: Run) -> None:
    found, unreadable = find_torrent(run, run.bbb.hash)
    if unreadable:
        return skip(run, "08", f"Transmission could not be read just now ({unreadable})")
    if found is None:
        return skip(run, "08", "Big Buck Bunny is not in Transmission (step 06 did not add it)")
    ex = run.rpc.call("torrent-get", {"ids": [run.bbb.hash], "fields": FULL_FIELDS})
    torrent = ex.torrents[0] if ex.torrents else {}
    text = status_text(torrent) if torrent else verdict(ex)
    complete = isinstance(torrent.get("percentDone"), (int, float)) and torrent["percentDone"] >= 1
    if complete:
        text += "; ALREADY COMPLETE when read, so this is not a downloading state"
    pending = "torrent-get-after-add-complete.json" if complete else "torrent-get-downloading.json"
    record(run, "08-torrent-get-after-add", ex, f"torrent-get of Big Buck Bunny right after the add, fields "
           f"{', '.join(FULL_FIELDS)}: {text}", pending)
    outcome(run, "08", text)


def loud_added(run: Run, step: str, ex: Exchange, name: str, info_hash: str, why: str) -> None:
    say(f"  !!! Transmission ADDED {name} ({why}). Delete it, with its data, in Transmission's web UI; it is listed "
        "again at the end.")
    remember(run, info_hash, name, why)


def step_09(run: Run) -> None:
    reason = scratch_problem(run)
    if reason:
        return skip(run, "09", reason)
    arguments = {"metainfo": base64.b64encode(b"not a torrent").decode("ascii"), "download-dir": run.scratch,
                 "labels": [LABEL], "paused": True}
    if not ask_write(run, "torrent-add", arguments, ["The metainfo is the 13 bytes 'not a torrent'; Transmission should "
                                                     f"refuse it ({CORRUPT_EXPECTED!r}) and add nothing."]):
        return skip(run, "09", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-add")
    ex = run.rpc.call("torrent-add", arguments)
    key, text = add_outcome(ex)
    if key == "torrent-added":
        added = ex.arguments["torrent-added"]
        loud_added(run, "09", ex, str(added.get("name")), str(added.get("hashString")), "the corrupt .torrent")
    elif key is None and ex.result == CORRUPT_EXPECTED:
        text += " (the refusal the source predicts)"
    elif key is None and isinstance(ex.result, str):
        # Kept whatever came: MAM-30 maps the answer recorded here, and INDEX.txt says which one it was.
        text += f" (not the {CORRUPT_EXPECTED!r} the source predicts; this is what MAM-30 must map)"
    record(run, "09-torrent-add-corrupt", ex, f"torrent-add with metainfo base64('not a torrent'): {text}",
           "torrent-add-corrupt-metainfo.json")
    outcome(run, "09", text)


def step_10(run: Run) -> None:
    reason = scratch_problem(run)
    if reason:
        return skip(run, "10", reason)
    second = run.second
    if find_torrent(run, second.hash)[0] is not None:
        say(f"  note: {second.facts.name!r} is already in Transmission (an earlier run?): the answer may be its duplicate "
            "rather than the folder check.")
    arguments = {"metainfo": second.metainfo, "download-dir": "relative/path", "labels": [LABEL], "paused": True}
    if not ask_write(run, "torrent-add", arguments, [f"The second torrent, {second.facts.name!r}, with a relative folder. "
                                                     "Transmission should refuse it and add nothing; paused, so even a "
                                                     "wrong acceptance downloads nothing."]):
        return skip(run, "10", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-add")
    ex = run.rpc.call("torrent-add", arguments)
    key, text = add_outcome(ex)
    if key == "torrent-added":
        loud_added(run, "10", ex, second.facts.name, second.hash, "with the relative folder 'relative/path'")
    record(run, "10-torrent-add-relative-folder", ex, f"torrent-add of {second.facts.name} with download-dir "
           f"'relative/path': {text}", "torrent-add-relative-download-dir.json")
    outcome(run, "10", text)


def step_11(run: Run) -> None:
    reason = scratch_problem(run)
    if reason:
        return skip(run, "11", reason)
    magnet = f"magnet:?xt=urn:btih:{run.magnet_hash}&dn=mam29-no-metadata"
    arguments = {"filename": magnet, "download-dir": run.scratch, "labels": [LABEL], "paused": False}
    if not ask_write(run, "torrent-add", arguments, ["A magnet of a made-up hash: no peer has it, so it stays without "
                                                     "metadata and downloads nothing. Delete it in the web UI later."]):
        return skip(run, "11", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-add")
    ex = run.rpc.call("torrent-add", arguments)
    key, text = add_outcome(ex)
    if key:
        remember(run, run.magnet_hash, "mam29-no-metadata", f"{run.scratch} (no data: it never got metadata)")
    unanswered_add(run, ex, run.magnet_hash, "mam29-no-metadata", run.scratch)
    record(run, "11a-torrent-add-magnet", ex, f"torrent-add of a magnet with no tracker for a made-up hash: {text}",
           "torrent-add-magnet-added.json")
    time.sleep(1.0)
    get = run.rpc.call("torrent-get", {"ids": [run.magnet_hash], "fields": FULL_FIELDS})
    torrent = get.torrents[0] if get.torrents else {}
    if torrent:
        files = torrent.get("files")
        get_text = (f"{status_text(torrent)}, metadataPercentComplete {torrent.get('metadataPercentComplete')}, "
                    f"{len(files) if isinstance(files, list) else '?'} file(s), name {torrent.get('name')!r}")
    else:
        get_text = "no torrent" if answered(get) else verdict(get)
    record(run, "11b-torrent-get-magnet", get, f"torrent-get of the magnet right after its add: {get_text}",
           "torrent-get-magnet-no-metadata.json")
    outcome(run, "11", f"{text}; then {get_text}")


def step_12(run: Run) -> None:
    reason = scratch_problem(run)
    if reason:
        return skip(run, "12", reason)
    second = run.second
    found, unreadable = find_torrent(run, second.hash)
    if unreadable:
        return skip(run, "12", f"Transmission could not be asked whether it already has {second.facts.name!r}, so the "
                               "torrent-get afterwards could not prove that the refused add added nothing; nothing was "
                               "sent. Run again with --only 12")
    if found is not None:
        return skip(run, "12", f"{second.facts.name!r} is already in Transmission, so the torrent-get afterwards could not "
                               "prove that the refused add added nothing. Delete it with its data in the web UI and run "
                               "again with --only 12")
    arguments = {"metainfo": second.metainfo, "download-dir": run.scratch, "labels": [LABEL], "paused": False}
    stale = "mam29-stale-" + token_source.token_hex(18)
    if not ask_write(run, "torrent-add", arguments, [
            f"The second torrent, {second.facts.name!r}, sent with a made-up X-Transmission-Session-Id.",
            "Transmission should refuse it with 409 before reading it. The script does NOT send it again; a torrent-get",
            "afterwards proves nothing was added."]):
        return skip(run, "12", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-add")
    ex = run.rpc.call("torrent-add", arguments, session_id=stale, handshake=False)
    key, text = add_outcome(ex)
    if ex.reply is not None and ex.reply.status == 409:
        text = "409 Conflict: refused before it was read; the session id to use is in X-Transmission-Session-Id (REDACTED)"
    if key == "torrent-added":
        loud_added(run, "12", ex, second.facts.name, second.hash, f"in {run.scratch}, despite the stale session id")
    record(run, "12a-torrent-add-stale-session", ex, f"torrent-add of {second.facts.name} with a stale session id, not "
           f"repeated: {text}", "torrent-add-stale-session-response.txt",
           "the X-Transmission-Session-Id sent is a made-up one, not Transmission's")
    get = run.rpc.call("torrent-get", {"ids": [second.hash], "fields": SMALL_FIELDS})
    if answered(get):
        get_text = ("no torrent: the refused add added nothing" if not get.torrents
                    else f"!!! the torrent IS there: {status_text(get.torrents[0])}")
    else:
        get_text = verdict(get)
    if get.torrents:
        say(f"  !!! {second.facts.name!r} is in Transmission after the refused add; delete it with its data in the web UI.")
        remember(run, second.hash, second.facts.name, run.scratch)
    record(run, "12b-torrent-get-after-stale-session", get, f"torrent-get of {second.facts.name} by its hash after the "
           f"refused add: {get_text}", "torrent-get-after-stale-session.json")
    outcome(run, "12", f"{text}; then {get_text}")


def progress_line(torrent: dict, elapsed: float) -> str:
    rate = torrent.get("rateDownload")
    eta = torrent.get("eta")
    eta_text = f"{eta // 60} min {eta % 60} s" if isinstance(eta, int) and eta >= 0 else "unknown"
    return (f"  {elapsed / 60:5.1f} min  {status_text(torrent)}, {common.human_size(rate)}/s, "
            f"peers {torrent.get('peersConnected')}, left {common.human_size(torrent.get('leftUntilDone'))}, eta {eta_text}")


def step_13(run: Run) -> None:
    found, unreadable = find_torrent(run, run.bbb.hash)
    if unreadable:
        return skip(run, "13", f"Transmission could not be read just now ({unreadable}); run again with --only 13,14")
    if found is None:
        return skip(run, "13", "Big Buck Bunny is not in Transmission (step 06 did not add it)")
    limit = run.args.wait * 60
    interval = run.args.poll
    say(f"  Reading its progress every {interval:g} s for up to {run.args.wait:g} min. Ctrl+C stops the waiting and "
        "goes on with the next step.")
    started = time.monotonic()
    state, failures, error_polls, done_polls = "timeout", 0, 0, 0
    try:
        while True:
            ex = run.rpc.call("torrent-get", {"ids": [run.bbb.hash], "fields": PROGRESS_FIELDS})
            if ex.reply is None or ex.reply.status != 200:
                failures += 1
                say(f"  poll failed: {verdict(ex)}")
                if failures >= 3:
                    state = "polls failed"
                    break
            elif not ex.torrents:
                state = "gone"
                break
            else:
                failures = 0
                torrent = ex.torrents[0]
                say(progress_line(torrent, time.monotonic() - started))
                percent = torrent.get("percentDone")
                if isinstance(percent, (int, float)) and percent >= 1:
                    done_polls += 1
                    # Seeding (6) or queued to seed (5) is the state wanted, stopped (0) won't change by waiting; give
                    # a verify a few polls to finish.
                    if torrent.get("status") in (0, 5, 6) or done_polls >= 4:
                        state = "complete"
                        break
                error_polls = error_polls + 1 if torrent.get("error") else 0
                if error_polls >= 3:
                    state = "error"
                    break
            if time.monotonic() - started >= limit:
                state = "timeout"
                break
            time.sleep(interval)
    except KeyboardInterrupt:
        state = "interrupted"
        say("  Waiting stopped (Ctrl+C).")
    if state == "gone":
        return skip(run, "13", "Big Buck Bunny is no longer in Transmission")
    ex = run.rpc.call("torrent-get", {"ids": [run.bbb.hash], "fields": FULL_FIELDS})
    torrent = ex.torrents[0] if ex.torrents else {}
    text = status_text(torrent) if torrent else verdict(ex)
    if state == "complete" and torrent.get("status") not in (5, 6):
        # Complete is not the same as seeding: a stopped torrent (an earlier run's step 14) must not be filed as one.
        state = "complete, stopped" if torrent.get("status") == 0 else "complete, other"
    pending, why = {
        "complete": ("torrent-get-seeding.json", "complete and seeding"),
        "complete, stopped": ("torrent-get-complete-stopped.json", "complete but stopped (status 0), not seeding"),
        "complete, other": ("torrent-get-complete.json", "complete, neither seeding nor stopped"),
        "error": ("torrent-get-error.json", "Transmission reports an error (three polls in a row)"),
        "timeout": ("torrent-get-after-wait.json", f"NOT complete after {run.args.wait:g} min"),
        "interrupted": ("torrent-get-after-wait.json", "waiting stopped by Ctrl+C before it completed"),
        "polls failed": ("torrent-get-after-wait.json", "three polls in a row got no answer"),
    }[state]
    record(run, "13-torrent-get-after-wait", ex, f"torrent-get of Big Buck Bunny, {why}: {text}", pending)
    outcome(run, "13", f"{why}: {text}")


def step_14(run: Run) -> None:
    torrent, unreadable = find_torrent(run, run.bbb.hash)
    if unreadable:
        return skip(run, "14", f"Transmission could not be read just now ({unreadable}); nothing was sent. Run again "
                               "with --only 13,14")
    if torrent is None:
        return skip(run, "14", "Big Buck Bunny is not in Transmission (step 06 did not add it)")
    percent = torrent.get("percentDone")
    complete = isinstance(percent, (int, float)) and percent >= 1
    effect = ["Stops Big Buck Bunny (it stops seeding); nothing is removed."]
    if not complete:
        effect.append(f"It is NOT complete ({status_text(torrent)}): the sample is then a stopped download, not a "
                      "stopped 100 %. Type anything but y to keep waiting for it in a later run (--only 13,14).")
    arguments = {"ids": [run.bbb.hash]}
    if not ask_write(run, "torrent-stop", arguments, effect):
        return skip(run, "14", "you did not type y; nothing was sent")
    run.rpc.permit("torrent-stop")
    ex = run.rpc.call("torrent-stop", arguments)
    record(run, "14a-torrent-stop", ex, f"torrent-stop of Big Buck Bunny by its hash: {verdict(ex)}", "torrent-stop.json")
    for _ in range(5):  # a stop is applied right after the answer; give it a moment rather than read a stale status
        time.sleep(1.0)
        now, unreadable = find_torrent(run, run.bbb.hash)
        if unreadable or now is None or now.get("status") == 0:
            break
    get = run.rpc.call("torrent-get", {"ids": [run.bbb.hash], "fields": FULL_FIELDS})
    stopped = get.torrents[0] if get.torrents else {}
    text = status_text(stopped) if stopped else verdict(get)
    pending = "torrent-get-stopped.json" if complete else "torrent-get-stopped-incomplete.json"
    record(run, "14b-torrent-get-stopped", get, f"torrent-get of Big Buck Bunny after torrent-stop: {text}", pending)
    outcome(run, "14", f"{verdict(ex)}; then {text}")


def step_15(run: Run) -> None:
    if not run.rpc.user:
        return skip(run, "15", "no user name was given, so this Transmission asks for no password")
    say("  Exactly one request. Transmission counts wrong passwords only when anti-brute-force-enabled is on in its "
        "settings.json (off by default; the linuxserver image leaves it off); then it locks the RPC after "
        "anti-brute-force-threshold of them (100 by default), and a good request resets the count.")
    ex = run.rpc.call("session-get", {"fields": ["version"]}, auth=run.rpc.wrong_auth, handshake=False)
    status = ex.reply.status if ex.reply is not None else None
    text = "401 Unauthorized" if status == 401 else verdict(ex)
    if status == 200:
        # 2026-10-13: Transmission ignores Authorization when rpc-authentication-required is off, i.e. when the
        # container's USER or PASS is empty, and its RPC is then open to the whole network.
        say("  !!! Transmission accepted a wrong password: it asks for none. Set USER and PASS of this container in "
            "Stashboard, Save & apply, then run again with --only 15.")
        text = "ACCEPTED A WRONG PASSWORD (no password is set): " + text
    record(run, "15-wrong-password", ex, f"session-get with the right user name and a wrong password, once: {text}",
           "wrong-password-response.txt")
    outcome(run, "15", text)


def step_16(run: Run) -> None:
    # libtransmission/rpc-server.cc isHostnameAllowed: any Host passes when a password is set or the Host whitelist is
    # off, and the linuxserver image keeps it off unless HOST_WHITELIST is set. Either answer is recorded, under its
    # own name, so a 200 is never filed as the 421 sample.
    if run.rpc.user:
        # MAM-29, Vasyl's decision of 2026-10-13: no container is opened without a password to record a 421; MAM-30
        # takes its shape from rpc-server.cc instead.
        note = ("with a password set, Transmission lets any Host name through (isHostnameAllowed), so 200 is expected "
                "here, not 421. A 421 comes only from a Transmission with no password and its Host whitelist on; it is "
                "not recorded live")
    else:
        note = ("without a password, Transmission checks the Host name only when its Host whitelist is on, which the "
                "linuxserver image does only when HOST_WHITELIST is set: 421 is expected then, 200 otherwise")
    say(f"  note: {note}")
    ex = run.rpc.call("session-get", {"fields": ["version"]}, host=HOST_NOT_ALLOWED)
    status = ex.reply.status if ex.reply is not None else None
    if status == 421:
        text, pending = f"refused the unknown Host: {verdict(ex)}", "wrong-host-response.txt"
    elif status == 200:
        text, pending = f"let the unknown Host through: {verdict(ex)}", "wrong-host-allowed-response.txt"
    else:
        text, pending = verdict(ex), None
    record(run, "16-wrong-host", ex, f"session-get with Host: {HOST_NOT_ALLOWED}: {text}", pending, note)
    outcome(run, "16", text)


STEPS = {"01": step_01, "02": step_02, "03": step_03, "04": step_04, "05": step_05, "06": step_06, "07": step_07,
         "08": step_08, "09": step_09, "10": step_10, "11": step_11, "12": step_12, "13": step_13, "14": step_14,
         "15": step_15, "16": step_16}


# ---------------------------------------------------------------- the end

def web_ui_address(run: Run) -> str:
    path = posixpath.dirname(run.rpc.parts.path.rstrip("/")) or ""
    return urllib.parse.urlunsplit((run.rpc.parts.scheme, run.rpc.parts.netloc, f"{path}/web/", "", ""))


def print_cleanup(run: Run) -> None:
    """Which torrents the owner deletes by hand: every torrent with the samples' label, plus any this run added without
    it. Read once more, because a torrent may be left from an earlier run."""
    rows: list[dict] = []
    read = False
    if run.session:
        ex = run.rpc.call("torrent-get", {"fields": ["id", "name", "hashString", "labels", "downloadDir", "percentDone"]})
        if ex.reply is not None and ex.reply.status == 200 and "torrents" in ex.arguments:
            read = True
            rows = [t for t in ex.torrents if LABEL in (t.get("labels") or []) or t.get("hashString") in run.cleanup]
    if not read:
        rows = [{"name": name, "hashString": info_hash, "downloadDir": where} for info_hash, (name, where)
                in run.cleanup.items()]
    if not rows:
        if run.session:
            say("\nNo sample torrent is in Transmission: nothing to delete.")
        return
    heading("DELETE THESE TORRENTS, WITH THEIR DATA, IN TRANSMISSION'S WEB UI")
    # The menu label as web/src/action-manager.js spells it at 4.0.6 and 4.1.3.
    say(f"Open {web_ui_address(run)}, right-click each torrent below and choose \"Trash data and remove from list…\" "
        "(removal together with its data). This script never removes a torrent.")
    for row in rows:
        percent = row.get("percentDone")
        shown = f", {percent * 100:.0f} %" if isinstance(percent, (int, float)) else ""
        say(f"  - {row.get('name')!r}  info hash {row.get('hashString')}  folder {row.get('downloadDir')}{shown}")
    if not read:
        say("(Transmission could not be read just now, so this is the list of what this run added.)")


def print_outcomes(run: Run) -> None:
    if not run.outcomes:
        return
    heading("RESULT")
    for step, text in run.outcomes:
        say(f"{step}  {text}")


# ---------------------------------------------------------------- the run

def parse_steps(text: str | None) -> list[str] | None:
    if not text:
        return None
    wanted = []
    for part in text.split(","):
        part = part.strip()
        if not part:
            continue
        step = part.zfill(2)
        if step not in STEP_TITLES:
            raise argparse.ArgumentTypeError(f"{part!r} is not a step (01 to 16)")
        wanted.append(step)
    return wanted


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Records Transmission's RPC answers (legacy dialect) as MAM-29 vendor sample candidates. Reads need "
                    "no confirmation; each torrent-add and torrent-stop is sent only after you type y. It never removes "
                    "a torrent and never changes Transmission's settings.",
        epilog=f"The password is asked with hidden input. The environment variable {common.ENV_TRANSMISSION_PASSWORD} can "
               "stand in for it, for unattended tests only. Steps: " + "; ".join(f"{k} {v}" for k, v in STEP_TITLES.items()))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--full", action="store_true", help="all 16 steps, for Transmission 4.1.3 (the default)")
    mode.add_argument("--short", action="store_true",
                      help="steps 01, 02, 04, 06, 08, 07 only, for the temporary Transmission 4.0.x container")
    parser.add_argument("--torrent", metavar="PATH",
                        help=f"a local .torrent to use instead of downloading {DEFAULT_TORRENT_URL}")
    parser.add_argument("--second-torrent", metavar="PATH",
                        help=f"a local .torrent for steps 10 and 12 instead of downloading {DEFAULT_SECOND_TORRENT_URL}")
    parser.add_argument("--wait", type=float, default=30.0, metavar="MINUTES",
                        help="how long step 13 waits for Big Buck Bunny to complete; default %(default)g")
    parser.add_argument("--only", type=parse_steps, metavar="STEPS",
                        help="run only these steps of the full set, e.g. 13,14 (01 and 02 always run)")
    parser.add_argument("--out", default=None, help=f"output root; default: {common.OUT_ROOT}")
    # Hidden: they exist so the tests can make the waiting happen in seconds, not minutes.
    parser.add_argument("--poll", type=float, default=10.0, help=argparse.SUPPRESS)
    parser.add_argument("--timeout", type=float, default=30.0, help=argparse.SUPPRESS)
    return parser.parse_args(argv)


def chosen_steps(args: argparse.Namespace) -> list[str]:
    if args.only:
        if args.short:
            raise Stop("--only picks steps of the full set; leave out --short.")
        return ["01", "02"] + [step for step in FULL_STEPS if step in args.only and step not in ("01", "02")]
    return SHORT_STEPS if args.short else FULL_STEPS


def password_overlap(password: str, url: str, user: str, scratch: str, library: str) -> list[str]:
    """What the samples hold that contains the password: every occurrence becomes REDACTED (common.SECRETS scrubs it
    wherever it stands), so a plain-word password ("transmission") turns the address into /REDACTED/rpc. Named by what
    it is, never quoted: say() would print the word with the password scrubbed out of it."""
    if len(password) < common.MIN_LITERAL_SECRET:
        return []  # too short to be scrubbed literally (common.MIN_LITERAL_SECRET)
    vocabulary = ["Transmission", "transmission", "success", "torrent-added", "torrent-duplicate", "Conflict",
                  "X-Transmission-Session-Id", *READ_METHODS, *WRITE_METHODS, *SESSION_FIELDS, *FULL_FIELDS,
                  *PROGRESS_FIELDS]
    places = [("the RPC address", [url]), ("the user name", [user]), ("the scratch folder", [scratch]),
              ("the library folder", [library]), ("the label", [LABEL]),
              ("the service's own words in its answers (its name, method or field names)", vocabulary)]
    return [place for place, texts in places if any(password in text for text in texts)]


def needs_second(steps: list[str]) -> bool:
    return any(step in steps for step in ("10", "12"))


def run_all(args: argparse.Namespace, rec: common.Recorder) -> int:
    steps = chosen_steps(args)
    say(f"MAM-29 Transmission samples ({'--short' if args.short else '--full'}): steps {', '.join(steps)}")
    say("Reads Transmission's RPC; each torrent-add and torrent-stop is sent only after you type y for it. It never "
        "removes a torrent and never changes Transmission's settings.")
    say(f"Output: {rec.out_root}\\transmission-<version>\\ (named after the version Transmission reports)")
    # 2026-10-13: a --short run that took the 9091 default went to 4.1.3 and wrote over its full set's raw files.
    default_url = common.DEFAULT_TRANSMISSION_40_URL if args.short else common.DEFAULT_TRANSMISSION_URL
    url = common.ask("Transmission RPC address", default_url)
    common.check_address(url, "The RPC address")
    user = common.ask("User name (Enter if the RPC has no password)")
    password = ""
    if user:
        password = common.ask_secret("Password (typing is hidden)", common.ENV_TRANSMISSION_PASSWORD)
        if not password:
            raise Stop("No password was typed; run again and type the PASS of the transmission container.")
    scratch = posixpath.normpath(common.ask("The scratch folder as Transmission sees it, outside the media folders",
                                            DEFAULT_SCRATCH))
    library = posixpath.normpath(common.ask("A library folder as Transmission sees it (read only: free-space)",
                                            DEFAULT_LIBRARY))
    for label, path in (("scratch folder", scratch), ("library folder", library)):
        if not path.startswith("/"):
            raise Stop(f"The {label} {path!r} is not an absolute path as the container sees it, e.g. /scratch.")
    spoiled = password_overlap(password, url, user, scratch, library)
    if spoiled:
        say(f"note: the password is also part of {', '.join(spoiled)}. Every occurrence is written as REDACTED, so the "
            "samples will show REDACTED there too; nothing leaks. If that spoils a sample, set a PASS that is not a "
            "plain word on the container in Stashboard, restart it and run again.")
    heading("The .torrent files (public, legal: Blender's open films from webtorrent.io)")
    rpc = Rpc(url, user, password, timeout=args.timeout)
    run = Run(args, rpc, rec, scratch, library)
    run.bbb = load_torrent("Big Buck Bunny", args.torrent, DEFAULT_TORRENT_URL, "--torrent")
    if needs_second(steps):
        run.second = load_torrent("The second torrent", args.second_torrent, DEFAULT_SECOND_TORRENT_URL,
                                  "--second-torrent")
        if run.second.hash == run.bbb.hash:
            raise Stop("The two .torrent files are the same torrent; the second must be another one, e.g. Sintel.")
    try:
        for step in steps:
            heading(f"{step}  {STEP_TITLES[step]}")
            STEPS[step](run)
    finally:
        try:
            print_cleanup(run)
        except ToolError as error:
            say(f"(The list of torrents to delete could not be made: {error})")
        print_outcomes(run)
    return 0


def main(argv: list[str] | None = None) -> int:
    common.setup_console()
    args = parse_args(argv)
    common.reset_run()
    out_root = Path(args.out).resolve() if args.out else common.OUT_ROOT
    if common.inside_repo_outside_claude(out_root):
        print(f"--out {out_root} is inside the repository but not under .claude\\ (gitignored); choose another folder.",
              flush=True)
        return 2
    rec = common.Recorder(out_root, "transmission")
    status = 1
    try:
        status = run_all(args, rec)
    except KeyboardInterrupt:
        say("\nInterrupted (Ctrl+C). A request already sent was not repeated.")
        status = 130
    except ToolError as error:
        say(f"\nSTOPPED: {error}")
    except Exception:  # noqa: BLE001 - a bug in this script: say where, with every secret scrubbed
        say("\nUNEXPECTED ERROR (a bug in this script; nothing was repeated):")
        say(common.redact_text(traceback.format_exc()))
    finally:
        if rec.folder is not None or rec.entries:
            if rec.finish():
                status = status or 3
    return status


if __name__ == "__main__":
    sys.exit(main())
