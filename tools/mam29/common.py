r"""Shared helpers of the MAM-29 owner-run sample scripts (transmission_samples.py, jackett_samples.py).

Not run on its own: the scripts sit in this folder and `import common`.

Reads:  nothing by itself. A script hands it the addresses and the secrets it asked for with hidden input (the only
        environment fallbacks, MAM29_TRANSMISSION_PASSWORD and MAM29_JACKETT_API_KEY, exist for unattended tests).
Writes: only what a script tells it to, under D:\Sources\Media-Acquisition-Manager\.claude\mam29-check\ (gitignored),
        and every text passes redact_text() and then leaks() before it is written: a secret never reaches the disk,
        and a text that still fails the check is replaced by a note saying so. It prints no secret either.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py
        python D:\Sources\Media-Acquisition-Manager\tools\mam29\jackett_samples.py

Python 3.10+, standard library only (the owner's PC has no pip packages).
"""

from __future__ import annotations

import getpass
import hashlib
import http
import http.client
import json
import os
import re
import socket
import ssl
import sys
import time
import urllib.parse
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------- constants

REPO_ROOT = Path(__file__).resolve().parents[2]
# Everything a script writes goes here; .claude/ is gitignored, so a recorded answer can't be committed by accident.
OUT_ROOT = REPO_ROOT / ".claude" / "mam29-check"
VENDOR_SAMPLES = REPO_ROOT / "tests" / "MediaAcquisitionManager.TestInfrastructure" / "VendorSamples"

DEFAULT_TRANSMISSION_URL = "http://192.168.1.195:9091/transmission/rpc"
# The temporary 4.0.x container of docker-compose.yml, the default of --short.
DEFAULT_TRANSMISSION_40_URL = "http://192.168.1.195:9092/transmission/rpc"
DEFAULT_JACKETT_URL = "http://192.168.1.195:9117"

# The only environment variables that may stand in for a typed secret. Each script names its own in --help.
ENV_TRANSMISSION_PASSWORD = "MAM29_TRANSMISSION_PASSWORD"
ENV_JACKETT_API_KEY = "MAM29_JACKETT_API_KEY"

REDACTED = "REDACTED"  # the only placeholder VendorSamples/README.md prescribes
RAW_DIR = "raw"
PENDING_DIR = "samples-pending"
INDEX_NAME = "INDEX.txt"
CONSOLE_NAME = "console.txt"
UNKNOWN_VERSION = "version unknown"  # the Recorder's label until a script learns the service's version

# A .torrent, a Torznab page or an RPC answer is far below this; a bigger answer is not what was asked for.
MAX_BODY_BYTES = 32 * 1024 * 1024
# Shorter strings are not scrubbed literally: a two-letter password would turn every "ab" of a sample into REDACTED.
# They still never reach a file, because the header that carries them is redacted by its shape.
MIN_LITERAL_SECRET = 4


class ToolError(Exception):
    """A failure the scripts report as one plain sentence instead of a traceback."""


class HttpFailure(ToolError):
    """No HTTP answer at all: refused, timed out, unresolvable. Never an HTTP status: a status is data."""


class Stop(ToolError):
    """The run can't go on (a refused password, an address that is not the service); said in one sentence."""


class BencodeError(ToolError):
    """The bytes are not a .torrent."""


def ensure_dir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    return path


def inside_repo_outside_claude(path: Path) -> bool:
    """Only .claude/ (gitignored) may hold what the scripts write: a candidate written anywhere else in the repository,
    say into VendorSamples, would be one `git add` away from a commit."""
    root = REPO_ROOT.resolve()
    path = path.resolve()
    if path != root and root not in path.parents:
        return False
    claude = root / ".claude"
    return path != claude and claude not in path.parents


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def safe_name(text: str) -> str:
    """A folder name made of what a version string holds; anything else becomes '-'."""
    cleaned = re.sub(r"[^A-Za-z0-9._-]+", "-", text.strip()).strip("-.")
    return cleaned or "unknown"


def human_size(count: int | float | None) -> str:
    if count is None:
        return "?"
    value = float(count)
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(value) < 1000 or unit == "TB":
            return f"{value:.0f} {unit}" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1000
    return f"{value:.1f} TB"


# ---------------------------------------------------------------- secrets

