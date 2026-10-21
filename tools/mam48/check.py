r"""MAM-48: check on the live installation what Phase E (0.5.0) promises about personal access tokens and the MCP
endpoint, with tokens the owner copies from Profile's shown-once step.

Reads:  MAM's HTTP API at --base-url (default http://192.168.1.195:8118) as each token's user, nothing else:
        GET /api/downloads, the jobs line Profile's shown-once step and the README print, run through Windows' curl.exe
        with -q first (it reads no .curlrc or _curlrc, so no config file can turn on -v or --trace and write the
        header out) and the Authorization header on stdin (`-H @-`), so the token is never on a command line; POST
        /api/downloads with all-zero ids, which a Read only token is refused; and POST /mcp the way Claude Code talks to it
        (initialize, notifications/initialized, tools/list, tools/call get_status, and tools/call send_download with
        all-zero ids, which a Read only token is refused). The expected sentences come from this checkout's
        src/MediaAcquisitionManager.Api/Locales/{en,uk}/account.json.
Writes: nothing on MAM by itself: no job, no search, no setting. The server keeps its own traces of the check: the
        PersonalAccessTokenRefused audit row of a refused send (at most one a minute per token) and each token's
        "Last used". On this PC, token-check-<step>.txt and token-check-<step>.json in
        D:\Sources\Media-Acquisition-Manager\.claude\mam48-check\ (gitignored); a step's files written before are kept
        as token-check-<step>-<UTC time>.txt/.json. No line printed and no file written holds a token: every mam_pat_
        value is cut to its first 14 characters, as Profile's list shows it, and a file that would still hold one is
        not written.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam48\check.py
            the guided run: every step in order, asking for the two tokens once at the start; while you revoke on
            Profile (and, if you want the member step, on the Users page) it reads the jobs every 3 s until MAM refuses
            the token, for at most 10 minutes (--no-poll: it waits for Enter instead)
        python D:\Sources\Media-Acquisition-Manager\tools\mam48\check.py <step> [--poll]
            one step alone: read, refused-send, mcp-tools, mcp-write, revoked or member; revoked and member wait for
            Enter unless --poll is given

The tokens are the script's own, never a Claude client's: "MAM-48 script read only" (revoked at the revoked step),
"MAM-48 script read and download" (revoked by the owner right after the run) and the temporary member mam48-member's
"MAM-48 member" (the owner's Revoke tokens on Users). They are asked for with hidden input, only in a real console:
on Windows the script takes the token from the clipboard by itself once Copy on Profile changes it (or on Enter or
Ctrl+V), and empties the clipboard of it; elsewhere getpass. They are held in memory for the run only. --tokens-from-env reads them from MAM48_READ_ONLY_TOKEN,
MAM48_READ_AND_DOWNLOAD_TOKEN and MAM48_MEMBER_TOKEN instead; it exists for trying the script against a local API
with test tokens, never for the check on production. No child process (curl.exe, git) gets a MAM48_ variable.

Python 3.10+, standard library only; Windows' curl.exe (C:\Windows\System32\curl.exe, curl 7.55 or later for -H @-).
"""

from __future__ import annotations

import argparse
import datetime as dt
import getpass
import http.client
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.parse
import warnings
from pathlib import Path

# Nothing of a run lands in the repository as bytecode (tools/mam34 keeps its own __pycache__ ignored).
sys.dont_write_bytecode = True
# common.py of the MAM-34 tools: the console setup, the answer type and the one-line error type. Nothing else is shared.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mam34"))
import common  # noqa: E402
from common import Reply, ToolError  # noqa: E402

# ---------------------------------------------------------------- constants

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT_DIR = REPO_ROOT / ".claude" / "mam48-check"
LOCALES_DIR = REPO_ROOT / "src" / "MediaAcquisitionManager.Api" / "Locales"
DEFAULT_BASE_URL = "http://192.168.1.195:8118"
DEFAULT_CURL = r"C:\Windows\System32\curl.exe"
DEFAULT_EXPECTED_VERSION = "0.5.0"
SCRIPT = "tools/mam48/check.py"

# The initialize handshake Claude Code 2.1.220 and mcp-remote speak (McpHttpExtensions.ClaudeCodeProtocolVersion).
PROTOCOL_VERSION = "2025-11-25"
# ADR 0041: nine reads for any token, three writes for Read and download only.
READ_TOOLS = ("get_status", "search_library", "search_releases", "list_search_releases", "get_download_options",
              "list_jobs", "get_job", "list_duplicates", "list_recommendations")
WRITE_TOOLS = ("send_download", "cancel_job", "dismiss_recommendation")
REQUIRES_USER_INTERACTION = "anthropic/requiresUserInteraction"

# The all-zero GUID: DownloadRules.Validate refuses it as a missing id before any row is read, so a send that carries
# it can't write a job, ask a client or replay an earlier send, whatever the token's access (see the README).
ZERO_ID = "00000000-0000-0000-0000-000000000000"
HARMLESS_SEND = {"requestId": ZERO_ID, "releaseId": ZERO_ID, "downloadClientId": ZERO_ID, "destinationId": ZERO_ID}

# A token: mam_pat_ and 43 base64url characters (ADR 0040). Profile's list shows its first 14 characters.
TOKEN_SHAPE = re.compile(r"mam_pat_[A-Za-z0-9_-]{43}")
TOKEN_LIKE = re.compile(r"mam_pat_[A-Za-z0-9_-]*")
SHOWN_LENGTH = 14
JOB_LIST_KEYS = ("items", "totalCount", "page", "pageSize", "needsYouCount", "inProgressCount")

STEPS = ("read", "refused-send", "mcp-tools", "mcp-write", "revoked", "member")
STEP_TITLES = {
    "read": "Read: the jobs line of Profile and the README with the Read only token",
    "refused-send": "Refused send: POST /api/downloads with the Read only token, in English and in Ukrainian",
    "mcp-tools": "MCP tools: what each token's assistant is offered",
    "mcp-write": "MCP write with the Read only token: send_download is refused with the read-only sentence",
    "revoked": "Revoked: the Read only token answers 401 at once after Revoke",
    "member": "Member: a member's token works, then answers 401 after the owner's Revoke tokens",
}

TOKEN_KINDS = {
    # kind: (what the prompt calls it, the environment variable --tokens-from-env reads, its name in README's plan)
    "read-only": ("the Read only token", "MAM48_READ_ONLY_TOKEN", "MAM-48 script read only"),
    "read-and-download": ("the Read and download token", "MAM48_READ_AND_DOWNLOAD_TOKEN",
                          "MAM-48 script read and download"),
    "member": ("the member's token", "MAM48_MEMBER_TOKEN", "MAM-48 member"),
}
MEMBER_USER = "mam48-member"
# How each token is made, for its prompt.
TOKEN_CREATE = {
    "read-only": "on your Profile (Профіль → Токени доступу → Створити токен) create",
    "read-and-download": "on your Profile create a second token,",
    "member": f"signed in as {MEMBER_USER}, on its Profile create",
}
TOKEN_ACCESS = {
    "read-only": "Read only (Лише читання)",
    "read-and-download": "Read and download (Читання й завантаження)",
    "member": "Read only (Лише читання)",
}
# Where each token is revoked, for the closing reminder.
REVOKE_WHERE = {
    "read-only": "Revoke it on Profile → Access tokens",
    "read-and-download": "Revoke it on Profile → Access tokens",
    "member": f"Press Revoke tokens on {MEMBER_USER}'s row on Users",
}

# --poll: the jobs line every 3 s while the owner revokes, for at most 10 minutes.
POLL_SECONDS = 3
POLL_LIMIT_SECONDS = 600

CONSOLE_NEEDED = ("Run it in a PowerShell window: this console would show the token as you paste it, so it was not "
                  "asked for.")
STEP_TOKENS = {
    "read": ("read-only",),
    "refused-send": ("read-only",),
    "mcp-tools": ("read-only", "read-and-download"),
    "mcp-write": ("read-only",),
    "revoked": ("read-only",),
    "member": ("member",),
}


class StopRun(ToolError):
    """A token was refused (401) or the address hit its sign-in limit (429): going on would only cost more attempts."""


# ---------------------------------------------------------------- never a token

