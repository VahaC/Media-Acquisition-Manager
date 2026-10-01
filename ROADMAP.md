# Roadmap

**Status:** Phase A in progress, targeting release 0.1.0. MAM-1 (the scaffold), MAM-2 (UI
localisation), MAM-3 (the Docker image and CI), MAM-4 (accounts and sign-in), MAM-6 (the live
environment facts), MAM-7 (the core model and integration settings), MAM-8 (the Jellyfin
library inventory), MAM-9 (the ffprobe inspection of the library's files), MAM-10 (the Torznab
release search) and MAM-11 (qBittorrent's categories and the download destinations) are built; no
phase has shipped yet.

This file lists unshipped phases only. When a phase ships, its heading becomes
`### ✅ Shipped (<release>) Phase …`, and its detail later moves to `HISTORY.md`. The phases
follow §6 of the
[development specification](docs/Media_Acquisition_Manager_Spec.md#6-implementation-phases-and-acceptance-criteria);
this file adds the ticket breakdown, the tests and the acceptance bar. Phase A is tracked as tickets
MAM-1 to MAM-15 in the Kaneo project `MAM`. Releases for phases B–D are not decided yet.

## Phases

### Phase A — Vertical slice without deletion

**Release:** 0.1.0. **Tickets:** MAM-1 to MAM-15.

**Complexity:** High
**Value:** search once and see the copies you already own beside the releases you could download,
then send the release you picked to qBittorrent in the right category — while the app leaves every
existing file untouched.

**Scope:**

- **MAM-1 Scaffold** (built): the solution and its test projects, the React web app shell
  with a service status card, `/api/health` and `/api/health/ready`, security headers with a
  hash-pinned CSP, SQLite in WAL mode with migrations applied on startup, first-run secrets, the
  real-HTTP test harness, and the project docs.
- **MAM-2 Internationalisation** (built): react-i18next with English resources split by feature;
  every UI string goes through `t()`, dates, numbers and file sizes are formatted in the UI locale,
  and lint refuses literal text in markup, so other languages can be added later
  ([ADR 0001](docs/adr/0001-every-ui-string-comes-from-a-locale-resource.md)).
- **MAM-3 Docker and CI** (built): one Docker image for amd64 and arm64 (API + built web app +
  `ffprobe`, running unprivileged), `docker-compose.yml` for the published image and
  `docker-compose.build.yml` for a local build, both publishing the port on `127.0.0.1` by
  default, `deploy.sh`, a Jenkins CI pipeline that tests every commit to `main`, and a
  release pipeline, started by hand, that pushes `X.Y.Z`, `X.Y` and `latest` to Docker Hub
  ([PUBLISHING.md](PUBLISHING.md)).
- **MAM-4 Accounts** (built): a first-run setup page that creates the installation owner and then
  closes, sign-in by user name with a 15-minute access token and a rotating `HttpOnly`,
  `SameSite=Strict` refresh cookie (sessions end after 30 days unused), a session check on every
  request so sign-out, disable, delete and a password reset act at once
  ([ADR 0002](docs/adr/0002-every-authenticated-request-checks-its-session.md)), one identical 401
  for every failed sign-in, 10 sign-in attempts a minute per client address, an owner-only Users
  page (server-side search and paging; add, disable and enable, reset password, delete with a
  confirmation), owner-only settings writes by namespace convention, and an audit trail. Two-factor
  sign-in and password reset by email are out of scope.
- **MAM-5 Personal access tokens** for scripts and other tools.
- **MAM-6 Live environment facts** (built): the specification's open questions (§7) answered
  against the real installation. One folder on one ext4 filesystem holds every media library and
  every download; Jellyfin and the app see it as `/media`, qBittorrent as `/downloads`, so each
  integration's paths reach the app through an explicit prefix mapping
  ([ADR 0003](docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md)).
  qBittorrent 5.2.3 has one category per media library, automatic torrent management on and no
  separate folder for incomplete downloads, so torrents download straight into the library. Toloka
  is searched through the Prowlarr that already runs there, not a new Jackett, and Prowlarr's
  download links stay on the server
  ([ADR 0004](docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md)).
  The app runs beside them with its own sign-in, published on the local network at port 8118. Real,
  sanitized answers of every service are the adapters' test fixtures
  (`tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/`). Questions 3, 4 and 6 stay
  open and are listed under Phases B and C.
- **MAM-7 Core model and integration settings** (built): the core entities of specification §4
  that Phase A needs — works, their titles and identifiers, versions, physical files, releases,
  download jobs and destinations — where every title, identifier, version, file and release names
  the source that reported it, a work is not a library item, a release is not one file and a file
  row is one name of a set of bytes
  ([ADR 0006](docs/adr/0006-every-title-identifier-and-file-the-core-stores-names-its-source.md));
  contracts for media libraries, release search, download clients and metadata behind adapter types
  registered by id, and one for the app's own file inventory, none naming a product
  ([ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md));
  and an owner-only Integrations page (server-side search, kind filter and paging; add, edit, switch
  off, remove after a confirmation) with a real connection test for Jellyfin, a Torznab feed and
  qBittorrent, from the dialog before saving and from the list. Credentials are encrypted at rest
  (AES-256-GCM with `Encryption:Key`, sealed to their integration and field) and never returned by
  the API
  ([ADR 0007](docs/adr/0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).
  Path mappings are stored per integration and resolved folder by folder, never by a guess
  ([ADR 0003](docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md)).
  MAM-7's adapters only tested a connection; MAM-8 added the library contract to Jellyfin's, MAM-10
  the search contract to Torznab's, and MAM-11 the destinations of the download contract to
  qBittorrent's (submission and job reading come with MAM-13 and MAM-14).
- **MAM-8 Jellyfin inventory** (built): the owner syncs a Jellyfin library (10.9 or later) from the
  Library page in the background (202 + run row + reconciler), reading its libraries and every
  movie in pages that overlap by one, each asking for collection grouping off
  (`collapseBoxSetItems=false`: asked with an API key, Jellyfin otherwise shows a collection in
  place of its movies whatever its own setting, 670 items instead of 870 on the reference
  installation). The sync replaces that library's snapshot only after reading all of it, so a
  failure or a library changing mid-read stores nothing; items become works per library by IMDb and
  TMDb id only, versions keep their media source, and ids stay stable across syncs
  ([ADR 0008](docs/adr/0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md)).
  Every signed-in user browses the works with their versions, file paths and the paths the app
  finds them at through the mappings, missing identifiers flagged and unknown values shown as
  unknown, searched, filtered (by Jellyfin library, by missing identifiers) and paged by the
  server. Items in nested `Collection` folders are found where they are, without being moved
  (`LibraryAcceptanceHttpTests` against the samples). Removing a Jellyfin integration removes the
  library it imported, not the files
  ([ADR 0006](docs/adr/0006-every-title-identifier-and-file-the-core-stores-names-its-source.md)).
- **MAM-9 ffprobe inspection** (built): the app reads the library's files itself, only inside the
  folders the owner allows on a Media folders settings page (none by default; owner's choice over an
  environment variable), resolving every link and refusing one that leads out of them. An inspection
  runs in the background after every successful sync, at each start and on the owner's **Inspect
  files** (202 + run row + reconciler), two files at a time, and writes each file's outcome as it
  goes; a file it can't read is an outcome with its reason, never a failure of the run. ffprobe's
  facts — duration, bit rate, and every video, audio and subtitle track with codec, resolution,
  dynamic range, channels, language and flags, unknown where the file doesn't say — are kept by the
  file's device, inode, size and modification time, so the same bytes are read once whatever names
  lead to them and again when any of the four changes; the identity and link count of every name
  are recorded for Phase B
  ([ADR 0009](docs/adr/0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)).
  The Library page shows the facts under each file and each source's inspected, unreadable and
  waiting files. Track titles are not shown (owner's choice).
- **MAM-10 Torznab search** (built): every signed-in user searches all enabled release sources at
  once through the API (`POST /api/releases/search`; the page is MAM-12's), and each source answers
  with its own status, its sentence for the owner, and its releases — size, seeders and peers, date,
  the page at the source, categories, and the quality and languages the title claims, marked as
  claims. One Torznab adapter, one integration per indexer feed, speaking plain Torznab with nothing
  specific to Prowlarr or Toloka; it reads each feed's capabilities and searches by IMDb or TMDb id
  where the feed takes one, then by title and year, then by title, in the movie categories. Each
  release found is stored (owner's choice), its download link sealed to it without the gateway's
  key, and removed a day later unless a download job uses it; the server fetches the payload itself,
  from the feed's own address only and following no redirect but to a magnet, and no key, link token,
  forum session or tracker passkey reaches the browser, the log or a stored column
  (`ReleaseSearchSecretsHttpTests`;
  [ADR 0004](docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md),
  [ADR 0007](docs/adr/0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).
  The live Toloka search was verified through the app on 2026-10-01: 16 movie releases for
  "Interstellar" 2014.
- **MAM-11 qBittorrent and destinations** (built): the owner maps logical destinations (`Movies`,
  `Movies 4K`) to qBittorrent's own categories on an owner-only Destinations page, and no save path is
  ever stored or sent: a destination is the category's name exactly as qBittorrent spells it, saved
  only once qBittorrent confirms it has that category, and one destination is the installation's
  default, never replaced on the owner's behalf
  ([ADR 0010](docs/adr/0010-a-destination-is-a-download-clients-own-category-never-a-folder.md)).
  Each destination shows where its category saves — qBittorrent's folder, through its path mappings —
  and whether that lies inside a Jellyfin library folder, which the library sync now stores; a folder
  outside every library, one that can't be determined, and libraries not synced yet are flagged with
  the reason, and will keep automatic replacement off in Phase C
  ([ADR 0003](docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md)). The
  qBittorrent adapter (4.4.1 or later) lists the categories and works each one's folder out the way
  qBittorrent does, a Windows host's network folders included, keeping one session per integration
  and signing in again once when qBittorrent has forgotten it, but never again with credentials
  qBittorrent refused, or after it banned the app's address, until the address or credentials change
  or a connection test with them works
  ([ADR 0007](docs/adr/0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md));
  it sends only the sign-in and GETs
  (`DestinationsClientDestinationsHttpTests.Every_call_the_destinations_make_to_a_client_is_the_sign_in_or_a_get_and_none_names_a_save_path`).
  Submission and job monitoring, once listed here, moved to MAM-13 and MAM-14 with their members of
  the download client contract, which MAM-11 left out rather than stub
  ([ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)).
  Only qBittorrent 5.2 was verified live, through the answers recorded from the reference
  installation and through the app; 4.4.1 to 5.1 are accepted from qBittorrent's source. Verified
  live on 2026-10-01: qBittorrent v5.2.4's connection test and its four categories, and the
  destination Movies landing in the Jellyfin library "Фільми" at `/media/movies`.
- **MAM-12 Unified search page:** library copies beside available releases, verified properties
  beside claimed ones; a failing source shows its own error without hiding the others.
- **MAM-13 Send to client:** magnet and `.torrent` submission to qBittorrent, in the destination's
  category and without a save path (the contract's submission member comes back with it); pick a
  release, client and destination; one click creates one job, and repeated clicks or retries never
  create a second download.
- **MAM-14 Job tracking:** the client's jobs and their files read back (the contract's job members
  come back with it); persisted jobs that follow the client's status and survive a restart.
- **MAM-15 Phase A acceptance:** run the acceptance bar below against the live installation and
  record which integrations were tested live and which remain unverified.
- **Backup/restore:** nothing to add. The whole state is the data directory (`mam.db`, which holds
  the accounts, sessions, integrations, audit trail, imported library and its sync runs too, and
  `.secrets/`, whose encryption key alone opens the stored credentials); copy it while the app is
  stopped. A lost library snapshot comes back with the next sync, and the files' inspection with
  the next inspection; the media folders the owner allowed are in `mam.db` too, and so are the
  download destinations and the library folders the last sync stored (those come back with the next
  sync); qBittorrent's session, and a sign-in it refused, are held in memory only. The releases a
  search found are kept there for a day, and those a download job sent for as long as the job. A secret set through
  `MAM_Encryption__Key` or `MAM_Jwt__Secret` lives in your environment instead of `.secrets/`, so
  back it up there.

**Out of scope:** removing, moving or replacing any library file (Phase C); duplicate detection
(Phase B); a second search or client adapter, Plex and Emby (Phase D); TV shows (specification
§7 question 3, which MAM-6 leaves open for Phases B and C).

**Tests:** every endpoint over real HTTP, with vendor APIs faked at the network seam; a failing
source returns its own error while the other sources still answer; submitting the same release
twice creates one job; a destination maps to its category and never sends a conflicting save
path; no Phase A code path writes to a media root.

**Acceptance bar:** a movie in a `Collection` folder can be found without moving it; Toloka (or
another indexer) appears in search when its integration actually works, and a Toloka failure shows
an explicit source-specific error; retrying the same command does not create duplicate jobs; the
category determines the expected destination without an overriding path.

### Phase B — Scanner and duplicates

**Complexity:** Medium
**Value:** see which files are true duplicates, which are other versions of the same movie, and
how much space could really be recovered — without the app guessing.

**Scope:**

- Scan the allowed roots for files no library reported, inside them only and without following
  links out of them — the rules MAM-9's inspection already applies to the library's own files
  (ADR 0009).
- Account for hardlinks (same device and inode, which MAM-9 records on every file name with its link
  count), and hash candidate files lazily, with the hash cache keyed by file identity, size and
  modification time like MAM-9's inspection cache.
- Separate the duplicate classes of specification §3.3: the same file or a hardlink,
  byte-identical copies, other versions of the same work, and possible matches.
- A Duplicates page with a filter per class and the evidence behind each grouping; a recoverable
  space estimate only where the storage relationship is known.
- **Backup/restore:** the scan inventory and the hash cache live in the database and can be rebuilt
  by rescanning, so they need no export of their own.

**Out of scope:** removing anything, and bulk deletion of any kind; replacing a selected copy is
Phase C.

**Open questions (specification §7, left open by MAM-6):**

- Question 3: does the scan cover TV shows, seasons, anime and documentaries, or movies only? On
  the reference installation the movie library holds a TV miniseries too, the cartoon library mixes
  movies and series, and the documentary library holds series.
- Question 6: are the library files original seeding files, hardlinks of them, or independent
  copies? It decides how often two names share one set of bytes, and so how much space a
  duplicate really takes. None of the files MAM-6 listed had a second link, so those at least are
  not hardlinks; Radarr, which runs beside qBittorrent, was not inspected and may import files its
  own way.

**Tests:** two hardlink names count their space once; a changed size or modification time
invalidates the cached hash; similar titles or years are not merged; different cuts stay separate
versions; conflicting IDs require manual review.

**Acceptance bar:** two hardlink names do not inflate occupied space; movies with similar names or
years are not merged incorrectly; different cuts are not deleted; conflicting IDs require manual
review.

### Phase C — Safe replacement

**Complexity:** High
**Value:** upgrade a copy you own in one step: the old file moves aside only after the new one is
downloaded, verified and visible in Jellyfin, and you can get it back.

**Scope:**

- A persisted replacement state machine (`Scheduled` → … → `Completed`, plus `NeedsReview`,
  `Paused`, `Failed` and `Cancelled`) with idempotent steps and safe recovery after a restart.
- Every check of specification §3.4 before an old file is touched, and the audit trail of each
  transition.
- Quarantine by an atomic move on the same filesystem; otherwise manual confirmation after the
  checks.
- The download dialog offers `Keep both` (default) or `Replace after verification` of specific,
  listed old files; the Jobs page can cancel a pending removal, retry verification and resolve
  ambiguity.
- **Backup/restore:** replacement jobs and the audit trail live in the database; quarantined files
  stay on disk in the quarantine directory, outside the database backup.

**Out of scope:** automatic quarantine cleanup (it needs retention and disk-space policies first),
Jellyfin's `DELETE /Items/{id}`, and deleting a torrent or its data from the client.

**Open questions (specification §7, left open by MAM-6):**

- Question 3: can a replacement target an episode or a season, or only a movie?
- Question 4: which quarantine mode and retention period, and is a second manual approval needed
  before a quarantined file is finally deleted?
- Question 6: are the old files original seeding files, hardlinks of them, or independent copies?
  A seeding file must stay untouched (specification §3.4, check 6), so the answer decides how often
  a replacement can move the old copy at all. On the reference installation torrents download
  straight into the library folders, so an old copy is often a seeding torrent's own data; how often
  was not measured
  ([ADR 0003](docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md)).

**Tests:** each failure case of the acceptance bar ends in `NeedsReview` or `Failed` with the old
file untouched; a successful replacement writes the audit trail and leaves a restorable
quarantined copy.

**Acceptance bar:** a process restart, a failed download, an unknown new path, a multifile torrent,
a failed ffprobe, a wrong ID, an unavailable Jellyfin, a changed old file, active seeding and an
invalid mount mapping never cause silent deletion. A successful unambiguous replacement leaves an
audit trail and a way to restore the quarantined copy.

### Phase D — Practical extensibility

**Complexity:** Medium
**Value:** use the app with other tools than Prowlarr and qBittorrent without losing any of the
safety of the first workflow.

**Scope:**

- One second search or download client adapter with different capabilities (for example
  Newznab with SABnzbd, or Transmission), added without changing the core state machine.
- Plex or Emby later, if needed.
- **Backup/restore:** only the new adapter's settings, stored like the existing ones.

**Out of scope:** any change to the core replacement workflow to fit the new adapter.

**Tests:** the adapter contract tests pass for both implementations; logical destinations, error
handling and the UI do not depend on qBittorrent-specific fields.

**Acceptance bar:** the second adapter works end to end — search or submit, track, and (for a
client) replace — with the core workflow unchanged.