class Secrets:
    """Every secret of the run: what was typed, what was derived from it (a Basic credential) and what a service handed
    out (Transmission's session ids). Each is scrubbed from all that is printed or written, in every spelling."""

    def __init__(self) -> None:
        self._values: list[str] = []

    def add(self, value: str | None) -> None:
        if value and value not in self._values:
            self._values.append(value)

    def clear(self) -> None:
        self._values.clear()

    def values(self) -> tuple[str, ...]:
        return tuple(self._values)

    def spellings(self) -> list[str]:
        found: set[str] = set()
        for secret in self._values:
            if len(secret) < MIN_LITERAL_SECRET:
                continue
            # A secret that travelled in a URL or inside a JSON string is spelled differently there.
            for spelling in (secret, urllib.parse.quote(secret, safe=""), urllib.parse.quote_plus(secret),
                             json.dumps(secret, ensure_ascii=False)[1:-1]):
                if len(spelling) >= MIN_LITERAL_SECRET:
                    found.add(spelling)
        # Longest first, so a secret that contains another one is replaced whole.
        return sorted(found, key=len, reverse=True)

    def scrub(self, text: str) -> str:
        for spelling in self.spellings():
            text = text.replace(spelling, REDACTED)
        return text


SECRETS = Secrets()


# ---------------------------------------------------------------- console and prompts

_LINES: list[str] = []


def setup_console() -> None:
    """Titles are Ukrainian. A Windows console or a redirected file may use a legacy code page that can't encode
    them, and one unencodable character would end the script with UnicodeEncodeError halfway through a run."""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="replace")


def reset_run() -> None:
    """A fresh run: tests call main() more than once in one process."""
    _LINES.clear()
    SECRETS.clear()


def console_lines() -> list[str]:
    return list(_LINES)


def say(text: str = "") -> None:
    text = SECRETS.scrub(str(text))
    print(text, flush=True)
    _LINES.append(text)


def heading(text: str) -> None:
    say()
    say("=" * 78)
    say(text)
    say("=" * 78)


def box(lines: list[str]) -> None:
    say("+" + "-" * 76)
    for line in lines:
        say(("| " + line).rstrip())
    say("+" + "-" * 76)


def _end_piped_line(answer: str) -> None:
    """A typed answer ends its line on the console; a piped one is not echoed, so whatever is printed next would join
    the prompt's line. Never used for a secret (ask_secret reads with getpass)."""
    try:
        piped = not sys.stdin.isatty()
    except (AttributeError, ValueError):  # no stdin, or a closed one
        piped = True
    if piped:
        print(SECRETS.scrub(answer), flush=True)


def ask(prompt: str, default: str | None = None) -> str:
    suffix = f" [{default}]" if default else ""
    try:
        answer = input(f"{prompt}{suffix}: ").strip()
    except EOFError:  # no console to ask on: act as if Enter was pressed
        answer = ""
    _end_piped_line(answer)
    answer = answer or (default or "")
    _LINES.append(SECRETS.scrub(f"{prompt}{suffix}: {answer}"))
    return answer


def _clean_secret(value: str) -> str:
    # A pasted secret often brings a trailing newline or the quotes it was shown in; either would make the service
    # answer 401 and look like a wrong secret.
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        value = value[1:-1].strip()
    return value


def ask_secret(prompt: str, env_name: str) -> str:
    """Hidden input. `env_name` is a fallback for unattended tests only: the owner types the secret. Whatever comes back
    is registered in SECRETS before anything else can print it."""
    from_env = os.environ.get(env_name, "").strip()
    if from_env:
        value = _clean_secret(from_env)
        SECRETS.add(value)
        say(f"{prompt}: taken from the environment variable {env_name}")
        return value
    try:
        value = _clean_secret(getpass.getpass(f"{prompt}: "))
    except EOFError:
        value = ""
    SECRETS.add(value)
    _LINES.append(f"{prompt}: ({'typed, hidden' if value else 'nothing typed'})")
    return value


def confirm(prompt: str) -> bool:
    """True only for a typed y/yes. Enter, anything else and a closed input are all "no": a write must be asked for."""
    try:
        answer = input(f"{prompt} [y/N]: ").strip().lower()
    except EOFError:
        answer = ""
    _end_piped_line(answer)
    _LINES.append(f"{prompt} [y/N]: {answer}")
    return answer in ("y", "yes")


# ---------------------------------------------------------------- HTTP

def _reason_phrase(status: int) -> str:
    try:
        return http.HTTPStatus(status).phrase
    except ValueError:
        return ""


