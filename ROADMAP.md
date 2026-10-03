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
access tokens) is still to do; no phase has shipped yet.

This file lists unshipped phases only. When a phase ships, its heading becomes
`### ✅ Shipped (<release>) Phase …`, and its detail later moves to `HISTORY.md`. The phases
follow §6 of the
[development specification](docs/Media_Acquisition_Manager_Spec.md#6-implementation-phases-and-acceptance-criteria);
this file adds the ticket breakdown, the tests and the acceptance bar. Phase A is tracked as tickets
MAM-1 to MAM-16 in the Kaneo project `MAM`. Releases for phases B–D are not decided yet.

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
  to Jellyfin. The HTTP tests cover the concurrent asking
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
(Phase B); a second search or client adapter, Plex and Emby (Phase D); TV shows (specification
§7 question 3, which MAM-6 leaves open for Phases B and C).

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
