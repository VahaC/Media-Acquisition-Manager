# MAM-29 vendor samples

## What this is

Two scripts the owner runs by hand for MAM-29. They record what Transmission and Jackett (with its Toloka indexer)
really answer on the media host (192.168.1.195), so that Phase D's adapters (MAM-30: Transmission instead of
qBittorrent, Jackett instead of Prowlarr) are tested against recorded answers rather than against the documentation.
Transmission 4.1.3 gives the full set; a temporary 4.0.x container gives a short one, because MAM supports Transmission
4.0 and newer. Claude turns what the scripts record into vendor samples. Nothing here is part of the app, its build or
its tests.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `transmission_samples.py` | Transmission's RPC in the legacy dialect: `session-get` (an allowlist of nine fields), `torrent-get`, `free-space`. Two public .torrent files (Big Buck Bunny and Sintel from webtorrent.io, over HTTPS, in memory only), or local ones (`--torrent`, `--second-torrent`). | Transmission, each only after a typed `y`: `torrent-add` (Big Buck Bunny into the scratch folder with the label `MAM29 Samples`, the same again, a corrupt .torrent, a relative folder, a magnet without metadata, an add with a stale session id) and `torrent-stop`. It never removes a torrent and never changes Transmission's settings; it prints which torrents to delete with their data in the web UI. | `python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --full` (`--short` for the 4.0.x container) |
| `jackett_samples.py` | Jackett's Torznab API (`/api/v2.0/indexers/toloka/results/torznab/api`), GET only: caps, the queries of the committed `prowlarr-toloka-*.xml` samples, title-shape searches, the newest releases, a wrong key, a download link with a mangled token and, after a typed `y`, one real .torrent download, looked at in memory and never saved. | Nothing on Jackett or Toloka. | `python D:\Sources\Media-Acquisition-Manager\tools\mam29\jackett_samples.py` |
| `common.py` | Not run; the two scripts import it. | Nothing by itself. | |

The docstring header of each script is the authority on what it reads, what it can write and the commands to run;
if it differs from this README, fix the README.

## Before you run: the containers

All three containers are one Stashboard project on media-stack: [`docker-compose.yml`](docker-compose.yml) next to this
README. Paste it into Stashboard as a new project, change the four `CHANGE-ME` values (the user name and password of
each Transmission; the same pair for both is fine), and **Save & apply**. Everything it keeps lives under
`/srv/docker/mam-phase-d` on media-stack; nothing is written into the media folder.

| Service | Image | Address | What it is for |
| --- | --- | --- | --- |
| `transmission-prepare` | `lscr.io/linuxserver/transmission:4.1.3-r0-ls363` | none | Runs once before both Transmissions and exits (Exited 0 is its normal state). |
| `transmission` | `lscr.io/linuxserver/transmission:4.1.3-r0-ls363` | `http://192.168.1.195:9091/transmission/web/` | The full sample set now, MAM-31's live check later. |
| `transmission-406` | `lscr.io/linuxserver/transmission:4.0.6-r6-ls326` | `http://192.168.1.195:9092/transmission/web/` | The short set on the oldest Transmission line MAM supports; removed after it. |
| `jackett` | `lscr.io/linuxserver/jackett:v0.24.2830-ls3` | `http://192.168.1.195:9117` | Toloka through Jackett: the search samples now, MAM-31 later. |

Why it looks the way it does:

- **`transmission-prepare`.** Docker creates a missing bind-mount folder owned by root, and Transmission, which runs as
  uid 1000, couldn't write to `/scratch`. The step also writes each Transmission's first `settings.json` from the
  image's own defaults with two changes: torrents added without a folder go to `/scratch` instead of
  `/downloads/complete`, and the temporary folder for unfinished torrents is off. Transmission doesn't report that
  folder per torrent (`torrent-get` gives only the destination, `downloadDir`), so neither the samples nor MAM could
  say where partial files are. An existing `settings.json` is never touched, so nothing needs setting in the web UI.
  The script still checks both settings and offers no write while the temporary folder is on outside the scratch
  folder.
