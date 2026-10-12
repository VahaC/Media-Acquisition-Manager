# 36. The image starts as root only to hand its data directory to PUID and PGID, then drops to them with no capabilities before the app starts

Status: accepted (0.3.0, MAM-39)

## Context

[ADR
0033](0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)
decided that the app runs as the media folders' owner, because moving an old copy into the
quarantine renames files there and only their owner may: 1000 and 100 on the reference
installation, qBittorrent's ids. It reached those ids the way 0.3.0 was first published, on
2026-10-11: the compose file's `user: "${APP_USER_ID:-10001}:${APP_GROUP_ID:-10001}"`, the ids in
`.env`, and the data volume, which belongs to the image's 10001, handed over once by hand with the
container stopped — `docker compose run --rm --no-deps --user 0:0 --entrypoint chown app -R
1000:100 /data` —, and back the same way.

What that did on the reference installation, when MAM-28 turned replacement on there on 2026-10-12:

- **The ids did nothing.** Production's `docker-compose.yml`, taken before 0.3.0, had no `user:`
  line, and `bash deploy.sh` changes no file, so `APP_USER_ID` and `APP_GROUP_ID` in `.env` would
  have been ignored without a word.
- **Ids and volume that disagree stop the app.** A volume handed to 1000 while the app still runs
  as 10001 — the `user:` line missing, or the ids left out of `.env` — leaves the app unable to open
  its own database and secrets, and the container restarts over and over.
- **Every installer on their own.** The release note had to grow the `user:` line to add, a
  `docker compose config | grep 'user:'` to see it took, a backup of `mam.db` and `.secrets/` before
  the `chown`, what a `Permission denied` in the log means, and the same `chown` backwards to go
  back: host commands, run by hand, in the right order.

The rest of a media stack doesn't work that way. The linuxserver.io and hotio images of
qBittorrent, Jellyfin, Sonarr and Radarr take the ids to run as from `PUID` and `PGID`, and give
their own config folder to them as they start; the reference installation's qBittorrent runs as 1000
and 100 that way, and the media's owner is whoever qBittorrent runs as.

What any such scheme has to get past:

- **Only root can hand files over.** A container started as 10001 can neither change an owner nor
  switch to other ids, so a container that hands its data over must start as root.
- **Root must not reach the app.** The app parses files downloaded from the internet (ffprobe),
  holds the integrations' credentials and, as the media's owner, may already write wherever
  qBittorrent may (ADR 0033). As root, with Docker's default capabilities, it could also write over
  the data of every other id, and a setuid binary in the image could take root back.
- **The data directory isn't alone.** A media folder can be mounted inside `/data` by mistake, and
  a secret file bind-mounted there; neither may change hands. `find -xdev` stays on one file system
  by its device number, and a bind mount of a folder from the same disk shares that number: tried
  on a local build, a media folder mounted inside `/data` from the same disk had its owner changed
  by `find -xdev` alone.
- **Ids that wrap.** `setresuid()` reads `(uid_t)-1`, 4294967295, as "keep the current id", and the
  current id is root's: `PUID=4294967295` would keep the app root.
- **Groups the installation gave.** A compose file's `group_add` (`docker run --group-add`) is how
  an installation lets the image's own user read media only a group may read. Docker gives those
  groups to the container's first process, root's entrypoint now; dropping them on the way down
  would take that read access away without a word, and root's own group 0 must not come along.
- **Root without its capabilities.** `cap_drop: ALL` was harmless while the image started as its
  own user. Started as root, the entrypoint couldn't look into the 0700 data directory or switch
  ids, and would fail half-way with an error that says nothing of why.

Vasyl's decision of 2026-10-12: `PUID` and `PGID`, as the rest of the stack takes them, inside
0.3.0, which keeps its number and is published again with this.

## Decision

**The container starts as root for one thing only: handing its data directory over to `PUID` and
`PGID` — the media folders' owner, the image's own 10001 when they are unset —, changing the owner
of nothing else and the mode of nothing at all. It then replaces itself with the app through
`setpriv`, as those ids, with no capability, an empty bounding set, `no_new_privs` and no
supplementary group but those Docker gave the container, never root's group 0. Root without the
capabilities that takes is refused up front. A container given a user (`user:`, `docker run
--user`) starts as that user, and the entrypoint only starts the app (Vasyl's decision of
2026-10-12).**