@dataclass
class Reply:
    """One HTTP answer as it came: an error status is data, not an exception, because the scripts exist to record
    409s, 401s and 404s. Headers keep their order and their repeats."""

    status: int
    reason: str
    headers: list[tuple[str, str]]
    body: bytes
    seconds: float
    http_version: str = "HTTP/1.1"
    truncated: bool = False

    @property
    def ok(self) -> bool:
        return 200 <= self.status < 300

    @property
    def text(self) -> str:
        return self.body.decode("utf-8", errors="replace")

    @property
    def json(self):
        """The parsed body, or None when it is empty or not JSON."""
        body = self.text.lstrip("\ufeff").strip()
        if not body:
            return None
        try:
            return json.loads(body)
        except ValueError:
            return None

    def header(self, name: str, default: str | None = None) -> str | None:
        wanted = name.lower()
        for key, value in self.headers:
            if key.lower() == wanted:
                return value
        return default

    def head(self) -> str:
        """The status line and the headers, the way the *-response.txt samples start."""
        lines = [f"{self.http_version} {self.status} {self.reason}".rstrip()]
        lines += [f"{name}: {value}" for name, value in self.headers]
        return "\n".join(lines) + "\n"

    def as_sample(self, include_body: bool = True) -> str:
        """The status line, headers, a blank line and the body, like VendorSamples/*/*-response.txt. The body is followed by
        a newline only when there is one, so a bodiless answer ends at the blank line."""
        body = self.text.replace("\r\n", "\n").rstrip("\n") if include_body else ""
        return self.head() + "\n" + (body + "\n" if body else "")


def check_address(url: str, what: str) -> urllib.parse.SplitResult:
    if "://" not in url:
        url = "http://" + url
    parts = urllib.parse.urlsplit(url)
    if parts.scheme not in ("http", "https") or not parts.hostname:
        raise Stop(f"{what} {url!r} is not an http(s) address.")
    if parts.username is not None or parts.password is not None:
        # The secret would ride in the URL, which is printed and recorded; it has its own hidden prompt.
        raise Stop(f"{what} must not hold a user name or password: the script asks for them separately.")
    try:
        parts.port
    except ValueError:
        raise Stop(f"{what} {url!r} has a port that is not a number.") from None
    return parts


def http_request(method: str, url: str, *, headers: dict[str, str] | None = None, body: bytes | None = None,
                 timeout: float = 30.0, host_header: str | None = None) -> Reply:
    """One request with http.client, not urllib: it ignores system proxy settings (a proxy would hijack LAN calls),
    follows no redirect behind the owner's back and returns an error status like any other answer. Nothing is retried.
    A transport failure raises HttpFailure with one sentence that names the host and port only: the URL's query can
    hold an API key."""
    parts = urllib.parse.urlsplit(url)
    host, port = parts.hostname or "", parts.port
    where = f"{host}:{port}" if port else host
    target = (parts.path or "/") + (f"?{parts.query}" if parts.query else "")
    sent = {"Connection": "close", "User-Agent": "MAM-29-samples"}
    sent.update(headers or {})
    if host_header is not None:
        # http.client adds no Host of its own when one is passed (it skips its own on a 'host' key).
        sent["Host"] = host_header
    if parts.scheme == "https":
        connection = http.client.HTTPSConnection(host, port, timeout=timeout, context=ssl.create_default_context())
    else:
        connection = http.client.HTTPConnection(host, port, timeout=timeout)
    started = time.perf_counter()
    failure: str | None = None
    try:
        connection.request(method, target, body=body, headers=sent)
        response = connection.getresponse()
        raw = response.read(MAX_BODY_BYTES + 1)
        seconds = time.perf_counter() - started
        return Reply(response.status, response.reason or _reason_phrase(response.status), list(response.getheaders()),
                     raw[:MAX_BODY_BYTES], seconds, "HTTP/1.0" if response.version == 10 else "HTTP/1.1",
                     len(raw) > MAX_BODY_BYTES)
    except ConnectionRefusedError:
        failure = f"nothing answers at {where} (connection refused); check that the container runs and the port is right"
    except (socket.timeout, TimeoutError):
        failure = f"{where} did not answer within {timeout:g} s"
    except socket.gaierror:
        failure = f"the host name {host!r} could not be resolved"
    except ssl.SSLError as error:
        failure = f"the TLS handshake with {where} failed ({error.reason or type(error).__name__})"
    except ConnectionResetError:
        failure = f"{where} closed the connection without answering"
    except OSError as error:
        failure = f"could not reach {where}: {error.strerror or type(error).__name__}"
    except http.client.HTTPException as error:
        failure = f"{where} answered something that is not HTTP ({type(error).__name__})"
    except ValueError as error:
        # http.client refuses a header value with a line break; the message would quote the value, so it is not kept.
        failure = f"the request to {where} could not be built ({type(error).__name__})"
    finally:
        connection.close()
    # Raised outside the except blocks and without a cause, so no chained traceback can show a header value.
    raise HttpFailure(SECRETS.scrub(failure)) from None