- **The images are pinned to exact builds** so a rebuild can't change what is recorded. `4.1.3-r0-ls363` is
  Transmission 4.1.3 (2026-09-29), `4.0.6-r6-ls326` is 4.0.6, the last 4.0.x (2026-01-27), and Jackett has no
  `AUTO_UPDATE`, so it stays v0.24.2830: Toloka's indexer is part of Jackett's own code, and another version may
  answer differently.
- **`PUID`/`PGID` 1000/100**, the same as qBittorrent and MAM: what Transmission writes belongs to the media's owner.
- **`USER` and `PASS`** on both Transmissions. The image asks for a password only when both are set; without them the
  RPC is open to the whole network. Pick a password that is not a plain word such as `transmission`, `torrent` or a
  folder name: the scripts write `REDACTED` wherever the password appears, so a plain word would spoil the samples
  (the script warns at the start when it does). A `$` in it is written `$$` in the compose file.
- **No `WHITELIST` or `HOST_WHITELIST`.** The image keeps both whitelists off unless they are set, so this PC and MAM
  reach the RPC from any address, and the password guards it. With a password set, Transmission lets any Host name
  through, so no 421 can be recorded; by the owner's decision of 2026-10-13 none is (MAM-30 takes its shape from
  Transmission's source).
- **Ports.** 9091 and 9092 are the two RPCs and web UIs, 9117 is Jackett; the peer ports are 51413 and 51414
  (qBittorrent listens on 6881). Nothing needs forwarding on the router: Big Buck Bunny comes from webtorrent.io's web
  seed. If Stashboard says a port is taken, change the number left of the colon and type that address when the script
  asks.
- **`/downloads` is commented out.** MAM-29 never writes into the media folder. MAM-31 needs it: then uncomment the
  line, after checking that its host path is the one qBittorrent mounts as `/downloads` (ADR 0003 records
  `/mnt/media14tb/jelly_qbit_data/Torrent`). The image's start script makes `PUID` the owner of `/downloads` only when
  it isn't already, and the media folder already belongs to 1000.

Then, in Jackett's web UI at `http://192.168.1.195:9117`:

1. **+ Add indexer**, find **Toloka.to** and add it with your own Toloka user name and password. Leave **Strip Cyrillic
   Letters** as Jackett sets it (ticked); the script asks which it is.
2. Note where the script's answers are: the **API Key** at the top right of the page, the Jackett version at the
   bottom.

The compose file was checked on 2026-10-13 by bringing it up on a local Docker with test passwords: the prepare step
exits 0, both Transmissions start with `download-dir` `/scratch`, the temporary folder off and the password on,
`/scratch` is writable by uid 1000, and the script's read-only steps and its refused writes (steps 09, 10, 12) and the
magnet of step 11 ran against the real 4.1.3 and 4.0.6 with a clean leak check.

## Run

Python 3.10 or newer on this PC; nothing to install. Each command works from any folder in PowerShell. Run them in this
order: Transmission 4.1.3, the 4.0.x container, then Jackett at any time.

### Transmission 4.1.3, the full set

```powershell
python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --full
```

`--full` is the default, written out so the command says which set it records: all 16 steps.

| It asks | Answer |
| --- | --- |
| Transmission RPC address | Enter, for `http://192.168.1.195:9091/transmission/rpc` |
| User name | the container's `USER` |
| Password (typing is hidden) | its `PASS` |
| The scratch folder as Transmission sees it | Enter, for `/scratch` |
| A library folder as Transmission sees it | Enter, for `/downloads` (only `free-space` reads it) |
| Send it? `[y/N]` | `y` for each write: steps 06, 07, 09, 10, 11, 12 and 14. Anything else skips that step. |

Before the steps it downloads two .torrent files from webtorrent.io over HTTPS into memory: Big Buck Bunny (3 files,
276 MB of data) and Sintel (11 files, 129 MB; only the two adds Transmission should refuse use it). If this PC can't
reach webtorrent.io, save them from a browser and add `--torrent PATH --second-torrent PATH`. Step 02 prints a note that
the scratch folder lies in Transmission's default download folder: the compose file makes `/scratch` that folder, so
that is expected.

