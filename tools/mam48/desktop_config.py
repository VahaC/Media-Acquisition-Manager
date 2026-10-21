r"""MAM-48: put Profile's Claude Desktop block into Claude Desktop's own configuration for the check, and take it out
again afterwards, so the owner merges no JSON by hand.

Reads:  the token for Claude Desktop, taken from the clipboard after Copy on Profile (or Enter / Ctrl+V), as check.py
        takes its tokens; Claude Desktop's claude_desktop_config.json; the paths of running claude.exe processes
        (PowerShell's Get-Process), to know Claude Desktop is quit.
Writes: claude_desktop_config.json — `add` backs it up as claude_desktop_config.json.mam48-backup first, then adds
        mcpServers.mam exactly as Profile's shown-once step builds it (npx -y mcp-remote@0.15.2 <base>/mcp
        --transport http-only [--allow-http] --header "Authorization:${AUTH_HEADER}", env AUTH_HEADER "Bearer <token>");
        `restore` puts the backup back byte for byte and deletes it. The token is printed nowhere: only its first 14
        characters, as Profile's list shows it.
Run:    python D:\Sources\Media-Acquisition-Manager\tools\mam48\desktop_config.py add
        python D:\Sources\Media-Acquisition-Manager\tools\mam48\desktop_config.py restore
        Both refuse while Claude Desktop runs: it reads the file only at start and writes it while running.

Claude Desktop from the Microsoft Store (MSIX) reads the file in its package folder,
%LOCALAPPDATA%\Packages\Claude_<id>\LocalCache\Roaming\Claude\, which programs outside the app can't see as
%APPDATA%\Claude\ (README "Connect an AI assistant (MCP)"); that one is used when no package folder exists.

Python 3.10+, standard library only; it borrows check.py's clipboard reading.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import check  # noqa: E402  (its clipboard reading, token shape and scrub)
from check import ToolError, clean, read_hidden, shape_problem, shown  # noqa: E402

DEFAULT_BASE_URL = "http://192.168.1.195:8118"
CONFIG_NAME = "claude_desktop_config.json"
BACKUP_SUFFIX = ".mam48-backup"
# Written beside the backup when there was no file at all, so restore deletes the file instead of putting one back.
ABSENT_MARK = ".mam48-absent"
# The mcp-remote release Profile's block names (CreateAccessTokenDialog's MCP_REMOTE). Pinned there after the check
# of 2026-10-21, whose run of this script still wrote the bare name, which npx resolved to this same release.
MCP_REMOTE = "mcp-remote@0.15.2"


def config_path() -> Path:
    """The file this installed Claude Desktop reads: the MSIX package's own copy, else %APPDATA%'s."""
    packages = Path(os.environ.get("LOCALAPPDATA", "")) / "Packages"
    if packages.is_dir():
        for package in sorted(packages.glob("Claude_*")):
            folder = package / "LocalCache" / "Roaming" / "Claude"
            if folder.is_dir():
                return folder / CONFIG_NAME
    return Path(os.environ.get("APPDATA", "")) / "Claude" / CONFIG_NAME


def claude_desktop_running() -> list[str]:
    """Paths of running claude.exe processes that belong to Claude Desktop (its own, and its Code tab's)."""
    result = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "Get-Process -Name claude -ErrorAction SilentlyContinue | ForEach-Object { $_.Path }"],
        capture_output=True, text=True, timeout=60)
    paths = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    return [path for path in paths if "\\WindowsApps\\Claude_" in path or "\\Packages\\Claude_" in path
            or "\\AnthropicClaude\\" in path]


def wait_until_quit(assume_quit: bool) -> None:
    if assume_quit:
        return
    while running := claude_desktop_running():
        print(f"Claude Desktop still runs ({len(running)} of its processes). Quit it completely — the tray icon by "
              "the clock → Quit; closing the window is not enough — then press Enter here (Ctrl+C stops).")
        input()


def read_config(path: Path) -> dict:
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as error:
        raise ToolError(f"{path} is not valid JSON ({error.msg}, line {error.lineno}): nothing was changed.") from None


def desktop_block(base_url: str, token: str) -> dict:
    """mcpServers.mam as Profile's shown-once step builds it (CreateAccessTokenDialog.claudeDesktopConfig)."""
    args = ["-y", MCP_REMOTE, f"{base_url.rstrip('/')}/mcp", "--transport", "http-only"]
    if base_url.lower().startswith("http:"):
        args.append("--allow-http")
    args += ["--header", "Authorization:${AUTH_HEADER}"]
    return {"command": "npx", "args": args, "env": {"AUTH_HEADER": f"Bearer {token}"}}


