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
