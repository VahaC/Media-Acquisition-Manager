# MAM-37 check against production

The script the owner runs by hand for MAM-37's check against production: a copy of production's database (the MAM
container on the media host, 192.168.1.195), kept on this PC, that the MAM-37 build is run on locally, so the
same-work groups can be read through `GET /api/library/duplicates` and on the Duplicates page with their running
times as production measured them, and nothing is written on production. Nothing here is part of the app, its build
or its tests.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `copy_db.py` | One ssh call, `tools/mam22/report.py`'s (imported, not repeated): a copy of `mam.db`, `-wal` and `-shm` of the container's `/data` (never `.secrets`) and the container's facts (image, digest, mounts, mount table, ffprobe version). `--verify` reads the kept copy only, read-only (SQLite `mode=ro`, SELECTs only). | `data\` (the copy, kept), `facts.txt` and `baseline.json` in the output folder; in the copy only, its allowed folders are removed and each integration's address becomes `http://127.0.0.1:9` (below). On the media host only report.py's `mktemp -d` folder, removed by the same command. | `python D:\Sources\Media-Acquisition-Manager\tools\mam37\copy_db.py`, then, with the local app running, `… --verify` |

## Why the copy is changed before the local app opens it

The app's start queues a file inspection whenever a media folder is allowed (`FileInspectionReconciler`), and
production allows `/media`, which is no path on Windows: the run would write every name "outside the allowed
folders" and clear the device, inode and size it read. The duplicates take a name's duration only from a name whose
last look read the media, so every running time would be gone, and with them what MAM-37 is checked by. With no
folder allowed the start queues nothing. The integrations' addresses are pointed at a port nothing listens on, so
the local app reaches none of production's services, whatever it tries; its own new `.secrets` can't open their
sealed keys either. `GET /api/library/duplicates` reads neither table, and `--verify` shows that the tables it does
read are as they were copied.

`--force` replaces an earlier copy (stop the local app first: Windows keeps an open database from being deleted);
a failed ssh leaves the earlier copy as it was. `--db <path>` copies a local `mam.db` instead (no ssh, no facts)
and `--out` writes elsewhere; both are for trying the script, not for the check. The script keeps report.py's
rules: Python 3.10+ and its standard library only; `ssh root@192.168.1.195` asks for the host's password itself;
output works on a Windows console.

## Where the output goes

`D:\Sources\Media-Acquisition-Manager\.claude\mam37-check\` (gitignored). The copy holds password hashes and sealed
keys: it stays on this PC, and `data\` is deleted when the check is done.
