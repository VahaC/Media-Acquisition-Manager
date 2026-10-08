# MAM-22 live check

The script the owner runs by hand before and after MAM-22's live check of Phase B on production (the MAM container
on the media host, 192.168.1.195): what production's database and container say about the library's files, so the
record can quote measured facts — second names, sizes two files share, the hashes, the room the files take, the
`/media` mount and its file system — and show that the check changed no file. Nothing here is part of the app, its
build or its tests.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `report.py` | One ssh call: a copy of `mam.db`, `-wal` and `-shm` of the container's `/data` (never `.secrets`), opened read-only on this PC (SQLite `mode=ro`, SELECTs only, named columns only); `docker inspect` of the container (image, digest, start time, mounts); the container's `/proc/self/mountinfo` and `ffprobe -version` through `docker exec`. | `report-<label>.txt` and `report-<label>.json` in the output folder, and while it runs `db-copy\`, deleted at the end. On the media host only a `mktemp -d` folder that holds the copy while it is streamed back, removed by the same command. | `python D:\Sources\Media-Acquisition-Manager\tools\mam22\report.py before`, then `… after` |

`after` compares itself with `report-before.json`: names that appeared or went, sizes, modification times and
identities that changed, new runs, new audit rows, the TMDb links' states and the duplicate decisions.

The script imports `tools/mam34/common.py` for the console setup and the ssh runner, and keeps its rules: Python
3.10+ and its standard library only; `ssh root@192.168.1.195` asks for the host's password itself; output works on
a Windows console. `--db <path>` reads a local `mam.db` instead (no ssh, no container facts), and `--out-dir`
writes elsewhere; both are for trying the script, not for the check.

## Where the output goes

`D:\Sources\Media-Acquisition-Manager\.claude\mam22-check\` (gitignored). The JSON holds every file name's path:
it stays on this PC.
