# MAM-48 token check and Claude Desktop configuration

The scripts the owner ran on his own PC during MAM-48's live check of Phase E (0.5.0) on 2026-10-21. `check.py` checks
what the installation answers to personal access tokens (MAM-5, [ADR 0040](../../docs/adr/0040-a-personal-access-token-acts-as-its-user-reads-what-its-user-reads-and-writes-only-what-an-explicit-list-opens.md))
and what its MCP endpoint offers each token (MAM-45, [ADR 0041](../../docs/adr/0041-an-ai-assistant-reaches-the-app-through-one-mcp-endpoint-for-personal-access-tokens-alone-where-each-tool-is-a-read-or-one-of-the-three-writes-judged-per-call.md)),
with the sentences in English and Ukrainian (MAM-47, [ADR 0042](../../docs/adr/0042-the-server-keeps-a-key-and-values-and-writes-each-sentence-in-the-language-the-request-asks-for.md)).
It takes tokens copied from Profile's shown-once step, so it never needs a password, and it makes the record the
ticket's check quotes. `desktop_config.py` puts Profile's Claude Desktop block into Claude Desktop's own configuration
and takes it out again. The pages, the languages, and Claude Code or Claude Desktop actually connected are checked by
hand; nothing here is part of the app, its build or its tests. What the runs found is in [The runs of
2026-10-21](#the-runs-of-2026-10-21) below, and in the README's Tested with and the roadmap's MAM-48 line.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `check.py` | MAM's HTTP API at `--base-url` (production's `http://192.168.1.195:8118` by default), nothing else: `GET /api/downloads` through Windows' `curl.exe`, `POST /api/downloads` with all-zero ids, and `POST /mcp` (initialize, notifications/initialized, tools/list, tools/call `get_status` and `send_download` with all-zero ids). The expected sentences come from this checkout's `src/MediaAcquisitionManager.Api/Locales/{en,uk}/account.json`. | Nothing on MAM by itself (see [What it leaves behind](#what-it-leaves-behind)). On this PC `token-check-<step>.txt` and `.json` in the output folder; a step's earlier record is kept as `token-check-<step>-<UTC time>.txt`/`.json`. | `python D:\Sources\Media-Acquisition-Manager\tools\mam48\check.py` |
| `desktop_config.py` | The token for Claude Desktop (from the clipboard, as `check.py` takes its tokens), Claude Desktop's `claude_desktop_config.json` (the MSIX package's own copy under `%LOCALAPPDATA%\Packages\Claude_<id>\LocalCache\Roaming\Claude\`, else `%APPDATA%\Claude\`), and the paths of running `claude.exe` processes, to refuse while Claude Desktop runs. | `add`: backs the file up as `claude_desktop_config.json.mam48-backup`, then adds `mcpServers.mam` exactly as Profile's shown-once step builds it (`npx -y mcp-remote@0.15.2 <mam>/mcp --transport http-only [--allow-http] --header "Authorization:${AUTH_HEADER}"`, `--allow-http` only for an `http:` address, the token in `env`). `restore`: puts the backup back byte for byte (or deletes the file if there was none) and deletes the backup. Prints only the token's first 14 characters. | `python D:\Sources\Media-Acquisition-Manager\tools\mam48\desktop_config.py add`, later `… restore`, each with Claude Desktop quit |

The docstrings of the scripts are the authority on what they read and write; if one differs from this README, fix
the README. `desktop_config.py` exists so the owner merges no JSON by hand into a 20 KB file the app also writes;
the block it adds is the one Profile prints, so the check still proves that block. Tried on 2026-10-21 on a scratch
copy (`--config`, `--assume-quit`, `--tokens-from-env`): added beside an existing server, a second `add` refused,
`restore` byte-identical, and the no-file case created and removed the file. The check's own run wrote `mcp-remote`
unpinned, as Profile did then, and npx fetched 0.15.2; after the check Profile's block was pinned to
`mcp-remote@0.15.2` (MAM-48's F7), and so was this script's, tried again the same day on a scratch copy: the block
added beside an existing server, `restore` byte-identical. Run with its output piped rather than in a console, it
needs `PYTHONIOENCODING=utf-8`, as its prompts print "→".

## Before you run

The script works with tokens of its own and revokes them as it goes. Claude Code and Claude Desktop get tokens of
their own when you connect them, never the script's: a revoked token still held by a client keeps failing and uses up
this PC's 10 sign-in attempts a minute, for every token and for your own sign-in.

| Token | Access | Created by | Revoked |
| --- | --- | --- | --- |
| `MAM-48 script read only` | Read only | you, on your **Profile** | by you at the `revoked` step, while the script waits |
| `MAM-48 script read and download` | Read and download | you, on your **Profile** | by you right after the run; the script's last lines remind you |
| `MAM-48 member` | Read only | the temporary member `mam48-member`, on its own **Profile** | at the `member` step: the owner presses **Revoke tokens** on that member's row on **Users** |

Create each token in your own browser, not in Claude's browser pane, so no page Claude reads ever shows a secret. In
the shown-once step press **Copy** (Копіювати) next to the token while the script's prompt waits: on Windows the
script notices the clipboard change, takes the token from it by itself, with no key to press, and empties the
clipboard of it; nothing appears. Other text copied meanwhile is skipped and left alone. A token copied before the
prompt started is taken only when you press Enter, so a stale one is never picked up unasked; Ctrl+V does the same,
and typed or pasted text is taken as it is. Each prompt names the token to create and its access. (A classic console window — cmd, or PowerShell's
own — hands Ctrl+V to a hidden prompt as a control character instead of the text, and its right-click may paste
nothing, which is why the script reads the clipboard: seen on 2026-10-21.) Start the script first, in a console window
(cmd, PowerShell or Windows Terminal): it asks for each token as you create it, so a token is taken once and never kept
anywhere. Anywhere else, such as an IDE's run window, it refuses before the first prompt, as that console would show
the token. Windows' clipboard history (Win+V), if it is on, keeps its own copy until the token is revoked.

1. Sign in to production as the owner and open **Profile** (Профіль) → **Access tokens** (Токени доступу).
2. **Create token** (Створити токен): name `MAM-48 script read only`, access **Read only** (Лише читання), expires in
   30 days. **Copy** while the script's first prompt waits, then **Done** (Готово).
3. **Create token**: name `MAM-48 script read and download`, access **Read and download** (Читання й завантаження),
   expires in 30 days. **Copy** while the second prompt waits, **Done**. Leave the step's Claude Code and Claude
   Desktop lines alone: those clients get their own tokens.
4. For the member step (asked at the end of the guided run, or `check.py member` alone): as the owner, add the user
   `mam48-member` on **Users** (Користувачі); sign in as `mam48-member` where the check looks at the member's pages
   (Claude's browser pane, whose page Claude doesn't read while the token is on screen, or a private window of your
   browser), open its **Profile** → **Access tokens**, create `MAM-48 member` with **Read only** yourself, and press
   **Copy** while the script's prompt for the member's token waits.

The script checks each value's shape on this PC before anything is sent (`mam_pat_` and 43 letters, digits, `-` and
`_`), refuses the same token pasted twice, and prints only its first 14 characters, the way Profile's list shows it,
so you can compare the two. `get_status` then names each token: a token not named as above is a CHECK.

## Run

```
python D:\Sources\Media-Acquisition-Manager\tools\mam48\check.py
```

The guided run takes every step below in order and asks for the two tokens once at the start. Where you act on a page,
after its ACTION line, it goes on by itself: while you revoke `MAM-48 script read only` on Profile, and, if you answer
`y` to the member question, while the owner presses **Revoke tokens** on `mam48-member`'s row on Users, it reads the
jobs with that token every 3 s and goes on at the first 401, for at most 10 minutes (then it stops with a CHECK).
`--no-poll` makes it wait for Enter instead. One step alone is
`python D:\Sources\Media-Acquisition-Manager\tools\mam48\check.py <step>`, asking only for the token it needs; there
`revoked` and `member` wait for Enter unless `--poll` is given, so start `member --poll` only when the revoke is less
than 10 minutes away.

| Step | With | Sends | Expects |
| --- | --- | --- | --- |
| `read` | `MAM-48 script read only` | The jobs line Profile's shown-once step and README "Scripts and the API" print, `curl -H "Authorization: Bearer mam_pat_…" <mam>/api/downloads` (below) | 200 `{ items, totalCount, page, pageSize, needsYouCount, inProgressCount }`, newest first; the record keeps the counts and the newest job, not the list |
| `refused-send` | `MAM-48 script read only` | `POST /api/downloads` with every id `00000000-0000-0000-0000-000000000000`, once without `Accept-Language` and once with `Accept-Language: uk` | 403 `{ error, code: "account.token.read-only" }` with ADR 0040's read-only sentence in English, then in Ukrainian |
| `mcp-tools` | both script tokens | `initialize` (protocol `2025-11-25`, what Claude Code 2.1.220 sends), `notifications/initialized`, `tools/list`, `tools/call get_status` | serverInfo `mam`, version `0.5.0+<commit>` (the commit compared with this checkout's HEAD), no `Mcp-Session-Id` (stateless), 202 for the notification; Read only: the nine reads and no `send_download`, `cancel_job` or `dismiss_recommendation`; Read and download: twelve, `send_download` marked `anthropic/requiresUserInteraction`; `readOnlyHint` true on each read and false on each write; `get_status` names the owner, the token's access, the token's name as above and the same version |
| `mcp-write` | `MAM-48 script read only` | `tools/call send_download` with all-zero ids, without and with `Accept-Language: uk` | An `isError` result whose text is the read-only sentence in English, then in Ukrainian, with no `structuredContent` |
| `revoked` | `MAM-48 script read only` | The `read` line (200); while you revoke the token, the same line every 3 s until its first 401 (with `--no-poll`: once, after Enter); then the line without a token, and `POST /mcp` initialize with the revoked token | The first request after the revoke 401, within 3 s of the token's last 200; its body byte for byte the body without a token (`{"error":"Sign in to continue.","code":"account.sign-in-required"}`), both with `WWW-Authenticate: Bearer`; `/mcp` the same 401 |
| `member` (optional) | `MAM-48 member` of `mam48-member` | The `read` line, `initialize`, `notifications/initialized` and `get_status`; while the owner presses **Revoke tokens** on Users, the line every 3 s until its first 401 (with `--no-poll`: once, after Enter); then the line without a token | 200 with the member's own jobs, `get_status` naming the member and its token; then 401, byte for byte the body without a token |

The owner's **Users** page showing the member's number of tokens (**1 token**), and **0 tokens** after **Revoke
tokens**, is read by eye while the member step waits; the script never sees Settings, which no token may read. The
record keeps the last 200 and the first 401 of a wait, not every request.

**The curl line.** Profile prints `curl -H "Authorization: Bearer <token>" <origin>/api/downloads`. The script runs
`C:\Windows\System32\curl.exe -q -sS -i --noproxy * --max-time 60 -H @- <base>/api/downloads` with the line
`Authorization: Bearer <token>` on curl's stdin: `-H @-` reads headers from stdin (curl 7.55 and later; Windows ships
8.x), so the token is on no command line and in no process list. `-q`, which curl takes only as its first argument,
keeps it from reading any `.curlrc` or `_curlrc`, where a `-v`, `--trace` or `--stderr` would write the header out.
The rest is for the record only: `-sS` drops the progress meter, `-i` keeps the status line and headers, `--noproxy *`
goes straight to MAM, as a proxy set in the environment would otherwise receive the token, and `--max-time 60` keeps a
dead address from hanging the run. Every other request goes through Python's `http.client`, which ignores proxy
settings too. No request sends a token anywhere but `--base-url`, and curl and git get the environment without any
`MAM48_` variable.

## Why the refused send can't send anything

**A Read only token never reaches the send.** `Program.cs` runs `UseAuthentication()` and `UseAuthorization()` before
`MapControllers()`. `DownloadsController.Create` carries `[PersonalAccessTokenWrite]`, so in the authorization
middleware `PersonalAccessTokenAccessHandler` fails a `ReadOnly` token's requirement, and
`PersonalAccessTokenRefusalResultHandler` writes the 403 with the sentence right there. The endpoint never runs: MVC's
model binding, `[ApiController]`'s automatic 400 and `DownloadService.SendAsync` all come after, and the body is never
read. At `/mcp` the same holds per tool: `McpToolAccessFilter.CallTool` refuses `send_download` before the tool (and
the binding of its arguments) runs.

**A Read and download token pasted by mistake sends nothing either.** Every id is the all-zero GUID.
`DownloadRules.Validate`, the send's first check, takes an empty `requestId` for a missing one and answers 400
`downloads.request.request-id-missing` before any row is read, so no job is written, no client is asked and no earlier
send can be replayed. Through MCP, `send_download` first looks the empty request id up as a retry (no job can have
one, as `Validate` refuses it), then asks the overwrite check about the empty destination, which refuses it without
asking a client, and the send refuses the empty request id. The script reads 400 or that error text as the wrong token
and says so with CHECK. Tried on a local API: both answers came, and the owner's job list stayed empty.

## Limits it keeps

- **Failed sign-ins.** A request with a token answered 401 takes one of its address's 10 sign-in attempts a minute,
  shared with sign-in. The guided run causes three, all expected: `revoked` two (the line and `/mcp`), `member` one.
  A request without a token takes none, and neither does a request MAM lets in, so the waits' requests every 3 s cost
  nothing until their first 401. A wrong but well-formed token stops the run at its first 401, so a typo costs one; a
  malformed paste is refused on this PC and costs none. The summary prints the count. Running a step again within the
  minute adds its own.
- **`/mcp`.** At most nine POSTs per token in a run (`MAM-48 script read only`: four in `mcp-tools`, four in
  `mcp-write`, one in `revoked`), against the limit of 60 a minute per token.
- **Waits.** At most 200 requests of the jobs line per wait (one every 3 s for 10 minutes), with a token MAM still
  lets in; no limit counts them, and **Last used** is written at most once a minute.
- **429.** On `account.too-many-attempts` or `account.token.too-many-requests` the run stops: wait a minute and run
  the step again.

## What it leaves behind

On production: no job, search, setting or token. The server keeps what any use of a token leaves: each token's **Last
used** (written at most once a minute), a warning in its log for each refused request, and one
`PersonalAccessTokenRefused` row in the owner's audit trail per token and minute for the refused sends (the read-only
token's four refusals, seconds apart in the guided run, make one row; steps run a minute or more apart make one each). The revokes are yours, with their
`PersonalAccessTokenRevoked` rows.

Right after the run, revoke `MAM-48 script read and download` on Profile: the script's last lines name every token of
the run MAM still lets in. `MAM-48 script read only` and `MAM-48 member` are revoked during the run; delete the user
`mam48-member` when the check no longer needs it.

## Output

`D:\Sources\Media-Acquisition-Manager\.claude\mam48-check\` (gitignored), one pair per step: `token-check-<step>.txt`,
which lists an `OK`, `CHECK` or `INFO` line per expectation and then every request with its answer, and
`token-check-<step>.json` with the same and the facts (counts, versions, tool names, the caller). `OK` is what Phase E
promises, `CHECK` is not and says why, `INFO` has nothing to judge. Neither file holds a token: every `mam_pat_` value
is cut to its first 14 characters before anything is printed or written, and a record that would still hold one is
not written. The jobs record names the newest job's release title, so the folder stays on this PC; send back the
`.txt` files.

Exit code 0 when every expectation is OK, 1 with any CHECK, 2 when the run stopped (a refused token, a 429, MAM not
reachable, no console to ask in, a wait's 10 minutes over), 3 when it stopped on a bug of its own (one line, never a
traceback), 130 on Ctrl+C. The record of a step begun is written even when it stops.

## The runs of 2026-10-21

- **`check.py`, the first two tries.** Run in a classic cmd window, both stopped before any request: Ctrl+V reached
  the hidden prompt as a control character, and right-click pasted nothing. Nothing reached production; their two
  tokens showed "last used: never" on Profile and Vasyl revoked them. The script has taken the token from the clipboard
  by itself since.
- **`check.py`, the guided run, 08:45 UTC,** against production with a `Read only` and a `Read and download` token:
  every expectation OK but the tokens' names, both "test" (two CHECKs of no meaning for the product). `read`: 200, 19
  jobs, newest first, through `curl.exe` 8.21.0. `refused-send`: 403 `account.token.read-only`, English without
  `Accept-Language`, Ukrainian with `uk`. `mcp-tools`: protocol `2025-11-25`, server `mam`, version `0.5.0+2593f91`
  (this checkout's HEAD then), no session, the nine reads for `Read only` and twelve tools for `Read and download`,
  `send_download` carrying `anthropic/requiresUserInteraction`. `mcp-write`: an `isError` result with the read-only
  sentence in both languages. `revoked`: 11 requests over 30 s, the first after **Revoke** 401, 3.0 s after the last
  200, byte for byte the body without a token, and `/mcp` the same. At 08:47 UTC Profile listed no active token.
- **`check.py member`, 10:11 to 10:17 UTC,** with the temporary member's own `Read only` token, named "mam48-member":
  its own jobs (200, none) and `get_status` naming "mam48-member (a member)" with the same version; then, after the
  owner's **Revoke tokens** on Users, the request sent at Vasyl's Enter got 401, byte for byte the body without a
  token, 0.1 s later. 13 OK and 1 CHECK, the token's name again.
- **`desktop_config.py`, 10:22 to 10:24 UTC:** `add`, with Claude Desktop quit, backed up the MSIX package's own
  `claude_desktop_config.json` and added `mcpServers.mam` with a `Read only` token, "MAM-48 Claude Desktop"; Claude
  Desktop 3.1623.0 then ran `mcp-remote` 0.15.2, which connected (the README's Tested with). Claude Desktop was quit at
  10:23:29 UTC, `restore` put the backup back, leaving no `mam` and no backup, and Vasyl revoked the token; at 10:24
  UTC neither Claude Desktop's configuration nor Claude Code's held a `mam`.

The records, `token-check-*.txt` and `.json`, stay in the gitignored output folder on that PC.

## Trying it without production

`--base-url`, `--out-dir`, `--expect-version` and `--tokens-from-env` exist for trying the script against a local API
with test tokens: `--tokens-from-env` reads `MAM48_READ_ONLY_TOKEN`, `MAM48_READ_AND_DOWNLOAD_TOKEN` and
`MAM48_MEMBER_TOKEN` instead of asking, and is never used against production. On 2026-10-21 the guided run passed
every expectation (68 OK, 0 CHECK) against a local 0.5.0 API (Development, `http://127.0.0.1:5191`, stamped
`0.5.0+2593f91` as the image is), once with `--no-poll` and the revokes made through the API at its prompts, and once
polling, with the revokes made through the API 4 s after each ACTION line (each 401 came 3.0 s after the last 200). A
wait nobody answers stopped with its CHECK (the limit shortened for the test); a console without hidden input (a pipe,
or getpass's echo fallback) was refused before the first prompt; a bug stopped the run with one line and exit code 3;
curl saw no `MAM48_` variable; with a `.curlrc` and `_curlrc` saying `-v` or `--trace-ascii`, curl without `-q` showed
the header and the script's curl did not; a Read and download token run as the Read only one, a malformed value and an
unknown token each behaved as described above, and no output or server log held a token.

Python 3.10+ and its standard library only; it imports `tools/mam34/common.py` for the console setup and its answer
and error types, and writes no bytecode.
