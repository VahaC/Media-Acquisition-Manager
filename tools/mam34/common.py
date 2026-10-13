r"""Shared helpers of the MAM-34 owner-run scripts (inventory.py, identify_check.py).

Not run on its own: the scripts sit in this folder and `import common`.

Reads:  nothing by itself. A script hands it the Jellyfin address and the API key, which the script asks for
        with hidden input (the only environment fallback, MAM34_JELLYFIN_API_KEY, exists for unattended tests).
Writes: only what a script tells it to: files under D:\Sources\Media-Acquisition-Manager\.claude\mam34-check\
        (gitignored). It never writes the API key anywhere and keeps it out of its own error messages.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam34\inventory.py
        python D:\Sources\Media-Acquisition-Manager\tools\mam34\identify_check.py

Python 3.10+, standard library only (the owner's PC has no pip packages).
"""

from __future__ import annotations

import getpass
import http
import http.client
import json
import os
import re
import shutil
import subprocess
import sys
import time
import unicodedata
import urllib.parse
from pathlib import Path

# ---------------------------------------------------------------- constants

REPO_ROOT = Path(__file__).resolve().parents[2]
# Everything a script writes goes here; .claude/ is gitignored, so a recorded answer can't be committed by accident.
OUT_DIR = REPO_ROOT / ".claude" / "mam34-check"

DEFAULT_JELLYFIN_URL = "http://192.168.1.195:8096"
DEFAULT_MAM_URL = "http://192.168.1.195:8118"
DEFAULT_SSH_HOST = "root@192.168.1.195"
MAM_CONTAINER = "media-acquisition-manager"
JELLYFIN_CONTAINER = "jellyfin"

# The one environment variable that may stand in for the typed API key. Scripts must name it in --help.
ENV_JELLYFIN_KEY = "MAM34_JELLYFIN_API_KEY"

REDACTED = "REDACTED"  # the only placeholder VendorSamples/README.md prescribes


class ToolError(Exception):
    """A failure the scripts report as one line instead of a traceback."""


class JellyfinError(ToolError):
    """The request could not be made or answered (transport, timeout, a refused key shape); never an HTTP status."""


class ReadOnlyError(ToolError):
    """A write was attempted on a client created with read_only=True. Not a JellyfinError: a handler written for
    transport problems must never swallow a script's attempt to write."""


class SshError(ToolError):
    """ssh could not be started."""


def ensure_out_dir() -> Path:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    return OUT_DIR


def out_path(*parts: str) -> Path:
    """A path under OUT_DIR whose folders exist, so a script can open it for writing straight away."""
    path = OUT_DIR.joinpath(*parts)
    path.parent.mkdir(parents=True, exist_ok=True)
    return path


# ---------------------------------------------------------------- console and prompts

def setup_console() -> None:
    """Titles are Ukrainian. A Windows console or a redirected file may use a legacy code page that can't encode
    them, and one unencodable character would end the script with UnicodeEncodeError halfway through a run."""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="replace")


def ask(prompt: str, default: str | None = None) -> str:
    suffix = f" [{default}]" if default else ""
    try:
        answer = input(f"{prompt}{suffix}: ").strip()
    except EOFError:  # no console to ask on: act as if Enter was pressed
        answer = ""
    return answer or (default or "")


def _clean_secret(value: str) -> str:
    # A pasted key often brings a trailing newline or the quotes it was shown in; either would make
    # the server answer 401 and look like a wrong key.
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        value = value[1:-1].strip()
    return value


def ask_secret(prompt: str, env_name: str | None = None) -> str:
    """Hidden input. `env_name` is a fallback for unattended tests only: the owner types the secret."""
    if env_name:
        from_env = os.environ.get(env_name, "").strip()
        if from_env:
            print(f"{prompt}: taken from the environment variable {env_name}")
            return _clean_secret(from_env)
    try:
        return _clean_secret(getpass.getpass(f"{prompt}: "))
    except EOFError:
        return ""


def confirm(prompt: str) -> bool:
    """True only for a typed y/yes. Enter, anything else and a closed input are all "no": a write must be asked for."""
    try:
        answer = input(f"{prompt} [y/N]: ").strip().lower()
    except EOFError:
        return False
    return answer in ("y", "yes")


# ---------------------------------------------------------------- files

def _under_out_dir(path: str | os.PathLike[str]) -> Path:
    # Looked up at call time, so a test can point OUT_DIR somewhere else.
    target = Path(path)
    return target if target.is_absolute() else OUT_DIR / target


