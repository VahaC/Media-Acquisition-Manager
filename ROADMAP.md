# Roadmap

**Status:** Phase A in progress, targeting release 0.1.0. MAM-1 (the scaffold), MAM-2 (UI
localisation), MAM-3 (the Docker image and CI), MAM-4 (accounts and sign-in), MAM-6 (the live
environment facts), MAM-7 (the core model and integration settings), MAM-8 (the Jellyfin
library inventory), MAM-9 (the ffprobe inspection of the library's files), MAM-10 (the Torznab
release search), MAM-11 (qBittorrent's categories and the download destinations), MAM-12 (the
search page of library copies and releases), MAM-13 (sending a release to qBittorrent), MAM-14
(following each download in qBittorrent until it finishes) and MAM-16 (the approved layout: the
search field in the header, numbered pages, versions as rows or a table, one error state per kind of
failure, and a Profile page where every user changes their own password) are built, and MAM-15
checked Phase A's acceptance bar against the live installation on 2026-10-03. MAM-5 (personal
access tokens) is still to do; no phase has shipped yet. Phase B, releases 0.2.0 and 0.2.1, has
started: MAM-17 answered specification §7 questions 3 and 6 against the live installation on
2026-10-04, and MAM-32 (series and episodes in the library, and searching, matching and sending
their releases), MAM-18 (the scan of the media folders for the files no library reported) and
MAM-19 (the room the files take, each file counted once, and the hashing of only the files that
could be copies) are built. MAM-33 (recommendations of the works the library doesn't have yet, from
TMDb, with what Toloka carries of them) and MAM-34 (linking a library work to its TMDb film or show
by hand, Jellyfin then identifying the item as it) are built and ship as release 0.2.1, with
MAM-35 (each library work's own poster from Jellyfin on the Library and Search cards), MAM-36 (the
Library page's Sources card as a status bar, and Refresh all), MAM-20 (the library's files grouped
into the four duplicate classes of the specification) and MAM-21 (the Duplicates page, where the
owner confirms or dismisses a possible match).

This file lists unshipped phases only. When a phase ships, its heading becomes
`### ✅ Shipped (<release>) Phase …`, and its detail later moves to `HISTORY.md`. The phases
follow §6 of the
[development specification](docs/Media_Acquisition_Manager_Spec.md#6-implementation-phases-and-acceptance-criteria);
this file adds the ticket breakdown, the tests and the acceptance bar. Phase A is tracked as tickets
MAM-1 to MAM-16 in the Kaneo project `MAM`, Phase B as MAM-17 to MAM-22 and MAM-32 to MAM-36.
Releases for phases C and D are not decided yet.

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
  the reason, and will keep automatic replacement off in Phase C
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

### Phase B — Scanner and duplicates

**Release:** 0.2.0, and 0.2.1 for MAM-33 to MAM-36 (Vasyl's decisions) and for MAM-20 and MAM-21,
built while 0.2.1 is the version. **Tickets:** MAM-17 to MAM-22 and MAM-32 to MAM-36.

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
  wide, each fetched once and kept; the Search page's library block was not opened live
  (`SearchesLibraryPostersHttpTests` pins its `posterUrl`).
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
  measured durations agree. The room a copy frees is estimated only for byte-identical files whose
  file systems the mount table says share no blocks, with the reason otherwise (decision 3). A
  seeding file is classed like any other, and no download client is asked (ADR 0018). Read from the
  reference library's development database on 2026-10-07, before the design: no file identity,
  hash or measurement yet; 17 movies with several files, 9 of them different cuts and 3 a widescreen
  variant; 20 Penguins of Madagascar pairs and the S03E31/S03E33 pair under one episode's IMDb id;
  the eight Mickey Mouse Clubhouse halves; and no file the scan found. The live check is MAM-22's.
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
  possible matches are decided. No delete, move or bulk action of any kind.
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
  above. MAM-21's decisions on possible matches are the owner's work and live in `mam.db` alone
  (`DuplicateMatchDecisions`, with their audit rows), so they are in its backup.
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
tabs.

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

**Acceptance bar:** two hardlink names do not inflate occupied space; movies and episodes with
similar names or years are not merged incorrectly; different cuts are not deleted; conflicting IDs
require manual review. The reference installation has no hardlinks and no two video files of one
size (MAM-17), so there the first criterion and a byte-identical pair of videos rest on the tests,
MAM-19's for the space and the hashing; the live check (MAM-22) can show the space adding up and an
inspection that reads no file whole. For MAM-32: a sync of the reference installation lists each
show once with its seasons; a search for a series through Toloka lists its TV releases matched to it
with the episodes held; and a TV release sent from there lands in the destination its section is
mapped to. For MAM-18: a scan of the reference installation's `/media` finds the video files MAM-17
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
written to Jellyfin, and both pages work in a browser at desktop and phone width. For MAM-20: after
an inspection of the allowed folders, `GET /api/library/duplicates` on the reference installation
lists the movies with several files and the Mickey Mouse Clubhouse halves as copies of one work,
never redundant, with the cuts flagged; the Penguins of Madagascar pairs and S03E31/S03E33 flagged
for review; no works of similar titles or years joined, and no possible match between works whose
ids differ; a byte-identical group only for two files that share their bytes, with no estimate
where the file system isn't known to keep its files apart; and nothing written to the library, the
files or any download client. For MAM-21: on the reference installation, after an inspection, the
Duplicates page lists those groups under their tabs, each with its evidence, the Mickey Mouse
Clubhouse halves told apart by their paths and the cuts flagged, with no estimate shown where
MAM-20 gives none; the owner confirms, dismisses and undoes a possible match — where the library
offers none, the tests stand for it —, each with its audit row, and a member sees the decision
without the buttons working; nothing is written to the library, the files or any download client;
and the page works in a browser at desktop and phone width — checked on 2026-10-07 in Chrome at
desktop width, 375 and 320px against a throwaway database seeded with groups of every class, as the
owner and as a member; the reference installation's part is MAM-22's.

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

**Open questions (specification §7, for MAM-23):**

- Question 3: can a replacement target an episode or a season, or only a movie?
- Question 4: which quarantine mode and retention period, and is a second manual approval needed
  before a quarantined file is finally deleted?
- Question 6, for replacement: what replacing means when the old copy is a seeding torrent's own
  data, which a replacement must leave untouched (specification §3.4, check 6). MAM-17 measured how
  often that is: 822 of the 1394 video files in the library folders (4.07 of 7.31 TiB) are a
  seeding torrent's own data, 571 belong to no torrent, and no file has a second name
  ([ADR 0018](docs/adr/0018-a-file-is-a-duplicate-whatever-a-torrent-client-does-with-it-and-its-space-counts-once-per-identity.md)).

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