def describe_request(method: str, url: str, headers: dict[str, str], body_text: str | None = None,
                     host_header: str | None = None) -> str:
    """The request as it went, for raw/: the credential headers keep only their scheme, and the URL and body are
    redacted later with everything else."""
    parts = urllib.parse.urlsplit(url)
    target = (parts.path or "/") + (f"?{parts.query}" if parts.query else "")
    lines = [f"{method} {target} HTTP/1.1", f"Host: {host_header if host_header is not None else parts.netloc}"]
    for name, value in headers.items():
        lowered = name.lower()
        if lowered == "authorization":
            scheme = value.split(" ", 1)[0] if " " in value else ""
            value = f"{scheme} {REDACTED}".strip()
        elif lowered in ("x-transmission-session-id", "cookie", "x-api-key"):
            value = REDACTED
        lines.append(f"{name}: {value}")
    text = "\n".join(lines) + "\n"
    if body_text:
        text += "\n" + body_text.rstrip("\n") + "\n"
    return text


# ---------------------------------------------------------------- .torrent files

@dataclass
class TorrentFacts:
    size: int                       # bytes of the .torrent itself
    v1_hash: str | None             # SHA-1 of the raw bencoded info dictionary
    name: str
    file_count: int
    total_length: int
    announce_hosts: list[str] = field(default_factory=list)   # host names only: a path can hold a passkey
    private: bool = False
    v2_only: bool = False
    hybrid: bool = False


_MAX_DEPTH = 64


def _bdecode(data: bytes, index: int, depth: int):
    if depth > _MAX_DEPTH:
        raise BencodeError("it nests deeper than any .torrent does")
    if index >= len(data):
        raise BencodeError("it ends in the middle of a value")
    lead = data[index]
    if lead == 0x69:  # i<digits>e
        end = data.find(b"e", index + 1)
        if end < 0 or not re.fullmatch(rb"-?\d+", data[index + 1:end]):
            raise BencodeError(f"the integer at byte {index} is malformed")
        return int(data[index + 1:end]), end + 1
    if 0x30 <= lead <= 0x39:  # <length>:<bytes>
        colon = data.find(b":", index)
        if colon < 0 or colon - index > 12 or not data[index:colon].isdigit():
            raise BencodeError(f"the string at byte {index} has a malformed length")
        start = colon + 1
        end = start + int(data[index:colon])
        if end > len(data):
            raise BencodeError(f"the string at byte {index} runs past the end")
        return data[start:end], end
    if lead == 0x6C:  # l...e
        index += 1
        items = []
        while True:
            if index >= len(data):
                raise BencodeError("a list never ends")
            if data[index] == 0x65:
                return items, index + 1
            item, index = _bdecode(data, index, depth + 1)
            items.append(item)
    if lead == 0x64:  # d...e
        index += 1
        result: dict = {}
        while True:
            if index >= len(data):
                raise BencodeError("a dictionary never ends")
            if data[index] == 0x65:
                return result, index + 1
            key, index = _bdecode(data, index, depth + 1)
            if not isinstance(key, bytes):
                raise BencodeError("a dictionary key is not a string")
            result[key], index = _bdecode(data, index, depth + 1)
    raise BencodeError(f"byte {index} is not the start of a bencoded value")


def _text(value) -> str:
    return value.decode("utf-8", errors="replace") if isinstance(value, bytes) else str(value or "")


def _v2_file_count(tree, depth: int = 0) -> tuple[int, int]:
    """Files and bytes of a BitTorrent v2 'file tree': a leaf is a dict under the empty key."""
    if not isinstance(tree, dict) or depth > _MAX_DEPTH:
        return 0, 0
    count = length = 0
    for key, value in tree.items():
        if key == b"" and isinstance(value, dict):
            count += 1
            length += value.get(b"length", 0) if isinstance(value.get(b"length"), int) else 0
        else:
            sub_count, sub_length = _v2_file_count(value, depth + 1)
            count += sub_count
            length += sub_length
    return count, length