_SECRETS: list[str] = []  # every token of this run, longest first
_FAILED_AUTHENTICATIONS = [0]  # requests with a token answered 401: each one took a permit of the sign-in limit


def shown(token: str) -> str:
    """A token as Profile's list shows it: mam_pat_ and six more characters, then an ellipsis."""
    return token[:SHOWN_LENGTH] + "…"


def scrub(text: str) -> str:
    """`text` with every token of the run, and anything else that looks like one, cut to its first 14 characters."""
    for secret in _SECRETS:
        text = text.replace(secret, shown(secret))
    return TOKEN_LIKE.sub(lambda match: match.group(0) if len(match.group(0)) <= SHOWN_LENGTH
                          else match.group(0)[:SHOWN_LENGTH] + "…", text)


def holds_a_token(text: str) -> bool:
    return any(secret in text for secret in _SECRETS) or re.search(r"mam_pat_[A-Za-z0-9_-]{7,}", text) is not None


def say(text: str = "") -> None:
    print(scrub(text), flush=True)


# ---------------------------------------------------------------- tokens

def shape_problem(value: str) -> str | None:
    """Why `value` can't be a token, without repeating any of it; None when it has a token's shape."""
    if TOKEN_SHAPE.fullmatch(value):
        return None
    problems = []
    if not value.startswith("mam_pat_"):
        problems.append("it doesn't start with mam_pat_")
    for name, found in (("whitespace", sum(c.isspace() for c in value)),
                        ("control characters (a Ctrl+V typed into the prompt?)",
                         sum(ord(c) < 32 and not c.isspace() for c in value)),
                        ("non-ASCII characters (a Ukrainian keyboard layout?)", sum(ord(c) > 127 for c in value)),
                        ("characters a token never has", sum(ord(c) <= 127 and not c.isspace() and ord(c) >= 32
                                                             and not (c.isalnum() or c in "_-") for c in value))):
        if found:
            problems.append(f"{found} of {name}")
    return (f"it is {len(value)} characters long, where a token is 51 (mam_pat_ and 43 letters, digits, - and _)"
            + ("; " + "; ".join(problems) if problems else ""))


def clean(value: str) -> str:
    # A pasted value often brings a trailing newline or the quotes it was shown in.
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        value = value[1:-1].strip()
    return value


def require_console() -> None:
    """Hidden input works only in a real console. Anywhere else (an IDE's run window, a pipe) getpass falls back to
    plain input that shows the token, with a GetPassWarning: refused before the first prompt, and the warning made an
    error in case such a console still gets through."""
    if sys.stdin is None or not sys.stdin.isatty():
        raise ToolError(CONSOLE_NEEDED)
    warnings.filterwarnings("error", category=getpass.GetPassWarning)


# ---------------------------------------------------------------- hidden input and the clipboard

CF_UNICODETEXT = 13


def _clipboard_api():
    """user32 and kernel32 with the argument and result types a 64-bit handle needs (ctypes' default int cuts it)."""
    import ctypes
    from ctypes import wintypes
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    user32.OpenClipboard.argtypes = [wintypes.HWND]
    user32.OpenClipboard.restype = wintypes.BOOL
    user32.CloseClipboard.argtypes = []
    user32.CloseClipboard.restype = wintypes.BOOL
    user32.EmptyClipboard.argtypes = []
    user32.EmptyClipboard.restype = wintypes.BOOL
    user32.GetClipboardData.argtypes = [wintypes.UINT]
    user32.GetClipboardData.restype = wintypes.HANDLE
    kernel32.GlobalLock.argtypes = [wintypes.HGLOBAL]
    kernel32.GlobalLock.restype = wintypes.LPVOID
    kernel32.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
    kernel32.GlobalUnlock.restype = wintypes.BOOL
    user32.GetClipboardSequenceNumber.argtypes = []
    user32.GetClipboardSequenceNumber.restype = wintypes.DWORD
    return ctypes, user32, kernel32


def clipboard_sequence() -> int:
    """Windows' count of clipboard changes: a Copy raises it, so a wait reads the clipboard only after one."""
    if os.name != "nt":
        return 0
    return _clipboard_api()[1].GetClipboardSequenceNumber()


def take_clipboard_text() -> str:
    """The clipboard's text, "" when it holds none. A token taken from it is emptied out of it at once, so it stays
    in no clipboard once the script has it (Windows' clipboard history, Win+V, keeps its own copy if it is on)."""
    if os.name != "nt":
        return ""
    ctypes, user32, kernel32 = _clipboard_api()
    for _ in range(20):  # another program may hold the clipboard open for a moment
        if user32.OpenClipboard(None):
            break
        time.sleep(0.05)
    else:
        return ""
    try:
        handle = user32.GetClipboardData(CF_UNICODETEXT)
        if not handle:
            return ""
        pointer = kernel32.GlobalLock(handle)
        if not pointer:
            return ""
        try:
            text = ctypes.wstring_at(pointer)
        finally:
            kernel32.GlobalUnlock(handle)
        if TOKEN_SHAPE.fullmatch(clean(text)):
            user32.EmptyClipboard()
        return text
    finally:
        user32.CloseClipboard()


def read_hidden(prompt: str, watch: bool = False) -> tuple[str, bool]:
    """What is entered at `prompt`, never shown, and whether it came from the clipboard. On Windows a classic console
    window (cmd, PowerShell's own) hands Ctrl+V to a hidden prompt as a control character instead of the text, and
    right-click may paste nothing: so Ctrl+V takes the clipboard's text here, and so does Enter with nothing typed.
    With `watch`, a token copied while the prompt waits (Copy on Profile) is taken at once, with no key at all; a token
    already in the clipboard when the prompt starts is taken only by Enter, so a stale one isn't picked up unasked.
    Elsewhere getpass."""
    if os.name != "nt":
        return getpass.getpass(prompt), False
    import msvcrt
    sys.stdout.write(prompt)
    sys.stdout.flush()
    typed: list[str] = []
    from_clipboard = False
    seen = clipboard_sequence() if watch else 0
    while True:
        if watch and not typed and not msvcrt.kbhit():
            now = clipboard_sequence()
            if now != seen:
                seen = now
                text = take_clipboard_text()
                if TOKEN_SHAPE.fullmatch(clean(text)):
                    sys.stdout.write("\n")
                    sys.stdout.flush()
                    return text, True
            time.sleep(0.2)
            continue
        char = msvcrt.getwch()
        if char in "\r\n":
            break
        if char == "\x03":
            sys.stdout.write("\n")
            raise KeyboardInterrupt
        if char in "\x00\xe0":  # an arrow or function key: its second code follows
            msvcrt.getwch()
            continue
        if char == "\b":
            if typed:
                typed.pop()
            continue
        if char == "\x16":  # Ctrl+V, as a classic console window hands it over
            typed.extend(take_clipboard_text())
            from_clipboard = True
            continue
        typed.append(char)
    sys.stdout.write("\n")
    sys.stdout.flush()
    value = "".join(typed)
    if not value.strip():
        value, from_clipboard = take_clipboard_text(), True
    return value, from_clipboard