**The ids.** `docker-entrypoint.sh` reads `PUID` and `PGID` from the environment — compose's
`environment:` or `.env`, which the compose file passes in whole —; the app never sees them. Each
must be digits only, 1 to 4294967294, leading zeros dropped, so `setpriv`, `chown` and `find` all
read the same number and none looks a name up; unset or empty, it is 10001, so an installation that
sets neither runs as it always has, and either may be set alone. 0 is refused — "[entrypoint] PUID
is 0: the app never runs as root. Set PUID and PGID to the owner of the media folders, or remove
them to run as the image's own 10001." —, and so are 4294967295 and anything but digits —
"[entrypoint] PGID must be a number from 1 to 4294967294, such as 100, not "abc"." —, each before
anything changes hands, with a non-zero exit and no app.

**The capabilities, first.** Before anything changes hands, the entrypoint reads its effective
capabilities (`CapEff` in `/proc/self/status`): without `CHOWN` and `DAC_OVERRIDE`, which the
hand-over takes, or `SETGID`, `SETUID` and `SETPCAP`, which `setpriv` does — `cap_drop: ALL` removes
them all —, it refuses with the ids it would have switched to and the two ways out, exit 1, nothing
changed: "[entrypoint] The container runs as root without the capabilities it takes to switch to
10001:10001 (CHOWN, DAC_OVERRIDE, SETGID, SETUID and SETPCAP; cap_drop: ALL removes them), so the
app was not started. Keep Docker's default capabilities, or run the container as a user instead:
user: "10001:10001" in docker-compose.yml, with the data volume already theirs." This holds for
every start as root, `PUID` and `PGID` set or not.

**The hand-over.** In the data directory (`${MAM_Storage__DataDirectory:-/data}`; the compose file
pins `/data`), one `find . -xdev`: each folder is asked `mountpoint -q --nofollow` and pruned, with
all it holds, when it is a mount, and a file only when it is about to change hands, as the poster
caches can hold thousands; only what another uid or gid owns gets `chown -h`, which changes a link
itself and never what it points at, and `find` follows no link. Modes are never touched, so the
directory stays 0700 and the secrets 0600. A start with nothing to change writes nothing and says
nothing; otherwise one line on standard error, "[entrypoint] Handed /data over to 1000:100: 6
files and folders changed owner; permissions are unchanged." A `find` or `chown` that fails stops
the start — "[entrypoint] Could not hand /data over to 1000:100; the app was not started." —, so
the app never runs over data half handed over. A data directory that doesn't exist yet is the
app's to create, as before.

**Then down, for good.** `exec setpriv --reuid --regid --inh-caps=-all --bounding-set=-all
--no-new-privs --groups=<list> -- dotnet MediaAcquisitionManager.Api.dll …`: the real, effective,
saved and file-system ids all `PUID` and `PGID`; as supplementary groups, those Docker gave the
container — `id -G` of the entrypoint without root's group 0, so compose's `group_add` and `docker
run --group-add` reach the app as they did under the image's own user —, and `--clear-groups` in
place of `--groups` when there are none; no inheritable, permitted, effective or ambient capability; an empty bounding set and `no_new_privs`, so nothing the
app runs — a setuid binary included — can gain a privilege back. `HOME` is `/nonexistent`, what the
image's user always had: the app keeps nothing outside its data directory. `exec` all the way, so
the app is PID 1 and gets `docker stop`'s SIGTERM itself. `setpriv` is util-linux's, already in the
base image.

**A user given to the container wins.** Started as anyone but root, the entrypoint can neither
change an owner nor switch ids, so it starts the app as it is, and says so when `PUID` or `PGID`
are set: "[entrypoint] PUID and PGID are ignored: the container already runs as 1000:100, set by
user: in docker-compose.yml or by docker run --user. Remove that to use PUID and PGID." A compose
file of 0.3.0 as first published, with its `user:` line, keeps working as it did.

**The image and the compose file.** The Dockerfile has no `USER`; its `ENTRYPOINT` is
`["/usr/local/bin/docker-entrypoint.sh", "dotnet", "MediaAcquisitionManager.Api.dll"]`, the app's
command line inside it rather than in a `CMD`, so arguments after the image name still reach the
app as configuration. The script is root's, mode 0755, like the app's own files, and the build
context's allowlist gains it. The compose file loses its `user:` line, and `APP_USER_ID` and
`APP_GROUP_ID` are gone; `PUID` and `PGID` sit commented out under `environment:`, and `.env`
works as well.

