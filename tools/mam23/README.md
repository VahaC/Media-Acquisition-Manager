# MAM-23 facts

The script the owner runs by hand for MAM-23: who owns the media folders on the media host (192.168.1.195), and
whether the MAM container's user has write and search permission there and what else would refuse a rename, so
that Phase C's write access was chosen on measured facts: the app runs as the media's owner
([ADR 0033](../../docs/adr/0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)).
Nothing here is part of the app, its build or its tests.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `facts.py` | One ssh call, read commands only: the kernel, `fs.protected_hardlinks`, the LXC's uid and gid maps; the MAM, Jellyfin and qBittorrent containers' image, user, added groups, mounts, `PUID`/`PGID`/`UMASK` (no other environment variable) and each main process's uid, gid, groups and umask; the mount, file system, free space, owners, modes and extended ACLs of the folder MAM mounts at `/media`; inside the MAM container, `id` and `test -w -x` on `/media`, each folder in it and each library's subfolders, which asks the kernel and writes nothing; how many files and folders have an owner the LXC can't map (65534), how many folders are sticky and how many files are immutable or append-only; qBittorrent's disk I/O, file pool, queueing, share-limit and content-removal settings; Jellyfin's .NET runtime, library-monitor delay and whether "date added" is a file's creation time. | `facts.txt` in the output folder. Nothing on the media host. | `python D:\Sources\Media-Acquisition-Manager\tools\mam23\facts.py` |

The script imports `tools/mam34/common.py` for the console setup and the ssh runner, and keeps its rules: Python
3.10+ and its standard library only; `ssh root@192.168.1.195` asks for the host's password itself; output works on
a Windows console. `--host` asks another host and `--out-dir` writes elsewhere.

## Where the output goes

`D:\Sources\Media-Acquisition-Manager\.claude\mam23-check\facts.txt` (gitignored): it names the host's folders,
users and groups, and stays on this PC.