class Tokens:
    """The run's tokens, each asked for once (hidden input) and held in memory only."""

    def __init__(self, from_env: bool) -> None:
        self.from_env = from_env
        self._values: dict[str, str] = {}
        self._refused: set[str] = set()

    def get(self, kind: str) -> str:
        if kind not in self._values:
            value = self._from_env(kind) if self.from_env else self._ask(kind)
            self._values[kind] = value
            _SECRETS.append(value)
            _SECRETS.sort(key=len, reverse=True)
        return self._values[kind]

    def refused(self, value: str) -> None:
        """MAM answered `value` 401: revoked, or never known. Nothing is left to revoke."""
        self._refused.add(value)

    def still_active(self) -> list[tuple[str, str]]:
        """(kind, token) of each token of the run that MAM has not refused."""
        return [(kind, value) for kind, value in self._values.items() if value not in self._refused]

    def _taken_by(self, value: str) -> str | None:
        return next((TOKEN_KINDS[kind][0] for kind, held in self._values.items() if held == value), None)

    def _from_env(self, kind: str) -> str:
        label, variable, _ = TOKEN_KINDS[kind]
        value = clean(os.environ.get(variable, ""))
        if not value:
            raise ToolError(f"--tokens-from-env: {variable} is empty, so {label} is missing.")
        if (problem := shape_problem(value)) is not None:
            raise ToolError(f"--tokens-from-env: {variable} is not a token: {problem}.")
        if (other := self._taken_by(value)) is not None:
            raise ToolError(f"--tokens-from-env: {variable} holds the same token as {other}.")
        say(f"{label[0].upper() + label[1:]}: {shown(value)} (from {variable})")
        return value

    def _ask(self, kind: str) -> str:
        label, _, name = TOKEN_KINDS[kind]
        for attempt in range(5):
            try:
                raw, from_clipboard = read_hidden(
                    f"{label[0].upper() + label[1:]}: {TOKEN_CREATE[kind]} \"{name}\" with access {TOKEN_ACCESS[kind]} "
                    "and press Copy (Копіювати) in its shown-once step. The script takes it from the clipboard by "
                    "itself (or press Enter after Copy); nothing shows. Waiting... ", watch=True)
            except getpass.GetPassWarning:
                raise ToolError(CONSOLE_NEEDED) from None
            except EOFError:
                raw, from_clipboard = "", False
            value = clean(raw)
            if not value:
                say(f"The clipboard is empty: create \"{name}\" on Profile and press Copy (Копіювати) next to it."
                    if from_clipboard else "Nothing was entered.")
                continue
            if (problem := shape_problem(value)) is not None:
                # Refused here, never sent: a malformed value would only use up one of the address's sign-in attempts.
                where = "The clipboard holds no token" if from_clipboard else "That is not a token"
                say(f"{where}, so nothing was sent: {problem}. Press Copy (Копіювати) next to the token in Profile's "
                    "shown-once step, then Enter here.")
                continue
            if (other := self._taken_by(value)) is not None:
                say(f"That is the same token as {other}; create \"{name}\" and copy that one.")
                continue
            taken = " (taken from the clipboard, which is emptied now)" if from_clipboard else ""
            say(f"  {label[0].upper() + label[1:]}: {shown(value)}{taken} — compare it with the token's row on "
                "Profile.")
            return value
        raise ToolError(f"No usable value for {label} after five tries; nothing was sent with it.")


# ---------------------------------------------------------------- the record of a step

def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0)


class Record:
    """One step's verdicts, facts and requests: printed as they come, written to token-check-<step>.txt and .json."""

    def __init__(self, step: str, ctx: "Context") -> None:
        self.step = step
        self.started = utc_now()
        self.lines: list[str] = []
        self.requests: list[dict] = []
        self.facts: dict = {}
        self.counts = {"OK": 0, "CHECK": 0, "INFO": 0}
        self.stopped: str | None = None
        self.ctx = ctx
        say()
        say(f"== {step}: {STEP_TITLES[step]}")

    def note(self, verdict: str, text: str) -> None:
        self.counts[verdict] += 1
        line = f"{verdict:<6}{text}"
        self.lines.append(line)
        say("  " + line)

    def ok(self, text: str) -> None:
        self.note("OK", text)

    def check(self, text: str) -> None:
        self.note("CHECK", text)

    def info(self, text: str) -> None:
        self.note("INFO", text)

    def expect(self, condition: bool, good: str, bad: str | None = None) -> bool:
        if condition:
            self.ok(good)
        else:
            self.check(bad or f"not as expected: {good}")
        return bool(condition)

    def request(self, label: str, method: str, path: str, via: str, credential: str, language: str | None,
                sent_body, reply: Reply, keep: str | None = None) -> None:
        """One request and its answer. `keep` replaces the answer's body in the record (a summary of a long one)."""
        headers = {name: value for name, value in reply.headers.items()
                   if name.lower() in ("content-type", "www-authenticate", "retry-after", "mcp-session-id", "allow")}
        body = keep if keep is not None else reply.text
        self.requests.append({
            "label": label, "method": method, "path": path, "via": via, "credential": credential,
            "acceptLanguage": language, "sentBody": sent_body, "status": reply.status, "reason": reply.reason,
            "seconds": round(reply.seconds, 3), "headers": headers, "body": body,
        })

    def write(self) -> list[Path]:
        out_dir: Path = self.ctx.out_dir
        out_dir.mkdir(parents=True, exist_ok=True)
        stem = f"token-check-{self.step}"
        text_path, json_path = out_dir / f"{stem}.txt", out_dir / f"{stem}.json"
        # A step's earlier record is kept under the UTC time it was written, never overwritten.
        earlier = [path for path in (text_path, json_path) if path.exists()]
        if earlier:
            written = dt.datetime.fromtimestamp(max(path.stat().st_mtime for path in earlier), dt.timezone.utc)
            for path in earlier:
                path.replace(path.with_name(f"{stem}-{written:%Y%m%dT%H%M%SZ}{path.suffix}"))

        text = scrub(self._text())
        data = json.loads(scrub(json.dumps(self._data(), ensure_ascii=False)))
        json_text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
        if holds_a_token(text) or holds_a_token(json_text):
            raise ToolError(f"Refusing to write {stem}: the record would still hold a token.")
        text_path.write_text(text, encoding="utf-8", newline="\n")
        json_path.write_text(json_text, encoding="utf-8", newline="\n")
        return [text_path, json_path]

    def _text(self) -> str:
        out = [f"MAM-48 token check, step {self.step}: {STEP_TITLES[self.step]}",
               f"Run {self.started:%Y-%m-%d %H:%M:%S} UTC against {self.ctx.api.base} by {SCRIPT}", ""]
        out += self.lines
        if self.stopped:
            out += ["", f"STOPPED: {self.stopped}"]
        out += ["", "Requests"]
        for number, item in enumerate(self.requests, 1):
            language = f", Accept-Language: {item['acceptLanguage']}" if item["acceptLanguage"] else ""
            out.append(f"  {number}. {item['label']}: {item['method']} {item['path']} ({item['via']}, "
                       f"{item['credential']}{language}) -> {item['status']} {item['reason']} in {item['seconds']:.2f} s")
            for name, value in item["headers"].items():
                out.append(f"       {name}: {value}")
            body = item["body"] if isinstance(item["body"], str) else json.dumps(item["body"], ensure_ascii=False)
            body = body.strip()
            if body:
                body = body if len(body) <= 600 else body[:600] + f" … ({len(body)} characters)"
                out += ["       " + line for line in body.splitlines()]
        out += ["", f"At a glance: {self.counts['OK']} OK, {self.counts['CHECK']} CHECK, {self.counts['INFO']} INFO"
                + (" (stopped early)" if self.stopped else "")]
        return "\n".join(out) + "\n"

    def _data(self) -> dict:
        return {
            "step": self.step, "title": STEP_TITLES[self.step], "startedUtc": self.started.isoformat().replace("+00:00", "Z"),
            "baseUrl": self.ctx.api.base, "script": SCRIPT, "stopped": self.stopped,
            "counts": self.counts, "verdicts": self.lines, "facts": self.facts, "requests": self.requests,
        }


# ---------------------------------------------------------------- HTTP

class Api:
    """MAM's HTTP API at one address. http.client, not urllib: it ignores system proxy settings (a proxy would get the
    token), follows no redirect, and returns an error status like any other."""

    def __init__(self, base_url: str, timeout: float = 60.0) -> None:
        if "://" not in base_url:
            base_url = "http://" + base_url
        parts = urllib.parse.urlsplit(base_url)
        if parts.scheme not in ("http", "https") or not parts.hostname or parts.query or parts.fragment:
            raise ToolError(f"{base_url!r} is not an http(s) address of MAM, such as {DEFAULT_BASE_URL}.")
        self.base = base_url.rstrip("/")
        self.timeout = timeout
        self._parts = parts

    def url(self, path: str) -> str:
        return self.base + path

    def send(self, method: str, path: str, token: str | None = None, body=None, headers: dict | None = None,
             language: str | None = None) -> Reply:
        sent = {"User-Agent": "MAM-48-check", "Connection": "close"}
        if token:
            sent["Authorization"] = "Bearer " + token
        if language:
            sent["Accept-Language"] = language
        sent.update(headers or {})
        payload = None
        if body is not None:
            payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
            sent.setdefault("Content-Type", "application/json")
        target = self._parts.path.rstrip("/") + path
        connection_type = http.client.HTTPSConnection if self._parts.scheme == "https" else http.client.HTTPConnection
        connection = connection_type(self._parts.hostname, self._parts.port, timeout=self.timeout)
        started = time.perf_counter()
        error = None
        try:
            connection.request(method, target, body=payload, headers=sent)
            response = connection.getresponse()
            raw = response.read()
            reply = Reply(response.status, time.perf_counter() - started, raw.decode("utf-8", errors="replace"),
                          dict(response.getheaders()), response.reason or "",
                          "HTTP/1.1" if response.version == 11 else "HTTP/1.0")
            reply.raw = raw
        except TimeoutError:
            error = f"{method} {self.base}{path} got no answer in {self.timeout:g} s."
        except (OSError, http.client.HTTPException, ValueError) as failure:
            error = f"{method} {self.base}{path} failed: {type(failure).__name__}: {failure}"
        finally:
            connection.close()
        if error is not None:
            # Raised outside the except blocks and without a cause: a ValueError from http.client quotes the header.
            raise ToolError(scrub(error) + " Is MAM running at that address, and is this PC on its network?") from None
        return reply


