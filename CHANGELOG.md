# Changelog

All notable changes to Media Acquisition Manager are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[semantic versioning](https://semver.org/) — released as tags of the Docker image
`vahac/media-acquisition-manager`.

## [0.1.0] — 2026-09-28

### Added

- **The solution is scaffolded: an ASP.NET Core API, a React web app and their test projects
  (MAM-1).** `MediaAcquisitionManager.slnx` holds `Api`, `Core` (no package references) and
  `Infrastructure` under `src/`, one test project per source project plus a shared
  `TestInfrastructure` harness under `tests/`, and the React 19 web app (Vite, strict TypeScript,
  React Compiler, React Router 7, TanStack Query) under `frontend/`. The version of record is
  `frontend/package.json`; the Api project's `0.0.0-dev` is only the fallback for unstamped builds.
- **The API serves the web app, and every failure outside the health endpoints answers
  `{ "error": "…" }` (MAM-1).** Paths outside `/api/` fall back to the web app's `index.html`, so
  client-side routes survive a reload. An unknown `/api/` path answers 404 JSON instead of that
  page, so a mistyped API call can't look like a success. An unhandled exception answers 500 with a
  trace id to quote and never echoes the exception text. The health endpoints answer with their own
  report instead (see below). JSON uses camelCase names, enums as strings, and UTC instants with a
  trailing `Z`.
- **Security headers and a hash-pinned Content Security Policy on every response (MAM-1).**
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`
  are set unless an endpoint set its own value. The CSP forbids inline scripts except the theme
  script in `frontend/index.html`, which it allows by SHA-256; `SecurityHeadersHttpTests`
  recomputes the hash from the file, so editing the script without updating the hash fails the
  test suite.
- **Health endpoints (MAM-1).** `GET /api/health` reports that the process is up and runs no
  checks. `GET /api/health/ready` checks that the database opens and has every migration applied,
  and answers 503 with a readable reason per failing check ("The database has 1 pending
  migration.").
- **SQLite storage that sets itself up (MAM-1).** The database `mam.db` lives in the data directory
  (`Storage:DataDirectory`, default `Data`, relative to the content root). Every connection runs in
  WAL mode with foreign keys on and a 5-second busy timeout, and migrations are applied on startup.
  `dotnet ef` uses a design-time factory, and no `dotnet ef` command creates the data directory or
  writes secrets. A migration lock that a killed process left behind is removed once it is two
  minutes old, so an interrupted upgrade can't block every later start.
- **Secrets are generated on first run (MAM-1).** When `Encryption:Key` or `Jwt:Secret` is blank,
  the API reads it from `<data>/.secrets/` or generates it there once (directory 0700, files 0600
  on Linux and macOS), with an atomic write that never overwrites an existing file. Generating
  happens under a lock on `.secrets/.creating.lock`, because on Linux a move without overwrite is
  not atomic: two instances starting at once could each keep a key of their own while only one
  reached the disk (`FirstRunSecretsTests` failed 23 runs in 40 in a Linux container). An empty key
  file stops the startup with a message instead of being replaced, because a new key would orphan
  data encrypted with the old one. An explicit value always wins and writes no file. `Jwt:Secret`
  signs the access tokens of MAM-4; `Encryption:Key` has no consumer until the encrypted integration
  credentials of MAM-7.
- **The web app shell (MAM-1).** A header with the logo (a clapperboard with a download arrow on an
  indigo tile, also the browser-tab icon), the app name and the version, a home page whose service
  status card polls readiness every minute and names the failing check, and a not-found page. Error
  boundaries catch a crashed page, and a code chunk that fails to load after a deploy triggers one
  reload instead of a blank screen. The theme follows the operating system's dark-mode preference
  from the first paint.
- **Test harness (MAM-1).** API tests run over real HTTP through `WebApplicationFactory`, each
  against its own copy of a migrated SQLite template database and a controllable clock.
  `run-tests.sh` runs the .NET suite as parallel shards with TRX and JUnit reports. The frontend
  runs Vitest with Testing Library, including a check that every release note is well-formed.
- **Project docs (MAM-1).** A README, this changelog, a ROADMAP with phases A–D, the development
  specification moved to `docs/`, an ADR template, `.env.example`, and a `.gitattributes` that keeps
  text files LF (Windows scripts stay CRLF).
- **Every text in the web app comes from a translation resource (MAM-2, ADR 0001).** react-i18next
  reads English resources from `frontend/src/locales/en/`, one JSON file per feature, bundled so the
  first render already has its text. English is the only language; the UI runs in the first locale
  the browser prefers whose language has resources, and there is no language picker until a second
  one exists. Adding a language means adding its resource folder and registering it in
  `lib/i18n.ts`. Keys are typed, so a mistyped key fails the build. Dates, numbers and file sizes are
  formatted with Intl in the UI locale (`lib/format.ts`): a British browser gets `28/09/2026`, and
  sizes count in 1024 steps as `7.94 GiB`, the same number qBittorrent and Toloka show. A lint rule
  refuses literal text in component markup. API messages stay English sentences. The `<html lang>`
  attribute and the tab title follow the UI locale.
- **One Docker image, tested by Jenkins CI and published to Docker Hub by hand (MAM-3).**
  `vahac/media-acquisition-manager` holds the API with the built web app in its `wwwroot`, for
  `linux/amd64` and `linux/arm64`, and brings `ffprobe` (Ubuntu's FFmpeg 6.1) for the file
  inspection of MAM-9. The app runs as user `mam` (uid 10001), can't modify its own files, and
  keeps everything persistent in `/data`: the database and the secrets. It listens on port 8118
  inside the container and out, clear of the 8080 that qBittorrent's web UI and many other
  self-hosted services use. The image's health check probes `/api/health/ready`, so `healthy` means
  the database is open and fully migrated, and SQL commands now log at Warning outside Development,
  so that probe doesn't write its queries to `docker logs` every 30 seconds. `docker-compose.yml`
  runs the published image with a `mam-data` volume and read-only media mounts as commented
  examples; `docker-compose.build.yml` builds it from the checkout as `:local`. Both publish the port
  on `127.0.0.1` only by default, because the app speaks plain HTTP and its setup page makes whoever
  opens it first the owner; `APP_BIND_ADDRESS` opens it, and `APP_PORT` changes the host port.
  `deploy.sh` pulls the image, recreates the container and waits until it is healthy. The
  `Jenkinsfile` runs the .NET build and tests (skipped when only documentation changed) and the
  frontend lint, build and tests on every commit to `main`, and pushes nothing. A release is
  `Jenkinsfile.publish`, started by hand with a `VERSION`: it refuses a version that differs from
  `frontend/package.json`, runs the same checks as CI, and pushes `X.Y.Z`, `X.Y` and `latest`.
  Starting it again with a version that is already out rebuilds that release and moves its tags to
  the new image. The API is stamped with the version and the commit (`0.1.0+abc1234`).
  `.env.example` documents the Compose variables, and tests keep it covering every setting of
  `appsettings.json` and every variable the Compose files read. See [PUBLISHING.md](PUBLISHING.md).
- **Accounts and sign-in: the first visitor creates the installation owner, and everyone signs in
  with a user name and a password (MAM-4, ADR 0002).** Until an owner exists every page leads to the
  setup page. `POST /api/setup` writes the owner, the installation row and a `SetupCompleted` audit
  row in one save and signs the owner in; from then on it answers 409 ("This installation is already
  set up. Sign in instead."), also to a second setup racing the first. User names are 3–32 ASCII
  letters, digits, `.`, `-` and `_`, compared ignoring letter case and surrounding spaces; passwords
  are 8–128 characters of any kind, spaces included, with no composition rules (NIST SP 800-63B),
  stored as PBKDF2-HMAC-SHA256 with 600 000 iterations and a 16-byte salt. Sign-in returns a
  15-minute access token (HS256 with `Jwt:Secret`; issuer, audience and algorithm pinned) that the
  web app keeps in memory only, and a refresh token in the `mam_refresh` cookie: `HttpOnly`,
  `SameSite=Strict`, `Path=/api/auth`, and `Secure` only over HTTPS, because a browser won't store a
  `Secure` cookie from a plain-HTTP address. A session lasts 30 days from its last use. Every
  refresh rotates the cookie, and only its SHA-256 hash is stored; the replaced cookie gets 409
  within 30 seconds (two tabs refreshing at once, and the web app retries) and revokes the session
  after that, as a copied cookie would. Every authenticated request checks its session and its user
  in the database, so sign-out, disabling or deleting a user and a password reset take effect on the
  next request rather than when the token expires; sign-out also ends a session that a racing
  refresh had just rotated. Every failed sign-in — unknown user, wrong password, disabled account,
  blank field, or a reset, disable or delete that commits while the password is being checked — is
  the same 401 ("The user name or password is incorrect.") after exactly one password hash, so
  neither the body nor the time tells which user names exist. Sign-in and setup allow 10 attempts a
  minute per client address (429 "Too many attempts. Wait a minute and try again." with
  `Retry-After: 60`); refresh has no limit. The limit counts the connection's address and ignores
  `X-Forwarded-For`, which any client can write: behind a reverse proxy, or when Docker's own proxy
  relays the connection, all clients arrive from one address and share the count, so ten failures a
  minute from anyone keep everyone from signing in; behind a TLS-terminating proxy the refresh
  cookie isn't `Secure` either (README, Security). The web app gains the setup and sign-in pages, a
  gate that restores the session on load (retrying every 3 seconds while the server is unreachable)
  and returns you to the page you opened once signed in — except after a deliberate sign-out — and a
  user menu with Sign out; one refresh runs at a time across tabs (under a Web Lock where the
  browser has one), and signing out in one tab signs out the others. `MAM_Jwt__Secret` has its
  consumer now: rotating it makes the issued access tokens stop validating and open tabs quietly
  fetch new ones, so it signs nobody out, and a value shorter than 32 bytes is refused with a
  message naming the variable.
- **The installation owner manages users, and settings writes are owner-only by where their
  controller lives (MAM-4).** `/api/settings/users` lists users with server-side search (part of the
  name in any letter case, `%` and `_` taken literally) and paging (page size 1–100, default 25),
  creates a user (409 when the name exists in any letter case), disables and enables one, resets a
  password and deletes one. Disable ends every session of the user at once, and enabling doesn't
  revive them; a reset ends every session of the user except the caller's, so the owner resetting
  their own password stays signed in on that device; a delete takes the user's sessions with it. The
  owner can't be disabled or deleted (409). Each change writes its audit row in the same
  transaction, and only when something changed (`UserCreated`, `UserDisabled`, `UserEnabled`,
  `UserPasswordReset`, `UserDeleted`), with the actor's and the target's names copied so the row
  outlives both; the table is generic, for the downloads and replacements of later tickets, and has
  no screen yet. The Users endpoints are owner-only for reading too. Beyond them, a model convention
  makes every action of a controller under `Controllers/Settings` that isn't limited to GET or HEAD
  owner-only — an action with no HTTP method counts as a write — so a settings controller added
  later is gated without an attribute to remember; anyone else gets 403 ("Only the installation
  owner can do this.") and a logged warning. Every API action declares `[Authorize]` or
  `[AllowAnonymous]`, which a test enforces, and an unknown `/api/` path still answers 404
  `{ "error": … }` to everyone, signed in or not. An id that doesn't exist answers a bare 404 with
  no body: the framework's problem-details body for client errors is off API-wide
  (`SuppressMapClientErrors`; a 415 is bodiless too), so it adds no second error format beside
  `{ error }`. A blank or missing field in setup, create or reset answers its rule's sentence, not a
  validation report. The Users page (**Users** in the owner's user menu) searches as you type, pages
  with a page size remembered per device, adds users and resets passwords in dialogs, disables and
  enables at once, and deletes after a confirmation; the owner's own Disable and Delete are
  unavailable, with the reason on hover.
- **The real installation is described, and its services' own answers are the adapters' test
  samples (MAM-6, ADR 0003, ADR 0004).** Jellyfin 12.1, qBittorrent 5.2.3 and the app share one
  media folder, which Jellyfin and the app see as `/media` and qBittorrent as `/downloads`: each
  integration's paths will reach the app through an explicit prefix mapping, and the README and
  `docker-compose.yml` no longer ask for the same path in every service, which that layout makes
  impossible. Toloka is searched through the Prowlarr 2.6.5 already running beside them, not a new
  Jackett, and a live search worked. Prowlarr's download links carry its API key, so they stay on
  the server; Toloka's topic links can carry the forum session, which is dropped before a link is
  stored or shown. Sanitized answers of every service (Jellyfin
  libraries and movies, qBittorrent categories, preferences, torrents and files, Toloka's Torznab
  caps and search, ffprobe of two files) live in
  `tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/` with a README on where each came
  from and what it taught, among others that Jellyfin hides a collection's movies behind one item
  unless `collapseBoxSetItems=false` is sent. `VendorSamplesTests` fails the build if a sample
  carries an API key, a session, a password, a tracker URL or a magnet's tracker list. The questions
  left open (TV shows, the quarantine policy, whether library files are seeding originals) are
  listed under Phases B and C.
- **The installation owner connects Jellyfin, a Torznab source and qBittorrent under Integrations,
  and the core gets a media model and adapter contracts that name no product (MAM-7, ADR 0005,
  ADR 0006, ADR 0007).** One migration (`AddIntegrationsAndMediaModel`) adds ten tables:
  integrations, their path mappings and download destinations, works with their titles and
  identifiers, versions, physical files, releases and download jobs. Only integrations and path
  mappings are written yet; the rest wait for MAM-8 to MAM-14, their shape pinned by persistence
  tests. Every title, identifier, version, file and release names its source — the kind, the
  integration, that integration's name copied, the source's own item id and when it was observed —
  so two sources that disagree stay two rows, and removing an integration keeps what it reported
  with its name. The model refuses three assumptions by its columns: a work has no title, year or
  library id of its own, so a Jellyfin item is only the source of a version (two items of one movie
  can be two versions of one work, one item with two media sources two versions sharing its id); a
  release has no file list and no work, as one release can hold several of both; a physical file row
  is one name of a file, so hardlinks are rows sharing `(DeviceId, Inode)` and count once. A file's
  path inside the app is worked out through its integration's current path mappings when it is
  needed, never stored. Removing a download client keeps its jobs with the client's name; its
  destinations go with it. A release's download link is stored encrypted like a credential, ready
  for the server-side fetch of MAM-13. Core holds `IIntegrationAdapter` and five contracts with
  their Phase A members only: four integration kinds — `IMediaLibraryProvider`,
  `IReleaseSearchProvider`, `IDownloadClient` and `IMetadataResolver` — whose calls return one
  `IntegrationCallStatus`, and `IFileInventory`, the app's own look at its local mounts rather than
  a connection, which answers its own `FileInventoryStatus`; none throws on an expected failure.
  `CoreIsVendorNeutralTests` refuses a product name in any Core namespace, type or member. Adapter types are ids registered by Infrastructure and stored on each
  integration (`jellyfin`, `torznab`, `qbittorrent`); the registry picks the adapter by id
  (Strategy), and a type serves a kind by implementing its contract. The three adapters implement
  only a connection test for now: Jellyfin reads `/System/Info` with the key in its `MediaBrowser
  Token` header ("Connected to Jellyfin 12.1.0 (VahaC media)."); Torznab asks the feed for `t=caps`,
  at the address as entered when its last folder is `api` (Prowlarr's `/1/api`) and with `/api`
  appended otherwise (Jackett's `…/torznab/`) ("Connected to Prowlarr over Torznab; movie search by
  title."); qBittorrent signs in once when a user name is set and reads its version ("Connected to
  qBittorrent v5.2.3.", or "… without signing in" when it lets the app's address in). A failure gets
  a fixed sentence for what happened — unreachable, no connection accepted within 5 seconds or no
  answer within 15, credentials refused, refused for now, the service's own error, or an answer that isn't the
  service's — never an exception's text. The Integrations page (**Integrations** in the owner's user
  menu) searches by name, filters by kind and pages on the server, with the page size remembered per
  device; its dialog picks the service, shows only the credentials it takes, tests the unsaved
  settings with **Test connection**, edits the path mappings of a service that reports file paths,
  and has a **Use this integration** switch that is kept but not yet read by anything; a row's
  **Test** shows its sentence in a banner, and **Remove** asks first and changes nothing in the
  service. The endpoints under `/api/settings/integrations` are owner-only, reads included; a
  connection test answers 200 `{ status, message }` whatever the service said, because the test ran,
  and 409 when a stored secret can't be read or this version no longer connects to the type, whose
  integrations can then only be removed; `PUT` answers 204 and keeps a switch or mappings it isn't
  sent. Credentials are sealed with AES-256-GCM as `v1:` + base64 of a fresh 12-byte nonce, the
  ciphertext and the tag, with the integration's id and the field as associated data, so a value
  copied to another row or column doesn't open. `MAM_Encryption__Key` has its consumer now; like
  `Jwt:Secret` it is read on first use, so a value that isn't base64 of 32 bytes fails the first
  save or test that needs it with a trace id and a log line naming the variable instead of stopping
  the app. No answer carries a secret, only `hasPassword` and `hasApiKey`; a secret is set, cleared,
  or kept by leaving it out, and keeping one never decrypts it. A stored secret that can't be read
  fails only its own integration's test with 409 and a sentence asking to enter it again, is never
  cleared on its own, and is logged as a warning. An API key is 8–256 visible ASCII characters
  without spaces, quotes or backslashes, so it can be masked and sent in a header; a password is up
  to 256 characters without line breaks. Every test message is masked of the call's secrets in the
  adapter and again in the service, and text a service sent is masked before it is cut to length,
  where half a key would no longer match. The integration HTTP clients follow no redirect (the test names
  where it pointed, without the query), keep no cookies, retry nothing and log no request URL,
  because a Torznab key rides in the query; answers are capped at 4 MiB, and XML with a DTD is
  refused. An address is a full http or https address without user info, query or fragment; the
  owner may enter a private one, as these services live on the LAN. Path mappings are ordered pairs
  per integration, at most 20, saved by the dialog's one Save and only for services that report file
  paths; paths are Linux- or Windows-style, matched folder by folder (`/downloads` never covers
  `/downloads2`), minding letter case for Linux paths and not for Windows ones, and `.` or `..` is
  refused. A path mapped twice, service paths of two styles, nested pairs that send one folder to two
  places, or nested pairs from Windows service paths onto Linux paths (they disagree as soon as the
  service reports another letter case) are refused at save, and a stored set that still disagrees resolves as ambiguous rather
  than by the longest prefix; with pairs, a path none covers is not covered, and without any it is
  taken as is (ADR 0003, Implementation). Adding, editing and removing an integration write the
  audit rows `IntegrationCreated`, `IntegrationUpdated` (only when something changed) and
  `IntegrationDeleted` with its name copied; a connection test writes none. The paging rules of the
  Users and Integrations lists are shared (`PagingRules`).
- **The installation owner imports the Jellyfin library, and a sync replaces that library's
  snapshot only after reading all of it (MAM-8, ADR 0008).**
  `POST /api/library/sources/{integrationId}/syncs` answers 202 with the queued run and its
  `Location` (`/api/library/syncs/{runId}`), and the sync runs in the background (Queue/Worker: a
  run row, a queue of 64, one worker taking the runs one at a time, and a reconciler that fails every
  run still queued or running at startup with "The app restarted before this sync finished. Start it
  again."). The start is owner-only; it lives outside `Controllers/Settings`, so it declares
  `[RequireInstallationOwner]`, and `LibraryWritesAreOwnerOnlyHttpTests` sends every library
  endpoint that isn't a GET as a non-owner and expects 403. An unknown integration, or one that isn't
  a media library, is a bare 404; a switched-off one, a type this version can't read a library from,
  or a sync already under way is 409 with its sentence — two starts racing queue one run, as a
  partial unique index (`IX_LibrarySyncRuns_OneActiveRunPerIntegration`) allows one active run per
  integration — and a full queue is 503, its run kept as failed. A sync tests the connection first
  (the version gate, see Changed), then reads Jellyfin's libraries (`/Library/VirtualFolders`) and
  every movie in pages of 50 that overlap by one (`/Items` with `collapseBoxSetItems=false`, and
  `/Videos/{id}/AdditionalParts` for a movie stacked from several files), with GET requests only. A
  total that changes between pages, a short page before the end, a boundary movie that moved, or
  fewer different movies than listed fails the run ("…its library changed while it was being read.
  Nothing was changed here…"), and a failure of any kind stores nothing. Only then, in one
  transaction with the run's success and counts, the library's rows are replaced. Its items are
  grouped into works per library, by IMDb and TMDb id only: items linked by shared ids are one work,
  and a group with two values of one scheme splits by its maximal identifier sets, never by title
  and year. A version is its media source, so merged versions that Jellyfin 10.9 to 10.11 list
  under every item of the group are stored once. Rows are found again by their provenance keys, so
  a work, a version or a file keeps its id and first-seen time across syncs, and an upgraded file
  under a new Jellyfin item id keeps its work through its ids; what the library no longer reports is
  deleted, and so are the works it leaves empty. A sync never puts its facts on a work another
  source holds. Every end state of a run is a conditional write on a run still under way, never
  cancelled; a failure's sentence is masked of the key again, cut to 1000 characters, and logged as
  a Warning with its status only. The adapter reads Jellyfin's answers field by field: the
  displayed and original title with the year, the IMDb and TMDb ids (never a collection id), each
  media source as a version with its path exactly as sent and its size, an edition label only when
  an item has two or more sources, and a file only for a local video file — a DVD or Blu-ray
  folder, a `.strm` link or a remote source gives a version without one. An item that isn't a movie,
  or has no id, fails the page instead of being skipped. One migration (`AddLibraryInventory`) adds
  the `LibraryLocations` and `LibrarySyncRuns` tables, the library of each file and the search
  columns below.
- **The Library page lists every movie the media libraries reported, searched, filtered and paged by
  the server (MAM-8).** **Library** in the header opens it for every signed-in user; the header's
  links and the user menu's entries now come from one list (`lib/nav-items.ts`).
  `GET /api/library/works` pages the works (page size 1–100, default 25) by title in Ukrainian
  alphabetical order, letter case ignored, with Latin titles after the Cyrillic ones: a
  `LIBRARY_TITLE` collation comparing with the `uk` culture is registered on every SQLite
  connection, and falls back to the invariant culture where ICU is missing. `search` finds part of
  a title or an original title in any letter case (upper-cased copies in `NormalizedText` and
  `NormalizedServicePath`, as SQLite folds only ASCII letters; `%` and `_` are taken literally),
  part of an IMDb or TMDb id, a year, or part of a file path. `locationId` keeps the works with a
  file in that Jellyfin library, `identifiers=Missing` those lacking an IMDb or a TMDb id, and a
  filter that is neither answers 400 with its sentence. Each work has its title, original title and
  year (`null`, shown as unknown, when the library gave none), its identifiers with their source,
  the schemes it lacks, and its versions with their files: the path as the service reported it, the
  path in the app worked out through the integration's current path mappings when the list is
  read and never stored (`PathMapper`'s first user, ADR 0003), how it mapped and, without an app
  path, why. `GET /api/library/locations` lists the Jellyfin libraries that hold a file,
  `GET /api/library/sources` every media library integration with its latest and last successful
  run and the number of its files the current mappings give no app path, and
  `GET /api/library/syncs/{runId}` one run (bare 404 when there is none). A run's error can name the
  service's address, a source's or a file's path problem quotes the owner's path mappings, and who
  started a run is the owner's sign-in name, so only the owner gets them; everyone else gets `null`,
  and the page says the owner can see why. The page polls the sources every 2 seconds
  while a sync is under way and refreshes the works once it succeeds. Each movie is a card with its
  identifier badges, a warning badge per missing id, and its versions and files; the sync panel
  shows each library's progress or outcome, the last successful sync after a failure, a warning
  when files have no app path, and **Sync now**, disabled with its reason for anyone but the owner,
  for a switched-off integration, for a type that can't be read and while a sync is under way.
- **The app reads the library's own files, only inside the folders the owner allows, and keeps
  what ffprobe read by each file's identity (MAM-9, ADR 0009).** An inspection runs in the
  background after every successful library sync, when the app starts, and when the owner asks:
  `POST /api/library/inspections` answers 202 with the queued run and its `Location`
  (`/api/library/inspections/{runId}`, readable by every signed-in user, owner-only fields `null`
  for the rest). It is owner-only (`[RequireInstallationOwner]`, swept by
  `LibraryWritesAreOwnerOnlyHttpTests`), 409 with "No media folders are allowed yet. Add the folders
  the app may read under Media folders in the user menu, then inspect again." while no folder is allowed,
  and 503 when the queue is full. At most one run is queued and one running
  (`IX_FileInspectionRuns_OneRunPerActiveStatus`): a request or trigger that finds one queued joins
  it. A run maps every file name the libraries reported through its integration's path mappings
  (`NoAppPath` when they give none), looks at each app path through the file inventory, answers from
  the cache what it holds for the file's device, inode, size and modification time, and reads the
  rest with ffprobe, once per key and two files at a time; each file's outcome is written as it
  comes, and at the end the run deletes the readings no name points at that were its to forget —
  those orphaned before it started and a changed file's old one. A file the run found nothing at
  keeps its last reading, so a mount that was away for a run costs no reading, and a reading a sync
  orphaned while the run went waits for the run that sync queued. A file it can't read is
  an outcome with its sentence — `OutsideAllowedRoots`, `NotFound`, `NotARegularFile`,
  `Unreadable`, `MediaUnreadable` (cached like a success, so a damaged file isn't read again until it
  changes), `IdentityUnknown` or `InspectorUnavailable` (never cached; the files still waiting in the
  run get the same sentence without starting ffprobe again) — never a failure of the run. The
  reconciler fails a run a stop interrupted ("The app restarted before this inspection finished. What
  it had read is kept."), queues a queued one again and queues the start's run; a run left running
  by a worker that couldn't mark it failed is failed by the next run's claim. The local file
  inventory resolves every link on the way, folder by folder (40 at most), and refuses a path, or a
  link, that leaves the allowed roots; it reads the identity with `statx` on Linux (`st_dev` as
  glibc's `makedev`, so it matches `stat -c %d`) and the NTFS file index on Windows. It opens the file
  it checked without following a link (on Linux name by name with `O_NOFOLLOW`, a pipe or a device
  never opened for reading) and asks the open file again after ffprobe reads it, so a file that changed
  meanwhile is not cached. ffprobe reads that open file — on Linux the inherited descriptor, as
  `/proc/self/fd/<n>` — as `file:<path>` with `-protocol_whitelist file` and a `-format_whitelist` of
  the containers films come in, so neither a URL nor a file a crafted list or playlist names is
  opened, through an argument list, for at most 2 minutes and 8 MiB of answer; when it fails, the
  sentence quotes its verdict, the last line it wrote. Its JSON is read field by field: duration, overall bit rate, and
  each video, audio and subtitle track with its codec, profile, size, dynamic range (Dolby Vision from
  its configuration record, else PQ as HDR10, HLG, any other declared transfer as SDR, and unknown
  when the stream declares none), channels, layout, language tag (`und` is none) and its default,
  forced and SDH flags; cover art is not a track, and track titles are not kept. One migration
  (`AddFileInspection`) adds the `AllowedRoots`, `MediaInspections`, `MediaInspectionTracks` and
  `FileInspectionRuns` tables and the link count and inspection columns of `PhysicalFiles` (its
  identity columns came with MAM-7; MAM-9 fills them).
- **The Media folders settings page sets where the app may read files (MAM-9, ADR 0009).**
  `GET /api/settings/allowed-roots` lists the folders and `PUT` replaces them (204), both owner-only,
  as they describe the host; each save that changes them writes an `AllowedRootsChanged` audit row. None by default, so
  nothing is read until the owner names a folder as the app sees it (`/media` in the Docker image's
  examples). A list that breaks a rule answers 400 with its sentence: more than 20 folders, a path
  that isn't full, a `.` or `..` folder, the whole file system, a path in the other operating
  system's style than the app runs on, a folder listed twice or one inside another. Saving starts no
  inspection.
- **The Library page shows each file's verified facts and where the inspection stands (MAM-9).**
  Each file of a version shows its duration, resolution, video codec and profile, dynamic range and
  overall bit rate, then its audio tracks (language, codec, channels, default and forced) and its
  subtitles (language, codec, default, forced and SDH); a value the file didn't give says unknown, and
  a file not inspected yet says so. A file the app couldn't read shows why to the owner and a short
  reason by its status to everyone else. `GET /api/library/works` gives each file an `inspection`
  (`null` until the app has looked), `GET /api/library/sources` each source's inspected, problem and
  not-yet-inspected counts with the first problem's sentence for the owner, and the list where
  inspections stand: whether a folder is allowed, the run under way and the last finished one. The
  sources panel shows that line with **Inspect files**, disabled with its reason for anyone but the
  owner, with no folder allowed and while a run waits — on its own or behind the running one, which
  the line then says (`hasQueuedRun`) — and polls while a run is under way.
- **Every signed-in user searches the release sources, and each source answers for itself (MAM-10,
  ADR 0004, ADR 0005).** `POST /api/releases/search` with `{ text, year, imdbId, tmdbId }` asks every
  enabled release source at once and answers 200 `{ sources }`: each source with its `status`, the
  sentence of a failure (`error`, for the owner only, as it can name the service's address), and the
  releases it listed, in its own order — title, size, seeders and peers, publish date, the page at the
  source, its categories with their names, whether it is a magnet, and the quality and languages the
  title claims (resolution, medium, video codec, dynamic ranges, audio and subtitle languages), marked
  as claims and unknown where the title says nothing. A failing source never hides the others, and a
  source the app can't ask says why (`notSearchedReason`: a stored key it can't read, a type this build
  can't search through, a feed with no search it can use). 400 with its sentence for a missing or
  overlong title, a year that isn't one, or an id that isn't an IMDb or a TMDb id. A switched-off
  source is left alone. Every source's capabilities are read first, and the search goes the way they
  allow: an IMDb or TMDb id alone where the movie search takes one, then the title and the year, then
  the title alone; Toloka through Prowlarr takes only text, so it is asked "Interstellar 2014", in the
  movie categories, for at most 100 releases. The Torznab adapter speaks plain Torznab, with nothing
  specific to Prowlarr or Toloka; Prowlarr's bare 401 to a wrong key, recorded from the reference
  installation, reads as a refused key. Verified live on 2026-10-01: Toloka through the reference
  Prowlarr answered 16 movie releases for "Interstellar" 2014.
- **A release's download link stays on the server, and it holds no copy of the gateway's key (MAM-10,
  ADR 0004, ADR 0007).** Each release a search finds is stored as a new release row, its download
  link sealed to it with the API key replaced by a placeholder that the fetch fills with the
  integration's current key; the browser gets the release's id. Releases stored more than a day ago
  that no download job points at are removed by the next search (ADR 0006). The page link loses the
  owner's forum session (`sid`, in any form), and one that could carry a credential, looks like a
  download or points at the gateway itself is not kept; a listing's id is kept only when it is that
  page link, and is otherwise a SHA-256 digest of it, since Prowlarr writes the tracker's own download
  link, passkey and all, as the id of an indexer that gives none. The payload fetch the download step
  will use (MAM-13) is built: a magnet as it is, a link only from the feed's own origin and only once
  — a redirect to a magnet is the payload, any other redirect is refused — and a feed whose links
  point at another origin fails its search with a sentence naming it.
  `ReleaseSearchSecretsHttpTests` plants the key, the gateway's link token, the session and tracker
  passkeys wherever a feed puts them and finds none in the answers to the owner or a member, in the
  log or in the release rows outside the sealed link.
- **The installation owner keeps download destinations — each a category the download client
  confirms it has, never a folder — with one default for the whole installation (MAM-11,
  ADR 0010).** `/api/settings/destinations` lists them (part of the name in any letter case, page size
  1–100, default 25, ordered by name, then by the client's name), reads one, adds one (201 with its
  `Location`), changes one (204) and removes one (204), and
  `GET /api/settings/destinations/client-destinations/{downloadClientId}` lists what a download client
  offers right now, each category checked. All of it is owner-only, the reads included, as a row
  quotes the client's folders and the owner's path mappings. A destination is a name (up to 64
  characters, unique per client in any letter case) and the client's own category name, stored
  exactly as the client spells it — never trimmed or matched in another letter case, as it is what a
  download will send — and no save path: the table has no column for one and no request carries one.
  Adding a destination, and a change of its client or category, asks the client first: 409
  "“qBittorrent” has no category “Movies 4K”. Create it in the client first, or pick one it has." when
  it has no category spelled exactly so; 502 with the client's own sentence when it was asked and
  couldn't answer (unreachable, timed out, credentials refused, a version too old, …); 409 when it
  can't be asked at all — switched off ("“qBittorrent” is switched off. Switch it on under
  Integrations to use it for destinations."), a type this version can't send downloads to, or a
  stored secret it can't read. A rename or a change of the default alone saves without asking the
  client, so it works while the client is away. A broken rule answers 400 with its sentence ("A
  destination needs a name.", "Pick the download client this destination belongs to.", "Pick the
  category of the download client this destination saves to.", at most 64 and 256 characters, no
  control characters), an id that isn't a download client 404 with "The download client you picked no
  longer exists. Reload the page and pick one again.", and a name the client's destinations already
  have 409 ("“qBittorrent” already has a destination named “Movies”."), also to the loser of two saves
  racing for one name. The first destination becomes the default whatever it asks; a later one only
  with `isDefault: true`, which takes the default from the one that had it in the same transaction.
  Un-marking or removing the default leaves none until the owner picks one, and a partial unique index
  (`IX_DestinationMappings_OneDefault`) keeps two saves that each take it from leaving two. Adding,
  changing and removing write the audit rows `DestinationCreated`, `DestinationUpdated` (only when
  something changed; a default taken off another destination is part of that row) and
  `DestinationDeleted`, with the name copied; a download job sent to a removed destination keeps its
  name and category. One migration (`AddDestinationDefaultAndLibraryFolders`) adds the `IsDefault`
  column and the `LibraryLocationFolders` table (see Changed).
- **Each destination says where its category saves and whether that is inside a media library,
  worked out when it is read and never stored (MAM-11, ADR 0003, ADR 0010).** Every read asks each
  download client on the page once, all of them at once, and answers 200 whatever they said. A
  destination's `check` carries the folder the client says the category saves to, the same folder in
  the app through the client's path mappings, and the media libraries one of whose root folders holds
  it, folder by folder, the root itself included — or a status and its sentence: `OutsideLibraries`
  ("“/media/incoming” is outside every media library folder, so automatic replacement stays off for
  this destination."); `PathUnknown`, when the client doesn't say where or the mappings give no app
  path, with the mapper's reason; `LibrariesUnknown` ("The app doesn't know the media library folders
  yet. Sync a library from the Library page, then look again.", or, when folders are stored but none
  of them maps into the app, a sentence sending the owner to the media library's path mappings
  instead); `CategoryMissing`; `ClientDisabled`, as a switched-off client isn't asked; and
  `ClientUnavailable`, with the client's sentence masked of the call's secrets again. A folder that
  only holds a library folder (`/media` above `/media/movies`) is outside it. Every status but
  `InLibrary` will keep automatic replacement off for the destination (Phase C); it stays usable for
  downloads. On the reference installation's samples the four qBittorrent categories land in their
  four Jellyfin libraries (`/downloads/movies` → `/media/movies`, "Фільми"). The library root folders
  this needs are stored by the library sync now (see Changed), and each is mapped through its own
  Jellyfin integration's current mappings when a destination is checked. A client that fails, or
  whose answer the app can't read, costs only its own destinations their check.
- **qBittorrent lists its categories and the folders they save to (MAM-11, ADR 0005, ADR 0007,
  ADR 0010).** The adapter implements the download client contract now: its capabilities (.torrent
  files and magnet links, categories that have folders, no job id promised by a submission) and the
  destinations. A listing reads `app/version` and refuses a qBittorrent older than 4.4.1 (see
  Changed), reads `torrents/categories` field by field — each category's name as qBittorrent keys it
  and its `savePath`, nothing else — and asks `app/defaultSavePath` only when some category's folder
  sits under the default save path. It never reads `app/preferences`, which carries the Web UI's API
  key and the proxy and mail passwords, and sends nothing but the sign-in and GETs. Each folder is
  worked out the way qBittorrent's own source does for the version that answered: an absolute
  `savePath` as it is, a relative one under the default save path, an empty one as the default save
  path and the category's name — except that from 5.2 a subcategory goes under its parent category's
  folder. From 4.5 qBittorrent lists a Windows host's network folder with `/`, so a save path that
  starts with exactly two `/` is that UNC folder (`//nas/media/Movies` is `\\nas\media\Movies`, and a
  5.2 subcategory without a save path of its own goes inside it); 4.4 lists a save path as it was
  saved, where a leading `//` may be a POSIX folder too, so there it is unknown, as is a category under
  a default save path that starts with `//`. From 4.5 no Windows host lists a backslash, so a save
  path such as `D:\Torrents\Movies` comes from another host, where it is a relative folder under the
  default save path whose name holds the backslashes; under a Windows default save path it is unknown. On 5.0 and 5.1 a subcategory without a save path follows
  an option only `app/preferences` reports, so its folder is unknown, and so is the folder of a name
  qBittorrent would rewrite on disk. A category whose name the app couldn't store or send back intact
  (blank, over 256 characters, a control character) is left out of the list, and an answer that isn't
  qBittorrent's categories fails the listing ("The service at this address answered, but not with
  qBittorrent's categories."). The adapter keeps one session per saved integration in memory, for as
  long as its address and credentials stay the same, so a page that lists destinations doesn't sign
  in on every load. When qBittorrent has forgotten the session — it answers 403, recorded from the
  reference installation — the adapter signs in once more and sends that one read again, once. A
  session it refuses right after handing it out fails the call ("qBittorrent refused the session it
  had just opened.") and is dropped, so the next call signs in. No call repeats a failed sign-in, and
  a sign-in qBittorrent refused — the user name or password (401, or 200 `Fails.`), or the app's
  address banned (403) — is remembered in memory for the integration, by the same fingerprint of its
  address and credentials, so no later call sends it again while they stay the same: qBittorrent
  counts each refused sign-in towards banning the address (five in a row by default), and every page
  load, dialog and save lists destinations. Such a call sends nothing and answers the same status
  with "qBittorrent refused the user name or password the last time the app signed in, so the app
  won't try them again: each refused sign-in counts towards qBittorrent's ban of this app's address.
  Correct them under Integrations, or test the connection there once qBittorrent accepts them." or
  "qBittorrent had banned this app's address the last time the app signed in, so the app won't sign
  in again on its own. Once the ban has ended (qBittorrent's Web UI options set how long), test the
  connection under Integrations.", which the Destinations list, the dialog's categories and a save's
  502 then show. The refusal goes once the address or credentials change, once a call signs in with
  other ones, and once a connection test of the integration signs in with the very same ones; a test
  of other credentials typed into the Edit dialog leaves it, as they may never be saved. A connection
  test qBittorrent refuses is remembered the same way, but never in place of a refusal of other
  credentials. A restart forgets it. A failure qBittorrent didn't count — unreachable, timed out, a server error, an answer
  that isn't qBittorrent's — isn't remembered, and the next call tries again; an unsaved integration
  keeps neither a session nor a refusal, and the connection test still signs in afresh every time
  without asking whether its credentials were refused
  (`QBittorrentAdapterSessionsTests.ListDestinations_twice_with_refused_credentials_signs_in_once_and_answers_the_second_from_memory`).
  Only qBittorrent 5.2 was verified live — the samples and the answer to a forgotten session come
  from the reference installation — and 4.4.1 to 5.1 are accepted from reading qBittorrent's source,
  as is how a Windows host lists a network folder (with Qt's). Verified live on 2026-10-01 through the
  app against the reference installation: the connection test signed in to qBittorrent v5.2.4, the
  category listing answered its four categories, and the destination Movies (with the path mapping
  `/downloads` → `/media`) saves to `/media/movies`, inside the Jellyfin library "Фільми", after a
  live sync stored that library's folders.
- **The Destinations settings page (MAM-11).** **Destinations** in the owner's user menu, right after
  Integrations, lists the destinations with their client, their category and where they save — the
  client's folder, the app's when it differs and the media libraries it lands in, or a warning badge
  with the API's sentence — with a **Default** badge, searched and paged on the server, the page size
  remembered per device. **Add destination** and a row's **Edit** open one dialog: a name, the
  download client (a switched-off one marked), its categories asked from the client once one is
  picked, the chosen one showing where it saves before anything is saved, and **Default
  destination**. On an edit, a category the client no longer has stays choosable, marked missing, so
  the destination can still be renamed. **Remove** asks first, and warns that no destination is the
  default after the default is removed. Adding, changing or removing an integration refreshes the
  destinations too, and the Integrations page's confirmation of a download client's removal says that
  every destination using it goes with it, the default too if it is one of them, while its categories
  stay in the client. Anyone else who opens the page's address gets the API's 403 sentence.

### Changed

- **Removing a media library integration removes the library it imported (MAM-8, ADR 0006).**
  MAM-7 kept everything an integration had reported, with its name. Now
  `DELETE /api/settings/integrations/{id}` also deletes, in the same transaction as the removal and
  its audit row, the titles, identifiers, versions, files and libraries the integration reported as
  a media library, and the works they leave empty. What any other kind of source reported, the
  download jobs sent to it and the library's sync runs stay, with its name. The Integrations page
  says so in the confirmation of a media library's removal.
  `IntegrationsDeleteHttpTests.Delete_keeps_the_facts_it_reported_with_their_source_name` gave way
  to `Delete_of_a_media_library_purges_its_snapshot_and_keeps_its_sync_runs_with_its_name` and
  `Delete_of_a_release_source_keeps_its_releases_with_the_source_name`.
- **The Jellyfin connection test refuses a Jellyfin older than 10.9 (MAM-8, ADR 0005).** It answers
  the new status `UnsupportedVersion` with "This is Jellyfin 10.8.13; the app reads libraries from
  Jellyfin 10.9 or later. Update Jellyfin, then test again.", and a version without a leading
  `major.minor` is refused as one it can't compare; the test still answers 200. A sync runs the same
  test first, so it never reads from an older server.
- **The Use this integration switch is read now (MAM-8).** MAM-7 kept it, but nothing read it. A
  switched-off media library can't be synced (409, and a run queued before the switch fails), while
  the library it imported last stays listed.
- **A successful library sync queues a file inspection (MAM-9).** Once a sync has replaced its
  library's snapshot, it queues an inspection of the files, or joins the one waiting; with no folder
  allowed it queues nothing, and a sync never fails because of it. A sync still never touches what
  the app read itself: a file keeps its identity, link count and inspection across syncs while its
  path stays.
- **`IFileInventory` takes the allowed roots on every call and reads media (MAM-9, ADR 0005).**
  MAM-7's contract described a file inside "the configured roots"; now the caller passes the roots
  it read from the owner's setting, and `InspectMediaAsync` reads the streams. `FileInventoryStatus`
  gained `MediaUnreadable`, `InspectorUnavailable` and `ChangedWhileReading`.
- **A Torznab feed's error text is cut before the app shows it (MAM-10, ADR 0004).** Every address in
  a feed's `<error description>` is cut to its scheme and host, and every credential-named pair
  (`passkey=`, `sid=`, …) is masked, before the text is cleaned and cut to 200 characters: Jackett's
  error 900 and Prowlarr's download error 500 quote the tracker's URL, passkey and all. A connection
  test's sentence that quoted the request (`…/1/api?t=caps&apikey=***`) now names the host only. The
  XML a feed answers is refused when it nests deeper than 32 levels, before it is read.
- **The IMDb and TMDb id rules are shared (MAM-10).** The Jellyfin adapter and the release search
  check ids with one rule (`MediaIdentifierRules`), so a search is never refused an id a library
  holds.
- **The release search contract is decided by the core (MAM-10, ADR 0005).** `ReleaseSearchQuery`
  carries its mode, its limit and only what the source takes, chosen by `ReleaseSearchPlanning` from
  the capabilities, which now say whether the source searches by text, whether its movie search takes
  text, its result limit and which categories hold movies; a hit carries its peers, and its id may be
  missing.
- **`IDownloadClient` carries only what MAM-11 implements (MAM-11, ADR 0005).** MAM-7 gave it every
  Phase A member; now it has `Capabilities` and `ListDestinationsAsync`. `SubmitAsync`,
  `ListJobsAsync` and `ListJobFilesAsync` come back with MAM-13 and MAM-14, which implement them,
  rather than stay as members qBittorrent would have to stub until then; the records they take and
  give stay in Core.
- **The qBittorrent connection test refuses a qBittorrent older than 4.4.1 (MAM-11, ADR 0005).** It
  answers `UnsupportedVersion` with "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1
  or later. Update qBittorrent, then test again.": 4.4.0 lists a category's folder under another name
  (qBittorrent issue #15969), and older versions differ more. A version it can't compare is refused
  rather than guessed, and the test still answers 200. Listing the destinations applies the same gate.
- **A library sync stores each library's root folders (MAM-11, ADR 0008).** With each library, the
  sync now keeps the folders Jellyfin lists for it (`LibraryLocationFolders`), in Jellyfin's order and
  as Jellyfin reports them, in the same transaction as the rest of the snapshot, so a failed sync
  keeps the last good one's. They are replaced on every sync rather than matched, as nothing points at
  a folder; they go with a library the sync no longer reports, and with the integration's snapshot
  when it is removed. Only the destination check reads them: which library holds a file is still
  decided from the folders the sync has just read.

### Notes for upgraders

- **Applying `AddLibraryInventory` logs EF Core warning 20410 once, and it is harmless.** On the
  first start with this change, on a fresh installation as on an existing one, the log shows
  `Microsoft.EntityFrameworkCore.Migrations[20410]`: the operation `PRAGMA foreign_keys = 0` of
  `AddLibraryInventory` cannot be executed in a transaction. Giving each file its library's foreign
  key makes SQLite rebuild the `PhysicalFiles` table, and EF Core switches foreign keys off for the
  rebuild outside the migration's transaction, so a process killed at that moment could leave the
  migration half applied. Nothing can be lost to it: no earlier build writes a row to
  `PhysicalFiles`, so the table rebuilt is empty everywhere. There is nothing to do.
- **File inspection is off until the owner allows a folder (MAM-9).** After the upgrade, open
  **Media folders** in the user menu and add the folder the app sees your media at — `/media` when
  the compose file mounts it there. The first inspection then starts with the next sync, the next
  start of the app, or **Inspect files** on the Library page; it reads every file once, which takes
  minutes on a large library, and later runs read only what changed. `AddFileInspection` rebuilds no
  table, so it logs no warning 20410.
- **Destinations start empty, and their check needs one library sync after the upgrade (MAM-11).**
  No earlier build wrote a destination. `AddDestinationDefaultAndLibraryFolders` adds a column and a
  table and rebuilds none, so it logs no warning 20410. The library folders a destination is checked
  against are stored by the next sync, so until each Jellyfin library is synced once more every
  destination you add says **Media libraries not known yet**. After the upgrade, check that the
  qBittorrent integration has its path mappings (`/downloads` → `/media` on the reference
  installation), choose **Sync now** on the Library page, then add destinations under
  **Destinations** in the user menu. A qBittorrent older than 4.4.1 now fails its connection test:
  update it first.