def write_text(path: str | os.PathLike[str], text: str) -> Path:
    """UTF-8 without a BOM and LF line endings, like every committed file (.gitattributes pins LF).
    A relative path is relative to OUT_DIR."""
    target = _under_out_dir(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")
    return target


def write_json(path: str | os.PathLike[str], obj) -> Path:
    # ensure_ascii=False: the samples keep Ukrainian titles readable, not as \u escapes.
    return write_text(path, json.dumps(obj, indent=2, ensure_ascii=False) + "\n")


# ---------------------------------------------------------------- Jellyfin

_UNSET = object()


def _reason_phrase(status: int) -> str:
    try:
        return http.HTTPStatus(status).phrase
    except ValueError:
        return ""


class Reply:
    """One answer. An HTTP error status is data, not an exception: the checks exist to record 400s and 404s."""

    def __init__(self, status: int, seconds: float, text: str, headers: dict | None = None,
                 reason: str = "", http_version: str = "HTTP/1.1") -> None:
        self.status = status
        self.seconds = seconds
        self.text = text
        self.headers = dict(headers or {})
        self.reason = reason or _reason_phrase(status)
        self.http_version = http_version
        self._json = _UNSET

    @property
    def ok(self) -> bool:
        return 200 <= self.status < 300

    @property
    def json(self):
        """The parsed body, or None when it is empty or not JSON (a 400 from Jellyfin is plain text)."""
        if self._json is _UNSET:
            body = self.text.lstrip("\ufeff").strip()
            try:
                self._json = json.loads(body) if body else None
            except ValueError:
                self._json = None
        return self._json

    def header(self, name: str, default: str | None = None) -> str | None:
        wanted = name.lower()
        for key, value in self.headers.items():
            if key.lower() == wanted:
                return value
        return default

    def data(self):
        """The parsed JSON of a 2xx answer; JellyfinError for anything else, so a script can't go on with a refusal."""
        if not self.ok:
            raise JellyfinError(f"HTTP {self.status} {self.reason}: {self._excerpt()}")
        if self.json is None and self.text.strip():
            raise JellyfinError(f"HTTP {self.status} but the body is not JSON: {self._excerpt()}")
        return self.json

    def as_sample(self) -> str:
        """The status line, headers, a blank line and the body, the way VendorSamples/Jellyfin/*-response.txt keep an
        answer. The body is followed by a newline only when there is one, so a bodiless 204 ends at the blank line."""
        lines = [f"{self.http_version} {self.status} {self.reason}".rstrip()]
        lines += [f"{name}: {value}" for name, value in self.headers.items()]
        body = self.text.rstrip("\n")
        return "\n".join(lines) + "\n\n" + (body + "\n" if body else "")

    def _excerpt(self) -> str:
        return " ".join(self.text.split())[:200] or "(empty body)"

    def __repr__(self) -> str:
        return f"Reply({self.status} {self.reason}, {self.seconds:.2f}s, {len(self.text)} chars)"


def guid32(text: str) -> str:
    """An item or user id as Jellyfin's "N" form: 32 lowercase hex digits. Dashes are accepted on the way in."""
    plain = str(text).strip().replace("-", "").lower()
    if not re.fullmatch(r"[0-9a-f]{32}", plain):
        raise JellyfinError(f"{text!r} is not a Jellyfin item id (32 hex digits).")
    return plain


def _normalize_location(path: str) -> str:
    path = path.replace("\\", "/")
    while "//" in path:
        path = path.replace("//", "/")
    return path


def library_for_path(folders: list[dict] | None, path: str) -> dict | None:
    """The GET /Library/VirtualFolders row whose Locations hold `path`, or None. Matches whole path segments, so
    /media/movies2/x is not in /media/movies; the longest location wins when libraries nest."""
    candidate = _normalize_location(path).rstrip("/") + "/"
    best, best_length = None, -1
    for folder in folders or []:
        for location in folder.get("Locations") or []:
            prefix = _normalize_location(location).rstrip("/") + "/"
            if candidate.startswith(prefix) and len(prefix) > best_length:
                best, best_length = folder, len(prefix)
    return best


def _check_key_shape(key: str) -> None:
    # The key goes into a quoted header value. Whitespace, a quote or a non-ASCII character would either break the
    # header (http.client then raises a ValueError that quotes the whole key) or be sent wrong and read as a refusal.
    # The message never repeats the value.
    # It says how long the input was and what kind of character is in it, never which ones: that is enough to tell a
    # stray Ctrl+V (a control character), a second paste (too long) or a Ukrainian keyboard layout (non-ASCII).
    problems = []
    for name, found in (("whitespace", sum(c.isspace() for c in key)),
                        ("quote or backslash characters", sum(c in '"\\' for c in key)),
                        ("control characters (a Ctrl+V typed into the prompt?)",
                         sum(ord(c) < 32 and not c.isspace() for c in key)),
                        ("non-ASCII characters (a Ukrainian keyboard layout?)", sum(ord(c) > 127 for c in key))):
        if found:
            problems.append(f"{found} of {name}")
    if problems:
        raise JellyfinError(f"The API key you entered is {len(key)} characters long and holds " + "; ".join(problems)
                            + ". A Jellyfin key is 32 letters and digits: copy it again from Jellyfin > Dashboard > "
                              "API Keys and paste it with a right-click (or Shift+Insert) in the console.")


class Jellyfin:
    """A small Jellyfin 12 client. The auth header is `Authorization: MediaBrowser Token="<key>"`; the legacy
    X-Emby-Token and api_key= are switched off in 12. Timeouts apply to each wait for the server, which for these
    synchronous calls is the whole wait; nothing is retried, because a retried write could be applied twice."""

    def __init__(self, base_url: str, api_key: str, timeout: float = 30.0, read_only: bool = False) -> None:
        if not api_key:
            raise JellyfinError("No Jellyfin API key.")
        _check_key_shape(api_key)
        if "://" not in base_url:
            base_url = "http://" + base_url
        parts = urllib.parse.urlsplit(base_url)
        if parts.scheme not in ("http", "https") or not parts.hostname:
            raise JellyfinError(f"{base_url!r} is not an http(s) address.")
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.timeout = timeout
        self._read_only = read_only
        self._parts = parts
        self._admin_user_id: str | None = None
        self._folders: list[dict] | None = None

    @property
    def read_only(self) -> bool:
        # No setter: a script that asked for a read-only client can't switch it off later by assigning to it.
        return self._read_only

    def __repr__(self) -> str:  # never the key: a repr ends up in tracebacks and logs
        return f"Jellyfin({self.base_url!r}, read_only={self.read_only})"

    # -- transport

    def request(self, method: str, path: str, query: dict | None = None, body=None,
                timeout: float | None = None, api_key_override: str | None = None) -> Reply:
        """Send one request. `path` has no query string (use `query`: None values are skipped, booleans become
        true/false, a list or tuple is joined with commas). `body` is any JSON value, sent as UTF-8
        application/json, or bytes, sent as they are. `api_key_override` replaces the key for this call (a wrong
        one records the 401); an empty string sends no Authorization header at all."""
        method = method.strip().upper()
        if self.read_only and method != "GET":
            # Raised before anything is sent: the inventory must be unable to write, not merely careful not to.
            raise ReadOnlyError(f"Refusing {method} {path}: this client is read-only.")
        key = self.api_key if api_key_override is None else api_key_override
        if key:
            _check_key_shape(key)
        extra_secrets = (key,) if key != self.api_key else ()

        headers = {"Accept": "application/json", "Connection": "close", "User-Agent": "MAM-34-check"}
        if key:
            headers["Authorization"] = f'MediaBrowser Token="{key}"'
        payload = None
        if body is not None:
            if isinstance(body, (bytes, bytearray)):
                payload = bytes(body)
            else:
                # ensure_ascii=False keeps Ukrainian text as UTF-8 bytes, which is what Jellyfin's JSON reader expects.
                payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json"

        target = self._target(path, query)
        wait = self.timeout if timeout is None else timeout
        connection_type = http.client.HTTPSConnection if self._parts.scheme == "https" else http.client.HTTPConnection
        # http.client, not urllib: it ignores system proxy settings (a proxy would hijack LAN calls), follows no
        # redirects behind the owner's back, and returns an error status like any other.
        connection = connection_type(self._parts.hostname, self._parts.port, timeout=wait)
        started = time.perf_counter()
        try:
            try:
                connection.request(method, target, body=payload, headers=headers)
                response = connection.getresponse()
                raw = response.read()
                seconds = time.perf_counter() - started
                reply = Reply(response.status, seconds, raw.decode("utf-8", errors="replace"),
                              dict(response.getheaders()), response.reason or "",
                              "HTTP/1.1" if response.version == 11 else "HTTP/1.0")
            except TimeoutError:
                hint = "" if method == "GET" else "; the server may still be doing it"
                message = f"{method} {target} got no answer in {wait:g}s{hint}"
                error = JellyfinError(self._scrub(message, *extra_secrets))
            except (OSError, http.client.HTTPException, ValueError) as failure:
                message = f"{method} {target} failed: {type(failure).__name__}: {failure}"
                error = JellyfinError(self._scrub(message, *extra_secrets))
            else:
                return reply
        finally:
            connection.close()
        # Raised outside the except blocks and without a cause: a ValueError from http.client quotes the header
        # value, and the traceback of a chained exception would print the key.
        raise error from None

    def get(self, path: str, query: dict | None = None, **options) -> Reply:
        return self.request("GET", path, query, **options)

    def post(self, path: str, body=None, query: dict | None = None, **options) -> Reply:
        return self.request("POST", path, query, body, **options)

    def _target(self, path: str, query: dict | None) -> str:
        if not path.startswith("/"):
            path = "/" + path
        target = self._parts.path.rstrip("/") + urllib.parse.quote(path, safe="/%:@")
        pairs = []
        for name, value in (query or {}).items():
            if value is None:
                continue
            if isinstance(value, bool):
                value = "true" if value else "false"
            elif isinstance(value, (list, tuple)):
                value = ",".join(str(item) for item in value)
            pairs.append((name, str(value)))
        if pairs:
            target += "?" + urllib.parse.urlencode(pairs, quote_via=urllib.parse.quote)
        return target

    def _scrub(self, text: str, *extra_secrets: str) -> str:
        for secret in (self.api_key, *extra_secrets):
            if secret:
                text = text.replace(secret, REDACTED)
        return text

    # -- conveniences (each returns the Reply; use .json or .data())

    def system_info(self) -> Reply:
        """GET /System/Info (needs the key; /System/Info/Public does not)."""
        return self.get("/System/Info")

    def virtual_folders(self) -> Reply:
        return self.get("/Library/VirtualFolders")

    def plugins(self) -> Reply:
        return self.get("/Plugins")

    def admin_user_id(self) -> str:
        """The first user in Jellyfin's order who is an enabled administrator, as 32 hex digits (what the app does).
        Reading one item needs a user: without one Jellyfin answers 400. Cached."""
        if self._admin_user_id is None:
            users = self.get("/Users").data()
            for user in users if isinstance(users, list) else []:
                policy = user.get("Policy") or {}
                if policy.get("IsAdministrator") is True and not policy.get("IsDisabled"):
                    self._admin_user_id = guid32(user.get("Id", ""))
                    break
            else:
                raise JellyfinError("GET /Users lists no enabled administrator, so no item can be read.")
        return self._admin_user_id

    def item(self, item_id: str) -> Reply:
        """GET /Items/{id}?userId={administrator}: the whole item."""
        return self.get(f"/Items/{guid32(item_id)}", {"userId": self.admin_user_id()})

    def list_items(self, query: dict) -> Reply:
        """GET /Items the way the library sync asks (no user), and again with the administrator's userId if Jellyfin
        answers 400 or 401. Pass collapseBoxSetItems=false when listing movies: without a user Jellyfin otherwise
        replaces the movies of a collection with one BoxSet item."""
        reply = self.get("/Items", query)
        if reply.status in (400, 401):
            reply = self.get("/Items", {**query, "userId": self.admin_user_id()})
        return reply

    def library_for(self, path: str) -> dict | None:
        """The VirtualFolders row `path` (a path as Jellyfin sees it, /media/...) belongs to. The list is read once."""
        if self._folders is None:
            folders = self.virtual_folders().data()
            self._folders = folders if isinstance(folders, list) else []
        return library_for_path(self._folders, path)


# ---------------------------------------------------------------- redaction (VendorSamples/README.md)

# The names VendorSamplesTests treats as secrets in a URL parameter (copied from it: a sample that passes
# redaction here must pass that test).
KEY_LIKE_NAMES = ("apikey|api_key|api_token|apitoken|jackett_apikey|passkey|pk|uk|tp|token|access_token|link|"
                  "authkey|torrent_pass|rsskey|key|secret|password|passwd|pwd|pass|auth|sig|signature|x-plex-token")
_CREDENTIAL_KEY = re.compile(r"[A-Za-z_-]*(?:api_?key|access_?token|password|passwd|passkey|secret|cookie|X-Emby-Token|session-id)",
                             re.IGNORECASE)

_REDACTIONS = [
    # a key-like URL parameter, e.g. ?apikey=...&link=...
    (re.compile(r"([?&;](?:%s)=)(?!REDACTED)[^&\"'<>\s]+" % KEY_LIKE_NAMES, re.IGNORECASE), r"\1REDACTED"),
    # a session id, e.g. SID=... or QBT_SID_8080=...
    (re.compile(r"(\b(?:QBT_)?SID(?:_\d+)?=)(?!REDACTED)[^&\"'<>\s;]+", re.IGNORECASE), r"\1REDACTED"),
    # Authorization: MediaBrowser Client="...", Token="..."
    (re.compile(r"(\bMediaBrowser\b[^\r\n]*?\bToken=\")(?!REDACTED\")[^\"]*", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\b(?:X-Api-Key|X-Emby-Token)\s*:\s*)(?!REDACTED)\S+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\bAuthorization[\"']?\s*:\s*[\"']?Bearer\s+)(?!REDACTED\b)[^\s\"']+", re.IGNORECASE), r"\1REDACTED"),
    # MAM-29: Transmission's Basic credential and CSRF session id, Jackett's download token, a cookie header
    (re.compile(r"(\bAuthorization[\"']?\s*:\s*[\"']?Basic\s+)(?!REDACTED\b)[^\s\"']+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\bX-Transmission-Session-Id\s*:\s*)(?!REDACTED)[^\s<\"']+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"([?&;]path=)(?!REDACTED)[^&\"'<>\s]+", re.IGNORECASE), r"\1REDACTED"),
    (re.compile(r"(\b(?:Set-)?Cookie\s*:\s*)(?!REDACTED)[^\r\n]+", re.IGNORECASE), r"\1REDACTED"),
]


def _literal_pairs(secrets, replacements) -> list[tuple[str, str]]:
    pairs: dict[str, str] = {}
    for secret in secrets or ():
        if not secret:
            continue
        # A key that travelled in a URL or inside a JSON string is spelled differently there.
        for spelling in (secret, urllib.parse.quote(secret, safe=""), urllib.parse.quote_plus(secret),
                         json.dumps(secret, ensure_ascii=False)[1:-1]):
            pairs[spelling] = REDACTED
    for literal, placeholder in (replacements or {}).items():
        if literal:
            pairs[literal] = placeholder
    # Longest first, so a literal that contains another one is replaced whole.
    return sorted(pairs.items(), key=lambda pair: len(pair[0]), reverse=True)


def _redact_string(text: str, pairs: list[tuple[str, str]]) -> str:
    for literal, placeholder in pairs:
        text = text.replace(literal, placeholder)
    for pattern, replacement in _REDACTIONS:
        text = pattern.sub(replacement, text)
    return text


def redact_text(text: str, secrets=(), replacements: dict[str, str] | None = None) -> str:
    """`secrets` (the API key and anything else typed in) become REDACTED, in the plain, URL-encoded and JSON-escaped
    spellings; `replacements` maps any other literal to the placeholder it gets; key-like URL parameters, session
    ids and Authorization-style headers are redacted by shape. VendorSamples/README.md prescribes REDACTED for
    credentials only: user names, user ids and the server id are kept as they are in the committed samples
    (users.json, item.json), so they are replaced only if the caller names them in `replacements`."""
    return _redact_string(text, _literal_pairs(secrets, replacements))


def redact_json(obj, secrets=(), replacements: dict[str, str] | None = None):
    """A deep copy of `obj` with every string passed through redact_text, and the string value of a credential-like key
    (AccessToken, ApiKey, Password...) replaced by REDACTED. The original is left alone."""
    pairs = _literal_pairs(secrets, replacements)

    def walk(value):
        if isinstance(value, str):
            return _redact_string(value, pairs)
        if isinstance(value, dict):
            copy = {}
            for key, item in value.items():
                new_key = _redact_string(key, pairs) if isinstance(key, str) else key
                if isinstance(key, str) and isinstance(item, str) and item and _CREDENTIAL_KEY.fullmatch(key):
                    copy[new_key] = REDACTED
                else:
                    copy[new_key] = walk(item)
            return copy
        if isinstance(value, (list, tuple)):
            return [walk(item) for item in value]
        return value

    return walk(obj)


# The rules of VendorSamplesTests.cs, so a candidate sample can be checked here before anyone copies it into
# the repository, where the build would fail on a leak.
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
    # MAM-29 (the same five as tools/mam29/common.py)
    ("Basic credentials", r"\bAuthorization[\"']?\s*:\s*[\"']?Basic\s+(?!REDACTED\b)[^\s\"']"),
    ("Transmission session id", r"\bX-Transmission-Session-Id\s*:\s*(?!REDACTED)[^\s<\"']"),
    ("Jackett download token", r"[?&;]path=(?!REDACTED)[^&\"'<>\s]"),
    ("cookie", r"\b(?:Set-)?Cookie\s*:\s*(?!REDACTED)\S"),
    ("the content of a .torrent", r"d8:announce|4:infod"),
]
_LEAK_PATTERNS = [(name, re.compile(pattern, re.IGNORECASE)) for name, pattern in _LEAK_RULES]


def leaks(text: str, secrets=()) -> list[str]:
    """Names of the VendorSamplesTests rules `text` would fail, plus "a secret typed into a script" for each of
    `secrets` that is still in it. An empty list means the text is safe to offer as a sample."""
    # XML escapes the & between URL parameters and JSON encoders may write &; the rules read the URLs as they are.
    plain = re.sub(r"\\u0026", "&", text.replace("&amp;", "&"), flags=re.IGNORECASE)
    found = [name for name, pattern in _LEAK_PATTERNS if pattern.search(plain)]
    found += ["a secret typed into a script" for secret in secrets if secret and secret in text]
    return found


# ---------------------------------------------------------------- ssh

def ssh_run(host: str, remote_command: str, stdin_bytes: bytes | None = None,
            timeout: float = 600) -> tuple[int, bytes, str]:
    """Run `remote_command` on `host` with the Windows OpenSSH client; returns (returncode, stdout bytes, "").
    Build the command with shlex.quote for anything that isn't a literal. ssh asks for the password itself, on the
    console, so stdin is left alone (it is piped only when `stdin_bytes` is given, for `docker exec -i ... < data`).
    stderr is NOT captured either: it stays on the console, as in the mam33 check that is known to show ssh's password
    prompt and errors; the third item is always "" and is kept so callers keep their shape.
    On a timeout ssh is killed and the code is 124; a command already started on the host may keep running."""
    ssh = shutil.which("ssh")
    if ssh is None:
        raise SshError("The OpenSSH client (ssh) is not on PATH. Windows: Settings > Apps > Optional features > "
                       "OpenSSH Client.")
    print(f"ssh {host}: it will ask for the password in this window (nothing shows while you type; press Enter).",
          flush=True)
    # -T: no pseudo-terminal, which would turn the binary stdout's \n into \r\n. ConnectTimeout: a host that is down
    # fails in seconds, not after the TCP timeout.
    command = [ssh, "-T", "-o", "ConnectTimeout=20", host, remote_command]
    try:
        done = subprocess.run(command, input=stdin_bytes, stdout=subprocess.PIPE, timeout=timeout)
    except subprocess.TimeoutExpired as expired:
        print(f"ssh did not finish in {timeout:g}s and was stopped", flush=True)
        return 124, expired.stdout or b"", ""
    except OSError as error:
        raise SshError(f"could not start ssh: {error}") from None
    return done.returncode, done.stdout, ""


# ---------------------------------------------------------------- titles

_APOSTROPHES = str.maketrans({"\u2018": "'", "\u2019": "'", "\u02bc": "'", "\u00b4": "'", "`": "'"})
_BRACKET_TAGS = re.compile(r"\[[^\]]*\]")
_TRAILING_YEAR = re.compile(r"\s*\(\s*\d{4}(?:\s*[-\u2013]\s*\d{4})?\s*\)\s*$")
_LEADING_NUMBER = re.compile(r"^\s*\d+\s*[.)]\s+")


def normalize_title(text: str) -> str:
    """A title as Jellyfin shows it ("120. Landing_Stripling (1962) [BDRip]") brought to the form TMDb's title can be
    compared with: no leading numbering, underscores as spaces, no trailing (year) or [release tags], one apostrophe
    and one Unicode form (a macOS file name can hold a decomposed "й"), lowercase, single spaces."""
    title = unicodedata.normalize("NFC", text or "").translate(_APOSTROPHES).replace("_", " ")
    title = " ".join(_BRACKET_TAGS.sub(" ", title).split())
    title = _TRAILING_YEAR.sub("", title)
    title = _LEADING_NUMBER.sub("", title)
    return " ".join(title.split()).lower().strip(" -")
