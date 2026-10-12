# MAM-28 live check

The script the owner runs by hand at each step of MAM-28's live check of Phase C (safe replacement, 0.3.0) on the real
installation (the MAM container on the media host, 192.168.1.195): what the container, its compose project, its
database and the media folders say at that step, and what changed since the step before. The record can then quote
measured facts: the version and the ids the app runs with, the read-write media mount and what is mounted inside it,
the switch and the ticks, each replacement job, copy, new file and quarantine name, the audit trail, which files moved
into the quarantine and back with the same inode, and the room Delete for good gave back. The first step, `before`, is
also the runbook's read-only pre-flight (`.claude/mam28-check/runbook.md`, section 0.1) and lists the candidates to
pick the film and the episode from (section D). Nothing here is part of the app, its build or its tests.

Production already runs the published 0.3.0 (Docker Hub's `0.3.0` = `0.3` = `latest`, pushed 2026-10-11 14:12 UTC,
index `sha256:0b75fdbd7f60…`) as the image's own user, uid 10001, most likely still without a `user:` line in its
compose file; there is no upgrade step, only the handover of runbook B1-B6.

| Script | Reads | Writes | Run |
| --- | --- | --- | --- |
| `report.py` | One ssh call (one password): a copy of `mam.db`, `-wal` and `-shm` of the container's `/data` (never `.secrets`), opened read-only on this PC (SQLite `mode=ro`, SELECTs only, named columns only); `docker inspect` of the container and its image (tag, digest, created, started, `Config.User`, every mount with its source, destination, read-write and propagation); the main process's uid, gid, groups and umask; `id`, `stat` of `/data` and `mam.db`, `/proc/self/mountinfo` and `test -w` (which a read-only mount fails) of each media mount, the folders directly in it and its `.mam-quarantine` through `docker exec`; the version in the app's `deps.json` (and the stamped `0.3.0+<sha>` in its dll) through `docker cp … -`; owner and mode of the data volume, `df` of each media mount's source and of every mount inside it, and a `find -printf` inventory of each media mount (type, size, mtime, inode, links, device, owner, mode, path), on the host as root; and the pre-flight below. | `report-<label>.txt` and `report-<label>.json` in the output folder (a report of the same label written before is kept as `report-<label>-<UTC time>.txt`/`.json`), and while it runs `report-db-copy\`, deleted at the end. On the media host only a `mktemp -d` folder that holds the copy while it is streamed back, removed by the same command, also when it fails. | `python D:\Sources\Media-Acquisition-Manager\tools\mam28\report.py <label>` |

Labels are free (letters, digits, `.`, `_`, `-`); the check uses these, one run each:

| Label | When |
| --- | --- |
| `before` | Before anything changes: the pre-flight and the candidates, with production on the published 0.3.0 as uid 10001, the switch off, nothing ticked and no `.mam-quarantine`. Run it once only: after the handover (runbook B1-B5) it can't be taken again. |
| `candidates` | Optional, any time before the film and the episode are sent: the candidates again, for example after B7's **Refresh all** has inspected every copy under 0.3.0. `--candidates` adds the same section to a run of any other label. |
| `on` | After "Turning it on" (runbook B1-B6): `APP_TAG=0.3.0` pinned, the compose file's `user:` line, the ids 1000:100 in `.env`, the data volume handed over, the switch and the ticks saved. |
| `scheduled` | After the film and the episode are sent with Replace after verification (and after a refused `.torrent`, a member's Download dialog, or a switched-off or unticked case, if those are staged). |
| `moved` | After the old copies are in the quarantine and Jellyfin shows the new ones (and after a held copy's Retry, if that is staged). |
| `restored` | After Restore of one job. |
| `deleted` | After Delete for good of a copy the owner agreed to lose. |

Each run compares itself with the report written before it; `--against <label>` picks another (a kept one too, by
its `<label>-<UTC time>`), `--against none` skips it. The comparison lists the files that moved (the same device and
inode at a new path, marked `ticked -> quarantine` or back; of a hard-linked file the path the quarantine names is
taken), went, appeared or changed, each tagged `ticked`, `quarantine` or `other`; the free space and the quarantine's
size before and after; any change of image, version, user, mounts, data owner or write access, path by path; the
compose lines and the `APP_` lines the pre-flight read, the auto-updaters it found and the folders uid 1000 can't
write in; the switch, ticks and allowed folders; every replacement job, copy, new file and quarantine name that
changed; new or changed download jobs (a refusal before sending shows there, with its overwrite check); and the new
audit rows. A `HELD:` line in "At a glance" quotes any "Still held by the torrent …" stop of a video, and any "Held
by the torrent …" of a companion that stayed.

## Pre-flight

The runbook's section 0.1, read in the same ssh call as everything else, so the owner runs no command block of his
own; each read is printed under "Pre-flight" with a verdict, and "At a glance" counts them:

- `docker compose version` (2.24 or newer, which the compose file's `env_file: required: false` needs);
- the image, its registry digest (Docker Hub's 0.3.0 index `sha256:0b75fdbd7f60…`), the stamped version
  (`0.3.0+2dec720`; another sha is written down, since the docs then name that commit), running and healthy since
  2026-10-11, `command -v chown` and `ffprobe -version` inside the container, `uname -m` on the host;
- the container's ids (`Config.User` empty or `mam`, `id` `uid=10001(mam)`), the mounts (`/data` the volume, one
  `/media` bind mount, both read-write), the container's mount table (exactly one mount at `/media`, options `rw,…`,
  nothing mounted inside it), and on the host `findmnt -T` of the media mount's source and `findmnt -R` of the file
  system holding it (nothing mounted below the source);
- from the compose project's folder (`--compose-dir`, else the folder docker compose recorded on the container, else
  `/srv/docker/media-acquisition-manager`), only the lines of the compose file about `image:`, `user:`, `restart:`,
  `read_only:` and `/media` (no `:ro` on the media line, no `user:` line yet), and only the lines of `.env` that start
  with `APP_` (no `APP_TAG`, so `latest`; no `APP_USER_ID`/`APP_GROUP_ID`). Every other line of `.env` may hold
  `MAM_Encryption__Key` or `MAM_Jwt__Secret` and is never read out, nor is any further line of a quoted value that
  runs over several lines, whatever it starts with; a compose line that names a secret (secret, pass, token, key, jwt,
  cred, auth, salt, private, encrypt, in any letter case) is left out even then, and an `APP_` line that names one in
  its name or its value is printed without its value;
- the names and images of running containers that look like an auto-updater (watchtower, diun, ouroboros, wud), none
  expected, and of the Jellyfin container;
- the owner of the data volume and `mam.db*` (10001:10001), its size without `.secrets` (`du --exclude=.secrets`) and
  the room at `/root` and `/var/lib/docker` for B1's backup;
- the folders under the media mount's source that uid 1000 doesn't own or can't write and search in (`find -xdev
  -type d \( ! -uid 1000 -o ! -perm -u=wx \)`): the four root-owned folders of 2026-10-08 and nothing else, since no
  copy directly inside one of them can be moved;
- no `.mam-quarantine` yet, from the inventory.

`OK` is what section 0.1 expects; `CHECK` is not, and on `before` means stop and report back; `UNKNOWN` is a read that
failed or never ran (a stopped container, a missing `findmnt`, a compose folder or file that isn't there, an unreadable
file), printed with the reason and never
stopping the run; `INFO` has nothing to judge. What step 2 changes on purpose (the `user:` line, the `APP_` ids, the
data owner, the app's ids, the quarantine) is judged against section 0.1 on `before` only and reads `INFO` at later
steps, where the comparison shows how it changed.

## Candidates

Computed on this PC from the database copy and the file inventory, with nothing more read from the host, for the labels
`before` and `candidates` and for any label run with `--candidates`; the owner picks the film and the episode from it
(runbook section D). A copy is kept only when Jellyfin reported it (not a file only the folder scan found) in a library
that holds only films, for a film, or only episodes, for an episode (Jellyfin's library type is not stored, so it is
read off the works each library holds, and a library holding both is left out); it is inspected as the Download dialog
counts it (the inspection's status, and the device, inode, size and time it read) and its inspection measured a video
track; a film has a TMDb or IMDb id, one of each kind at most, and an episode is placed in one series, season and
episode, by its own library item too (the place the dialog ties the copy to), its series has a TMDb or IMDb id with no
second series under the same ids, and its file name is no a/b half (`S03E09a`) and no file of several episodes
(`S01E01-E02`); it is no disc folder clip or disc image, not also a copy of another work, has one link (in the database
and on disk), is not held by an open replacement (by its row, its path or the identity its inspection read, as the
dialog's "already scheduled"), maps to an app path under an allowed folder and is on disk; a film is not one part of a film split over several
files (`Film.a`, `Film CD1`, `Film.part2`), and other videos beside it are only noted, as only the chosen video and its
same-name companions move; and every folder from the copy's own up to its library folder is owned by uid 1000 with owner write and
search (`u=wx`), which a rename and a later Restore both need, so the root-owned folders are left out. The table and
column names are those of the EF model (`PhysicalFiles`, `MediaVersions`, `MediaWorks`, `MediaWorkIdentifiers`,
`MediaWorkTitles`, `MediaEpisodePositions`, `MediaInspections`, `MediaInspectionTracks`, `LibraryLocations`,
`LibraryLocationFolders`, `PathMappings`), and the app path follows `PathMapper`: no mapping means the same path,
several covering mappings must agree.

The kept copies are ranked worst quality first: by resolution and then by the height a 16:9 frame of the same width
would have, when that is more than the height measured (so a 1920x800 film counts as 1080p and ranks with the 1920x1080
ones, while 480 lines still rank below 576), then by codec age (mpeg2, then mpeg4/xvid, then h264, then hevc, then av1),
then by size per minute of running time. A copy whose size or inode on disk is no longer what its inspection read stays
in the list with a note, since B7's **Refresh all** reads it again. The section lists the top 15 films and the top 15
episodes, at most two of one series, each with its title and year, ids, path, library and library folder, resolution,
codec, size, running time, MB per minute, container, and whether the database already knows a second copy of the work
(another name of it with another identity, or a copy of a work sharing its id, or, for an episode, its series, season
and episode); it also counts the copies left out by their first failed rule. Which torrent holds a copy is not known
here (the Download dialog names it), nor whether the copy is still unchanged when the dialog reads it, so B7's
**Refresh all** comes before the pick. With `--db` there is no inventory: the on-disk and folder checks then read "not
checked", and a film's folder is judged by the names the database knows.

The script imports `tools/mam34/common.py` for the console setup and the ssh runner, and keeps its rules: Python
3.10+ and its standard library only; `ssh root@192.168.1.195` asks for the host's password itself; output works on
a Windows console. A database older than 0.3.0 (such as the dev database) reads as one without replacements; columns a
table lacks are named at the top of the report. `--db <path>` reads a local `mam.db` instead (no ssh, no container,
mount, file or pre-flight facts), and `--out-dir` writes elsewhere; both are for trying the script, not for the check.

## Where the output goes

`D:\Sources\Media-Acquisition-Manager\.claude\mam28-check\` (gitignored). The JSON holds the path of every file on the
media mounts and every audit row it read: it stays on this PC.