def curl_line_shown(base: str, token: str) -> str:
    """The jobs line as Profile's shown-once step prints it, with the token as Profile's example shows it (its prefix)."""
    return f'curl -H "Authorization: Bearer {shown(token)}" {base}/api/downloads'


def child_environment() -> dict[str, str]:
    """This process's environment without any MAM48_ variable, for curl.exe and git: --tokens-from-env's tokens stay
    in this process."""
    return {name: value for name, value in os.environ.items() if not name.upper().startswith("MAM48_")}


def curl_jobs(ctx: "Context", rec: Record, label: str, token: str | None, credential: str) -> Reply:
    """GET /api/downloads through curl.exe (run_curl), in the record."""
    reply = run_curl(ctx, token)
    record_curl(rec, label, token, credential, reply)
    return reply


def record_curl(rec: Record, label: str, token: str | None, credential: str, reply: Reply) -> None:
    rec.request(label, "GET", "/api/downloads", "curl.exe" + (", -H @-" if token is not None else ""), credential, None,
                None, reply, keep=summarise_body(reply))


def run_curl(ctx: "Context", token: str | None) -> Reply:
    """GET /api/downloads through curl.exe, the token's header read from stdin (`-H @-`). Added to the printed line:
    -q, first as curl requires, so no .curlrc/_curlrc is read (one with -v, --trace or --stderr would write the header
    out); and for the record alone -sS (no progress meter, errors still shown), -i (the status line and headers),
    --noproxy * (straight to MAM, as a proxy in the environment would otherwise get the token) and --max-time 60."""
    args = [ctx.curl, "-q", "-sS", "-i", "--noproxy", "*", "--max-time", "60"]
    if token is not None:
        args += ["-H", "@-"]
    args.append(ctx.api.url("/api/downloads"))
    started = time.perf_counter()
    try:
        if token is not None:
            done = subprocess.run(args, input=f"Authorization: Bearer {token}\n".encode("ascii"),
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=90, env=child_environment())
        else:
            done = subprocess.run(args, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                  timeout=90, env=child_environment())
    except subprocess.TimeoutExpired:
        raise ToolError(f"curl.exe got no answer from {ctx.api.base} in 90 s.") from None
    except OSError as failure:
        raise ToolError(f"Could not start {ctx.curl}: {failure}") from None
    seconds = time.perf_counter() - started
    if done.returncode != 0:
        message = done.stderr.decode("utf-8", errors="replace").strip()
        raise ToolError(scrub(f"curl.exe ended with exit code {done.returncode}: {message or '(no message)'}"))
    return parse_curl_output(done.stdout, seconds)


def parse_curl_output(raw: bytes, seconds: float) -> Reply:
    """`curl -i`'s output: status lines and headers, a blank line, the body (any 1xx answer before the final one is
    skipped)."""
    rest = raw
    while True:
        head, separator, body = rest.partition(b"\r\n\r\n")
        if not separator:
            head, separator, body = rest.partition(b"\n\n")
        lines = head.decode("iso-8859-1").splitlines()
        if not lines or not lines[0].startswith("HTTP/"):
            raise ToolError("curl.exe answered something that isn't an HTTP answer: " + scrub(raw[:200].decode("utf-8", "replace")))
        status_parts = lines[0].split(" ", 2)
        status = int(status_parts[1])
        if 100 <= status < 200 and body.startswith(b"HTTP/"):
            rest = body
            continue
        headers = {}
        for line in lines[1:]:
            name, _, value = line.partition(":")
            if name:
                headers[name.strip()] = value.strip()
        reply = Reply(status, seconds, body.decode("utf-8", errors="replace"), headers,
                      status_parts[2] if len(status_parts) > 2 else "", status_parts[0])
        reply.raw = body
        return reply


def summarise_body(reply: Reply) -> str | None:
    """A job list's body is summed up (its titles stay out of the record); any other body is kept as it is."""
    data = reply.json
    if reply.status == 200 and isinstance(data, dict) and "items" in data:
        return f"(a job list of {len(data.get('items') or [])} items; summed up in the verdicts)"
    return None


def guard(rec: Record, reply: Reply, who: str, token: str | None, expect_401: bool = False) -> None:
    """Stops the run on an answer that makes going on pointless or costly."""
    if reply.status == 401 and token is not None:
        _FAILED_AUTHENTICATIONS[0] += 1
        rec.ctx.tokens.refused(token)
        if not expect_401:
            reason = (f"{who} {shown(token)} was answered 401 ({reply.text.strip()}): MAM doesn't know it, or it was "
                      "revoked or has expired. Compare its first characters with the rows on Profile → Access tokens; "
                      "a token is shown only once, so create a new one if it is lost. The run stops here, as every "
                      "further try would use up one more of this address's 10 sign-in attempts a minute.")
            rec.check(reason)
            rec.stopped = reason
            raise StopRun(reason)
    if reply.status == 429:
        reason = (f"429 ({reply.text.strip()}, Retry-After {reply.header('Retry-After') or '?'}): this address used up "
                  "its sign-in attempts, or the token its 60 MCP requests, of the minute. Wait a minute and run the "
                  "step again.")
        rec.check(reason)
        rec.stopped = reason
        raise StopRun(reason)


def is_json(reply: Reply) -> bool:
    return (reply.header("Content-Type") or "").lower().startswith("application/json")


# ---------------------------------------------------------------- MCP (Streamable HTTP, as Claude Code speaks it)

def mcp_messages(reply: Reply) -> list[dict]:
    """The JSON-RPC messages of an answer to POST /mcp: a JSON body, or the `data:` of each event of an event stream."""
    text = reply.text.lstrip("\ufeff")
    if not text.strip():
        return []
    found: list = []
    if "text/event-stream" in (reply.header("Content-Type") or "").lower():
        for event in re.split(r"\r?\n\r?\n", text):
            data = [line[5:][1:] if line[5:].startswith(" ") else line[5:]
                    for line in event.splitlines() if line.startswith("data:")]
            if data:
                try:
                    found.append(json.loads("\n".join(data)))
                except ValueError:
                    pass
    else:
        try:
            found.append(json.loads(text))
        except ValueError:
            return []
    messages: list[dict] = []
    for item in found:
        messages.extend(item if isinstance(item, list) else [item])
    return [message for message in messages if isinstance(message, dict)]


