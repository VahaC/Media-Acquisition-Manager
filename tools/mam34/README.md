# MAM-34 live checks

Scripts the owner runs by hand against the live homelab (Jellyfin 12.1 and the MAM container on the media
host, 192.168.1.195) before any production code of MAM-34 is written: how a TMDb id set by hand should reach
Jellyfin through its Identify (`POST /Items/RemoteSearch/Movie|Series`, then
`POST /Items/RemoteSearch/Apply/{itemId}?replaceAllImages=true`) rather than through `ProviderIds` and a
metadata refresh. Nothing here is part of the app, its build or its tests.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `inventory.py` | A copy of the MAM database (`mam.db`, `-wal` and `-shm` of the container's `/data`, fetched with one ssh call, opened read-only with SQLite `mode=ro`, deleted at the end unless `--keep-copy`) and Jellyfin, with `GET` requests only: version, plugins, libraries and their options, the item of every TMDb link, and a name search for the known cases. Its Jellyfin client is created `read_only=True`, so any other method raises before a byte is sent. | `inventory.txt` and `inventory.json` in the output folder (and, while it runs, `mam-copy\`). Nothing on Jellyfin or in MAM; on the media host only a `mktemp -d` folder that holds the copy while it is streamed back, removed by the same command. | `python D:\Sources\Media-Acquisition-Manager\tools\mam34\inventory.py` |
| `identify_check.py` | Jellyfin: the items it is pointed at, and what Identify answers for a TMDb id. | Jellyfin, and only after a typed `y` for each item: Apply replaces an item's name, overview, images and all of its provider ids. | `python D:\Sources\Media-Acquisition-Manager\tools\mam34\identify_check.py` |
| `common.py` | Not run; the two scripts import it. | Nothing by itself. | |

The docstring header of each script is the authority on what it reads, what it can write and the one command
to run; if it differs from this table, fix the table.

## Where the output goes

`D:\Sources\Media-Acquisition-Manager\.claude\mam34-check\` (created on first write). `.claude/` is
gitignored, so nothing there can be committed by accident. Each script says at the end which files it wrote.
The output is raw material: to turn an answer into a vendor sample, copy it into
`tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/Jellyfin/` by hand and follow that folder's
README (sanitizing, naming, the row in its table). `common.redact_json`, `common.redact_text` and
`common.leaks` apply the same rules `VendorSamplesTests` enforces, so a candidate that `leaks()` finds
nothing in will not fail the build. By that README only credentials become `REDACTED`; user names, user ids
and the server id stay as they are in the committed samples.

## Rules the scripts keep

- Python 3.10+ and its standard library only: no `requests`, no `pip`.
- Secrets are asked for with hidden input (`getpass`), never taken from a file the owner edits, never printed
  and never written anywhere. The one exception is an environment variable that stands in for the typed
  Jellyfin API key so a test can run unattended: `MAM34_JELLYFIN_API_KEY`. Each script names it in `--help`.
- `ssh root@192.168.1.195` asks for the host's password itself, on the console.
- Output works on a Windows console: stdout and stderr are reconfigured to UTF-8 with `errors="replace"`,
  because titles are Ukrainian.
- Everything committed is English. Comments say why a line exists, not what it does.

## `common.py` at a glance

- Constants: `REPO_ROOT`, `OUT_DIR`, `DEFAULT_JELLYFIN_URL`, `DEFAULT_MAM_URL`, `DEFAULT_SSH_HOST`,
  `MAM_CONTAINER`, `JELLYFIN_CONTAINER`, `ENV_JELLYFIN_KEY`, `REDACTED`.
- Console and prompts: `setup_console()`, `ask(prompt, default)`, `ask_secret(prompt, env_name)`,
  `confirm(prompt)` (true only for a typed `y` or `yes`).
- Jellyfin: `Jellyfin(base_url, api_key, timeout, read_only)` with `request`, `get`, `post`, `system_info`,
  `virtual_folders`, `plugins`, `admin_user_id`, `item`, `list_items`, `library_for`; every call returns a
  `Reply` and never raises on an HTTP error status. Transport failures and timeouts raise `JellyfinError`,
  with the key redacted from the message.
- Files: `write_json`, `write_text`, `out_path`, `ensure_out_dir`.
- Samples: `redact_json`, `redact_text`, `leaks`, `Reply.as_sample()` (the status line, headers and body the
  way the `*-response.txt` samples keep them).
- `ssh_run(host, remote_command, stdin_bytes, timeout)` and `normalize_title(text)`.
