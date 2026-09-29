# Roadmap

**Status:** Phase A in progress, targeting release 0.1.0. MAM-1 (the scaffold), MAM-2 (UI
localisation), MAM-3 (the Docker image and CI), MAM-4 (accounts and sign-in), MAM-6 (the live
environment facts) and MAM-7 (the core model and integration settings) are built; no phase has
shipped yet.

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
  The adapters only test a connection so far; MAM-8, MAM-10 and MAM-11 add their library, search
  and download contracts.
- **MAM-8 Jellyfin inventory:** paginated library listing with IDs and their sources, versions,
  file paths and state snapshots; items in nested `Collection` folders work without being moved.
  Movies are listed with collection grouping switched off: with it on, Jellyfin shows a collection
  in place of its movies (670 items instead of 870 on the reference installation).
- **MAM-9 ffprobe inspection** of local files under configured media roots: resolution, codecs,
  audio and subtitle tracks, duration and HDR, shown as unknown when not available.
- **MAM-10 Torznab search:** one Torznab provider with per-indexer configuration; Toloka tested
  through Prowlarr with a real query. The provider speaks plain Torznab, so it has no code specific
  to Prowlarr or Toloka.
- **MAM-11 qBittorrent and destinations:** category listing (its connection test came with
  MAM-7), magnet and `.torrent` submission, job and file monitoring, and logical destinations
  (`Movies`, `Movies 4K`) mapped to existing categories without an overriding save path.
- **MAM-12 Unified search page:** library copies beside available releases, verified properties
  beside claimed ones; a failing source shows its own error without hiding the others.
- **MAM-13 Send to client:** pick a release, client and destination; one click creates one job,
  and repeated clicks or retries never create a second download.
- **MAM-14 Job tracking:** persisted jobs that follow the client's status and survive a restart.
- **MAM-15 Phase A acceptance:** run the acceptance bar below against the live installation and
  record which integrations were tested live and which remain unverified.
- **Backup/restore:** nothing to add. The whole state is the data directory (`mam.db`, which holds
  the accounts, sessions, integrations and audit trail too, and `.secrets/`, whose encryption key
  alone opens the stored credentials); copy it while the app is stopped. A secret set through
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

- Scan only configured roots, without following symlinks outside them.
- Account for hardlinks (same device and inode), verify metadata, and hash candidate files lazily,
  with the hash cache keyed by file identity, size and modification time.
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