def read_torrent(data: bytes) -> TorrentFacts:
    """What a .torrent holds, read in memory. The v1 info hash is the SHA-1 of the info dictionary's raw bytes as they
    stand in the file (never re-encoded: a re-encoding could reorder keys and change the hash). Only the host names of
    the trackers are kept, because the rest of an announce URL can carry the owner's passkey."""
    if not data:
        raise BencodeError("it is empty")
    if data[:1] != b"d":
        raise BencodeError("it does not start as a bencoded dictionary")
    index = 1
    top: dict = {}
    info_span: tuple[int, int] | None = None
    while True:
        if index >= len(data):
            raise BencodeError("its top dictionary never ends")
        if data[index] == 0x65:
            break
        key, index = _bdecode(data, index, 1)
        if not isinstance(key, bytes):
            raise BencodeError("a dictionary key is not a string")
        start = index
        top[key], index = _bdecode(data, index, 1)
        if key == b"info":
            info_span = (start, index)
    info = top.get(b"info")
    if info_span is None or not isinstance(info, dict):
        raise BencodeError("it has no 'info' dictionary")
    has_pieces = b"pieces" in info
    meta_version = info.get(b"meta version")
    v2 = meta_version == 2 and b"file tree" in info
    if b"files" in info and isinstance(info[b"files"], list):
        files = [f for f in info[b"files"] if isinstance(f, dict)]
        count = len(files)
        total = sum(f.get(b"length", 0) for f in files if isinstance(f.get(b"length"), int))
    elif isinstance(info.get(b"length"), int):
        count, total = 1, info[b"length"]
    elif v2:
        count, total = _v2_file_count(info[b"file tree"])
    else:
        count, total = 0, 0
    urls = []
    if isinstance(top.get(b"announce"), bytes):
        urls.append(top[b"announce"])
    for tier in top.get(b"announce-list") or []:
        if isinstance(tier, list):
            urls += [url for url in tier if isinstance(url, bytes)]
    hosts: list[str] = []
    for url in urls:
        try:
            host = urllib.parse.urlsplit(url.decode("utf-8", errors="replace")).hostname
        except ValueError:
            host = None
        if host and host not in hosts:
            hosts.append(host)
    name = _text(info.get(b"name.utf-8") or info.get(b"name"))
    return TorrentFacts(size=len(data), v1_hash=hashlib.sha1(data[info_span[0]:info_span[1]]).hexdigest(), name=name,
                        file_count=count, total_length=total, announce_hosts=hosts, private=info.get(b"private") == 1,
                        v2_only=v2 and not has_pieces, hybrid=v2 and has_pieces)


# ---------------------------------------------------------------- redaction (VendorSamples/README.md)

# The names VendorSamplesTests treats as secrets in a URL parameter (copied from it: a sample that passes redaction
# here must pass that test). Jackett takes its key as apikey or passkey, and puts it into its own download links as
# jackett_apikey: all three are in the list.
KEY_LIKE_NAMES = ("apikey|api_key|api_token|apitoken|jackett_apikey|passkey|pk|uk|tp|token|access_token|link|"
                  "authkey|torrent_pass|rsskey|key|secret|password|passwd|pwd|pass|auth|sig|signature|x-plex-token")

# An apostrophe is allowed inside: Ukrainian names hold one ("Пам'ять"), and the tracker list may follow it.
_MAGNET = re.compile(r"magnet:\?[^\"<>\s]+", re.IGNORECASE)
_TRACKER_PARAMETER = re.compile(r"^(?:tr|ws|xs|as)(?:\.\d+)?=", re.IGNORECASE)


def _strip_magnet(match: re.Match) -> str:
    # The README keeps only xt, dn and xl of a magnet: a tracker parameter can carry the passkey.
    uri = match.group(0)
    head, _, query = uri.partition("?")
    separator = "&amp;" if "&amp;" in query else "&"
    kept = [part for part in query.split(separator) if part and not _TRACKER_PARAMETER.match(part)]
    return head + "?" + separator.join(kept)


