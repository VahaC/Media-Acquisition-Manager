# Roadmap

**Status:** Release 0.1.0, published on 2026-10-04, holds all of Phase A but MAM-5 (personal
access tokens), which is still to do: MAM-1 (the scaffold), MAM-2 (UI localisation), MAM-3 (the
Docker image and CI), MAM-4 (accounts and sign-in), MAM-6 (the live environment facts), MAM-7 (the
core model and integration settings), MAM-8 (the Jellyfin library inventory), MAM-9 (the ffprobe
inspection of the library's files), MAM-10 (the Torznab release search), MAM-11 (qBittorrent's
categories and the download destinations), MAM-12 (the search page of library copies and
releases), MAM-13 (sending a release to qBittorrent), MAM-14 (following each download in
qBittorrent until it finishes) and MAM-16 (the approved layout: the search field in the header,
numbered pages, versions as rows or a table, one error state per kind of failure, and a Profile
page where every user changes their own password), and MAM-15 checked Phase A's acceptance bar
against the live installation on 2026-10-03. Phase B was published as release 0.2.0 on 2026-10-05
and release 0.2.1 on 2026-10-07. MAM-17 answered specification §7 questions 3 and 6 against the
live installation on 2026-10-04; 0.2.0 holds MAM-32 (series and episodes in the library, and
searching, matching and sending their releases), MAM-18 (the scan of the media folders for the
files no library reported) and MAM-19 (the room the files take, each file counted once, and the
hashing of only the files that could be copies); 0.2.1 adds MAM-33 (recommendations of the works
the library doesn't have yet, from TMDb, with what Toloka carries of them), MAM-34 (linking a
library work to its TMDb film or show by hand, Jellyfin then identifying the item as it), MAM-35
(each library work's own poster from Jellyfin on the Library and Search cards), MAM-36 (the Library
page's Sources card as a status bar, and Refresh all), MAM-20 (the library's files grouped into the
four duplicate classes of the specification) and MAM-21 (the Duplicates page, where the owner
confirms or dismisses a possible match). MAM-22 checked Phase B's acceptance criteria on the
reference installation on 2026-10-08, so Phase B has shipped as 0.2.1. MAM-37 (built while 0.2.1 was
the version, checked the same day on a copy of production's database, and first published in 0.3.0)
follows from that check: the Duplicates page lists only real copies of one film or episode — 4 of
the 46 groups production listed — and flags for review the copies of one work whose measured
running times differ widely. MAM-38 (built the same day while 0.2.1 was the version, resting on the
tests, and first published in 0.3.0) refuses a download whose files would land on a different file
already in the destination's folder before anything reaches qBittorrent, which would otherwise
download into that file in place. Phase C was published as release 0.3.0 on 2026-10-11 (Docker
Hub's `0.3.0`, `0.3` and `latest`, digest `sha256:0b75fdbd7f60…`, stamped `0.3.0+2dec720`), and
production has run it since that day. MAM-23 settled on 2026-10-08 what a Phase C replacement may
touch — specification §7 questions 3, 4 and 6, the write access and the gates (ADR 0030 to ADR
0033) —, and 0.3.0 holds MAM-24 (built on 2026-10-09: the owner schedules, in the Download dialog,
the replacement of specific old copies, which follows its download to `Downloaded`), MAM-25 (built
on 2026-10-10: each new file is checked before anything is touched — complete by qBittorrent's
word, readable by ffprobe, and identified by Jellyfin's own ids as the old copy's work, every ticked
copy paired with exactly one new file —, and the replacement ends `Verified`, or stops for review
with the reason), MAM-26 (built on 2026-10-10: the old copy and every client's torrents checked
again right before the move, the old copy renamed with its own companions into `.mam-quarantine` at
the top of its media folder, where it stays until the owner restores it or deletes it for good, and
a move, a restore or a deletion a restart cut found again by device and inode) and MAM-27 (built on
2026-10-11: the Jobs page and each job's page show where each replacement stands, its ticked copies
with the new file each pairs with, what the check found and why it stopped, and the owner keeps both
copies, retries a stopped check or drops one old copy, each with an audit row). MAM-28 checked Phase
C's acceptance criteria on the reference installation on 2026-10-12, on production running the
published 0.3.0 with replacement turned on: a film's old copy moved into the quarantine and came
back with Restore, a season's replacement stopped for the torrent that seeds its old copies and was
kept with Keep both, and three restarts, Jellyfin stopped during a check, the switch off, a folder
unticked, a member's view and a release that would land on a file already there were staged, each
leaving the old file where it was; a second replacement of the film, ticking both its copies, was
cancelled at 1.8 % of its download, so a held copy moving after Retry and Delete for good weren't
tried and rest on the tests, and production then went back to the image's own ids with replacement
switched off. Preparing that check found that check 6 passed over a download client whose path
mappings were changed after a replacement went through it, and that the release note's Turning it on
missed the compose file's `user:` line and a backup; both were fixed on 2026-10-12 under 0.3.0,
which keeps its number (Vasyl's decision). MAM-39 (built the same day, inside 0.3.0 by Vasyl's
decision) then replaced that `user:` line and the `chown` of the data volume by hand with `PUID` and
`PGID`: the image starts as root only to hand its data directory over to them, then drops to them
with no capability before the app starts (ADR 0036), and the Media folders page names whose folder
the app can't write to and which `PUID` and `PGID` to set. MAM-40 (built the same day, inside 0.3.0
by Vasyl's decision) follows from production's four groups of one work's copies, which the Duplicates
page could only show: the owner removes one copy into the quarantine there, through replacement's
switch, ticks and checks, restores it or deletes it for good under Removed copies, or keeps the
copies as they are, and the groups have plain names (ADR 0037). MAM-41 (built the same day, inside
0.3.0 by Vasyl's decision) sorts the Jobs page into tabs by what waits for the user — Needs you, In
progress, Finished and All, worked out and counted on the server —, keeps only verbs under a row's
Actions, Restore and Delete for good now among them, and opens a job's details, the replacement's
old copies included, under its row. MAM-42 (built on 2026-10-13, inside 0.3.0 by Vasyl's decision,
a Phase B ticket) follows from the Library page's Sources card warning of a file that couldn't be
read with Details closed and not with them open: every warning now sits on the item whose row holds
it, the files in no library are listed in a dialog with why a library most likely doesn't list each,
and the owner ignores the ones that aren't media, which the app then stops counting, nothing changed
on the disk (ADR 0021's MAM-42 section). MAM-43 (built the same day, inside 0.3.0, a Phase B ticket)
follows from that list: 16 of its 17 files were four Blu-ray clips of films Фільми does list and twelve
shorts in `Shorts` folders, all with one vague reason. A disc's clips are now its film's files, listed by
the sync (ADR 0038), and the list names the extras folder or extra's name that made a library skip a file.
MAM-44 (built the same day, inside 0.3.0, a Phase B ticket) follows from production's list after MAM-43,
which still warned of those twelve shorts, which Jellyfin shows nowhere: the files a library takes for
extras sit on an Extras tab and warn of nothing.
All eight go out when Vasyl publishes 0.3.0 again — the image published on 2026-10-11 has none of
them. Phase C's heading below stays
unmarked until that image is published.
This file lists unshipped phases only. When a phase ships, its heading becomes
`### ✅ Shipped (<release>) Phase …`, and its detail later moves to `HISTORY.md`. The phases
follow §6 of the
[development specification](docs/Media_Acquisition_Manager_Spec.md#6-implementation-phases-and-acceptance-criteria);
this file adds the ticket breakdown, the tests and the acceptance bar. Phase A is tracked as tickets
MAM-1 to MAM-16 in the Kaneo project `MAM`, Phase B as MAM-17 to MAM-22, MAM-32 to MAM-38 and
MAM-42 to MAM-44, Phase C as MAM-23 to MAM-28 and MAM-39 to MAM-41, Phase D as MAM-29 to MAM-31.
Release 0.3.0 holds MAM-24 to MAM-27, and
MAM-37 and MAM-38, built while 0.2.1 was the version; MAM-28's fixes and MAM-39 to MAM-44 go
out when 0.3.0 is published again, under the same number. Phase D is release 0.4.0, with
Transmission instead of qBittorrent and Jackett instead of Prowlarr (Vasyl's decisions of
2026-10-13, MAM-29).

## Phases

### Phase A — Vertical slice without deletion

**Release:** 0.1.0. **Tickets:** MAM-1 to MAM-16.

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
  confirmation), owner-only settings writes by namespace convention, and an audit trail. MAM-16
  added a Profile page where every user changes their own password with the current one, which ends
  their other sessions and keeps the one it was made from
  ([ADR 0016](docs/adr/0016-a-users-own-password-change-keeps-the-session-it-was-made-from-and-ends-the-others.md)).
  Two-factor sign-in and password reset by email are out of scope.
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
  open and are listed under Phases B and C (MAM-17 has since answered 3 and 6 for Phase B; their
  replacement side is MAM-23's).
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
  the search contract to Torznab's, MAM-11 the destinations of the download contract to
  qBittorrent's, MAM-13 the submission and the lookup of jobs by info hash, and MAM-14 the files
  of a job and the jobs of a destination.
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
  the reason, and will keep automatic replacement off in Phase C (MAM-24 since: such a destination
  takes no replacement in the Download dialog)
  ([ADR 0003](docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md)). The
  qBittorrent adapter (4.4.1 or later) lists the categories and works each one's folder out the way
  qBittorrent does, a Windows host's network folders included, keeping one session per integration
  and signing in again once when qBittorrent has forgotten it, but never again with credentials
  qBittorrent refused, or after it banned the app's address, until the address or credentials change
  or a connection test with them works
  ([ADR 0007](docs/adr/0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md));
  for the destinations it sends only the sign-in and GETs
  (`DestinationsClientDestinationsHttpTests.Every_call_the_destinations_make_to_a_client_is_the_sign_in_or_a_get_and_none_names_a_save_path`).
  Submission and job monitoring, once listed here, moved to MAM-13 and MAM-14 with their members of
  the download client contract, which MAM-11 left out rather than stub
  ([ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)).
  Only qBittorrent 5.2 was verified live, through the answers recorded from the reference
  installation and through the app; 4.4.1 to 5.1 are accepted from qBittorrent's source. Verified
  live on 2026-10-01: qBittorrent v5.2.4's connection test and its four categories, and the
  destination Movies landing in the Jellyfin library "Фільми" at `/media/movies`.
- **MAM-12 Unified search page** (built): every signed-in user searches by title — required, with a
  year, an IMDb id and a TMDb id in fields of their own (owner's choice) — and the library and every
  enabled release source are asked at once, the library through a database context of its own.
  MAM-16 moved the title to a search field in the header: Enter opens `/search?q=…`, which makes the
  search once and becomes `?id=` (ADR 0011), a year in brackets ending the line becomes the year
  ([ADR 0015](docs/adr/0015-only-a-bracketed-trailing-year-in-the-header-search-becomes-the-year.md)),
  and the page keeps the year and the ids to narrow the search on screen.
  **In your library** lists every work of the snapshot MAM-8 imported that matches, strongest first:
  by identifier, then by original or localised title with its year, then by title alone as a
  candidate to check, each with its versions and the facts MAM-9 verified, the id or title that
  matched with the source that reported it, and a conflict of ids flagged, never merged; then,
  marked as no match, at most 20 works one of whose titles or file paths holds every word of the
  title searched for, with how many more there are and a link to the Library page searched for the
  same text
  ([ADR 0012](docs/adr/0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md)).
  **Available releases** lists every release found, even for a movie already owned, its claimed
  quality marked as claimed, with every work of **In your library** it matches — the works listed
  by words too, though never by words — by the title and year read from its title and by the IMDb or
  TMDb id its listing gives (owner's choice; Toloka gives none), and each source's own status, a
  failed one never hiding the others. The search is stored with how each source answered and every
  release, so the server's filters (source, audio language, quality, size), sorts and pages read
  what it stored and a reload asks no source again; a search is its creator's and lives a day, as
  long as its releases
  ([ADR 0011](docs/adr/0011-a-search-is-stored-once-and-every-filter-sort-and-page-reads-what-it-stored.md)).
  Each release has Details (its listing and claims beside the matching copies' verified facts), Open
  source and Download, which MAM-13 made send the release. MAM-10's
  `POST /api/releases/search` stays as it was (owner's choice), and both searches ask and store
  through one implementation; `IMetadataResolver` stays unimplemented, as the snapshot needs no call
  to Jellyfin (MAM-33 implements it with TMDb, for recommendations only). The HTTP tests cover the
  concurrent asking
  (`SearchesConcurrencyHttpTests.Create_asks_the_library_and_every_source_at_once`), one failing
  source (`SearchesFailingSourcesHttpTests.Create_with_one_failing_source_is_201_with_its_error_beside_the_other_sources_answer`),
  the filters and sorts (`SearchReleasesListHttpTests.Releases_filters_and_orders_the_searchs_releases_on_the_server`),
  matching by id against title and year
  (`SearchReleasesMatchingHttpTests.Releases_lists_every_work_of_the_search_a_release_matches_strongest_first`,
  `SearchesLibraryMatchingHttpTests`), a movie in a `Collection` folder found where it is
  (`SearchesLibraryMatchingHttpTests.Create_finds_a_movie_inside_a_collection_folder_with_each_version_where_it_is`),
  and the works holding the words of the title, one only through its file's path
  (`SearchesLibraryMatchingHttpTests.Create_lists_the_works_holding_every_word_of_the_title_after_the_matches`,
  `SearchReleasesMatchingHttpTests.Releases_are_never_matched_to_a_work_by_words`).
  The page was checked in a browser at desktop and phone width against fake services; MAM-13,
  MAM-15 and MAM-16 then used it against the reference installation.
- **MAM-13 Send to client** (built): every signed-in user sends a release of their own search to a
  download client from the Search page's **Download** dialog (MAM-13's decision 1) — a client that
  can take it, one of its destinations with the default picked first, and a summary that says the
  existing copies are kept. The server fetches the release itself and hands qBittorrent (4.4.1 or
  later) the `.torrent` or the magnet in one `torrents/add` with the destination's category exactly
  as stored and automatic torrent management, never a save path (decision 2), after confirming the
  category live and that qBittorrent doesn't already have the torrent, which fails the job rather
  than adopt a download the app didn't add (decision 4)
  ([ADR 0010](docs/adr/0010-a-destination-is-a-download-clients-own-category-never-a-folder.md)).
  One click creates one job: the job is written with the request id the dialog made and an audit row
  before anything is fetched or sent, the same id answers that job and sends nothing, two copies
  racing make one job, and a submission is never sent again — an answer that leaves open whether
  qBittorrent took it is followed by reading its list. The job is confirmed by qBittorrent's own id
  (5.2) or by the info hashes worked out from the payload (4.4.1 to 5.1, or 5.2 naming no single
  id); several candidates, or no hash, are `NeedsLinking`, never a guess. A release a job that didn't
  fail already sent to that client — recognised by its source's id for it or its info hash, as every
  search keeps its own copy of a listing — is refused (decision 3)
  ([ADR 0013](docs/adr/0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md),
  [ADR 0006](docs/adr/0006-every-title-identifier-and-file-the-core-stores-names-its-source.md)).
  The contract's `SubmitAsync` came back, and `FindJobsAsync` took the place of `ListJobsAsync`
  ([ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md));
  the release's download link and a magnet's trackers stay on the server
  ([ADR 0004](docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md),
  [ADR 0007](docs/adr/0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).
  The HTTP tests cover the job, its row and its audit row
  (`DownloadsSendHttpTests.Create_is_201_with_the_job_the_client_took_its_row_and_its_audit_row`),
  the one add into the category with automatic management and no save path
  (`DownloadsSendHttpTests.Create_sends_the_torrent_once_into_the_category_with_automatic_management_and_no_save_path`),
  a qBittorrent before 5.2
  (`DownloadsSendHttpTests.Create_against_qbittorrent_before_5_2_finds_the_job_by_its_info_hash`),
  one request id sent twice and twice at once
  (`DownloadsIdempotencyHttpTests.Create_twice_with_one_request_id_answers_the_same_job_and_sends_once`,
  `DownloadsIdempotencyHttpTests.Create_twice_at_once_with_one_request_id_makes_one_job_and_sends_once`),
  the id used for another download
  (`DownloadsIdempotencyHttpTests.Create_with_a_request_id_used_for_another_download_is_409_and_writes_nothing`),
  a release already sent
  (`DownloadsIdempotencyHttpTests.Create_of_a_release_already_sent_to_the_client_is_409_and_sends_nothing`),
  the refusals that ask no service
  (`DownloadsValidationHttpTests.Create_to_a_client_that_can_not_take_the_release_is_400_and_asks_nothing`,
  `DownloadsValidationHttpTests.Create_to_an_unknown_destination_is_404_and_asks_nothing`), the
  failures and their confirmation
  (`DownloadsSendFailuresHttpTests.Create_whose_category_is_gone_fails_and_sends_nothing`,
  `DownloadsSendFailuresHttpTests.Create_for_a_torrent_qbittorrent_already_has_fails_and_sends_nothing`,
  `DownloadsSendFailuresHttpTests.Create_whose_add_answered_500_but_listed_the_torrent_records_it_submitted`,
  `DownloadsSendFailuresHttpTests.Create_with_two_jobs_listed_for_the_hash_needs_linking`), the
  dialog's choices
  (`DownloadOptionsHttpTests.Options_lists_the_switched_on_clients_that_take_the_release_with_their_destinations`)
  and the secrets
  (`DownloadsSecretsHttpTests.No_answer_log_job_or_audit_row_of_a_download_carries_a_planted_secret`).
  Verified live on 2026-10-02: a release found on the Search page went through Prowlarr to the
  reference installation's qBittorrent v5.2.4, which listed it under the info hash the app worked out,
  in the Movies category's folder under automatic torrent management and with no save path sent; a
  first try that Prowlarr failed to fetch from Toloka ended `Failed` with the feed's sentence and sent
  nothing. The page was checked in a browser at desktop and phone width against local fakes.
  qBittorrent's answers to `torrents/add` before 5.2 are written from its source.
- **MAM-14 Job tracking** (built): a background pass reads every followed job from qBittorrent every
  10 seconds, after the previous pass ends, and writes what changed to the job's row, so nothing is
  kept in memory and a restart goes on where the rows are. A job is found by the id or the info hashes
  MAM-13 stores on it, one lookup per client and pass, 50 ids to a request; one its ids no longer find,
  sent as a magnet naming its v1 hash only, among its destination's torrents
  (`torrents/info?category=`), closing ADR 0013's gap of the hybrid qBittorrent renames. Its status
  follows qBittorrent's — `Submitted`, `Downloading`, the new `Paused`, and `Downloaded`, which ends the
  following, only for a finished state with a progress of 1 — with its progress and, for a job whose
  status or progress moved, its files (the contract's `ListJobFilesAsync` came back, and
  `ListDestinationJobsAsync` is new,
  [ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)).
  A send a stop interrupted, left `Created`, is looked for once it is 5 minutes old: followed when
  qBittorrent has it, `NeedsLinking` when it lists it twice, `Failed` with why otherwise. A client
  error (MAM-14's decision 2), a torrent qBittorrent no longer lists (decision 3) and a client that
  can't be asked keep the job's status with a block reason and keep it followed; the reason goes by
  itself once its cause does, and a client's own sentence is the owner's to read. Cancel only stops
  following: a conditional update to the new `Cancelled` with an audit row, nothing sent to
  qBittorrent, as the contract has no member that removes, stops or pauses, and a cancelled job's
  release may be sent again
  ([ADR 0014](docs/adr/0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md),
  [ADR 0013](docs/adr/0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).
  The **Jobs** page (in the header, for every signed-in user) lists the caller's own jobs, the owner's
  too (decision 1), newest first, filtered by status and paged on the server, with the status, the
  reason or the error, the progress and **Cancel job**, and refreshes every 10 s while it shows a
  followed job; a job's page adds its facts and its files. The HTTP tests cover each of qBittorrent's
  states (`DownloadJobTrackingHttpTests.Poll_maps_what_qBittorrent_reports_onto_the_job`), the end of
  the following
  (`DownloadJobTrackingHttpTests.Poll_of_a_finished_download_ends_its_following_and_never_asks_about_it_again`),
  the files
  (`DownloadJobTrackingHttpTests.Poll_stores_a_jobs_files_and_reads_them_again_only_when_the_job_moved`),
  the restart and the interrupted sends
  (`DownloadJobRecoveryHttpTests.Poll_after_a_restart_follows_the_jobs_a_previous_run_left_and_asks_about_no_ended_one`,
  `DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_qBittorrent_took_follows_it_from_then_on`,
  `DownloadJobRecoveryHttpTests.Poll_finds_a_hybrid_torrent_qBittorrent_renamed_after_its_magnet_through_its_destination`),
  a torrent missing from qBittorrent and a client that can't be asked
  (`DownloadJobMissingHttpTests.Poll_of_a_torrent_removed_from_qBittorrent_keeps_the_job_with_why_and_follows_it_until_it_comes_back`,
  `DownloadJobMissingHttpTests.Poll_of_a_client_that_can_not_be_asked_never_makes_a_job_missing`),
  cancel never reaching qBittorrent
  (`DownloadsCancelHttpTests.Cancel_sends_nothing_to_qBittorrent_and_no_later_pass_asks_about_the_job`,
  `DownloadsCancelHttpTests.Poll_racing_a_cancel_never_undoes_it`,
  `DownloadClientNeverRemovesTests.No_download_client_member_is_named_for_removing_deleting_stopping_or_pausing`),
  and the list and the files of one's own jobs only
  (`DownloadsListHttpTests.List_of_the_installation_owner_holds_only_their_own_jobs`,
  `DownloadJobFilesHttpTests.Files_of_another_users_job_are_a_bare_404_even_for_the_installation_owner`).
- **MAM-15 Phase A acceptance** (done): the acceptance bar below, checked on 2026-10-03 against the
  reference installation — Jellyfin 12.1.0, Prowlarr 2.6.5 serving the Toloka.to indexer, and
  qBittorrent 5.2.4 — through the app run from the repository. The header search "Sherlock Holmes
  (2009)" found the movie in `/media/movies/Guy Ritchie. Collection` where it is, beside Toloka's 12
  releases. With the Torznab feed pointed for one search at an indexer Prowlarr doesn't have, the
  Search page still showed the library's match and said under Available releases what Prowlarr
  answered (404); the address was then put back. MAM-13's request of 2026-10-02, sent again with its
  request id, answered with the same job, and the same Toloka release from a new search was refused
  as already sent: still two jobs and two `DownloadRequested` audit rows, and nothing added to
  qBittorrent. The destination Movies is qBittorrent's category `Movies`, which saves into the
  Jellyfin library "Фільми", where MAM-13's send landed with no save path. A library sync afterwards
  listed each of the 866 files of the 2026-10-01 sync at the same path with the same size. What was
  tried live, and what wasn't, is in the README's [Tested with](README.md#tested-with) and the
  release note.
- **MAM-16 Approved layout** (built): the web app takes the layout the owner approved on
  2026-10-02 (`docs/design-system/design_handoff_approved_layout/README.md`), with the ticket's plan
  deciding where the brief is wrong about the code. The header stays at the top of the window and
  holds a search field in place of the Search link: `/` jumps to it, and Enter opens
  `/search?q=…`, which makes the search once and becomes `?id=`
  ([ADR 0011](docs/adr/0011-a-search-is-stored-once-and-every-filter-sort-and-page-reads-what-it-stored.md));
  only a year from 1900 to 2099 in brackets ending the line becomes the year, so "Blade Runner 2049"
  stays a title (owner's choice,
  [ADR 0015](docs/adr/0015-only-a-bracketed-trailing-year-in-the-header-search-becomes-the-year.md),
  with a section added to
  [ADR 0012](docs/adr/0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md)),
  and the page keeps the year and the ids to narrow the search with **Apply**. Every paged list shows
  the range on screen ("26–50 of 812", from its `totalCount`) and numbered pages. A library card
  shows its versions as rows, or from two versions side by side in a table, one choice per browser
  for Library and Search and always rows below 48rem (owner's choice). The user menu says who is
  signed in, with their role, the version and the server's readiness, and holds **Profile**, the
  owner's settings, a Light, Dark or System theme kept in the browser, and **Sign out**. Every failed
  request shows one of a few error states: a box with the server's sentence and **Try again** where
  a first load failed, a banner with **Retry** over the data kept on screen, a banner that
  **Dismiss** clears for a refused action, a banner above a form's buttons, a sentence under the
  field it is about, and a strip under the header while the server can't be reached, which asks
  `GET /api/health` every 5 seconds until it answers; red text takes a `--text-danger` token that
  reaches the 4.5:1 text needs in the dark theme too. On the Users page the Add user and Reset
  password dialogs place each refusal (two passwords that differ under Confirm password, a taken
  name under User name, every other server sentence in a banner above the buttons, with no client
  copy of the server's rules), and a reset that goes through says so in a dismissible ok banner
  above the list (`CreateUserDialog.test.tsx`, `ResetPasswordDialog.test.tsx`,
  `UsersPage.test.tsx`). The **Profile** page shows the account, when it was created and last
  signed in and when this session ends — the new `createdUtc`, `lastSignInUtc` and
  `sessionExpiresUtc` of `GET /api/auth/me` — and lets every user change their own password with
  the current one through `POST /api/auth/password`: the user's other sessions end at once and this
  one stays, a wrong current password is a 400, never a 401, a `UserPasswordChanged` audit row is
  written, and 10 attempts a minute are allowed per user, counted apart from sign-in
  ([ADR 0016](docs/adr/0016-a-users-own-password-change-keeps-the-session-it-was-made-from-and-ends-the-others.md),
  with a section added to
  [ADR 0002](docs/adr/0002-every-authenticated-request-checks-its-session.md)); the web app
  refreshes an expired access token for these two calls as for any other. The HTTP tests cover the
  sessions a change ends and keeps
  (`AuthPasswordHttpTests.ChangePassword_keeps_the_calling_session_and_ends_the_users_other_sessions_at_once`),
  a wrong current password
  (`AuthPasswordHttpTests.ChangePassword_with_a_wrong_current_password_is_400_with_the_sentence_and_changes_nothing`),
  an owner's reset landing during the check
  (`AuthPasswordHttpTests.ChangePassword_overtaken_by_a_change_to_the_user_during_the_password_check_is_400_and_the_owners_change_stands`),
  the limit per user
  (`AuthPasswordHttpTests.ChangePassword_is_rate_limited_per_user_whatever_the_client_address`) and
  the end of the calling session
  (`AuthMeHttpTests.Me_says_when_the_calling_session_ends_not_another_session_of_the_user`). The web
  app's tests cover the header search and its year (`HeaderSearch.test.tsx`,
  `search-query.test.ts`, and `SearchPage.test.tsx`'s "makes the search in ?q= once, though
  StrictMode runs the page's effects twice" and "goes Back from a search made from ?q= to the page
  before it, never to the ?q= that would ask again"), the pager (`ListPager.test.tsx`,
  `page-numbers.test.ts`), the versions (`LibraryWorkCard.versions.test.tsx`), the theme
  (`theme-store.test.ts`), the error states and the strip (`ErrorState.test.tsx`,
  `use-load-retry.test.tsx`, `OfflineStrip.test.tsx`, `connection-store.test.ts`) and the Profile
  page (`ProfilePage.test.tsx`, `ChangePasswordCard.test.tsx`, and `api.test.ts`'s "refreshes once
  and retries once after a 401 from GET /api/auth/me" and "… POST /api/auth/password"). The pages
  were checked in a browser at 1280, 375 and 320px, light and dark: a fresh database for the setup,
  Users dialogs, Profile password change and the strip (with the API stopped), and the live library
  and Toloka search for the versions view and the Search page. System following the operating system
  without a reload was not seen, because the hidden browser pane sends no change events.
- **Backup/restore:** nothing to add. The whole state is the data directory (`mam.db`, which holds
  the accounts, sessions, integrations, audit trail, imported library and its sync runs too, and
  `.secrets/`, whose encryption key alone opens the stored credentials); copy it while the app is
  stopped. A lost library snapshot comes back with the next sync, and the files' inspection with
  the next inspection; the media folders the owner allowed are in `mam.db` too, and so are the
  download destinations and the library folders the last sync stored (those come back with the next
  sync); qBittorrent's session, and a sign-in it refused, are held in memory only. The download jobs
  live in `mam.db` too, each with the request id it was sent under, the info hashes (v1 and v2) of
  what it sent, its status, progress and block reason as the last pass read them, and its files as
  qBittorrent last listed them; the jobs still followed catch up with the next pass after a
  restore, and the torrents themselves are qBittorrent's to back up. The releases a search found
  are kept there for a day, and those a download job sent for as long as the job; the Search page's
  searches, with how each source answered, for a day too. MAM-16 adds nothing to back up: the
  theme and the way versions show are kept in each browser (`mam:theme`, `mam:versionsView`), not
  in the data directory. A secret set through
  `MAM_Encryption__Key` or `MAM_Jwt__Secret` lives in your environment instead of `.secrets/`, so
  back it up there.

**Out of scope:** removing, moving or replacing any library file (Phase C); duplicate detection
(Phase B); a second search or client adapter, Plex and Emby (Phase D); TV shows (Phase B, where
MAM-17 answered specification §7 question 3 and MAM-32 imports them and searches and sends their
releases).

**Tests:** every endpoint over real HTTP, with vendor APIs faked at the network seam; a failing
source returns its own error while the other sources still answer; submitting the same release
twice creates one job; a destination maps to its category and never sends a conflicting save
path; a job follows the client's snapshots, survives a restart and a torrent missing from the
client, and its cancel never reaches the client; a user's own password change ends their other
sessions at once, keeps the one it was made from, loses to an owner's reset that lands during it
and is limited per user (`AuthPasswordHttpTests`); no Phase A code path writes to a media root.

**Acceptance bar:** a movie in a `Collection` folder can be found without moving it; Toloka (or
another indexer) appears in search when its integration actually works, and a Toloka failure shows
an explicit source-specific error; retrying the same command does not create duplicate jobs; the
category determines the expected destination without an overriding path. Met on the reference
installation on 2026-10-03 (MAM-15), with no existing file moved, changed or removed.

### ✅ Shipped (0.2.1) Phase B — Scanner and duplicates

**Release:** 0.2.0, and 0.2.1 for MAM-33 to MAM-36 (Vasyl's decisions) and for MAM-20, MAM-21,
MAM-37 and MAM-38, built while 0.2.1 was the version; MAM-42 to MAM-44, built on 2026-10-13, go out in
0.3.0 when it is published again (Vasyl's decision). **Tickets:** MAM-17 to MAM-22, MAM-32 to MAM-38 and
MAM-42 to MAM-44.

**Complexity:** Medium
**Value:** see which files are true duplicates, which are other versions of the same movie or
episode, and how much space could really be recovered — without the app guessing.

**Scope:**

- **MAM-17 Media types and file origins** (done): specification §7 questions 3 and 6 answered
  against the reference installation on 2026-10-04. Duplicate detection covers movies and episodes
  in every library (Vasyl's decision). An episode is a work known by its own IMDb and TVDB ids and
  by its series' ids with its season and episode numbers, never by Jellyfin's series item (The Grand
  Tour is four), and the library alone says whether an item is a movie or an episode: the 38
  episodes Jellyfin lists as movies (The Continental, Greatest Events of World War II in Colour,
  Planet Earth I–III) stay movies, as the library stays as it is
  ([ADR 0017](docs/adr/0017-an-episode-is-a-work-known-by-registry-ids-and-the-library-alone-says-what-an-item-is.md)).
  No file on the media disk has a second name: 822 of the 1394 video files in the library folders
  (4.07 of 7.31 TiB) are a seeding torrent's own data and 571 belong to no torrent; Radarr, which
  Vasyl doesn't use, has never imported a file; and no two of those video files have the same size.
  A seeding file is a duplicate like any other, so Phase B reads no download client (Vasyl's
  decision, [ADR 0018](docs/adr/0018-a-file-is-a-duplicate-whatever-a-torrent-client-does-with-it-and-its-space-counts-once-per-identity.md)).
  Jellyfin's real answers for series, seasons and episodes are new adapter samples.
- **MAM-32 Series and episodes, and their releases** (built): a sync imports Jellyfin's series and
  episodes from every library beside its movies, reading three lists (movies, series, episodes) each
  with its own checks. A series is a work holding the show's titles and IMDb, TMDb TV and TVDB ids
  and no version; an episode is a work with versions and files, placed in its series by a position
  per library item (series work, season, episode); the seasons are read from the episodes' numbers,
  and an episode Jellyfin lists without a file is left out (Vasyl's decisions 1 and 3,
  [ADR 0019](docs/adr/0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).
  The Library page shows a series as one card with collapsible seasons whose episodes load a page
  at a time (`GET /api/library/works/{id}/episodes`), an episode of no series as a card of its own,
  and a Movies/Series filter (Vasyl's decisions 2 and 6). The release search asks every source for
  TV beside movies — the title without the year in its TV categories, a season where asked — and a
  release is TV by its category, in TV/Anime and TV/Documentary by a season or episodes marker since
  MAM-33; a TV title is read for its seasons, episodes and span of years, a
  TV release matches library series only and a movie release movies only, and each TV match counts
  the episodes held of what it claims; the Search page lists series in **In your library**, has a
  Season field and a Movies/TV filter (Vasyl's decisions 4 and 6). The owner maps a release source's
  sections to destinations, and the Download dialog opens on the destination the release's section
  is mapped to, else the default (Vasyl's decision 5,
  [ADR 0020](docs/adr/0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
- **MAM-18 Scan of the media folders** (built): the owner's **Scan folders** on the Library page
  (`POST /api/library/scans`, owner-only, 202 + run row + reconciler) walks every allowed folder for
  media files — a video extension, letter case ignored, Blu-ray and DVD clips included — following a
  link only when it ends inside the folders, never one leading out, ending loops, and opening no
  file; on Linux each folder is opened name by name without following a link and listed through its
  descriptor. A folder it couldn't list now keeps the names found there before, and a walk past
  100,000 folders or 200,000 entries stores nothing. A file is reported when one of its names is
  where the app finds a library's file, a movie's or an episode's, through the current path
  mappings, by path alone; each file no library reported keeps one name with no version, kept across
  scans, and the usual file inspection then reads it (`FolderScanned`). A **Media folders** row
  beside the Jellyfin sources says how the scan stands and counts the files in no library by the
  app's look at each, counted when read, so a sync that starts reporting a path takes it off at once.
  It runs only when the owner asks and reads no download client (Vasyl's decisions 1–4,
  [ADR 0021](docs/adr/0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md),
  [ADR 0009](docs/adr/0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)).
  Checked on the reference installation on 2026-10-05, on a copy of its data beside production:
  1,395 media files in `/media`, the 1,378 Jellyfin lists each matched by path and 17 in no library,
  and a second scan read nothing new.
- **MAM-19 Hardlinks counted once and lazy hashing** (built): the Library page says how much room
  the files take on disk — in every Jellyfin source's row, in the Media folders row and, for every
  stored file name, in the file inspection's row — counting a file once per device and inode however
  many names lead to it, with the size the app read and never the library's, and the names it hasn't
  measured apart (Vasyl's decision 2, [ADR
  0018](docs/adr/0018-a-file-is-a-duplicate-whatever-a-torrent-client-does-with-it-and-its-space-counts-once-per-identity.md)).
  Every file inspection ends by reading whole, for its SHA-256, each file whose size a file of
  another identity shares and whose media facts — duration, tracks, codecs, picture size, channels,
  languages — don't tell the two apart, one at a time (Vasyl's decisions 1 and 3), inside the
  allowed folders and from the file the checks opened. The hash is kept under the file's identity,
  size and modification time like MAM-9's readings, so the file is read again only when one of them
  changes, and is forgotten with the last name and reading of its bytes; a file that changed while
  it was read keeps nothing, and a file it can't read is a problem with its sentence, never a
  failure. No run of its own: a restart's run answers the stored hashes from the cache and reads the
  rest. The inspection's row follows the hashing, and no download client is read ([ADR
  0022](docs/adr/0022-a-file-is-hashed-only-when-a-file-of-another-identity-shares-its-size-and-could-be-its-copy-as-the-last-step-of-the-inspection-one-at-a-time.md),
  [ADR
  0009](docs/adr/0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)).
  Checked on production on 2026-10-08 (MAM-22): the 1,397 files, each with one link and a size no
  other file shares, take 7.34 TiB, the 7.25 TiB of Jellyfin's libraries and the 86.7 GiB only the
  scan found adding up to it to the byte, and the inspections hashed nothing.
- **MAM-33 Recommendations** (built, release 0.2.1): a Recommendations page in the header suggests
  the films, series, cartoons and documentaries the library doesn't have yet, in four tabs — Movies,
  Series, Cartoons, Documentaries — by their catalog genres (Vasyl's decision 2), each with every
  reason as a chip: a collection's missing released parts ("2 of 3"), the other works of a director
  or creator two or more library works are by, the catalog's recommendations weighted by how many
  library works point at them, a held series' aired seasons with no episode, what a release source
  just published, and for a tab with few library works the best-rated works a source carries. The
  order is a fixed score of those reasons; a rating floor applies to reasons of taste only; a work not
  released yet, one the library holds by id or by title and year, and one a scanned file's name
  claims are left out ([ADR
  0023](docs/adr/0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)). The
  catalog is TMDb under the owner's own token, a new kind of integration, Metadata (Vasyl's decision
  1, [ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md));
  its answers live in tables of their own with their provenance, are read again after 30 days and are
  never written to the library ([ADR
  0025](docs/adr/0025-the-catalog-is-kept-apart-from-the-library.md)). A background run (202 + run row
  + reconciler) computes the lists after every successful sync, when TMDb is connected, on the
  owner's **Recompute now**, when someone switches on a tab nobody had on and when a source's newest
  releases resolve to new works. Toloka is asked about the recommended works within each user's daily
  limit (60 by default, 0 for none), split between their tabs, at most 4 searches every 15 minutes, the
  result shared by every user and checked again after a day when found and a week when not, and its
  newest 90 releases are read every 3 hours for "New on Toloka" ([ADR
  0024](docs/adr/0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)). A
  card's **Releases** opens the Search page with the work; nothing is sent by itself. "Not interested"
  is each user's own (Vasyl's decision 3), and each user's Profile switches the tabs and sets the limit,
  every tab on until they save (decision 5). A film Toloka files in TV/Anime or
  TV/Documentary is a movie unless its title or description names seasons or episodes, and the movie
  search asks those two categories too, which fixes the ordinary search for documentary and anime films
  (decision 6, [ADR
  0020](docs/adr/0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)
  amended); each release keeps Toloka's own topic title. Checked live on 2026-10-05: the app's
  container reaches TMDb's API and image host; Prowlarr answers Toloka's latest 90 releases with no
  paging; with Vasyl's token the library's 640 TMDb movie ids took 943 calls and about 3 minutes, with
  no rate limit met — 279 movies in 118 collections, 42 of them incomplete with 63 released parts
  missing, 138 directors with two or more films, and, by a script's estimate, 2,274 film candidates
  over a rating floor with a collection, a director or two recommending films behind them. TMDb was
  then connected in the app with Vasyl's token, and the computation adding it queued took 3 min 15 s:
  815 seeds (640 by TMDb id, 169 found by title and year or IMDb id, 6 shows) and 3,634 candidates
  (Movies 2,852, Cartoons 545, Documentaries 135, Series 102), 109 of them top-rated fallbacks waiting
  for a release source to carry them; the missing seasons of How the Universe Works (3–11), The Grand
  Tour (5–6) and MobLand (2) are recommended, and so is Planet Earth, whose episodes the library holds
  as movie items with no id (MAM-34 links them). That run's order changed the score — a rating weighed
  by its votes, the recommenders by their logarithm — and the run's progress is now shown, as Vasyl
  asked. The checks of Toloka for the recommended works, the app's reading of its newest releases and
  a download from a recommendation are still to be tried live.
- **MAM-34 Link a work to TMDb by hand** (built, release 0.2.1): the owner links a library work — a
  film, a series, or an episode of no series — to its TMDb film or show from the **TMDb…** button on
  its Library card: one of up to five suggestions a background run (202 + run row + reconciler)
  prepares after each sync, ranked by evidence the owner reads — the title, the year, the runtime
  against the duration ffprobe measured, the library's genre, a linked neighbour's collection,
  director or show —, a pasted TMDb or IMDb link or id, or a TMDb search. A link is kept per library
  item in a table of its own, with the owner as its source; every sync re-applies it, and it follows
  its item's files when Jellyfin re-creates the item. A linked work counts the id as its own on the
  Library and Search pages, in release matching and in the recommendations. **Jellyfin identifies
  the item as that film or show** (Vasyl's decision of 2026-10-06, which replaced his first decision
  of 2026-10-05, ids only, and the lookup he added the next morning): the app does what Jellyfin's
  own **Identify** button does — a search by the id in the item's own kind of provider, never the
  other kind's, then one `RemoteSearch/Apply` with `replaceAllImages=true`, and the item read again
  to confirm it — so Jellyfin replaces the item's title, overview and images with TMDb's, in the
  language of the item's library, which is that library's own setting and not the app's choice.
  Nothing waits inside a request: **Save** queues the link, and a background worker identifies it,
  two films at a time and one series at a time, a series taking minutes; the dialog says what will
  happen before **Save**, then "Waiting to identify", "Identifying…" and "Identified in Jellyfin",
  or "Not identified yet" with the reason and **Retry**. A library that saves artwork or metadata
  next to the media is refused before any search, and no file or folder is ever touched. An episode
  Jellyfin lists as a film, linked with a season and an episode, is identified through The Open
  Movie Database by the episode's own IMDb id, which TMDb gives — on the live check its title and
  year were corrected, its overview kept and its image replaced by what is probably a frame of the
  video —, or it stays in the app with its reason; an episode item stays in the app too, as Jellyfin
  identifies movie and series items only. **Remove link** forgets the link in the app only: Jellyfin
  keeps what Identify set, and replacing a link is a new Identify. The links set under the first
  rules, which only put ids on items, are repaired by the owner's **Update Jellyfin** (`GET`/`POST
  /api/library/tmdb/catch-up`): a count of the links not identified yet, one confirmation, the
  batch's progress, a protection that refuses an item holding another IMDb or TMDb id than its
  link's, and the **Not identified** tab that lists every link Jellyfin or the app refused or failed
  to identify, with its reason. A TMDb links page reviews the works without an id one at a time by
  the keyboard, links every confident suggestion — the one exact title-and-year match whose runtime
  agrees — with one click, identified in the background, and lists the links not identified, the
  conflicts and the links whose item is gone. Owner-only, reads included ([ADR
  0026](docs/adr/0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md);
  ADR 0005, 0006, 0008, 0012, 0017, 0023 and 0025 amended). Checked live with Jellyfin 12.1.0: on
  2026-10-05 an item is read only as a user, so the app reads it as the first enabled administrator,
  and a film's ids written with the item as read answered 204 — the first decision's write, recorded
  as history —; on 2026-10-06 Vasyl's checks showed the ids alone changed nothing visible, and then
  the Identify check, in which he typed `y` for each apply: a search by a TMDb id answers one row,
  an id sent to the other kind's search finds an unrelated work, an apply answers 204 once it is
  done — 1.8 s for a film, 21.3 s for an episode through The Open Movie Database, 159.3 s for The
  Penguins of Madagascar with its 149 episodes — and replaces the item's title, overview and images,
  and the folder listings of the three items were identical before and after. The server's database
  held 229 links, 220 of them written by the first builds, and 217 of those items still showed the
  title their file name gave. TMDb gives "Landing Stripling" a runtime of 6 minutes, against 6.2 for
  Jellyfin's file, and names Planet Earth 1044 first. The number of confident suggestions in each
  library is read on the server after the release (Vasyl's decision 4).
- **MAM-35 Show each library work's own poster from Jellyfin on the Library and Search cards**
  (built, release 0.2.1): every card on the Library page, and under **In your library** on the
  Search page, shows the work's own picture beside its title block, in the 6rem box of a poster's
  2:3 shape the recommendations use: the Primary image Jellyfin holds for the item whose title the
  card shows, else for the next item that has one; a series' card the series' picture, the episodes
  of an opened season none, an episode of no series its own, and a film or TV icon where Jellyfin
  has none (Vasyl's decisions of 2026-10-06). It is never TMDb's poster from the catalog (ADR 0025);
  an item Jellyfin identified (MAM-34) shows the image Jellyfin took from TMDb then. Each sync reads
  each item's own image tag (`enableImages=true&enableImageTypes=Primary&imageTypeLimit=1`) and
  stores it as a fact of its work with its source; the app serves
  `/api/library/posters/{itemId}-{tag}.jpg` to every signed-in user, only for a tag a sync stored,
  fetching it once from the saved Jellyfin as a 342-wide JPEG with the key in the header and keeping
  it in `<data>/library-posters/`, and the page shows it as a `data:` URL, so the browser never
  contacts Jellyfin and the Content-Security-Policy is unchanged. A picture of another shape, such as
  a frame of the video, is shown whole, and nothing is written to Jellyfin ([ADR
  0027](docs/adr/0027-a-library-works-poster-is-the-librarys-own-image-read-by-the-syncs-tag-and-served-by-the-app.md);
  ADR 0005, 0006, 0007, 0008 and 0025 amended). Checked live and read-only with Jellyfin 12.1.0 on
  2026-10-06 and 2026-10-07: the image route answers without a key, `format=Jpg` makes it a JPEG and
  `quality=90` a 342x513 poster of 35 KB instead of 115 KB, the tag isn't checked and only versions
  the URL, an item without the image is 404 and an id Jellyfin doesn't know 400, and an episode's
  entry in the list carries its series' and season's tags beside its own. The items with a picture,
  read on 2026-10-07: 635 of the 650 movies of «Фільми»; all 194 movies, both series and all 284
  episodes of «Мульфільми», its 29 virtual episodes left out; all 35 movies, the one series and the
  16 episodes of «Документалки»; and all 8 series and 140 episodes of «Серіали». Checked end to end
  on 2026-10-07: a sync of the reference installation stored 1,315 poster tags (843 of the 858 movie
  works, all 7 series, all 432 episodes), and the Library page showed the pictures at 1280 and 375 px
  wide, each fetched once and kept. On 2026-10-08 (MAM-22) the header search "Alien (1979)" on
  production showed «Чужий» (1979) and «Чужий³» (1992) under **In your library** with Jellyfin's own
  posters, 342x513 JPEGs the app served, perhaps from its disk cache, so Jellyfin 12.2.0's image
  route is still unproven (`SearchesLibraryPostersHttpTests` pins the Search page's `posterUrl`).
- **MAM-20 Duplicates grouped into the four classes** (built, release 0.2.1):
  `GET /api/library/duplicates?class=&page=1&pageSize=25`, every signed-in user's, sorts the
  library's file names into specification §3.3's classes on every read, storing nothing
  ([ADR 0028](docs/adr/0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md);
  ADR 0012, 0017, 0018, 0021, 0022 and 0026 amended): the names of one device and inode, counted once
  and freeing nothing; files of two identities or more with one SHA-256; film and episode works of
  every library and integration sharing a registry key — an IMDb id whatever the kind, a TMDb or
  TVDB id within its kind, a series' id with the season and episode, a TMDb id or an episode-to-show
  link the owner set (MAM-34) — in two copies or more, never called redundant; and possible matches
  by a title and its own year that no kind, id or episode place keeps apart, a file only the scan
  found included by its file or folder name unless either names seasons or episodes. One item's 65
  id-less files are no copies (ADR 0017); ids that disagree stay one work flagged for review
  (`IdentifierConflict`, `EpisodeNumbers`; Vasyl's decision 1 of 2026-10-07); different editions —
  read from a word list in English and Ukrainian that decides no match (decision 2) — and different
  measured dynamic ranges are flagged; a possible match always needs review, with whether the
  measured durations agree. MAM-37 (below) changed two of these on 2026-10-08: two episodes of one
  show at different places are never one work, so `EpisodeNumbers` is gone, and copies whose names
  say different editions are no copies of each other, so `Edition` flags a possible match only. The
  room a copy frees is estimated only for byte-identical files whose file systems the mount table
  says share no blocks, with the reason otherwise (decision 3). A seeding file is classed like any
  other, and no download client is asked (ADR 0018). Read from the
  reference library's development database on 2026-10-07, before the design: no file identity,
  hash or measurement yet; 17 movies with several files, 9 of them different cuts and 3 a widescreen
  variant; 20 Penguins of Madagascar pairs and the S03E31/S03E33 pair under one episode's IMDb id;
  the eight Mickey Mouse Clubhouse halves; and no file the scan found. Checked on production on
  2026-10-08 (MAM-22), after **Refresh all**: 46 groups, all Same work, 34 flagged for review — 13
  whose editions differ, the 20 Penguins of Madagascar pairs and S03E31/S03E33 — and 12 without a
  flag; no group of another class, no possible match and no estimate. Blade Runner's group and
  «Ніж у воді»'s are joined by the ids the library holds though their files are not one film.
  (MAM-37 since: 4 groups on a copy of production's database the same day, 1 needing review,
  «Ніж у воді»'s, for its running times; Blade Runner's left the page by the years its file names
  claim, and with it the Penguins of Madagascar pairs, S03E31/S03E33, the eight Mickey Mouse
  Clubhouse episodes held as "a" and "b" files and the 12 other groups of different editions.)
- **MAM-21 Show duplicates on a Duplicates page** (built, release 0.2.1): `/library/duplicates`,
  every signed-in user's, from **Duplicates** in the header and at the top of the Library page
  (Vasyl's decision 4 of 2026-10-07), with underline tabs All, Same file (decision 3),
  Byte-identical, Same work, Possible and Dismissed, the tab in the address, filtered and paged on
  the server. Each group is headed by its first work — an episode by its series, with its season,
  episode and own title — and shows its class, its distinctions, the evidence its class gives, the
  room its files take and, only where MAM-20 gives one, the room removing all but one copy would
  free, or why there is none; the list comes in the order of those headings, an episode's group by
  its series, season and number. Its files reuse the Library's version rows and comparison table,
  each named by the end of its path that tells it from the group's other files — its file name, or
  the nearest folder that differs where another file has that name —, with its folder, identity,
  hash, editions and the works that hold it. The owner alone confirms or dismisses a possible match
  and undoes either (decision 1; members see the decision without the decider's name, the buttons
  disabled with the reason): `PUT` and `DELETE
  /api/library/duplicates/{key}/decision`, one row of `DuplicateMatchDecisions` per group key, each
  change with an audit row (`DuplicateMatchConfirmed`, `DuplicateMatchDismissed`,
  `DuplicateMatchDecisionUndone`), the same decision again writing nothing
  ([ADR 0029](docs/adr/0029-a-possible-match-is-confirmed-or-dismissed-by-the-owner-alone-kept-by-its-groups-key-and-never-authorises-removal.md);
  ADR 0028 amended). A confirmed match stays on the list, marked, still a possible match, and never
  authorises removal; a dismissed one leaves every tab for Dismissed, a short row with **Undo**
  taking its card's place, and can be undone there too (decision 2); a group another work, or a
  scanned file no library reports, joins or leaves gets another key and is asked about again. Only
  possible matches are decided. No delete, move or bulk action of any kind. On production on
  2026-10-08 (MAM-22) the page listed the 46 groups on two pages of 25, and at desktop width, 375
  and 320px it had no horizontal scroll; there was no possible match to decide. (MAM-37 since: "1–4
  of 4" on a copy of production's database the same day.)
- **Backup/restore:** the scan inventory and the hash cache live in the database and can be rebuilt
  — the names by rescanning, the hashes by the next inspection, which reads the files that could be
  copies whole again — so they need no export of their own; the series, seasons and episodes come
  back with the next sync, like the movies. The tracker sections mapped to destinations are rows of
  the database beside the destinations, in the data directory's backup; a search's season and its
  releases' kinds and categories live for a day, like the search. MAM-33's catalog, recommendations,
  availability checks and newest releases rebuild themselves from TMDb and the release sources, and
  the posters in `<data>/posters/` are fetched again when missing; each user's "Not interested" and
  Profile settings are rows of `mam.db`, in its backup. MAM-34's links are the owner's work and live
  in `mam.db` alone — a link kept in the app, and a link not identified yet, nowhere else —, so they
  are in its backup; the suggestions and their searches rebuild themselves after the next sync.
  Identify changes Jellyfin's own data and can't be undone, so Jellyfin's data folder is worth a
  backup before **Update Jellyfin**. MAM-35's poster tags come back with the next sync, and the
  posters in `<data>/library-posters/` are fetched again from Jellyfin when missing. MAM-20 stores
  nothing: the duplicate groups are worked out on every read from the names, hashes, ids and links
  above, and MAM-37's running times from the durations the inspection measured. MAM-21's decisions
  on possible matches are the owner's work and live in `mam.db` alone (`DuplicateMatchDecisions`,
  with their audit rows), so they are in its backup.
- **MAM-36 The Library's Sources card as a status bar, and Refresh all** (built, release 0.2.1): the
  card that took about 480 px at the top of the Library page is one bar — an item for each media
  library, for the media folders and for the file inspection, with an icon, when it last ran or how far
  it has got, amber chips for what needs a look and the total of the files and the room they take —,
  and **Details** opens the rows as they were. Vasyl picked it, layout "A", from four mockups on
  2026-10-06. **Refresh all**, the owner's, syncs every enabled library, scans the media folders and
  inspects the files from the page, each step once the one before has ended, stopping with a sentence at
  a refused start or a failed run (Vasyl's decision of the same day: from the browser, not an endpoint).
  A chain that survives a closed tab would need an endpoint that queues the three steps; it is not
  planned. No API changed.
- **MAM-22 Phase B acceptance** (done): the acceptance bar's four criteria below, with MAM-20's and
  MAM-21's parts, checked on 2026-10-08 on production — the MAM container on the media host,
  running the published 0.2.1 image (`vahac/media-acquisition-manager:latest`) — with Jellyfin
  12.2.0 and ffprobe 6.1.1-3ubuntu5, Vasyl signed in as the owner. **Refresh all** synced Jellyfin
  in 5.8 s (1,360 items into 1,291 works and 7 series, 1,384 versions, 1,380 files), scanned the
  media folders in 1.6 s (1,397 files, 17 in no library, 0 problems) and inspected the files in two
  runs, the sync's and the scan's, each checking the 1,397 files from the app's cache of readings,
  ffprobe running on none, and finding no file to hash. The 1,397 files take 8,067,686,182,937 B
  (7.34 TiB): the 1,380 of Jellyfin's libraries 7,974,593,521,562 B (7.25 TiB), each read at the
  size Jellyfin reports, and the 17 only the scan found 93,092,661,375 B (86.7 GiB), adding up to
  the total exactly; every file has one link, no two share a size, and no hash is stored.
  `GET /api/library/duplicates` then listed 46 groups, all Same work, 34 needing review — 13 whose
  editions differ; the 20 Penguins of Madagascar pairs, each one episode IMDb id under two TVDB ids
  and two numbers; and Mickey Mouse Clubhouse S03E31/S03E33, one episode's IMDb and TVDB ids under
  two numbers — and 12 without a flag, the eight Mickey Mouse Clubhouse halves among them. Works
  of similar titles or years — Ghostbusters (1984), Ghostbusters (2016) and Ghostbusters II,
  RoboCop of 1987 and of 2014, Upgrade (2018) and Upgraded (2024), and more — joined no group, the
  Penguins of Madagascar film (2014) was joined to none of the series' episodes, and no possible
  match was offered. The Duplicates page listed the groups on two pages of 25 and didn't scroll
  sideways at desktop width, 375 and 320px; it has no control that deletes, moves or renames.
  `tools/mam22/report.py`, which Vasyl ran before and after, read production's database and
  container: no file's size, modification time, device, inode or link count changed, none
  appeared or went, and no audit row was written; `/media` is mounted read-write there, so this
  rests on that comparison and on the app opening files read-only. Production has no hardlink, no
  byte-identical pair, no group with both copies' dynamic ranges measured and no possible match, so
  the space counted once per file, the hashing, the Same file and Byte-identical classes, the space
  estimate and the "Dynamic ranges differ" flag rest on the tests, and Confirm, Dismiss and Undo,
  with a member's view of them, on the tests and MAM-21's browser check of 2026-10-07 against a
  throwaway database. Two groups are joined by the ids the library holds, not by title, and are
  recorded as they are: Blade Runner (1982) in The Final Cut and Blade Runner 2049 (2017), whose
  item Jellyfin gives the 1982 film's IMDb and TMDb ids, flagged only by The Final Cut label; and
  «Ніж у воді» (1962), the film (95.5 min) and a featurette (26.7 min) under one IMDb and one TMDb
  id, the TMDb id also a link the owner set, with no flag. (MAM-37, below, since lists 4 of these 46
  groups, 1 needing review: «Ніж у воді»'s, flagged "Running times differ". Blade Runner's left the
  page by the years its file names claim, and so did the Penguins of Madagascar pairs and
  S03E31/S03E33, as two episodes at different places, the eight Mickey Mouse Clubhouse halves, as
  an episode's "a" and "b" files, and the 12 other groups of different editions.) The other
  tickets' open items stay open: a TV release sent (MAM-32); the Toloka checks within the users'
  limits, New on Toloka and a download from a recommendation (MAM-33); Identify with Jellyfin 12.2,
  in «Фільми» and with applies sent at the same time (MAM-34); and a Jellyfin behind a reverse proxy
  (MAM-35). What was tried live, and what wasn't, is in the README's
  [Tested with](README.md#tested-with) and the release note.
- **MAM-37 Running times differ, and real copies only** (built, release 0.2.1; checked on 2026-10-08
  on a copy of production's database, first published in 0.3.0): the Duplicates page lists only
  real copies of one film or episode, and flags for review the copies of one work whose measured
  running times differ widely (Vasyl's decisions 1 to 4 of 2026-10-08;
  [ADR 0028](docs/adr/0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)
  amended). Nothing in the library, a file or a download client changes, only what the list holds,
  and registry ids stay the only join: a file's name can keep two copies apart, never join them. Two
  episodes of one show at different places are never one work, whatever ids they share (decision 2,
  after the first check: the Penguins of Madagascar pairs under one IMDb id, Mickey Mouse Clubhouse
  S03E31 under all of S03E33's ids); this reverses MAM-20's rule of 2026-10-07 for episodes, while
  films keep it — one IMDb id with two TMDb ids is still one work flagged "TMDb ids differ". A show
  is every id and title one place names, so a show the library knows by no id is one show by its
  title; the works are joined in id order, a join refused where the two sit at different places of a
  show both hold or either at two places of one, and a film item whose ids reach two such places
  joins neither; a work the library itself holds at two places of a show is no group, and such
  episodes are never offered as a possible match either. `EpisodeNumbers` and "Episode numbers
  differ" are gone; `IdentifierConflict` stays for films and for copies of one episode under two
  shows' ids. An episode's files whose names glue different segment letters to their first `SxxEyy`
  — `Mickey Mouse Clubhouse_S03E09a` and `_S03E09b`, one episode to Jellyfin — are no copies of each
  other (decision 2): an "a" file is a copy of another "a" file only, never of a "b" or of a whole
  episode, the letter a lowercase a to d standing alone. A film's copies whose file names claim
  years more than one apart are no copies (decision 3, that Blade Runner 2049 must not be among the
  duplicates at all, by the rule proposed to Vasyl for it): a year counts only alone in round or
  square brackets, 1880 to 2099 — never a bare year, a list, a span or a year beside a word in its
  bracket —, a name claiming two years claims none, years each within one of the next are one
  film's, and beside two runs of years or more the copies claiming none are a part of their own. So
  Blade Runner (1982) and Blade Runner 2049 (2017), one movie with two versions under the 1982
  film's ids in Jellyfin, leave the page, while "Seksmisja (1984).mp4" beside "Seksmisja [1983
  DVDRemux].mkv", which claims no year, stays one group; films only, as in an episode's name a
  bracketed year is its show's or its season's. Different editions are not duplicates (decision 4,
  on Rambo: Last Blood's Theatrical and Extended Cut; it reverses specification §3.3's reading, in
  its MAM-20 note, that different editions are "distinctions needing review", and MAM-20's edition
  flag for this class): copies whose names say different
  sets of editions — Theatrical, Extended, Director's Cut, Special Edition, Unrated, Assembly, Open
  Matte, Widescreen, Remastered, Final Cut and the rest of the word list — are no copies, and a copy
  that says none beside one that says one is another edition; a hardlinked file's names say one
  thing between them; a possible match keeps "Editions differ" as evidence, and same-file and
  byte-identical groups are untouched, as their bytes are one. The copies are split by their
  letters, then (films only) their years, then their editions, each part a group with two copies and
  two files or more; its key adds `|Segments:` when its copies carry letters, then `|Years:` and
  `|Editions:` when those parted something, so a group with none keeps the key it had. Last, a
  same-work group whose shortest measured copy runs under three quarters of its longest — 4 ×
  shortest < 3 × longest in whole milliseconds, exactly three quarters not under — gains
  `RunningTimes` (5), the badge "Running times differ", and needs review, beside any other
  distinction and last in kind order (decision 1); running times split nothing. Copies, not files,
  are compared: a copy is what one version holds, its running time its files' measured durations
  summed (a film in two parts as long as both), each distinct copy counted once, and a copy with a
  file never inspected or whose reading failed decides nothing. The group's `evidence.runningTimes`,
  `{ shortestMilliseconds, longestMilliseconds }`, gives the shortest and the longest measured copy
  whenever two copies have a running time, flagged or not, `null` otherwise and for every other
  class; the card says under "One work by its ids:" "The shortest copy runs 26:41, under three
  quarters of the longest, 1:35:32." when the group is flagged. Rejected: the possible matches' 2
  minutes or 10 %, made to tell another rip from another cut, and "more than 40 minutes", which no
  episode or short could reach. Checked on 2026-10-08 with the build run on this PC on a copy of
  production's database, production keeping the published 0.2.1 image (Vasyl's decision 5 that day):
  `tools/mam37/copy_db.py`, which Vasyl ran at 11:31 UTC, copied the database — 1,397 names, 1,396
  with a measured duration — and in the copy only removed the allowed folder `/media` and pointed
  the four integrations at `http://127.0.0.1:9`; its `--verify` found the 14 tables the duplicates
  are read from as copied at the start and after the check. `GET /api/library/duplicates` listed 4
  groups, all same work, each under the key it had in MAM-22's list that morning and each with
  `runningTimes`: The Gods Must Be Crazy (1980), two WEB releases; «Ніж у воді» (1962), the film and
  the featurette at 27.9 % of it, flagged `RunningTimes`, the 1 needing review; Seksmisja (1984);
  and Alien: Romulus (2024), a 2160p remux and a 1080p rip. The page said "1–4 of 4", with the badge
  and the sentence on «Ніж у воді»'s card alone, and didn't scroll sideways at desktop width and
  375px; Vasyl signed in himself and looked at it. The other 42 groups left the page: the 20
  Penguins of Madagascar pairs and S03E31/S03E33, the eight Mickey Mouse Clubhouse episodes held as
  "a" and "b" files, Blade Runner, and 12 groups of different editions — The Bourne Identity
  (Theatrical and Open Matte), Rambo: Last Blood, Terminator 2's three editions, Terminator
  Salvation, Alien, Aliens, Alien³, Alien: Resurrection, AvP: Requiem, and three Tom & Jerry shorts,
  plain beside Widescreen. Earlier that day the first version of the change had listed the 46
  groups, and the second, after decision 2, 17 of films and shorts; decisions 3 and 4 followed from
  those lists. A copy in two parts, a copy unmeasured or whose reading failed beside measured ones,
  a hardlinked file whose names say different editions, a whole episode file beside an "a" file, a
  film item linked as an episode at another place, a part's own key and a possible match, none of
  which production has, rest on the tests; production has run an image that holds it, the published
  0.3.0, since 2026-10-11.
- **MAM-38 Downloads that would write over a file** (built, release 0.2.1; first published in
  0.3.0; met on 2026-10-12 only in a replacement's strict send, MAM-28): a release whose files would
  land on a different file already in the destination's folder is refused before anything reaches
  the client (Vasyl's decisions of 2026-10-08;
  [ADR 0034](docs/adr/0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)).
  qBittorrent 5.2.4 keeps every file it finds at a new torrent's paths and refuses only an info hash
  it already has, and libtorrent 2.0.15 checks such a file and downloads the pieces that don't match
  into it, in place — read from their source while MAM-23 was decided, with no live test —; on the
  reference installation every category saves straight into a Jellyfin library folder, and the app
  can't stop a torrent once sent (ADR 0014, ADR 0031). Right before the one submission, after the
  client is asked whether it already has the torrent, `DownloadSender` asks
  `IDownloadOverwriteGuard`: `TorrentContentReader` reads the .torrent's files (v1 `files` or
  `length`, `name.utf-8` and `path.utf-8` first, pad files left out as BEP 47 has them, a hybrid by
  its v1 metadata, a v2 torrent by its file tree), each name cleaned as libtorrent 2.0's
  `sanitize_append_path_element` cleans it on Linux (`TorrentPathElement`, a byte-for-byte port:
  every path element kept as one, the folder `_` or the v1 info hash for a name that is empty or
  dropped), and `TorrentTargetPaths` gives each file's places below the category's folder under all
  three content layouts — Original, Subfolder (a lone file in a folder of its name without the
  extension) and NoSubfolder —, as the app never reads `app/preferences`, which carries the Web UI's
  key. A place inside the Media folders that holds a file of another size, a file of the same size
  whose sampled pieces — up to three wholly inside it, the first, one in the middle and the last,
  SHA-1 for v1 or a SHA-256 tree for v2 (`IFileInventory.HashPiecesAsync`) — don't hash as the
  torrent says, something that isn't a file, a file the app can't read or one that changed while it
  was compared, or a place a torrent the client already has in the category will write — compared
  below its save path (`ListDestinationJobsAsync`), by its content or its name first and then, for
  at most 20 torrents that may write there, by its files (`ListJobFilesAsync`), one still in
  qBittorrent's folder for incomplete torrents included —, refuses the send: the job, written first
  as ever (ADR 0013), ends `Failed` with the app's sentence naming the first file and counting the
  release's other files that land on one, each once — "“qBittorrent” would write over
  “Film.2001.mkv” in the folder of the category “Movies”: a file of another size is already there,
  so nothing was sent." —, everyone's to read, and nothing is handed to the client. A release whose
  files already there have the same bytes is sent (a Toloka update re-adding a series' old
  episodes), an empty file is no place, and a folder that is only the same folder passes. A magnet,
  a .torrent whose list can't be read or that libtorrent refuses, a folder with no path in the app
  or outside the Media folders, a place behind a link leading out of them, a file name the app can't
  look up (DEL or a C1 control, which libtorrent keeps), and a client that can't tell its torrents
  or their files are sent as before, unless another place refuses the send, and the job's
  `OverwriteCheck` (migration `AddDownloadJobOverwriteCheck`; `Checked` or one of six `NotChecked…`
  reasons, by name, `null` for older jobs and refused ones) says why, which the Jobs pages show in
  an amber line, but not on a failed job. A check that breaks on a bug fails the job with the app's
  sentence, nothing sent. The Download dialog asks
  `GET /api/downloads/overwrite-check?destinationId=` when a destination is picked, holds Send until
  that first answer arrives (never after a failed one), and warns before Send when its folder can't
  be checked, the warning read with Send, for every signed-in user, with a reason and never a path;
  its summary now says the app "doesn't send a release it finds would write over a different one".
  Rejected: refusing any file already there, comparing sizes alone, hashing whole files, adding the
  torrent stopped to let qBittorrent check, sending a content layout or a save path of the app's,
  reading qBittorrent's preferences, and fetching a magnet's files first. The cost is two reads of
  the client more per send and one question when a destination is picked, never a write; Phase C's
  replacement will use the same check, the same bytes refused too (ADR 0032). (MAM-24 since: a
  replacement is sent with the check made strict — any file already there, and any place it can't
  look at, refuses it —, and the dialog's summary now reads "Kept: nothing is replaced or removed,
  and a release that would write over a different file isn't sent.")
- **MAM-42 List the files in no library, let the owner ignore the ones that aren't media, and show
  the same warnings with Details open or closed** (built on 2026-10-13, release 0.3.0; not tried on
  production, checked in a browser on a scratch installation with real ffprobe at desktop and phone
  width): on 2026-10-13 the Sources card warned "17 in no library" and "1 couldn't be read" with
  Details closed and only the first with them open, as the Inspection item summed every file no look
  could read, and nothing showed which files they were (Vasyl's decisions of 2026-10-13, inside
  0.3.0; the MAM-42 section of
  [ADR 0021](docs/adr/0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).
  *One rule for the bar:* every amber chip sits on the item whose row under Details holds its amber
  line, and the other way round — a library's item its files without an app path and its own files
  that couldn't be read, the Folders item the files in no library (counted now, no longer the last
  scan's stored count), the folders not scanned and the files in no library that couldn't be read,
  and the Inspection item only the files it couldn't hash; the Media folders row's status line ends
  with the files found, followed by "17 files are in no library." with **Show files**, the counts
  line adds the ignored ones, and the files that couldn't be read are one line with a count.
  *Files in no library:* a dialog for every signed-in user, opened from the Folders item's two file
  chips and **Show files**, with tabs In no library, Couldn't be read and Ignored, counted and paged
  on the server (`GET /api/library/unreported-files?view=Problems|Ignored`), each file with its
  name, folder, size, what the app's look found and why a library most likely doesn't list it —
  under no library's folders, in a library's folders but not listed (skipped by its name, or added
  after its last scan), or the libraries' folders not known yet —, worked out when read from the
  folders the last syncs stored. *Ignore:* the installation owner's (`PUT` and `DELETE …/{id}/ignored`),
  `PhysicalFiles.IgnoredUtc` on the scan's name (migration `AddIgnoredUnreportedFiles`), with an audit
  row each way: the file leaves every count of files in no library and that couldn't be read but not
  the room the files take, nothing changes on the disk or in a library, the mark holds while a scan
  keeps the name and a moved or renamed file counts again. `scan.files` in `GET /api/library/sources`
  leaves the ignored names out and `scan.ignoredCount` counts them. Accepted gap: an ignored file is
  still inspected and hashed, and still offered as a possible match on the Duplicates page.
- **MAM-43 Count a Blu-ray or DVD folder's clips as its film's files, and say when a file sits in an
  extras folder** (built on 2026-10-13, release 0.3.0; not tried on production): on 2026-10-13 Files
  in no library listed four `BDMV/STREAM/00000.m2ts` clips of films Фільми does list, and twelve
  shorts in folders named `Shorts`, all with the reason "a library skips samples, trailers and extras
  by their names, and lists a file added after its last scan only after the next one" (Vasyl's order
  of 2026-10-13;
  [ADR 0038](docs/adr/0038-a-disc-a-library-reports-by-its-folder-gets-the-clips-of-its-bdmv-or-video-ts-folder-as-its-files-listed-by-the-sync.md)
  and ADR 0021's MAM-43 section). *Discs:* the Jellyfin adapter keeps a local disc's folder
  (`VideoType` `BluRay` or `Dvd`), and the sync walks it inside the media folders through the
  integration's mappings (`IFileInventory.ListMediaFilesUnderAsync`). It stores the media files of the
  disc's own `BDMV` or `VIDEO_TS` folder as the version's files in Jellyfin's terms, so the scan, the
  Jellyfin row's room, the inspection and the duplicates count them as any reported file. No
  migration. *Extras:* Jellyfin's `NamingOptions.VideoExtraRules` (`LibraryExtraNames`) give two new
  places, `ExtrasFolder` ("In a folder named “Shorts” in Мульфільми's folders: …") and `ExtraName`
  ("Named as an extra (“-trailer”) …"), with `place.extraName`. `LibraryFolder` keeps only "a
  library lists a file added after its last scan only after the next one". Accepted gaps: a disc reached
  only after a sync gets its files at the next sync, and the Library page's sort by size still counts a
  disc as 0, as its clips carry no reported size.
- **MAM-44 Keep the files Jellyfin takes for extras on an Extras tab of their own, without a
  warning** (built on 2026-10-13, release 0.3.0; not tried on production): after MAM-43 production's
  list still warned of twelve shorts in `Shorts` folders, and a read of Jellyfin found them nowhere,
  neither as items nor as any film's special features (Vasyl's decisions of 2026-10-13: extras by
  name too, no warning even for one that couldn't be read; ADR 0021's MAM-44 section). The files
  whose place is `ExtrasFolder` or `ExtraName` leave the counted files, the problems, the bar's
  chips and the Media folders row's warning. They sit on a fourth tab, **Extras**
  (`GET /api/library/unreported-files?view=Extras`, `extrasCount`), and the counts line adds
  "· 12 extras" (`scan.extrasCount`). Their room still counts, and Ignore takes one to Ignored. The
  two reasons now say Jellyfin shows such files nowhere when the folder above holds no single film.
  One set, `UnreportedFileSet`, feeds the list and the counts.

**Out of scope:** removing anything, and bulk deletion of any kind; replacing a selected copy is
Phase C. Searching releases for one episode by its number (a source's `ep=`), which no phase plans
yet: a release's title says which episodes it holds, and a season can be searched for. Jellyfin's
season items and its virtual (missing) episodes (Vasyl's decisions 1 and 3). Letting a file name
decide what a library item is (ADR 0017), or a release's title what a release is: its source's
category decides (ADR 0020), except in TV/Anime and TV/Documentary, where Toloka files films beside
series and a season or episodes marker in the title or description decides (MAM-33). For MAM-33: any
download a recommendation starts by itself; weighting by what was watched or marked favourite, as
Jellyfin's user data needs a user and the integration has an API key only; and writing the TMDb ids
the app found by itself into the library or showing the catalog's data on the Library page's cards
(ADR 0025; MAM-34 identifies only what the owner picks). For MAM-34: "Replace all metadata" and any
refresh, which Identify replaced; locks and collections, which the app never touches; a TMDb id on
an item of the other kind (a show's on a movie item, or any id sent to the other kind's search);
identifying in a library that saves beside the media; sending an item's people; any file or folder —
nothing is moved, renamed or written; a catalog other than TMDb; changing what kind of item Jellyfin
says something is; an undo of an Identify; linking a work without the owner's click; and joining two
libraries' copies by a linked id (MAM-20). For MAM-35: the catalog's posters on the Library and
Search cards (ADR 0025); a season's picture, and pictures on the episodes of an opened season; any
of Jellyfin's images but the item's own primary one — a backdrop, a logo, or a series' or season's
picture lent to an episode —; uploading, setting, refreshing or deleting an image in Jellyfin; and
pruning the posters' cache. For MAM-20: removal of any kind, a copy chosen to keep or one called
redundant; counts per class in the answer; storing the groups; reading a download client; and a
file system's type read anywhere but Linux's mount table, so Windows gives no estimate. For MAM-21:
deleting, moving or renaming a file, and any bulk action; a decision on a group of another class,
by a member, or one that follows a group whose members changed; a confirmation that joins the works
or clears the review flag; pruning the decisions whose key no group has now; and counts on the
tabs. For MAM-37: joining anything by a file's name — its segment letters, years and editions only
keep copies apart, and registry ids stay the only join —; splitting a group, or keeping works
apart, by their running times; a running-times distinction on a possible match, which keeps its 2
minutes or 10 %; parting a same-file or byte-identical group by its names; reading a year in an
episode's name, a bare year, a span or a year beside a word in its bracket; calling a copy
redundant; and writing anything to the library, a file or a download client, or telling the owner
to change Jellyfin's ids or their links. For MAM-38: any write to a download client — stopping,
removing or re-adding a torrent —; a content layout or a save path the app picks; reading
qBittorrent's preferences; fetching a magnet's files before sending it; and doing anything to, or
suggesting anything about, the file already there. For MAM-42: moving, renaming or deleting a file
in no library, or telling a library to scan; ignoring a library's file, a folder or a pattern of
names; ignoring several files at once; and storing why a library doesn't list a file.

**Tests:** two hardlink names count their space once; a changed size or modification time
invalidates the cached hash; similar titles or years are not merged; different cuts stay separate
versions; conflicting IDs require manual review; a series known by several library items is one
series, and a movie and a series sharing a TMDb number stay apart; a seeding file is classed like
any other. MAM-32: a series' seasons come from its episodes' own numbers, and a virtual episode is
stored as nothing; a list of series or episodes that changes while it is read stores nothing; the TV
queries go to the TV categories without the year, with the season where asked, and a failing one
fails its source; a TV release matches series only, a movie release movies only, a season-1 pack by
its first year exactly and a later seasons' pack by a series of that year or earlier, with the
episodes held counted once each; the Download dialog's suggestion prefers the source's own section,
and a tie suggests nothing. MAM-18: the walk lists media files by extension in any letter case,
hidden folders included, follows a link inside the folders under its own name, refuses one leading
out and a loop, and answers a missing or empty root, a dangling link, a folder it may not list and a
folder swapped for a link after the check as places it couldn't list; a movie's and an episode's
file are reported by their mapped path alone, a file reached by two names is stored once, a name
stored where the walk couldn't look stays and one it looked for and didn't find goes, and a walk
past its limit, a failure or a stop stores nothing; the count of files in no library follows a sync
or a fixed mapping at once; a scanned name never shows in the works list, a search or a match; and
the inspection after a scan reads only what is new or changed. MAM-19: two hardlink names count
their space once, and a file a library and the scan, or two libraries, name counts once in the
total, never with the size a library reported; a file whose size no file of another identity shares,
two names of one file and two files of one size whose compared facts differ are never hashed, while
a fact only one of them knows or a file ffprobe couldn't read rules nothing out; a changed size,
modification time, inode or device hashes the file again and forgets the old hash, while a mount
away for several runs costs no hash of a file the app has a reading of; a file that changes while it
is hashed keeps nothing; one file is hashed at a time; and a restart's run hashes only what is left.
MAM-33: the TMDb adapter reads its recorded answers — a film's details with its collection, credits
and recommendations, a show's with its seasons, a collection, a person's credits, searches, a find,
discoveries, the genre lists and a wrong token —, paces its requests and waits out a 429; the
Torznab adapter reads Toloka's recorded newest releases and their descriptions; each signal and each
exclusion, the tabs by genre, a tab nobody has on never asked, the rating floor for taste alone and
the missing seasons; the catalog's rows apart from the library with their provenance, read again
after 30 days, a lookup cached when it found nothing; the run's start, worker, reconciler and
triggers; a film in TV/Anime or TV/Documentary matched to the library's movie by the ordinary
search; and the list's filters and read-time rules, "Not interested" per user, the tab switches and
limits with the budget split and the shared result, the availability check and its recheck rules,
the poster endpoint and the Profile settings. MAM-34: the Jellyfin adapter reads its recorded
answers — the users, the items as read, the searches' rows, the applies' 204, 400, 401 and 404 and
the libraries' options —, sends the film or series search with the id under its exact key, its
provider and the library's language, then one apply with `replaceAllImages=true` carrying the chosen
row, reads the item again to confirm it, marks an item that already shows the match identified with
no apply, never sends an id to the search of the other kind, refuses a library that saves beside the
media, an item that is locked, held ids that conflict in a protected batch, no match and an
ambiguous one, never sends an apply twice whatever becomes of its answer, never reports an item that
took only the ids as identified, refuses an episode item before anything is sent and an item hidden
from the administrator, and names the kinds it identifies; the worker identifies two films at a time
and one series at a time, the owner's own click first, stops a batch after two identifications that
don't confirm, and a film saved while a series is identified doesn't wait for it; a series' link is
identified through set, replace, Accept all, Retry, the worker and the repair, and judged by its
show's id at a sync; an episode's link is identified through The Open Movie Database by the
episode's IMDb id TMDb gives, or stays in the app with its reason until saved again once TMDb gives
one, and a sync judges it by that id; the repair counts and queues exactly the links not identified
yet, protects the library's own ids and refuses the four evident mistakes, takes a refused link
again at the next click and never one whose apply was sent, with its batch's progress; a sync leaves
a refusal alone and judges only identified links; setting, replacing and removing a link — whose
requests send nothing to Jellyfin — with every sentence; TMDb's recorded details give a short's runtime and
a show's runtime, IMDb id and other titles; a link survives a sync, groups by its id, keeps a third
Jellyfin id apart as a conflict, follows its files to a re-created item, clearing its identified
time, and stays unattached when two items or two links claim them; each write state and what a sync
makes of it; Retry and Identify again, with every sentence; the suggestion run, its triggers and its
cache, the evidence, the confident rule and the order; every paste format and the free search; the
review queue, "none fits", the confident list and Accept all with its background worker; the
migration that adds one column and keeps the old ones; every endpoint owner-only; the dialog saying
before **Save** what Jellyfin will replace and asking before **Retry**, the panel's count,
confirmation and progress, and the **Not identified** tab; and the Library list, its missing filter
and search, the Search page, release matching and the recommendations counting a linked id. MAM-35:
the Jellyfin adapter reads each item's own primary image tag from the recorded pages — an episode its
own, never its series' or season's, the virtual special none, a tag the app couldn't keep none —,
asks for a 342-wide JPEG with the key in the header, keeps only a JPEG, takes the recorded 404 and
400 as no poster, follows no redirect and throws on an id or a tag Jellyfin never gives; a sync
stores, keeps, replaces, removes and moves the tags with the rest of the snapshot, changes none when
it fails, stores none for an item with nothing else, and the purge takes them; the works and the
Search page's library works carry the poster of the item the card is named after, else of the next;
the endpoint serves only a pair a sync stored, from disk or fetched once and kept, with every bare
404 and the 502 for the owner and for everyone else, its key masked; and the card shows the poster
beside the title, the placeholder without one, and fetches it once, only as it comes near the window.
MAM-20: two hardlink names are one group whose room counts once and frees nothing, and one path two
libraries report is no group; files of two identities with one current SHA-256 are one group, a hash
kept for a file changed since makes none, and the estimate is given on ext4, never on btrfs, ZFS or
XFS, on an unknown file system, with an unknown link count or with a name the app never found, each
with its reason; a seeding file is classed like any other with no client asked; one movie in two
libraries by its IMDb id or a TMDb id the owner linked is one work, the wrong id a link replaces
joins nothing, and a film item linked as an episode joins the library's own episode; the 65 files of
one id-less item are no copies, The Continental's three items are one work never called redundant,
an episode's halves are two versions of it, and a series known by two library items is one series;
a movie and a series sharing a TMDb number stay apart; conflicting ids, one episode's ids at two
places, different cuts and different measured dynamic ranges require manual review; similar titles
or years are never merged or offered; a possible match needs one title key and its year, never
across kinds, differing ids or episode places, and a scanned file is offered by its file or folder
name, never an episode's; and the list's access, sentences, filter, paging, order and wire names.
(MAM-37 since: an episode's "a" and "b" files, two episodes of one show at different places under
one episode's ids and different cuts make no group, and those tests now say so, below.)
MAM-21: the owner's confirmation and dismissal are stored by group key with an audit row whose
target is the key, the same decision again writes nothing and a change writes a second audit row;
a dismissed match leaves every list but the dismissed one and a confirmed one stays, still needing
review, the decider's name for the owner only; a member's decision or undo is 403 and an anonymous
one 401, writing nothing; a key no group has now, of any shape, is a bare 404 and a group of another
class 400; every decision that isn't one, and every body that can't be read, gets its sentence,
checked before the key; an undo forgets the decision with an audit row, and one with nothing to undo
writes nothing; two first decisions racing leave one row; a decision doesn't follow a group whose
members changed and changes nothing in the library; the episode groups come by their series, then
season and episode, and a list of decided matches asks no file system; and the page's tabs and
address, the query each tab sends, its paging, each class's evidence and estimate, an episode's
heading, the path ends that tell files apart and two files of one name told apart by their
folders, the rows and the table, the decisions and their failures, a 404 that says the group
changed and asks for the list again, the short rows a dismissal and an undone dismissal leave with
Undo and Dismiss again, a member's disabled buttons and the load and refresh failures.
MAM-37: two episodes of one show at different places are never one work — under one IMDb id, under
all of one episode's ids, and in a show without ids by its title — while one episode at one place
known by two shows' ids is one, one episode's id in two shows sharing no id or title joins them, two
films with one IMDb id and different TMDb ids stay one, a film item with one episode's IMDb id joins
it, and copies of one episode join while another episode under its IMDb id stays out; a film item
linked at one place and the library's episode at another stay apart, a film item with the IMDb id of
two episodes at different places joins neither, a work the library holds at two places of a show
joins nothing and makes no group, and two films carrying its ids are grouped without it; the
components are the same whatever order the works come in, a chain through another show included; and
two such episodes with one title and year are never offered as a possible match. An episode's "a"
and "b" files make no group, each segment is grouped with its copy in another library with its own
running times, and a whole episode file stays apart from its segment file; the letter is read only
glued to a name's first `SxxEyy`, never from two segments or a range, a capital letter or a word
glued to the number, a marker written otherwise or a film's name. A film's versions, or two films
under one id, whose names claim years far apart make no group; a year sharing its bracket with a
word, a run of years each one apart and a copy claiming no year beside the film's copy keep one
group under the work's key; a copy whose parts claim years far apart claims none; the copies of two
films under one id are grouped by their years, a third copy claiming none left out, or, with a copy
of its own, a group of its own; an episode's copies are one group whatever years their names claim;
and a year is read alone in brackets, from production's names too, never a bare year, a list, a
span, a year among other words, two different years, one out of range or other digits. Different
cuts — the Theatrical and the Extended Cut, three editions of one film, a cartoon and its Widescreen
copy, and a version whose library name says a cut beside one that says none — make no group, while
two copies of one cut keep the work's key, the copies of one cut beside another cut get a key of
their own, those saying no edition beside another edition a "none" key, and a part whose copies say
several editions names them in edition order; one file whose hardlinked names say different editions
is in one group of copies; and a possible match's files whose editions differ are still flagged. A
shortest copy at exactly three quarters of the longest is not flagged and one a millisecond under
is, with no rounding, a longest not divisible by four deciding by the exact share; a film and a
featurette under its ids are flagged by their running times, and beside ids that disagree, after
them, while a shortest copy over three quarters of the longest, another cut and another rip are not;
a copy never inspected, one whose reading failed and one with a part not measured decide nothing,
and the measured copies beside one never inspected are still compared; a film in two parts runs as
long as both, one path two libraries report is one copy, and the evidence is the shortest and the
longest of three copies, flagged or not, written by name and last, and `null` for every other class;
a possible match gets no running-times distinction whatever its durations; and the card shows the
badge after the others and the sentence only for a flagged group. MAM-38: a send whose file would
land on a file of another size, on one of the same size with other bytes, on something that isn't a
file, on a file the app can't read or that changed while it was compared, or where a torrent
qBittorrent already has will write — its own file, a file in its folder, one still in its folder
for incomplete torrents, or one listed after more than 20 others, found without asking any files —
fails with the app's sentence, the same for the member who sent it, several collisions naming the
first and counting the release's other files once each, a later file of the same size counted
unread, with no add reaching qBittorrent and no write but the sign-in and the one add; a lone file
in a folder of its name fails under the Subfolder layout; a collision still refuses beside a link
leading out or a name that can't be looked up; the check runs after "already has this torrent", and
one that throws fails the job with the app's sentence and logs the type, never the message; the
same bytes are sent as checked, for v1 with pad files and for v2, and so are a folder that is only
the same folder and a torrent's folder holding other files; and a folder outside the Media folders,
where no file is looked at, a place behind a link leading out of them, a category folder without a
path in the app, a file name the app can't look up, a magnet, a file list the app can't read, and a
client that can't list its torrents, holds one without metadata, can't list or throws on a
torrent's files, or has more than 20 to ask (20 asked) are sent unchecked with their reason. The
dialog's question gives no reason for a folder inside the Media folders, says so for one outside
them to a member too, says unknown for a folder without a path, a switched-off client, which it
doesn't ask, a client that can't list its categories and a category it no longer lists, and is 400
without a destination, 404 for one that no longer exists and 401 signed out. The .torrent reader,
the places under the three layouts, libtorrent's name cleaning, the v1 and v2 piece hashes and tree
widths, the bencode parser, the destination rule, the save path's read and the hashing of pieces on
real files, a chunk at a time, have their own tests; the dialog holds Send until the first answer
about the folder, never after a failed one, warns before Send, read with it, and still sends, and
takes a warning away for a folder that can be checked; and a job sent unchecked shows its amber
line for every reason, never on a failed job. MAM-42: the list gives every counted file in path
order with its size, its look and its place among the library folders, the reason it couldn't be
read to the owner alone, pages on the server and counts every view on every page, says the folders
are unknown while no sync stored one, leaves out a file once a sync reports its path, and answers a
view or page it doesn't take with its sentence; Ignore takes the file off the Media folders row's
counts and lists it under Ignored with an audit row, twice records one, Stop ignoring counts it
again, an id no scanned name has — a library's file included — is a bare 404 writing nothing, a
member is refused, and a rescan keeps an ignored file ignored while a moved one counts again; and
the bar's chips match the rows, the dialog's tabs, reasons, Ignore and refusals.

**Acceptance bar:** two hardlink names do not inflate occupied space; movies and episodes with
similar names or years are not merged incorrectly; different cuts are not deleted; conflicting IDs
require manual review. The reference installation has no hardlinks and no two video files of one
size (MAM-17), so there the first criterion and a byte-identical pair of videos rest on the tests,
MAM-19's for the space and the hashing. Met on the reference installation on 2026-10-08 (MAM-22),
with no file moved, changed or removed; the first criterion and the byte-identical pair rest on the
tests as said, and their absence was measured again there: one link for each of the 1,397 files, no
two files of one size, the space adding up to the byte, and inspections that read no file whole, as
no file was a candidate for hashing. For MAM-32: a sync of the reference installation lists each
show once with its seasons; a search for a series through Toloka lists its TV releases matched to it
with the episodes held; and a TV release sent from there lands in the destination its section is
mapped to (MAM-28 since: on 2026-10-12 the Download dialog offered a season of Clarkson's Farm the
Documentary destination its section is mapped to, and sent to TV Shows, picked by hand, it landed
in a new folder at the top of `/media/tv-shows`).
For MAM-18: a scan of the reference installation's `/media` finds the video files MAM-17
counted, every file Jellyfin lists — an episode's included — counts as reported, and the files in no
library are the ones Jellyfin doesn't list; and the inspection after a second scan reads nothing
new. Checked on 2026-10-05 with an image of the change beside production, on a copy of its data with
the media read-only (Vasyl's decision 4): 1,395 files — the 1,394 in the library folders MAM-17
counted, and a software disc image at the root of `/media` — of which the 1,378 Jellyfin lists
matched by path and 17 are in no library; the inspection read only those 17, and a second scan read
nothing. For MAM-33: TMDb with Vasyl's token gives each tab its recommendations on the reference
installation; the checks of Toloka stay within the users' limits; and a download from a
recommendation lands in the destination of its section, its card gone after the next sync; and the
page works in a browser at desktop and phone width. For MAM-34: on the reference installation the
owner links a Tom & Jerry short and sees it identified — its title, overview and poster TMDb's — and
the dialog say so; a series is identified in the background with progress; a documentary episode is
identified through The Open Movie Database or stays in the app with its reason; **Update Jellyfin**,
run once after the upgrade, identifies the planned links (their counts are in the ticket), leaves
the four refused ones listed under **Not identified**, and changes no file or folder — a library
that saves beside the media is refused before any search, and the live check compared three items'
folder listings before and after; the number of confident suggestions in each library is recorded in
the ticket; and the dialog and the TMDb links page work in a browser at desktop and phone width.
For MAM-35: after a sync of the reference installation (**Sync now** or **Refresh all**), the
Library and Search pages show each work's own picture from Jellyfin — the series' on a series card,
its own on an episode of no series, none on the episodes of an opened season and an icon where
Jellyfin has none —, the server fetches each once and then serves it from its disk, nothing is
written to Jellyfin, and both pages work in a browser at desktop and phone width; on production on
2026-10-08 (MAM-22) the header search "Alien (1979)" showed «Чужий» and «Чужий³» under **In your
library** with Jellyfin's own posters. For MAM-20: after
an inspection of the allowed folders, `GET /api/library/duplicates` on the reference installation
lists the movies with several files and the Mickey Mouse Clubhouse halves as copies of one work,
never redundant, with the cuts flagged; the Penguins of Madagascar pairs and S03E31/S03E33 flagged
for review; no works of similar titles or years joined, and no possible match between works whose
ids differ; a byte-identical group only for two files that share their bytes, with no estimate
where the file system isn't known to keep its files apart; and nothing written to the library, the
files or any download client. Checked on the reference installation on 2026-10-08 (MAM-22), after
**Refresh all**: those groups and flags, no works of similar titles or years joined, no possible
match, no byte-identical group, as no two files share a size, no file changed, no audit row written;
the estimate rests on the tests. (MAM-37 since: the Mickey Mouse Clubhouse halves, the cuts, the
Penguins of Madagascar pairs and S03E31/S03E33 are no groups; see its part below.)
For MAM-21: on the reference installation, after an inspection, the
Duplicates page lists those groups under their tabs, each with its evidence, the Mickey Mouse
Clubhouse halves told apart by their paths and the cuts flagged, with no estimate shown where
MAM-20 gives none; the owner confirms, dismisses and undoes a possible match — where the library
offers none, the tests stand for it —, each with its audit row, and a member sees the decision
without the buttons working; nothing is written to the library, the files or any download client;
and the page works in a browser at desktop and phone width — checked on 2026-10-07 in Chrome at
desktop width, 375 and 320px against a throwaway database seeded with groups of every class, as the
owner and as a member; the reference installation's part was checked on 2026-10-08 (MAM-22), as the
owner: the 46 groups on two pages of 25, the empty tabs with their sentences, a Penguins of
Madagascar card with its flags, a Mickey Mouse Clubhouse card (season 3, episode 9, Road Rally)
whose two halves are told apart by their file names, no estimate, and no control that removes,
moves or renames; at desktop width, 375 and 320px the page didn't scroll sideways. Production
offers no possible match, so Confirm, Dismiss and Undo rest on the tests and the 2026-10-07 check,
as the bar allows; so does a member's view. (MAM-37 since: "1–4 of 4", no card of halves, cuts or
Penguins of Madagascar pairs.) MAM-36 has no bar; its **Refresh all** ran on
production on 2026-10-08 (MAM-22) and synced, scanned and inspected with no stop.
For MAM-37: on the reference installation's measured files, `GET /api/library/duplicates` and the
Duplicates page list only real copies of one film or episode — no two episodes of one show at
different places, no episode's "a" and "b" files, no copies whose file names claim years more than
one apart, no two editions — and flag "Running times differ" on the groups whose shortest copy runs
under three quarters of the longest, with the sentence giving both running times; nothing is
written. Checked on 2026-10-08 with the MAM-37 build run on this PC on a copy of production's
database that `tools/mam37/copy_db.py` made, not on a published image (Vasyl's decision 5 that day):
4 of the 46 groups production listed that morning (MAM-22), each under the key it had then and each
with `runningTimes` — The Gods Must Be Crazy (1980), «Ніж у воді» (1962), Seksmisja (1984) and
Alien: Romulus (2024) —; `RunningTimes` on «Ніж у воді»'s alone, the 1 group needing review; on the
Duplicates page "1–4 of 4", and the badge and "The shortest copy runs 26:41, under three quarters of
the longest, 1:35:32." on that card alone, with no sideways scroll at desktop width and 375px; and
the 14 tables the duplicates are read from unchanged at the start and after the check. MAM-43: a
sync stores a Blu-ray folder's clips as its version's files in its library, the works list shows
them, a scan after it lists none in no library and the Jellyfin row counts their room, a DVD gives
only its `VIDEO_TS` files, the clips are named in Jellyfin's terms through a path mapping, a disc
outside the media folders or with none allowed gets no file and the sync succeeds, a film with a file
is never walked, and a second sync forgets a clip gone and every clip once Jellyfin drops the disc.
The folder walk refuses a folder outside the roots, a link leading out and, inside the disc, a link
out of it, and answers a missing folder as unlisted. The adapter keeps a local disc's folder and
nothing else. Each extras folder name and rule is checked, the library's root excepted, and the list
says which one made the library skip a file. MAM-44: the extras leave the counted list and every
count of the sources, one ffprobe couldn't read included, and their room stays; the Extras view lists
them with their places; an ignored extra is listed under Ignored and no longer under Extras. A copy in two
parts, a copy unmeasured or whose reading failed beside measured ones, the keys of a split group and
a possible match, none of which production has, rest on the tests; production has run an image that
holds MAM-37, the published 0.3.0, since 2026-10-11.
MAM-42 has no bar; it was checked on 2026-10-13 on a scratch installation on Windows with real
ffprobe and a scratch media folder holding an ISO and two short videos — the chips and rows, the
dialog, Ignore, a rescan keeping the mark, Stop ignoring and a member's view, at desktop and phone
width —, not on production. MAM-43 has no bar either, and was not tried on production; nor was MAM-44.

**Repair plan for MAM-34 (for the owner's approval; nothing is sent to Jellyfin before his yes).**
Publish 0.2.1; its migration adds `LibraryItemLinks.IdentifiedUtc`. First, with his yes, identify
one film of «Фільми» by hand — the only library with a Ukrainian language, which no live check
covered — and look at it in Jellyfin. Then on the TMDb links page **Update Jellyfin** counts 223
links not identified yet on the reference installation (184 films and cartoons, 3 series, 33
documentary episodes Jellyfin lists as films and 3 more such episodes, the refused "Continental"
links), asks once,
and the background worker identifies them with progress "done of total": 179 films and cartoons (165
cartoons and 14 films, about 2 s each, two at a time), 32 episodes through The Open Movie Database
(about 21 s each), 2 series (about a second per episode, one at a time); 6 items that already show
the whole match — name, year, id, poster and TMDb's overview — are only marked, and three of them
get their own apply when their overview differs; about 213 to 216 applies and 15 minutes in all, and
the app stops a batch by itself when Jellyfin fails to confirm two in a row. The language is each
library's own setting: «Фільми» Ukrainian (about 15 items), «Мульфільми» (about 168) and
«Документалки» (about 33) none, so Jellyfin uses its server's default, English in the live check;
set a library's metadata language in Jellyfin before the run if Ukrainian is wanted. At least four
links are refused by the held-id protection and listed under **Not identified** for the owner to
decide, and any other item holding a different IMDb or TMDb id is refused the same way: three "The
Continental" episode links that all name TMDb season 1 episode 1 (`tt13217676`) — link the right
episode or Remove link —, and the "Polanski + Skolimowski o Noże" link to the 1962 film "Knife in
the Water" (`tt0056291`) while Jellyfin knows the 2003 documentary (`tt1163854`): an earlier build
already wrote the film's TMDb id into that item and **Remove link doesn't take it out**, so link the
documentary's real TMDb entry or fix the id in Jellyfin by hand; Retry would apply the wrong link.
Six links stay "Kept in the app" (Jellyfin episode items, and two episodes TMDb gives no IMDb id).
The owner decides before the run whether the 32 episode-movies go through The Open Movie Database —
the live result replaced the poster of Blitzkrieg by a frame of the video —; if not, that branch is
removed before publishing and those 33 links stay as they are. Verification: the panel's counts, the
**Not identified** tab, a spot check of one cartoon, one series and one episode in Jellyfin, and
about 213 to 216 `LibraryItemIdentified` audit rows; "no file or folder touched" rests on the
refusal of libraries that save beside the media and on the live check's identical folder listings,
and a before-and-after listing script is added only if the owner asks.

What production showed on 2026-10-08 (MAM-22): 216 `LibraryItemIdentified` audit rows in its
database; **Update Jellyfin** with nothing left to identify; and of the links, 220 in the library, 8
kept in the app and 1 not identified — The Continental, season 1 episode 2 —, none queued. MAM-22
read the state production's database was in, which includes the owner's own removing, setting and
identifying of The Continental's links on 2026-10-07 (15:37 to 15:55 UTC), and did not observe the
repair's run itself; MAM-34's bar, which expects four refused links, was not checked by MAM-22.

### Phase C — Safe replacement

**Release:** 0.3.0, published on 2026-10-11, holds MAM-24 to MAM-27, built while 0.3.0 was the
version; MAM-28 checked it on the reference installation on 2026-10-12, and its fix of check 6,
MAM-39 to MAM-41, built the same day, go out when 0.3.0 is published again, under the same number.
**Tickets:** MAM-23 to MAM-28 and MAM-39 to MAM-41.

**Complexity:** High **Value:** upgrade a film, an episode or a season you own in one step: the old
copy moves aside only after the new one is downloaded, verified and visible in Jellyfin, it stays in
a quarantine until you delete it for good, and you can put it back.

**Scope:**

- **MAM-23 What a replacement may touch** (decided 2026-10-08): specification §7 questions 3, 4 and
  6 answered, with the write access and the gates (Vasyl's decisions).
  - *Quarantine:* the old copy the owner picked, with its own companion files (`Alien.srt`,
    `Alien.nfo`), moves into `.mam-quarantine` at the top of the allowed folder that holds it —
    `/media/.mam-quarantine/<job>/movies/…` here —, by one atomic rename per name that never
    replaces and never copies (`renameat2(RENAME_NOREPLACE)`, never .NET's `File.Move`), on one
    mount; never a folder, another video, another video's companions or a disc's clip. It stays
    there, out of Jellyfin's sight and outside the app's own folder scan, until the owner restores
    it or deletes it for good after a second confirmation; no timer and no automatic cleanup. Where
    the move can't be atomic, nothing is deleted: the job stops for review, a departure from §3.4; a
    restart halfway is found again by device and inode and stops for review too. After every move
    and restore the app asks Jellyfin to look at the paths, and writes nothing else into it ([ADR
    0030](docs/adr/0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md)).
  - *Seeding copies:* the app never writes to a download client. An old copy any torrent lists, in
    any state, stops the job before the move with "Still held by the torrent <torrent>: remove that
    torrent in qBittorrent without its files, then press Retry"; the dialog warns of it when the
    copy is picked. That was 823 of the 1394 library video files on 2026-10-04 (one of them still
    downloading); the 571 no torrent holds move without it. Removing a pack's torrent also ends the
    seeding of its other files ([ADR
    0031](docs/adr/0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md)).
  - *Targets and matching:* films, one or more episodes (a range within a season counts as its
    episodes) and the held episodes of whole seasons. A release that matched by identifier, or by
    title and year once the owner confirms the work in the dialog (Toloka lists no ids), may be
    scheduled; a title alone or conflicting ids never; nor a `.torrent` that would land on a file
    already there, nor a magnet. The old copy moves only once Jellyfin, after refreshing the new
    file's item, has given it the old copy's work — the same TMDb or IMDb id, or the same series,
    season and episode —, each ticked old copy paired with exactly one new file of its work;
    anything else stops for the owner, who can correct the new item in Jellyfin and Retry, drop the
    copy or cancel, but never confirm the work by hand after the download. A new episode no ticked
    copy pairs with is simply added ([ADR
    0032](docs/adr/0032-a-replacement-targets-films-episodes-or-seasons-on-the-owners-word-and-goes-ahead-only-when-the-library-gives-the-new-file-the-old-copys-work.md)).
  - *Gates:* off by default. Replacement moves nothing until the media folders are mounted
    read-write, the app runs as their owner (`APP_USER_ID` and `APP_GROUP_ID`, 1000 and 100 here, as
    `tools/mam23/facts.py` measured; no folder's owner or mode changes, and the data volume is
    handed over once — since MAM-39 `PUID` and `PGID`, the data volume handed over by the image
    itself), the owner has switched it on (a setting in the app) and has ticked the
    library folder that holds the old copy, on the Media folders page. Only the owner schedules,
    retries, resolves, restores and deletes for good; members keep **Keep both** ([ADR
    0033](docs/adr/0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)).
- **MAM-24 Scheduling in the Download dialog** (built, release 0.3.0; tried on production on
  2026-10-12, MAM-28, which scheduled a film, a season and the film again there): the owner
  schedules the replacement of specific old copies with a download, and the replacement follows that
  download to `Downloaded`, where MAM-25's check takes it up; MAM-24 itself verifies and moves
  nothing, and writes nothing to the media folders (Vasyl's decisions of 2026-10-08 and 2026-10-09;
  the MAM-24 sections of [ADR
  0031](docs/adr/0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md),
  [ADR
  0032](docs/adr/0032-a-replacement-targets-films-episodes-or-seasons-on-the-owners-word-and-goes-ahead-only-when-the-library-gives-the-new-file-the-old-copys-work.md),
  [ADR
  0033](docs/adr/0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)
  and [ADR
  0034](docs/adr/0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md),
  and of ADR 0005, ADR 0012, ADR 0013 and ADR 0014). *Gates:* the master switch
  (`ReplacementSettings`, one row, and none — off — after the upgrade) and the per-folder ticks
  (`ReplacementFolders`, keyed by the folder's path in the app, never by a row a sync rebuilds) are
  the Media folders page's second card, **Replacing copies**, saved by its one Save with the allowed
  folders in one transaction, with `ReplacementSwitchedOn` or `ReplacementSwitchedOff` and
  `ReplacementFoldersChanged` audit rows; a missing switch or tick list is off and none, a tick must
  be a library folder the mappings bring into the app or one already stored, and a copy counts as
  inside a ticked folder only when every library folder holding it is ticked and inside the Media
  folders. *The dialog:* **Keep both** (the default, changed only by a click) or **Replace after
  verification**, the owner's alone — a member reads "Only the installation owner can replace a
  copy." —, from `GET /api/replacements/options?releaseId=&destinationId=&holdings=`: the matched
  film's every file of every version, or the held episodes a TV release claims at their first place
  within the claim, each version tied to its own library item's place; the release's reasons
  (`SwitchedOff`, `Magnet`, `DestinationNotUsable` with the server's sentence — no category, a
  folder not confirmed inside a library, or one MAM-38's check can't look into —, `NoMatchedWork`),
  a work's (`TitleOnly`, `ConflictingIds`, `NoClaimedSeason`, `NoClaimedEpisodes`) and a copy's
  (`NoAppPath`, `FolderNotTicked`, `OutsideTheClaim`, `AlreadyScheduled`, since MAM-40
  `HeldByRemoval`, `NotInspected`, `SharedWithUnavailableWork`, then from a fresh read of the disk
  `Missing`, `Unreadable` and `ChangedSinceInspection`), one copy per path whatever works list it,
  with the strictest reason;
  edition words and the measured dynamic range per copy, and the edition words of the release's
  title; a copy with another name marked as freeing no room from the first read, in either mode;
  nothing pre-ticked, a season's "Tick every copy that can be replaced"; the title-and-year
  confirmation in the server's words ("This release is Alien (1979)"; twins told apart by their
  libraries, then by their first copy's name, else "(1 of 2)"); Send held, with its reason, until
  every needed tick is there; the summary by work with the paths and the conditions. *Held marks:*
  only once Replace is picked (`holdings=true`) and while a copy can be ticked, every download
  client's whole listing through the new `IDownloadClient.ListJobsWithFilesAsync` (qBittorrent 5.2
  or later, one `torrents/info?includeFiles=true`; an older one is reported as not checked), each
  torrent file mapped by its path and confirmed by device and inode: "Held by the torrent …" (told
  apart by torrent id), a second name of a torrent's file, and the clients not read or read in part
  — nothing uncertain read as "not held", and scheduling still allowed. *The schedule:*
  `POST /api/replacements`, one core with `POST /api/downloads`; the plan reads only the database
  and the disk before the job is written (ADR 0013), each ticked copy read on the disk again; the
  job, the `ReplacementJob` with each ticked file's identity then (device, inode, size, modification
  time, link count), its matched works with their registry ids and the owner's confirmations, and
  the `ReplacementScheduled` and `ReplacementWorkConfirmed` audit rows in one save; a replay
  compared as sets across both routes, a request id one route used refused by the other. *Strict
  send:* a gateway link that turns out a magnet, a category outside every library, any file already
  at a place the release would write — the same bytes included — and every case MAM-38's check
  couldn't look at fail the whole job, nothing handed to qBittorrent; the dialog keeps the form and
  the owner picks Keep both himself. *Following:* after each pass of MAM-14's following, mirrored
  past `Scheduled` only for a download whose check passed (`OverwriteCheck` `Checked`) —
  `Submitted`, `Downloading`, `Paused`, `Downloaded` —; a failed, refused or interrupted send
  `Failed`, a download that needs linking `NeedsReview`, a cancel `Cancelled` with a
  `ReplacementCancelled` row; every job's answer carries `replacement`, and the Jobs page shows one
  line (the full view stays MAM-27's, which builds it below). Every send now records what its
  release turned out to be
  (`DownloadJobs.PayloadKind`). Migrations `AddReplacementSettings` and `AddReplacementJobs` rebuild
  no table. Accepted gaps: strict mode compares a place nothing is at yet only with the torrents of
  the destination's own category, as MAM-38 does, so a torrent of another category writing there
  later is MAM-25's to catch (it does, below); and "already scheduled" is checked before the save,
  not atomically, so two different schedules racing for one copy at the same instant can both pass —
  ADR 0013's kind of gap —, the second then finding the copy gone when it comes to move. Tried live:
  the reference installation's qBittorrent answered the whole listing in 355 ms, 358 torrents with
  848 files, every path mapped into the app through `/downloads` → `/media`; no replacement was
  scheduled, and no mark met a real file. On production on 2026-10-12 (MAM-28) three replacements
  were scheduled — a film, a season of eight copies and the film again with two copies —, the film
  and the season confirmed by title and year, the copy the first film replacement had brought marked
  "Held by" a real torrent's file, the strict send let the three through and refused a fourth whose
  file would land on the restored copy, and a member's dialog offered Keep both only.
- **MAM-25 Verifying the new files** (built, release 0.3.0; tried on production on 2026-10-12,
  MAM-28, where the film's new file and the season's eight passed it): once its download
  is done, a replacement checks each new file before anything is touched, and ends `Verified`, or
  `NeedsReview` / `Failed` with the reason, both copies staying (Vasyl's decisions of 2026-10-10;
  [ADR
  0035](docs/adr/0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md),
  and the MAM-25 sections of ADR 0014, ADR 0017, ADR 0030, ADR 0032, ADR 0033 and ADR 0034). *The
  loop:* `ReplacementVerificationTracker`, a background service of its own, a pass when the app
  starts and 15 s after each pass ends, whatever the switch and the ticks say; it claims a
  `Downloaded` replacement (`Inspecting`, `InspectionStartedUtc`, a `ReplacementCheckStarted` row)
  and takes it a step on every pass from its rows (`IX_ReplacementJobs_Verifying`), so a restart
  goes on where it stood; the following never follows `Inspecting` or `Verified`. *Check 1:*
  qBittorrent read fresh, never the stored rows — the download seeding or complete at a progress of
  1 with its content in its save folder, each wanted video file complete by its own word; an error,
  a download gone, a file not finished or none at all stop for review. *Check 2:* each new file
  placed in the app through the client's mappings and in the one library folder holding it, then
  read by the media inspector inside the Media folders — a regular file, not empty, with a video
  stream and a running time (else `Failed`, the one way to it), of the client's size, and not an old
  copy by identity or path; then the client's whole listing (qBittorrent 5.2 or later) must show no
  other download writing it, by path or by device and inode, closing MAM-24's accepted gap, and
  another download the app can't place — no metadata yet, a save path no mapping reaches — waits,
  then stops. *Check 4:* once every file passed, `POST /Library/Media/Updated` to each library with
  the new files' paths in its own terms, at most 500 to a request, sent through the new optional
  capability `IMediaLibraryFileLookup` and again on the next pass while it isn't taken
  (`LibraryAskedUtc` the moment it was); then every pass reads, in batches alike, each new file's
  item, and each ticked episode copy's, by the id its path computes (`GET /Items/{id}?userId=` of an
  administrator, as a film, an episode, a plain video, then a trailer, an extra read as no work),
  which finds a hidden alternate version that `/Items` leaves out — listed, read once it carries a
  running time (and an episode its number), its ids, an episode's season, number and end number, and
  its series' ids. *Check 3 and the pairs:* a film's item must carry the old work's TMDb or IMDb id,
  the owner's TMDb link counted, and no other value of a scheme the work has; an episode's series
  must be the show by its ids, ADR 0017's way, at the ticked copy's season and number, and the
  ticked episode's own item, as Jellyfin reads it now, still the show's at the number it had when
  scheduled; each ticked copy pairs with exactly one new file of its work, and a new file no copy
  pairs with is simply added — so is a film Jellyfin identifies as another work once every ticked
  copy found its new file, a collection pack's other film (Vasyl's decision of 2026-10-10) —;
  another work (another value in a scheme the work has) while a ticked copy has found none, a new
  episode in another series whatever paired, conflicting ids, ids for a work the app holds no TMDb
  or IMDb id of, a ticked episode Jellyfin now numbers otherwise or places in another series, a film
  item for an episode or the other way round, two new files of one work, none, an old copy that is
  an `a`/`b` half, a file covering a ticked episode and the next — by Jellyfin's end number, read
  for every new file and ticked copy, or a range in its name where Jellyfin gives none —, a name
  claiming another edition than the release (every title the work goes by, and an episode's own,
  taken out first), and a paired file running under three quarters of the old copy's measured
  running time (a longer cut and an old copy never measured pass) stop for review. *The hour:* what
  can't be settled yet waits an hour (`ReplacementVerificationRules`, constants) — from the check's
  start for qBittorrent and the files, from the moment Jellyfin took the request to look for
  Jellyfin, ids only of schemes the work has none of waiting too —, the job staying `Inspecting` and
  the owner free to fix the new item in Jellyfin meanwhile, then stops with what is still missing;
  an unavailable Jellyfin never lets a file pass, and a wrong or conflicting id stops at once once
  every file is read; a new file that changed or went after its check stops it, and so does a call
  the adapter refuses as the app's own mistake. *The end:* a conditional move with the evidence and
  a `ReplacementVerified`, `ReplacementNeedsReview` or `ReplacementFailed` row, no actor, in one
  transaction. *Evidence:* `ReplacementJobNewFiles` and `ReplacementJobNewFileIdentifiers` — each
  new file's client facts, paths, identity, measured video, library item, kind, numbers, ids, work
  and problem — and, on each ticked copy, its pair, its end number, its measured running time and
  its problem; migration `AddReplacementVerification` rebuilds no table. *The Jobs line:*
  "Replacement checks the new file: 1 copy, kept until it is verified.", "New file verified: 1 copy
  will be replaced by it in a later version; nothing is moved yet." with a green tick (MAM-26 below
  rewords it, as the old copy now moves), or "Replacement needs review: <reason>"; the pages ask
  every 2 s while the check runs. qBittorrent is
  only read, and Jellyfin hears only the request to look, every other call a read. Tried live: on
  2026-10-10 a read-only probe read the reference installation's Jellyfin 12.2.0 with the app's
  stored settings — the computed id right for 880 of 880 movies and 440 of 440 episode files, a
  hidden alternate version answered by its id and by no `Items?ids=`, `LibraryMonitorDelay` 60 s,
  the real-time monitor on in all four media libraries, no file with an end number —; on 2026-10-12
  (MAM-28) the film's new file passed it on production, verified 18 s after the app asked
  Jellyfin to look — Jellyfin had been stopped during the check and started again —, and the
  season's eight new files each paired with their old copy by the series' ids and the same running
  time to the second. Accepted gaps: a torrent added after the check isn't
  seen by it; names Jellyfin 12.2 reads as no range (`S02E05-S02E06`) give none, and only `a` and
  `b` are halves; a Jellyfin with case-insensitive ids or path substitution never lists a new file,
  and no Jellyfin lists a sample file, which it ignores, so such checks end in review after the
  hour.
- **MAM-26 The quarantine** (built, release 0.3.0; tried on production on 2026-10-12, MAM-28, a
  film's old copy moved in and put back, nothing deleted for good): right before the move the app
  checks the old copy and every download client's torrents again, then renames the picked video and
  its own companions into `.mam-quarantine` at the top of the allowed folder that holds it, one
  `renameat2(RENAME_NOREPLACE)` per name through a new write contract beside the read-only
  `IFileInventory`, and keeps them there until the owner restores them or deletes them for good; a
  move, a restore or a deletion a restart cut is found again by device and inode and stops for review
  (Vasyl's decisions of 2026-10-08 and 2026-10-10; [ADR
  0030](docs/adr/0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md),
  and the MAM-26 sections of ADR 0005, ADR 0009, ADR 0021, ADR 0031, ADR 0033 and ADR 0035). *The
  loop:* `ReplacementQuarantineTracker`, a background service of its own, a pass right after the app
  starts and 15 s after each pass ends; it settles the cut jobs first, then takes up each `Verified`
  replacement, oldest first (`IX_ReplacementJobs_Quarantine`), then sends the library notices still
  owed; every job it works on is held in `ReplacementJobsInHand`, so an owner's action on it waits,
  and the other way round. Switched off, a verified job is left as it is, nothing read or written.
  *Check 5:* each ticked copy still inside the Media folders and in a ticked folder, no disc clip,
  read fresh from the disk, within its own allowed folder, with the identity it was scheduled with, a
  file itself and not a link, neither a new file's path nor its
  identity, and its paired new file still the one verified (`ReplacementNewFileCheck`, shared with the
  check). *Check 6:* the copy's own companions listed through the write contract
  (`QuarantineCompanions`), then one read of every download client's torrents with their files — a
  client whose mappings reach no allowed folder left out unless the app has sent a replacement to it
  (MAM-28 below) — for every name the move would rename; a
  client not read or read in part stops the job, a video any torrent lists stops it with ADR 0031's
  "Still held by the torrent … then press Retry.", a companion a torrent lists stays, named on the
  job, and a second name of a torrent's file moves. A stop before the move is `NeedsReview` with
  the reason and `QuarantineCheckedUtc`, nothing renamed. *The move:* a claim (`Quarantining`) with
  one `ReplacementJobNames` row per planned name — its place in the quarantine, the allowed folder
  and the identity read right before —; then name by name, videos first, the switch, the ticks and
  the allowed folders read again under the intent's write lock, an intent row committed before each
  rename and an outcome row after; a video that can't move stops the job for review, what moved
  staying in the quarantine, a rename that moved another file put at the name records it `Lost` and
  stops it too, and a companion that can't stays where it is, named on the job; a Retry's run takes
  up the companions a stop left waiting beside a moved video; every video in, `Quarantined`. On Linux,
  `LocalFileQuarantine`: `O_PATH | O_NOFOLLOW` folders from ADR 0009's walk, the name compared by
  `statx` without following it, another mount refused by `STATX_MNT_ID`, the quarantine's folders made
  one level at a time, the folders walked again right before the rename, both folders flushed and the
  new name's identity checked after it; anywhere else every write is refused, so a replacement there
  stops for review. *Restart:* a job left `Quarantining`, `Restoring` or `Deleting` that no one in the
  process holds has each name in the middle looked up by device and inode where it was and in the
  quarantine, recorded where it is (or `Lost`), and stops for review ("The app stopped while moving
  the old copies: 1 of 3 names is in the quarantine."), renaming and deleting nothing — a restore or a
  deletion cut after its last name ends `Restored` or `Completed`, and a look that fails decides
  nothing until a later pass. *Retry:* the
  owner's, for a replacement the move stopped — checks 5 or 6, a refused rename, a cut — back to
  `Verified`, nothing written to the media; a stop of the new files' check is refused, as that Retry
  is MAM-27's (MAM-27 below takes it). *Restore:* the owner's, from `Quarantined` or a review with
  names in the quarantine,
  every name checked first — the switch, the ticks, the file in the quarantine unchanged, nothing at
  its old path, a dangling link included, its folder still there; no folder is created; never after
  a part-way Delete for good took a video —, then renamed back, videos first,
  ending `Restored` with both copies in the library. *Delete for good:* the owner's, from
  `Quarantined`, for exactly the names the second confirmation listed with the room they free, each
  compared again right before its `unlinkat`, no folder removed, ending `Completed`. *Jellyfin:*
  after a move and a restore, `POST /Library/Media/Updated` with the old copy's library paths as
  `Deleted` or `Created` (`LibraryFileChange`), never a quarantine path, owed for an hour while not
  taken (`LibraryNoticeDueUtc`); nothing else. *The page:* the job's **Old copies** section (MAM-27
  below renames it **Replacement**) — every name, where it is now, the room — with **Retry**,
  **Restore** and **Delete for good**, the owner's alone (`GET /api/replacements/{id}`,
  `POST …/retry`, `…/restore`, `…/delete-for-good`); the Jobs line for every new state and the
  owner's Retry on the row; the Media folders page's per-folder hint of whether a rename can work
  there (another mount, a read-only mount, no write permission, or not
  checked), the room each quarantine takes, and the Save that would leave a quarantine holding names
  outside every allowed folder refused. *The scan:* the folder scan leaves out every
  `.mam-quarantine`, at any depth. *The end:* `Quarantining` → `Quarantined`, then `Restoring` →
  `Restored` or `Deleting` → `Completed`, or `NeedsReview`; every job step and every name's intent
  and outcome with an audit row — sixteen new actions, the mover's with no actor, the owner's with
  him —, and the client contract's forbidden words now also moving, relocating, renaming and setting
  a location. Migration `AddReplacementQuarantine` rebuilds no table. Tried live on 2026-10-12
  (MAM-28): the film's old copy moved into `/media/.mam-quarantine` on production's ext4 under
  kernel 7.0.14-22-pve, its one file by one rename keeping inode 390463793, the quarantine's folders
  made 1000:100 with mode 755, and came back with Restore the same way; check 6 stopped the season
  for the torrent that seeds it; the Media folders page's hint read for folders the app's user
  couldn't write to and then for writable ones, and the room of the quarantine; Restore and Delete
  for good disabled while switched off and still offered after a restart. Not tried live: Delete for
  good, as no copy was left in the quarantine to delete — the film's second replacement was
  cancelled at 1.8 % of its download —, and a held copy moving once its torrent is removed and Retry
  pressed. Not tried by the tests either: `EXDEV`, `EINVAL`, `EROFS` and `EOVERFLOW` from the real
  calls (the mount check stops a cross-mount rename first, and the container's file systems all have
  `RENAME_NOREPLACE`), a real read-only bind mount (read from a mount table instead), a mount at
  `.mam-quarantine`, a kernel without `STATX_MNT_ID`, an fsync that fails (the rename stands and is
  recorded) and arm64's open flags.
- **MAM-27 Replacements on the Jobs page** (built, release 0.3.0; tried on production on 2026-10-12,
  MAM-28, on the film's and the season's replacements and a member's view): the Jobs page and
  each job's page show where a replacement stands, its ticked copies with the new file each pairs
  with, what the check found and why it stopped, and the owner keeps both copies, retries a stopped
  check or drops one old copy, each with an audit row (Vasyl's decisions of 2026-10-08 and
  2026-10-11; the MAM-27 sections of [ADR
  0032](docs/adr/0032-a-replacement-targets-films-episodes-or-seasons-on-the-owners-word-and-goes-ahead-only-when-the-library-gives-the-new-file-the-old-copys-work.md),
  ADR 0014, ADR 0031, ADR 0033 and ADR 0035). *The view:* the Jobs line says until when the check
  waits for Jellyfin, and a review's line what to do next by what stopped it — the download, the
  check or the move — and whether a copy only a drop settles is in it, with the dropped copies
  counted; the owner's row has **Retry**, **Keep both** and a **Copies** toggle that opens the old
  copies' table under the row (MAM-41, below, moved the line and the table into the details a
  chevron opens under the row, and dropped the toggle, in the same release). The job's page, its
  section now **Replacement**, has a box of what the owner can do — for a film Jellyfin gave no id,
  a link to the Library page searched by its
  title —, how far the check got with its times, the old copies — each one's pair, status, problem
  and **Drop** — and the new files — how far each got on the disk and in Jellyfin, the ids its work
  is expected to carry and those it was given —, then MAM-26's names under **Files the move
  planned**; both pages ask again every 2 s while the check runs. The server works out each copy's
  status and each new file's progress (`ReplacementJobProgress`), so the pages never pair anything
  themselves (`GET /api/replacements/{id}` and the job's `replacement` widened). *Keep both:*
  `POST …/cancel`, from `Scheduled` to `Verified` and from a review, while no name of the job has
  left its folder — `Cancelled` with "You cancelled the replacement, so nothing will be replaced.",
  its names still `Planned` or `Stayed` forgotten, nothing written to the download job or sent to
  qBittorrent, so the download goes on; **Cancel job** still ends a followed replacement with its
  download. *The check's Retry:* `POST …/retry` now takes a stop of the check back to `Downloaded`,
  the last check's evidence forgotten in the same transaction; the check's next pass reads
  qBittorrent and the files again, asks Jellyfin to look again and gives both hours anew, so
  Jellyfin's ids decide again (its look keeps the ids an Identify set, by Jellyfin's source). A stop
  of the move is retried as MAM-26 built it, and a download the app can't follow has no Retry. *Only
  a drop settles:* Jellyfin numbering the ticked episode otherwise, no new episode at its number, a
  half or a file of several episodes on either side, another edition and a file under three quarters
  of the old copy's running time — the pairing marks such a copy (`OnlyDropSettles`), and while one
  is in a replacement Retry is withheld. *Dropping a copy:* `POST …/copies/{copyId}/drop`, only from
  a review the check or the move stopped, never the last copy or one a name of which left its
  folder; the copy stays where it is (`DroppedUtc`), its names still `Planned` or `Stayed`
  forgotten, and nothing more happens until the owner presses Retry; the check no longer reads,
  pairs or waits on it, the move passes it by — a torrent holding it stops nothing —, another
  download may tick it, and it can't be taken back in. *Pairs:* settled only through the library
  (correct the new item, then Retry) or by a drop; no click confirms a work or an edition by hand,
  and nothing moves until every ticked copy is paired or dropped. *The end:* no new state; three new
  audit actions, `ReplacementCheckRetried`, `ReplacementCancelledAlone` and `ReplacementCopyDropped`
  — the last aimed at the copy, a new target kind —, the owner's with him; each action one
  transaction writing nothing to the media or a client, owner-only by where its route lives, and
  usable while replacement is switched off. Migration `AddReplacementCopyDrops` rebuilds no table.
  Tried live on 2026-10-12 (MAM-28): the lines and the **Replacement** section on production's real
  replacements, Keep both on the season held by its torrent, Retry of the film's stop for an
  unticked folder, Drop only as disabled for the last copy, a member's view, and Cancel job on the
  film's second replacement, after which the job's page kept the **Replacement** section's "Keep
  both" and "Waits for the download." until it was reloaded; the check's Retry after a fix in
  Jellyfin, a Drop done and the drop-only stops rest on the tests and the look at 1280 px and 375 px
  over made-up answers. Accepted gaps: a copy ticked by mistake can't be dropped while the download
  runs, only the whole replacement kept;
  a dropped copy can't be taken back in; until the owner presses Retry after a drop, the line keeps
  the old reason; a new file of a dropped copy still holds the job until Jellyfin has listed, read
  and identified it, as any new file does, since it may be a second file of a copy left — a fix in
  Jellyfin, then Retry, settles it; a title-and-year film with no TMDb or IMDb id keeps Retry for
  its "no id to compare" stop, which can only stop again; and a stop of the move because the new
  file changed after it was verified keeps MAM-26's Retry, which can only stop again — a drop or
  Keep both is the way out.
- **MAM-28 Phase C acceptance** (checked on 2026-10-12): the acceptance bar below checked on
  2026-10-12 on production — the MAM container on the media host, a Proxmox LXC on Linux
  7.0.14-22-pve x86_64 with Docker 29.9.0 and Compose v5.6.0 — running the published 0.3.0 image
  (`sha256:0b75fdbd7f60…`, stamped `0.3.0+2dec720`, published by Vasyl on 2026-10-11), with Jellyfin
  12.2.0, qBittorrent 5.2.4, Toloka's releases and ffprobe 6.1.1-3ubuntu5, Vasyl signed in as the
  owner. Replacement was turned on as the 0.3.0 release note's **Turning it on** says: `/media` was
  a read-write bind of one ext4 disk already, the host's compose file got the `user:` line it lacked
  and a new `.env` the ids 1000 and 100, the data volume was backed up and handed over with the
  note's `chown`, keeping its modes, and the app came up healthy in 7.4 s as 1000:100; with 10001
  the Media folders page had said "The app's user can't write here or in its media folder, so a move
  would fail: run the app as the media folders' owner.", and then said "A move into the quarantine
  can work here: …" for `/media/movies` and `/media/tv-shows`, ticked and saved. The film, «Останній
  бойскаут» (1991), was scheduled from Toloka with its 576p copy ticked and its work confirmed,
  downloaded in about 3 minutes, its new file checked and identified by Jellyfin's ids (IMDb
  tt0102266, TMDb 9319) 18 s after Jellyfin was asked to look, and its old copy moved into
  `.mam-quarantine` keeping its inode, each step in the audit trail, from `DownloadRequested`,
  `ReplacementScheduled` and `ReplacementWorkConfirmed` to `ReplacementVerified`,
  `QuarantineStarted`, `NameMoving`, `NameMoved` and `Quarantined`; Jellyfin's search then listed
  only the new item. Restore put it back (`RestoreStarted`, `NameRestoring`, `NameRestored`,
  `Restored`), and Jellyfin listed both items again, the old one under its old id. Clarkson's Farm
  season 2, eight copies, verified and paired, then stopped with "Still held by the torrent
  “Clarksons Farm Collection” in “qBittorrent”: remove that torrent there without its files, then
  press Retry."; as that torrent seeds seasons 1 to 5, Vasyl kept both copies. Staged on production,
  each leaving the old file where it was: three restarts at rest points (downloading, checking with
  Jellyfin down, quarantined), Jellyfin stopped for about three minutes during the check, the switch
  off before and after the move, `/media/movies` unticked with the replacement verified (a stop for
  review, then Retry), a temporary member's view, and a release whose one file would land on the
  restored copy, refused before anything reached qBittorrent. A second replacement of the film,
  ticking the restored 576p copy and the HEVC one, which the dialog marked as held by its own
  torrent ("Held by …"), was cancelled with Cancel job at 1.8 % of its download, as Vasyl wouldn't
  wait ("Replacement stopped: You cancelled the download, so nothing will be replaced."); so a held
  copy moving after its torrent's removal and Retry, and Delete for good — no copy was left in the
  quarantine to delete —, rest on the tests with the other cases of the bar (below). Vasyl then
  deleted the 576p copy by hand on the host, so the film ends as one copy, the 1080p H.265. At the
  end replacement was switched off with both folders unticked and the quarantine empty, and
  production went back to the image's own ids: the note's `chown` with `10001:10001`, the compose
  file from before the check without the `user:` line, no `.env`, and the app healthy in 7.4 s as
  uid 10001. `tools/mam28/report.py`, run before anything changed, after each stage and at the end,
  found of 1,619 entries only the 576p file gone, the 1080p H.265 file added and the modification
  times of `/media`, `/media/movies` and `/media/tv-shows` changed between the first and the last;
  the replacement jobs end as 1 `Restored`, 2 `Cancelled` and 1 `Failed` (the refused overwrite).
  What was tried live, and what wasn't, is in the README's [Tested with](README.md#tested-with) and
  the release note. Preparing it on 2026-10-12 found that check 6 passed over the torrents of a
  client whose path mappings had been deleted or pointed outside the Media folders since a
  replacement went through it, so a seeding old copy would have moved; fixed in release 0.3.0, which
  keeps its number, by the tests alone, and not in the image published on 2026-10-11 (Vasyl's
  decisions of 2026-10-12; [ADR
  0031](docs/adr/0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md)'s
  MAM-28 section): right before a move, every client the app has sent a replacement to is asked
  whatever its mappings say, and a torrent of one listing a file they put outside the Media folders
  with the name of a file that would move, with no size, or with its size unless that file has other
  names, stops it for review. The `user:` line and the `chown` it was turned on and off with are
  0.3.0 as first published; MAM-39 (below) replaced them in the same release.
- **MAM-39 Run the app as the media folders' owner through PUID and PGID** (built on 2026-10-12,
  release 0.3.0; checked on a local build, not on production yet): the app reaches the media's
  owner's ids the way the rest of a media stack does, through `PUID` and `PGID`, with nothing to do
  on the host (Vasyl's decision of 2026-10-12, inside 0.3.0; [ADR
  0036](docs/adr/0036-the-image-starts-as-root-only-to-hand-its-data-directory-to-puid-and-pgid-then-drops-to-them-with-no-capabilities-before-the-app-starts.md)
  and the MAM-39 section of ADR 0033). *The image:* no `USER`; `docker-entrypoint.sh` starts as root
  only to hand the data directory (`${MAM_Storage__DataDirectory:-/data}`) over to `PUID` and `PGID`
  — 10001, the image's `mam`, when unset —, changing the owner only of what another id owns, never
  of anything mounted inside it (each mount pruned with `mountpoint`, as `find -xdev` alone changed
  the owner of a media folder mounted there from the same disk), and no mode; then
  `exec setpriv --reuid --regid --inh-caps=-all --bounding-set=-all --no-new-privs` into the app,
  keeping the supplementary groups Docker gave the container (`group_add`) but root's 0 through
  `--groups`, `--clear-groups` when there are none, and `HOME=/nonexistent`. 0, 4294967295
  (`(uid_t)-1`, which `setresuid` reads as "keep", root) and anything but digits are refused, and so
  is root without the capabilities it takes (`cap_drop: ALL`), up front with the two ways out — keep
  Docker's default capabilities, or a `user:` line with the data volume already its ids' —, the app
  not started and nothing changed; one log line says how many entries
  changed hands, none when nothing did. Started as another user (`user:`, `docker run --user`), it
  starts the app as that user and logs that `PUID` and `PGID` are ignored, so a compose file of
  0.3.0 as first published keeps working. The app's command is in `ENTRYPOINT`, so arguments after
  the image name still reach it; the health check and `docker exec` now run as root inside the
  container. The compose file loses its `user:` line, and `APP_USER_ID` and `APP_GROUP_ID` are
  gone. *The hint:* each
  library folder of `GET /api/settings/allowed-roots` carries `writeAccess` —
  `{ path, ownerUserId, ownerGroupId, appUserId, appGroupId }`, for an `Available` folder whose
  `renameProblems` holds `NotWritable`, on Linux, when the owner could be read (`statx` on the
  descriptor held, the app's effective ids), else `null` —, the folder named being the ticked one
  when its own permissions refuse, else the allowed folder's `.mam-quarantine`, or the allowed folder
  while it has none; the Media folders page says which `PUID` and `PGID` to set ("The app runs as
  user 10001, group 10001, and can't write to /media/movies, which belongs to user 1000, group 100,
  so a move would fail: set PUID=1000 and PGID=100 for the app in docker-compose.yml or .env, and
  restart it."), that a root-owned folder can't be helped by them, that the app already runs as the
  owner and the folder's permissions are wrong, or, with no ids or the overflow id 65534, the
  general way out. *The note:* step 2 of the 0.3.0 release note's **Turning it on** sets `PUID` and
  `PGID` and restarts the app, and **Going back** takes them out; no backup, `chown` or `user:` line.
  Checked on 2026-10-12 on a fresh local build by `tools/check-image-user.sh`, eight scenarios all
  passing, and in a browser, that image over a copy of a development database at desktop width and
  375 px: before `PUID` and `PGID` the folders of 1000:100 gave the other-owner sentence with "set
  PUID=1000 and PGID=100" and a root-owned folder the root sentence; with `PUID=1000` and
  `PGID=100` the log said "[entrypoint] Handed /data over to 1000:100: 7 files and folders changed
  owner; permissions are unchanged.", the folders of 1000:100 "A move into the quarantine can work
  here: …", a 555 folder of 1000 the already-owner sentence and the root folder the root sentence
  with "(now user 1000, group 100)". Not tried on production, which was turned on and off by the
  first way (MAM-28). Accepted gaps: an installation with `cap_drop: ALL` keeps Docker's default
  capabilities or a `user:` line, its data handed over by hand; and the ids the hint asks for are
  those of the folder the check refused, the owner of every library file only where one user owns
  them all.
- **MAM-40 Settle duplicates on the Duplicates page by removing a copy into quarantine or keeping
  both** (built on 2026-10-12, release 0.3.0; resting on the tests, not tried on production yet):
  production's four groups of one work's copies — The Gods Must Be Crazy, «Ніж у воді», Сексмісія
  and Alien: Romulus — could only be looked at, Phase C could take a copy out only by downloading a
  release to replace it, a same-work group couldn't even be dismissed, and "Same work" said nothing
  to Vasyl (Vasyl's decisions of 2026-10-12, inside 0.3.0; [ADR
  0037](docs/adr/0037-a-copy-is-removed-from-the-duplicates-page-into-the-quarantine-on-the-owners-word-through-replacements-gates-and-checks.md),
  and the MAM-40 sections of ADR 0028 to ADR 0031 and ADR 0033). *Remove this copy:* on each file of
  an "Exact copies" or "Same film, different files" group, the owner's, never a copy the app picks;
  the list gives each such file a verdict from what is stored — the switch, the ticks, an inspected
  readable file, no disc clip, no other work in it, no open replacement or removal holding it,
  another file of the group read as media —, and the request, one at a time, checks that again, the
  identity the page showed, the disk, another copy still there and every download client (check 6),
  then renames the file and its own companions into `.mam-quarantine/<removal id>/…` as a replaced
  copy is moved, Jellyfin told; a video that can't move ends it `NotMoved` with nothing moved, a
  video another file took the place of ends it `NeedsReview`, listed with where to look, and a
  restart in the middle is found again by the quarantine's pass. A copy an open removal holds can't
  be ticked for a replacement (`HeldByRemoval`, ADR 0032's MAM-40 section). *Removed copies:* a tab
  of the Duplicates page, the owner's, with each open removal, the room they take, Restore and
  Delete for good of the whole removal, both disabled with why and refused while replacement is off
  (`switchedOff`); replaced copies stay on Jobs. *Keep both:* on the same two classes, the possible
  match's `Dismissed`, the group's members kept with it, so it moves to "Kept as they are" until a
  new file joins it; a confirmation of copies and any decision on one file's names are refused.
  *Plain names:* "One file, two names", "Exact copies", "Same film, different files", "Maybe the
  same film", "Kept as they are", with one line on each card saying what it is and what can be done.
  A name in the quarantine counts in no group while its path, device, inode, size and modification
  time all match the library's file. Stored in `CopyRemovals` and `CopyRemovalNames`, beside the
  replacements' tables, which keep their shape: the name columns moved into a base class EF doesn't
  map, and the quarantine's checks, write loop, recovery and notices are shared, not copied.
  Accepted gaps: a removal whose video got lost stays listed with no button, settled outside the
  app, holding its copy's path; and a Restore or Delete for good whose video is lost on the way ends
  with that reason.
- **MAM-41 Sort the Jobs page by what waits for you, open a job's details under its row, and keep
  only actions in Actions** (built on 2026-10-12, release 0.3.0; not tried on production, checked in a browser at desktop and
  phone width with the API's answers stood in for): the
  Jobs page listed every job under one status filter, so a replacement stopped for review or old
  copies waiting in the quarantine sat among the finished downloads, a row spread a replacement's
  whole sentence across the table, and MAM-27's **Copies** sat among the actions though it acts on
  nothing (Vasyl's decisions of 2026-10-12, inside 0.3.0; the MAM-41 sections of [ADR
  0014](docs/adr/0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md),
  ADR 0030 and ADR 0032). *Tabs:* **Needs you** — the job needs linking, or its replacement stopped
  for review, waits in the quarantine, or is verified while replacement is switched off —, **In
  progress** — the download still followed, or the replacement still checked or moved, a verified
  one included while replacement is on —, **Finished** and **All**, a job in the first that fits;
  the tab in the address (`?tab=`), so a reload and Back keep it. The server sorts and counts:
  `GET /api/downloads?view=NeedsYou|InProgress|Finished` (anything else 400, left out every job,
  combined with `status`, which stays), and the list answers `needsYouCount` and `inProgressCount`,
  the caller's own jobs whatever view, status and page were asked, the switch read once per request.
  *Rows:* **Release** — the title, with the client, the destination and when it was sent under it
  —, **Download** — the status in a word, a bar only while the download is followed, "Cancelled at
  1.8%", a long reason or error cut at two lines —, **Replacement** — the owner's, a few words per
  state, such as "Check stopped" or "9 old copies in the quarantine" — and **Actions** — verbs only:
  **Retry**, **Keep both**, **Restore**, **Delete for good** and **Cancel job**; the Progress, Sent
  to and Sent columns and the status filter are gone. *Details:* a chevron before the title opens,
  under the row, the whole sentences, the replacement's old copies with their new files and **Drop**,
  the source, the client's id and a link to the job's page, where **Copies** opened the copies before.
  *Restore and Delete for good:* also on the row, by the details' rules (`canRestore` and
  `canDeleteForGood` on a job's `replacement`) and with the job page's confirmations, Delete for good
  sending exactly the names it listed; every Delete for good confirmation, the Duplicates page's
  included, is half as wide again, each file's size on its path's line at the right edge. A member sees the same tabs; a member's jobs carry no replacement,
  so a member's Needs you holds only the jobs that need linking, and the **Replacement** column is the
  owner's. Accepted gap: the page no longer filters by one status, which the API still does.
- **Backup/restore:** replacement jobs, the copies removed on the Duplicates page (MAM-40), the
  decisions on duplicates, the switch, the ticks and the audit trail live in the database;
  quarantined files stay on disk in the quarantine folder, outside the database backup.

**Out of scope:** automatic quarantine cleanup (it needs retention and disk-space policies first),
deletion without a quarantine, Jellyfin's `DELETE /Items/{id}`, any write to a download client —
stopping, removing or moving a torrent included —, copying watched state or collections to the new
copy in Jellyfin, and replacing a disc folder; on the Duplicates page (MAM-40), any copy the app
picks by itself, a bulk removal, and removing a possible match or one file's names.

**Tests:** each failure case of the acceptance bar ends in `NeedsReview` or `Failed` with the old
file untouched; so do a held old copy, an unclear pair, a multi-episode file, the switch off, an
unticked folder, another mount and a restart halfway through a move; a `.torrent` that would land on
an existing file is refused before anything is sent; a member is refused on every replacement route
and no replacement job is written; a successful replacement writes the audit trail and leaves a
restorable quarantined copy, and Restore and Delete for good each write theirs. MAM-24's tests prove
the scheduling half and nothing of the move: a member refused on both replacement routes with
nothing written or asked of qBittorrent; the switch off, an unticked or nested folder, a title
alone, conflicting ids, a missing confirmation, a copy changed or gone since the dialog read it and
each other reason refusing a schedule with nothing written; the job, its replacement, the fresh
identities, the works' ids and the audit rows written in one save before the `.torrent` is fetched;
the strict send failing on any file already there, the same bytes included, and on every unchecked
case, with nothing handed to qBittorrent; the held marks read only when asked, confirmed by
identity, and uncertain whenever a client or a torrent can't be read; and the replacement following
its download to `Downloaded` and stopping there, across a restart (`ReplacementOptionsHttpTests`,
`ReplacementOptionsEpisodesHttpTests`, `ReplacementOptionsHoldingsHttpTests`,
`ReplacementScheduleHttpTests`, `ReplacementScheduleSendHttpTests`, `ReplacementFollowingHttpTests`,
`ReplacementsAreOwnerOnlyHttpTests`, `ReplacementSettingsHttpTests`). MAM-25's tests prove the check
of the new files and still nothing of the move, each stop ending `NeedsReview` or `Failed` with its
reason, its evidence and its audit rows, qBittorrent only read, Jellyfin asked to look (again on the
next pass, within the hour, while it doesn't take the request) and otherwise read, always by its own
paths, and the old copies where they were: a failed download (an error, a download gone, a file not
finished, its client removed or switched off, two downloads of the release), an unknown new path, a
file in no library folder or in two libraries' folders, an unclear pair from a multi-file torrent
(two new files of one film or episode), a failed ffprobe and every other bad file, the old copy
itself, another download writing the new file at its path or by a link at another, one the app can't
place, a wrong id and conflicting ids, ids sharing no scheme with the work's, no id within the hour,
an unavailable Jellyfin and qBittorrent until the hour, a Jellyfin that took the request late given
its hour from then, a multi-episode file by Jellyfin's end number and by its name, an `a`/`b` half,
a ticked episode Jellyfin now numbers otherwise, a ticked copy with no new file, another edition, a
file under three quarters of the old copy's running time, a film and an episode crossed, a changed
or gone new file, a library removed and a library that can't be asked to look; and a passing film,
by IMDb id or the owner's TMDb link, with no edition read from any title the work goes by, a passing
season pack that brings an extra episode, a pack of more files than one request takes, a longer cut,
the owner's fix within the hour, replacement switched off, later passes going on from the rows and a
verified job never read again (`ReplacementVerificationHttpTests`,
`ReplacementVerificationFilesHttpTests`, `ReplacementVerificationEpisodesHttpTests`,
`ReplacementVerificationLibraryHttpTests`, `ReplacementLibraryPlacesTests`, the Core rules' own
tests, and `JellyfinAdapterFilesScanTests`, `JellyfinAdapterFilesReadTests` and
`JellyfinAdapterFilesReadFailureTests` for the adapter). MAM-26's tests prove the move, its way back
and its way out, over HTTP with a stand-in for the write contract and, for the real calls, on Linux
folders in Docker: a changed, gone, unticked or outside copy, a disc clip, the new file itself, a
changed new file, a folder that can't be listed and a companion without an identity each stopping
the job for review with nothing renamed; a video any torrent lists, in any state, two torrents, a
client switched off, out of reach, too old or with torrents it can't place doing the same with only
the listing sent, a held companion staying while its video moves, a second name moving, and Retry
moving the copy once the torrent is gone; a film's video and its own companions moved with their
device and inode, another video's companion left, a season whose second episode can't move keeping
the first in the quarantine, the switch off or a folder unticked between two names, a Media folder
taken out of the list right before a name's intent, another mount, a name taken, a platform that
can't rename and a rename that moved another file each moving nothing more; a Retry moving the
companions a stop left waiting; a restart after a rename and before its outcome, and one between two
names, found again by device and inode with nothing renamed twice, a cut restore and a cut deletion
settled the same way or ended once every name was done, and a look that fails left for a later pass;
Restore putting every name back with its identity, refused with nothing moved by each of its checks,
stopped for review part-way, and Delete for good deleting exactly the confirmed names, refused for
another set, a changed file or the switch off; both still reaching the quarantine after its folder
is replaced by a parent, and refused once it is out of the list; a cut Restore or Delete for good
settled by the next pass; two clicks at once renaming or deleting each name once; Jellyfin told
`Deleted` after a move and `Created` after a restore, never a quarantine path; a member refused on
every new route; the Media folders page's hint, the quarantines' room and a Save that would strand
one refused; the scan skipping every quarantine; and the real rename, restore and deletion keeping
the identity, refusing a changed file, a name taken, another mount, a link, a folder link into
another allowed folder or a quarantine, and a folder swapped right before the rename or the deletion
(`ReplacementQuarantineHttpTests`, `ReplacementQuarantineChecksHttpTests`,
`ReplacementQuarantineHoldsHttpTests`, `ReplacementQuarantineRecoveryHttpTests`,
`ReplacementQuarantineFlowHttpTests`, `ReplacementRetryHttpTests`, `ReplacementRestoreHttpTests`,
`ReplacementDeleteForGoodHttpTests`, `ReplacementDetailsHttpTests`, `ReplacementLibraryNoticeTests`,
`ReplacementHoldingReaderTargetsTests`, `ReplacementSettingsHttpTests`,
`ReplacementsAreOwnerOnlyHttpTests`, `LocalFileQuarantineLinuxTests`,
`LocalFileQuarantinePlacesLinuxTests`, `LocalFileInventoryListingTests`,
`AddReplacementQuarantineMigrationTests`, and `JobDetailsPage.replacement.test.tsx` and
`MediaFoldersPage.test.tsx` for the pages). MAM-27's tests prove the review's view and the owner's
three actions over HTTP: each action with its audit row — Keep both from every state before the move
with the download and qBittorrent untouched, a drop aimed at the copy, the check's Retry forgetting
the last check's evidence —; Retry moving the copy once the holding torrent is gone, and a film
Jellyfin gave no id verified and moved once the owner fixed it in Jellyfin and pressed Retry,
Jellyfin asked to look again; a 409 for every state an action doesn't apply to, a copy only a drop
settles withholding Retry, and the last copy refusing a drop, each with nothing written; a dropped
copy never read, paired, checked or moved again, and tickable by another download; a member refused
on every replacement route, found by the test that discovers the routes and sent over a real stopped
replacement, the owner's and the member's own, with nothing written, renamed or asked; another
user's job a bare 404, even for the owner; and the line and the details by state
(`ReplacementCancelHttpTests`, `ReplacementDropCopyHttpTests`, `ReplacementRetryHttpTests`,
`ReplacementReviewFlowHttpTests`, `ReplacementDroppedCopyHttpTests`, `ReplacementDetailsHttpTests`,
`ReplacementLineHttpTests`, `ReplacementsAreOwnerOnlyHttpTests`, `ReplacementRestoreHttpTests`,
`DownloadsGetHttpTests`, `AddReplacementCopyDropsMigrationTests`, the Core rules' own tests, and
`JobsPage.test.tsx`, `JobDetailsPage.replacement.test.tsx` and `ReplacementCopies.test.tsx` for the
pages). MAM-39's tests prove the hint's ids and the image: the folder the app's user may not write to
— the ticked folder, the quarantine or the allowed folder — named with its owner and the app's
effective ids, read on Linux folders by `statx` on the descriptor held and compared with what
coreutils' `stat` and `/proc/self/status` say, including `/var/mail` (0:8) and `/var/local` (0:50),
whose owner and group differ, so ids read the wrong way round turn the tests red; `writeAccess`
over HTTP for a folder the app may not write to and `null` for every other; each sentence of the
page, the overflow id, a folder without the ids, plain digits and a path with "&" and quotes
(`LocalFileQuarantinePlacesLinuxTests`, `FakeFileQuarantineTests`, `ReplacementSettingsHttpTests`
and `MediaFoldersPage.test.tsx`); and, on a built image, `tools/check-image-user.sh`'s eight
scenarios — the default start, `PUID` and `PGID` handing the data over with modes kept and the app
left no capability and only the `--group-add` group, handing it back with no group left, `--user`,
the refused ids and root without capabilities (`--cap-drop ALL`), a start with nothing to hand
over, a media folder mounted inside the data keeping its owner, and `docker stop` within 5 s. MAM-40's
tests prove the Duplicates page's removal behind the same gates: every rule the list reports
refused again at the request with nothing written, the identity the page showed, a copy changed or
gone on the disk, the other copy gone, a torrent holding the video and a client that can't be read
each 409 with nothing moved, a held companion left where it is, the last two copies removed at once
leaving one, a video that can't move ending `NotMoved`, a stop part-way and a restart's cut found
again by device and inode, Restore and Delete for good with every refusal and the switch off, a
member refused on every new route, a quarantined name left out of the groups and a new file at its
path counted, and Keep both kept until a new file joins (`CopyRemovalHttpTests`,
`CopyRemovalChecksHttpTests`, `CopyRemovalVerdictsHttpTests`, `CopyRemovalActionsHttpTests`,
`CopyRemovalListHttpTests`, `CopyRemovalRecoveryHttpTests`, `LibraryDuplicatesQuarantineHttpTests`,
`LibraryDuplicateKeepBothHttpTests`, `ReplacementsAreOwnerOnlyHttpTests`,
`AddCopyRemovalsMigrationTests`, and `DuplicateGroupCard.remove.test.tsx` and
`DuplicatesPage.removed.test.tsx` for the page).

**Acceptance bar:** a process restart, a failed download, an unknown new path, a multifile torrent,
a failed ffprobe, a wrong ID, an unavailable Jellyfin, a changed old file, active seeding and an
invalid mount mapping never cause silent deletion. A successful unambiguous replacement leaves an
audit trail and a way to restore the quarantined copy. Checked on the reference installation on
2026-10-12 (MAM-28), on the published 0.3.0: the film replaced, each step in the audit trail, and
its old copy restored from the quarantine, while Delete for good wasn't tried, no copy being left in
the quarantine to delete, and rests on the tests; of the ten cases, active seeding was met
naturally (the season), a multi-file torrent on its passing side, an invalid mount as the Media
folders page's hint before the handover, and three restarts at rest points and Jellyfin unavailable
for about three minutes were staged, each leaving the old file where it was; a restart in the middle
of a move, a restore or a deletion, a failed download, an unknown new path, an unclear pair, a
failed ffprobe, a wrong id, Jellyfin unavailable for the whole hour, a changed old file, a held copy
moving once its torrent is removed and Retry pressed, another or a read-only mount and a mapping
changed after a send rest on the tests, as the **Tests** paragraph above and the README's
[Tested with](README.md#tested-with) list.

### Phase D — Practical extensibility

**Release:** 0.4.0 (Vasyl's decision of 2026-10-13). **Tickets:** MAM-29 to MAM-31.

**Complexity:** Medium
**Value:** use the app with other tools than Prowlarr and qBittorrent — Transmission instead of
qBittorrent, Jackett instead of Prowlarr — without losing any of the safety of the first workflow.

**Scope:**

- **MAM-29 Transmission and Jackett samples, and the choice** (decided 2026-10-13): Vasyl's
  decisions. The app isn't tied to qBittorrent or Prowlarr: an installer can run an analogue
  **instead of** either. Phase D's case is an analogue in place of qBittorrent or Prowlarr, not two
  download clients side by side.
  - *Transmission instead of qBittorrent:* Transmission 4.0 or newer, through its legacy RPC
    dialect (kebab-case methods such as `torrent-add`, camelCase fields such as `hashString`). 4.0
    takes labels in `torrent-add`, so a send stays one write
    ([ADR 0013](docs/adr/0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).
    An older Transmission gets a sentence with the version it reports and "needs 4.0 or newer".
    The JSON-RPC 2.0 dialect of Transmission 4.1 gets a ticket of its own when the legacy one is
    removed. Transmission is the specification's one new adapter (the MAM-29 section of
    [ADR 0005](docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)).
  - *Jackett instead of Prowlarr:* through the same Torznab adapter, with no new search adapter.
    What in it fits only Prowlarr is MAM-30's to fix. ADR 0004's "keep Prowlarr rather than add
    Jackett" of 2026-09-29 was about the reference installation, which keeps Prowlarr; Jackett
    instead of Prowlarr is a supported route from Phase D (the MAM-29 section of
    [ADR 0004](docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md)).
  - *Destinations:* Transmission has no categories. Its destinations are the Jellyfin library
    folders it can reach — each library's root mapped back into Transmission's paths through its
    integration's path mappings, and confirmed by Transmission's free-space answer — plus its own
    default download folder, marked "outside the libraries, replacement off". A library folder
    Transmission can't see stays listed with a sentence saying how to fix it, never hidden. A send
    is one `torrent-add` with the folder and the destination's name as its label: a film sent to
    Movies shows in Transmission in, say, `/downloads/movies` with the label "Movies". This amends
    [ADR 0010](docs/adr/0010-a-destination-is-a-download-clients-own-category-never-a-folder.md)
    for a client without categories, and qBittorrent keeps its categories
    ([ADR 0039](docs/adr/0039-a-download-client-without-categories-gets-the-library-folders-it-can-reach-and-its-own-default-folder-as-destinations.md)).
  - *The overwrite guard* reads the one client a send goes to. A guard across several clients was
    dropped, as two clients side by side isn't Phase D's case (the MAM-29 section of
    [ADR 0034](docs/adr/0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)).
  - *Not chosen:* Deluge, which takes two writes per send and applies its label's folder only when
    the download completes. SABnzbd, which needs a Newznab source, can be followed only by hash and
    can't be replaced before unpacking, so Usenet is out of Phase D. A direct Toloka adapter, which
    is a ticket of its own, later.
  - *The live instances:* one Stashboard project on media-stack, `tools/mam29/docker-compose.yml`:
    Transmission 4.1.3 (`lscr.io/linuxserver/transmission:4.1.3-r0-ls363`), a temporary 4.0.6
    (`4.0.6-r6-ls326`) for a short second set, and Jackett v0.24.2830
    (`lscr.io/linuxserver/jackett:v0.24.2830-ls3`, no automatic update) with the Toloka.to indexer
    Vasyl added and "Strip Cyrillic Letters" ticked, Jackett's default. Vasyl ran the scripts in
    `tools/mam29/` on 2026-10-13; they stay in the repo. The test torrents were public (Big Buck
    Bunny and Sintel from webtorrent.io), in a scratch folder outside the media folders.
    Transmission 4.1.3 had no password at the first recording, so a wrong password got 200. Steps
    15 and 16 were recorded again at 15:21 with the password set: a wrong password got 401 with
    `WWW-Authenticate: Basic`, and an unknown Host got 200. No 421 is recorded live: Transmission
    checks the Host only when no password is set, and MAM-30 takes the 421's shape from
    Transmission's source.
  - *What the samples taught:* Transmission 4.0.6 and 4.1.3 answered alike in the short set
    recorded on both: the handshake, `session-get`, `torrent-add` with labels, the duplicate and an
    unknown hash. A `torrent-add` refused with a 409 added nothing. `isFinished` stays false at
    100 %, and a magnet without metadata has `leftUntilDone` 0, so completion needs the metadata
    too. Jackett's Toloka answers carry Prowlarr's titles, descriptions and category ids: of the 38
    releases both listed, all but two that Toloka retitled between the recordings read alike in the
    app's Torznab parsing. "Strip Cyrillic Letters" leaves a Ukrainian-only release's `<title>` as
    punctuation, its name kept in `<description>` alone. The rest is under "What the samples
    taught" in
    [`VendorSamples/README.md`](tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/README.md#what-the-samples-taught).
  - *Toloka and Transmission:* Toloka's rules, FAQ and help section restrict no client, and its
    topic on Linux clients recommends Transmission (read on 2026-10-13). MAM-31's real download is
    the final check: a refusal would show as a tracker error in Transmission.
  - *The live check:* MAM-31 runs on a second, clean MAM on media-stack, set up from zero with
    Jellyfin, Jackett and Transmission only, never on production.
- **MAM-30 Transmission instead of qBittorrent, Jackett instead of Prowlarr:** the Transmission
  adapter — connect, destinations, send, follow, and what holds a copy —, the Torznab adapter's
  Prowlarr-only parts fixed for Jackett, and no "qBittorrent" or "category" where the client is
  Transmission, added without changing the core state machine. Vasyl's decisions of 2026-10-13,
  after the samples: the Search page also reads a release's `<description>`, as Recommendations
  do, so a Ukrainian-only release whose `<title>` "Strip Cyrillic Letters" left as punctuation
  matches its library copy, through Jackett or Prowlarr alike; and only Jackett's default for that
  box, ticked, is supported, which the README tells installers.
- **MAM-31 Phase D on a clean installation:** the clean MAM above, set up by the README alone:
  search through Jackett, the Search and Recommendations pages, a send to Transmission, and one
  real film replaced through the quarantine.
- Plex or Emby later, if needed.
- **Backup/restore:** only the new adapter's settings, and Transmission's destinations with their
  folders, stored like the existing ones.

**Out of scope:** any change to the core replacement workflow to fit the new adapter; two download
clients side by side, and an overwrite guard across several clients; Usenet (SABnzbd, Newznab); a
direct Toloka adapter and Transmission's JSON-RPC 2.0 dialect, each a ticket of its own.

**Tests:** the adapter contract tests pass for both implementations, qBittorrent and Transmission,
and the Torznab adapter's tests pass on Jackett's answers as on Prowlarr's; logical destinations,
error handling and the UI do not depend on qBittorrent-specific fields.

**Acceptance bar:** the second adapter works end to end — search or submit, track, and (for a
client) replace — with the core workflow unchanged: on a clean installation with Jellyfin, Jackett
and Transmission only (MAM-31), Toloka is searched through Jackett, a release lands in a library
folder through Transmission with the destination's label, and a film is replaced through the
quarantine.