class McpSession:
    """One assistant's conversation with /mcp: each message a POST of its own, with the headers Claude Code sends —
    Accept: application/json, text/event-stream; Content-Type: application/json; the token; MCP-Protocol-Version once
    initialize has answered, and Mcp-Session-Id if the server gave one (stateless, it gives none)."""

    def __init__(self, ctx: "Context", rec: Record, token: str, who: str) -> None:
        self.ctx, self.rec, self.token, self.who = ctx, rec, token, who
        self.protocol: str | None = None
        self.session_id: str | None = None
        self.last_reply: Reply | None = None
        self._next_id = 1

    def post(self, message: dict, label: str, language: str | None = None, keep=None) -> tuple[Reply, list[dict]]:
        headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        if self.protocol:
            headers["MCP-Protocol-Version"] = self.protocol
        if self.session_id:
            headers["Mcp-Session-Id"] = self.session_id
        reply = self.ctx.api.send("POST", "/mcp", self.token, message, headers, language)
        self.last_reply = reply
        messages = mcp_messages(reply)
        self.rec.request(label, "POST", "/mcp", "MCP", f"{self.who} {shown(self.token)}", language, message, reply,
                         keep=keep(messages) if keep and reply.status == 200 else None)
        guard(self.rec, reply, self.who, self.token)
        return reply, messages

    def call(self, method: str, params: dict, label: str, language: str | None = None, keep=None) -> dict | None:
        """A request's result; None (with a CHECK line) when the answer is no 200 or holds no result."""
        request_id = self._next_id
        self._next_id += 1
        reply, messages = self.post({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params}, label,
                                    language, keep)
        if reply.status != 200:
            self.rec.check(f"{label}: POST /mcp answered {reply.status} {reply.reason}, not 200: {reply.text.strip()[:300]}")
            return None
        answer = next((message for message in messages if message.get("id") == request_id), None)
        if answer is None:
            self.rec.check(f"{label}: the answer ({reply.header('Content-Type')}) holds no JSON-RPC response with id "
                           f"{request_id}: {reply.text.strip()[:300]}")
            return None
        if "error" in answer:
            self.rec.check(f"{label}: JSON-RPC error {json.dumps(answer['error'], ensure_ascii=False)[:300]}")
            return None
        result = answer.get("result")
        return result if isinstance(result, dict) else None

    def initialize(self) -> dict | None:
        result = self.call("initialize", {"protocolVersion": PROTOCOL_VERSION, "capabilities": {},
                                          "clientInfo": {"name": "mam48-check", "version": "1.0.0"}},
                           f"initialize ({self.who})", keep=lambda messages: _keep_initialize(messages))
        if result is not None:
            self.protocol = str(result.get("protocolVersion") or PROTOCOL_VERSION)
            self.session_id = self.last_reply.header("Mcp-Session-Id") if self.last_reply else None
        return result

    def initialized(self) -> Reply:
        reply, _ = self.post({"jsonrpc": "2.0", "method": "notifications/initialized"},
                             f"notifications/initialized ({self.who})")
        return reply

    def tool(self, name: str, arguments: dict, label: str, language: str | None = None, keep=None) -> dict | None:
        return self.call("tools/call", {"name": name, "arguments": arguments}, label, language, keep or _keep_tool_result)


def _keep_initialize(messages: list[dict]):
    # The instructions say what this token may do; kept whole, they are short.
    return next((message.get("result") for message in messages if "result" in message), None)


def _keep_tool_list(messages: list[dict]):
    result = next((message.get("result") for message in messages if "result" in message), None) or {}
    return [{"name": tool.get("name"), "title": tool.get("title"), "annotations": tool.get("annotations"),
             "_meta": tool.get("_meta")} for tool in result.get("tools") or [] if isinstance(tool, dict)]


def _keep_tool_result(messages: list[dict]):
    result = next((message.get("result") for message in messages if "result" in message), None) or {}
    return {"isError": result.get("isError"),
            "text": [block.get("text") for block in result.get("content") or [] if isinstance(block, dict)],
            "hasStructuredContent": result.get("structuredContent") is not None}


def _keep_status(messages: list[dict]):
    result = next((message.get("result") for message in messages if "result" in message), None) or {}
    structured = result.get("structuredContent") or {}
    return {"isError": result.get("isError"), "summary": first_text(result),
            "caller": structured.get("caller"), "version": structured.get("version")}


def first_text(result: dict | None) -> str | None:
    for block in (result or {}).get("content") or []:
        if isinstance(block, dict) and block.get("type") == "text":
            return block.get("text")
    return None


# ---------------------------------------------------------------- context

class Context:
    def __init__(self, args: argparse.Namespace) -> None:
        self.api = Api(args.base_url)
        self.curl = args.curl
        self.out_dir = Path(args.out_dir)
        self.expected_version = args.expect_version
        self.tokens = Tokens(args.tokens_from_env)
        self.guided = args.step is None
        # The guided run polls unless --no-poll; a step alone waits for Enter unless --poll.
        self.poll = self.guided if args.poll is None else args.poll
        self._locales: dict[str, dict] = {}

    def sentence(self, language: str, key: str) -> str:
        """The server's sentence for `key` (its code, such as account.token.read-only) in `language`, from this
        checkout's locale files: the words 0.5.0 answers with."""
        area, *path = key.split(".")
        cache_key = f"{language}/{area}"
        if cache_key not in self._locales:
            file = LOCALES_DIR / language / f"{area}.json"
            try:
                self._locales[cache_key] = json.loads(file.read_text(encoding="utf-8"))
            except (OSError, ValueError) as failure:
                raise ToolError(f"Could not read the expected sentences from {file}: {failure}") from None
        node = self._locales[cache_key]
        for part in path:
            node = node.get(part) if isinstance(node, dict) else None
        if not isinstance(node, str):
            raise ToolError(f"{LOCALES_DIR / language / (area + '.json')} has no sentence {key}.")
        return node


def wait_for_enter(instruction: str) -> None:
    say()
    say("ACTION: " + instruction)
    try:
        input("Press Enter when it is done (Ctrl+C stops the check): ")
    except EOFError:
        raise ToolError("There is no console to wait on; run the step in a PowerShell window.") from None