_REDACTIONS = [
    # a tracker URL keeps its scheme and host; its path, which may hold the passkey, becomes /REDACTED
    (re.compile(r"\b([a-z][a-z0-9+.-]*://[^/\s\"'<>]+)/[^\s\"'<>?#&]*\b(?:announce|ann)\b[^\s\"'<>]*", re.IGNORECASE),
     r"\1/REDACTED"),
    # a JSON tracker field keeps its scheme and host whatever its path looks like: the passkey may be the path itself
    (re.compile(r"(\"tracker\"\s*:\s*\"[a-z]+://[^/\"]+/)(?!REDACTED\")[^\"]*", re.IGNORECASE), r"\1REDACTED"),
    # a key-like URL parameter, e.g. ?apikey=... or Jackett's &jackett_apikey=...
    (re.compile(r"([?&;](?:%s)=)(?!REDACTED)[^&\"'<>\s]+" % KEY_LIKE_NAMES, re.IGNORECASE), r"\1REDACTED"),
    # Jackett's download token: with the key it opens the download, so it goes the way of the key
    (re.compile(r"([?&;]path=)(?!REDACTED)[^&\"'<>\s]+", re.IGNORECASE), r"\1REDACTED"),
    # a session id in a URL or cookie, e.g. Toloka's sid=...
    (re.compile(r"(\b(?:QBT_)?SID(?:_\d+)?=)(?!REDACTED)[^&\"'<>\s;]+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\bMediaBrowser\b[^\r\n]*?\bToken=\")(?!REDACTED\")[^\"]*", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\b(?:X-Api-Key|X-Emby-Token)\s*:\s*)(?!REDACTED)\S+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\bAuthorization[\"']?\s*:\s*[\"']?Bearer\s+)(?!REDACTED\b)[^\s\"']+", re.IGNORECASE), r"\1REDACTED"),
    # Transmission: HTTP Basic credentials, and the CSRF session id in its header and in the 409 page that quotes it
    (re.compile(r"(\bAuthorization[\"']?\s*:\s*[\"']?Basic\s+)(?!REDACTED\b)[^\s\"']+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\bX-Transmission-Session-Id\s*:\s*)(?!REDACTED)[^\s<\"']+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\b(?:Set-)?Cookie\s*:\s*)(?!REDACTED)[^\r\n]+", re.IGNORECASE), r"\1REDACTED"),
    # user info in a URL keeps only REDACTED before the @
    (re.compile(r"(\b[a-z][a-z0-9+.-]*://)(?!REDACTED@)[^/\s\"<>@]*@", re.IGNORECASE), r"\1REDACTED@"),
    # a string value under a credential-like JSON key
    (re.compile(r"(\"[A-Za-z_-]*(?:api_?key|access_?token|password|passwd|passkey|secret|cookie|X-Emby-Token|session-id)\"\s*:\s*\")"
                r"(?!REDACTED\")(?:[^\"\\]|\\.)+(?=\")", re.IGNORECASE), r"\1REDACTED"),
]


def redact_text(text: str) -> str:
    """Every registered secret becomes REDACTED in each spelling it can take; then credentials are redacted by their
    shape (VendorSamples/README.md, "Sanitizing"): key-like URL parameters, Jackett's download token, session ids,
    credential headers, user info, credential JSON fields; magnet trackers are removed and tracker URLs cut to the
    host. Names of files, folders and releases are kept, as the README says."""
    text = SECRETS.scrub(text)
    text = _MAGNET.sub(_strip_magnet, text)
    for pattern, replacement in _REDACTIONS:
        text = pattern.sub(replacement, text)
    return text