| Rejected option | Why not |
| --- | --- |
| The compose file's `user:` line and a `chown` of the data volume by hand (0.3.0 as first published) | Host commands every installer runs on their own, and again backwards to go back; a compose file without the line, as production's was, ignores the ids without a word, and ids that disagree with the volume leave the app unable to open its data, restarting over and over |
| Finding the owner of `/media` and running as it | Magic nobody sees or sets; it may pick root, who owns four of production's folders, while a library can have several owners, and `/media` is only the compose file's example |
| `gosu` or `su-exec` | Another binary to install and keep patched, where `setpriv` comes with the base image |
| s6-overlay, the linuxserver.io way | An init system and a process supervisor, with their own conventions, for one process |
| `find -xdev` alone to keep mounts out | A bind mount from the same disk shares the device number: tried, it changed the owner of a media folder mounted inside `/data` |
| `setpriv` without `--bounding-set=-all` and `--no-new-privs` | A setuid binary the app could run would take root back |
| `--clear-groups` on every start | Drops the groups `group_add` gives, the way an installation lets the app read media only a group may read, without a word |

`bash tools/check-image-user.sh <image>` pins it, on a built image, in eight scenarios, each PASS or
FAIL: (a) a default start runs the app as 10001:10001, healthy, its data 10001:10001 and 0700; (b)
`PUID=4242 PGID=4343` with `--group-add 4444` hand all of the data over with every mode kept, and
the app, PID 1, runs as 4242:4343 in all four ids with 4444 its only supplementary group, every
capability set empty, `NoNewPrivs` 1 and `HOME` `/nonexistent`, the log's count matching the data's
entries; (c) taken out again, they hand it back to 10001, and the app has no supplementary group;
(d) `--user 1000:100` with `PUID` set runs as 1000:100 and logs that `PUID` and `PGID` are ignored;
(e) `PUID=0`, `PGID=0`, `PGID=abc`, `PUID=4294967295` and root without capabilities
(`--cap-drop ALL`) are refused, each with its line, a non-zero exit and the data as it was; (f) a start and a `docker restart` with nothing to hand over
print nothing; (g) a media folder of 1000:100, modes 750 and 640, mounted inside `/data` on a
hand-over keeps its owner and modes; (h) `docker stop` ends the app within 5 seconds with exit
code 0.

## Consequences

**Turning replacement on takes no host command.** Drop `:ro` from the media mount, set `PUID` and
`PGID` to the media's owner, and restart the app the way it is updated, `bash deploy.sh` or
`docker compose up -d`; going back is taking them out and restarting, and the data comes back to
10001 by itself. Going back to 0.2.1, which has no entrypoint and runs as 10001, takes one start of
0.3.0 without them first, so that its data belongs to 10001 again.

**Root inside the container, for a moment.** The entrypoint runs as root with Docker's default
capabilities, of which it needs `CHOWN`, `DAC_OVERRIDE` (to walk a 0700 data directory another id
owns), `SETUID`, `SETGID` and `SETPCAP` (to empty the bounding set). A container run with
`cap_drop: ALL` and no user, which started under 0.2.1, is refused up front after the upgrade, its
line naming both ways out: keep Docker's default capabilities, or give it a `user:` line, the data
volume already belonging to those ids.

**Root where the app isn't.** With no `USER` in the image, the health check's `curl` and a
`docker exec` run as root inside the container, unless the container is given a user; the app
doesn't. `docker exec … id` therefore reads root's ids, not the app's: `/proc/1/status` has the
app's.

**The groups Docker gives stay the app's, and only those.** A `group_add` keeps giving the app read
access to media only a group may read, whoever it runs as; root's group 0 never reaches it, and a
container given none leaves the app none.

**A bind-mounted data directory changes owner on the host**, to `PUID` and `PGID`, and back to 10001
once they are taken out; its mode stays as it was created, so it is still made 0700 first.

**One walk of the data directory per start**, `stat`s only while nothing changes hands: the poster
caches' thousands of files are looked at, not written.

**Not met on the reference installation yet.** The eight scenarios passed on a fresh local build
on 2026-10-12, and the same image, run over a copy of a development database with `PUID=1000` and
`PGID=100`, logged "[entrypoint] Handed /data over to 1000:100: 7 files and folders changed owner;
permissions are unchanged." and showed the Media folders page's sentences in a browser (ADR 0033's
MAM-39 section); production was turned on and off again by the first way, a `user:` line and a
`chown` (MAM-28).

## Anti-consequence

Root is the entrypoint's for the hand-over of the app's own data and nothing more: no other folder
changes owner, no mode changes, nothing in the media folders is touched, and nothing the app does
runs as root or with a capability. It doesn't license a later feature to keep root, or a capability,
past the entrypoint.