def wait_for_revoke(ctx: Context, rec: Record, instruction: str, token: str, credential: str, who: str,
                    label: str, last_ok_at: float) -> tuple[Reply, str]:
    """Waits while the owner revokes, then returns the first request after the revoke and when it came.

    --poll: the jobs line every 3 s with the token until its first answer other than 200. A request MAM lets in costs
    no sign-in attempt; only that final 401 takes one. The record keeps the last 200 and that answer, not every 200.
    After 10 minutes without one the step stops with a CHECK. Otherwise: Enter, then one request."""
    if not ctx.poll:
        wait_for_enter(instruction)
        entered = time.perf_counter()
        after = curl_jobs(ctx, rec, label, token, credential)
        return after, f"{time.perf_counter() - entered:.1f} s after Enter"

    say()
    say("ACTION: " + instruction)
    say(f"  The check goes on by itself: it reads the jobs with this token every {POLL_SECONDS} s until MAM refuses it, "
        f"for at most {POLL_LIMIT_SECONDS // 60} minutes (Ctrl+C stops the check).")
    started = time.perf_counter()
    polls, last_ok, minutes_said = 0, None, 0
    while True:
        asked_at = time.perf_counter()
        reply = run_curl(ctx, token)
        polls += 1
        if reply.status != 200:
            break
        last_ok, last_ok_at = reply, asked_at
        waited = time.perf_counter() - started
        if waited + POLL_SECONDS > POLL_LIMIT_SECONDS:
            record_curl(rec, f"the last request of the wait ({polls} requests, each 200)", token, credential, reply)
            reason = (f"{who} {shown(token)} was still let in after {waited / 60:.1f} minutes ({polls} requests, one "
                      f"every {POLL_SECONDS} s, each 200): was Revoke pressed and confirmed on its row? Run the step "
                      "again (with --no-poll to wait for Enter instead).")
            rec.check(reason)
            rec.stopped = reason
            raise StopRun(reason)
        if int(waited // 60) > minutes_said:
            minutes_said = int(waited // 60)
            say(f"  still let in after {minutes_said} min ({polls} requests); waiting for the revoke")
        time.sleep(max(0.0, POLL_SECONDS - (time.perf_counter() - asked_at)))
    if last_ok is not None:
        record_curl(rec, f"the last request of the wait still let in (request {polls - 1})", token, credential, last_ok)
    record_curl(rec, f"{label} (request {polls} of the wait)", token, credential, reply)
    rec.info(f"waited by polling: {polls} requests, one every {POLL_SECONDS} s, over "
             f"{time.perf_counter() - started:.0f} s; every one but the last was let in, which costs no sign-in attempt")
    rec.facts["wait"] = {"mode": "poll", "requests": polls, "seconds": round(time.perf_counter() - started, 1)}
    return reply, f"{asked_at - last_ok_at:.1f} s after the token's last 200 (request {polls} of the wait)"


def expect_error_body(rec: Record, reply: Reply, code: str, sentence: str, what: str) -> None:
    body = reply.json
    rec.expect(isinstance(body, dict) and body.get("code") == code and body.get("error") == sentence,
               f"{what}: {{ \"error\": \"{sentence}\", \"code\": \"{code}\" }}",
               f"{what}: expected {{ error: \"{sentence}\", code: {code} }}, got {reply.text.strip()[:400]}")
    if isinstance(body, dict) and set(body) - {"error", "code"}:
        rec.info(f"the body also carries {sorted(set(body) - {'error', 'code'})}")


# ---------------------------------------------------------------- the steps

def step_read(ctx: Context, rec: Record) -> None:
    token = ctx.tokens.get("read-only")
    rec.info("Profile's shown-once step and README \"Scripts and the API\" print: " + curl_line_shown(ctx.api.base, token))
    rec.info(f"run as: {ctx.curl} -q -sS -i --noproxy * --max-time 60 -H @- {ctx.api.base}/api/downloads, with the "
             f"line \"Authorization: Bearer {shown(token)}\" on stdin, so the token is on no command line, and no curl "
             "config file read (-q)")
    reply = curl_jobs(ctx, rec, "the jobs with the Read only token", token, f"Read only token {shown(token)}")
    guard(rec, reply, "The Read only token", token)
    if not rec.expect(reply.status == 200, "curl.exe sent the header it read from stdin (-H @-) and MAM answered 200",
                      f"MAM answered {reply.status} {reply.reason}, not 200: {reply.text.strip()[:300]}"):
        return
    body = reply.json
    if not rec.expect(isinstance(body, dict) and all(key in body for key in JOB_LIST_KEYS),
                      "the answer is { items, totalCount, page, pageSize, needsYouCount, inProgressCount }",
                      f"the answer is not the job list: {reply.text.strip()[:300]}"):
        return
    rec.expect(is_json(reply), f"Content-Type {reply.header('Content-Type')}")
    items = [item for item in body.get("items") or [] if isinstance(item, dict)]
    statuses: dict[str, int] = {}
    for item in items:
        statuses[str(item.get("status"))] = statuses.get(str(item.get("status")), 0) + 1
    created = [str(item.get("createdUtc") or "") for item in items]
    rec.expect(created == sorted(created, reverse=True), "the page lists the newest job first",
               f"the page is not newest first: {created[:5]}")
    rec.info(f"{body['totalCount']} jobs; Needs you {body['needsYouCount']}, In progress {body['inProgressCount']}; "
             f"page {body['page']} (page size {body['pageSize']}) lists {len(items)}"
             + (": " + ", ".join(f"{status} {count}" for status, count in sorted(statuses.items())) if statuses else ""))
    newest = items[0] if items else None
    if newest:
        rec.info(f"newest job: {newest.get('status')}, sent {newest.get('createdUtc')}, "
                 f"\"{str(newest.get('releaseTitle'))[:100]}\" to {newest.get('downloadClientName')} / "
                 f"{newest.get('destinationName')}")
    rec.facts["jobs"] = {key: body[key] for key in JOB_LIST_KEYS if key != "items"}
    rec.facts["jobs"]["itemsOnPage"] = len(items)
    rec.facts["jobs"]["statusesOnPage"] = statuses
    rec.facts["jobs"]["newest"] = None if newest is None else {
        key: newest.get(key) for key in ("id", "status", "createdUtc", "releaseTitle", "downloadClientName",
                                         "destinationName")}


def step_refused_send(ctx: Context, rec: Record) -> None:
    token = ctx.tokens.get("read-only")
    rec.info(f"body: {json.dumps(HARMLESS_SEND)} — every id the all-zero GUID, which no row has and the send's own rules "
             "refuse before reading anything, so even a Read and download token would send nothing (README)")
    for language in (None, "uk"):
        lang = language or "en"
        how = "with Accept-Language: uk" if language else "without Accept-Language"
        reply = ctx.api.send("POST", "/api/downloads", token, HARMLESS_SEND, language=language)
        rec.request(f"a send {how}", "POST", "/api/downloads", "http.client", f"Read only token {shown(token)}",
                    language, HARMLESS_SEND, reply)
        guard(rec, reply, "The Read only token", token)
        if reply.status in (400, 404, 409):
            rec.check(f"POST /api/downloads {how} answered {reply.status}, not 403: the server read the body, so this "
                      f"token may send — it is not the Read only one. Nothing was sent (the ids are all zero): "
                      f"{reply.text.strip()[:300]}")
            continue
        rec.expect(reply.status == 403, f"POST /api/downloads {how} answered 403",
                   f"POST /api/downloads {how} answered {reply.status} {reply.reason}, not 403: {reply.text.strip()[:300]}")
        expect_error_body(rec, reply, "account.token.read-only", ctx.sentence(lang, "account.token.read-only"),
                          "the read-only sentence in " + ("Ukrainian" if language else "English"))
        rec.expect(is_json(reply), f"Content-Type {reply.header('Content-Type')}")


def check_initialize(ctx: Context, rec: Record, result: dict, who: str, session_id: str | None) -> None:
    info = result.get("serverInfo") or {}
    rec.expect(result.get("protocolVersion") == PROTOCOL_VERSION,
               f"{who}: initialize answered protocol version {PROTOCOL_VERSION}, the one Claude Code asks for",
               f"{who}: initialize answered protocol version {result.get('protocolVersion')!r}, not {PROTOCOL_VERSION}")
    rec.expect(info.get("name") == "mam", f"{who}: serverInfo.name \"mam\" (title \"{info.get('title')}\")",
               f"{who}: serverInfo.name is {info.get('name')!r}, not \"mam\"")
    version = str(info.get("version") or "")
    # The image stamps <release>+<short commit> (Dockerfile APP_REVISION); a local `dotnet build` of a git checkout
    # appends .<full commit> to it, which is accepted too.
    match = re.fullmatch(re.escape(ctx.expected_version) + r"\+([0-9a-f]{7,40})(?:\.[0-9a-f]{40})?", version)
    rec.expect(match is not None, f"{who}: serverInfo.version {version} ({ctx.expected_version} and its commit)",
               f"{who}: serverInfo.version is {version!r}, not {ctx.expected_version}+<commit>")
    if match:
        rec.facts.setdefault("serverVersion", version)
        head = git_head()
        if head is None:
            rec.info("this checkout's commit could not be read (git), so the image's commit is not compared")
        elif head.startswith(match.group(1)):
            rec.info(f"the commit {match.group(1)} is this checkout's HEAD")
        else:
            rec.info(f"the commit {match.group(1)} is not this checkout's HEAD ({head[:7]})")
    rec.expect("tools" in (result.get("capabilities") or {}), f"{who}: the server offers tools")
    instructions = str(result.get("instructions") or "")
    rec.info(f"{who}: instructions of {len(instructions)} characters: \"{instructions[:160]}…\"")
    rec.info(f"{who}: " + ("no Mcp-Session-Id: stateless, as ADR 0041 says" if not session_id
                            else f"the server gave an Mcp-Session-Id ({session_id[:12]}…)"))


def git_head() -> str | None:
    git = shutil.which("git")
    if git is None:
        return None
    try:
        done = subprocess.run([git, "-C", str(REPO_ROOT), "rev-parse", "HEAD"], stdout=subprocess.PIPE,
                              stderr=subprocess.DEVNULL, timeout=20, env=child_environment())
    except (OSError, subprocess.TimeoutExpired):
        return None
    head = done.stdout.decode("ascii", errors="replace").strip()
    return head if done.returncode == 0 and re.fullmatch(r"[0-9a-f]{40}", head) else None


def mcp_open(ctx: Context, rec: Record, token: str, who: str) -> McpSession | None:
    session = McpSession(ctx, rec, token, who)
    result = session.initialize()
    if result is None:
        return None
    check_initialize(ctx, rec, result, who, session.session_id)
    reply = session.initialized()
    rec.expect(reply.status == 202, f"{who}: notifications/initialized answered 202 Accepted",
               f"{who}: notifications/initialized answered {reply.status} {reply.reason}, not 202")
    return session


def check_status(rec: Record, session: McpSession, who: str, owner: bool | None, access: str | None,
                 kind: str) -> dict | None:
    result = session.tool("get_status", {}, f"get_status ({who})", keep=_keep_status)
    if result is None:
        return None
    caller = (result.get("structuredContent") or {}).get("caller") or {}
    if not rec.expect(result.get("isError") is not True and bool(caller), f"{who}: get_status answered",
                      f"{who}: get_status answered an error: {first_text(result)}"):
        return None
    rec.info(f"{who}: get_status says: {first_text(result)}")
    if access is not None:
        rec.expect(caller.get("tokenAccess") == access, f"{who}: the token's access is {access}",
                   f"{who}: the token's access is {caller.get('tokenAccess')!r}, not {access} — another token was pasted")
    if owner is not None:
        rec.expect(caller.get("isInstallationOwner") is owner,
                   f"{who}: it acts as {caller.get('userName')}, " + ("the owner" if owner else "a member"),
                   f"{who}: it acts as {caller.get('userName')}, " + ("a member, not the owner" if owner
                                                                     else "the owner, not a member"))
    # README's plan: the script's tokens are its own, as it revokes them; a Claude client's token revoked while the
    # client holds it would use up this PC's sign-in attempts.
    name = TOKEN_KINDS[kind][2]
    user = MEMBER_USER if kind == "member" else caller.get("userName")
    rec.expect(caller.get("tokenName") == name and caller.get("userName") == user,
               f"{who}: the token is \"{name}\" of {user}, the script's own (README \"Before you run\")",
               f"{who}: the token is \"{caller.get('tokenName')}\" of {caller.get('userName')}, not \"{name}\" of {user}: "
               "the script needs tokens of its own, never one a Claude client holds (README \"Before you run\")")
    rec.info(f"{who}: expires {caller.get('tokenExpiresUtc') or 'never'}")
    version = (result.get("structuredContent") or {}).get("version")
    if rec.facts.get("serverVersion"):
        rec.expect(version == rec.facts["serverVersion"], f"{who}: get_status reports the same version {version}",
                   f"{who}: get_status reports version {version!r}, initialize {rec.facts['serverVersion']!r}")
    return caller


def step_mcp_tools(ctx: Context, rec: Record) -> None:
    for kind, who, access, expected in (("read-only", "Read only token", "ReadOnly", READ_TOOLS),
                                        ("read-and-download", "Read and download token", "ReadAndDownload",
                                         READ_TOOLS + WRITE_TOOLS)):
        token = ctx.tokens.get(kind)
        session = mcp_open(ctx, rec, token, who)
        if session is None:
            continue
        result = session.call("tools/list", {}, f"tools/list ({who})", keep=_keep_tool_list)
        if result is None:
            continue
        tools = [tool for tool in result.get("tools") or [] if isinstance(tool, dict)]
        names = [str(tool.get("name")) for tool in tools]
        missing = [name for name in expected if name not in names]
        extra = [name for name in names if name not in expected]
        rec.expect(not missing and not extra and len(names) == len(expected),
                   f"{who}: tools/list offers exactly the {len(expected)} tools: {', '.join(expected)}",
                   f"{who}: tools/list offers {len(names)} tools; missing {missing or 'none'}, unexpected {extra or 'none'}")
        if kind == "read-only":
            rec.expect(not any(name in names for name in WRITE_TOOLS),
                       f"{who}: no {', '.join(WRITE_TOOLS)} — the assistant has no tool that sends",
                       f"{who}: offers the writes {[name for name in WRITE_TOOLS if name in names]}")
        wrong_hint = [str(tool.get("name")) for tool in tools
                      if ((tool.get("annotations") or {}).get("readOnlyHint") is True) != (tool.get("name") in READ_TOOLS)]
        rec.expect(not wrong_hint, f"{who}: every read is marked readOnlyHint true and every write false",
                   f"{who}: readOnlyHint is wrong on {wrong_hint}")
        if kind == "read-and-download":
            send = next((tool for tool in tools if tool.get("name") == "send_download"), {})
            rec.expect((send.get("_meta") or {}).get(REQUIRES_USER_INTERACTION) is True,
                       f"{who}: send_download carries _meta {REQUIRES_USER_INTERACTION}: true (Claude Code asks first)",
                       f"{who}: send_download's _meta is {send.get('_meta')!r}")
        rec.facts.setdefault("tools", {})[kind] = names
        check_status(rec, session, who, owner=True, access=access, kind=kind)


def step_mcp_write(ctx: Context, rec: Record) -> None:
    token = ctx.tokens.get("read-only")
    who = "Read only token"
    session = mcp_open(ctx, rec, token, who)
    if session is None:
        return
    arguments = {"releaseId": ZERO_ID, "downloadClientId": ZERO_ID, "destinationId": ZERO_ID, "requestId": ZERO_ID}
    rec.info(f"arguments: {json.dumps(arguments)} — all-zero ids, so even a Read and download token would send nothing")
    for language in (None, "uk"):
        how = "with Accept-Language: uk" if language else "without Accept-Language"
        result = session.tool("send_download", arguments, f"send_download {how}", language)
        if result is None:
            continue
        text = first_text(result) or ""
        expected = ctx.sentence(language or "en", "account.token.read-only")
        rec.expect(result.get("isError") is True, f"send_download {how}: an isError result",
                   f"send_download {how}: isError is {result.get('isError')!r}: {text[:300]}")
        if text == expected:
            rec.ok(f"send_download {how}: the read-only sentence in {'Ukrainian' if language else 'English'}: \"{text}\"")
        else:
            rec.check(f"send_download {how}: \"{text[:300]}\", not the read-only sentence \"{expected}\""
                      + (" — the tool ran, so this token may send: it is not the Read only one. Nothing was sent (the ids "
                         "are all zero)." if text and "read only" not in text and "лише для читання" not in text else ""))
        rec.expect("structuredContent" not in result or result.get("structuredContent") is None,
                   f"send_download {how}: no structuredContent, as an error carries none")


def step_revoked(ctx: Context, rec: Record) -> None:
    token = ctx.tokens.get("read-only")
    credential = f"Read only token {shown(token)}"
    asked_at = time.perf_counter()
    before = curl_jobs(ctx, rec, "before the revoke", token, credential)
    guard(rec, before, "The Read only token", token, expect_401=True)
    if before.status == 401:
        rec.check("the Read only token already answered 401 before the revoke (revoked earlier, or another token was "
                  "pasted): the 401 below is that answer, and that it came at once is not shown by this run")
        after = before
    else:
        rec.expect(before.status == 200, "the Read only token works right before the revoke: 200",
                   f"before the revoke MAM answered {before.status} {before.reason}: {before.text.strip()[:300]}")
        after, when = wait_for_revoke(
            ctx, rec, f"Revoke the Read only token \"{TOKEN_KINDS['read-only'][2]}\" ({shown(token)}) now: Profile "
                      "(Профіль) → Access tokens (Токени доступу) → Revoke (Відкликати) on its row, and confirm.",
            token, credential, "The Read only token", "the first request after the revoke", asked_at)
        guard(rec, after, "The Read only token", token, expect_401=True)
        rec.expect(after.status == 401,
                   f"the first request after the revoke answered 401, {when}",
                   f"the first request after the revoke answered {after.status} {after.reason}: was Revoke pressed and "
                   "confirmed on this token's row?")
    anonymous = curl_jobs(ctx, rec, "the same line without a token", None, "no token")
    rec.expect(anonymous.status == 401, "the same line without a token answered 401")
    compare_401s(ctx, rec, after, anonymous, "the revoked token's")
    # The same token at /mcp: one more failed attempt, and the proof that no MCP connection outlives a revoke either.
    message = {"jsonrpc": "2.0", "id": 1, "method": "initialize",
               "params": {"protocolVersion": PROTOCOL_VERSION, "capabilities": {},
                          "clientInfo": {"name": "mam48-check", "version": "1.0.0"}}}
    mcp = ctx.api.send("POST", "/mcp", token, message, {"Accept": "application/json, text/event-stream"})
    rec.request("initialize with the revoked token", "POST", "/mcp", "MCP", credential, None, message, mcp)
    guard(rec, mcp, "The Read only token", token, expect_401=True)
    rec.expect(mcp.status == 401 and getattr(mcp, "raw", b"") == getattr(anonymous, "raw", None),
               "POST /mcp with the revoked token answered the same 401 body",
               f"POST /mcp with the revoked token answered {mcp.status}: {mcp.text.strip()[:300]}")


def compare_401s(ctx: Context, rec: Record, refused: Reply, anonymous: Reply, whose: str) -> None:
    same = getattr(refused, "raw", None) == getattr(anonymous, "raw", None)
    rec.expect(refused.status == 401 and same,
               f"{whose} 401 body is byte for byte the body without a token: {anonymous.text.strip()}",
               f"{whose} answer ({refused.status}: {refused.text.strip()[:200]}) differs from the one without a token "
               f"({anonymous.status}: {anonymous.text.strip()[:200]})")
    rec.expect(refused.header("WWW-Authenticate") == anonymous.header("WWW-Authenticate") == "Bearer",
               "both carry WWW-Authenticate: Bearer",
               f"WWW-Authenticate: {refused.header('WWW-Authenticate')!r} with the token, "
               f"{anonymous.header('WWW-Authenticate')!r} without")
    expect_error_body(rec, anonymous, "account.sign-in-required", ctx.sentence("en", "account.sign-in-required"),
                      "the 401 without Accept-Language is English")


def step_member(ctx: Context, rec: Record) -> None:
    token = ctx.tokens.get("member")
    credential = f"member's token {shown(token)}"
    reply = curl_jobs(ctx, rec, "the jobs with the member's token", token, credential)
    guard(rec, reply, "The member's token", token)
    rec.expect(reply.status == 200, "the member's token reads the member's jobs: 200",
               f"MAM answered {reply.status} {reply.reason}, not 200: {reply.text.strip()[:300]}")
    body = reply.json if isinstance(reply.json, dict) else {}
    if body:
        rec.info(f"the member has {body.get('totalCount')} jobs (Needs you {body.get('needsYouCount')}, "
                 f"In progress {body.get('inProgressCount')})")
        rec.facts["memberJobs"] = {key: body.get(key) for key in JOB_LIST_KEYS if key != "items"}
    caller = None
    session = mcp_open(ctx, rec, token, "member's token")
    if session is not None:
        caller = check_status(rec, session, "member's token", owner=False, access=None, kind="member")
    last_ok_at = time.perf_counter()
    name = (caller or {}).get("userName") or MEMBER_USER
    after, when = wait_for_revoke(
        ctx, rec, f"As the owner, open Users (Користувачі): {name}'s row counts its active tokens. Press Revoke tokens "
                  "(Відкликати токени) on that row and confirm; the row then reads 0 tokens (0 токенів).",
        token, credential, "The member's token", "the first request after Revoke tokens", last_ok_at)
    guard(rec, after, "The member's token", token, expect_401=True)
    rec.expect(after.status == 401,
               f"the first request after Revoke tokens answered 401, {when}",
               f"after Revoke tokens MAM answered {after.status} {after.reason}: was it pressed on {name}'s row?")
    anonymous = curl_jobs(ctx, rec, "the same line without a token", None, "no token")
    compare_401s(ctx, rec, after, anonymous, "the member's revoked token's")


STEP_FUNCTIONS = {
    "read": step_read,
    "refused-send": step_refused_send,
    "mcp-tools": step_mcp_tools,
    "mcp-write": step_mcp_write,
    "revoked": step_revoked,
    "member": step_member,
}


def run_step(ctx: Context, step: str, results: list[tuple[str, dict, list[Path]]]) -> None:
    rec = Record(step, ctx)
    try:
        STEP_FUNCTIONS[step](ctx, rec)
    except StopRun:
        raise
    except ToolError as error:
        rec.stopped = str(error)
        rec.check("stopped: " + str(error))
        raise
    except Exception as failure:
        # A bug in the script: the record says where; the console gets one scrubbed line (the entry point below).
        rec.stopped = scrub(f"a bug: {type(failure).__name__}: {failure}")
        rec.check("stopped by " + rec.stopped)
        raise
    finally:
        paths = rec.write()
        results.append((step, rec.counts, paths))
        say(f"  -> {paths[0]}")


# ---------------------------------------------------------------- main

def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="MAM-48: check Phase E's personal access tokens and MCP endpoint on the live installation, with "
                    "tokens copied from Profile's shown-once step. No step given: the guided run of every step.")
    parser.add_argument("step", nargs="?", choices=STEPS,
                        help="one step alone; without it the guided run takes them all in this order")
    parser.add_argument("--base-url", default=DEFAULT_BASE_URL, help=f"MAM's address (default {DEFAULT_BASE_URL})")
    parser.add_argument("--curl", default=DEFAULT_CURL, help=f"curl.exe to run the jobs line with (default {DEFAULT_CURL})")
    parser.add_argument("--expect-version", default=DEFAULT_EXPECTED_VERSION,
                        help="the release serverInfo.version must name, before its +commit (default %(default)s)")
    parser.add_argument("--out-dir", default=str(DEFAULT_OUT_DIR), help="where the records go (default %(default)s)")
    parser.add_argument("--poll", action=argparse.BooleanOptionalAction, default=None,
                        help=f"at the revoked and member steps, read the jobs every {POLL_SECONDS} s until MAM refuses the "
                             f"token (at most {POLL_LIMIT_SECONDS // 60} minutes) instead of waiting for Enter; the guided "
                             "run's default, --no-poll waits for Enter")
    parser.add_argument("--tokens-from-env", action="store_true",
                        help="for trying the script against a local API only: read the tokens from "
                             + ", ".join(variable for _, variable, _ in TOKEN_KINDS.values()) + " instead of asking")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    common.setup_console()
    args = parse_args(argv)
    ctx = Context(args)
    if not Path(ctx.curl).is_file():
        raise ToolError(f"{ctx.curl} is not there. Windows 10 and 11 ship it; pass --curl with another curl 7.55+.")
    steps = [args.step] if args.step else list(STEPS[:-1])
    say(f"MAM-48 token check against {ctx.api.base}; records go to {ctx.out_dir}")
    if ctx.api.base != DEFAULT_BASE_URL:
        say(f"Note: not production's address ({DEFAULT_BASE_URL}).")
    if not args.tokens_from_env:
        require_console()
    # Asked for up front, so the guided run needs nobody at the keyboard until the revoke.
    for kind in dict.fromkeys(kind for step in steps for kind in STEP_TOKENS[step]):
        ctx.tokens.get(kind)

    results: list[tuple[str, dict, list[Path]]] = []
    code = 0
    try:
        for step in steps:
            run_step(ctx, step, results)
        if ctx.guided:
            say()
            answer = common.ask("Check a member's token too (it needs the owner's Revoke tokens on the Users page)? [y/N]")
            if answer.strip().lower() in ("y", "yes"):
                run_step(ctx, "member", results)
    except StopRun as stop:
        say()
        say("Stopped: " + str(stop))
        code = 2
    except ToolError as error:
        say()
        say("Stopped: " + str(error))
        code = 2

    say()
    say("Summary")
    for step, counts, paths in results:
        say(f"  {step:<13} {counts['OK']:>3} OK  {counts['CHECK']:>3} CHECK   {paths[0].name}")
    say(f"  Requests with a token answered 401 (each took one of this address's 10 sign-in attempts a minute): "
        f"{_FAILED_AUTHENTICATIONS[0]}")
    if any(counts["CHECK"] for _, counts, _ in results) and code == 0:
        code = 1
    # The script's tokens are revoked right after it: the guided run, when it got through, is done with them.
    done_with_tokens = ctx.guided and code in (0, 1)
    for kind, token in ctx.tokens.still_active():
        label, _, name = TOKEN_KINDS[kind]
        say(f"  Still active: {label} \"{name}\" ({shown(token)}). {REVOKE_WHERE[kind]} "
            + ("now: the script is done with it, and no Claude client holds it." if done_with_tokens
               else "once no step of the script needs it any more."))
    return code


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        print("\nStopped with Ctrl+C; the record of every step begun so far is written.", flush=True)
        sys.exit(130)
    except ToolError as failure:
        print(scrub(f"Stopped: {failure}"), flush=True)
        sys.exit(2)
    except Exception as failure:  # a bug: one scrubbed line, never a traceback
        print(scrub(f"Stopped by a bug: {type(failure).__name__}: {failure}"), flush=True)
        sys.exit(3)