# The rules of VendorSamplesTests.cs, so a candidate is checked here before anyone copies it into the repository,
# where the build would fail on a leak. The last five came with MAM-29; tools/mam34/common.py has the same list.
_LEAK_RULES = [
    ("key-like URL parameter", r"[?&;](?:%s)(?:=|%%3D)(?!REDACTED)[^&\"'<>\s]" % KEY_LIKE_NAMES),
    ("key-like parameter inside an encoded URL", r"(?:%%3F|%%26)(?:%s)%%3D(?!REDACTED)" % KEY_LIKE_NAMES),
    ("session id", r"\b(?:QBT_)?SID(?:_\d+)?=(?!REDACTED)[^&\"'<>\s;]"),
    ("magnet tracker list", r"magnet:\?(?:[^\"<>]*&)?(?:tr|ws|xs|as)(?:\.\d+)?="),
    ("tracker announce URL", r"://[^/\s\"'<>]+/[^\s\"'<>?#&]*\b(?:announce|ann)\b"),
    ("tracker path", r"\"tracker\"\s*:\s*\"[a-z]+://[^/\"]+/(?!REDACTED\")[^\"]"),
    ("user info in a URL", r"://(?!REDACTED@[^/\s\"<>@]*(?:[/\s\"<>]|$))[^/\s\"<>]*@"),
    ("URL-encoded URL", r"(?:https?|udp|wss?)%3A%2F%2F"),
    ("credential field", r"\"[A-Za-z_-]*(?:api_?key|access_?token|password|passwd|passkey|secret|cookie|X-Emby-Token|session-id)\""
                         r"\s*:\s*\"(?!REDACTED\"|\")"),
    ("credential header", r"\b(?:X-Api-Key|X-Emby-Token)\s*:\s*(?!REDACTED)\S|\bMediaBrowser\b[^\r\n]*\bToken=\"(?!REDACTED\")"),
    ("bearer token", r"\bAuthorization[\"']?\s*:\s*[\"']?Bearer\s+(?!REDACTED\b)[^\s\"']"),
    # MAM-29
    ("Basic credentials", r"\bAuthorization[\"']?\s*:\s*[\"']?Basic\s+(?!REDACTED\b)[^\s\"']"),
    ("Transmission session id", r"\bX-Transmission-Session-Id\s*:\s*(?!REDACTED)[^\s<\"']"),
    ("Jackett download token", r"[?&;]path=(?!REDACTED)[^&\"'<>\s]"),
    ("cookie", r"\b(?:Set-)?Cookie\s*:\s*(?!REDACTED)\S"),
    ("the content of a .torrent", r"d8:announce|4:infod"),
]
_LEAK_PATTERNS = [(name, re.compile(pattern, re.IGNORECASE)) for name, pattern in _LEAK_RULES]


def leaks(text: str) -> list[str]:
    """Names of the rules `text` would fail, plus "a secret typed into the script or handed out by a service" for each
    registered secret still in it. An empty list means the text is safe to keep."""
    # XML escapes the & between URL parameters and JSON encoders may write \u0026; the rules read the URLs as they are.
    plain = re.sub(r"\\u0026", "&", text.replace("&amp;", "&"), flags=re.IGNORECASE)
    found = [name for name, pattern in _LEAK_PATTERNS if pattern.search(plain)]
    if any(spelling in text for spelling in SECRETS.spellings()):
        found.append("a secret typed into the script or handed out by a service")
    return found


def make_safe(text: str) -> tuple[str, list[str]]:
    """The text as it may be written (LF line endings, redacted), or a note in its place when it still fails the leak
    check: then nothing of it reaches the disk."""
    text = redact_text(text.replace("\r\n", "\n"))
    problems = sorted(set(leaks(text)))
    if problems:
        return ("WITHHELD by the MAM-29 sample script: after redaction this text still failed the leak check ("
                + ", ".join(problems) + "), so it was not written.\n"), problems
    return text, []


# ---------------------------------------------------------------- output