Step 13 waits for Big Buck Bunny to finish downloading into `/scratch`. It reads the progress every 10 s for up to 30
minutes (`--wait MINUTES` changes that). Transmission takes the data from webtorrent.io's web seed, so it usually needs a
few minutes. Ctrl+C stops the waiting, and the run goes on with step 14. If Big Buck Bunny isn't complete by then, type
anything but `y` at step 14 and later run steps 13 and 14 on their own:

```powershell
python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --only 13,14
```

It writes `transmission-4.1.3\`.

### Transmission 4.0.x, the short set

```powershell
python D:\Sources\Media-Acquisition-Manager\tools\mam29\transmission_samples.py --short
```

At the first question press Enter: with `--short` the default address is the 4.0.x container's,
`http://192.168.1.195:9092/transmission/rpc`, and a `--short` run that reaches a 4.1 or newer Transmission stops
before it adds anything. Then this container's `USER` and `PASS`, Enter for `/scratch` and for `/downloads`, and `y` for steps 06 and 07. It runs steps 01,
02, 04, 06, 08 and 07, in about a minute; it doesn't wait for Big Buck Bunny. It writes `transmission-4.0.6\`.

### Jackett

```powershell
python D:\Sources\Media-Acquisition-Manager\tools\mam29\jackett_samples.py
```

| It asks | Answer |
| --- | --- |
| Jackett address | Enter, for `http://192.168.1.195:9117` |
| Indexer id | Enter, for `toloka` |
| Jackett API key (typing is hidden) | the **API Key** at the top right of Jackett's page |
| Jackett version | as the bottom of Jackett's page shows it, e.g. `v0.24.2830` |
| Is "Strip Cyrillic Letters" ticked? | `y` or `n`, Enter if you don't know |
| Which release's .torrent to download? | a number from the list it prints, Enter for 1 |
| Download it? `[y/N]` | `y` for the one real download |