def ask_token(from_env: bool) -> str:
    if from_env:
        value = clean(os.environ.get("MAM48_DESKTOP_TOKEN", ""))
        if shape_problem(value) is not None:
            raise ToolError("--tokens-from-env: MAM48_DESKTOP_TOKEN is not a token.")
        return value
    check.require_console()
    for _ in range(5):
        raw, from_clipboard = read_hidden(
            "The token for Claude Desktop: on your Profile (opened at http://192.168.1.195:8118) create \"MAM-48 Claude "
            "Desktop\" with access Read only (Лише читання) and press Copy (Копіювати) in its shown-once step. The "
            "script takes it from the clipboard by itself (or press Enter after Copy); nothing shows. Waiting... ",
            watch=True)
        value = clean(raw)
        if (problem := shape_problem(value)) is None:
            note = " (taken from the clipboard, which is emptied now)" if from_clipboard else ""
            print(f"  Token: {shown(value)}{note} — compare it with the token's row on Profile.")
            return value
        print(f"That is not a token, so nothing was written: {problem}." if value else "Nothing was taken.")
    raise ToolError("No usable token after five tries; nothing was written.")


def add(base_url: str, from_env: bool, assume_quit: bool, path: Path) -> None:
    backup = path.with_name(path.name + BACKUP_SUFFIX)
    if backup.exists() or path.with_name(path.name + ABSENT_MARK).exists():
        raise ToolError(f"A backup from an earlier run is still there ({backup.name}): run `restore` first, so the "
                        "original file isn't lost.")
    token = ask_token(from_env)
    print("Now quit Claude Desktop completely (the tray icon by the clock → Quit), then press Enter here.")
    if not assume_quit:
        input()
    wait_until_quit(assume_quit)
    config = read_config(path)
    servers = config.get("mcpServers") or {}
    if not isinstance(servers, dict):
        raise ToolError("mcpServers in the file is not an object: nothing was changed.")
    if "mam" in servers:
        raise ToolError("The file already has an mcpServers.mam: nothing was changed. Run `restore` if this script "
                        "put it there, or take it out by hand.")
    if path.exists():
        shutil.copy2(path, backup)
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.with_name(path.name + ABSENT_MARK).write_text("no file before MAM-48\n", encoding="utf-8")
    config["mcpServers"] = {**servers, "mam": desktop_block(base_url, token)}
    path.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    written = read_config(path)["mcpServers"]["mam"]
    print(f"Added mcpServers.mam to {path}")
    print(f"  command: {written['command']}")
    print(f"  args:    {' '.join(written['args'])}")
    print(f"  env:     AUTH_HEADER = Bearer {shown(token)}")
    print(f"  backup:  {backup.name if backup.exists() else '(there was no file; restore deletes it)'}")
    print("Next: start Claude Desktop. Settings → Developer should list mam as running; in a chat (not Code), + → "
          "Connectors → mam shows its tools (9 for a Read only token). Then quit it completely and run restore.")


def restore(assume_quit: bool, path: Path) -> None:
    backup = path.with_name(path.name + BACKUP_SUFFIX)
    absent = path.with_name(path.name + ABSENT_MARK)
    if not backup.exists() and not absent.exists():
        raise ToolError(f"No backup next to {path}: nothing to restore.")
    print("Quit Claude Desktop completely (tray icon → Quit) if it runs, then press Enter here.")
    if not assume_quit:
        input()
    wait_until_quit(assume_quit)
    if backup.exists():
        shutil.copy2(backup, path)
        if path.read_bytes() != backup.read_bytes():
            raise ToolError(f"{path} doesn't read back as the backup: the backup {backup.name} is kept.")
        backup.unlink()
    else:
        path.unlink(missing_ok=True)
        absent.unlink()
    left = (read_config(path).get("mcpServers") or {}) if path.exists() else {}
    print(f"Restored {path}: the file is as before the check, mcpServers.mam {'still there!' if 'mam' in left else 'gone'}.")
    print("Next: start Claude Desktop, then revoke \"MAM-48 Claude Desktop\" on Profile.")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Add Profile's Claude Desktop block for MAM-48, or take it out.")
    parser.add_argument("action", choices=("add", "restore"))
    parser.add_argument("--base-url", default=DEFAULT_BASE_URL, help=f"MAM's address (default {DEFAULT_BASE_URL})")
    parser.add_argument("--config", type=Path, help="a copy of the file to work on, for trying the script")
    parser.add_argument("--assume-quit", action="store_true", help="skip the Claude Desktop checks; with --config only")
    parser.add_argument("--tokens-from-env", action="store_true", help="read MAM48_DESKTOP_TOKEN; with --config only")
    args = parser.parse_args(argv)
    if (args.assume_quit or args.tokens_from_env) and args.config is None:
        parser.error("--assume-quit and --tokens-from-env are for trying the script on a copy (--config)")
    path = args.config or config_path()
    print(f"Claude Desktop's configuration: {path}")
    try:
        if args.action == "add":
            add(args.base_url, args.tokens_from_env, args.assume_quit, path)
        else:
            restore(args.assume_quit, path)
    except KeyboardInterrupt:
        print("\nStopped; nothing more was changed.")
        return 130
    except ToolError as error:
        print(check.scrub(f"Stopped: {error}"))
        return 2
    except Exception as failure:  # one scrubbed line, never a traceback that could hold the token
        print(check.scrub(f"Stopped by a bug: {type(failure).__name__}: {failure}"))
        return 3
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