class Recorder:
    """The output folder of one run: raw/ (every request and answer, secrets removed), samples-pending/ (redacted
    candidates named like the future VendorSamples files), INDEX.txt (one line per file) and console.txt (everything
    printed). Until the service's version is known the files wait in memory, because the folder is named after it."""

    def __init__(self, out_root: Path, prefix: str) -> None:
        self.out_root = out_root
        self.prefix = prefix
        self.folder: Path | None = None
        self.version_label = UNKNOWN_VERSION
        self.entries: list[tuple[str, str]] = []
        self.written: list[Path] = []
        self._buffer: list[tuple[str, str]] = []

    def open(self, name: str) -> Path:
        if self.folder is None:
            self.folder = ensure_dir(self.out_root / safe_name(name))
            for relative, text in self._buffer:
                self._put(relative, self._with_version(text))
            self._buffer.clear()
        return self.folder

    def _with_version(self, text: str) -> str:
        """A file recorded before the version was known (Transmission's handshake goes before session-get reads it)
        gets the version in its '# <time>, <version>' header line once it is known."""
        if self.version_label == UNKNOWN_VERSION:
            return text
        label = redact_text(self.version_label) + " (read after this request)"
        return re.sub(r"^(# \S+), " + re.escape(UNKNOWN_VERSION) + "$", lambda match: f"{match.group(1)}, {label}",
                      text, count=1, flags=re.MULTILINE)

    def raw(self, name: str, text: str, what: str) -> None:
        self._record(f"{RAW_DIR}/{name}", text, what)

    def pending(self, name: str, text: str, what: str) -> None:
        self._record(f"{PENDING_DIR}/{name}", text, what)

    def _record(self, relative: str, text: str, what: str) -> None:
        safe, problems = make_safe(text)
        if problems:
            say(f"  WITHHELD {relative}: after redaction it still failed the leak check ({', '.join(problems)})")
            what = f"WITHHELD, the leak check found {', '.join(problems)}; {what}"
        # The same name written twice in one run (a step run again) keeps one INDEX line, the last.
        self.entries = [(r, w) for r, w in self.entries if r != relative] + [(relative, what)]
        if self.folder is None:
            self._buffer = [(r, t) for r, t in self._buffer if r != relative] + [(relative, safe)]
        else:
            self._put(relative, safe)

    def _put(self, relative: str, text: str) -> None:
        assert self.folder is not None
        path = self.folder / relative
        ensure_dir(path.parent)
        # UTF-8 without a BOM and LF line endings, like every committed file (.gitattributes pins LF).
        path.write_text(text, encoding="utf-8", newline="\n")
        if path not in self.written:
            self.written.append(path)

    def _merged_rows(self, folder: Path) -> list[tuple[str, str, str]]:
        """This run's lines merged into the INDEX of an earlier run in the same folder: a file written again gets its new
        line where the old one stood, a file this run didn't touch keeps its line (and its version), so INDEX.txt
        still describes every file of the folder."""
        current = dict(self.entries)
        rows: list[tuple[str, str, str]] = []
        seen: set[str] = set()
        try:
            earlier = (folder / INDEX_NAME).read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            earlier = []
        for line in earlier:
            parts = line.split(" | ", 2)
            if len(parts) != 3:
                continue
            relative = parts[0].strip()
            if relative in seen or not (folder / relative).is_file():
                continue  # the header, or a file no longer there
            seen.add(relative)
            if relative in current:
                rows.append((relative, current[relative], self.version_label))
            else:
                rows.append((relative, parts[1], parts[2]))
        rows += [(relative, what, self.version_label) for relative, what in self.entries if relative not in seen]
        return rows

    def finish(self) -> int:
        """Writes INDEX.txt and console.txt, then reads back EVERY file of the folder through leaks(): a file that fails
        is overwritten with a note. Returns how many failed."""
        folder = self.open(f"{self.prefix}-unknown")
        self.entries = [(r, w) for r, w in self.entries if r != CONSOLE_NAME]
        self.entries.append((CONSOLE_NAME, "everything printed, run after run, secrets removed"))
        console, _ = make_safe(f"===== run of {now_iso()} =====\n" + "\n".join(console_lines()) + "\n")
        console_path = folder / CONSOLE_NAME
        if console_path.is_file():
            # A later run (say --only 13,14 after a long wait) adds to the record instead of replacing it.
            console = console_path.read_text(encoding="utf-8", errors="replace").rstrip("\n") + "\n\n" + console
        self._put(CONSOLE_NAME, console)
        rows = self._merged_rows(folder)
        width = max((len(relative) for relative, _, _ in rows), default=0)
        header = [f"MAM-29 samples: {self.version_label}. Last run {now_iso()}, by tools/mam29.",
                  "raw/ holds every request and answer with the secrets removed; samples-pending/ holds REDACTED candidates",
                  "named like the future files of tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/. Read each",
                  "one before it is copied; VendorSamples/README.md says how to name it and which row to add.",
                  "One line per file: the file | what it shows | the service version.", ""]
        lines = [f"{relative.ljust(width)} | {what} | {version}" for relative, what, version in rows]
        index, _ = make_safe("\n".join(header + lines) + "\n")
        self._put(INDEX_NAME, index)
        failed = 0
        for path in sorted(p for p in folder.rglob("*") if p.is_file()):
            problems = leaks(path.read_text(encoding="utf-8", errors="replace"))
            if problems:
                failed += 1
                path.write_text(f"WITHHELD by the MAM-29 sample script: the final leak check found "
                                f"{', '.join(sorted(set(problems)))}.\n", encoding="utf-8", newline="\n")
                say(f"!!! LEAK CHECK FAILED for {path.relative_to(folder)}: {', '.join(sorted(set(problems)))}; "
                    "its content was replaced by a note.")
        say()
        say(f"Files written under {folder}:")
        for path in self.written:
            say(f"  {path.relative_to(folder)}")
        if failed:
            say(f"!!! The final leak check failed for {failed} file(s); see above.")
        else:
            say("Final leak check over every file of the folder: clean.")
        return failed