GET requests only. The 12 searches reach Toloka through Jackett, at least 2 s apart (`--pause SECONDS` changes that), so
the run takes a few minutes, longer when Toloka is slow: each request waits up to 90 s. The downloaded .torrent is
looked at in memory and never saved. It writes `jackett-0.24.2830\` (named after the version you type).

## Where the output goes and what to send back

`D:\Sources\Media-Acquisition-Manager\.claude\mam29-check\<service>-<version>\` (created on first write):
`transmission-4.1.3\`, `transmission-4.0.6\` and `jackett-0.24.2830\`. `.claude/` is gitignored, so nothing there can be
committed by accident; `--out` writes elsewhere, and a folder inside the repository but outside `.claude\` is refused.
Each folder holds:

- `raw\` every request and answer, with the secrets already removed: the password, the `Authorization` header, every
  `X-Transmission-Session-Id` and `session-id` field, the Jackett API key in `apikey=` and `jackett_apikey=`, Jackett's download tokens
  (`path=`), Toloka's `sid=`; a .torrent sent in `torrent-add` is replaced by `<base64, N bytes>` (N: the .torrent's size);
- `samples-pending\` REDACTED candidates named like the future files of
  `tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/Transmission/` and `VendorSamples/Torznab/`: the JSON
  body of a 200, the XML of a Torznab answer, or the status line, headers and body as `*-response.txt`;
- `INDEX.txt` one line per file: what it shows and the service version;
- `console.txt` everything the run printed.

A second run into the same folder (`--only 13,14`) replaces the files it writes again, keeps the others
and their `INDEX.txt` lines, and adds to `console.txt`.

Secrets never touch the disk: every text goes through `common.redact_text` and then `common.leaks` before it is
written, and a text that still fails is replaced by a note. Before the script ends, `Recorder.finish` reads every file
of the folder back through `leaks()` and prints "Final leak check over every file of the folder: clean."

Nothing needs sending: the folders stay on this PC, where Claude reads them. Say in the chat which runs finished, and
whether any line started with `!!!` or `STOPPED`. Claude turns the candidates into vendor samples by hand, following
VendorSamples/README.md (sanitizing, naming, the row in its table).

## After

- **Delete the test torrents with their data**, in each Transmission's web UI: right-click the torrent and choose
  **Trash data and remove from list…**. The script prints the list at the end of each run, under "DELETE THESE
  TORRENTS, WITH THEIR DATA, IN TRANSMISSION'S WEB UI"; every one carries the label `MAM29 Samples`.
  - Transmission 4.1.3: **Big Buck Bunny** (in `/scratch`, 276 MB, stopped by step 14) and **mam29-no-metadata** (the
    magnet of step 11, with no data). **Sintel** only if a `!!!` line said Transmission added it: steps 10 and 12
    expect a refusal.
  - Transmission 4.0.x: **Big Buck Bunny** (step 06 started its download).
- **Remove the 4.0.x container**: delete the `transmission-406` block from the project's compose file in Stashboard
  and **Save & apply**; if its stopped container is still listed, remove it there too. Then delete its folder on
  media-stack, which holds only its settings and scratch folder:

  ```bash
  rm -rf /srv/docker/mam-phase-d/transmission-406
  ```

- **Transmission 4.1.3 and Jackett stay** for MAM-31's live check. Keeping or removing them after MAM-31 is the owner's
  call. Nothing of MAM-29 is left in the media folder either way.

## Rules the scripts keep

- Python 3.10+ and its standard library only: no `requests`, no `pip`.
- Secrets are asked for with hidden input (`getpass`), never printed and never written anywhere. The only exceptions
  are two environment variables that stand in for a typed secret so a test can run unattended:
  `MAM29_TRANSMISSION_PASSWORD` and `MAM29_JACKETT_API_KEY`. Each script names its own in `--help`.
- Every write to a service waits for a typed `y`; Enter or anything else skips that step. A write is never repeated:
  the permit a `y` gives is spent before the request leaves. The one exception is Transmission's 409, which says the
  request was refused before it was read and names the session id to use: the request goes once more with that id, as
  the RPC protocol prescribes, except in step 12, which records that refusal and never resends.
- The Transmission script can send `session-get`, `torrent-get`, `free-space`, `torrent-add` and `torrent-stop`, and no
  other method. It never removes a torrent and never changes Transmission's settings (the two settings above are the
  owner's to change). No write is offered while the scratch folder overlaps the library folder, Transmission can't see
  the scratch folder, or Transmission's temporary folder is on outside it.
- The Jackett script sends GET requests only, and downloads one .torrent, after a typed `y`, without saving it.
- The brute-force lock is never triggered. The wrong Transmission password goes exactly once (step 15), and only when a
  user name was typed; if the typed password itself is refused (401), the run stops at once. Transmission's lock exists only when
  `anti-brute-force-enabled` is on, and the image leaves it off. The wrong Jackett key goes three times (step 14: caps,
  a search and a download link), each refused by Jackett itself before Toloka is asked. Neither script touches a
  whitelist.
- An HTTP error status is recorded, not raised; a refused connection or a timeout is one plain sentence, and the run
  goes on or stops cleanly.
- Output works on a Windows console: stdout and stderr are reconfigured to UTF-8 with `errors="replace"`, because
  titles are Ukrainian.
- Everything committed is English. Comments say why a line exists, not what it does.

## Facts the scripts rest on

- **Transmission's legacy dialect.** The method names (`session-get`, `torrent-add`, `torrent-get`, `free-space`,
  `torrent-stop`), the kebab-case arguments of `torrent-add` (`download-dir`, `metainfo`, `filename`, `paused`, `labels`),
  the camelCase fields of `torrent-get` and the session fields were checked against `docs/rpc-spec.md` at the tags
  4.0.0 and 4.0.6. The spec at 4.1.3 documents only the new JSON-RPC 2.0 dialect (snake_case) and says the legacy one is
  still served and deprecated; `libtransmission/rpcimpl.cc` at 4.1.3 converts a legacy request and converts its answer
  back (`api_compat`). `labels` in `torrent-add` came with 4.0.0 (`rpc-version` 17). 4.1.3 reports `rpc-version` 19,
  `rpc-version-minimum` 14 and `rpc-version-semver` "6.0.1" (the first two deprecated since 4.1.0 and still served);
  4.0.6 reports 17, 14 and "5.3.0".
- **Transmission's refusals, read in `libtransmission/rpc-server.cc` at 4.0.6 and 4.1.3.** The order is: the
  brute-force lock (403), the IP whitelist (403), the password (401, with `WWW-Authenticate: Basic realm="Transmission"`),
  the Host name (421), the session id (409). The lock exists only when `anti-brute-force-enabled` is on, which it is not
  by default; then failed passwords count up to `anti-brute-force-threshold` (100 by default) and a good request resets
  the count. Any Host passes when a password is set or the Host whitelist is off, and an IP address or localhost always
  passes. The 409 of 4.1.3 also carries `X-Transmission-Rpc-Version: 6.0.1`; that of 4.0.6 carries
  `Access-Control-Expose-Headers` instead.
- **Transmission's torrent-add refusals, read in `libtransmission/rpcimpl.cc` at 4.0.6 and 4.1.3.** A relative
  `download-dir` is refused before the .torrent is read ("download directory path is not absolute"). Bytes that don't
  parse as a .torrent, like step 09's 13 bytes `not a torrent`, get HTTP 200 with the result "unrecognized info", and
  that is what MAM-30 maps; "invalid or corrupt torrent file" comes only when metainfo that parses can't make a torrent,
  which an RPC client can't practically reach. Step 09 records whatever comes and says in INDEX.txt whether it is the
  predicted answer.
- **The linuxserver image** (`root/defaults/settings.json` and the `init-transmission-config` script of
  linuxserver/docker-transmission). Its settings have `download-dir` `/downloads/complete`, `incomplete-dir`
  `/downloads/incomplete` with `incomplete-dir-enabled` **true**, and both whitelists off. The start script switches the
  IP whitelist on only when `WHITELIST` is set, the Host whitelist only when `HOST_WHITELIST` is set, and the password
  only when both `USER` and `PASS` are set; `PEERPORT` sets the peer port and turns its random choice off. Hence the two
  settings the owner changes, and step 16's answer: with `USER` and `PASS` set it records a 200, kept as
  `wrong-host-allowed-response.txt`. A 421 (`wrong-host-response.txt`) would need a container with no `USER` and
  `PASS` and with `HOST_WHITELIST` set; by the owner's decision of 2026-10-13 none is opened for it.
- **Jackett.** The Torznab API is `api/v2.0/indexers/{indexerId}/results/torznab/api`
  (`src/Jackett.Server/Controllers/ResultsController.cs`); it takes the key as `apikey` or `passkey`, and answers a wrong
  one with HTTP 200 and `<error code="100" description="Invalid API Key"/>`, not with Prowlarr's bare 401. Toloka's id
  is `toloka` (`src/Jackett.Common/Indexers/Definitions/Toloka.cs`); its "Strip Cyrillic Letters" setting, on by
  default, shapes the `<title>`, so the script asks for it. A download link is
  `dl/{indexerId}/?jackett_apikey=…&path=…&file=…` (`Jackett.Server/Services/ServerService.cs`, `ConvertToProxyLink`):
  a wrong `jackett_apikey` is a bare 401 and a `path` that doesn't unprotect is a 404, both before Toloka is asked
  (`DownloadController.cs`).

## `common.py` at a glance

- Constants: `REPO_ROOT`, `OUT_ROOT`, `VENDOR_SAMPLES`, `DEFAULT_TRANSMISSION_URL`, `DEFAULT_JACKETT_URL`,
  `ENV_TRANSMISSION_PASSWORD`, `ENV_JACKETT_API_KEY`, `REDACTED`, `UNKNOWN_VERSION`.
- Console and prompts: `setup_console()`, `say`, `heading`, `box`, `ask(prompt, default)`,
  `ask_secret(prompt, env_name)`, `confirm(prompt)` (true only for a typed `y` or `yes`).
- Secrets: `SECRETS` (`add`, `scrub`): what was typed, the Basic credential and every session id a service handed out.
- HTTP: `http_request(method, url, headers, body, timeout, host_header)` returns a `Reply` (status, reason, headers in
  order, body, `as_sample()`), never raises on an HTTP status and raises `HttpFailure` with one sentence otherwise;
  `describe_request` writes a request down with its credential headers reduced to their scheme.
- .torrent files: `read_torrent(bytes)` returns `TorrentFacts` (v1 info hash, name, files, size, tracker host names).
- Samples: `redact_text`, `leaks`, `make_safe`, and `Recorder` (`raw`, `pending`, `open`, `finish`; a file recorded
  before the version is known gets it in its header when `open` names the folder).
