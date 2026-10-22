# Changelog

All notable changes to Media Acquisition Manager are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[semantic versioning](https://semver.org/) — released as tags of the Docker image
`vahac/media-acquisition-manager`.

## [0.5.0] — 2026-10-16

### Added

- **Every user creates, lists and revokes their own personal access tokens on Profile, for scripts
  and other tools (MAM-5, ADR 0040, Vasyl's decisions of 2026-10-14 and 2026-10-16).** Profile's new
  **Access tokens** card, after **Password**, lists the user's tokens newest first — name, first
  characters (`mam_pat_AbC123…`), access, created, last used and expires, an expired one muted with an
  **Expired** badge until revoked, a revoked one gone — with **Revoke** behind a confirmation and
  **Create token**. Its dialog asks for a **Name** (at most 100 characters), the **Access** —
  **Read only** or **Read and download**, each with a hint — and **Expires**: 30 days, 90 days (the
  default), 1 year or never. It then shows the token once, with **Copy** (on a plain-HTTP address,
  where the browser has no clipboard API, a selected-text copy, else "Select the token and press
  Ctrl+C."), the warning "Copy it now: it won't be shown again.", a `curl` line and **Done**, and
  doesn't close on an outside click meanwhile; while the token is being created it can't be closed at
  all — Escape, the X, an outside click and a disabled Cancel do nothing —, so a token it makes always
  shows its secret. A token is `mam_pat_` and 32 random bytes in base64url
  (51 characters); the database keeps its SHA-256 hash in hex under a unique index and its first 14
  characters, never the value. `GET /api/profile/tokens` lists the caller's tokens that aren't revoked,
  unpaged, as they are one person's; `POST /api/profile/tokens` with `{ name, access, lifetime }`
  answers 201 `{ token, secret }`, or 400 "Give the token a name, such as “Claude on the laptop”.",
  "A token's name can be at most 100 characters.", "Choose the token's access: Read only or Read and
  download." or "Choose when the token expires.", or, for a body that isn't a token — not JSON, or an
  access or lifetime that isn't one of the names —, "Send the token as JSON: a name, access ReadOnly or
  ReadAndDownload, and lifetime Days30, Days90, OneYear or Never."; the create checks the caller's
  session again inside its write transaction, so one racing the owner's reset, disable or delete of
  the user answers the 401 the session's next request gets and stores nothing — no token outlives the
  revocation it raced (ADR 0002's MAM-5 section); `DELETE /api/profile/tokens/{id}` answers 204, and a
  bare 404 for another user's token or one already revoked, so a double click writes one audit row.
  `Profile/PersonalAccessTokensHttpTests`, `Profile/PersonalAccessTokenCreateSessionTests` and
  `Profile/PersonalAccessTokensAccessHttpTests` pin them.
- **A token acts as its user, reads what that user reads, and writes only what an explicit list opens
  (MAM-5, ADR 0040).** A request with `Authorization: Bearer mam_pat_…` is answered as its user — a
  member's token reads only that member's jobs and searches, and an owner-only read refuses it as it
  refuses the member. **Read only** opens every GET the user can reach and the two searches, POSTs
  marked as reads (`POST /api/searches`, `POST /api/releases/search`); **Read and download** adds
  exactly `POST /api/downloads` — always Keep both, as `SendDownloadRequest` has no replacement
  field —, `POST /api/downloads/{id}/cancel` and `PUT /api/recommendations/dismissals/{kind}/{catalogId}`.
  Every other write refuses any token, an endpoint added later included, and so do `GET /api/auth/me`,
  `POST /api/auth/password`, the whole of `/api/profile/tokens` and every endpoint under
  `Controllers/Settings`, reads included (a default put to Vasyl): 403 "An access token can't do this. A
  token reads, and with “Read and download” access also sends downloads, cancels following and marks
  Not interested; everything else is done in the web app. See “Scripts and the API” in the README.", or,
  for a **Read only** token on the three writes, "This access token is read only. To send downloads,
  cancel following or mark Not interested from a script, create a token with “Read and download”
  access on your Profile page; see “Scripts and the API” in the README." A token's cancel that takes
  a job whose replacement is still followed is refused and rolled back, nothing changed (a default
  put to Vasyl): "This job also replaces a copy in the library, and an access token never touches a
  replacement. Cancel it on the Jobs page of the web app; see “Scripts and the API” in the README.";
  a cancel that takes nothing — the job already ended, or still being sent — answers the token the
  409 a session gets, whatever replacement the job carries. Undoing Not interested stays closed (a
  default put to Vasyl). The rule is the default authorization policy,
  read from endpoint metadata (`[PersonalAccessTokenRead]`, `[PersonalAccessTokenWrite]`,
  `[DenyPersonalAccessToken]`), and `SettingsWritesAreOwnerOnlyConvention` adds the deny to every
  Settings action's endpoint metadata.
  `Conventions/PersonalAccessTokenWritesHttpTests` keeps the list fixed and sends every other write of
  the API as the owner, and every refused read, with a token of each kind;
  `Conventions/PersonalAccessTokenConventionHttpTests` proves the rule on test-only endpoints, an
  unmarked one standing for an endpoint added later; `Downloads/DownloadsPersonalAccessTokenHttpTests`
  and `Recommendations/RecommendationDismissalsPersonalAccessTokenHttpTests` the three writes, the
  refused cancel, the 409 of a cancel that takes nothing and the refused undo.
- **The owner sees each user's active tokens and revokes them all (MAM-5, ADR 0040).** The
  Users page has an **Access tokens** column with each user's number of tokens neither revoked nor
  expired, and a **Revoke tokens** action behind a destructive confirmation, waiting with "No active
  tokens." at none. **Disable**, which now revokes the user's tokens, asks first for a user with
  active tokens, saying how many it revokes and that Enable won't bring them back, and disables a
  user without any at once, as before (`UsersPage.test.tsx`). `DELETE /api/settings/users/{id}/tokens`
  answers 204, also when the user has none — then with no audit row —, and a bare 404 for an unknown
  user; it lives under `/api/settings/users`, owner-only by folder, where the ticket wrote
  `/api/users/…`, which doesn't exist. `Settings/UsersTokensHttpTests` pins it.
- **Three audit actions (MAM-5).** `PersonalAccessTokenCreated` (actor the user, target the token by
  its name), `PersonalAccessTokenRevoked` (actor whoever revoked — the user, or the owner for revoke-all,
  a reset or a disable —, one row per token) and `PersonalAccessTokenRefused` (actor the token's user),
  at most one refused row per token a minute, while every refusal logs a warning with its method and
  path; target kind `PersonalAccessToken`.
- **An AI assistant reaches the app through its MCP endpoint, `POST /mcp`, with a personal access
  token (MAM-45, ADR 0041, ADR 0040's MAM-45 section, Vasyl's decisions of 2026-10-14 and
  2026-10-16).** Streamable HTTP in stateless mode through `ModelContextProtocol.AspNetCore` 2.2.0,
  pinned in its own item group — the ticket said 1.x, whose last release, 1.4.1, gets critical fixes
  only. Claude Code connects directly and Claude Desktop through `mcp-remote`, on the home network or
  a VPN; claude.ai and the phone app can't, as their connectors call from Anthropic's cloud. Each
  JSON-RPC message is a POST of its own, served with the request's own scope, user and database
  context, and nothing is kept between them. The server introduces itself as `mam`, "Media
  Acquisition Manager", with the build's informational version, speaks the `2025-11-25` handshake
  Claude Code and `mcp-remote` use as well as `2026-07-28`, and sends instructions that differ by the
  token's access — under 2,000 characters, in English, asking the model to reply in the user's
  language —: the order the tools go in, sending only what the user confirmed, what a `Read only`
  token can't do, and that replacing, quarantining, restoring and deleting copies and every setting
  are the web app's. Personal access tokens only: no token or a refused one gets the API's one 401
  byte for byte, a refused one taking a sign-in attempt of its address as on the API — with the ten
  used up, every token from it gets 429 "Too many attempts. Wait a minute and try again." —, and a
  sign-in's access token 403 "The MCP endpoint takes a personal access token, not a web app sign-in.
  Create a token on your Profile page and connect with it; see “Connect an AI assistant (MCP)” in the
  README." with no audit row (a default put to Vasyl). At most 60 requests a minute per token, then
  429 "Too many requests with this access token: at most 60 a minute. Wait a minute and try again."
  with `Retry-After` (a default put to Vasyl). `tools/list` gives every token the nine reads and a
  `Read and download` token the three writes too; a `Read only` token's call of a write anyway
  answers ADR 0040's read-only sentence as an `isError` result, recorded as the HTTP API records a
  refusal — a warning each naming `tools/call <tool>`, one `PersonalAccessTokenRefused` row a minute
  (a default put to Vasyl). Arguments that don't fit a tool answer "The arguments don't fit
  `<tool>`: `<the binder's reason>` Check them against the tool's input schema and call it again."
  instead of the SDK's "An error occurred invoking …", and aren't logged as an error; the same
  exceptions thrown by a tool itself, past its arguments, stay the SDK's error, logged at Error as a
  bug should be. Every answer is a one-sentence English summary and its data as JSON, in a text
  block and as `structuredContent` matching the tool's output schema — nulls written, instants with
  a trailing `Z`, enums by name, Cyrillic as it is —, and every expected failure an `isError`
  sentence, never a thrown exception. The tools are registered by `McpToolRegistration` rather than
  the SDK's `WithToolsFromAssembly`, which, with hosts built in parallel, could list the caller as a
  required argument; it also describes every instant as a `date-time` string, where the schema
  exporter wrote `true`, which the MCP TypeScript SDK of Claude Code and `mcp-remote` refuses for the
  whole tool list (found by the live check against a local app, 2026-10-16). The SDK's log category is `Warning` in `appsettings.json`, as it writes three
  Information lines per call. `Mcp/McpEndpointHttpTests`, `Mcp/McpToolListHttpTests` and
  `Mcp/McpToolAccessHttpTests` pin it.
- **Twelve tools, each calling the service its web app page calls, as the token's user (MAM-45, ADR
  0041).** For any token: `get_status` — the user, owner or member, the calling token's name, access
  and expiry, the version, the library sources as `GET /api/library/sources` gives them, the latest
  recommendations run and the release sources whose last recommendations call failed, their
  sentences for the owner only, and no Settings health (a default put to Vasyl); `search_library` —
  the Library page's list, 10 a page; `search_releases` — the Search page's stored search, asking
  every enabled source, with its first page of releases, 20 by default and most seeders first, the
  page size and filters checked before any source is asked; `list_search_releases`, Vasyl's twelfth
  tool — one of the caller's searches of the last day filtered, sorted and paged without asking a
  source again, and "There is no search with this id among yours from the last day; run
  search_releases again." for another user's, an older or an unknown one; `get_download_options` —
  the Download dialog's choices, the client and destination it would preselect
  (`DownloadDialogPreselection`, the twin of the dialog's `preselect()` and `destinationOf()`), and
  the overwrite check of that destination or of one asked for, with `sendHeld` where its client
  can't see the folder (a default put to Vasyl), and "This destination isn't one of this release's
  download options; give one of the destinations get_download_options lists for it." for one the
  dialog doesn't offer; `list_jobs` — by the Jobs page's view or a status, with both counts, 20 a
  page; `get_job` — a job with its first 25 files, and "There is no download job with this id among
  yours; list_jobs lists them with their ids." for another user's or an unknown one;
  `list_duplicates` — the Duplicates page's list with the new `sort`, 10 a page;
  `list_recommendations` — one tab with the page's filters, a tab switched off answering its
  sentence, 20 a page. For `Read and download`: `send_download` — a new send refused beforehand,
  nothing written, where the Download dialog disables Send, with the dialog's own sentence "This
  destination's download client can't see its folder, so nothing will be sent there until the owner
  fixes the destination under Destinations in the user menu." (a default put to Vasyl), else the
  HTTP API's send — always Keep both, a `requestId` given again answering the same job, even once
  the folder is out of sight and without asking the client, a Failed job a success whose summary
  says why and that a new send needs a new `requestId` —, listed with
  `anthropic/requiresUserInteraction`, so Claude Code asks before every send whatever its rules
  allow (Vasyl's decision of 2026-10-16); `cancel_job` — stops following, the torrent left in the
  client, a job whose replacement is still followed refused with the HTTP API's sentence, recorded
  and changing nothing; `dismiss_recommendation` — Not interested, answering the mark as it is kept,
  with no audit row, as the page writes none. No tool reaches a replacement, the quarantine, a
  copy's removal, Settings, users or tokens. `Mcp/McpStatusToolHttpTests`,
  `Mcp/McpLibraryToolHttpTests`, `Mcp/McpReleaseToolsHttpTests`, `Mcp/McpDownloadToolsHttpTests`,
  `Mcp/McpJobToolsHttpTests`, `Mcp/McpDuplicateToolHttpTests` and
  `Mcp/McpRecommendationToolsHttpTests` pin them.
- **The token's shown-once step gives the lines that connect Claude Code and Claude Desktop (MAM-45,
  Vasyl's decision of 2026-10-16).** Under the `curl` example, **Connect an AI assistant** shows the
  **Claude Code** command, `claude mcp add --transport http --scope user mam <address>/mcp --header
  "Authorization: Bearer <token>"`, with "Run it once in a terminal; Claude Code then has the app in
  all your projects.", and the **Claude Desktop** configuration for `claude_desktop_config.json`,
  running `npx -y mcp-remote <address>/mcp --transport http-only` (`mcp-remote@0.15.2` since
  MAM-48's fix under **Changed**), with `--allow-http` on a plain-HTTP address only and the header as
  `Authorization:${AUTH_HEADER}` with `Bearer <token>` in `env`, as Claude Desktop on Windows doesn't
  escape the spaces inside `args`, and a hint to paste it into `mcpServers`, restart Claude Desktop
  and have Node.js 18 or later (both hints reworded by MAM-48, under **Changed**). Both carry the
  whole token, unlike the `curl` example, and the page's own address, each in a read-only field with
  a **Copy** and a line of its own saying how it went; a `Read only` token adds "This token is read
  only: the assistant reads what you can, but can't send downloads, cancel following or mark Not
  interested." The step is 48rem wide once the token shows, so the configuration's longest line fits
  whole, and the block wraps on a phone. Each **Copy** still reads "Copy", named "Copy the token",
  "Copy the Claude Code command" and "Copy the Claude Desktop configuration" for a screen reader.
  The development server proxies `/mcp` to the API as it does `/api`.
  `CreateAccessTokenDialog.test.tsx` and `CreateAccessTokenDialog.https.test.tsx` pin it.
- **Every user chooses the web app's language on Profile — As in the browser, English or
  Українська —, saved with the account (MAM-46, ADR 0001's MAM-46 section, Vasyl's decisions of
  2026-10-14 and 2026-10-17).** Profile's new **Language** card, after **Appearance**, says "Saved
  with your account, so it follows you to other browsers and devices. Before you sign in, the
  browser's language is used." Its three choices are **As in the browser** (the default, saved as no
  language), **English** and **Українська**, the two languages named in themselves in either UI
  language and marked with their `lang`. A choice saves at once, with no Save button, as the theme
  does: it shows as picked while it saves, and a failed save goes back with the server's reason, or
  "Could not save your language."; when the UI's language changes the signed-in page mounts afresh in
  it, and otherwise the card says "Saved.". `PUT /api/profile/language` with
  `{ "language": "en" | "uk" | null }` answers 200 `{ language }` as saved, with no audit row, as a
  preference; the property must be there, so a body without it — or one that isn't JSON or whose
  value isn't text — is 400 "Send the language as JSON: language en, uk, or null to follow the
  browser.", and a value other than `en` or `uk`, compared exactly (`UK` and `uk-UA` included), is 400
  "“x” is not a language of the app. Use en, uk, or null to follow the browser.", saving nothing; a
  personal access token is refused (ADR 0040's MAM-46 section). The web app runs, for no choice, in
  the browser's first locale whose language it has, now English or Ukrainian, and for English or
  Ukrainian in the browser's first locale of that language (an `en-GB` browser keeps British dates),
  else in the bare code. It switches when a session is set (sign-in, setup, refresh) and when Profile
  saves, and goes back to the browser's language on sign-out; the browser's other tabs signed in as
  the same user follow at once, over the `mam-auth` channel or the `mam:auth:language` storage key,
  and a tab on another device with its next refresh or as soon as its Profile page opens, which
  reads `GET /api/auth/me` and takes the language it answers unless a save or a newer change came
  meanwhile. While a new token's secret is on screen, a language followed from elsewhere waits until
  the dialog closes, so the remount can't take the secret away. A choice made with the arrow keys
  keeps the keyboard focus on the chosen option across the remount, and "Saved." shows under the
  choices, so they never move. `Profile/UserLanguageHttpTests`, `LanguageCard.test.tsx`,
  `ProfilePage.test.tsx`, `CreateAccessTokenDialog.test.tsx`, `i18n.test.ts`, `auth-store.test.ts`
  and `ProtectedRoute.test.tsx` pin it.
- **The web app speaks Ukrainian (MAM-46, ADR 0001's MAM-46 section, Vasyl's decisions of
  2026-10-17).** Every namespace of `frontend/src/locales/en/` has its Ukrainian file in
  `frontend/src/locales/uk/`, registered beside English and translated by the glossary settled
  first, `docs/i18n/glossary-uk.md`: the style (the user addressed as «ви», infinitives on buttons, «…» quotes,
  the ’ apostrophe, product names as they are), the counted words in Ukrainian's four plural forms,
  the units, and the terms, four of them chosen by Vasyl on 2026-10-17 — folder «тека», library
  «медіатека», work «твір», episode «серія». Dates, numbers and file sizes follow the Ukrainian
  locale through `lib/format.ts`. For the translation the English side changed too: counted text
  takes plural forms and `count` (`libraryLinks`' `run.runningCounts`, `catchUp.progress` and
  `confident.identifying`, `recommendations`' `reasons.recommendedOneMore` and
  `reasons.recommendedMore`); "exactly one" has a key of its own where Ukrainian's `_one`, which also
  means 21 or 31, can't say it (`reasons.missingSeason`, `replace.heldByOneCopy`, the Jobs page's
  `…OneCopy` keys); `replace.workPlace` takes the formatted `{{place}}` and `{{total}}`; and a
  language name Intl writes in lower case is capitalised where it starts an option or a label
  (`capitalizeFirst`, `languageLabel`). Every API request sends `Accept-Language` with the UI's
  locale, for MAM-47. `src/locales/locales.test.ts`, through `src/locales/parity.ts`, holds every
  Ukrainian namespace to its English one — the same keys, the four forms of each English plural, the
  same placeholders, `<Trans>` tags and leading and trailing whitespace, no empty value, and no
  English quotes or ASCII apostrophe in Ukrainian text —; `src/test/ukrainian-pages.test.tsx` renders
  every route of the web app in Ukrainian on canned answers, with no English fallback, and fails on a
  missing key, a raw key, an unresolved placeholder or English text on screen;
  `src/test/plural-probe.ts`'s probes count the changed keys in Ukrainian, and `api.test.ts` pins the
  header.
- **The owner chooses the language of the TMDb catalog (MAM-46, ADR 0025's and ADR 0005's MAM-46
  sections, Vasyl's decisions of 2026-10-14 and 2026-10-17).** The TMDb integration's dialog has a
  **Catalog language** select under the credentials: "Titles, overviews and posters on the
  Recommendations page and in the TMDb link dialog come in this language where TMDb has them,
  otherwise in the original. The release sources are searched by these titles too, so pick the
  language your trackers name films in. After a change, the app reads the catalog again at its next
  computation." It offers every language TMDb keeps translations in, 144 tags (decision 1 of
  2026-10-17), named in the UI language — a tag Intl has no name for, such as TMDb's `cn-CN`, shown
  as the tag —, Ukrainian and English (United States) first, the other Englishes next, then the rest
  in the UI language's alphabetical order. A new TMDb starts at the UI language's (`uk-UA` for
  Ukrainian, the browser's own English such as `en-GB`, else `en-US`), an edit at the stored one, kept
  in the list even if TMDb no longer lists it — the server takes it back as unchanged, in any letter
  case, so a rename is never refused for it —, saved with the rest by **Add integration** or **Save
  changes**. The Integrations list shows "Catalog language: <name>" under the TMDb row's kind.
  `GET /api/settings/integrations/adapter-types` gives `catalogLanguages`, TMDb's list in its order,
  for `tmdb` and `null` for every other type; an integration carries `catalogLanguage`, `null` but for
  a catalog. `POST` and `PUT /api/settings/integrations` take `catalogLanguage` in any letter case and
  store it as TMDb spells it; a create without one, or with a blank one, starts in the owner's saved
  language, else in the request's first `Accept-Language` tag, else in `en-US`, each brought to the
  closest of the list (`uk` → `uk-UA`, `de` → `de-DE`, `en` → `en-US`), and an update without one
  keeps the stored one. 400, checked after the credentials and path mappings and before the name:
  "“x” is not one of TMDb's catalog languages. Use one of the tags its adapter type lists, such as
  en-US or uk-UA." and, for a type that is no catalog, "Jellyfin has no catalog language to choose.
  Leave catalogLanguage out." (with that type's name). TMDb's list is recorded as a vendor sample,
  `Tmdb/configuration-primary-translations.json`. `Settings/IntegrationsCatalogLanguageHttpTests`,
  `Settings/IntegrationsHttpTests`, `TmdbAdapterLanguageTests`, `CatalogLanguageRulesTests`,
  `IntegrationDialog.test.tsx`, `IntegrationsPage.test.tsx` and `catalog-languages.test.ts` pin it.
- **Every sentence the server writes reads in the language of the page, and every API error carries a
  stable code (MAM-47, ADR 0042, ADR 0001's MAM-47 section, Vasyl's decisions of 2026-10-19).** An
  error after a failed save or a refused request, why a job stopped or waits for the owner, a
  replacement's or a removal's reason, a check's or an inspection's problem, a sync's, a scan's or a
  run's failure, a disabled action's reason, a connection test's answer, the health descriptions and
  the MCP tools' answers come in the language the request's `Accept-Language` asks for — `uk` (or
  `uk-UA`, or a list that ranks it above English) for Ukrainian, anything else or nothing for
  English —, which
  the web app sets to the language it runs in: they read in Ukrainian on a Ukrainian page, a job that
  stopped before the update included, and follow a switch on Profile without a reload, as the web app
  fetches again the answers on screen (`lib/query-client.ts`). The account's saved language isn't
  read, so a script, a token or an MCP client gets English unless it sends the header, and the 401 of
  every cause and the sign-in failures stay one answer per header, byte for byte. A service's own
  words quoted in a sentence stay as the service wrote them; Jellyfin's and qBittorrent's menus named
  in a Ukrainian sentence are their own Ukrainian labels, Transmission's and Jackett's their English
  ones. Every error body is `{ "error": "<sentence>", "code": "<key>" }`, the code being the sentence's
  key in the server's locale files — `account.sign-in-required`, `account.owner-only`,
  `common.paging.page-size-invalid`, `downloads.release-missing` … —, the same in every language and
  release: the README's "Errors, codes and languages" lists the common ones, and its API surface the
  code of every sentence it quotes. A body or a query value the framework can't bind keeps ASP.NET's
  own validation problem, English and without a code (Vasyl's decision). A library source's
  `pathProblemCode`, its inspection's `problemCode`, and a TMDb dialog item's `cannotWriteReasonCode`
  and `writeErrorCode` carry the key beside their sentence, and the Library page and the TMDb dialog
  compare those instead of the words. The sentences — about 970 keys in 20 files a language — live
  in `src/MediaAcquisitionManager.Api/Locales/en/` and `uk/`, embedded in the image, in the web app's
  shape: placeholders, a hint for a number, a size, a running time, a list, a nested sentence or a
  title that may be the label of a work with none, and the four Ukrainian plural forms; the Ukrainian
  follows the glossary's new "Server sentences" section. Ukrainian writes numbers as the web app does
  (`1 234`, `0,5`), sizes in their exact unit (`8 МіБ`) and running times as the server's clock
  (`0:45:00`). Logs, the release notes and what an AI assistant's model reads — the MCP server's
  instructions and the tools' descriptions — stay English. `Texts/RequestLanguageHttpTests`, the Ukrainian 401s of `Auth/AuthSignInHttpTests` and
  `Auth/PersonalAccessTokenAuthenticationHttpTests`, `Texts/MessageRendererTests`,
  `Texts/EnglishFormatPinTests`, `Replacements/ReplacementConfirmationLabelTests`,
  `query-client.test.ts`, `LibrarySyncPanel.test.tsx` and `TmdbLinkEditor.test.tsx` pin it, with one
  class per area that spells the Ukrainian a reader sees and the English without the header —
  `Auth/AuthUkrainianHttpTests`, `Profile/ProfileUkrainianHttpTests`,
  `Downloads/DownloadsUkrainianHttpTests`, `Search/SearchUkrainianHttpTests`,
  `Settings/DestinationsUkrainianHttpTests`, `Settings/IntegrationsUkrainianHttpTests`,
  `Library/LibraryUkrainianHttpTests`, `LibraryLinks/LibraryLinksUkrainianHttpTests`,
  `Library/DuplicatesUkrainianHttpTests`, `Replacements/ReplacementsUkrainianHttpTests`,
  `Recommendations/RecommendationsUkrainianHttpTests`, `Mcp/McpUkrainianHttpTests` and
  `Health/HealthUkrainianHttpTests`: one Ukrainian body for every cause of a 401 or a sign-in failure,
  the four plural forms, the vendors' labels, a reason an older version stored in English, and the
  MCP tools' descriptions kept English.
- **Phase E's acceptance bar checked on production, and the README and the release note say what was
  tried live and what wasn't (MAM-48, Vasyl's decisions of 2026-10-21).** Checked on 2026-10-21 on
  production, the published 0.5.0 image (Docker Hub's `0.5.0`, `0.5` and `latest`, digest
  `sha256:cc6ff4d97f47…`, pushed by Vasyl on 2026-10-20 at 17:24 UTC), which named itself
  `0.5.0+2593f91` on `/mcp` and whose web app's 75 files were byte for byte a build of commit 2593f91
  (the container's image id wasn't read on the host), against Jellyfin 12.2.0, qBittorrent and
  Prowlarr serving Toloka, with TMDb; Vasyl signed in as the owner. **Tokens.**
  `tools/mam48/check.py`, new, run by Vasyl on his Windows PC at 08:45 UTC, takes each token from
  the clipboard after **Copy** on Profile, sends it only to MAM's address and writes no token
  anywhere: Profile's `curl` line, run through Windows' `curl.exe` 8.21.0 with the header on its
  standard input, read 19 jobs, newest first, with a `Read only` token; that token's send answered 403
  `{ "error": "This access token is read only. …", "code": "account.token.read-only" }` in English
  without `Accept-Language` and «Цей токен доступу лише для читання. …» with `uk`; `/mcp` answered
  protocol `2025-11-25`, server `mam`, no session, the nine reads for `Read only` and twelve tools for
  `Read and download`, `send_download` marked `anthropic/requiresUserInteraction`, and `get_status`
  named VahaC, the access and the token; `send_download` with the `Read only` token answered the
  read-only sentence as an `isError` result; and after **Revoke** the first request answered 401, 3.0 s
  after the token's last 200, `{"error":"Sign in to continue.","code":"account.sign-in-required"}` with
  `WWW-Authenticate: Bearer`, byte for byte the answer without a token, and `/mcp` the same. Its only
  CHECKs were the tokens' names ("test", not the script's). A temporary member's `Read only` token read
  the member's own jobs, none, and `get_status` named a member; the owner's Users page read "1 токен"
  on the member's row, and **Відкликати токени**, confirmed at 10:17:11 UTC, left "0 токенів" and the
  member's next request the same 401. **MCP.** Claude Code 2.1.304, in a new session of the Claude
  desktop app's Code tab, with `mam` in user scope at `http://192.168.1.195:8118/mcp` and a `Read and
  download` token, found «Сяйво» (1980) — the library's copy and 9 releases, each film release matched
  to it by title and year, the 1997 mini-series told apart — and «Зелена миля» — the library's 1080p
  copy and 11 releases —, proposed the 4K UHD BDRemux of The Green Mile (1999), 78.6 GB, which
  `get_download_options` gave qBittorrent and Movies, preselected, asked "Надіслати 4K-ремукс у
  qBittorrent → Movies?", and after Vasyl's "так" and Claude Code's permission prompt sent it: job
  `c462d0c3-1ef0-4c1a-b6f8-16a81b66b4c4`, qBittorrent, Movies, overwrite check Checked, then
  Downloading. The Jobs page showed it under «Виконуються (1)» in Movies, and the web app's Download
  dialog offered the same release qBittorrent and Movies with Keep both, as `get_download_options` had;
  "Що зараз завантажується?" got that one job. Claude Desktop 3.1623.0, installed as an MSIX package,
  given Profile's block with a `Read only` token by `tools/mam48/desktop_config.py`, new, which backs
  up `claude_desktop_config.json`, adds `mcpServers.mam` while Claude Desktop is quit and later puts
  the file back byte for byte, ran `mcp-remote` 0.15.2 on Node.js 24.18.1: its log in
  `%LOCALAPPDATA%\Claude\logs\mcp-server-mam.log` says it connected over Streamable HTTP with the
  header from `env`, and `initialize` and `tools/list` were answered; Settings → Developer showed `mam`
  running. **Languages.** Vasyl's account in Українська, in the desktop app's browser pane, on about 45
  states at 1366 px, 21 pages and 6 dialogs at 375 × 812 and 17 and 6 at 320 × 640: no sideways scroll,
  and no Latin text of the web app's own, every run found being data or a service's words; jobs stored
  in English before the update read «Клієнт «qBittorrent» перезаписав би файл «The Last Boy Scout
  (1991) BDRip-AVC.mkv» у теці категорії «Movies»: …» and «Джерелу Torznab не вдалося віддати реліз
  (помилка 500: Download failed).»; the sign-in page followed the pane's Ukrainian browser. The member
  in English saw no Ukrainian of the web app's, the catalog's Cyrillic titles and genres being data,
  and its **Description** in English. **TMDb.** Production's catalog language stayed `uk-UA`; on the
  development database, backed up first, English took the recommendations 3 min 03 s to read again and
  back 3 min 22 s, and the database was then restored, its SHA-256 equal. What the check found is fixed
  below under **Changed** (`/api/health/ready`, `ch-GU`'s name, `mcp-remote` pinned, the address line
  and Profile's hints, the README's "Connect an AI assistant (MCP)") and in this file and the
  README: the token's 401 body under **Changed**, shown without its `code`, MAM-45's and MAM-46's
  build dates under **Notes for upgraders**, and the README's status and release list. Not tried
  live: an expired token, the owner's reset and Disable revoking tokens, a user's own password
  change keeping them, the limits' 429s, a sign-in's token on production's `/mcp`,
  `list_search_releases`, `cancel_job`, `list_duplicates`, `list_recommendations` and
  `dismiss_recommendation` from an assistant, a `Read only` token's Claude session, any call from
  Claude Desktop, the tools in Ukrainian, an assistant reaching the app over
  HTTPS or through a reverse proxy, and the sign-in page in an English-first browser. Observed, not
  looked into: the member's `get_status` counted one release source failing, the owner's none. At
  the end the tokens were revoked, the member deleted and both assistants' `mam` removed; Vasyl then
  cancelled the Green Mile job at 19.9 % in the web app, its torrent left in qBittorrent, and set
  his language back to English. The README (**Tested with**) and the release note list both; the
  README's and the roadmap's MAM-5, MAM-45, MAM-46 and MAM-47 lines no longer say they weren't tried
  on production, ADR 0041 gives MAM-48's results where it said MAM-48 would try them, and the
  roadmap marks Phases A, C, D and E shipped as 0.1.0, 0.3.0, 0.4.0 and 0.5.0.

### Changed

- **Every request with a token checks it, and every failure answers the one 401 (MAM-5, ADR 0002's
  MAM-5 section).** A wrong, unknown, malformed, revoked or expired token, and a disabled or deleted
  user's, answer 401 with `WWW-Authenticate: Bearer` and `{ "error": "Sign in to continue.", "code":
  "account.sign-in-required" }` (its `code` since MAM-47), the same bytes as a missing or ended
  sign-in, now written by one shared writer for both. "Last used" is written at most once a minute,
  and within the minute no update is even sent — nor a refusal's audit claim —, since in SQLite an
  update that matches no row still waits for the one write lock.
  `Auth/PersonalAccessTokenAuthenticationHttpTests` pins it.
- **The owner's password reset and Disable revoke the user's tokens; a user's own password change
  keeps them (MAM-5, ADR 0016's MAM-5 section, defaults put to Vasyl).** A reset revokes every token of
  the user, the owner's reset of his own password included; a disable revokes them in the same
  transaction as the sessions, so Enable brings none back; a delete takes them with the user. Profile's
  Password card adds "Your access tokens keep working.", and the Users page's Reset password dialog
  says the user's access tokens stop working. `UserDto` — the Users list and every user answer —
  carries `activeTokenCount`, read for a whole page in one grouped query.
  `Settings/UsersPasswordHttpTests`, `Settings/UsersLifecycleHttpTests` and `Auth/AuthPasswordHttpTests`
  pin it.
- **Failed token attempts count against the sign-in limit of their address (MAM-5, ADR 0040).**
  `SignInRateLimitPolicy` keeps its name, numbers and partition, and draws from one shared store,
  `SignInAttemptLimiter`, which a failed token attempt also takes from. While an address's ten a
  minute are used up, every token request from it to an endpoint that needs a signed-in user answers
  429 "Too many attempts. Wait a minute and try again." with `Retry-After`, without the token being
  looked up — the health endpoints, refresh and sign-out answer as without a token —; a lookup under
  way counts against the attempts left until it ends, so guesses sent at once get no more lookups
  than guesses sent one after the other; a good token takes nothing, and sign-in and setup count as
  before, every attempt, successful or not.
- **`GET /api/library/duplicates` takes `sort`, `Default` or `Recoverable` (MAM-45, ADR 0028's MAM-45
  section, Vasyl's decision of 2026-10-16).** `Recoverable` lists first the groups whose removal
  would free the most room, by the `recoverable` estimate alone — one file's names, at 0, last of
  those —, then the groups with none — byte-identical files with a `gap`, same works and possible
  matches —, each tie in the default order, so a page never reshuffles; read by name in any letter
  case, never as a number, and checked after the paging, the class and the decision: 400 "“x” is not
  a sort. Use Default or Recoverable.". Left out, the order is as before. The Duplicates page doesn't
  send it; `list_duplicates` does. `Library/LibraryDuplicatesSortHttpTests` pins it.
- **GET, HEAD and DELETE `/mcp` answer 405 `Allow: POST`, and `/.well-known/…` a bare 404, instead of
  the web app's page (MAM-45).** Stateless `MapMcp` maps POST alone, so the web app's fallback
  answered `GET /mcp` with 200 HTML, which an MCP client opening its event stream after connecting
  would read as events; and an MCP client looks under `/.well-known/` for an OAuth sign-in after a
  401, where it would try to parse the page. Both answer with a token or without, as the routing 405
  they stand in for would. `Mcp/McpEndpointHttpTests` pins them.
- **A fourth token mark, for an endpoint for personal access tokens alone (MAM-45, ADR 0040's MAM-45
  section).** `[PersonalAccessTokenOnly]` is endpoint metadata, as the other marks are:
  `PersonalAccessTokenAccessHandler` refuses a signed-in session there with 403 and the new
  `PersonalAccessTokenRefusalReason.SessionNotAccepted`, and lets a token of either access in after
  the deny, as the endpoint applies the access itself; `/mcp` alone carries it.
  `PersonalAccessTokenRefusalResultHandler` now writes the 403 `{ error }` for every refusal of the
  handler and records only a token's, as a session has no token row, and `[PersonalAccessTokenWrite]`
  on an MCP tool's method makes it a write. The rate limiter's 429 says the MCP sentence on the
  endpoint whose policy is `mcp`, and "Too many attempts" everywhere else, as before; an address whose
  sign-in limit is used up still gets "Too many attempts" on `/mcp` too, from the token's
  authentication, before the `mcp` limit is reached.
  `Conventions/PersonalAccessTokenConventionHttpTests` pins the mark on a probe endpoint.
- **TMDb is asked in the integration's catalog language, no longer always in Ukrainian (MAM-46, ADR
  0005's MAM-46 section).** `TmdbAdapter`'s `CatalogLanguage = "uk-UA"` constant is gone: every
  catalog call — a work's details, an episode, a collection, a person, a search, a find, discovery,
  the genre lists and the lookups — sends `language=` the integration's catalog language, escaped, or
  `en-US`, TMDb's own default, for settings that name none. `IMetadataCatalog` gains `Languages`,
  the languages a catalog can be asked in, and `IntegrationConnectionSettings` a trailing
  `CatalogLanguage`. So on Recommendations and in the TMDb dialog the titles, genre, collection and
  people names, overviews and posters come in the catalog's language; the Description dialog keeps
  asking in the reader's (Vasyl's decision 2 of 2026-10-17). `TmdbAdapterLanguageTests` pins it.
- **Sign-in, setup and refresh answer `user.language`, and `GET /api/auth/me` answers `language`
  (MAM-46).** `en`, `uk`, or `null` for a user who follows the browser, a trailing field of
  `CurrentUserResponse` and `CurrentUserProfileResponse`; every start of the web app comes through a
  refresh, so the language arrives with the session. `Auth/AuthSignInHttpTests`,
  `Auth/AuthRefreshHttpTests`, `Auth/AuthMeHttpTests` and `Setup/SetupHttpTests` pin it.
- **The catalog's cached rows say which language they were read in, and a change of language reads
  them again (MAM-46, ADR 0025's MAM-46 section).** `CatalogWorks.DetailsLanguage` and the
  `Language` of `CatalogGenres`, `CatalogCollections`, `CatalogPeople` and `CatalogLookups` are
  written with every row; a work's details, a collection, a person, a genre list and a lookup by a
  title are fresh only in the catalog's language of now, while a lookup by an IMDb id, which holds ids
  alone, stays fresh in any. A run under way when the language changes still gets its answers, but
  its old-language answers only add the rows not stored yet, stamped in their language, and change no
  stored row. Saving a new language for an enabled TMDb queues a computation of the recommendations
  and a preparation of the TMDb suggestions with a new trigger, `CatalogLanguageChanged`, which
  `GET /api/recommendations/runs/latest` and `GET /api/library/tmdb/runs/latest` can answer — also
  when the same save switches TMDb back on —; on a switched-off TMDb it is saved and audited
  (`IntegrationUpdated`) and queues nothing, the stamps reading again once it is on. Either way the
  newest releases first seen within the last 14 days that were resolved to no work are marked
  unresolved again, so their next read resolves them in the new language (ADR 0024's MAM-46
  section), and a **New on …** card's work, which the newest releases resolved once, has its details
  read again in it. A link's `catalogTitle` stays TMDb's title of when it was linked.
  `Catalog/CatalogLanguageChangeHttpTests`, `Catalog/CatalogStoreTests` and
  `Settings/IntegrationsCatalogLanguageHttpTests` pin it.
- **A preparation of the TMDb suggestions reads again the candidates read in another language (MAM-46,
  ADR 0025's MAM-46 section).** In every run, whatever queued it, a work whose search is kept from the
  cache now reads again, through the cache, the details of its stored candidates not stored as read
  in the catalog's language — read in another, or with no details or no row, as a run under way
  during a change leaves them —, so the TMDb dialog shows their titles and overviews in it without
  searching again; a candidate already read in it costs nothing, and TMDb removed and added again in
  another language is caught up too. `Catalog/CatalogLanguageChangeHttpTests` pins it.
- **The Description dialog asks in Ukrainian for a reader of the Ukrainian web app (MAM-46, ADR 0025's
  MAM-46 section, Vasyl's decision 2 of 2026-10-17).** It sends the UI language's code, now `uk` or
  `en`, with English beneath it where TMDb has nothing in Ukrainian, whatever the catalog's language.
- **Room for the longer Ukrainian text (MAM-46).** Measured in Chrome on the Ukrainian strings: the
  Search form's year column is `minmax(0, 7rem)`; up to 1240 px wide the header keeps its links as
  icons in Ukrainian, so the search field keeps its room (`html:lang(uk)` rules in
  `app-layout.css`); the Access tokens card's labels wrap on a 320 px phone with no sideways scroll; a
  full-width button of the sign-in and setup forms wraps its text instead of overflowing; and the
  Recommendations page's tabs are the shared `PageTabs`, which scroll the current tab into view on a
  phone, again whenever a label changes width (the Jobs page's counts arrive after its tabs, and in
  Ukrainian they pushed a current **Усі** partly out of the row). `styles/downloads.test.ts` ("the room
  the Ukrainian text takes"), `RecommendationTabs.test.tsx` and `PageTabs.test.tsx` pin it.
- **The database keeps no sentence: the 21 columns that held a reason hold its key and values, and
  the English an older version stored is converted as the app starts (MAM-47, ADR 0042).**
  `DownloadJobs.Error` and `BlockReason`, `SearchSources.Error`, the replacements' and removals'
  reasons and problems, `ReplacementJobWorks.ConfirmationText`, `LibraryItemLinks.WriteError` and
  `LookupError`, the files' and inspections' errors, and the scans', syncs', suggestion runs' and
  recommendation runs' errors keep their name, type, length and nullness and hold JSON
  (`{"k":"<key>","v":{…}}`) through one EF value converter, so no migration rebuilds a table. A reason
  longer than its 1,000 characters is cut inside its longest value with "…", so its own words stay,
  where the first 999 characters used to be kept. At start, after the migrations and before the
  reconcilers, `StoredTextConverter` reads every stored English sentence back into its key and values
  with the English templates themselves, and accepts a reading only when writing it again gives the
  stored text exactly — or, for a sentence an older version cut at the column's limit inside its last
  value, its beginning; a row it can't read keeps its words, shown as they are and marked so a later
  version tries again, a converted row is never read again, and nothing it meets stops the start. It
  leaves null and empty texts alone, reads in pages of 500 and writes each page in one transaction,
  and writes a row only while it still holds the text it read; a column that fails is logged and
  tried again at the next start. Code that compared stored sentences compares keys or whole
  messages. The audit row of an owner's confirmation names its claim in no language — `Alien
  (1979)`, `Doctor Who (2005) S02E01–E05 · Фільми`, `— S01` for a work with no title — instead of his
  sentence, and the older rows are rewritten the same way (Vasyl's decision of 2026-10-19); the claim
  itself reads in the reader's language, the Download dialog's checkbox included. On a copy of
  production's database taken on 2026-10-20 every stored sentence read back exactly (below).
  `Conventions/MessageColumnsTests`, `Persistence/MessageColumnTests`,
  `Persistence/ModelHasNoPendingChangesTests`, `Texts/MessageRoundTripTests`,
  `Texts/StoredEnglishCorpusTests`, `Texts/StoredTextConverterTests` (every message column of the
  model planted with older English, a second run changing nothing, a kept row tried again under a
  newer version, a failure in the middle of a page, the audit rows, the TMDb links' restart and
  no-episode-IMDb rows), `Texts/StoredTextConverterStartupHttpTests` (converted before the first
  request, a failing column, the Jobs list in Ukrainian) and `Texts/ProductionCopyConversionTests`
  (skipped unless `MAM47_PRODUCTION_DB_COPY` names a copy) pin it.
- **English changed only where it had to (MAM-47).** "1 bytes" reads "1 byte" in a new file's size
  reason, and a sync that read one different movie or episode says "1 different movie" (or
  "episode"), no longer "movies"; a removal's name found swapped on **Restore** says "this copy's own
  file wasn't found", no longer the old copy's, a replacement's words; a work with no title loses its
  year in the consent words and the plan's refusals ("This release is a work with no title, season
  2"), while the pairing's reasons keep it; a reason longer than its column is cut inside its longest
  value (above); and every error body gains `code`. Every other sentence reads byte for byte as it
  did, but in cases no reader meets today: a timeout or a limit of exactly one second, minute, byte
  or character, an operating system's explanation ending in " .", a binder message with control
  characters in the MCP arguments' sentence, a progress that isn't a number, and the adapters'
  guards against a caller's bug, which now name the rule. Sentences no page or answer could show
  were deleted, not translated.
- **`run-tests.sh` runs the .NET suite twice by default (MAM-47, ADR 0042).** The second pass,
  `MAM_TEST_LANGUAGE=pseudo`, sends every request in a pseudo-language whose sentences the server
  wraps in ⟦…⟧: the harness unwraps them where a test reads English, and fails a sentence that reaches
  a DTO field it doesn't list, the database or a log. Jenkins runs both and publishes each pass's
  reports, so each test shows twice; `PSEUDO=0` skips the pass locally. It adds about a minute and a
  half: a full run takes about three minutes where it took about one and a half (77 s for the first
  pass alone, 164 s for both, measured on 2026-10-20). An IL scan of the three assemblies
  (`Conventions/NoLiteralSentenceTests`) refuses a literal sentence that reaches a reader and a
  message printed into a string or handed to a log, and each test project refuses a message compared
  with a string; `Conventions/KeysInUseTests` refuses a key without its English or a template no code
  names, `Conventions/LocaleParityTests` an English file without its Ukrainian one or a Ukrainian
  file that differs from its English one, and `Conventions/ServerLabelsTests` a quoted label that is
  neither the web app's nor a recorded vendor's.
- **EF Core's SQLite packages go from 10.0.12 to 10.0.13, for a race in the connection pool
  ([dotnet/efcore#39008](https://github.com/dotnet/efcore/issues/39008)).** In Microsoft.Data.Sqlite
  10.0.12 a pooled connection that was being opened looked abandoned for an instant, and another
  thread opening a connection or clearing the pool at that moment could take it back: it closed the
  connection under its owner, removed the owner's collation, or lent it to a second owner. The app
  meets it rarely, where several connections open at once (the TMDb link writer's lanes). SQLitePCLRaw
  comes along at 2.1.13. The test harness no longer calls `SqliteConnection.ClearAllPools()` while
  tests run: each test database and app factory clears only its own pool (`TestDatabase.ClearPoolOf`),
  as clearing every pool reached into the classes running beside it, and the suite failed now and then
  with `ObjectDisposedException` 'SQLitePCL.sqlite3', a link left Writing or a failed class cleanup.
- **`/api/health/ready` writes a Ukrainian description as raw UTF-8, as every other answer does
  (MAM-48's live check, 2026-10-21).** Production answered `Accept-Language: uk` with «База даних
  доступна, і всі міграції застосовано.» written as `\uXXXX` escapes, 335 bytes where the English is
  144: the health endpoints write their JSON straight to the response outside MVC, through
  `StreamingJson.Options`, which set no encoder, so the default one escaped every Cyrillic letter. It
  now sets `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, the encoder the controllers' answers and
  `McpJson` already write with; the body is `application/json`, never put inside a page or a script.
  The English answer is byte for byte what it was. `Health/HealthUkrainianHttpTests` reads the body as
  bytes in both languages and compares each with the exact JSON.
- **Profile's Claude Desktop configuration runs `mcp-remote@0.15.2`, no longer whatever release of
  `mcp-remote` is newest (MAM-48's live check, Vasyl's decision of 2026-10-21).** The block's `args`
  start `"-y", "mcp-remote@0.15.2"`, where they named `mcp-remote` alone, which `npx -y` fetches at
  its newest release without asking. The access token passes through that bridge, so a new release on
  npm no longer starts running with it unchecked. 0.15.2 is the release the check ran with Claude
  Desktop 3.1623.0; it is raised on purpose, in one constant (`MCP_REMOTE` in
  `CreateAccessTokenDialog.tsx`), once a newer one has been tried with the app. A configuration copied
  before keeps the unpinned name until it is changed by hand. `CreateAccessTokenDialog.test.tsx` and
  `CreateAccessTokenDialog.https.test.tsx` pin it.
- **The TMDb catalog language `ch-GU` reads "Chamorro (Guam)", no longer "Ch-GU" (MAM-48's live
  check, 2026-10-21).** The browser's Intl had no name for Chamorro — `Intl.DisplayNames` gave "ch
  (Guam)" in English and "ch (Гуам)" in Ukrainian —, so the TMDb dialog's **Catalog language** select
  showed one of TMDb's 144 languages as a code. The app now keeps its own name for such a language,
  used where Intl has none or gives back the code: "Chamorro" in English and «чаморро» in Ukrainian,
  in lower case as Intl writes language names there, with Intl's name of the region, so the option
  reads "Chamorro (Guam)" or «Чаморро (Гуам)». A language neither names, such as TMDb's own `cn-CN`,
  still shows as its tag. `catalog-languages.test.ts` names each of the 144 recorded languages in
  English and Ukrainian, with Node's Intl and with a stand-in for a browser's that has no name for
  Chamorro.
- **Profile says which address an AI assistant needs, and to quit Claude Desktop before pasting its
  configuration (MAM-48's live check, Vasyl's decisions of 2026-10-21).** The Claude Code command
  and the Claude Desktop configuration carry the address the Profile page is open at. On production
  that was an address behind Cloudflare, closed to outsiders, which Claude Code can't pass, while the
  home-network address works. Above both fields the shown-once step now says, for every token, "The
  command and the configuration below use this page's address, which the assistant must reach from
  its own computer: on your home network or through a VPN. If this address asks for a sign-in in the
  browser (Cloudflare Access, for example), open the app at its home-network address instead and
  create a new token there: both then use that address." Opened there, Profile builds both lines
  for that address, `--allow-http` included on plain HTTP, so nothing is edited by hand. There is no
  setting for another address (Vasyl's decision). The Claude Desktop hint now reads "Quit Claude
  Desktop completely (closing its window isn't enough), paste it into claude_desktop_config.json
  (into its mcpServers, if it has one already), then start Claude Desktop again. It needs Node.js 18
  or later.", where it said to paste it and then restart Claude Desktop: Claude Desktop writes the
  file while it runs, so an edit made meanwhile can be lost. The Claude Code hint ends "…, from its
  next session on.", as Claude Code takes up a new server only in a new session.
  `CreateAccessTokenDialog.test.tsx` and `CreateAccessTokenDialog.https.test.tsx` pin them.
- **The README's "Connect an AI assistant (MCP)" says what the live check met (MAM-48, 2026-10-21).**
  The address the lines need, as Profile now says; the Claude Desktop block with `mcp-remote@0.15.2`;
  Claude Desktop quit completely before its configuration is edited, as it writes the file while it
  runs; a `mam` in `claude_desktop_config.json` also loading into the desktop app's Code tab, where it
  wins over `claude mcp add`'s, so connect one of the two there; a new server seen only by a new
  Claude Code session; a stale `mam` removed before it is added again, and both entries removed before
  their tokens are revoked; Claude Code 2.1.246 or later for a send's prompt with no "don't ask again",
  where it said 2.1.214; Claude Desktop's log in `%LOCALAPPDATA%\Claude\logs` (older versions
  `%APPDATA%\Claude\logs`); and, in the desktop app, where `/mcp` opens claude.ai's connector catalogue,
  asking Claude which `mam` tools it has, or the token's **Last used** on Profile, to see the
  connection.
- **A job goes by its listing's description when the listing gave one — Toloka's whole title — and by
  its title otherwise (Vasyl's report of 2026-10-22).** "Strip Cyrillic Letters", which Prowlarr's and
  Jackett's Toloka apply, leaves a Ukrainian-only release's title as punctuation, and the Jobs page
  listed two cartoons sent from Toloka as "' (2016) HDTV 1080p" and "WEB-DL 1080p (2013)". A job's
  `releaseTitle` — the Jobs page and the job's own page, the Cancel and Keep both dialogs, MCP's
  answers — and the audit rows written for the job and its replacement now carry the description,
  "Сім'я (2016) HDTV 1080p" where the title was "' (2016) HDTV 1080p", and an English release reads
  with its Ukrainian title first, "Бійцівський клуб / Fight Club (1999) …". It is read on every
  request, so the jobs sent before read the same; a release stored before 0.2.1 has no description and
  keeps its title, as do the audit rows already written. The Download dialog's title and its
  **Release** line give the same name, and so does a release's row on the Search page, which is headed
  by it and no longer shows the description again under it, nor the title (Vasyl's decision of
  2026-10-22); its **Details** still show the title with the description under it, and what each was
  read as. The release itself keeps the title it was listed with, which every reading of its quality
  still reads
  ([ADR 0004](docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md)'s
  MAM-30 section). `DownloadsListHttpTests.List_and_details_name_a_job_by_its_listings_description_when_the_listing_gave_one`,
  `DownloadsSendHttpTests.Create_names_the_job_and_its_audit_row_by_the_listings_description_when_the_listing_gave_one`,
  `ReplacementQuarantineFlowHttpTests.Flow_names_the_job_on_every_row_of_its_trail_by_the_listings_description_when_the_listing_gave_one`,
  `release-name.test.ts`, `ReleaseRow.test.tsx` and `DownloadDialog.test.tsx` pin it.

### Notes for upgraders

- **Applying `AddPersonalAccessTokens` rebuilds no table, so it logs no warning 20410 (MAM-5).** It
  creates one table, `PersonalAccessTokens` — `Id`, `UserId`, `Name`, `Prefix`, `TokenHash`, `Access`
  (stored by name), `ExpiresUtc`, `LastUsedUtc`, `LastRefusedUtc`, `RevokedUtc` and `CreatedUtc` —,
  with a unique index on `TokenHash`, an index on `(UserId, CreatedUtc)` and a foreign key to `Users`
  that deletes a user's tokens with them (`AddPersonalAccessTokensMigrationTests`).
- **Nothing of it is used until someone creates a token (MAM-5).** The web app's requests are
  answered as before; the Users page shows the new column, every count 0; Profile shows the empty card.
- **A script with a wrong token uses up its address's sign-ins (MAM-5).** Behind a reverse proxy, or
  Docker's own proxy, every client shares one address, so a script left looping with a revoked or
  mistyped token keeps everyone there from signing in, as ten sign-in attempts a minute already did:
  fix or stop the script.
- **Rotating `MAM_Jwt__Secret` leaves personal access tokens working (MAM-5).** They aren't signed
  with it. Revoke a token on Profile, or all of a user's on Users; a reset or a disable of the user
  revokes them too.
- **Nothing to turn on for an AI assistant but a token (MAM-45).** `/mcp` answers a personal access
  token alone, so an installation where nobody connects an assistant sees no change but `GET /mcp`
  answering 405 instead of the web app's page. MAM-45 adds no table, column, setting or environment
  variable. An assistant needs a token from Profile and the app's address reachable from its
  computer — behind a reverse proxy, `/mcp` has to reach the app as `/api` does —, as the README's
  "Connect an AI assistant (MCP)" says.
- **Applying `AddLanguages` rebuilds no table, so it logs no warning 20410 (MAM-46).** It adds seven
  columns in place: `Users.Language`, null for every existing user, who so follows the browser;
  `IntegrationConnections.CatalogLanguage`, set to `uk-UA` on every `tmdb` row by EF's own
  `UpdateData` and null on every other; and `CatalogWorks.DetailsLanguage` and the `Language` of
  `CatalogGenres`, `CatalogCollections`, `CatalogPeople` and `CatalogLookups`, non-null with the
  default `uk-UA`, so
  every row cached before counts as read in Ukrainian, as it was, and the upgrade reads nothing from
  TMDb again (`AddLanguagesMigrationTests`).
- **A browser that prefers Ukrainian shows the web app in Ukrainian after the upgrade, with nothing
  done (MAM-46).** **As in the browser** is every user's choice until they make one, and the browser's
  `uk-UA`, which 0.4.0 skipped for English, now has its language. The sign-in and setup pages follow
  the browser too. A user who wants English picks **English** under Profile → **Language**, once, for
  every browser and device.
- **An installation's TMDb keeps Ukrainian (MAM-46).** The migration gives every TMDb integration
  `uk-UA`, the language it was always asked in, so the Recommendations page and the TMDb dialog read
  as before; production keeps Українська. The owner changes it under Integrations → TMDb → **Catalog
  language**, and the next computation reads the catalog again. A TMDb added after the upgrade starts
  in the owner's language.
- **The server's sentences follow the page's language, with MAM-47 (MAM-46, MAM-47, ADR 0001's
  MAM-46 and MAM-47 sections).** In the Ukrainian web app the sentences the API writes — an error, a
  job's or a check's reason, a run's failure — read in Ukrainian since MAM-47, while a service's own
  words stay as the service wrote them, and the release notes stay English only (Vasyl's decision of
  2026-10-14).
- **The first start converts the stored reasons, with no migration (MAM-47).** The 21 columns keep
  their shape, so nothing is rebuilt and no warning 20410 is logged; the conversion reads each column
  once, in pages of 500, before the app serves its first request, and logs one Information line per
  column it changed, such as `Converted 4 stored texts of DownloadJobs.Error; 0 with a cut value, 0
  shortened to fit, 0 kept as their words.`, a second line with the row ids of any it kept or
  shortened, and `Named 4 owner's confirmations in AuditEvents.TargetName by their claim; 0 left as
  they were.` for the audit rows — counts and row ids, never a sentence. A reason it couldn't read
  shows its English words as they are, in either language, and is tried again by a later version
  that learns its wording. On a copy of production's database taken on 2026-10-20, all 26 texts were
  read: 22 stored sentences in seven columns converted exactly and 4 audit rows named by their claim,
  none kept, cut or shortened, so no older wording had to be taught to the converter.
- **Rolling back to an older image after 0.5.0 shows the converted reasons as raw JSON (MAM-47,
  accepted by Vasyl on 2026-10-19).** That image shows `{"k":…,"v":…}` wherever a reason was converted.
  Its catch-up counts a converted restart reason of a TMDb link as one to repair, so **Update
  Jellyfin** there may send a second Apply to an item whose write was interrupted: wait a few minutes
  after the rollback before pressing it. It asks TMDb again for the episodes a converted "TMDb knows no
  IMDb id of this episode" row names, and its job poller writes each followed job's block reason once
  more, in English; both heal themselves. Upgrading again converts whatever English it wrote.
- **A script that compared error sentences can compare `code` (MAM-47).** The English sentences are
  what they were but for the few under Changed, and a request with no `Accept-Language` gets English,
  so a script keeps working; the code is the part that never changes.
- **With a catalog language other than Ukrainian, release matching for recommendations leans on the
  original title (MAM-46, ADR 0024's MAM-46 section).** The availability check and the newest releases
  match a work by its titles, the catalog's now in that language: Toloka's Ukrainian part of a name
  then matches nothing, and the original part, which its names carry beside it, decides; a work
  whose original title isn't in Latin letters (a Japanese or Korean film) is searched by its title in
  the catalog's language and shows nothing available on Toloka.
- **0.5.0 adds Phase E's MAM-5, MAM-45, MAM-46 and MAM-47 to the 0.4.0 image published on
  2026-10-14.** Vasyl published 0.4.0 on 2026-10-14 at 20:41 UTC (Docker Hub's `0.4.0`, `0.4` and
  `latest`), built after commit 6ef689b, so that image holds all of 0.4.0 below, the Recommendations
  page's Description dialog and layout included. From MAM-5, `frontend/package.json` says 0.5.0
  (the ticket's version, Vasyl's decision of 2026-10-14), so the next image published from `main` is
  0.5.0, never 0.4.0 again; MAM-45 (built on 2026-10-16), MAM-46 (2026-10-17) and MAM-47
  (2026-10-20) keep that number, and Vasyl published 0.5.0 on 2026-10-20 at 17:24 UTC (Docker Hub's
  `0.5.0`, `0.5` and `latest`), built from commit 2593f91. MAM-48's fixes, under **Changed** above,
  came after that image and are in 0.5.0 once it is published again, under the same number, as is the
  jobs' name by the listing's description (2026-10-22).

## [0.4.0] — 2026-10-13

### Added

- **Phase D's tools are chosen, and their answers recorded (MAM-29, ADR 0039, the MAM-29 sections of
  ADR 0004, 0005, 0010 and 0034, Vasyl's decisions of 2026-10-13).** An installer may run Transmission 4.0
  or newer instead of qBittorrent, and Jackett instead of Prowlarr — never two download clients side by
  side. Transmission speaks its legacy RPC dialect; a Transmission destination is a Jellyfin library
  folder it can reach or its own default folder, sent as one `torrent-add` with the folder and the
  destination's name as a label (ADR 0039, amending ADR 0010 for a client without categories); Jackett
  goes through the existing Torznab adapter. Deluge, SABnzbd, a direct Toloka adapter and Transmission's
  JSON-RPC 2.0 dialect were not chosen. `tools/mam29/` recorded Transmission 4.1.3 (the full set) and
  4.0.6 (a short set) and Jackett v0.24.2830 with its Toloka.to indexer into
  `VendorSamples/Transmission/` (24 files) and `VendorSamples/Torznab/jackett-*` (15 files), with
  what they taught in `VendorSamples/README.md`; `VendorSamplesTests` keeps Transmission's session id and
  Basic credentials and Jackett's `path=` token out of every sample. No code of the app changed.
- **The owner connects Transmission 4.0 or newer as a download client, the fifth type of integration
  (MAM-30, ADR 0005's and ADR 0010's MAM-30 sections, Vasyl's decisions of 2026-10-13).** The Integrations
  page adds the type **Transmission** (`transmission`, kind `DownloadClient`), with path mappings, an
  optional user name and password, and no API key. Its address is that of Transmission's web
  interface ("The address of Transmission's web interface, such as http://192.168.1.10:9091. Transmission
  4.0 or newer."); the adapter appends `/transmission/rpc`, and also takes an address ending in
  `/transmission/rpc`, `/transmission` or `/transmission/web`. Every call is a `POST` of
  `{ method, arguments, tag }` with Basic credentials when a user name is set and the session id
  Transmission handed out in a 409's `X-Transmission-Session-Id` header, kept in memory per integration
  and per address and credentials, masked and never logged (a test of an unsaved integration keeps
  none). Every member first reads `session-get` — `version`, `rpc-version`, `rpc-version-semver`,
  `download-dir`, `incomplete-dir`, `incomplete-dir-enabled` — and refuses a Transmission older than 4.0
  (`rpc-version` below 17, or `rpc-version-semver` below 5.3.0 when it is sent): "This is Transmission
  3.00; the app needs Transmission 4.0 or newer. Update Transmission, then test again."
  (`UnsupportedVersion`); and one whose temporary folder is on: "Transmission keeps unfinished downloads
  in its temporary folder “/downloads/incomplete”, where the app can't follow them. In Transmission's web
  interface open Edit preferences, Torrents, untick “Use temporary folder”, then test again.", for every
  call — destinations, sends and following alike. A test answers "Connected to Transmission 4.1.3." (its
  version without the build); 401 "Transmission refused the user name or password." (or "Transmission
  asks for a user name and password. Enter them under Integrations." without a user name); 403
  "Transmission refused this app (403 Forbidden): …"; 421 "Transmission doesn't accept the host name in
  this address (421 Misdirected Request); …"; 404 "Transmission's RPC wasn't found at this address (404
  Not Found). …"; and a 200 that isn't Transmission's envelope "The service at this address answered, but
  not the way Transmission does.". Any `result` but `success` is a refusal, whatever the HTTP status.
  Only an HTTP 401 or 403 is remembered — per integration and per address and credentials, until they
  change or a connection test with them works — and not sent again, since with its anti-brute-force
  setting on Transmission counts each one towards locking its RPC until a restart: "Transmission refused
  the user name or password the last time the app used them, so the app won't send them again: …". It
  is remembered in memory, as qBittorrent's refusals are, so a restart of the app, an update included,
  sends the refused credentials once more. The
  password, the Basic value, the session id, a magnet and its trackers' addresses are masked in every
  sentence. `GET /api/settings/integrations/adapter-types` lists five types, and it and the integrations
  list gain `destinationKind` (`Category` for qBittorrent, `Folder` for Transmission, `null` for a type
  that sends nothing); `UnsupportedVersion` now also answers a Transmission older than 4.0.
  `FakeTransmissionServer` answers as the recorded 4.0.6 and 4.1.3 do (`FakeTransmissionServerTests`);
  `TransmissionAdapterTests`, `TransmissionAdapterSessionsTests`, `TransmissionAdapterDestinationsTests`,
  `TransmissionAdapterSubmitTests`, `TransmissionAdapterJobsTests`, `TransmissionAdapterFilesTests`,
  `TransmissionAdapterJobsWithFilesTests` and `TransmissionAdapterDestinationJobsTests` read every
  recorded Transmission answer; `TransmissionAdapterMethodsTests` keeps the adapter to the RPC methods
  `session-get`, `free-space`, `torrent-get` and `torrent-add`; one behavioural contract suite,
  `DownloadClientContractTests`, runs the same cases against qBittorrent and Transmission
  (`QBittorrentDownloadClientContractTests`, `TransmissionDownloadClientContractTests`); and
  `IntegrationsRealAdaptersHttpTests`, `IntegrationsSecretsHttpTests` and `IntegrationsHttpTests` cover
  it over HTTP.
- **A Transmission destination is one of your library folders it can reach, or its own download folder
  (MAM-30, ADR 0039's Implementation section, ADR 0003's and ADR 0008's MAM-30 sections, Vasyl's
  decisions of 2026-10-13).** For a client without categories the Destinations page and the destination's
  dialog list, as **Folder**, each library folder the sync stored — mapped into the app through the
  Jellyfin integration's pairs, as before, then back into the client's paths through its own pairs by the
  new `PathMapper.MapBack` — and the client's `download-dir`, each confirmed by one `free-space` per
  folder each time the list is read (`result` `success` and `size-bytes` 0 or more). A library folder
  that is also the default folder is listed once, as that library's, marked as the client's folder too;
  a default folder inside a library folder is listed in that library (`InLibrary`), so a replacement
  may go through it, and one outside every library is `OutsideLibraries` or `PathUnknown`, which takes
  none (ADR 0039's Implementation section). A folder Transmission can't see
  stays listed, not pickable, with the new status `FolderNotSeen` ("Not seen by the client") and the
  client's sentence: "Transmission can't see “/downloads/movies” (No such file or directory). Mount that
  folder into Transmission, or add a path mapping on this integration that leads there, then look again."
  A library folder with no folder in the client is listed under the field as unplaced, with the
  integration, the library and the fix: none of the client's pairs leads to it ("“Transmission” has no
  folder for the library “Фільми”: none of its path mappings leads to “/media/movies”. Add one under
  Integrations, “Transmission”, Path mappings (for example /downloads → /media), then look again."), two
  lead to it from different places, Jellyfin's own pairs can't place it, its name can't be written under
  the client's Windows folder, or its folder is longer than the 256 characters a destination holds.
  Jellyfin's Collections and playlists libraries (`CollectionType` `boxsets`, `playlists`) are kept with
  no folder by the sync — from the next one, which drops the folder an older version stored —, so they
  are never offered, and the Media folders page no longer lists or counts their folder. Save takes only
  a folder the client listed and confirmed — the request's own key is never proposed to the client, so
  a folder can't be typed —, stores it as the client names it, canonical, in `DestinationMappings.ClientDestinationKey` (no new column), and refuses
  any other with 409 "“Transmission” doesn't offer the folder “/downloads/x”. Pick one of the folders it
  lists." or the folder's own problem; a save that changes neither the client nor the folder asks
  nobody. A destination's name is Transmission's label, so a name with a comma is 400 for a client
  without categories, on create and on every update, a rename included: "“Transmission” shows this
  destination's name as the torrent's label, and a label can't contain a comma. Pick a name without
  one." The stored folder is checked again, as itself, each time it is shown, sent to or offered for a
  replacement; one that doesn't parse, or that the client no longer lists, is `FolderNotSeen` for that
  destination alone. `GET /api/settings/destinations` and `…/{id}` gain `destinationKind`;
  `GET /api/settings/destinations/client-destinations/{downloadClientId}` answers
  `{ items, error, destinationKind, unplaced }`, each item gaining `isClientDefaultFolder`, `unplaced`
  `[{ libraryName, folder, problem }]`, `[]` for a category client. It also takes `?destinationId=`,
  which the Edit dialog sends: a folder client then lists that destination's stored folder as itself,
  even once no library leads there, so a mapping edit never shows it as missing; a destination of
  another client proposes nothing more, and a malformed id is 400 "The destination being edited isn't
  a valid id. Reload the page and open it again.". `PathMapperBackTests`, `DestinationPathCheckTests`,
  `DestinationRulesTests`, `DestinationsClientDestinationsHttpTests`, `JellyfinAdapterLocationsTests`,
  `LibraryLocationFoldersHttpTests` and `TransmissionAdapterDestinationsTests` pin it, and
  `DestinationsFolderClientHttpTests` and `DestinationsFolderSaveHttpTests` over HTTP with a fake folder
  client the app factory adds (`FolderClientApiFactory`, `FakeFolderDownloadClient`), and
  `DestinationsTransmissionHttpTests` end to end through `FakeTransmissionServer`.
- **A send to Transmission is one `torrent-add` into the destination's folder, labelled with its name,
  started (MAM-30, ADR 0013's MAM-30 section, ADR 0039).** The add carries exactly the release —
  `metainfo` (the .torrent, base64) or `filename` (a magnet) —, `download-dir`, `labels` with the
  destination's name and `paused: false`, so a send never waits paused whatever Transmission's "start
  added torrents" says; Transmission's `hashString` is the job's id at once. A 409 to it, which
  Transmission answers before it reads the body, is resent once with the session id from that 409's
  header; a second 409, or one without a usable id, fails the job: "Transmission refused the session id it
  had just sent." A torrent Transmission already has is refused before the add by the sender's look for
  the release's hashes, as for qBittorrent: "“Transmission” already has this torrent, so nothing was
  sent." One Transmission takes between that look and the add is answered `torrent-duplicate`, which adds
  nothing and changes nothing of the torrent Transmission has, so the job fails with where it is, read
  back with one `torrent-get`: "Transmission already has this torrent, in “/downloads/movies” with the
  label “Movies”, so it added nothing." (or, when that read fails, "Transmission already has this
  torrent, so it added nothing."). Once its payload is fetched, before
  anything is listed or sent, a release that is BitTorrent v2 only is refused for a client that can't take one (the new
  capability `TakesV2OnlyTorrents`; the adapter refuses one too, without a request): "“Transmission”
  can't take a torrent that is BitTorrent v2 only, which this release is, so nothing was sent." A
  destination whose folder the client no longer sees refuses the send: "“Transmission” can't use the
  folder of the destination “Movies” any more, so nothing was sent. The owner checks the destination
  under Destinations in the user menu." Every sentence a member can read names the destination, never its
  folder: `GET /api/downloads/options` gives a folder destination `clientDestinationKey` `null` and each
  client its `destinationKind`, and `GET /api/downloads/{id}` and the list give a folder job's
  `clientDestinationKey` to the owner alone, with the job's `destinationKind`, which the job now keeps
  from its client when it is written (`DownloadJobs.DestinationKind`, `null` for a job sent before).
  `TransmissionAdapterSubmitTests` (the add's arguments, the one resend after a 409, a second 409, a
  duplicate with and without its read back, a v2-only release, a timeout never resent),
  `DownloadsSendFolderClientHttpTests` (the label, the folder found in any spelling, a folder the client
  can't see, a v2-only release, what the owner and a member each read),
  `ReplacementScheduleSendFolderClientHttpTests`, `DownloadsSendTransmissionHttpTests` (the same end to
  end through `FakeTransmissionServer`, with a refused add, the temporary folder on, an unreachable
  Transmission and a refused password not sent again), `DownloadsTransmissionSecretsHttpTests` (no
  password, Basic value or session id in an answer, a log line, a job or audit row, the poller's block
  reasons or what a member reads),
  `DownloadJobPersistenceTests.A_job_keeps_the_kind_of_its_destination_by_name_and_an_older_job_none`
  and `AddDownloadJobDestinationKindMigrationTests` pin it.
- **The overwrite check knows Transmission's layout, its names and its `.part` files (MAM-30, ADR 0034's
  MAM-30 section).** For Transmission (`TorrentFilePlacement.OwnLayoutOnly`) a torrent's files land in
  its own layout only — a lone file straight in the folder, several in a folder named after the
  torrent —, each name cleaned as Transmission cleans it: 4.0 on any system (`TorrentNameReadings.Portable`:
  cut at a NUL, split on `/`, the ends' whitespace and trailing dots stripped, `_` before a Windows device
  name, `" * : < > ? \ |` as `_`), 4.1 on Windows (`WindowsSafe`, also each control character as `_`) and
  4.1 on Linux (`Verbatim`, only `.` and `..` as `_.` and `_..`). The names are read as the .torrent
  holds them — `TorrentContentReader` now keeps them beside libtorrent's cleaned ones —, each folder name
  of a path on its own. The check isn't told the version, so it looks at every place the three readings
  give, and at each place's `<name>.part`, the name of an unfinished file on the disk, counted as the
  same file of the release. A torrent whose places can't be predicted — a file path of a torrent of
  several files held in bytes that aren't UTF-8 or with a NUL, or a file no reading gives a name — is
  sent unchecked (`NotCheckedFileList`), and refused for a replacement: "The app couldn't check every
  place this release would write in the folder of the destination “Movies” (the .torrent's list of
  files can't be read), so nothing was sent and no copy will be replaced. Pick Keep both to download
  it." A file the torrent marks as padding (BEP 47) is a place like any other, as Transmission ignores
  the mark, but for one at BEP 47's own name, `.pad/<digits>`, which lands in a `.pad` folder.
  Transmission's torrents in the destination's folder are those whose `downloadDir` is that folder,
  never read by label. The sentences name the destination: "“Transmission” would write over
  “Film.2001.mkv” in the folder of the destination “Movies”: a file of another size is already there,
  so nothing was sent." A folder Transmission can't see is a folder the check can't look into, and
  `GET /api/downloads/overwrite-check` answers it with a reason of its own, `NotCheckedFolderNotSeen`,
  which no sent job carries, as nothing is sent there: the send refuses it before the check, and the
  check, asked about it anyway, refuses it too, Keep both or a replacement alike, with the send's
  sentence (`DownloadOverwriteGuardFolderNotSeenTests`).
  qBittorrent's three layouts and libtorrent's cleaning are unchanged. `TorrentNameReadingsTests`,
  `TorrentTargetPathsTests` and `TorrentContentReaderTests` pin it, and
  `DownloadsSendOverwriteFolderClientHttpTests` and `DownloadsSendOverwriteTransmissionHttpTests` over
  HTTP.
- **Transmission's downloads are followed, and its torrents hold your copies, as qBittorrent's do
  (MAM-30, ADR 0014's and ADR 0031's MAM-30 sections).** A job is found by its 40-character
  `hashString`, never Transmission's `id`, which a restart changes. `status` 4 is downloading, 5 and 6
  seeding, 1, 2 and 3 queued or checking, and 0 stopped — `Downloaded` once complete, `Paused` before;
  complete is `metadataPercentComplete` 1 with `leftUntilDone` 0, whatever `isFinished` says; `error` 3
  (a local error) is the client's error on the job, while 1 and 2, a tracker's warning or error, are
  not. `ServiceContentPath` is the torrent's folder or file as that Transmission cleans its name. The
  poller's search of a destination for a job its hashes no longer find runs only for a client whose job
  ids can change (`JobIdsMayChange`: qBittorrent's libtorrent 2 renames, ADR 0014) — never for
  Transmission, whose every torrent it would read every 2 seconds. A replacement's checks read
  Transmission's torrents and their files as they read qBittorrent's, and a `<path>.part` beside an old
  copy, where a torrent's file is unfinished, counts as held, so an unfinished download never moves into
  the quarantine as its companion; a `.part` no torrent lists moves with its video like any companion.
  A job that needs linking now names its client: "“Transmission”'s downloads couldn't be told apart for
  this release, so the app can't follow it to verify the new file." `TransmissionAdapterJobsTests`,
  `TransmissionAdapterJobsWithFilesTests`,
  `DownloadJobTrackingFolderClientHttpTests.Poll_of_a_torrent_the_client_no_longer_lists_shows_why_and_never_reads_every_torrent_of_its_folder`,
  `ReplacementHoldingReaderPartialFilesTests`, `ReplacementQuarantinePartialFileHttpTests`, and end to
  end `DownloadJobTrackingTransmissionHttpTests` and `ReplacementTransmissionFlowHttpTests` pin it, and
  `ReplacementDetailsHttpTests`, `ReplacementDropCopyHttpTests`, `ReplacementFollowingHttpTests`,
  `ReplacementLineHttpTests` and `ReplacementRetryHttpTests` the linking sentence.
- **Jackett serves Toloka through the same Torznab adapter (MAM-30, ADR 0004's MAM-30 section).** No new
  adapter: the adapter read Jackett's answers as Prowlarr's already, its link at the host's root
  (`/dl/toloka/?jackett_apikey=…&path=…&file=…`) included. `path` joins the names whose values a feed's
  error text never shows. `TorznabSamples` and `FakeTorznabServer` gain Jackett's shape — its downloads
  at the origin's root, its recorded 200 `<error code="100">` to a wrong key on caps and searches, its bare
  401 on a download and its 404 on a bad token (`FakeTorznabServerJackettTests`).
  `TorznabAdapterJackettTests` and `TorznabAdapterGatewaySamplesTests` read every Jackett sample, beside
  Prowlarr's; `SearchesJackettHttpTests`, `ReleaseSearchJackettHttpTests`,
  `RecommendationsJackettHttpTests`, `DestinationsSectionsJackettHttpTests`,
  `DownloadOptionsSuggestionJackettHttpTests` and `DownloadsSendJackettHttpTests` go end to end with
  Jackett as the only source: a film by its Ukrainian title, a Ukrainian-only film through its
  description, a series with its seasons, a cartoon series by its Ukrainian title, Recommendations, the
  sections that pick a destination, a send through Jackett's download link, and a wrong key or download
  token.
- **The Search page matches a release by its description too (MAM-30, ADR 0012's MAM-30 section, Vasyl's
  decision of 2026-10-13).** A release is read from its title and, when its listing gives one, from its
  `<description>` — Toloka's whole topic title, Ukrainian first —, the same way Recommendations read it;
  the description adds a match only by an id or by a title and year, a work's match is the stronger of
  the two, and a tie goes to the title. A series' held episodes, and the episodes a replacement offers,
  come from the claim that matched, or from the other when it names none. So "« » (2026) WEB-DL-AVC …",
  a Ukrainian-only title "Strip Cyrillic Letters" left as punctuation, matches its copy, through Jackett
  or Prowlarr. `GET /api/searches/{id}/releases` gives each release `descriptionWork` (`{ titles, year,
  lastYear, episodes }`, `null` without a description), and **Details** shows it as "Read from the
  description". `SearchReleasesDescriptionHttpTests`, `ReplacementOptionsDescriptionHttpTests` and
  `ReleaseDetailsDialog.test.tsx` pin it.
- **Phase D's acceptance bar checked on a clean installation with Jellyfin, Jackett and Transmission,
  and the README and the release note say what was tried live and what wasn't (MAM-31, Vasyl's
  decisions of 2026-10-13 and 2026-10-14).** Checked on 2026-10-14 on a second, clean MAM container on
  the reference installation's host beside production — its own data volume, production's `/media`
  (a read-write bind of one ext4 disk), `PUID` 1000 and `PGID` 100, deployed by Vasyl as the
  Stashboard project `mam-clean` on port 8119 —, running an image built locally from commit ab2d1c1
  once its CI build #70 had passed (10,292 cases, 0 failed, 7 skipped): `mam-local:0.4.0-ab2d1c1`,
  image `sha256:e488319229bc…`, stamped `0.4.0+ab2d1c1`, not published, on Docker 29.9.0 and Compose
  v5.6.0 under Linux 7.0.14-22-pve x86_64. It ran against Jellyfin 12.2.0, Jackett v0.24.2830
  (`lscr.io/linuxserver/jackett:v0.24.2830-ls3`) with its Toloka.to indexer and "Strip Cyrillic
  Letters" ticked, Transmission 4.1.3 (`lscr.io/linuxserver/transmission:4.1.3-r0-ls363`) and TMDb for
  the Recommendations page, with ffprobe 6.1.1-3ubuntu5, and no qBittorrent or Prowlarr; Vasyl signed
  in as its owner. Set up from zero by the README — which named a **Sync** button where the Library
  page has **Refresh all** and **Sync now**, and said nothing of Transmission's peer port —, every
  **Test connection** answered ("Connected to Transmission 4.1.3."), and replacement was switched on
  for all four library folders. Before Transmission had the libraries mounted, **Add destination**
  listed each as "(can't be used)" with "Transmission can't see “/downloads/movies” (No such file or
  directory). Mount that folder into Transmission, or add a path mapping on this integration that
  leads there, then look again."; with the mount, Movies, TV Shows, Cartoons and Documentary were
  saved on the four library folders with production's Toloka sections, Transmission's download folder
  `/downloads` showed "Outside the media libraries" with replacement off, and Jellyfin's Collections
  library wasn't offered. Through Jackett, the series and the cartoon were first offered TV Shows and
  Cartoons by their Toloka sections, and the film Movies as the default; the Search page matched the
  film's and the series' copies by title and year; a Ukrainian-only title found no release, as in
  MAM-29's recording, and another's description was read but not matched, Toloka's year (1971)
  differing from Jellyfin's (1973); and Recommendations, with Jackett the only source, listed
  releases. Toloka's 1080p release of Undercover Blues (1993), one file of 8.41 GiB, was sent at 12:51
  UTC with the old copy (1,947,648,052 B, inode 390463819, held by no torrent) ticked ("Transmission
  took the release and lists it among its downloads." "Replacement scheduled: 1 copy, kept until the
  new one is verified."; `DownloadRequested`, `ReplacementScheduled`, `ReplacementWorkConfirmed`), and
  showed in Transmission's web interface in `/downloads/movies` with the label "Movies", Toloka's
  tracker answering with 5 peers and no error. It started only once Vasyl had turned uTP on and
  forwarded port 51413 on the router (first peer at 13:12:04 UTC), its unfinished `….mkv.part`
  standing in `/media/movies` at full size; it was followed to `Downloaded` at 13:37 UTC, one file
  straight in `/media/movies` (9,027,402,332 B, 1080p H.264, 1:30:13), verified 75 s after the app
  asked Jellyfin to look (IMDb tt0108442, TMDb 12661; `ReplacementCheckStarted`,
  `ReplacementVerified`), and the old copy, held by no torrent, moved into
  `/media/.mam-quarantine/69fd9196-1136-41d3-a80c-b61a817969e5/movies/` keeping its inode
  (`ReplacementQuarantineStarted`, `ReplacementNameMoving`, `ReplacementNameMoved`,
  `ReplacementQuarantined`, in 0.26 s). Vasyl then pressed **Delete for good** — now **Delete old copy
  permanently** — at 16:16:44 UTC, the first deletion from the quarantine on a real setup: the old
  copy left the disk, its empty folders kept (`ReplacementDeleteStarted`, `ReplacementNameDeleting`,
  `ReplacementNameDeleted`, `ReplacementCompleted`). With Transmission's container stopped from
  12:57:17 to 12:59:28 UTC, the integration's test, each destination, the folder field and the job's
  line said "Couldn't reach Transmission at http://192.168.1.195:9091: nothing accepted the
  connection. Check the address and port, and that Transmission is running.", and the job, not
  failed, went on by its hash once Transmission was back; with a wrong Jackett key, the test and the
  Search page said "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API
  key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this
  integration under Integrations." Two places said less: the Download dialog's line on the overwrite
  check named neither Transmission nor the fix, and Recommendations said nothing of a refused key
  (both fixed below, under **Changed**). The app's calls reach Transmission from 172.20.0.1, the
  gateway of its Docker network, which a `WHITELIST` there would have to name. At desktop width and
  at 320 px no page scrolled sideways or said "qBittorrent" for Transmission; "category" stayed in
  the Destinations page's lead and empty state, the dialog's first open, the line asking for a folder
  and "Standard categories" (fixed below). Between ca480a2 and ab2d1c1, `DownloadJobFollowing.cs` and
  `Core/Replacements/` didn't change; in `Api/Services/Replacements/` the transitions are the same but
  for the needs-linking reason, which now names the client, and three readers changed what they read
  — a Transmission torrent's `.part` counted as held, a folder destination found by its folder, and a
  series' episodes taken from the claim that matched, the title's or the description's, which lets a
  description decide which seasons and episodes a replacement offers, and offer a work the title
  matched by title only, for every client and every source, Prowlarr included (D15, ADR 0012's
  MAM-30 section, accepted by Vasyl on 2026-10-14). Production stayed on 0.3.0
  (`sha256:c42d0ed41aaf…`, stamped `0.3.0+082c84e`), not restarted, no failure in its log, its
  quarantine as before. The fixes made during the check (below, under **Changed**) came after that
  image; built into `mam-local:0.4.0-mam31` (`sha256:f21c717efb7d…`, stamped `0.4.0+ab2d1c1.mam31`,
  not published) and put on the clean container at 16:17 UTC with its data kept, they were looked at
  there — the destinations' folder words, "Standard sections", the dialogs' widths and columns and
  "Replaced: the old copy was deleted permanently." —, except the Download dialog's and
  Recommendations' new lines, the new button names, the empty Destinations page's words and **Edit
  preferences**, which rest on the tests. Not tried live yet: a copy held by a Transmission torrent, Transmission
  4.0.x (MAM-29's 4.0.6 samples only), its lock-out after wrong passwords (its anti-brute-force is
  off), a 421, its whitelist, its temporary folder ticked, a BitTorrent v2-only release, a
  description's match to a copy the title doesn't match, a saved folder Transmission no longer sees,
  a send with Keep both, Restore with Transmission, Phase C's other cases with Transmission, moving
  an installation from qBittorrent or Prowlarr, and upgrading from 0.3.0. Vasyl keeps the clean
  installation, Transmission with its torrent and mount, Jackett and the router's forward for now,
  and removes them himself; the findings he hasn't settled are listed in the roadmap's MAM-31 line.
  The README (**Tested with**) and the release note list both; the README's and the roadmap's MAM-30
  lines no longer say MAM-31 is the live check, the release note's Tested with no longer says a real
  send to Transmission wasn't tried, and ADR 0004, ADR 0005, the specification's Phase D note and the
  vendor samples' README give MAM-31's results where they said it would try or diff.

- **A recommendation's Description: what the work is about, in a dialog (Vasyl, 2026-10-14; ADR 0025's
  description section).** Each card on the Recommendations page has a **Description** button, between
  **Releases** and **Not interested** (its icon alone on a phone), that opens a dialog with the work's
  title, its original title and year, and TMDb's overview of it; "The metadata catalog has no
  description of it." when TMDb has none, and "Could not load the description." with the server's
  sentence — and **Try again** when another try can help — when it fails. Vasyl's choices: the overview
  is asked for on the click, not with the lists, so every card has one at once at the cost of a short
  wait the first time; and in English for now, as not every user reads Ukrainian (TMDb has a Ukrainian
  overview for about half the films). `GET /api/recommendations/overviews/{kind}/{catalogId}?language=`
  asks TMDb for the work's details in that language (`/3/movie/{id}?language=`, nothing appended),
  else in English, and keeps each answer — the "none" too — in the new `CatalogWorkOverviews` table
  (migration `AddCatalogWorkOverviews`), one row per work and language, removed with the work; it is
  asked again once older than the catalog's 30 days, and a kept one is served, however old, while TMDb
  can't be asked. The web app asks in its UI language's code, so a translated UI asks in its own and
  falls back to English with no change on the server. Every signed-in user may ask; only a work the
  catalog has is asked about (a bare 404 otherwise); 400 for a kind, an id or a language code it
  doesn't take; 409 with nothing kept and no catalog; 502 with TMDb's sentence for the owner and "The
  metadata catalog couldn't send this description." for everyone else. `IMetadataCatalog` gains
  `GetOverviewAsync`. `RecommendationOverviewsHttpTests`, `TmdbAdapterOverviewTests` and
  `RecommendationsPage.overview.test.tsx` pin it.

### Changed

- **"Folder" for a client without categories, and the client's own name where qBittorrent was named
  (MAM-30).** The Destinations page heads its column **Category**, **Folder** or **Category or folder**
  by its rows; the destination's dialog says "Folder", "Choose a folder", "Asking Transmission which
  folders it can reach…" and "Transmission shows this name as the torrent's label, so it can't contain a
  comma." for a folder client; removing a folder destination or a folder client says "Nothing changes in
  Transmission: torrents already sent keep their folder and label."; the Download dialog reads "Send this
  release to a download client. The destination decides where it lands.", sums up a folder
  destination by its name alone, and warns before Send that nothing is sent to a folder the client
  can't see, holding Send there under Keep both as under Replace; the job's page shows the owner
  "Movies — folder “/downloads/movies”";
  the Jobs page's "Fix it in Jellyfin or qBittorrent" names the job's client, and "a torrent goes in
  qBittorrent" reads "a torrent goes in the download client that lists it"; the reason of a replacement
  whose download needs linking names the job's client, qBittorrent's included — "“qBittorrent”'s
  downloads couldn't be told apart for this release, so the app can't follow it to verify the new
  file.", where it read "qBittorrent's downloads …" (ADR 0014's MAM-30 section). The Destinations
  page's lead and empty texts name a category or a folder, and a release's **Details** without a work
  in its title says "Any match comes from the description or from an id in the listing.". The
  Integrations page names Jackett and Transmission in its empty state, gives Transmission's address its
  own placeholder and help, adds Jackett's Copy Torznab Feed to the Torznab help, and tells a folder
  client's mappings that "Your library folders become Transmission's destinations through these
  mappings". Three destination rules are neutral: "Pick the category or folder of the download client
  this destination saves to.", "A category or folder can be at most 256 characters." and "A category or
  folder can't contain line breaks or other control characters." The qBittorrent adapter's own
  sentences are unchanged. `DestinationDialog.folders.test.tsx`, `DestinationsPage.test.tsx`,
  `DownloadDialog.test.tsx`, `DownloadDialog.replace.test.tsx`, `JobDetailsPage.test.tsx`,
  `ReplacementLine.test.tsx`, `IntegrationDialog.test.tsx` and `IntegrationsPage.test.tsx` pin them.
- **Torznab sentences say where to fix it (MAM-30, ADR 0004's MAM-30 section).** A refused key (errors 100
  to 102, a bare 401 or 403) reads "The Torznab feed refused the API key (…). Copy the API key from the
  dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration
  under Integrations."; an error 200 to 299 on the capabilities request, which can only be about the
  address, "The Torznab feed doesn't serve what this address names (…). Check the indexer in the feed's
  address, such as …/indexers/toloka/results/torznab/ for Jackett or …/1/api for Prowlarr."; and a 404
  "Nothing answers Torznab at this address (404 Not Found). Use the feed's Torznab address: Prowlarr's
  ends in /api, and Jackett's Copy Torznab Feed gives one ending in /results/torznab/." A search's own
  error wording is unchanged.
- **A season marker with an unknown episode total reads as a marker (MAM-30).** "(Сезон 1, серії 1-25
  з ?)", "of XX" and "з хх" are taken with the marker, so "Розслідування з Андрієм Данілевичем (Сезон 1,
  серії 1-25 з ?) (2026)" reads "Розслідування з Андрієм Данілевичем", without the stray "(" it kept.
  `ReleaseWorkClaimsTvTests` pins it.
- **`IDownloadClient` takes the folders the app proposes, and says more about itself (MAM-30, ADR 0005's
  MAM-30 section).** `ListDestinationsAsync(settings, proposedFolders)` — a category client ignores them —;
  `DownloadClientCapabilities` adds `DestinationKind`, `FilePlacement`, `PartialFileSuffix`,
  `TakesV2OnlyTorrents` and `JobIdsMayChange`; `ClientDestination` adds `IsClientDefaultFolder` and
  `Problem`; `DownloadSubmission` adds `Label`. No member removes, stops or moves anything, as before
  (`DownloadClientNeverRemovesTests`). The tests that used `transmission` as the id of a type no build
  registers — `DownloadJobMissingHttpTests`, `ReplacementOptionsHoldingsHttpTests` and the frontend's
  `retiredClient` fixture — use `retired-client` ("Old client") now.
- **The dialogs with a long list are up to 1,024 px wide, their lists in columns, and the integration
  dialog's short fields in pairs (MAM-31).** The Add and Edit destination dialog was capped at 28rem
  (448 px) like every settings dialog; it now takes up to 64rem, and a source's sections sit in columns
  at least 14rem wide — three a row at full width — so a long list such as Toloka's runs a third as
  long. The others with a long list take the same 64rem: the Download dialog once the owner's copies
  are listed, from 48rem (768 px), an open season's episodes two a row in columns at least 22rem wide;
  **Files in no library**, from 48rem, its files two a row in columns at least 26rem wide, each closed
  by a line under it where a line opened all but the first; and **Details** with the TMDb link dialog,
  which shares its classes, from 60rem (960 px), still two columns. The Add and Edit integration dialog
  takes up to 48rem, from 36rem (576 px), so a path mapping's two paths and Jackett's feed address get
  the room, and sets the service beside the name and the user name beside the password where two 16rem
  columns fit; a lone API key keeps the row, and TMDb's attribution, which sat between the service and
  the name, moves under the two. Where two columns don't fit, as on a phone, every list and pair stays
  one column and the dialog the screen's width (Vasyl, MAM-31's live check, 2026-10-14). A short form
  and a confirmation keep their widths, as a few fields or a sentence and two buttons read worse wider:
  the Download dialog without copies, Add user, Reset password and the confirmations; the permanent
  deletion's, which lists paths, stays at MAM-41's 39rem. `DestinationDialog.test.tsx`,
  `IntegrationDialog.test.tsx`, `UnreportedFilesDialog.test.tsx` and `downloads.test.ts` pin the
  classes, the widths and the columns.
- **Transmission's destinations say "folder" and qBittorrent's "category", and the destination's dialog
  sets the name beside the download client (MAM-31's live check, 2026-10-14, ADR 0010's MAM-31
  section).** With Transmission alone, the Destinations page still spoke of categories. Its lead and
  empty text, which MAM-30 above made name both, now follow the clients connected:
  - **qBittorrent**, as before MAM-30: "Where downloads go. Each destination is a category of your
    download client, and the category decides the folder." and "No destinations yet. Click Add
    destination to pick a category of your download client.";
  - **Transmission**: "Where downloads go. Each destination is a folder your download client saves to."
    and "…to pick a folder your download client can reach.";
  - **neutral** while the clients load, when none is connected or they couldn't be read, for anyone but
    the owner, and with clients of both kinds: "Where downloads go. Each destination tells your
    download client where to save a download." and "…to pick a download client and where it saves."

  The Add and Edit destination dialog showed "Pick a category of your download client …", **Category**
  and "Choose a category" until the clients answered. It now opens with the clients the page already
  read and, until a client's kind is known, uses neutral words: "Pick a download client, then where it
  saves. The app checks with the client that it can save there.", "A new client, or a new place it
  saves to, is checked with the client before it is saved.", **Saves to**, "Choose where it saves",
  "Asking … where it can save…" and "Could not ask the client where it can save." A destination saved
  without its folder or category reads "Pick the folder of “Transmission” this destination saves to."
  or "Pick the category of “qBittorrent” this destination saves to.", where MAM-30 read "Pick the
  category or folder of the download client this destination saves to.". That sentence stays for a
  client of a type this build doesn't register. The rule is now read once the client is found, so a
  client that no longer exists answers its 404 before a missing key or a wrong section. The length and
  line-break rules still name both, as only a hand-made request reaches them. The group of Torznab's
  standard ids among a source's sections reads "Standard sections", where it read "Standard
  categories". **Name** and **Download client** sit side by side where two 16rem columns fit, as the
  integration dialog's pairs do (`.destinations-pair`), and stay one column on a phone (Vasyl's
  request). `download-clients.test.ts`, `DestinationsPage.test.tsx`, `DestinationDialog.test.tsx`,
  `DestinationDialog.folders.test.tsx`, `DestinationDialog.sections.test.tsx`, `downloads.test.ts`,
  `DestinationRulesTests`, `DestinationsCreateHttpTests`, `DestinationsUpdateHttpTests`,
  `DestinationsSectionsSaveHttpTests` and `DestinationsFolderSaveHttpTests` pin them.
- **The Download dialog names the client and the fix when it can't check a destination's folder, and
  the Recommendations page names a release source that failed (MAM-31's live check, 2026-10-14).**
  - **The Download dialog.** With Transmission stopped, the dialog read under Keep both "The app can't
    check whether this release would write over a file already there: the destination's folder has no
    path in the app, or its download client can't say where it is. …", naming neither.
    `GET /api/downloads/overwrite-check` now also answers `folderUnknownCause`: `ClientNotAsked` for a
    client switched off, unreachable, refusing the app, of a type this build can't use or with a secret
    the app can't read; `CategoryMissing` (qBittorrent only); or `NoPathMapping` when no path mapping
    of the client covers the folder it names. It comes with `clientName`, and for `ClientNotAsked` with
    `clientError`, the client's own sentence, sent to the owner only. The job's stored check and the
    send are unchanged. The warning then reads:
    - "The app couldn't ask “Transmission” where this destination saves, so a release sent there goes
      without the check for files it would write over.", then the client's sentence for the owner
      ("Couldn't reach Transmission at …: nothing accepted the connection. …") or "Ask the owner to
      check “Transmission” under Integrations." for anyone else;
    - "No path mapping of “Transmission” brings this destination's folder into the app, … The owner
      adds one under Integrations, “Transmission”, Path mappings.";
    - "“qBittorrent” no longer has this destination's category, … The owner picks another category
      under Destinations in the user menu.";
    - the sentence it read before when there is no cause to name, such as two mappings that send the
      folder to different places.

    Where the warning carries the client's sentence, the reason Replace can't be picked reads "The app
    can't check this destination's folder for files already there, and a replacement is sent only
    after that check. Pick another destination, or Keep both." instead of the same sentence again.
  - **The Recommendations page.** With a wrong Jackett key, the page kept its old counts without a word.
    `GET /api/recommendations` now answers `failedSources`: each switched-on release source whose last
    call for the lists failed, from the availability check or the newest releases' read, with `name`,
    `status`, `failedUtc` and, for the owner only, `error`. The page lists them above the cards, as the
    Search page does: "Toloka · Last asked for these lists 12 minutes ago: Refused the API key. The
    Torznab feed refused the API key (error 100: Invalid API Key). …"; anyone else reads "… Ask the
    owner to check “Toloka” under Integrations." A line goes once the source answers again, up to 15
    minutes later, or 3 hours when nothing is due. The failures are kept in memory, not in the
    database, so a restart forgets them until the next pass, which runs as the app starts.

  The README's API reference lists the new fields. `DownloadsOverwriteCheckHttpTests`,
  `DownloadsSendOverwriteTransmissionHttpTests`, `RecommendationSourceFailuresHttpTests`,
  `DownloadDialog.test.tsx`, `DownloadDialog.replace.test.tsx` and `RecommendationsPage.test.tsx` pin
  them.
- **The README's install and connect steps say what the clean installation needed (MAM-31's live
  check, 2026-10-14).**
  - **Quick start.** "Open <http://localhost:8118>." now adds "on the machine it runs on". For a
    server with no browser of its own, such as an LXC, it offers an SSH tunnel
    (`ssh -L 8118:127.0.0.1:8118 user@server`) or `APP_BIND_ADDRESS=0.0.0.0`. With the second, create
    the owner account at once: until it exists, whoever opens the app first becomes the owner. The
    compose file's header comment says the same.
  - **Jellyfin, step 2.** It says **Refresh all** at the top of the **Library** page, or **Sync now**
    under **Details**. It said **Sync**, which the page doesn't have.
  - **Transmission.** The steps say **Edit preferences**, as Transmission's web interface does, and
    gain three:
    - set "Download to" (**Edit preferences** → **Torrents**) to a folder inside the libraries' mount,
      outside the libraries and owned by `PUID` and `PGID`. The image's `/downloads/complete` isn't
      there once the libraries are mounted at `/downloads`, so the app lists it as "(can't be used)";
    - make the peer port reachable: publish `PEERPORT` (51413 by default, TCP and UDP) and forward it
      on the router, or at least tick **Enable uTP for peer communication**, which the image ships
      unticked. The first send stayed at 0% until the port was forwarded;
    - for a 403: with both in Docker on one host and the app given the host's address, Transmission
      sees the gateway of its Docker network (`172.20.0.1` on the reference installation, not
      `192.168.1.195`).
  - **TMDb, step 6.** The release sources' newest releases, behind "New on Toloka", are read only
    while TMDb is connected: when the app starts, then every 3 hours. Restart the app
    (`docker compose restart app`) once TMDb and a release source are added, or wait up to 3 hours.
    The Recommendations section, which said "every 3 hours", now says "when the app starts and then
    every 3 hours".

  In the app only Transmission's two sentences that name its menu changed with them, to **Edit
  preferences**: the temporary folder's (quoted under **Added** above) and "… isn't a full path the
  app can use. In Transmission's web interface open Edit preferences, Torrents, give “Download to” a
  full path such as /downloads, then look again." (`TransmissionAdapterTests`,
  `TransmissionAdapterDestinationsTests`).
- **The job page's Replacement buttons name the old copy, and "for good" reads "permanently" (MAM-31's
  live check, Vasyl, 2026-10-14).** Under a page title that is the new release's name, **Restore** and
  **Delete for good** read as acting on the new file, and "for good" is an idiom not every reader
  knows. On the job's page and on its Jobs row the buttons now say what they act on, counted by the
  old copies the quarantine holds: **Restore old copy** or **Restore old copies**, and **Delete old copy
  permanently** or **Delete old copies permanently**, their titles "Put the old copy in the quarantine
  back where it was" and "Delete the old copy in the quarantine permanently" (or "copies"). The
  confirmation asks "Delete the old copy permanently?" with **Delete permanently**, and reports
  "Deleted permanently: 2 files."; "Put the old copy back?" and its **Restore** stay. Under the
  Duplicates page's **Removed copies** the button reads **Delete permanently**, asking "Delete “…”
  permanently?". Every other sentence that said "for good" says "permanently": the Jobs line ("Old
  copy moved into the quarantine: 1 copy, kept until you restore it or delete it permanently.",
  "Deleting the old copy permanently…", "Replaced: the old copy was deleted permanently."), each file's
  "Deleted permanently.", the Duplicates page's lead, empty tab and confirmations, the Media folders
  page's hints and the Download dialog's conditions; and the server's: "… Restore them or delete them
  permanently first.", "The app stopped while deleting the old copies permanently: …", "… while
  deleting this copy permanently: …", "A video of this replacement was deleted permanently, so nothing
  is put back without it. Press Retry, then delete permanently what the quarantine still holds.", "This
  copy's video was deleted permanently, … Delete permanently what the quarantine still holds.", a
  removal's "… then press Delete permanently again." and a replacement's "Deleting the old copies
  permanently needs the files you confirmed.", "… changed since you opened it. Press Delete again to
  see what it holds now." and "… Switch it on, then press Delete again.", which name its button by
  its first word, as "press Restore again" does. A reason stored before keeps its words. The routes
  and fields (`delete-for-good`, `canDeleteForGood`) are unchanged. `JobDetailsPage.replacement.test.tsx`
  and `JobsPage.actions.test.tsx` pin the labels for one copy and for several, and the pages', the
  components' and the Api tests that pinned "for good" pin the new sentences.
- **A recommendation's rating sits in the card's top-right corner, its genres on a line of their own,
  and its poster is larger (Vasyl, 2026-10-14).** The rating and its votes moved from the line under
  the title to the top-right corner beside the title, after an amber star, the votes in short so the
  title keeps its room on a phone ("★ 8.2 (29K)"); the full count — "8.2 (28,968 votes)" — shows on
  hover and is what a screen reader reads. The genres left the line of the original title and the year
  for a line under it, and the poster is 7rem wide, up from 6rem. `RecommendationsPage.cards.test.tsx`
  pins the rating and the genres.
- **A release's row on the Search page reads its listing from the top-right corner, its claims as
  three groups, and names its source (Vasyl, 2026-10-14).** The seeders (an up arrow in the status
  green) and the peers (a people icon) moved into the row's top-right corner, each saying on hover what
  it counts — "Seeders: people who have the whole release and are sharing it now. The more there are,
  the faster it downloads." and "Peers: everyone connected to this torrent, seeders and those still
  downloading together, as the source counts them." — and read in full to a screen reader ("46
  seeders"); the size sits under them, larger, and the publish date under the size. A phone puts the
  three on one line under the text. The "Claimed" label and its chips gave way to up to three groups
  of plain text, each after its icon: the picture ("1080p · BDRip · H.264", a TV release's "TV" and
  seasons first), the audio languages and the subtitle languages ("Ukrainian, English"). A value the
  title doesn't state is left out rather than named as unknown, and a title that states nothing
  leaves no line; the line says on hover "What the release's title claims, not checked against a
  file". Vasyl picked the groups on a prototype of three layouts (chips, groups, the resolution picked
  out). The Details dialog and the replacement's works keep the full list with its unknowns
  (ClaimedQuality). The source moved from the facts line to the row's bottom left, beside the
  actions, as "Source: Prowlarr". `ReleaseRow.test.tsx` pins the corner, the hovers, the groups and
  the source.

### Notes for upgraders

- **Applying `AddDownloadJobDestinationKind` rebuilds no table, so it logs no warning 20410 (MAM-30).** It
  adds one nullable column, `DownloadJobs.DestinationKind`, stored by name (`Category`, `Folder`), and no
  index; every job sent before comes through with none, read as a category
  (`AddDownloadJobDestinationKindMigrationTests`).
- **What Transmission and Jackett add stays off until you add them; some of 0.4.0 reaches every
  installation (MAM-30).** With qBittorrent and Prowlarr a send, its following and a replacement work
  as they did. What changes for them too: the Search page also matches a release by its description, so
  a release may match a copy it didn't, and a series' held episodes, and the episodes a replacement
  offers, come from the claim that matched, or from the other when it names none; a season marker with
  an unknown episode total reads as a marker; the wording listed under Changed — the Download dialog,
  the Destinations and Integrations pages, the Jobs page's next steps, the reason of a replacement whose
  download needs linking, which now names the client, and the three destination rules —; the Torznab
  sentences for a refused key, a feed that doesn't serve the address and a 404; and from the next sync
  the app keeps no folder for Jellyfin's Collections and playlists libraries, so the Media folders page
  no longer lists or counts theirs (ADR 0008's MAM-30 section). The qBittorrent adapter's own
  sentences are unchanged. After upgrading, sync your Jellyfin library once (**Library** → **Refresh
  all**) so its Collections library leaves the folder list: until that sync the app keeps the folder an
  older version stored for it, `/config/data/collections` on the reference installation, and a
  Transmission destination's dialog lists it as a library none of the client's path mappings leads to.
- **Set Transmission up before you add it (MAM-30, ADR 0039).** Untick "Use temporary folder" (Edit
  preferences → Torrents; the linuxserver.io image ships it ticked) before anything is sent, and never
  tick it while downloads run: a download started there moves into its folder only once it is done,
  which no answer shows. Give it `USER` and `PASS`: without a password it takes any credentials, so a
  test that works proves nothing. Mount your library folders into it and run it as their owner, so it
  can write there, and give its integration a path mapping that leads back to each library folder
  (`/downloads` → `/media`). See the README's [Connecting the services](README.md#connecting-the-services).
- **Retire qBittorrent with care (MAM-30, ADR 0031, ADR 0034's MAM-29 section).** Once Transmission's
  destinations exist, remove qBittorrent's, which takes it out of the Download dialog
  (`GET /api/downloads/options` lists only a client with a destination): the overwrite check reads only
  the client a send goes to, so with both offered a torrent of one still queued, or fetching its
  metadata, for a library path is invisible to a send to the other. Keep the integration switched on:
  right before a move the app asks every download client, destinations or none, and one switched off
  stops every move for review, and the following of its jobs. Remove it only once none of its torrents
  seeds a file of your libraries and none of its jobs is on a Jobs page's In progress or Needs you
  tab: a followed job whose client is removed is blocked permanently ("The download client “qBittorrent”
  was removed, so this download can't be followed. Cancel the job to stop tracking it."). Its
  destinations go with it, and the jobs sent to them keep their names.
- **Moving from Prowlarr to Jackett maps the sections again (MAM-30, ADR 0020).** A release source's
  tracker sections are mapped per integration: tick them again in each destination for Jackett, then
  remove Prowlarr, which takes its mappings and the newest releases it gave Recommendations with it.
  Leave Jackett's "Strip Cyrillic Letters" ticked, its default and the only way the app supports.
- **0.4.0 adds Phase D's MAM-30 to the 0.3.0 image published again on 2026-10-13.** Vasyl published
  0.3.0 again on 2026-10-13 at 18:55 UTC (Docker Hub's `0.3.0`, `0.3` and `latest`), built after his
  commit 082c84e, so that image already holds MAM-28's fixes and MAM-39 to MAM-44, listed under 0.3.0
  below; MAM-29 changed no code of the app. From MAM-30, `frontend/package.json` says 0.4.0 (Vasyl's
  decision of 2026-10-13), so the next image published from `main` is 0.4.0, never 0.3.0 again.

## [0.3.0] — 2026-10-09

### Added

- **The owner switches replacement on, and ticks the library folders it may touch, on the Media
  folders page (MAM-24, ADR 0033, Vasyl's decisions of 2026-10-08).** The page (user menu → **Media
  folders**) gains a second card, **Replacing copies**, inside the same form and saved by its one
  **Save** in one request and one transaction: the switch **Replace copies after verification**, off
  by default ("Off by default. When on, the Download dialog offers Replace after verification for
  copies in the folders ticked below, to you alone. In this version a replacement is only scheduled;
  moving old copies comes in a later version and will also need the media folders mounted
  read-write."; MAM-25 below rewords its last sentence, and MAM-26 below rewords it again, as the
  old copy now moves), and **Library folders replacement may
  touch**: every library folder the Jellyfin
  integration's path mappings bring into the app, by its path in the app, with the libraries that
  list it ("In the library Фільми"), none ticked by default. An unticked folder outside the Media
  folders can't be ticked ("Outside the Media folders."), a ticked one says its tick "has no effect
  until the folder is inside one", and a stored tick that is no longer any library folder the app
  knows is listed ticked so the owner can untick it ("No longer a library folder the app knows, so a
  tick here has no effect. Saved unticked, it leaves this list."); with nothing stored the card says
  "No library folders yet: sync a library first.", and it counts the library folders the mappings
  leave without a path ("3 library folders have no path in this app: add path mappings to the
  Jellyfin integration."). The ticks stay while the switch is off. The switch is the one row of the
  new `ReplacementSettings`, none until the owner first switches it on, and no row is off; the ticks
  are `ReplacementFolders`, keyed by the folder's path in the app, never by a library-folder row,
  which every sync rebuilds, so a folder that leaves the libraries or the Media folders loses its
  tick's effect and gets it back if it returns. Both are read fresh on every request, never cached.
  A copy lies in a ticked folder only when every library folder that holds it is ticked and inside
  the Media folders: `/media/movies` ticked and `/media/movies/kids` not keeps the kids' copies out
  of reach. `GET /api/settings/allowed-roots` now answers
  `{ paths, replacementEnabled, libraryFolders, unmappedLibraryFolderCount }`, each folder
  `{ appPath, libraryNames, ticked, status }` with `status` `Available`, `OutsideMediaFolders` or
  `NoLongerALibraryFolder`; `PUT` takes `{ paths, replacementEnabled, replacementFolders }`, a
  missing switch off and a missing list of ticks none, as a missing list of paths allows nothing.
  Each tick must be a full path in either style, given once, at most 50, and a library folder the
  mappings bring into the app or a tick already stored: otherwise 400, such as "“/srv/films” isn't a
  library folder the app knows, so it can't be ticked for replacement.", and nothing is saved, the
  folders and the switch included. A Save writes `ReplacementSwitchedOn` or `ReplacementSwitchedOff`
  when the switch changed and one `ReplacementFoldersChanged` when the set of ticks changed, beside
  `AllowedRootsChanged`, none of them with a target, and a Save that changes nothing writes none;
  the log says on or off and how many folders are ticked, never a path.
  `ReplacementSettingsHttpTests` (a fresh installation, the sample library's folders with `/media`
  mapped to the platform's own folder, a folder two libraries share, every status, a stale tick kept
  or removed, a path the page never lists, the rules, a member's 403, two first switch-ons at once,
  the log), `ReplacementSettingsPersistenceTests`, `SettingsWritesAreOwnerOnlyHttpTests` (a member's
  Save writes neither table), `ReplacementOptionsHttpTests` (the nested folders),
  `MediaFoldersPage.test.tsx` and `allowed-roots-api.test.ts` pin it.
- **The owner picks, in the Download dialog, the specific old copies a release should replace
  (MAM-24, ADR 0032, Vasyl's decisions of 2026-10-08 and 2026-10-09).** Under **Existing copies**
  the dialog now has two choices: **Keep both**, picked by default and changed only by a click, and
  **Replace after verification**. A member's is disabled with "Only the installation owner can
  replace a copy.", and the dialog asks the server nothing more for it. The owner's, once a
  destination is picked, asks
  `GET /api/replacements/options?releaseId=&destinationId=&holdings=false` and lists, in both modes
  — read-only under Keep both, so the owner sees why before picking —, the works the release
  matched, as the Search page matches them for the owner, strongest evidence first: for a film
  release every file of every version of each matched film, in the Library's order; for a TV release
  the held episodes its title claims — its one episode, those of a range within one season, or every
  held episode of each whole season —, by season, then number, each at its first place the claim
  holds, with its versions. Each copy shows its file name, told apart from the others' as the
  Duplicates page tells them, its folder, its version's label or "Version 2", the edition words
  MAM-37 reads in its name and label, the resolution and dynamic-range chips the Library shows, and
  its size; above them, the edition words of the release's own title. Nothing is ticked in advance;
  each season has "Tick every copy that can be replaced (18)" or "Untick all" and "3 of 18 ticked",
  folded when there are several. Replace can't be picked, with the reason under it, before a
  destination is picked, while the copies are read ("Checking your copies…"), and when nothing of
  the release can be replaced: replacement switched off ("Replacement is switched off. Switch it on,
  and tick the folders it may touch, on the Media folders page.", linked); a magnet link ("This
  release is a magnet link: the app can't check its files before sending."); a destination that
  can't take one — its client can't say where the category saves or no longer has it, its folder
  isn't confirmed inside a library the app knows, or it lies outside the Media folders or has no
  path in the app, so ADR 0034's check can't look into it ("The destination's folder lies outside
  the Media folders or has no path in the app, so the app can't check it for files already there,
  and a replacement is sent only after that check.") —; or no matched work ("This release matches no
  film or held episodes of yours."). A work can't be replaced when it was matched by its title alone
  ("Matched by title alone, which never allows a replacement."), when its ids conflict with the
  release's ("Its ids conflict (TMDb), so it can't be replaced."), when a TV title names no season,
  or claims none of the episodes held. A copy can't be ticked, the first that applies: no path in
  the app, or outside the Media folders; a library folder holding it isn't ticked; a TV version its
  own library item places outside the claim or under another series; a replacement not yet ended
  already holds it, by its path or its device and inode; not inspected, or inspected without an
  identity; the same file is also a copy of a work that can't be replaced — one file is one copy
  however many works list it, and the strictest reason holds —; then, read fresh from the disk,
  gone, unreadable, or "Changed since the app inspected it." (device, inode, size or modification
  time). A copy whose reading failed stays tickable, without its dynamic-range chip. A work matched
  by title and year — every Toloka release's — asks the owner to tick the server's own words, "This
  release is Alien (1979)", or for a series what the release claims of it, "This release is The
  Penguins of Madagascar (2008), season 2", "…, seasons 1–5", "…, season 2, episodes 1–5", with the
  hint "Matched by title and year only. Your tick lets the app schedule this; nothing moves unless
  Jellyfin later gives the new file this work's id."; two works whose words would read the same are
  told apart by their libraries ("… in “Фільми”"), else by the name of each one's first copy ("…
  (the copy “Halloween.2018.1080p.mkv”)"), else by their place ("… (1 of 2)"), in the dialog and in
  what is kept. **Send** in Replace mode waits, saying why, while the copies are read or couldn't
  be, for a reason the whole release has, for a destination folder MAM-38's check can't look into
  ("The app can't check this destination's folder for files already there, and a replacement is sent
  only after that check. Pick another destination, or Keep both."), for a copy that can be ticked,
  for one ticked ("Tick the copies to replace.") and for every confirmation a ticked copy's work
  needs ("Tick “This release is Alien (1979)” first."). The summary's **Existing copies** then lists
  the ticked copies by work — "Alien (1979): 1 copy" — with their paths in the app, and "Conditions:
  the old copy would move aside only after the new one is downloaded, checked and shown in Jellyfin
  as the same work; a copy held by a torrent waits until you remove that torrent in qBittorrent.
  This version only schedules the replacement: once the download is done it waits, and nothing is
  checked or moved until a later version.", frozen in the sent view (MAM-25 below rewords its last
  sentence, as the new file is now checked, and MAM-26 below rewords the whole, as the old copy now
  moves into the quarantine). A refused schedule reads the
  copies again, so the list shows what changed. The dialog widens to 48rem while it lists works and
  stacks the copies at phone width, long names wrapped. `DownloadDialog.replace.test.tsx`,
  `DownloadDialog.test.tsx` (the member's view), `replacements.test.ts`, `replacements-api.test.ts`,
  `queries/replacements.test.tsx`, `styles/downloads.test.ts`, `ReplacementOptionsHttpTests` and
  `ReplacementOptionsEpisodesHttpTests` pin it.
- **The dialog marks the copies a torrent holds, from a new read of every torrent with its files
  (MAM-24, ADR 0031, ADR 0005).** Once the owner picks Replace after verification, the dialog asks
  the options again with `holdings=true`, and only then, and only while a copy can be ticked, the
  server reports on every download client, switched off or not, and asks each one it may ask once,
  all at once, through the client contract's new `ListJobsWithFilesAsync`: for qBittorrent the
  sign-in, `app/version` and one `GET /api/v2/torrents/info?includeFiles=true`, every torrent in
  every state and category, never `includeTrackers`. It needs qBittorrent 5.2 or later, whose Web
  API (2.15.1) lists the files with the torrents; an older one ignores `includeFiles` without a
  word, which would read as nothing held, so it is asked nothing more and answers "This is
  qBittorrent 5.1.4; telling which torrents hold a library file needs qBittorrent 5.2 or later.". A
  torrent without a usable hash, or files the app can't read, fail the whole answer; a torrent still
  fetching its metadata comes without files, never with none; an answer over the 4 MiB cap is
  "qBittorrent's list of every torrent with its files is larger than the app reads (4 MiB), so the
  app couldn't tell which torrents hold these copies.". Each torrent file's path — its save path
  joined with its path in the torrent — is mapped through that client's path mappings as a whole,
  and only a file of a tickable copy's size inside the Media folders is looked at on the disk, once
  per path: the copy's own device and inode make it a hold, "Held by the torrent
  “Alien.1979.Remastered.1080p” in “qBittorrent”: before this copy can move you'll remove that
  torrent there without its files — which also stops seeding its other files." — said once per
  torrent of a work, with how many of its copies, each copy then marked "Held by “…”" —, and the
  copy's data under another name a torrent lists marks it "This file has another name, so moving it
  frees no space. It is the torrent “…”'s file."; a copy with more than one name gets the first
  sentence whether or not a torrent was found — the other name usually lies outside the Media
  folders —, and from the first read on in either mode, as it needs only the disk. Holds are told
  apart by client and torrent id, so two torrents of one name, a cross-seed, are two. A hold never
  keeps a copy from being ticked: the owner may remove the torrent while the new copy downloads. A
  client the dialog didn't read is reported with the app's short sentence after the dialog's own
  words — "Couldn't check whether a torrent in “seedbox” holds these copies: “seedbox” is switched
  off.", "… This version of the app can't read “transmission”.", a secret it can't open, the
  client's own failure masked of its secrets, or "The app couldn't read what “qBittorrent”
  answered." for an answer that broke the reader, logged with its type and stack and never its
  message —, and one some of whose torrents couldn't be placed — no save path, no metadata yet, a
  path no mapping covers or two cover differently, an unknown size, a file of the copy's size the
  disk doesn't show — as "Couldn't check 3 torrents in “qBittorrent”: their files couldn't be placed
  in the app's folders."; a screen reader reads these with **Send**. Nothing uncertain reads as "not
  held", and scheduling stays allowed. A Keep both send never reads a whole client, and the schedule
  doesn't read the torrents again: the read that decides comes right before a move, a later
  version's (MAM-26 below brings it into this one). The log names a client by its id, with counts,
  never a torrent's name or a path. Tried
  against the reference installation's qBittorrent on 2026-10-09, read only, by the app's own
  adapter and sealed settings, booted on the development PC over a copy of its data directory: it
  answered in 355 ms with 358 torrents — 353 seeding, 4 errored, 1 downloading —, every one with its
  files, 848 files, each path below its save path with the torrent's folder first, as the adapter
  reads it, and each one mapped into the app through the client's `/downloads` → `/media` pair, none
  left uncovered; that qBittorrent passed the 5.2 gate. The marks themselves met no real file: that
  PC reaches none of the media. `QBittorrentAdapterJobsWithFilesTests` (the requests, every torrent
  whatever its state, the files read exactly, no metadata, the version gate at 5.1.4 and 5.2.0, the
  bodies that fail the whole answer, a renewed session, transport errors, the size cap, the timeout
  and a cancel), `ReplacementOptionsHoldingsHttpTests` (holds in any state, a cross-seed, a
  same-size file that is another file, a second name, other names, unplaced torrents, each way a
  client isn't read, a qBittorrent too old, nothing read without `holdings` or while no copy can be
  ticked), `DownloadsSendHttpTests` (a Keep both send never reads the whole client),
  `DownloadClientNeverRemovesTests` and `IntegrationsInfrastructureExtensionsTests` pin it.
- **A scheduled replacement is written with its download before anything is sent, and sent strictly
  (MAM-24, ADR 0013, ADR 0032, ADR 0034, Vasyl's decisions of 2026-10-09).** **Send** in Replace
  mode posts `POST /api/replacements` with
  `{ requestId, releaseId, downloadClientId, destinationId, fileIds, confirmedWorkIds }`, ids only:
  the server reads every path and identity itself. It runs `POST /api/downloads`'s send — the same
  rules, request id, release, client, destination and "already sent" checks, one core for both — and
  then plans the replacement from the database and the disk alone, asking no client before the job
  is written: replacement on, the release's stored payload not a magnet, a matched work, 1 to 500
  copies each ticked once, every ticked id a copy the release offers (400 "One of the ticked copies
  isn't a copy of a work this release matched. The list was read again: check the ticks and send
  again."), every confirmation one a matched work needs and with a copy of it ticked (400), every
  ticked copy's work and the copy itself able to be replaced, each copy read on the disk again (409
  naming the file, such as "“Alien (1979).mkv” changed since the app inspected it, so it can't be
  replaced. Inspect the library again, or pick Keep both."), and every title-and-year work with a
  ticked copy confirmed (400 "Tick “This release is Alien (1979)” to replace a copy of it. …"). A
  refusal writes and sends nothing. Then, in the job's one save, beside the job and its
  `DownloadRequested` row: the replacement job, `Scheduled`, with who scheduled it, when, and the
  edition words of the release's title; one row per ticked copy — its file, version, work and
  matched work, an episode's season and number as its own library item places it, the library's path
  and the app's, and the identity read from the disk then: device, inode, size, modification time to
  the tick and link count, with the version's label —; one row per matched work with a ticked copy —
  its kind, title, year, evidence, the confirmation's words and whether the owner ticked them — with
  its registry ids then, the owner's TMDb links counted and a series' its show's own; a
  `ReplacementScheduled` audit row, its target the replacement job named by the release; and one
  `ReplacementWorkConfirmed` row per confirmed work, named by the words he ticked. All of it is a
  snapshot keyed to no row a sync rebuilds; the release, the payload kind, the client, the
  destination, the client's job id and the info hashes are its download job's. The send is then
  strict: a gateway link that turns out a magnet once fetched ("This release turned out to be a
  magnet link, whose files the app can't check before sending, so nothing was sent and no copy will
  be replaced. Pick Keep both to download it."); a category whose folder isn't inside a library the
  app knows ("The folder of the category “Movies” isn't inside a library the app knows, so nothing
  was sent and no copy will be replaced. Pick Keep both to download it, or the owner fixes the
  destination."); any file already at a place the release would write, the same bytes included, told
  by being there and its pieces never read ("“qBittorrent” would write over “Alien (1979).mkv” in
  the folder of the category “Movies”: a file is already there, and a replacement never lands on a
  file already there, so nothing was sent."); and every place MAM-38's check couldn't look at ("The
  app couldn't check every place this release would write in the folder of the category “Movies”
  (the folder, or a link on the way to one of its places, lies outside the Media folders), so
  nothing was sent and no copy will be replaced. Pick Keep both to download it.", with the reason of
  each unchecked case) — each ends the job `Failed`, and its replacement with it, nothing handed to
  qBittorrent. The dialog keeps the form with the sentence, and the owner picks Keep both himself: a
  replacement is never sent as a plain download on its own. The answer is 201 with the job and its
  `Location` under `/api/downloads/`, its replacement on it; the same request id with the same
  copies and confirmations, compared as sets, answers 200 with that job and sends nothing, and with
  other ticks, or a request id a Keep both send used — or the other way round —, 409 "This request
  id was already used to send a different download. Reload the page and try again.". Two copies of
  one request at once make one job, one replacement and one add, and a copy whose twin wrote its job
  while it planned is answered with that job, not refused as already scheduled. Left open: two
  different schedules racing for one copy at the same instant can both pass the "already scheduled"
  check, ADR 0013's kind of gap. `ReplacementScheduleHttpTests` (everything written before the
  `.torrent` is fetched, the fresh identities to the bit, a confirmation and its row, an episode's
  place, the ids kept after a sync, every 400 and 409 with nothing written, a file two works list,
  twins, a copy another replacement holds, the camel-case answer, every replay and race) and
  `ReplacementScheduleSendHttpTests` (the same bytes already there, a gateway link that turns out a
  magnet, a destination outside every library, a library outside the Media folders, a link leading
  out, a name the app can't look up, a list of files it can't read, a qBittorrent that can't list
  the category's torrents, and an add qBittorrent refused after the check) pin it.
- **A scheduled replacement follows its download to `Downloaded` and waits there, and the Jobs page
  says where it stands (MAM-24, ADR 0014, Vasyl's decision of 2026-10-09).** After every pass of the
  downloads' following, in the same scope and its own try, the replacements still followed are
  brought in line with their downloads — `Scheduled` → `Submitted` → `Downloading` (↔ `Paused`) →
  `Downloaded`, where the following leaves them for the check of their new files (MAM-25 below) —,
  but only for a download whose check before the send passed (`overwriteCheck` `Checked`, strict
  mode's only passing verdict). A download that failed after it fails its replacement ("The download
  failed, so nothing will be replaced."), one refused before it was sent too ("The download was
  refused before it was sent, so nothing will be replaced."), and so does a send the app stopped
  during, which the following adopted with nothing checked ("The app stopped while sending this
  download, before it checked where the new files would land, so nothing will be replaced."); a
  download that needs linking puts it at `NeedsReview` ("qBittorrent's downloads couldn't be told
  apart for this release, so the app can't follow it to verify the new file."). **Cancel job**
  cancels it in the same transaction, from any state still followed, `NeedsReview` included, with a
  `ReplacementCancelled` audit row ("You cancelled the download, so nothing will be replaced.";
  MAM-27 below adds the owner's **Keep both**, which cancels the replacement alone, its download
  left as it is).
  Each write is conditional on the state it read, the mirrored steps write no audit row, a bug on
  one replacement costs only that one the pass, and everything is rows, so a restart goes on where
  they stand. Every job the API answers — the list, a job, a send, a schedule, a cancel — carries
  `replacement` `{ state, copyCount, reason, switchedOff, copiesOutsideTickedFolders }`, `null` for
  a Keep both job, read in one batch per page. The Jobs pages and the dialog's sent view show a line
  under such a job: "Replacement scheduled: 2 copies, kept until the new one is verified.",
  "Replacement waits for verification: 1 copy. This version doesn't verify or move anything yet." or
  "Replacement stopped: <reason>" — only "Replacement stopped." on a failed job, whose error already
  says why —; while the download goes on and the gates say so now, "Replacement scheduled, but
  replacement is switched off: nothing will move while it is off." and "1 copy is no longer in a
  ticked folder and won't move.". (MAM-25 below takes a `Downloaded` replacement on to the check of
  its new files, and the line with it: "Replacement checks the new file: 1 copy, kept until it is
  verified.", then "New file verified: …" or "Replacement needs review: <reason>"; MAM-26 below
  takes a verified one on into the quarantine, with lines of its own; MAM-27 below adds until when
  the check waits for Jellyfin, and what to do at a review.)
  `ReplacementFollowingHttpTests` (to `Downloaded` and no further
  with nothing sent, a restart, a send still under way, a cancel with its row, the review a cancel
  ends, a failed download, an adopted interrupted send, a follower that throws, the gates on a job's
  answer), `ReplacementJobPersistenceTests` and `JobsPage.test.tsx` pin it.
- **Replacing copies is the installation owner's alone, by where its routes live (MAM-24, ADR 0033,
  Vasyl's decision of 2026-10-08).** `GET /api/replacements/options` and `POST /api/replacements`
  live in `Controllers/Replacements`, and every action there, reads included, is owner-only by
  `ReplacementsAreOwnerOnlyConvention`, not by an attribute each action has to remember: a member
  gets 403 "Only the installation owner can do this." before anything is read, written or asked of
  qBittorrent, and keeps Keep both. `ReplacementsAreOwnerOnlyHttpTests` discovers every endpoint in
  that namespace and sends each as a member (403, no job, replacement or audit row written, nothing
  asked of qBittorrent) and without a token (401), and `ControllerAccessDeclarationTests` lists both
  actions. The options route answers 200
  `{ unavailable, destinationProblem, releaseEditions, works, clients }` (the README's API table has
  each shape), 400 "Pick the release to send.", "Pick the destination to send it to." or "Say true
  or false for whether the download clients' torrents are read.", and 404 as a send does for a
  release of no search of the caller's from the last day or a destination that is gone; it writes
  nothing. Nothing of it but qBittorrent's read above was tried live: no replacement was scheduled,
  the development database has no Media folder and no inspected file, and production runs no build
  with MAM-24. The pages were looked at in a browser at 1280 px and 375 px on that development
  database: the Media folders page and its save, and the dialog's two reasons for a real Toloka
  release (`SwitchedOff`, then `DestinationNotUsable`); the list of copies, the marks, the
  confirmation, Send's reasons, the summaries, a member's view and the Jobs page's line only with
  answers made up in the page.
- **Once its download is done, a replacement checks each new file before anything is touched, and
  ends `Verified`, or stops with the reason, both copies staying (MAM-25, ADR 0035, ADR 0032, ADR
  0034, Vasyl's decisions of 2026-10-10).** A background loop of its own,
  `ReplacementVerificationTracker`, runs a pass when the app starts and 15 s after each pass ends,
  apart from the downloads' 2 s following and whatever the replacement switch and the ticks say (ADR
  0033): it claims a `Downloaded` replacement — `Inspecting`, with a `ReplacementCheckStarted` audit
  row — and takes it a step on every pass from its rows, so a restart goes on where it stood. *Check
  1:* qBittorrent is asked again, never the stored rows, which stop at the finish: the download
  seeding or complete at a progress of 1, its content in its save folder, and each file it downloads
  with a video file's extension complete by its own word — "“qBittorrent” hasn't finished
  “Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.Bonus.1080p.mkv” (73 % done)." —; a download it
  reports an error for ("“qBittorrent” reports an error for this download, so the app can't confirm
  its files are complete."), no longer has, or that holds no such file stops for review, and one
  still moving its files waits. *Check 2:* each new file is placed in the app through the client's
  path mappings ("The app can't tell where the new file “Alien.1979.Directors.Cut.2160p.mkv” is: No
  path mapping of this integration covers “/srv/elsewhere/Alien.1979.Directors.Cut.2160p.mkv”.") and
  in the one library folder holding it, the deepest where folders nest ("The new file
  “/media/elsewhere/…” is in no library folder the app knows, so no library can be asked to confirm
  it."), then read by the media inspector inside the Media folders. `Failed` is for a bad file only
  — one ffprobe can't read as media, not a regular file,
  "“/media/movies/Alien.1979.Directors.Cut.2160p.mkv” is empty.", "… has no video stream." and "The
  media inspector found no running time in “…”." —; a file of another size than qBittorrent
  downloaded, one whose identity the platform doesn't give, and "The new file “…” is the old copy
  itself." stop it for review. Then qBittorrent's whole listing (5.2 or later) must show no other
  download writing a new file, by path or by device and inode, which closes MAM-24's accepted gap:
  "“qBittorrent”'s download “Alien remux” also writes
  “/media/movies/Alien.1979.Directors.Cut.2160p.mkv”, so the app can't tell the new file is this
  download's own."; another download the app can't place — a magnet still fetching its metadata, a
  save path or a file no mapping places, a file inside the Media folders whose size or identity
  can't be read — leaves that unclear: the check waits, and once the hour runs out stops ("The app
  can't place “qBittorrent”'s download “Alien magnet” in its folders, so it can't tell whether that
  download writes the new files."); and a qBittorrent too old to list them stops it at once ("This
  version of “qBittorrent” can't list the files of its other downloads, so the app can't tell
  whether another one writes the new files."). *Check 4:* once every file passed, each library
  holding new files is sent `POST /Library/Media/Updated` with their paths in its own terms, at most
  500 to a request (`IMediaLibraryFileLookup.MaximumPaths`, the reads batched alike, so a pack of
  any size is asked and read whole), and asked again on the next pass while it doesn't take it;
  `LibraryAskedUtc` is the moment it took it, after the files' checks. A call the adapter refuses as
  the app's own mistake stops the job at once ("The app refused its own request to “Living room”
  about the new files as one it should never make, so they can't be confirmed."). From then on a
  pass only looks at each file's identity again ("The new file “…” changed after it was checked.",
  or "Nothing is at “…”." for one gone) and reads, by the id each path computes, the item Jellyfin
  made of it — a hidden alternate version included, a trailer read like any extra as no work — and
  of each ticked episode copy, until each is listed and read: a running time on it, and an episode's
  number. *Check 3 and the pairs* (ADR 0032's MAM-25 section): a film's item must carry the old
  work's TMDb or IMDb id, the owner's TMDb link counted, and no other value of a scheme the work
  has; an episode's series must be the show by its ids, ADR 0017's way, at the ticked copy's season
  and number, and the ticked episode's own item, as Jellyfin reads it now, still the show's at the
  number it had when scheduled; each ticked copy pairs with exactly one new file of its work, and a
  new file no copy pairs with is simply added — so is a film Jellyfin identifies as another work,
  another value in a scheme both carry, once every ticked copy found its new file: a collection
  pack's other film, as Vasyl decided on 2026-10-10. Another work while a ticked copy has found
  none, as that film may be its new file misidentified ("“Living room” identified “…” as another
  work (IMDb tt0090605), not Alien (1979)."), a new episode in another series whatever paired,
  conflicting ids ("… conflicting ids: IMDb tt0078748 is Alien (1979)'s, TMDb 679 is not."), ids for
  a work the app holds no TMDb or IMDb id of ("“Living room” gave “…” IMDb tt0078748 and TMDb 348,
  but Alien (1979) has no TMDb or IMDb id here to compare them with."), a film item for an episode
  copy or the other way round, a ticked episode Jellyfin now numbers otherwise ("“Living room” now
  numbers the old copy “…” season 2, episode 3, not season 2, episode 1 as when it was scheduled.")
  or places in another series ("“Living room” placed the old copy “…” in another series (…), not
  …"), two new files of one work ("Two new files are Alien (1979): “…” and “…”; the app can't tell
  which one replaces the old copy."), none ("No new file is Yellowstone (2018), season 2, episode 2,
  so “…” has nothing to replace it."), a ticked copy that is one half of an episode ("… (its name
  gives the episode the letter “a”), so it can't be replaced on its own."), a file covering a ticked
  episode and the next, by Jellyfin's end number or a range in its name ("… covers episodes 2–3 of
  season 2, so it can't replace “…” alone."), a name claiming another edition than the release, once
  every title the work goes by — and an episode's own — is taken out of it ("… claims Extended cut,
  while the release claimed Director's cut.") and a paired file under three quarters of the old
  copy's measured running time ("… runs 1:06:40, less than three quarters of the old copy's
  1:33:24.") stop it for review; a longer cut, and an old copy never measured, pass. *The hour*,
  Vasyl's: what can't be settled yet keeps the job `Inspecting` an hour — from the check's start for
  qBittorrent and the files, from the moment Jellyfin took the request to look for Jellyfin —, the
  owner free to fix the new item in Jellyfin meanwhile, then stops it with what is still missing:
  "“Living room” gave “…” no TMDb or IMDb id within an hour, so it can't be told to be Alien
  (1979).", "“Living room” gave “…” TMDb 348 within an hour, but Alien (1979) is known here only by
  IMDb tt0078748." (ids only of schemes the work has none of), "“Living room” gave the old copy “…”
  no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of …",
  "“Living room” didn't list “…” within an hour of being asked to look.", "“Living room” hadn't
  finished reading “…” within an hour.", "The app couldn't read “Living room” within an hour of
  asking it to look at the new files: Jellyfin answered 503 Service Unavailable. Its own log says
  why." or "The app couldn't ask “qBittorrent” about this download within an hour: …". An
  unavailable Jellyfin never lets a file pass, and a wrong or conflicting id stops at once once
  every file is read. A library of a type that can't be asked to look stops the job at once ("This
  version of the app can't ask “…” to look at a file, so the new files can't be confirmed."). Each
  reason names an integration by its name under Integrations, quoted, except one whose type this
  build can't use, named by its type. *The end:* `Verified`, `NeedsReview` or `Failed`, a
  conditional move — another writer that got there first wins — in one transaction with the evidence
  and a `ReplacementVerified`, `ReplacementNeedsReview` or `ReplacementFailed` row, no actor, its
  target the replacement job named by the release: the app's own decisions, where the following's
  mirrored steps write none. *The evidence*, kept with the job for MAM-27 to show (it does, below):
  `ReplacementJobNewFiles`, a row per new file in the client's order — its name, size and progress
  by qBittorrent, its path in the app, its library and its path there, its device, inode, size,
  modification time, running time, video codec and frame size as read, its item, kind, whether read,
  series item, season, episode and end number, when last read, the work it was identified as and why
  it stopped the job —, with `ReplacementJobNewFileIdentifiers`, its item's and its series' ids; and
  on each ticked copy its pair, its end number by Jellyfin, its measured running time and its
  problem. Nothing moves: a `Verified` job holds both copies until MAM-26. qBittorrent is only read,
  and Jellyfin hears only the request to look, every other call a read (`System/Info` before each,
  `Users`, `Items/{id}`, `Items?ids=`). The log names ids, states and counts, never a path, a title
  or a sentence, and a check that throws costs its job that pass, logged with its type and stack.
  Tried live only as a read: on 2026-10-10 a probe on the development PC read the reference
  installation's Jellyfin 12.2.0 with the app's stored settings, GETs only, and found the facts the
  check stands on (the next entry) — `LibraryMonitorDelay` 60 s, the real-time monitor on in all
  four media libraries, no file with an end number or a half's letter —; the check itself and its
  pages ran against fakes only, and no replacement was downloaded on any installation, which
  MAM-28's check is for. `ReplacementVerificationHttpTests` (a passing film with its evidence and
  its paths in the app and in Jellyfin, by IMDb id or by the owner's TMDb link alone, three quarters
  exactly and a longer cut, an old copy never measured, no edition read from any title the work goes
  by, replacement switched off, a collection pack's other film simply added, another work — the
  pack's film of the ticked copy among them —, conflicting ids, ids sharing no scheme with the
  film's, two new files, another edition, too short, a check that throws, the loop),
  `ReplacementVerificationFilesHttpTests` (an error and a download gone, a file not finished,
  qBittorrent unreachable or switched off for the hour, its client removed, two downloads of the
  release, a path no mapping places, a file in no library folder or in folders of two libraries, a
  file ffprobe can't read, empty, without video or a running time, the old copy itself, another
  download writing it at its path or by a link at another, one the app can't place, a qBittorrent
  too old), `ReplacementVerificationEpisodesHttpTests` (a season pack with an extra episode, a pack
  of more files than one request takes, files of two episodes by end number and by name, a ticked
  copy of two episodes or one half, a ticked episode Jellyfin now numbers otherwise, a missing
  episode, two files of one, film and episode crossed, conflicting series ids) and
  `ReplacementVerificationLibraryHttpTests` (Jellyfin unavailable, or switched off or failing its
  item reads after taking the request, no id, not listed, not read, the request not taken — each
  until the hour, then review —, the request taken late and the hour reckoned from then, the owner's
  fix within the hour, a type that can't be asked, a library removed, a file changed or gone, later
  passes going on from the rows, a verified job never read again), all over TestInfrastructure's
  `ReplacementVerificationHarness`, whose Jellyfin knows the app's `/media` as `/data`, with
  `ReplacementLibraryPlacesTests` (a file's path in its library's terms),
  `ReplacementFollowingHttpTests` (the following never touches `Inspecting` or `Verified`),
  `ReplacementOptionsHttpTests` (both hold their copies), `NewFileCompletenessTests`,
  `NewFileReadabilityTests`, `EpisodeRangeNamesTests`, `ReplacementPairingFilmsTests`,
  `ReplacementPairingEpisodesTests`, `ReplacementPairingVerdictTests`,
  `ReplacementVerificationRulesTests`, `ReplacementJobStatesTests`, `ReplacementJobPersistenceTests`
  and `AddReplacementVerificationMigrationTests` pin it.
- **The media library contract learns to look at new files and to read the item each became, as an
  optional capability (MAM-25, ADR 0005, ADR 0030, ADR 0035).** `IMediaLibraryFileLookup` — never a
  member of `IMediaLibraryProvider` — is found by the adapter registry as
  `IMediaLibraryItemIdentifier` is, and Jellyfin's adapter implements it. `RequestScanAsync` sends
  `POST /Library/Media/Updated` with `{"Updates":[{"Path":…,"UpdateType":"Created"}]}`, every path
  in one body, once, whatever comes of it: 204, which Jellyfin answers before it looks, is the one
  answer taken; 401 and 403 are the key's — "Jellyfin accepted the key but won't let it ask the
  library to look at new files (403 Forbidden). Use a key from Dashboard → API Keys, not a user's
  access token." —, a 5xx the server's, and anything else, a 200 included, an answer Jellyfin
  doesn't send for it. `ReadFilesAsync` reads `GET /Users` for the first enabled administrator, then
  for each path `GET /Items/{id}?userId=` for the id the path computes as a film's, an episode's, a
  plain video's and a trailer's, in that order — a 404 tries the next, and an answer whose `Path`
  isn't exactly the path asked is another file's; Jellyfin 12.2 gives a trailer a type of its own,
  and it reads, like any extra, as no work —, and gives per path whether Jellyfin lists it, its item
  and kind, whether Jellyfin has finished reading it (a running time, and an episode's
  `IndexNumber`), its ids by the sync's rules, an episode's season, number and end number
  (`IndexNumberEnd`, which the sync still doesn't read), and its series' ids, one
  `GET /Items?ids=…&fields=ProviderIds` per series; any failed call fails the whole read with its
  status and sentence, so an odd Jellyfin never lets a file pass. A caller's mistake — no path, more
  than `IMediaLibraryFileLookup.MaximumPaths` (500), a path blank, not absolute, with a `.` or `..`
  folder, a control character or over 4096 characters — throws before anything is sent; the check
  sends its paths in batches of at most that many. Ids are computed, not listed, because `/Items`
  has no path filter and leaves alternate versions out — the usual replacement, a second version of
  a film the owner has —: the read-only check of the reference installation's Jellyfin 12.2.0 on
  2026-10-10 found `new Guid(MD5(UTF-16 of the type's full name + the path))` right for 880 of 880
  movies and 440 of 440 episode files, the 29 episodes it missed virtual and without a path, and
  `GET /Items/{id}?userId=` answering a hidden alternate version whole while `GET /Items?ids=` of
  the same id answered nothing; that alternate is the new vendor sample
  `Jellyfin/episode-alternate-version-item.json`. `FakeJellyfinServer` answers the request to look —
  recording each request's paths, 204 at once or a status a test sets, and a hook that makes
  Jellyfin's items when asked — and plants films and episodes under the ids their paths compute,
  read or not yet; `JellyfinItemIds` repeats the formula on purpose, pinned against every recorded
  movie and episode file. `JellyfinAdapterFilesScanTests`, `JellyfinAdapterFilesReadTests`,
  `JellyfinAdapterFilesReadFailureTests`, `FakeJellyfinServerFilesTests` and `VendorSamplesTests`
  pin it.
- **Right before the move, a verified replacement checks its old copies again and reads every
  download client's torrents once more, and anything in the way stops it for review with nothing
  moved (MAM-26, ADR 0030, ADR 0031, ADR 0033, Vasyl's decisions of 2026-10-08 and 2026-10-10).** A
  background loop of its own, `ReplacementQuarantineTracker`, runs a pass right after the app starts
  and 15 s after each pass ends, apart from the check of the new files, which still moves nothing.
  A pass takes up each `Verified` replacement, oldest first (`IX_ReplacementJobs_Quarantine`), held
  in the process-wide `ReplacementJobsInHand` while it works, so an owner's action on it waits for
  the next pass and the other way round. While replacement is switched off the job is left
  `Verified` and untouched — no disk, client or library read, nothing written —, as its line already
  says. *Check 5*, for each ticked copy whose video isn't in the quarantine yet: still inside the
  Media folders ("“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” is no longer inside
  the Media folders, so it stays where it is.") and in a ticked folder ("… is no longer in a folder
  ticked for replacement, so it stays where it is."); no clip of a Blu-ray or DVD folder ("“00000.m2ts”
  is part of a disc folder, which can't be replaced one file at a time."); read fresh from the disk,
  inside the allowed folder whose quarantine takes it alone — a copy a folder link leads to in
  another allowed folder is that folder's, where a Restore could never reach back for it —, with the
  device, inode, size and modification time to the tick it was scheduled with ("… is gone,
  so there is nothing to move.", "… changed since it was picked for replacement, so it stays where it
  is.", or the disk's own sentence after "The app couldn't read “…” again, so it stays where it
  is:"); neither a new file's path nor its identity ("… is the new file's own path, so it stays where
  it is."); and its paired new file still the file the check read ("The new file paired with “…”
  changed or went since it was verified, so nothing moves."). *Companions:* the regular files of the
  copy's folder, listed through the new write contract without following a link — a video that is
  itself a link, which the listing leaves out, stops the job ("“Alien (1979) Theatrical Cut.mkv” is a
  link to another file, and the app moves only a file itself, so it stays where it is.") — and picked by
  `QuarantineCompanions` — the video's name without its extension and a dot, no media file itself,
  and none that begins with the longer name of another video there and a dot —, each with its
  identity ("The app couldn't read “…” well enough to tell whether a torrent holds it." otherwise).
  *Check 6:* one read of every download client's torrents with their files for every name the move
  would rename, through the holdings reader's new overload for names, which leaves out a client none
  of whose path mappings reaches an allowed folder and asks it nothing (a client without mappings is
  read) — but never a client the app has sent a replacement to (MAM-28, ADR 0031, Vasyl's decisions
  of 2026-10-12): every client a replacement's download went to and took, whose integration still
  exists, is asked whatever its mappings say, as one deleted or pointed outside the Media folders
  since puts its torrents' files outside them, where the app never looks. A client not read, or read
  in part, stops the job ("The app couldn't tell whether a torrent in “qBittorrent” holds the old
  copy: This is qBittorrent v5.1.4; telling which torrents hold a library file needs qBittorrent 5.2
  or later.", "The app couldn't place 1 of “qBittorrent”'s torrents in its folders, so it can't tell
  whether one of them holds the old copy."), and so does a torrent of a client the app has sent a
  replacement to that lists a file its mappings put outside the Media folders with the file name of
  a name the move would rename, in any letter case, no size, or that name's size unless the name's
  file has other names ("“qBittorrent”'s path mappings put 1 of its torrents outside the Media
  folders, so the app can't tell whether that torrent holds the old copy: check those mappings under
  Integrations, then press Retry.", after the couldn't-place sentence when a client has both), while
  a torrent that lists nothing like a name stops nothing and the copy moves, and so does one listing
  only the size of a copy with other names — its hardlink twin there, `/data/torrents` hardlinked
  into `/data/media`, is the torrent's own name, which the move leaves seeding; a video any
  torrent lists, in any state, stops it with ADR 0031's sentence, each torrent once by client and id
  ("Still held by the torrent “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there
  without its files, then press Retry.", or "Still held by the torrents “…” in “qBittorrent” and “…”
  in “seedbox”: remove those torrents there without their files, then press Retry."); a companion a
  torrent lists stays, named on the job ("Held by the torrent “Alien.1979.Subs” in “qBittorrent”, so
  it stays where it is."), and its video still moves; a second name of a torrent's file moves, its
  link count kept so that Delete for good counts no room for it. A pass with no name left to move —
  a Retry of a replacement whose every video is in the quarantine already, or deleted from it — asks
  no client at all. A stop before the move is one
  transaction — `Verified` → `NeedsReview` with the reason, the copy's problem, `QuarantineCheckedUtc`
  (which marks a stop the owner's Retry may take) and a `ReplacementNeedsReview` row with no actor —;
  nothing is renamed, no name row written, and the clients get nothing but the listing.
  `ReplacementQuarantineChecksHttpTests` (every reason of check 5, a listing refused, a companion
  without an identity), `ReplacementQuarantineHoldsHttpTests` (a video held in any state, two
  torrents, a held companion, a second name, Retry once the torrent is gone, a path no mapping
  places, qBittorrent unreachable, switched off or too old, and, for MAM-28, qBittorrent's mapping
  deleted or pointed outside the Media folders, a hardlink twin seeding outside them, subtitles left
  waiting whose other name went, a seedbox an earlier replacement went through and one the app never
  sent a replacement to), `ReplacementQuarantineFlowHttpTests` (a mapping deleted, put
  back under Integrations, then the torrent removed), `ReplacementHoldingReaderTargetsTests`,
  `QuarantineCompanionsTests`, `DiscFoldersTests` and `ReplacementJobsInHandTests` pin it.
- **The old copy and its own companions move into `.mam-quarantine`, one `renameat2` each, through
  a new write contract, and the replacement rests `Quarantined` (MAM-26, ADR 0030, ADR 0033, ADR
  0009, Vasyl's decisions of 2026-10-08 and 2026-10-10).** Once the checks pass, one transaction
  claims the job — `Verified` → `Quarantining`, conditional on the state read —, writes a row of the
  new `ReplacementJobNames` per name the move plans — each ticked video, then its companions,
  `Planned`, or `Stayed` for one a torrent holds, with where it goes, the allowed folder whose
  quarantine takes it, and the device, inode, size, modification time and link count read right
  before — and a `ReplacementQuarantineStarted` row. A name goes to `<allowed folder>/.mam-quarantine/
  <replacement job id>/<its path inside that folder>` (`QuarantineFolder.PathOf`):
  `/media/.mam-quarantine/<id>/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv` here. Then name
  by name, videos first: the switch, the ticks and the allowed folders read again inside the
  intent's own transaction, under SQLite's write lock, so a Save of the Media folders either comes
  first and stops the move or comes after and finds the name `Moving` ("Replacement was switched off
  while the old copies were moving: 1 of 4 names is in the quarantine.", "“…” left the folders
  ticked for replacement while the old copies were moving: …", the count left out while none is
  there yet), an intent committed before the rename (`Moving`,
  `ReplacementNameMoving`), the rename, and its outcome committed after it (`Quarantined` with
  `ReplacementNameMoved`, or `Stayed` with the contract's sentence and `ReplacementNameNotMoved`),
  each name row its own audit target (`AuditTargetKind.ReplacementJobName`, named by the file's own
  name), no actor. A video that didn't move stops the job at `NeedsReview` ("“Yellowstone
  S02E02.mkv” couldn't be moved into the quarantine: … 1 of 3 names is in the quarantine."), what
  moved before it staying in the quarantine and its companions left `Planned`; a companion that
  didn't move stays, named on the job, and its video still moves. A rename that moved another file —
  one put at the name after the last look (`FileQuarantineStatus.Swapped`) — records the name `Lost`
  ("A different file now sits in the quarantine under “…”, as another file took its place just before
  the rename, and the old copy's own file wasn't found.") and stops the job the same way, whichever
  name it was. A Retry's run takes up the companions an earlier run left `Planned` beside a video it
  moved, each checked again as its video was — still ticked, still the file the plan read, else
  `Stayed` or, gone, `Lost` with why — and read by check 6 with the rest. Every video in:
  `Quarantined`, with `QuarantinedUtc` and a `ReplacementQuarantined` row (`Completed` when a Retry
  finds every video deleted from the quarantine for good). The write contract is `IFileQuarantine`,
  beside `IFileInventory`, which stays read-only by construction (`FileInventoryNeverWritesTests`):
  `Capabilities`, `ListFolderFilesAsync`, `ListFolderNamesAsync`, `MoveIntoQuarantineAsync`,
  `RestoreFromQuarantineAsync`, `DeleteFromQuarantineAsync` and `DescribeRenamePlacesAsync`, every
  member that changes a file named
  for the quarantine and none named for copying, writing over, linking, owners or permissions
  (`FileQuarantineNamesTests`). Its Linux implementation, `LocalFileQuarantine`, holds the file's
  folder and the allowed folder as `O_PATH | O_NOFOLLOW` descriptors reached by ADR 0009's walk — a
  move's file folder within the allowed folder whose quarantine takes it alone, and no folder whose
  links lead into a quarantine, for a move, a restore or a listing ("“…” already lies in a
  quarantine.") —, compares the name's `statx`, never following it, with the facts the caller expects,
  answers a rename whose new name shows another device or inode `Swapped`, refuses another
  mount by `STATX_MNT_ID` ("“…” and “…” are on different mounts, where a rename would be a copy."),
  makes the quarantine's folders one level at a time (`mkdirat`, 0755 before the umask), walks both
  folders again and compares the name once more, then makes one `renameat2(RENAME_NOREPLACE)`,
  flushes both folders, and checks that the new name shows the same device and inode. `EEXIST`,
  `EXDEV`, `EINVAL`, `EACCES`, `EPERM`, `EROFS` and `EOVERFLOW` each leave the name where it was with a
  sentence ("“…” already holds a file, and the app never writes over one.", "“…” is on a read-only
  mount.", "The app's user isn't allowed to change “…”: renaming or deleting a file needs write and
  search permission on its folder, and on the one it goes into."). It creates no folder outside
  `.mam-quarantine`, and `File.Move`, `FileInfo.MoveTo` and `Directory.Move` are never used; anywhere
  but Linux every write is `NotSupported` ("Only the app running on Linux renames or deletes a library
  file, as only there can a rename be made without ever replacing or copying one."), so a replacement
  there stops for review with nothing moved. The log names ids, states and counts, never a path.
  `ReplacementQuarantineHttpTests` (a film's video and its own companions moved with their
  identities, another video's companion left, a season whose second episode can't move, the switch
  off or a folder unticked between two names, the switch off before the move, a job an owner's action
  holds, the tracker), `ReplacementQuarantineChecksHttpTests` (another mount, a name taken, a
  platform that can't rename, a companion refused), `LocalFileQuarantineLinuxTests` (the real calls
  on overlayfs in Docker, as uid 1000 and as root: identity kept, folders reused, a changed file, a
  name taken, another mount on `/dev/shm`, a quarantine folder that is a link, a folder or the file
  swapped right before the rename, a link, a folder the app may not write to),
  `LocalFileQuarantineTests`, `QuarantineFolderTests`, `FakeFileQuarantineTests`,
  `FilesInfrastructureExtensionsTests`, `FileInventoryNeverWritesTests`, `FileQuarantineNamesTests`
  and `ReplacementJobPersistenceTests` pin it.
- **A move, a restore or a deletion a restart cut is found again by device and inode and stops for
  review, renaming and deleting nothing by itself (MAM-26, ADR 0030).** Each pass first takes every
  replacement left `Quarantining`, `Restoring` or `Deleting` that no one in the process holds, and
  looks each name caught between its intent and its outcome up, at its old path and in the
  quarantine, by device and inode: a name being moved is `Quarantined` when found in the quarantine,
  `Planned` again when found where it was, and `Lost` with why when found at neither; one being put
  back is `Restored`, `Quarantined` again or `Lost`; one being deleted is `Deleted` when gone,
  `Quarantined` when still there unchanged, and `Lost` when something else is there. A look that
  didn't answer found or not found — a read error —, or a name found nowhere while neither the folder
  it was in nor its folder in the quarantine is there — a mount missing —, decides nothing: the job
  stays as the cut left it, with a warning in the log, and the next pass looks again. Each finding
  writes its outcome row; then the job goes to `NeedsReview` with a `ReplacementNeedsReview` row and
  no actor — "The app stopped while moving the old copies: 1 of 3 names is in the quarantine.", "The
  app stopped while putting the old copies back: 1 of 3 names is back where it was." or "The app
  stopped while deleting the old copies for good: 1 of 3 names is deleted.", the count left out
  while it is none —, Retry offered, and Jellyfin is owed a notice when a cut move left a name in the
  quarantine or a cut restore brought one back. A cut restore that put every name back ends
  `Restored`, and a cut deletion that deleted every name `Completed`, with the row the owner's own
  end writes and no actor: a review there could only end in a Retry that moves the restored copies
  back, or in nothing (MAM-26's deviation from "a cut stops for review").
  `ReplacementQuarantineRecoveryHttpTests` (a cut after a rename and before its outcome, a cut
  between two names with nothing renamed twice and then retried, names found where they were or
  lost, a cut restore, a cut deletion, each with every name done, a look that fails or finds no
  folder left unsettled, a job someone in the app holds left alone) pins it.
- **The owner retries a replacement the move stopped, restores what it holds in the quarantine, or
  deletes that for good after a second confirmation, on the job's page (MAM-26, ADR 0030, ADR 0033,
  Vasyl's decisions of 2026-10-08 and 2026-10-10).** Four routes join `Controllers/Replacements`,
  owner-only like the rest, each for the caller's own replacement, another's or a missing one a bare
  404. `GET /api/replacements/{id}` answers every name the move planned — where it was and where it
  goes, its size, whether it has another name, its state and its problem —, the room the replacement
  takes in the quarantine and the room deleting it frees (a name with another name frees none, its
  link count read from the disk at the quarantine path while the file there is the one moved, so a
  torrent removed with its files since the move frees its room),
  whether replacement is switched off, and which actions are offered. `POST …/retry` takes a
  replacement the mover stopped — checks 5 or 6, a rename refused, a cut move, restore or deletion,
  as `QuarantineCheckedUtc` marks — back to `Verified` with a `ReplacementRetried` row, writing
  nothing to the media; the next pass checks everything again, and what is in the quarantine stays
  there. A stop of the new files' check is 409 "This replacement isn't waiting for review after a
  move, so there's nothing to retry here.": that Retry stays MAM-27's (MAM-27 below takes it, and
  shortens this sentence). `POST …/restore`, from
  `Quarantined`, or from `NeedsReview` with names in the quarantine, checks every name before
  anything is written — replacement switched off ("Replacement is switched off on the Media folders
  page, so nothing can be put back. Switch it on, then press Restore again."), an old path no longer
  in a ticked folder, a quarantine no longer inside the Media folders ("“/media/.mam-quarantine” is
  no longer inside the Media folders, so nothing was put back."), a file that changed in the
  quarantine ("“…” changed in the quarantine since it was moved there, so nothing was put back."),
  any name at an old path, a link leading nowhere too, as the folder's listing of names shows it
  ("“…” already holds a file, so nothing was put back."), an old folder gone ("The folder “…” is gone,
  so nothing was put back; the app creates no folder."), a platform that can't rename —, then puts
  each name back, videos first,
  by one `renameat2(RENAME_NOREPLACE)`, the switch and the ticks read again before each, with intent
  and outcome rows (`ReplacementRestoreStarted`, `ReplacementNameRestoring`, then
  `ReplacementNameRestored` or `ReplacementNameNotRestored`), and ends `Restored` — both copies in the
  library — with `ReplacementRestored` and a notice owed to Jellyfin, or `NeedsReview` and 409 with
  "“…” couldn't be put back: … 1 of 2 names is back where it was."; a rename back that moved another
  file records the name `Lost` and stops the same way. A replacement one of whose videos a Delete for
  good stopped part-way deleted is not put back — its subtitle would go back alone, and the job would
  end saying the old copy is back —: 409 "A video of this replacement was deleted for good, so nothing
  is put back without it. Press Retry, then Delete for good what the quarantine still holds.", and
  Retry settles it `Quarantined`. Each name is written inside its own allowed folder while a current
  allowed folder holds that one, so the quarantine stays in reach after the owner puts a folder above
  it in its place. `POST …/delete-for-good` with
  `{ names }`, the ids the confirmation listed, and only from `Quarantined`: 400 "Delete for good
  needs the files you confirmed." without them; 409 "This replacement holds nothing in the quarantine
  to delete.", "What this replacement holds in the quarantine changed since you opened it. Open
  Delete for good again." for any other set, "Replacement is switched off on the Media folders page,
  so nothing can be deleted. Switch it on, then press Delete for good again." or "“…” changed in the
  quarantine since it was moved there, so nothing was deleted.", nothing deleted; then it deletes each
  name from the quarantine alone, its identity compared again right before one `unlinkat`, and no
  folder (`ReplacementDeleteStarted`, `ReplacementNameDeleting`, then `ReplacementNameDeleted` or
  `ReplacementNameNotDeleted`), ending `Completed` with `ReplacementCompleted`, or `NeedsReview` and
  409 with "“…” couldn't be deleted: … names are deleted."; Jellyfin hears nothing of it, having
  heard of the move. The owner is the actor of each of these rows. A replacement being worked on —
  the mover's pass or another click — is 409 "This replacement is being worked on right now. Try
  again in a moment.", so two clicks at once rename or delete each name once. The job's page gains an
  **Old copies** section (MAM-27 below renames it **Replacement**) between its details and its
  files: every name with where it is now, the
  reason one stayed, wasn't put back or was lost, and the room in the quarantine, with **Retry**,
  **Restore** (a plain confirmation) and **Delete for good** (a red outline, whose confirmation lists
  every path with its size, "Frees 4.2 GiB." and how many files have another name, and sends exactly
  the ids it listed) as offered — the last two disabled with "Replacement is switched off on the Media
  folders page: nothing is put back or deleted while it is off." while it is off —; a member reads
  "Only the installation owner sees the old copies a replacement moves, and restores them or deletes
  them for good." and nothing is asked. A replacement with no name yet says "No old copy has moved
  yet: each moves into the quarantine once the new file is verified." while a move can still come
  ("No old copy has moved yet, and none moves while replacement is switched off." while it is off),
  and "Nothing of this replacement was moved." once it was cancelled, failed or stopped for review
  before its move. The Jobs row shows the owner's **Retry** beside a line the
  move stopped (MAM-27 below shows it for a stop of the check too, beside **Keep both**).
  `ReplacementRetryHttpTests`, `ReplacementRestoreHttpTests`,
  `ReplacementDeleteForGoodHttpTests`, `ReplacementDetailsHttpTests`,
  `ReplacementQuarantineFlowHttpTests` (a move then Restore, a move then Delete for good, a held copy
  then Retry, end to end with Jellyfin's requests and the audit rows in order),
  `ReplacementsAreOwnerOnlyHttpTests` (the four new routes as a member: 403, nothing renamed, deleted
  or written), `ControllerAccessDeclarationTests`, `JobDetailsPage.replacement.test.tsx`,
  `JobsPage.test.tsx`, `replacements-api.test.ts` and `queries/replacements.test.tsx` pin it.
- **Jellyfin is told an old copy went, and that it came back (MAM-26, ADR 0030, ADR 0005).** A move
  that put a name in the quarantine, and a restore that brought one back, owe a notice
  (`LibraryNoticeDueUtc`), which `IReplacementLibraryNotice` sends right after, and each pass after
  while it is owed: `POST /Library/Media/Updated` to each library holding the old copy's places, with
  the paths in its own terms — a name in the quarantine as `Deleted` at its old place, a name put back
  as `Created`, a video by the path the library reported for its copy —, one request per Jellyfin and
  kind of change, each path once, and never a quarantine path. Taken by every library, or nothing to
  tell, settles it; a Jellyfin that doesn't take it keeps it owed for an hour, then it is given up
  with a warning in the log; a library that can never be told settles it at once. A notice never
  changes the replacement's state. `IMediaLibraryFileLookup.RequestScanAsync` takes the kind of change
  (`LibraryFileChange`: `Created` or `Deleted`; anything else is a caller's mistake that sends
  nothing), and Jellyfin's adapter sends it as each path's `UpdateType`, which Jellyfin 12.2 doesn't
  read but which says what happened; MAM-25's look at new files sends `Created` as before.
  `ReplacementLibraryNoticeTests`, `JellyfinAdapterFilesScanTests` (paths that went, posted as
  `Deleted`; a change that is no member) and `FakeJellyfinServerFilesTests` (each request's update
  types recorded) pin it.
- **The Media folders page says whether a move can work in each ticked folder, lists the room each
  quarantine takes, and refuses a Save that would leave a quarantine out of reach (MAM-26, ADR 0033,
  ADR 0030).** Each library folder whose tick counts gets, from one `DescribeRenamePlacesAsync` for
  all of them, what keeps a rename into its allowed folder's quarantine from working there — another
  mount than the allowed folder's (`STATX_MNT_ID`), a read-only mount by the folder's own options in
  `/proc/self/mountinfo` (`MountTable.MountOptionsOf`), or no write and search permission for the
  app's user on it or on the allowed folder's `.mam-quarantine`, where a move makes its names — on the
  allowed folder itself while it has none (`faccessat`); what can't be read, a file or a link at
  `.mam-quarantine`, and every folder
  anywhere but Linux, is "couldn't check". A folder ticked in the form shows it: "A move into the
  quarantine can work here: the same mount as the quarantine, mounted read-write, and writable by the
  app.", or a line per problem ("Not on the same mount as its media folder's quarantine, so a move
  would fail: mount the media folder as one read-write mount.", "Mounted read-only, so a move would
  fail: mount the media folders read-write.", "The app's user can't write here or in its media folder,
  so a move would fail: run the app as the media folders' owner.", "The app couldn't check whether a
  move can work here; the move itself will tell."): a hint, as the rename decides (MAM-39 below
  rewords the third, and names in it the folder, its owner and the `PUID` and `PGID` to set). A new
  **Quarantine** field lists each quarantine that holds names, from the replacements' rows and never
  a walk of the disk — "/media/.mam-quarantine: 4.2 GiB in 2 files, from 1 replacement." — or "The
  quarantine is empty.", with a link to **Jobs** (MAM-40 below counts the removals there too). A Save that changes the folders so that a
  quarantine still holding names (`Quarantined`, `Moving`, `Restoring` or `Deleting`) lies under
  none of them is 400 "“/media/.mam-quarantine” still holds replaced copies, so its media folder
  can't be taken out of the list. Restore them or delete them for good first." (MAM-40 below:
  "replaced or removed copies") and saves nothing,
  checked inside the Save's transaction; a folder above the old one keeps it in reach, the quarantine
  itself put in its folder's place doesn't.
  `ReplacementSettingsHttpTests` (the hint for each available folder and none for the others, the
  room, every held state refusing, a parent folder, a quarantine that holds nothing now, a Save that
  keeps the folders), `LocalFileQuarantinePlacesLinuxTests`, `MountTableTests` (a mount's own
  options, a read-only bind of a writable disk) and `MediaFoldersPage.test.tsx` pin it.
- **The folder scan never walks into a quarantine (MAM-26, ADR 0021, ADR 0030).** The walk leaves out
  every entry named `.mam-quarantine`, at any depth, before any other check and without counting it
  as a place it didn't scan, follows no link into one, and gives nothing for an allowed folder that
  lies in one, so a quarantined file is never a file no library reported, a possible match on the
  Duplicates page or part of the Media folders row's count; `FakeFileInventory` does the same.
  `LocalFileInventoryListingTests.List_never_walks_a_folder_named_mam_quarantine_at_any_depth_and_counts_no_problem`,
  `LocalFileInventoryListingTests.List_never_follows_a_link_into_a_quarantine` and
  `LocalFileInventoryListingTests.List_of_a_root_that_lies_in_a_quarantine_gives_nothing_for_it_and_walks_the_others`
  pin it.
- **The owner sees, on the Jobs page and on each job's page, where a replacement stands, the old
  copies he ticked with the new file each pairs with, what the check found on the disk and in
  Jellyfin, and why it stopped, with what to do next (MAM-27, ADR 0032, ADR 0035, Vasyl's decisions
  of 2026-10-08 and 2026-10-11).** The Jobs line now says until what time the check waits for
  Jellyfin, once Jellyfin took the request to look — "Replacement waits for Jellyfin to identify the
  new file until 14:05: 1 copy, kept until it is verified." —, and a review's line ends with what to
  do, by what stopped it: "Replacement needs review: <reason> Fix it in Jellyfin or qBittorrent,
  then press Retry — or see the job." after a stop of the check, "… Keep both, or cancel the job."
  for a download the app can't follow, "… 1 copy can only be dropped, or both kept: see the job."
  while a copy only a drop settles is in it — "… A new check can't pair its last copy: keep both."
  once every copy left is one —, and nothing more after a stop of the move, whose own sentence says
  what to do; on the job's own page the line leaves these out, and ": see the job" after MAM-26's
  files that stayed, as its section below says them. "1 copy dropped." follows once a copy was
  dropped, and every count is of the copies still in the replacement. The Download dialog's sent
  view shares the line. On the owner's row a **Copies** button opens, right under the job, the table
  of its old copies, with **Drop** on each the server lets go, asking again every 2 s while the
  check runs (MAM-41 below moves the line and the table into the details a chevron opens, and drops
  the button); a refusal shows in the page's banner, while one of the row's actions is on its way the
  others wait, and a member's row has no such button. A job's page names its section
  **Replacement**, where it read **Old copies**, with one row of actions — **Retry**, **Keep both**,
  **Restore**, **Delete for good** —; a box of what the owner can do, by what stopped it ("Fix what
  the reason says — for a film Jellyfin gave no id, set its TMDb link on the Library page, and
  Jellyfin's Identify applies it —, then press Retry: the check starts again, and Jellyfin's ids
  decide. Or drop a copy below: it stays where it is. Or keep both.", without "Or drop a copy below"
  when no copy can be dropped, as for a single film), with **Open Alien in Library**, the Library
  page searched by the film's title (`/library?search=Alien`), for a film the check stopped, and
  "Replacement is switched off on the Media folders page: nothing moves while it is off. Switch it
  on there, or keep both." while it is off and Keep both is offered; how far the check got — "Waits
  for the download.", "The check of the new files starts within a few seconds.", "Reading the new
  files on the disk (started 13:05).", "Waiting for Jellyfin to identify the new files until
  14:05.", "Every old copy is paired with a new file Jellyfin identified." or "The check stopped."
  —; the old copies, **Old copy**, **New file** and **Where it stands**, each with its path, size,
  running time, episode and edition, its new file's chips, its status ("Kept until the new file is
  verified.", "Paired with its new file: it moves into the quarantine when the replacement goes
  ahead.", "Not paired.", "Stopped the replacement:" with the problem the check or the move stored — "Still held by the torrent
  “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there without its files, then press
  Retry.", say —, "Dropped from the replacement: it stays where it is.", "In the quarantine." …) —
  while the check still waits, what a pass found shows under the copy, and under its new file, as a
  warning, as it stops nothing yet — and "Only dropping it or keeping both settles this." where only
  that does, the last copy left excepted, under "Nothing moves until every copy is paired or
  dropped: 1 of 2 paired, 1 dropped."; the new files, **New file**, **On the disk** and **Jellyfin**, each with how far it got
  ("Not read on the disk yet.", "Not listed by Jellyfin yet.", "No TMDb or IMDb id from Jellyfin
  yet.", "An extra: simply added.", "Identified by Jellyfin." …), the ids its work is expected to
  carry ("Expected: IMDb tt0078748") and the work Jellyfin gave it, once the check wrote them; and
  MAM-26's table, now under **Files the move planned** and only once a name exists. The page asks
  again every 2 s while the check runs. Every table is cards at phone width. A member reads "Only
  the installation owner sees a replacement's copies and checks, and retries, cancels or drops
  them." and nothing is asked. `GET /api/replacements/{id}` answers the copies, the new files and
  the works behind it, and the server works out each copy's status and each new file's progress
  (`ReplacementJobProgress`), so the page never pairs anything itself; it still writes nothing,
  holds nothing, and reads a fixed number of queries per replacement. None of MAM-27 was tried on a
  real setup — no replacement was kept, retried or dropped there, and MAM-28 is the live check —;
  the Jobs page and the job's page were looked at in a browser at 1280 px and 375 px with answers
  made up in the page — stops of the check and of the move, a dropped copy, a replacement kept both
  and a member's view among them —, nothing sent.
  `ReplacementDetailsHttpTests` (each reason as the page shows it — a held torrent, no id during the
  hour and after it, an unclear pair, replacement switched off —, the pairs, statuses, drops and
  offers, the new files' evidence and ids, the works' expected ids, what a pass found while the
  check still waits never read as a stop, the copies a cancelled or restored replacement never moved
  staying where they are, one query per table for a page of jobs), `ReplacementLineHttpTests` (each
  state's fields), `ReplacementJobProgressTests`, `JobsPage.test.tsx`, `JobsPage.polling.test.tsx`,
  `JobDetailsPage.replacement.test.tsx`, `ReplacementCopies.test.tsx` and
  `replacement-details.test.ts` pin it.
- **The owner keeps both copies — cancels the replacement alone, its download left as it is — at any
  point before an old copy starts to move (MAM-27, ADR 0014, ADR 0033, Vasyl's decisions of
  2026-10-08).** `POST /api/replacements/{id}/cancel`, owner-only like the rest of
  `Controllers/Replacements`, for the caller's own replacement, takes one `Scheduled`, `Submitted`,
  `Downloading`, `Paused`, `Downloaded`, `Inspecting`, `Verified` or `NeedsReview` none of whose
  names is moving, in the quarantine, being put back or deleted, or deleted for good to `Cancelled`
  with "You cancelled the replacement, so nothing will be replaced.", in one transaction that also
  forgets its names still `Planned` or `Stayed` — a name put back or lost stays, and doesn't stand
  in its way — and writes a `ReplacementCancelledAlone` audit row, the owner its actor, its target
  the replacement named by the release. It writes nothing to the download job — no
  `DownloadCancelled` row —, asks and sends qBittorrent nothing and writes nothing to the media, so
  the download goes on, followed as before, and the old copies can be ticked by another download.
  200 with the download job, as `GET /api/downloads/{id}` answers it; 409 "This replacement has
  already ended, so there's nothing to cancel." for `Failed`, `Cancelled`, `Completed` and
  `Restored`, "Part of this replacement has left its folders already, so it can't be cancelled:
  Restore puts back what is in the quarantine." for `Quarantined` or a name taken out of its folder,
  "This replacement is being worked on right now. Try again in a moment." while it is
  `Quarantining`, `Restoring` or `Deleting` or the mover's pass or another click holds it, and "This
  replacement changed while the app was working on it, so it stopped. Reload the page to see where
  it stands." when it changed meanwhile, nothing written; a bare 404 for another user's replacement
  or one that doesn't exist. It works while replacement is switched off. A pass of the check or of
  the move that read the job before loses its conditional write and writes nothing. **Cancel job**
  still ends a replacement whose download is followed, with its own `ReplacementCancelled` row, as
  MAM-24 built it, and whichever of the two comes first wins. The Jobs row shows **Keep both**, the
  owner's, before **Cancel job**, and the job's page beside **Retry**, not red, as nothing is
  deleted; it asks first, "Keep both copies of …?" — "The replacement is cancelled: the old copy
  stays where it is, and nothing of it moves. The download is left as it is, and nothing is sent to
  qBittorrent. This can't be undone." — with **Keep both** and **Keep replacing**; then the job's
  page says "Replacement cancelled: both copies stay.", and the Jobs row's line "Replacement
  stopped: You cancelled the replacement, so nothing will be replaced.".
  `ReplacementCancelHttpTests` (every state before the move, a review holding only names planned,
  stayed, put back or lost, every other state and every name taken out of its folder refused with
  nothing changed, a double click, the download followed on afterwards with the check and the move
  writing nothing, the copies tickable again, a later **Cancel job** writing no second row, a cancel
  landing inside a pass of the check, one being worked on, the follower ending the job between the
  read and the write (409) or moving it on (cancelled, the log naming the state it left),
  replacement switched off, another user's 404), `JobsPage.test.tsx`, `JobsPage.polling.test.tsx`
  and `JobDetailsPage.replacement.test.tsx` pin it.
- **The owner retries a replacement the check of its new files stopped, once he fixed the cause in
  Jellyfin or qBittorrent, and the check starts again from the files (MAM-27, ADR 0035, ADR 0032,
  Vasyl's decisions of 2026-10-08 and 2026-10-11).** `POST /api/replacements/{id}/retry` now also
  takes a `NeedsReview` the check stopped — its download `Downloaded` and checked before the send,
  and no copy in it that only a drop settles — back to `Downloaded`, its reason cleared, with a
  `ReplacementCheckRetried` audit row, the owner its actor; in the same transaction the last check's
  evidence goes — the new files' rows and their ids, and each copy's pair, problem and drop-only
  mark, a dropped copy keeping its last problem —, so the page never shows the old run's findings as
  current. The request writes nothing to the media and asks no one; within 15 s the check's next
  pass claims the job as any `Downloaded` one, with `ReplacementCheckStarted`, reads qBittorrent and
  the files again, asks Jellyfin to look again and gives both hours anew, and Jellyfin's ids decide
  again: a Retry with nothing fixed stops again with the same reason. Asked to look again, Jellyfin
  keeps the ids its Identify set, as its source shows — the request queues an ordinary refresh,
  which replaces no metadata —; MAM-28 sees it live. A replacement the move stopped is retried as
  MAM-26 built it, back to `Verified` with `ReplacementRetried`. 409 "The app can't follow this
  replacement's download, so its new file can't be checked again: keep both copies, or cancel the
  job." for a review of a download that needs linking, or of a check whose download is no longer
  done and checked; "“Yellowstone S02E05.mkv” can only be dropped from this replacement, or both
  copies kept: checking again wouldn't change it." while a copy only a drop settles is in it, the
  first by its place; "This replacement isn't waiting for review, so there's nothing to retry here."
  for every other state, `Failed` included; the being-worked-on and changed-meanwhile sentences —
  nothing written. Only a drop settles a ticked episode Jellyfin now numbers otherwise, a ticked
  episode with no new episode at its number, an old copy or a new file that is half an episode or
  covers several, a new file whose name claims another edition than the release, and one under three
  quarters of the old copy's running time; the check stores that mark beside each copy's problem
  (`OnlyDropSettles`), and two new files of one work, a film with no new file, an old copy without a
  number or listed as a film, and a series Jellyfin hasn't identified stay open to a Retry. The Jobs
  row shows **Retry** for a stop of the check too, titled "Check the new files again: Jellyfin's ids
  decide whether the old copies move", and hides it while a copy only a drop settles is in it; the
  job's page shows it disabled then, with "Drop the copies a new check can't pair first, or keep
  both." — or, once every copy left is one, "A new check can't pair the copies left, and the last
  copy can't be dropped: keep both instead." — also as a line, and after a Retry says "Retried: the
  app checks the new files again and asks Jellyfin to look at them again.".
  `ReplacementRetryHttpTests` (back to `Downloaded` with the evidence forgotten and a dropped copy's
  kept, a download that needs linking, a copy only a drop settles and then 200 once it is dropped, a
  download no longer done and checked, every other state, one being worked on, a stop of the check
  or of the move that ended between the read and the write, the move's Retry unchanged),
  `ReplacementReviewFlowHttpTests` (a film Jellyfin gave no id, fixed in Jellyfin, retried, verified
  and moved, Jellyfin asked twice, the second request later; a Retry with nothing fixed stopping
  again with nothing moved), `ReplacementPairingDropOnlyTests`, `ReplacementJobOffersTests`,
  `JobsPage.test.tsx` and `JobDetailsPage.replacement.test.tsx` pin it.
- **The owner drops an old copy from a replacement that stopped for review: it stays where it is,
  and the others go on once he presses Retry (MAM-27, ADR 0032, Vasyl's decisions of 2026-10-08 and
  2026-10-11).** `POST /api/replacements/{id}/copies/{copyId}/drop`, owner-only, for a copy of the
  caller's own replacement, only from a `NeedsReview` the check or the move stopped, marks the copy
  dropped (`DroppedUtc`) and forgets its names still `Planned` or `Stayed`, with a
  `ReplacementCopyDropped` audit row aimed at the copy itself — a new target kind,
  `ReplacementJobFile`, named by its file name —, the owner its actor; the replacement keeps its
  state and reason, and nothing more happens until the owner presses **Retry**, so several copies
  can be dropped for one check. 200 with the download job, as `GET /api/downloads/{id}` answers it;
  409 "A copy can be dropped only from a replacement that stopped for review.", "The app can't
  follow this replacement's download, so dropping a copy wouldn't change anything: keep both copies,
  or cancel the job.", "“Alien.mkv” is dropped from this replacement already.", "“Alien.mkv” has
  left its folder already, so it can't be dropped: Restore puts it back." for a copy one of whose
  names is moving, in the quarantine, being put back or deleted, or deleted for good, "“Alien.mkv”
  is the last copy this replacement would replace, so it can't be dropped: keep both copies
  instead." and the being-worked-on and changed-meanwhile sentences, nothing written; a bare 404 for
  another user's replacement, one that doesn't exist, and a copy that isn't one of this
  replacement's. A dropped copy can't be taken back in, and it is out of the replacement, not out of
  the library: the check no longer reads, pairs or waits on it, so a new file it would have paired
  with is simply added once Jellyfin has listed, read and identified it — until then it holds the
  replacement, as any new file does, since it may be a second file of a copy left —; checks 5 and 6
  and the move pass it by, so a torrent that holds it stops nothing; another download may tick it;
  and a new file that is that very copy still stops the check. So an unclear pair is settled through
  the library — the owner corrects the new item in Jellyfin, then presses Retry — or by dropping
  that copy, an episode Jellyfin numbers otherwise only by dropping it or keeping both, no click
  confirms a work or an edition by hand, and nothing moves until every ticked copy is paired or
  dropped. On the page **Drop** sits on each copy the server says may go, asks "Drop “Alien.mkv”
  from the replacement?" — "It stays where it is, and nothing of it moves. A new file it was paired
  with is simply added once Jellyfin has identified it. Press Retry afterwards to go on with the
  other copies." — with **Drop it** and **Keep it in**, then, on the job's page, says "Dropped “Alien.mkv”: it stays where it is.";
  the last copy's is disabled with "It is the replacement's last copy: keep both instead.", and a
  dropped copy stays in the table, greyed, with its last problem. `ReplacementDropCopyHttpTests` (a
  stop of the check and one of the move, every state it doesn't apply to, a download that needs
  linking, a copy dropped already, each name taken out of its folder, the last copy, two drops of
  the last two at once, the other copy dropped meanwhile, one being worked on, the follower ending
  the job between the read and the write, a copy of another replacement, a made-up copy, another
  user's 404, the schedule's replay after a drop), `ReplacementDroppedCopyHttpTests` (the check
  never reading, pairing or waiting on a dropped copy, its new file simply added once identified, a
  new file that is one still stopping it, the move never planning or renaming one, a torrent holding
  one stopping nothing, the copy tickable again, the drop-only mark stored and cleared, and what
  passes that waited stored forgotten at a stop before the pairing),
  `ReplacementReviewFlowHttpTests` (a renumbered episode dropped and the other moved after Retry,
  the dropped one where it was; a pack's held copy dropped and the others moved without asking about
  its torrent), `JobsPage.test.tsx`, `JobDetailsPage.replacement.test.tsx` and
  `ReplacementCopies.test.tsx` pin it.
- **The owner's actions on a replacement are refused to a member on every route, found by a test
  that discovers them, and on a real stopped replacement too, the owner's and the member's own
  (MAM-27, ADR 0033).** Keep both and Drop live in `Controllers/Replacements`, so
  `ReplacementsAreOwnerOnlyConvention` answers a member 403 "Only the installation owner can do
  this." before anything is read, and `ReplacementsAreOwnerOnlyHttpTests` now finds eight routes.
  Two new sweeps plant a `NeedsReview` replacement the move stopped, with two copies — one with
  names of its move, none of which left its folder, one paired with its new file —, once as the
  owner's and once as the member's own, where Retry, Keep both and Drop would each write for him;
  each fills every discovered route's parameters by name — `id` the replacement, `copyId` one of its
  copies; a parameter it doesn't know fails the test —, sends each as a member with no body, and
  finds every row as it was, nothing asked of qBittorrent or Jellyfin or read on the disk, and
  nothing renamed, deleted or made. The actions find a job by its sender, so only the sweep over the
  member's own would catch a gate that came after the write; and a member's own quarantined job
  refuses him Restore and Delete for good too. Another user's job with a stopped replacement is a
  bare 404, even for the owner. `ReplacementsAreOwnerOnlyHttpTests`,
  `ControllerAccessDeclarationTests` (both new actions), `ReplacementRestoreHttpTests` (a member's
  own quarantined job) and `DownloadsGetHttpTests` pin it.
- **Phase C's acceptance bar checked on the reference installation, and the README and the release
  note say what was tried live and what wasn't (MAM-28, Vasyl's decisions of 2026-10-12).** Checked
  on 2026-10-12 on production, the published 0.3.0 image (Docker Hub's `0.3.0`, `0.3` and `latest`,
  digest `sha256:0b75fdbd7f6029b6ea84b829c344e30e16106ea14a4dfdfbdb4ddd16b0d163be`, stamped
  `0.3.0+2dec720`, published on 2026-10-11), against Jellyfin 12.2.0 and qBittorrent 5.2.4, with
  ffprobe 6.1.1-3ubuntu5, on Docker 29.9.0 and Compose v5.6.0 under Linux 7.0.14-22-pve x86_64,
  `/media` a read-write bind of one ext4 disk; Vasyl signed in as the owner. Replacement was turned
  on as **Turning it on** says, from 09:38 to 09:42 UTC: the mount was read-write already; the
  host's compose file, which had no `user:` line, got one, and a new `.env` `APP_USER_ID=1000` and
  `APP_GROUP_ID=100`; the data volume was backed up and handed over with the note's `chown`, keeping
  `/data` at 700 and the secrets at 600, and the app came up healthy in 7.4 s as 1000:100. The Media
  folders page, which with 10001 had said "The app's user can't write here or in its media folder,
  so a move would fail: run the app as the media folders' owner.", said "A move into the quarantine
  can work here: the same mount as the quarantine, mounted read-write, and writable by the app." for
  `/media/movies` and `/media/tv-shows`, and Save wrote `ReplacementSwitchedOn` and
  `ReplacementFoldersChanged`. The film «Останній бойскаут» (1991), its 576p H.264 copy
  (2,241,669,589 B, inode 390463793) held by no torrent, was scheduled from Toloka's 1080p H.265
  release with its work confirmed (`DownloadRequested`, `ReplacementScheduled`,
  `ReplacementWorkConfirmed`), downloaded in about 3 minutes, its new file (7,245,507,785 B,
  1920x800 HEVC, 1:45:26) verified 18 s after the app asked Jellyfin to look (IMDb tt0102266, TMDb
  9319), and its old copy moved into
  `/media/.mam-quarantine/e989e08c-bd05-41d3-bfd9-aad52043f034/movies/` keeping its inode
  (`QuarantineStarted`, `NameMoving`, `NameMoved`, `Quarantined`, in 0.6 s); Jellyfin's search then
  listed only the new item. Restore put it back the same way (`RestoreStarted`, `NameRestoring`,
  `NameRestored`, `Restored`), and Jellyfin listed both items again, the old one under its old id;
  the copy put back read "Couldn't be read / Not inspected yet" in the Download dialog until Refresh
  all inspected it again. Clarkson's Farm season 2, eight 1080p copies ticked for a 2160p release,
  was verified with each copy paired by the series' ids, then stopped with "Still held by the
  torrent “Clarksons Farm Collection” in “qBittorrent”: remove that torrent there without its files,
  then press Retry."; that torrent seeds seasons 1 to 5, so Vasyl pressed Keep both. Staged, each
  leaving the old file where it was: three restarts at rest points — downloading, checking with
  Jellyfin down ("(started 12:56 PM)" kept, the hour not started again) and quarantined —;
  Jellyfin's container stopped for 2 min 55 s during the check ("… (jellyfin) failed: Unreachable."
  on each pass, the library never asked); the switch off before the move ("New file verified, but
  replacement is switched off: nothing will move while it is off.") and with the copy quarantined
  (Restore and Delete for good disabled); `/media/movies` unticked ("Replacement needs review:
  “/media/movies/The Last Boy Scout (1991) BDRip-AVC.mkv” is no longer in a folder ticked for
  replacement, so it stays where it is."), then ticked again and Retry, which moved it; a temporary
  member's Jobs page, job address and Download dialog; and a release whose one file would land on
  the restored copy, refused before anything reached qBittorrent ("“qBittorrent” would write over
  “The Last Boy Scout (1991) BDRip-AVC.mkv” in the folder of the category “Movies”: …"). A second
  replacement of the film, both copies ticked and the HEVC one marked as held by its own torrent,
  was cancelled with Cancel job at 1.8 % of its download ("Replacement stopped: You cancelled the
  download, so nothing will be replaced."; the job's page kept the replacement section's "Keep both"
  and "Waits for the download." until reloaded), so no copy was left in the quarantine for Delete
  for good; Vasyl then deleted the 576p copy by hand on the host, leaving the film one copy, the
  1080p H.265. At the end replacement was switched off, both folders unticked and the quarantine
  empty, and production went back to the image's own ids — the note's `chown` with `10001:10001`,
  the compose file from before without the `user:` line, no `.env` —, healthy in 7.4 s as uid 10001.
  `tools/mam28/report.py`, run before anything changed, after each stage and at the end, found of
  1,619 entries only the 576p file gone, the 1080p H.265 file added and the modification times of
  `/media`, `/media/movies` and `/media/tv-shows` changed; the replacement jobs end as 1 `Restored`,
  2 `Cancelled` and 1 `Failed` (the refused overwrite). Not tried live yet: Delete for good, a held
  copy moving once its torrent is removed and Retry pressed, a restart in the middle of a move, a
  restore or a deletion, a failed download, an unknown new path, an unclear pair, a multi-episode
  file or a half, a failed ffprobe, a wrong id, Jellyfin out of reach for the whole hour, a changed
  old copy, another or a read-only mount, a client whose mappings changed after a send (the fix
  above, by the tests alone and not in the image published on 2026-10-11), the check's Retry after a
  fix in Jellyfin, a Drop done, a copy with another name, going back to 0.2.1, a qBittorrent older
  than 5.2 and arm64. The README (**Tested with**) and the release note list both; the README's and
  the roadmap's MAM-24 to MAM-27 lines no longer say MAM-28 does the live check, that nothing was
  tried on a real setup or that they are in no published image, and MAM-37 and MAM-38 are recorded
  as first published in 0.3.0. The `user:` line, `APP_USER_ID`, `APP_GROUP_ID` and the `chown` that
  production was turned on and off with are 0.3.0 as first published: MAM-39 below replaced them,
  in the same release, with `PUID` and `PGID`.
- **The app runs as the media folders' owner through `PUID` and `PGID`, and the image hands its own
  data over to them (MAM-39, ADR 0036, ADR 0033, Vasyl's decision of 2026-10-12).** The image's new
  entrypoint, `docker-entrypoint.sh`, reads `PUID` and `PGID`, as the linuxserver.io and hotio
  images of qBittorrent, Jellyfin and Sonarr do; each is 10001 when unset, so an installation that
  sets neither runs as before. Started as root, it hands the data directory
  (`${MAM_Storage__DataDirectory:-/data}`) over to them: only what another id owns changes owner, by
  `chown -h`, never anything mounted inside it — every folder is asked `mountpoint` and left out
  with all it holds when it is a mount, as `find -xdev` alone changed the owner of a media folder
  mounted inside `/data` from the same disk —, and no mode changes, so `/data` stays 0700 and the
  secrets 0600. Then `exec setpriv --reuid --regid --inh-caps=-all --bounding-set=-all
  --no-new-privs` starts the app, PID 1, as those ids, with no capability and no way to gain one
  back, and `HOME=/nonexistent`; the supplementary groups Docker gave the container — compose's
  `group_add`, `docker run --group-add` — stay the app's through `--groups`, as they were under the
  image's own user, never root's group 0, and with none it gets `--clear-groups`. Root without
  Docker's default capabilities — `CHOWN`, `DAC_OVERRIDE`, `SETGID`, `SETUID` and `SETPCAP`, which
  `cap_drop: ALL` removes — is refused up front, from `CapEff`, before anything changes hands. It
  writes to standard error, each line starting `[entrypoint]`: "[entrypoint] Handed /data over to
  4242:4343: 6 files and folders changed owner; permissions are unchanged." ("1 file or folder" for
  one, and nothing
  when nothing changes hands); "[entrypoint] PUID is 0: the app never runs as root. Set PUID and
  PGID to the owner of the media folders, or remove them to run as the image's own 10001." (and
  `PGID is 0:` alike); "[entrypoint] PGID must be a number from 1 to 4294967294, such as 100, not
  "abc"." (`PUID`'s says "such as 1000"), which refuses 4294967295 too, as `setresuid` reads it as
  "keep the current id", root's; "[entrypoint] The container runs as root without the capabilities
  it takes to switch to 10001:10001 (CHOWN, DAC_OVERRIDE, SETGID, SETUID and SETPCAP; cap_drop: ALL
  removes them), so the app was not started. Keep Docker's default capabilities, or run the
  container as a user instead: user: "10001:10001" in docker-compose.yml, with the data volume
  already theirs." (with the ids it would have switched to); and "[entrypoint] Could not hand /data
  over to 4242:4343; the app was not started." — each exiting non-zero with no app started, the
  refusals before anything changes hands. Started as another user, by compose's `user:` or
  `docker run --user`, it starts the app as that user, and with `PUID` or `PGID` set says "[entrypoint] PUID and PGID are ignored: the
  container already runs as 1000:100, set by user: in docker-compose.yml or by docker run --user.
  Remove that to use PUID and PGID.". Taking them out hands the data back to 10001 at the next
  start. The release note's **Turning it on** now sets `PUID` and `PGID` in step 2 and restarts the
  app the way it is updated, asking for no `user:` line, backup or `chown`, and says what the log
  says; its **Going back** takes them out again, and goes back to 0.2.1 only after one start of
  0.3.0 without them. `tools/check-image-user.sh <image>`, new, checks a built image in eight
  scenarios, each PASS or FAIL — a default start; `PUID=4242 PGID=4343` with `--group-add 4444`
  handing the data over with every mode kept and the app left no capability and only group 4444;
  handing it back, no group left; `--user`; `PUID=0`, `PGID=0`, `PGID=abc`, `PUID=4294967295` and
  root without capabilities (`--cap-drop ALL`) refused; a start and a `docker restart` with nothing
  to hand over; a media folder mounted inside `/data` that keeps its owner; `docker stop` within
  5 s —, and passed all eight on a fresh local build on 2026-10-12; CI doesn't run it, and
  PUBLISHING.md asks for it before a release. Not tried on production yet.
- **The Media folders page names the folder the app can't write to, whose it is and which `PUID` and
  `PGID` to set (MAM-39, ADR 0033).** Where it finds `NotWritable`, `DescribeRenamePlacesAsync` now
  also reads the refused folder's owner and group by `statx(STATX_UID | STATX_GID)` on the
  descriptor it already holds, never the path again, and the app's effective ids (`geteuid`,
  `getegid`) — the ticked folder when its own permissions refuse, also when both do, else its
  allowed folder's `.mam-quarantine`, or the allowed folder while it has none —, and
  `GET /api/settings/allowed-roots` gives each library folder a `writeAccess` with them, `null` when
  they couldn't be read. A folder ticked in the form then says, when another user owns it, "The app
  runs as user 10001, group 10001, and can't write to /media/movies, which belongs to user 1000,
  group 100, so a move would fail: set PUID=1000 and PGID=100 for the app in docker-compose.yml or
  .env, and restart it."; when root owns it, "The app can't write to /media/.mam-quarantine, which
  belongs to root, so a move would fail here: the app never runs as root, so it can move copies
  only in folders that belong to the user it runs as (now user 1000, group 100)."; when the app
  already runs as its owner, "The app already runs as the owner of /media/movies, user 1000, but the
  folder's permissions don't let its owner write there, so a move would fail: give the owner write
  permission on it."; and without the ids, or when its owner or group is 65534, the overflow id of
  user-namespace remapping and NFS root squashing, "The app's user can't write here or in its media
  folder, so a move would fail: set PUID and PGID for the app to the user and group that own your
  media folders, and restart it.", where it said "… run the app as the media folders' owner.". The
  ids are written as plain digits, never grouped the locale's way, and the path as it is. The
  switch's hint now ends "Moving needs the media folders mounted read-write and the app running as
  their owner, set with PUID and PGID.". `LocalFileQuarantinePlacesLinuxTests` (a folder the app may
  not write to, the quarantine under a writable allowed folder, the allowed folder with no
  quarantine yet, the ticked folder when both refuse, and `/var/mail` and `/var/local`, 0:8 and 0:50
  in the .NET SDK container, whose owner and group differ, so ids read the wrong way round turn six
  tests red; each compared with coreutils' `stat` and `/proc/self/status` through `LinuxProbes`),
  `FakeFileQuarantineTests`, `ReplacementSettingsHttpTests` and `MediaFoldersPage.test.tsx` pin it.
  Looked at in a browser on 2026-10-12, a local image over a copy of a development database, at
  desktop width and at 375 px with no sideways scroll: before `PUID` and `PGID`, folders owned by
  1000:100 gave the other-owner sentence with "set PUID=1000 and PGID=100" and a folder root owns
  the root sentence; with `PUID=1000` and `PGID=100`, the log said "[entrypoint] Handed /data over
  to 1000:100: 7 files and folders changed owner; permissions are unchanged.", the folders owned by
  1000:100 said "A move into the quarantine can work here: …", a folder of 1000 with mode 555 gave
  the already-owner sentence, and the root folder the root sentence with "(now user 1000, group
  100)".
- **The owner removes a copy of a duplicate into the quarantine on the Duplicates page, and restores
  it or deletes it for good under Removed copies (MAM-40, ADR 0037, ADR 0030, ADR 0031, ADR 0033,
  Vasyl's decisions of 2026-10-12).** On 2026-10-12, before production's four groups of one work's
  copies, Vasyl asked how he was to settle them there: the page only showed, and Phase C could take
  a copy out only by downloading a release to replace it, when the better copy was already in the
  library. Each file of an "Exact copies" or "Same film, different files" group now has **Remove
  this copy**, in its open row and in the table's new **Removal** row, a red outline, the owner's;
  the app never suggests which copy to remove. `GET /api/library/duplicates` gives each such file
  `removal`, `{ allowed, reason }`, from what the app has stored (ADR 0037's rules, the switch, the
  ticks and the holds read once per page, for every signed-in user, no download client asked), and
  `null` on the other classes; a file that can't be removed now shows the button disabled with the
  sentence, also as a line under it — "Removing copies is switched off. Turn on “Replace copies
  after verification” under Media folders.", "This file isn't inside the Media folders, so the app
  can't move it.", "This file's library folder isn't ticked for replacement under Media folders.",
  "The app hasn't inspected this file yet. Press Inspect files on the Library page first.", "The
  app's last look couldn't read this file as media, so it can't tell what would go.", "This file is
  part of a disc folder, which can't be removed one file at a time.", "This file also holds a film
  or an episode outside this group, which would go with it.", "A replacement on the Jobs page holds
  this file.", "A removal under Removed copies holds this file." or "No other copy in this group is
  one the app has read as media, so this one stays: it may be the only one that plays." —, and a
  member sees it disabled with "Only the owner keeps or removes copies.". It asks first — "Remove
  “Alien (1979) 2160p.mkv”?", "This file moves into the quarantine folder at the top of its media
  folder, with its subtitles and other files of the same name:", the file with its size, "You can
  restore it or delete it for good under Removed copies on this page.", and "This file stays:" or
  "These files stay:" with each one's size, **Remove this copy** first and **Cancel** last —, then
  sends `POST /api/replacements/removals` with `{ groupKey, fileId, deviceId, inode, sizeBytes }`,
  the identity the page showed. The server works the group out again — a bare 404 for a key no group
  has or a file not in it, which the card reads as "This group has changed since the page loaded:
  the list shows it again as it is now." —, checks the same rules, then, one removal at a time in
  the process, the platform ("The app can't rename files on the platform it runs on, so nothing was
  removed."), the identity ("This copy changed since the page showed it. Reload the page to see it
  as it is now."), the file on the disk as a replaced copy is checked right before its move ("“…” is
  gone, so there is nothing to remove.", "“…” changed since the app last read it, so it stays where
  it is. Inspect the library's files again, then press Remove again."), another copy of the group —
  another file, never a second name of this one — still on the disk as read ("No other copy of this
  group is on the disk as the app last read it, so this one stays. Inspect the library's files again
  to see what is there."), its companions, and every download client, as check 6 reads them ("Still
  held by the torrent “…” in “qBittorrent”: remove that torrent there without its files, then press
  Remove again.", "The app couldn't tell whether a torrent in “qBittorrent” holds this copy: …"):
  each refusal is 409 with its sentence, inline on the card, nothing written. Then it claims the
  removal — a `CopyRemovals` row `Moving`, its names planned in `CopyRemovalNames`,
  `CopyRemovalStarted` — and renames the video, then its own subtitles and `.nfo`, into `<media
  folder>/.mam-quarantine/<removal>/<path inside it>`, one `renameat2(RENAME_NOREPLACE)` each, the
  switch and the ticks read again before each, with rows before and after (`CopyRemovalNameMoving`,
  then `CopyRemovalNameMoved` or `CopyRemovalNameNotMoved`), and tells Jellyfin the old paths went:
  200 with the removal, `Quarantined` (`CopyRemoved`). A video that can't move ends it `NotMoved`
  (`CopyRemovalNotMoved`) with nothing in the quarantine, and the answer is 409 "“…” couldn't be
  moved into the quarantine: …", `NotMoved` meaning only that; a rename that moved another file put
  at the video's place records the video `Lost` and leaves the removal `NeedsReview`
  (`CopyRemovalNeedsReview`), listed under Removed copies, the request 409 with a reason that says
  where to look ("… A different file now sits in the quarantine at “…”, as another file took the
  place of “…” just before the rename, and this copy's own file wasn't found. Look there to see
  which file it is."); a companion a torrent holds or that can't move stays where it is, named on
  its row; the switch off or the folder unticked between two names leaves it `NeedsReview` with how
  many names are in the quarantine. On the page a short row says "Removed “Alien (1979) 2160p.mkv”:
  it waits in the quarantine, under Removed copies, until you restore it or delete it for good."
  with a **Removed copies** button, and the list is asked again: a group of two leaves it, a larger
  one stays without the file. The new **Removed copies** tab, the owner's alone (`?tab=Removed`,
  read as All for a member), lists the removals still open, newest first, paged on the server (`GET
  /api/replacements/removals?page=&pageSize=`, with `switchedOff`), under "The quarantine holds 2
  GiB of removed copies.": each headed by its group's title as it was, the file's name, where it
  was, where it is in the quarantine, its size, when and by whom it was removed, where it is now
  ("In the quarantine.", "Needs your review." with the reason, …) and every file that went with it,
  as a replacement's names are shown. **Restore** asks "Put “…” back?", listing what goes back, then
  `POST /api/replacements/removals/{id}/restore` checks every file first, as a replacement's Restore
  does, and puts each back, the video first, ending `Restored` (`CopyRemovalRestoreStarted`,
  `CopyRemovalNameRestoring`, `CopyRemovalNameRestored` or `CopyRemovalNameNotRestored`,
  `CopyRemovalRestored`), Jellyfin told the files came back, so the copy counts in its group again;
  "This copy's video was deleted for good, so nothing is put back without it. Delete for good what
  the quarantine still holds." once a Delete for good stopped after the video. **Delete for good**,
  red, asks once more with every path, its size, "Frees …" and how many files have another name,
  then `POST /api/replacements/removals/{id}/delete-for-good`, with no body — the whole removal —,
  deletes each file from the quarantine alone, its identity compared right before, ending `Deleted`
  (`CopyRemovalDeleteStarted`, `CopyRemovalNameDeleting`, `CopyRemovalNameDeleted` or
  `CopyRemovalNameNotDeleted`, `CopyRemovalDeletedForGood`). Both are offered from `Quarantined` and
  from `NeedsReview` while a file is in the quarantine, refused while replacement is switched off
  ("Replacing and removing copies is switched off on the Media folders page, so nothing can be put
  back. Turn on “Replace copies after verification”, then press Restore again." and its Delete for
  good twin) — the list answers `switchedOff`, and the tab then disables both with the reason and
  says above the list "Replacing and removing copies is switched off: nothing is put back or deleted
  while it is off. Switch it on in Media folders.", linking to the page —, and stop for review
  part-way only while a file is still in the quarantine; what they did shows above the list, a
  refusal on the card — or above the list once the card has left it —, the banner, or the empty
  list, taking the focus. `Restored`, `Deleted` and `NotMoved` end a removal, kept with its audit
  rows and listed nowhere. The quarantine's pass every 15 s settles a removal a restart cut in
  `Moving`, `Restoring` or `Deleting` — each file in the middle looked up by device and inode, as a
  replacement's; a video a move left nowhere `Lost`, its removal `NeedsReview` with "The app stopped
  while moving this copy, and found its video neither at “…” nor in the quarantine at “…”. Look in
  both places to see where it went." —, and sends the notices it owes. A removal whose video was
  lost has nothing in the quarantine to restore or delete: it stays listed, holding its copy's path,
  until the owner settles it outside the app. A Restore or Delete for good whose video is lost on
  the way ends `Restored` or `Deleted` with that reason once nothing of it is left in the
  quarantine. The removals are the installation's: the owner acts on every one, the list and all
  three writes owner-only by where `CopyRemovalsController` lives. `AuditAction` gains 58 to 74, and
  `AuditTargetKind` `CopyRemoval` (10) and `CopyRemovalName` (11). `CopyRemovalHttpTests`,
  `CopyRemovalChecksHttpTests`, `CopyRemovalVerdictsHttpTests`, `CopyRemovalActionsHttpTests`,
  `CopyRemovalListHttpTests`, `CopyRemovalRecoveryHttpTests`, `ReplacementsAreOwnerOnlyHttpTests`,
  `ControllerAccessDeclarationTests`, `CopyRemovalPersistenceTests`,
  `AddCopyRemovalsMigrationTests`, `DuplicateGroupCard.remove.test.tsx`,
  `DuplicatesPage.removed.test.tsx` and `copy-removals-api.test.ts` pin it. Not tried on the
  reference installation; the page was checked in a browser at desktop and phone width with the
  API's answers stood in for.
- **The owner keeps a group of copies as they are (MAM-40, ADR 0029, Vasyl's decisions of
  2026-10-12).** A group of exact copies or of one work's copies has **Keep both** — **Keep all**
  past two files —, the owner's, with no confirmation: `PUT /api/library/duplicates/{key}/decision`
  with `Dismissed`, as a dismissed possible match, which a same-work group couldn't take before. The
  row now keeps `KeptMembers`, the group's members by path, as its key doesn't change when a copy
  joins: the group leaves every tab for **Kept as they are**, the Dismissed tab renamed, with
  "Kept as they are by owner on …. A new copy joining the group brings it back to the list." and
  **Undo**, and comes back, undecided, once a new file joins it; a file leaving keeps it kept. A
  short row says "Kept the copies of “Alien (1979)” as they are: the group is listed under Kept as
  they are now." with **Undo**, and an Undo on Kept as they are "“Alien (1979)” is back among the
  duplicates." with **Keep again**. Audit rows `DuplicateCopiesKept` (56) and
  `DuplicateCopiesKeptUndone` (57), named by the group's first work, "Alien (1979)", an episode's
  "Clarkson's Farm S02E03". Keep both removes and moves nothing. `LibraryDuplicateKeepBothHttpTests`,
  `DuplicateMatchDecisionPersistenceTests`, `DuplicateGroupCard.decisions.test.tsx` and
  `DuplicatesPage.decisions.test.tsx` pin it.
- **A name in the quarantine counts in no duplicate group (MAM-40, ADR 0028).** The list leaves out
  every name a replacement or a removal took into the quarantine and hasn't put back — moving, in
  it, on its way back or out, or deleted for good —, matched by its old path and the device, inode,
  size and modification time it moved with, all five against the app's last read of the file, so a
  file put at that path since counts, and so does one a later download got the inode of a name
  deleted for good; a replacement's old copy no longer waits for the next sync to leave its group.
  `LibraryDuplicatesQuarantineHttpTests` pins it.
- **The Jobs page sorts the jobs by what waits for you, in tabs the server works out and counts
  (MAM-41, ADR 0014, Vasyl's decisions of 2026-10-12).** The status filter gives way to underline
  tabs — **Needs you**, **In progress**, **Finished** and **All** —, the first two saying how many
  jobs they hold ("Needs you (2)"). A job needs you while it needs linking, while its replacement
  stopped for review, while its replacement's old copies wait in the quarantine for Restore or Delete
  for good, and while its replacement is verified with replacement switched off, as nothing moves
  until the owner switches it on. It is in progress while the app still follows its download —
  `Created`, `Submitted`, `Downloading` or `Paused` — or still checks or moves its replacement —
  `Downloaded`, `Inspecting`, `Quarantining`, `Restoring`, `Deleting`, or `Verified` with replacement
  switched on. Every other job is finished, and All lists every job. A job is in the first of the
  three that fits it, so in exactly one. The tab is in the address (`/jobs?tab=NeedsYou`, nothing for
  All), so a reload and Back from a job's page keep it; another tab starts at page 1, and an empty
  tab says so — "Nothing waits for you: every job moves on by itself or has ended.", "Nothing is in
  progress." or "No job has finished yet." — with **Show all jobs**. The server sorts the jobs into
  the tabs and counts them, never the page: `GET /api/downloads` takes `view`, `NeedsYou`,
  `InProgress` or `Finished` by name in any letter case — left out or blank, every job; anything
  else, 400 "“x” is not a view of the jobs. Use NeedsYou, InProgress or Finished." —, alone or with
  `status`, which stays though the page no longer sends it, and its answer gains `needsYouCount` and
  `inProgressCount`, the caller's own jobs in each of the two whatever view, status and page were
  asked. The database applies the view, and the switch is read once per request, as it is then, so
  switching replacement on moves a verified job from Needs you to In progress. A member sees the
  same tabs; a member's jobs carry no replacement, so a member's Needs you holds only the jobs that
  need linking. `DownloadsListViewsHttpTests`, `DownloadJobListRulesTests`, `JobsPage.test.tsx` and
  `JobsPage.polling.test.tsx` pin it. Not tried on the reference installation; the page was checked in
  a browser at desktop and phone width with the API's answers stood in for.
- **The owner restores or deletes for good a replacement's old copies from its row on the Jobs page
  (MAM-41, ADR 0030, ADR 0033, Vasyl's decisions of 2026-10-12).** **Restore** and **Delete for
  good**, on the job's page alone until now, also sit in the row's actions while the replacement
  offers them, with the job page's confirmations: Restore asks "Put the old copy back?", and Delete
  for good lists every file it deletes with its size and the room it frees, read from
  `GET /api/replacements/{id}` when it is pressed, then sends exactly the names it listed, so a
  quarantine that changed meanwhile is refused as on the job's page ("What this replacement holds in
  the quarantine changed since you opened it. Open Delete for good again."). While replacement is
  switched off both are disabled with "Replacement is switched off on the Media folders page: nothing
  is put back or deleted while it is off.". A job's `replacement` gains `canRestore` and
  `canDeleteForGood`, by the rules `GET /api/replacements/{id}` answers them with
  (`ReplacementJobOffers`), so the row offers what the actions take; a member's row has neither.
  `ReplacementLineHttpTests` and `JobsPage.actions.test.tsx` pin it.
- **The files in no library are listed, and the owner ignores the ones that aren't media (MAM-42,
  ADR 0021, Vasyl's decisions of 2026-10-13).** On 2026-10-13 the Sources card warned "17 in no
  library" and "1 couldn't be read" with nothing to show which files they were or how to settle them.
  A **Files in no library** dialog now lists them, opened from the Folders item's chips on the closed
  bar and from **Show files** on the Media folders row: "Files the scan found in the media folders
  that no library lists. Ignore a file that isn't media, such as an installer's ISO: the app stops
  counting it, and nothing changes on the disk." Underline tabs "In no library (17)", "Couldn't be
  read (1)" and "Ignored (0)", counted by the server, each paged there, 25 to a page, in path order.
  Each file shows its name over its folder, its size as the app's last look read it ("Size unknown"
  until read), what that look found — ffprobe's reading as on the Library page, why it couldn't read
  it (the sentence the owner's, a status sentence a member's) or "Not inspected yet" —, "Ignored Oct
  13, 2026, 10:26 AM." on the Ignored tab, and why a library most likely doesn't list it: "No
  library's folders hold it, so no library looks for it there.", "In Фільми's folders, but Фільми
  didn't list it: a library skips samples, trailers and extras by their names, and lists a file added
  after its last scan only after the next one." (MAM-43 below shortens it and adds two reasons for
  extras) or, while no library folder the last syncs stored
  maps into the app, "The app doesn't know the libraries' folders yet. Sync a library first." Every
  signed-in user opens it, as the Library page already shows paths;
  `GET /api/library/unreported-files?view=&page=1&pageSize=25` answers it, `view` `Problems` or
  `Ignored` by name in any letter case and blank for every file the app counts, anything else 400
  "“x” is not a view of the files in no library. Use Problems or Ignored, or leave it out for every
  file.". **Ignore** and **Stop ignoring** are the installation owner's (`PUT` and `DELETE` on
  `/api/library/unreported-files/{id}/ignored`, 204 also when already so, a bare 404 for an id no
  name the scan stored has, a library's file included; a member sees them disabled with "Only the
  owner can ignore files."). Ignore sets `PhysicalFiles.IgnoredUtc` on the scan's name — the app's
  own note: nothing changes on the disk or in a library (specification §1). An ignored file leaves
  every count of files in no library and of files that couldn't be read at once, but still counts in
  the room the files take, as it still takes that room, and the inspection still reads it. It stays
  ignored while a scan keeps its row; moved or renamed, it is a new name and counts again. Each change
  writes an audit row, `UnreportedFileIgnored` (75) or `UnreportedFileIgnoreUndone` (76), target kind
  `UnreportedFile` (12), named by the file's own name; a repeat records nothing. A refused action shows
  on its file's row, a bare 404 as "This file is no longer in no library: a scan or a sync changed it
  meanwhile.". `UnreportedFilesHttpTests`, `UnreportedFileListRulesTests`,
  `LibraryWritesAreOwnerOnlyHttpTests` and `UnreportedFilesDialog.test.tsx` pin it. Checked in a
  browser on 2026-10-13 on a scratch installation with real ffprobe, an ISO and two short videos —
  the chips, the rows, the dialog, Ignore, a rescan keeping the mark, Stop ignoring and a member's
  view, at desktop and phone width —; not tried on the reference installation.
- **A Blu-ray or DVD folder's clips are its film's files, and Files in no library says when an
  extra's name made a library skip a file (MAM-43, ADR 0038, ADR 0021's MAM-43 section, Vasyl's order
  of 2026-10-13).** On 2026-10-13, 16 of the 17 files in no library were four
  `BDMV/STREAM/00000.m2ts` clips — Dirty Dozen and the three Matrix films — and the twelve Toy Story
  and Alien shorts in folders named `Shorts`. All of them carried the same reason, "a library skips
  samples, trailers and extras by their names, and lists a file added after its last scan only after
  the next one". For the clips that was false: Фільми lists those films. Jellyfin reports a disc by its
  folder (the one holding `BDMV`, or `VIDEO_TS`), so the adapter gave the version no file, and the
  scan found the clip with no library name to match it. *Discs:* the adapter now keeps a local disc's
  folder (`VideoType` `BluRay` or `Dvd`). The sync maps it into the app through the integration's path
  mappings and walks it inside the media folders (`IFileInventory.ListMediaFilesUnderAsync`, checked as
  a file is for a look: inside an allowed root by its text and once every link is followed). It stores
  each media file in the disc's own `BDMV` or `VIDEO_TS` folder as the version's file, named in
  Jellyfin's terms, with no size as reported. So the Library page lists the clip under its film with
  its library, the app's reading and its size as read. The Jellyfin row counts its room, and the scan
  no longer lists it in no library. A sample beside the disc's folders stays in no library. A disc
  folder that doesn't map, lies outside the media folders, is a link leading out or is away gives the
  version no file, as before, and the sync succeeds; with no media folder allowed nothing is walked.
  *Extras:* a file in a library's folders now says why the library skipped it when its name is an
  extra's by Jellyfin's own rules (`NamingOptions.VideoExtraRules`, `LibraryExtraNames`), checked
  in this order (MAM-44 below rewords both sentences and moves these files to a tab of their own):
  - its own folder is named `Shorts`, `Trailers`, `Extras`, `Featurettes`, `Behind The Scenes`,
    `Deleted Scenes`, `Interviews`, `Scenes`, `Samples`, `Sample`, `Clips`, `Other`, `Extra` or
    `Backdrops` (letter case ignored, never the library's root): "In a folder named “Shorts” in
    Мульфільми's folders: Мульфільми takes the videos in a folder so named for a film's extras, not
    for films of their own.";
  - its name, trailing digits aside, is `trailer` or `sample`, or ends with `-trailer`, `.trailer`,
    `_trailer`, `- trailer`, the same four for `sample`, `-scene`, `-clip`, `-interview`,
    `-behindthescenes`, `-deleted`, `-deletedscene`, `-featurette`, `-short`, `-extra` or `-other`:
    "Named as an extra (“-trailer”) in Фільми's folders: Фільми takes a video so named for a film's
    extra, not for a film of its own."

  Any other file in a library's folders now reads "In Фільми's folders, but Фільми didn't list it: a
  library lists a file added after its last scan only after the next one." In
  `GET /api/library/unreported-files`, `place.kind` gains `ExtrasFolder` and `ExtraName`, and `place`
  gains `extraName` (the folder's name as on the disk, or the rule the name matches); `libraryName`
  is now given for those two kinds too. `LibraryDiscClipsHttpTests`,
  `LocalFileInventoryFolderListingTests`, `JellyfinAdapterItemsTests`, `LibraryExtraNamesTests`,
  `DiscFoldersTests`, `UnreportedFilesHttpTests` and `UnreportedFilesDialog.test.tsx` pin it. Not
  tried on the reference installation.
- **The files a library takes for extras have an Extras tab of their own and warn of nothing (MAM-44,
  ADR 0021's MAM-44 section, Vasyl's decisions of 2026-10-13).** After MAM-43, production's Files in
  no library still warned of the twelve Toy Story and Alien shorts in `Shorts` folders. A read of
  Jellyfin the same day found them nowhere: a search for "Hawaiian", "Containment" or "Lamp Life"
  gave no item, and no Toy Story or Alien film had a special feature. Jellyfin takes a `Shorts`
  folder for the extras of the one film in the folder above it, and a folder of several films has
  none. Every file a library takes for a film's extra now leaves the counted files and every warning:
  "In no library", "Couldn't be read" (even when ffprobe couldn't read it), the bar's chips and the
  Media folders row's "N files are in no library.". That covers its folder (`ExtrasFolder`) and its
  name (`ExtraName`). The dialog lists these files on a fourth tab, **Extras (12)**, between
  **Couldn't be read** and **Ignored**, under "Files a library takes for a film's extras, by their
  folder or their name. They warn of nothing, and still count in the room your files take.", with
  "No file is taken for a film's extra." when it is empty. The Media folders row's counts line adds
  them, "Files in no library: 4 inspected · 12 extras · 1 ignored"; with nothing else counted,
  **Show files** there opens **Extras**. They still count in the room the files take, and **Ignore**
  still takes one to **Ignored**. The two reasons now say what Jellyfin does:
  - "In a folder named “Shorts” in Мульфільми's folders: Мульфільми takes such a folder for the
    extras of the one film in the folder above it, and shows them nowhere when that folder holds no
    single film.";
  - "Named as an extra (“-trailer”) in Фільми's folders: Фільми takes such a video for an extra of
    the one film in its folder, and shows it nowhere when that folder holds no single film."

  `GET /api/library/unreported-files` takes `view=Extras`, and its answer gains `extrasCount` after
  `ignoredCount`; `scan` in `GET /api/library/sources` gains `extrasCount` after `ignoredCount`. The
  list and the counts read one set (`UnreportedFileSet`), so they can't disagree.
  `UnreportedFilesHttpTests`, `LibrarySourcesScanHttpTests`, `UnreportedFileListRulesTests`,
  `UnreportedFilesDialog.test.tsx` and `LibrarySyncPanel.scan.test.tsx` pin it. Not tried on the
  reference installation.

### Changed

- **The Download dialog's summary of existing copies (MAM-24).** "Kept: this version of the app
  never replaces or removes a file, and doesn't send a release it finds would write over a different
  one." now reads "Kept: nothing is replaced or removed, and a release that would write over a
  different file isn't sent.", since a replacement can now be scheduled. MAM-38's warning about a
  destination folder the check can't look into now sits right under the destination, and in Replace
  mode gives way to the replacement's own sentence, as such a destination takes no replacement.
- **The Media folders API answers and takes the replacement gates (MAM-24).**
  `GET /api/settings/allowed-roots` answers
  `{ paths, replacementEnabled, libraryFolders, unmappedLibraryFolderCount }` where it answered
  `{ paths }`, and `PUT` takes `{ paths, replacementEnabled, replacementFolders }`. A `PUT` with
  `{ paths }` alone, as 0.2.1's page sends it, switches replacement off and unticks every folder.
- **A download job carries its replacement (MAM-24).** `DownloadJobDto` gains a trailing
  `replacement`, after `statusChangedUtc`: `null` for a job sent with Keep both, else
  `{ state, copyCount, reason, switchedOff, copiesOutsideTickedFolders }`, `state` by name.
- **Every send records what the release turned out to be (MAM-24).** Right after the fetch, a job
  keeps its payload's kind, `TorrentFile` or `Magnet`, by name, Keep both sends included
  (`DownloadJobs.PayloadKind`, not in the API): the payload type specification §4 asks a replacement
  to keep, which a gateway's link names only once fetched. It stays `null` for a job sent before the
  upgrade and one whose payload couldn't be fetched.
- **qBittorrent's checked session now hands back the version it read (MAM-24).** The adapter's one
  `app/version` read before each call returns the version as reported and as compared, so the whole
  listing gates on 5.2 without a second read; the job lookup, the files read and the submission pass
  it by unchanged. `HttpIntegrationAdapter.CallAsync` takes an optional sentence for an answer over
  the 4 MiB cap, which only the whole listing passes.
- **The release matching, a caller's release lookup and the edition words are shared, not copied
  (MAM-24).** `IReleaseMatchReader` holds the per-release matching `SearchService` did,
  `CallerSearches` the rule that a release is one of the caller's searches' from the last day (the
  search, the send and the replacement read), `CopyEditions` the Duplicates page's edition words of
  a copy, `LibraryFolderReader` the Destinations page's library folders, and `FileChips` the
  Library's chips of a file; the behaviour of each is unchanged, and their own suites pass as they
  were.
- **A replacement's state gains `Inspecting` and `Verified` (MAM-25).** `replacement.state` on every
  job the API answers may now also be `Inspecting` (its new files being checked) or `Verified`
  (every ticked copy paired with a checked new file), by name; `NeedsReview` and `Failed` may now
  come from the check, with its reason. A replacement in either still holds its copies, so another
  download can't tick them (`AlreadyScheduled`) until MAM-26 has moved them.
- **The Jobs line follows the check, and the pages keep asking while it runs (MAM-25).** Under a job
  whose download is done, "Replacement waits for verification: 1 copy. This version doesn't verify
  or move anything yet." gives way to "Replacement checks the new file: 1 copy, kept until it is
  verified." while it is `Downloaded` or `Inspecting`; then "New file verified: 1 copy will be
  replaced by it in a later version; nothing is moved yet." (MAM-26 below rewords it, as the old
  copy now moves), its icon green as a downloaded job's;
  or "Replacement needs review: <reason>" — MAM-24's review of a download that needs linking now
  reads so too, where it read "Replacement stopped: …" —; `Failed` and `Cancelled` keep "Replacement
  stopped: <reason>". The switched-off and unticked-folder notes now show while it is scheduled,
  checked or verified, `Scheduled` to `Verified`, and on no stopped replacement — needing review,
  failed or cancelled —, which goes no further until the owner acts, though one in review still
  holds its copies: "Replacement checks the new file, but replacement is switched off: nothing will
  move while it is off." and "New file verified, but replacement is switched off: nothing will move
  while it is off.". The Jobs page and a job's page ask again every 2 s while a job's download is
  followed or its replacement is `Downloaded` or `Inspecting`, so the verdict shows without a
  reload. `JobsPage.test.tsx`, `JobsPage.polling.test.tsx`, `JobDetailsPage.test.tsx` and
  `download-jobs.test.ts` pin it.
- **The Download dialog and the Media folders page say the new file is checked (MAM-25).** The
  summary's conditions end "Once the download is done, the app checks that the new file is complete,
  readable and identified by Jellyfin as the old copy's work, but nothing is moved in this version:
  moving comes in a later version." where they ended "This version only schedules the replacement:
  once the download is done it waits, and nothing is checked or moved until a later version.", and
  the switch's hint says "In this version a replacement is scheduled and its new file checked once
  downloaded, but nothing is moved; moving old copies comes in a later version and will also need
  the media folders mounted read-write." (MAM-26 below rewords both again, as the old copy now
  moves.) `DownloadDialog.replace.test.tsx` and `MediaFoldersPage.test.tsx` pin them.
- **The library folders carry their library and path, and a client file is placed in one place
  (MAM-25).** `LibraryFolderReader` now gives each library folder its integration and its path in
  the library's own terms beside its path in the app, which the check needs to ask the right library
  about a new file; and the mapping of a client's file into the app, which MAM-24's marks did inside
  `ReplacementHoldingReader`, is `ClientFilePlaces`, shared by the marks and the check. The
  Destinations page, the replacement gates and the marks behave as they did, and their suites pass
  unchanged.
- **The Jobs line, the Download dialog's conditions and the switch's hint say that the old copy now
  moves (MAM-26).** A verified replacement's line reads "New file verified: 1 old copy moves into the
  quarantine next." where it read "New file verified: 1 copy will be replaced by it in a later
  version; nothing is moved yet."; after it come "Moving the old copy into the quarantine…" (with a
  spinner), "Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for
  good." (and "1 file stayed in its folder: see the job." when one stayed), "Putting the old copy
  back…", "Deleting the old copy for good…", "Replacement undone: the old copy is back where it was."
  and, green, "Replaced: the old copy was deleted for good."; the pages also ask again every 2 s while
  a replacement moves, is put back or is deleted, and while it is verified with replacement on. A
  copy still to move whose folder is no longer ticked now reads "1 copy is no longer in a ticked
  folder: unless it is ticked again, the replacement stops for review and nothing moves." where it
  read "1 copy is no longer in a ticked folder and won't move.", as the check right before the move
  stops the whole replacement over it; verified with replacement on, the line says only "New file
  verified, but 1 copy is no longer in a ticked folder: …", and `copiesOutsideTickedFolders` no
  longer counts a copy whose video is in the quarantine already. The
  summary's conditions read "Conditions: the old copy moves into the quarantine only after the new
  one is downloaded, checked and shown in Jellyfin as the same work; a copy held by a torrent waits
  until you remove that torrent in qBittorrent. Once the download is done, the app checks that the
  new file is complete, readable and identified by Jellyfin as the old copy's work. The old copy
  stays in the quarantine until you restore it or delete it for good on the job's page." where they
  read "Conditions: the old copy would move aside only after … but nothing is moved in this version:
  moving comes in a later version."; and the switch's hint ends "Once the new file is verified, the
  old copy moves into its media folder's quarantine and stays there until you restore it or delete
  it for good. Moving needs the media folders mounted read-write and the app running as their owner."
  where it ended "In this version a replacement is scheduled and its new file checked once
  downloaded, but nothing is moved; moving old copies comes in a later version and will also need the
  media folders mounted read-write." (MAM-39 above adds ", set with PUID and PGID" to its end).
  `JobsPage.test.tsx`, `JobsPage.polling.test.tsx`,
  `JobDetailsPage.test.tsx`, `download-jobs.test.ts`, `DownloadDialog.replace.test.tsx` and
  `MediaFoldersPage.test.tsx` pin them.
- **The Media folders API answers the rename hint and the quarantines (MAM-26).**
  `GET /api/settings/allowed-roots` answers
  `{ paths, replacementEnabled, libraryFolders, unmappedLibraryFolderCount, quarantines }`, each
  folder `{ appPath, libraryNames, ticked, status, renameProblems }` — `renameProblems` `[]`, or
  `OtherMount`, `ReadOnly`, `NotWritable` and `CouldNotCheck` by name, for an `Available` folder, and
  `null` for the others — and each quarantine `{ path, replacementCount, fileCount, sizeBytes }`
  (MAM-40 below adds `removalCount`);
  `PUT` may now answer 400 for a quarantine it would leave out of reach.
- **A replacement's line carries its id and what the move did (MAM-26).** `replacement.state` may now
  also be `Quarantining`, `Quarantined`, `Restoring`, `Deleting`, `Restored` or `Completed`, by name,
  the first four holding their copies as the open states do; and `replacement` gains, after its
  other fields, `id` (for the replacement's own routes), `quarantinedCopyCount` (the ticked copies
  whose video is in the quarantine), `stayedFileCount` (the companions that stayed in a replacement
  that moved something) and `canRetry`, read for a page in one query. `ReplacementScheduleHttpTests`
  pins the shape.
- **The media library's request to look says what happened, and the download client contract's
  forbidden words grow (MAM-26, ADR 0005, ADR 0031).** `IMediaLibraryFileLookup.RequestScanAsync`
  takes a `LibraryFileChange`, and Jellyfin's adapter sends it as each path's `UpdateType`,
  `Created` or `Deleted`, where it always sent `Created`. `DownloadClientNeverRemovesTests` now also
  refuses a member named for moving, relocating or renaming, or with the consecutive words set and
  location — ADR 0031's `SetLocationAsync` — and, our addition, set, save and path or set, download
  and path; its method keeps its name, and `IDownloadClient` gains no member.
- **The verifier's check of a paired new file and the holdings reader are shared with the move
  (MAM-26).** The identity check of a paired new file is `ReplacementNewFileCheck`, which the
  verifier now calls with the same sentences; `IReplacementHoldingReader` reads the dialog's copies
  through the move's overload for names; and a torrent's file whose mapped path is a copy's own path
  is now a hold whatever size it lists, in the dialog too, where a different size left it out
  (`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_marks_a_copy_a_torrent_lists_at_its_path_whatever_size_it_lists`).
  The dialog still reads every client, those whose mappings reach no Media folder included
  (`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_still_answers_for_a_client_whose_mappings_reach_no_media_folder`).
- **A replacement's line carries what the pages need to say what to do next (MAM-27).**
  `replacement` gains, after its other fields, `droppedCopyCount`, `dropOnlyCopyCount` (the copies
  still in it that only a drop settles), `canCancel` (the owner's Keep both), `stop` (what stopped a
  `NeedsReview` — `Download`, `Check` or `Move`, by name —, `null` in any other state), `checkStage`
  (`Files` or `Library`, by name, only while `Inspecting`) and `checkWaitsUntilUtc` (only while
  `Inspecting`); `copyCount` now counts the copies not dropped, `copiesOutsideTickedFolders` the
  copies not dropped and not yet moved, and `canRetry` is true for a stop of the move, and for a
  stop of the check while no copy that only a drop settles is in it. All of it is read from the rows
  the page already reads, one query per table. `ReplacementLineHttpTests` and
  `ReplacementScheduleHttpTests` pin the shape.
- **A replacement's details carry its copies, its new files and its works (MAM-27).**
  `GET /api/replacements/{id}` keeps its fields, `canRetry` now by the rule above, and gains
  `canCancel`, `stop`, `checkStage`, `checkStartedUtc`, `libraryAskedUtc`, `checkWaitsUntilUtc`,
  `dropOnlyCopyCount`, `copies`, `newFiles` and `works`; the README's API table has each shape.
  `ReplacementDetailsHttpTests` and `ReplacementQuarantineFlowHttpTests` pin it.
- **The job's page calls its section Replacement, and tells a member a little more (MAM-27).** The
  section reads **Replacement** where it read **Old copies**, as it now holds the new files too, and
  MAM-26's table of names sits inside it under **Files the move planned**; a member reads "Only the
  installation owner sees a replacement's copies and checks, and retries, cancels or drops them."
  where he read "Only the installation owner sees the old copies a replacement moves, and restores
  them or deletes them for good.". `JobDetailsPage.replacement.test.tsx` pins it.
- **MAM-26's Retry answers a replacement that isn't in review with a shorter sentence (MAM-27).**
  "This replacement isn't waiting for review after a move, so there's nothing to retry here." now
  reads "This replacement isn't waiting for review, so there's nothing to retry here.", as a stop of
  the check is retried too; a stop of the move is retried as before. `ReplacementRetryHttpTests`
  pins it.
- **The check marks what only a drop settles, and forgets its last run in one place (MAM-27).** The
  pairing's outcome for each old copy (`ReplacementOldCopyOutcome`) says whether only dropping it,
  or keeping both, can settle its problem, which the check stores beside the problem; and deleting
  the new files' rows with each copy's pair, problem and mark, which the check's files stage did
  alone, is `ForgetCheckAsync`, shared with the owner's Retry, leaving a dropped copy's last problem
  as it was. A stop of the library stage before the pairing — a new file that changed, or Jellyfin
  not read once the hour ran out — clears what the passes that waited before it stored on each copy
  left and each new file, as none of it was a verdict, so a mark of theirs never withholds Retry.
  The check's own suites reach the verdicts they reached, nothing they plant being dropped.
  `ReplacementPairingDropOnlyTests` and `ReplacementDroppedCopyHttpTests` pin it.
- **A confirmation's title breaks a long name anywhere (found by MAM-27's look in a browser).**
  Unbroken, "Keep both copies of Heat.1995.2160p.UHD.BluRay.REMUX.HDR.HEVC?" — and Cancel job's and
  Drop's alike — pushed the dialog past its right edge, cutting off the message and the buttons, at
  1280 px and at 375 px; the title of a confirmation now wraps anywhere, clear of the close button
  (`.confirm-dialog .ui-dialog-title`, pinned in `downloads.test.ts`).
- **The release note's Turning it on names the compose file's `user:` line, a backup first, every
  hint and the way back (MAM-28, found preparing the live check and confirmed on production on
  2026-10-12).** Production's `docker-compose.yml`, taken before 0.3.0, had no `user:` line, without
  which `APP_USER_ID` and `APP_GROUP_ID` do nothing and the app, its volume handed over, can't open
  its data. Step 1 now says a mount without `:ro` is read-write already; step 2 gives the line
  `user: "${APP_USER_ID:-10001}:${APP_GROUP_ID:-10001}"` to add under `restart:`, says that
  `bash deploy.sh` doesn't add it and `docker compose config | grep 'user:'` shows whether it took,
  backs up `mam.db` and `.secrets/` together with the app stopped before the `chown`, and says what a
  start that logs `Permission denied` means; step 3 says a Media folder added there is saved before
  its library folders can be ticked; step 4 quotes each line the Media folders page can give, "The
  app couldn't check whether a move can work here; the move itself will tell." among them, and says
  that it never looks into the folders inside a ticked one, so a copy in a folder the app's user
  can't write to — four of production's belong to root — stops its replacement with the copy where
  it is. **Replace after verification** is said to be there but disabled until then, rather than to
  appear. A new **Going back** settles the quarantine first, while replacement is on and the app
  runs as the media folders' owner, then hands the volume back with `chown … -R 10001:10001 /data`.
  The release note alone changed; no code. MAM-39 above replaced step 2 and **Going back** again,
  inside 0.3.0: they set and take out `PUID` and `PGID`, and ask for no `user:` line, backup or
  `chown`; the rest of this correction stands.
- **The image has no `USER`, and the app's command moved into `ENTRYPOINT` (MAM-39, ADR 0036).** The
  container starts as root, and `docker-entrypoint.sh` — root's, mode 0755, let into the build
  context's allowlist in `.dockerignore` — drops to `PUID` and `PGID` before the app starts.
  `ENTRYPOINT` is `["/usr/local/bin/docker-entrypoint.sh", "dotnet", "MediaAcquisitionManager.Api.dll"]`,
  the app's command in it rather than in a `CMD`, so arguments after the image name still reach the
  app as configuration. The health check's `curl` and `docker exec` now run as root inside the
  container, unless it is given a user; `docker exec … id` therefore reads root's ids, and
  `/proc/1/status` the app's.
- **The compose file has no `user:` line, and `APP_USER_ID` and `APP_GROUP_ID` are gone (MAM-39).**
  `docker-compose.yml` loses `user: "${APP_USER_ID:-10001}:${APP_GROUP_ID:-10001}"` and gains
  `PUID` and `PGID`, commented out, under `environment:`; `.env.example`'s new **Container user**
  section replaces the one on `APP_USER_ID`, `APP_GROUP_ID` and the `chown` of the data volume, and
  the comments of both files name `PUID` and `PGID` for the media's owner.
- **The Media folders API answers `writeAccess` (MAM-39).** Each `libraryFolders` item of
  `GET /api/settings/allowed-roots` now also has `writeAccess`,
  `{ path, ownerUserId, ownerGroupId, appUserId, appGroupId }` or `null`: filled only for an
  `Available` folder whose `renameProblems` holds `NotWritable`, on Linux, when the owner could be
  read, `path` in canonical form.
- **The Duplicates page says what each kind of duplicate is, in plain words (MAM-40, ADR 0028,
  Vasyl's decision of 2026-10-12).** Vasyl couldn't tell what "Same work" meant. The tabs read All,
  "One file, two names", "Exact copies", "Same film, different files", "Maybe the same film", "Kept
  as they are" (was Dismissed) and, for the owner, "Removed copies"; a card's badge has the same
  words, an episode's group "Same episode, different files"; and each card has one line saying what
  its group is and what can be done, such as "These files hold exactly the same bytes. Remove a copy
  you don't need, or keep them as they are." or "One file reached by two names or more (hardlinks):
  it takes its room once, so there is nothing to keep or remove.". The page's lead no longer says
  nothing there removes a file: "… Removing a copy moves it into the quarantine folder of its media
  folder, where you can restore it or delete it for good under Removed copies; keeping copies as
  they are, or confirming a possible match, removes nothing."; the Library page's **Duplicates**
  button's title uses the same names. The API's class names are unchanged. `DuplicatesPage.test.tsx`,
  `DuplicateGroupCard.test.tsx`, `duplicates.test.ts` and `LibraryPage.test.tsx` pin it.
- **A decision is refused by what the group's class takes (MAM-40, ADR 0029).**
  `PUT /api/library/duplicates/{key}/decision` takes `Dismissed` on `SameWork` and `ByteIdentical`
  groups too (Keep both, above), and answers 400 "These files are copies by their bytes or registry
  ids, which no one has to confirm: keep them as they are, or remove a copy." to `Confirmed` on them,
  and "These are names of one file, not copies of it: removing a name frees nothing, so there is
  nothing to keep or remove." to any decision or undo on `SameFile`; the old sentence for a group of
  another class, "Only a possible match is confirmed or dismissed: …", is gone. `DELETE` undoes kept
  copies too. `decision=Dismissed` lists kept copies beside dismissed matches, a kept group's
  `decision` is `Dismissed` while no new file has joined it, and that list now asks the file system
  for its byte-identical groups' estimate.
- **The duplicates list's files carry the removal verdict (MAM-40).** Each `files` item of
  `GET /api/library/duplicates` gains a trailing `removal`, `{ allowed, reason }` on `ByteIdentical`
  and `SameWork` groups and `null` on the others.
- **The Media folders page counts the removals in the quarantine (MAM-40, ADR 0030).** Each
  quarantine of `GET /api/settings/allowed-roots` gains `removalCount`, beside `replacementCount`, and
  its `fileCount` and `sizeBytes` count a removal's names too; the page says "/media/.mam-quarantine:
  4 GiB in 4 files, from 1 replacement and 2 removals." ("from 1 removal." when it holds no
  replacement's), and its hint now links to **Removed copies** as well as **Jobs**. A Save that would
  strand a quarantine is refused for a removal's names too, as "“/media/.mam-quarantine” still holds
  replaced or removed copies, so its media folder can't be taken out of the list. Restore them or
  delete them for good first.", where it said "still holds replaced copies". The switch's hint adds
  that the Duplicates page offers Remove this copy for files in the ticked folders, and that a
  removed copy moves into the quarantine at once. `ReplacementSettingsHttpTests` and
  `MediaFoldersPage.test.tsx` pin it.
- **The Download dialog won't tick a copy an open removal holds (MAM-40, ADR 0032).** A copy an open
  removal holds, by path or by device and inode, gets a copy reason of its own, `HeldByRemoval`
  (10), after `AlreadyScheduled`: the dialog says "A removal under Removed copies on the Duplicates
  page holds this copy.", and a send that ticks it is 409 "A removal under Removed copies on the
  Duplicates page holds “…”, so it can't be replaced. Pick Keep both to download the release." with
  nothing written or sent; an ended removal holds nothing (`ReplacementOptionsHttpTests`,
  `ReplacementScheduleHttpTests`).
- **The quarantine's checks and writes are shared by a replacement and a removal (MAM-40, ADR 0030,
  ADR 0031).** A replacement's names keep their table, `ReplacementJobNames`, their columns now
  `QuarantinedNameEntity`'s, an abstract base EF doesn't map, which a removal's names share; check
  5's look at the copy, the companions' listing, check 6 and its sentences (`QuarantineWording`:
  "the old copy" and "Retry" for a replacement, "this copy" and "Remove again" for a removal), the
  owner's write loop of Restore and Delete for good and its checks, the look-up of a cut name, and
  Jellyfin's notice moved out of the replacement's classes into ones both use, with no change for a
  replacement. In the web app, a quarantined name's row (`QuarantineNameRow`) and Delete for good's
  confirmation (`DeleteForGoodMessage`) moved out of the job page's **Replacement** section into
  shared components, and the list of files with their sizes that Remove's, Restore's and Delete for
  good's confirmations show is one, `FileSizeList`.
- **A Jobs row says where the download and the replacement stand in a few words, keeps only verbs
  under Actions, and opens the job's details under it (MAM-41, ADR 0014, ADR 0032, Vasyl's decisions
  of 2026-10-12).** The columns are **Release**, **Download**, **Replacement** — the owner's alone —
  and **Actions**, where they were Release, Status, Progress, Sent to, Sent and Actions. **Release**
  is the title, with the client, the destination and when it was sent under it. **Download** says the
  status in a word, with a bar only while the app follows the download, "Cancelled at 1.8%" for a
  download cancelled part-way, and a block reason or an error cut at two lines. **Replacement** says
  where the replacement stands in a few words — "Waits for the download: 1 copy", "Checking the new
  files", "Waits for Jellyfin until 14:05", "Verified, moves next", "Switched off: nothing moves", "9
  old copies in the quarantine", "Check stopped", "Move stopped", "Can't follow the download",
  "Undone: the old copy is back", "Replaced: old copy deleted", "Cancelled: nothing moved" —, a dash
  for a job without one. **Actions** holds verbs only, the step forward first: **Retry**, **Keep
  both**, **Restore**, **Delete for good** and **Cancel job**. A chevron before the title opens, in a
  row of its own under the job, what the cells cut short: the whole sentences — why the download
  isn't moving on or failed, the replacement's line with what to do ("Replacement needs review: …
  Fix it in Jellyfin or qBittorrent, then press Retry — or see the job."), why the release went
  unchecked —, the replacement's old copies with their new files and **Drop**, asked for only while
  open, where it came from ("From Toloka"), the client's id for it and **Open the job page**. MAM-27's
  **Copies** button is gone, as the copies are details, not an action (Vasyl's word). The job's own
  page keeps its layout. `JobsPage.details.test.tsx`, `JobDownloadSummary.test.tsx` and
  `ReplacementSummary.test.tsx` pin it.
- **Every Delete for good confirmation is half as wide again (MAM-41).** On a Jobs row, on the job's
  page and under the Duplicates page's **Removed copies**, Delete for good's confirmation, which lists
  every file with its path and size, is 50% wider than a plain one (`.confirm-dialog-wide`), and each
  file's size sits on its path's line at the right edge rather than under it, so the list fills the
  width and runs half as long; a path too long for the line still breaks, its size under it (Vasyl,
  2026-10-12). Remove's and Restore's lists of files on the Duplicates page take the same lines.
  `ConfirmDialog.test.tsx` pins the width.
- **The list of jobs takes a view and counts two, and a job's replacement says what Restore and
  Delete for good take (MAM-41).** `GET /api/downloads` takes `view` beside `status`, and answers
  `{ items, totalCount, page, pageSize, needsYouCount, inProgressCount }` where it answered
  `{ items, totalCount, page, pageSize }`; `replacement` gains, after its other fields, `canRestore`
  and `canDeleteForGood`.
- **The Sources card shows the same warnings with Details open or closed (MAM-42, Vasyl's decisions of
  2026-10-13).** With Details closed the bar warned "17 in no library" and "1 couldn't be read"; open,
  only the first, as the Inspection item's chip summed every file no look could read, the libraries'
  and the scan's. Every amber chip on the bar now sits on the item whose row under Details holds its
  amber line, and the other way round. A library's item keeps "N files without an app path" and gains
  "N couldn't be read" for its own files, its row a count line for them — the owner's with the first
  sentence, "1 file couldn't be read: …", a member's "… The owner can see why." — that doesn't repeat
  the path problem its "no path in this app" warning already quotes. The Folders item says "N in no
  library", counted now — the scan's names no library reports and the owner hasn't ignored — where it
  showed the last scan's stored `filesUnreported`, "N folders not scanned" as before and, new, "N
  couldn't be read" for the files in no library; the two file chips are buttons, "Show these files",
  that open the dialog (above) on the matching tab. The Inspection item carries only "N couldn't be
  hashed", the hashing's own problems once its run has succeeded, as its row's hashing warning does.
  The Media folders row's status line ends with the files found, "Scanned Oct 12, 2026, 6:24 PM: 1,404
  files." (it added ", 17 in no library."), followed by an amber "17 files are in no library." with
  **Show files**; the counts line adds the ignored ones, "Files in no library: 16 inspected · 1
  couldn't be read · 1 ignored" (with every file ignored, **Show files** ends it and opens Ignored);
  and the files that couldn't be read are one amber line with a count and **Show files**. The rule
  for "couldn't be read" is one, `FileInspectionCounts.IsProblem`, and the Media folders row's counts
  and the dialog read the same files (`UnreportedFileReader`). `LibrarySyncPanel.bar.test.tsx`,
  `LibrarySyncPanel.scan.test.tsx` and `LibrarySyncPanel.inspection.test.tsx` pin it.
- **The sources leave the ignored files out of their counts (MAM-42).** In `GET /api/library/sources`,
  `scan.files` leaves out the names the owner ignored, and `scan` gains `ignoredCount` after `space`,
  how many of the scan's names no library reports are ignored; `scan.space` still counts them.
  `GET /api/library/scans/{runId}`'s `filesUnreported` is still what that scan stored.

### Notes for upgraders

- **Applying `AddReplacementSettings` and `AddReplacementJobs` rebuilds no table, so it logs no
  warning 20410 (MAM-24).** `AddReplacementSettings` only creates `ReplacementSettings` and
  `ReplacementFolders`, with the unique index on a tick's path; `AddReplacementJobs` adds one
  nullable column, `DownloadJobs.PayloadKind`, and creates `ReplacementJobs`, `ReplacementJobFiles`,
  `ReplacementJobWorks` and `ReplacementJobWorkIdentifiers` with their six indexes, one of them the
  partial `IX_ReplacementJobs_Followed`. Every job sent before the upgrade keeps `null` there and
  carries no replacement.
- **Applying `AddReplacementVerification` rebuilds no table, so it logs no warning 20410 (MAM-25).**
  It adds nullable columns — `ReplacementJobs.InspectionStartedUtc` and `LibraryAskedUtc`, and
  `ReplacementJobFiles.PairedNewFileId`, `EpisodeNumberEnd`, `DurationMilliseconds` and `Problem` —
  and creates `ReplacementJobNewFiles` and `ReplacementJobNewFileIdentifiers`, with no key into a
  table a sync rebuilds, and three indexes, one of them the partial `IX_ReplacementJobs_Verifying`.
  A replacement already `Downloaded` is checked by the first pass after the start.
- **Applying `AddReplacementQuarantine` rebuilds no table, so it logs no warning 20410 (MAM-26).** It
  adds three nullable columns to `ReplacementJobs` — `QuarantineCheckedUtc`, `QuarantinedUtc` and
  `LibraryNoticeDueUtc` —, creates `ReplacementJobNames`, one row per name a move plans, deleted with
  its replacement, with the unique index on `(ReplacementJobId, Position)`, and adds the partial
  `IX_ReplacementJobs_Quarantine` over `Verified` and `Quarantining`, leaving the following's and the
  check's indexes as they are; every replacement comes through whole, with nothing quarantined
  (`AddReplacementQuarantineMigrationTests`). A replacement already `Verified` is taken up by the
  mover's first pass after the start, once replacement is switched on.
- **Applying `AddReplacementCopyDrops` rebuilds no table, so it logs no warning 20410 (MAM-27).** It
  adds two columns to `ReplacementJobFiles` — `DroppedUtc`, nullable, and `OnlyDropSettles`, not
  null with a default of false — and no index; every replacement comes through whole, each of its
  copies not dropped and open to a Retry (`AddReplacementCopyDropsMigrationTests`). A review the
  check stopped before the upgrade offers Retry whatever its reason, as no copy carries the
  drop-only mark yet; a Retry that can't help stops again, and marks the copy this time.
- **Replacement is off, and no folder ticked, after the upgrade, and turning it on takes four steps
  (MAM-24, MAM-26, MAM-39, ADR 0033, ADR 0036).** Members send as before, with Keep both, and so
  does the owner until he turns it on; the owner's dialog says why Replace can't be picked. To turn
  it on: (1) mount the media folders read-write — the media mount in `docker-compose.yml` without
  `:ro`, each allowed folder one mount holding its libraries with nothing mounted inside it, so that
  its quarantine shares the mount —; (2) set `PUID` and `PGID` to the media folders' owner, usually
  the same as your qBittorrent's (1000 and 100 on the reference installation), under the `app`
  service's `environment:` or in `.env`, delete a `user:` line if the service has one, and restart
  the app with `bash deploy.sh` or `docker compose up -d` — `docker compose restart` keeps the old
  settings —: the container hands its own data over to them as it starts, and its log says so;
  (3) user menu → **Media folders** → **Replacing copies**, tick **Replace copies after
  verification**; (4) tick the library folders it may touch, each saying whether a move into the
  quarantine can work there — or which folder the app can't write to, whose it is and which `PUID`
  and `PGID` to set —, then **Save**. A copy can then be ticked only once the app has inspected it
  inside the Media folders, and only through a destination whose category saves inside a library
  folder under them. With the switch on but the mount still `:ro` or the image's own ids, the Media
  folders page says a move would fail, and a move that is tried stops its replacement for review
  with nothing moved; so does any move on a host that isn't Linux. To go back, settle the quarantine
  first, while replacement is on and the app still runs as the media folders' owner — Restore or
  Delete for good every replacement with a copy there, and Keep both on every other one still under
  way —, then switch it off, take `PUID` and `PGID` out and restart the app: it hands its data back
  to 10001 by itself. To go back to 0.2.1, which runs as 10001 and hands nothing over, let 0.3.0
  start once that way first, then set `APP_TAG=0.2.1`, as the release note's **Going back** says.
- **What moves, and what the backup doesn't keep (MAM-24, MAM-25, MAM-26, MAM-27).** Scheduling
  reads the download clients' torrents and the old copies' identities, and the check of the new
  files reads qBittorrent, the new files and Jellyfin, sending Jellyfin only
  `POST /Library/Media/Updated`; the move then writes to the media folders `.mam-quarantine` and its
  replacement folders, the renames of the old copies' files into it and out of it, and the deletions
  from it the owner confirms — nothing else, and nothing to qBittorrent but the one add every send
  makes. The owner's Keep both, Drop and Retry write only the app's own rows, nothing to the media
  folders and nothing to qBittorrent; a retried check asks Jellyfin to look again, as the first one
  did. Quarantined files stay on disk in `.mam-quarantine` at the top of each media folder, outside
  the database backup: backing up `mam.db` and `.secrets/` doesn't keep them, and restoring a backup
  doesn't bring them back. The replacements, the evidence of their checks, the copies dropped from
  them and where each old file went live in `mam.db` alone; back it up with `.secrets/`. A
  replacement needs room for both copies until the old one is deleted for good.
- **The marks of torrents that hold a copy need qBittorrent 5.2 or later (MAM-24).** An older one —
  the app still sends to and follows 4.4.1 and later — is reported in the dialog as not checked,
  "Couldn't check whether a torrent in “qBittorrent” holds these copies: This is qBittorrent 5.1.4;
  telling which torrents hold a library file needs qBittorrent 5.2 or later.", and scheduling stays
  allowed. The check of the new files needs it too (MAM-25): it can't tell whether another download
  writes a new file without the whole listing, so with an older qBittorrent every replacement stops
  for review there ("This version of “qBittorrent” can't list the files of its other downloads, so
  the app can't tell whether another one writes the new files."). So does any other torrent of it
  the app can't place in its folders — a magnet still fetching its metadata, one saving where no
  path mapping of the qBittorrent integration reaches —, once the check's hour has run out ("The app
  can't place “qBittorrent”'s download “…” in its folders, so it can't tell whether that download
  writes the new files."): map every folder qBittorrent saves to. The check right before a move
  needs it as well (MAM-26): with an older qBittorrent, or torrents it can't place, every move stops
  for review ("The app couldn't tell whether a torrent in “qBittorrent” holds the old copy: This is
  qBittorrent v5.1.4; telling which torrents hold a library file needs qBittorrent 5.2 or later.").
- **A `docker-compose.yml` of 0.3.0 as first published keeps working, and so does a replacement
  turned on with it (MAM-39).** Its `user: "${APP_USER_ID:-10001}:${APP_GROUP_ID:-10001}"` wins over
  `PUID` and `PGID`: the app runs as the ids it gives, and the log says `PUID` and `PGID` are ignored
  while it is there. To move to them, delete the line, set `PUID` and `PGID` to the ids
  `APP_USER_ID` and `APP_GROUP_ID` held, and restart: a data volume already handed over to them
  changes nothing. Without the line, `APP_USER_ID` and `APP_GROUP_ID` do nothing and can go. To go
  back from such a setup, delete the line too: under it the app starts as the ids it gives and can't
  hand its data back to 10001.
- **A container run with `cap_drop: ALL` and no `user:` line refuses to start the app (MAM-39, ADR
  0036).** The container now starts as root, which `cap_drop: ALL` leaves without the `CHOWN`,
  `DAC_OVERRIDE`, `SETGID`, `SETUID` and `SETPCAP` the hand-over and the switch take, so the
  entrypoint refuses up front, `PUID` and `PGID` set or not, and changes nothing: "[entrypoint] The
  container runs as root without the capabilities it takes to switch to 10001:10001 (CHOWN,
  DAC_OVERRIDE, SETGID, SETUID and SETPCAP; cap_drop: ALL removes them), so the app was not started.
  Keep Docker's default capabilities, or run the container as a user instead: user: "10001:10001"
  in docker-compose.yml, with the data volume already theirs." Two ways out: drop the
  `cap_drop: ALL`, or give the service that `user:` line — the image's 10001, or the media's owner
  —, with the data volume already theirs; the entrypoint then only starts the app.
- **`group_add` keeps working (MAM-39).** The supplementary groups Docker gives the container —
  compose's `group_add`, `docker run --group-add` — stay the app's whoever it runs as, as they did
  under the image's own user; root's group 0 never does, and with none the app has none.
- **A bind-mounted data directory changes owner on the host (MAM-39).** With `PUID` and `PGID` set,
  the container hands it over to them as it starts, and back to 10001 once they are removed; its
  mode stays as it was made, so create it 0700 first, as before.
- **Applying `AddCopyRemovals` and `AddKeptDuplicateMembers` rebuilds no table, so it logs no
  warning 20410 (MAM-40).** `AddCopyRemovals` only creates `CopyRemovals`, with an index on who made
  each and the partial `IX_CopyRemovals_Open` over `Moving`, `Quarantined`, `NeedsReview`,
  `Restoring` and `Deleting`, and `CopyRemovalNames`, deleted with its removal, with the unique index
  on `(CopyRemovalId, Position)`; it touches no other table, and every replacement's names come
  through whole (`AddCopyRemovalsMigrationTests`). `AddKeptDuplicateMembers` adds one nullable
  column, `DuplicateMatchDecisions.KeptMembers`: every decision on a possible match comes through as
  it was, with none.
- **Applying `AddIgnoredUnreportedFiles` rebuilds no table, so it logs no warning 20410 (MAM-42).**
  It adds one nullable column, `PhysicalFiles.IgnoredUtc`, and no index: every file in no library
  comes through counted, none ignored.
- **Removing a copy from the Duplicates page needs replacement turned on, and nothing more (MAM-40,
  ADR 0033).** It stands behind the same four steps as a replacement — the media folders mounted
  read-write, `PUID` and `PGID` set to their owner, the switch on and the library folder ticked —,
  and until then every **Remove this copy** is disabled with why; **Keep both** works whatever the
  switch says. A removed copy's files stay on disk in `.mam-quarantine`, outside the database backup,
  as a replaced copy's do; the removals and where each file went live in `mam.db` alone. To go back
  to 0.2.1, settle **Removed copies** too — Restore or Delete for good every removal there — while
  replacement is still on, as the release note's **Going back** says for replacements: 0.2.1 knows
  nothing of the quarantine.
- **A Blu-ray or DVD film gets its files at the first sync after the upgrade (MAM-43, ADR 0038).** No
  migration: the clips become their films' files when the sync lists them, so until **Sync** or
  **Refresh all** runs, Files in no library still lists them. A disc the app can only reach through a
  path mapping needs that mapping on the Jellyfin integration, as any file does.

## [0.2.1] — 2026-10-05

### Added

- **The owner connects TMDb as a metadata catalog, the fourth kind of integration (MAM-33, ADR 0005,
  ADR 0007).** The Integrations page adds an integration of type **TMDb** (`tmdb`), kind `Metadata`
  ("Metadata catalog"), at `https://api.themoviedb.org`, with TMDb's API Read Access Token in the API
  key field (Vasyl's decision 1): the dialog says "TMDb's API address, https://api.themoviedb.org. In
  the API key field, enter TMDb's API Read Access Token from themoviedb.org → Settings → API, not the
  shorter API key." and shows TMDb's attribution, "This product uses the TMDB API but is not endorsed
  or certified by TMDB.". `GET /api/settings/integrations/adapter-types` lists it with `apiKey`
  `Required`, no user name or password, and `reportsFilePaths` `false`; the list's kind filter takes
  `Metadata` and offers **Metadata catalogs**. The adapter appends `/3` to an address that doesn't end
  in it, sends the token only as `Authorization: Bearer`, asks everything in Ukrainian
  (`language=uk-UA`) with the original title beside it, and only ever sends GETs. **Test connection**
  asks `GET /3/authentication` and answers "Connected to TMDb.", or for a refused token "TMDb refused
  the key (401 Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API,
  not the shorter API key.", and for an address that isn't TMDb's "TMDb's API wasn't found at this
  address (404 Not Found). Use https://api.themoviedb.org." or "The service at this address answered,
  but not the way TMDb's API does. Check the address: TMDb's is https://api.themoviedb.org.". Requests
  start at least 25 ms apart, whoever sends them; a 429 holds every request back for its
  `Retry-After` (2 seconds without one, 10 at most) and the same GET is sent again at most three
  times, then the call fails with "TMDb is asking the app to slow down (429 Too Many Requests), and
  still was after three retries. Try again in a minute.". Adding a TMDb integration switched on, or
  switching one back on, queues a computation of the recommendations; the integration's save writes
  its one audit row as before, and the queuing none.
- **The app computes recommendations from the library and the catalog in the background (MAM-33, ADR
  0023, ADR 0025).** A run (202 + run row + reconciler) starts after every successful library sync
  (`LibrarySynced`), when a TMDb integration is added switched on or switched back on
  (`CatalogConnected`), when a user switches on a tab no other user had on (`TabEnabled`), when the
  newest releases of a source resolve to new works (`FeedRead`), and on the owner's **Recompute
  now** (`Requested`); a trigger that finds a run waiting joins it, and without a catalog nothing is
  queued. At most one run is queued and one running (`IX_RecommendationRuns_OneRunPerActiveStatus`).
  `GET /api/recommendations/runs/latest` gives every signed-in user the running run, else the newest
  by when it was queued, as `{ id, status, trigger, requestedByUserName, createdUtc, startedUtc,
  finishedUtc, seedCount, candidateCount, stage, stageDone, stageTotal, error }` — `status` `Queued`,
  `Running`, `Succeeded` or `Failed`, `seedCount` the library works it started from and
  `candidateCount` the works it recommends, both `null` unless it succeeded, `requestedByUserName`
  and `error` for the owner only —, or 204 with no body before the first run. A running run says how
  far it is, as Vasyl asked after a first computation of his library took 3 min 15 s: `stage` is
  `ReadingLibrary`, `FindingLibraryWorks`, `ReadingWorks`, `ReadingCollections`, `ReadingPeople`,
  `Discovering` or `Saving`, `null` when the run isn't running, and `stageDone` of `stageTotal` the
  stage's items done (240 of 815), both `null` for a stage without counts; the run row
  records them at each change of stage and at most every 2 seconds within one
  (`RecommendationRunProgressHttpTests.Recompute_writes_each_stage_in_order_with_its_counts_and_ends_with_none`,
  `RecommendationRunProgressHttpTests.Recompute_writes_a_stages_count_at_most_every_two_seconds_and_always_at_its_end`
  and `RecommendationRunProgressHttpTests.LatestRun_while_a_run_reads_the_catalog_shows_its_stage_and_counts_to_the_owner_and_a_member`).
  `POST /api/recommendations/runs` is owner-only (403 "Only
  the installation owner can do this." for anyone else) and answers 202 with the queued run, or with
  the one already waiting; 409 "Connect a metadata catalog under Integrations first." without a
  switched-on catalog of a type this version serves, and 503 "Too many recommendation runs are
  waiting. Try again in a minute." when the queue is full, the run kept as failed with that sentence.
  A run takes the first switched-on catalog by name, tests its connection before anything else, and
  asks it only for the kinds some user's tabs want — none at all when every tab of every user who can
  sign in is off. A failure of the catalog fails the run with the catalog's sentence, masked of the
  token, and keeps the previous recommendations; a run a restart interrupted fails with "The app
  restarted before these recommendations were computed. The previous ones are kept.", one that threw
  with "The recommendations stopped computing because of an error in the app. Its log says more.",
  and a queued one is queued again; the start queues none of its own. A run replaces every
  recommendation in one transaction at its end. The log gives ids and counts, never a title or an
  address.
- **Each recommendation says why it is there, in four lists ordered by a fixed score (MAM-33, ADR
  0023).** A run starts from the library's films and series the catalog knows — by their own TMDb or
  TMDb TV id, else, as a possible match, by their IMDb id or by the one result their title, cleaned
  of what a file name adds ("120. Landing Stripling" is "Landing Stripling"), and year match — and
  keeps each reason as a chip: **Collection**, a collection's released parts the library lacks
  ("Guardians of the Galaxy · 2 of 3"); **Director** and **Creator**, the released works of a person
  two or more of the library's films or shows are by ("Ridley Scott · 12 in your library");
  **Recommended**, what TMDb recommends beside the library's works, with up to five of their titles
  and how many of the library's catalog works recommend it, a film the library holds twice counting
  once ("Because you have X, Y and 3 more"); **MissingSeasons**, a held series' aired seasons it
  holds no episode of ("Missing seasons 4–5"), season 0 never, a partly held season never;
  **NewAtSource**, a work the app first saw among a release source's newest releases in the last two
  weeks ("New on Toloka · 2 days ago", dated by the source's publish date); and **TopRated**, for a
  tab with fewer than 10 of the library's works, TMDb's best-rated works of the tab among those at
  least 300 people rated, listed only once a release source carries one. A documentary goes to **Documentaries**, else an animation, anime included, to
  **Cartoons**, else a film to **Movies** and a show to **Series** (Vasyl's decision 2). Reasons of
  taste — Recommended, Director, Creator, TopRated — are kept only for a work whose rating holds when
  pulled toward 5.0 by 50 imagined votes (`votes × (rating − 6.0) ≥ 50`); the others whatever the
  rating. The score is `8·MissingSeasons + 6·Collection + 3·(Director or Creator) + 2·log2(1 +
  recommenders) + 1·NewAtSource + 0.5·TopRated` plus the shares of the library's works sharing a
  genre and the decade (0 to 1 each), plus `1.5·(weighted rating − 6)`, the weighted rating being
  `(votes × rating + 50 × 5.0) / (votes + 50)`; then votes, title and id break ties. The weights were
  set after the first computation on the reference library, whose first formula — recommenders one
  point each up to ten, a hundredth of the rating — put films rated around 6.2 on top of Movies and
  buried "2 of 3" collection parts at ranks 17, 51 and 98; now Guardians of the Galaxy Vol. 3 (7.9,
  "2 of 3" and its director), Back to the Future Part II and Blade Runner 2049 lead, and those parts
  stand at 1, 6 and 4. A work not released yet is never listed; a work the library holds — by its
  TMDb or IMDb id, or a work of its kind it matches by title and year — and one a scanned file's name
  claims by title and year are left out, a held series keeping only its missing seasons.
- **The catalog's answers are kept in tables of their own and read again after 30 days (MAM-33, ADR
  0025, ADR 0006).** Films and shows, genres, collections, people and the answers to "which work is
  this" — nothing found included — are stored with the TMDb integration as their source and the
  catalog's own record id (`movie/118340`, `tv/73586`), and asked again only when older than 30 days,
  so a run of an unchanged library makes a handful of calls: the connection test, and for a tab with
  few of the library's works the discovery pages of its best-rated works, which are asked every run.
  Nothing is written to the library's works, titles or identifiers, and a possible match is never
  shown on the Library page or used by a search.
- **The Recommendations page (MAM-33, ADR 0023).** **Recommendations** in the header, after
  **Library**, opens `/recommendations` for every signed-in user, and is hidden while all four of
  their tabs are off. Its tabs are the user's lists that are on, the tab in the address (`?tab=`); its
  filters are the reason, the genre, the decade, films or series in **Cartoons** and
  **Documentaries**, and **Available only**, each select counting its values over the whole tab; its
  **Sort** — **Best match** (the score), **Rating**, **Most votes**, **Newest**, **Oldest**, **Title
  A–Z** or **Most releases** — is kept in the address too (`&sort=`). A
  card shows the poster, the Ukrainian and the original title, year, genres, rating with its votes,
  the reasons as chips, what the release sources carry — "4 releases · up to 2160p · Ukr", "Not found
  (checked 3 days ago)" or "Not checked yet" — and **Downloading** while a download job of it is
  followed. **Releases** opens the Search page with the title, the year and a film's TMDb id
  (`/search?q=…&tmdbId=…`), from which the Download dialog sends it as any release; **Not
  interested** hides it for this user alone, with **Undo** (Vasyl's decision 3). Nothing is ever sent
  by itself. The page shows when the lists were computed — while a computation runs, its stage, how
  far through it is ("240 of 815") with a progress bar, and how long ago it started — and, to the
  owner, **Recompute now**,
  disabled with "Connect a metadata catalog under Integrations first." without a catalog; a member
  reads "The last computation failed. The owner can see why." for a failed one. TMDb's attribution
  closes the page.
  `GET /api/recommendations?tab=&reason=&available=&genreId=&decade=&kind=&sort=Score&page=1&pageSize=25`
  answers `{ items, totalCount, page, pageSize, facets: { reasons, genres, decades, kinds },
  catalogConnected, lastRun }`, each item `{ id, kind, catalogId, title, originalTitle, year, genres,
  rating, voteCount, posterUrl, reasons: [{ kind, name, held, total, titles, seasons, dateUtc }],
  availability: { checkedUtc, found, releaseCount, bestResolution, hasUkrainianAudio, maxSeeders },
  downloading, searchText, searchTmdbId }`, filtered, sorted and paged on the server (25 a page by
  default, at most 100), without asking the catalog or a source. `sort` is `Score` by default — the
  score, then votes, title and catalog id —, `Rating` (the weighted rating, so a 10.0 of three votes
  doesn't top the tab), `Votes`, `Newest` or `Oldest` (by release date, a work of no known date last),
  `Title` (in Ukrainian alphabetical order, as the library's works) or `Releases` (the availability
  check's release count: the works found first, then those not found, then those never checked),
  ties falling back to the score's order (`RecommendationListSortHttpTests.List_sorted_breaks_a_tie_by_the_score_then_votes_then_title_then_catalog_id`;
  one test a sort, and `RecommendationListSortHttpTests.List_with_a_sort_it_doesnt_take_is_400_with_why`). The facets count the caller's whole list of the tab before
  the filters, whatever the sort, the reasons in a card's order and the genres by name. 400 with the first broken rule's sentence, the value cut at 32
  characters: "Choose a tab: Movies, Series, Cartoons or Documentaries.", "“x” is not a recommendation
  tab. Use Movies, Series, Cartoons or Documentaries.", "“x” is not a recommendation reason. Use
  Collection, Director, Creator, Recommended, MissingSeasons, TopRated or NewAtSource.", "“x” is not
  an availability filter. Use true or false.", "“x” is not a genre id, which is a number such as
  18.", "“x” is not a decade. Use its first year, such as 1990.", "“x” is not a kind of work. Use
  Movie or Series.", "“x” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or
  Releases.", "Page must be 1 or more.", "Page size must be between 1 and 100." and "This tab
  is switched off in your profile.". Every user's download jobs count, matched to a work by its kind
  and an id or the title and year the release claims, for a held series by a missing season or none:
  a followed job (`Created`, `Submitted`, `Downloading`, `Paused`) shows **Downloading**, one that
  ended without failing (`Downloaded`, `NeedsLinking`, `Cancelled`) leaves the work out, even beside a
  followed one, and a failed one changes nothing. `PUT` and
  `DELETE /api/recommendations/dismissals/{Movie|Series}/{catalogId}` mark and unmark "Not
  interested" for the caller alone, 204 either way and with no audit row, or 400 "“x” is not a kind of
  work. Use Movie or Series." or "“x” is not a catalog id, which is a number such as 603.".
  `GET /api/recommendations/posters/{fileName}` answers a catalog work's poster, `image/jpeg` or
  `image/png` with `Cache-Control: private, max-age=2592000, immutable`: fetched once from TMDb's
  image host without any credential and kept in `<data>/posters/`, served from there with or without
  a catalog; a bare 404 for a name that is no poster's or no catalog work's, one the image host
  doesn't have, or one not kept while no catalog is connected; 502 when the catalog can't send it,
  with its masked sentence for the owner and "The metadata catalog couldn't send this poster." for
  everyone else.
- **The release sources are asked about recommended works within each user's daily limit (MAM-33,
  ADR 0024).** A background check runs as the app starts, then 15 minutes after each pass ends, and
  searches at most 4 recommended works a pass for the whole installation; it asks nothing while no
  release source is switched on, and needs no catalog. A work is due when it was never checked, or
  was checked a day or more ago and found, or 7 days or more ago and not found; one check per work is
  shared by every user. Each search serves one user's list and counts against that user's limit over
  the last 24 hours (Vasyl's decision 5), even when it fails: the limit is split evenly between their
  tabs that are on, the remainder to the first tabs (62 over four tabs is 16, 16, 15 and 15), the due
  tab furthest under its share goes first, and a tab at its share gets more only once no tab under its
  share has a work due. Users take turns, the one searched for least recently first, then by user
  name; within a tab the works never checked go first, then by score. A user's "Not interested" works
  and the works of a tab they have off are never due for them, and a limit of 0 searches nothing. A
  film is asked through the movie search with its year and a show through the TV search with none, by
  the original title when every letter of it is Latin, else by the catalog's Ukrainian title, and no
  id is sent; a release counts when it is of the work's kind and its title or description claims the
  work by its TMDb or IMDb id or by title and year — a pack "(S5) (2022-2024)" counts for the show
  begun in 2018 — and, for a held series, a missing season or no season. Per work the app keeps how
  many releases, the best resolution claimed, whether one claims Ukrainian audio, the most seeders and
  when; what the sources that answered listed is stored, and when none answered nothing is stored, the
  work stays due and the pass stops. These searches store no search and no release, and the log of
  them is forgotten after two days: the Search page asks again when you choose **Releases**.
- **New on Toloka: each release source's newest releases are read every 3 hours (MAM-33, ADR 0024,
  ADR 0004).** A read runs as the app starts, then 3 hours after each read ends, while a catalog is
  connected and some user has a tab on: each enabled source gets one general search with no text in
  the categories of the kinds the tabs want — Toloka answers its latest 90 — and each release is kept
  by its source and id (a listing without one is left out), updated when it is listed again, with no
  download link. Up to 100 releases not yet resolved, newest first, are looked up in the catalog by
  the titles in their description, then in their title, each with its year; a pack of a show's later
  seasons is looked up by its title alone and taken only when exactly one show of that title began by
  its first year. A failure of the catalog leaves the rest for the next read, and when any release
  turned out to be a work a computation is queued. A work first seen there in the last 14 days is
  recommended as "New on Toloka". A release no source listed for 30 days is forgotten, and the
  entries go with their source; the reads count against no user's limit.
- **Every user sets their own lists and daily limit in Profile (MAM-33, ADR 0024).** The Profile page
  gains a **Recommendations** card: **Movies**, **Series**, **Cartoons** and **Documentaries**, each
  on for a user who never saved (Vasyl's decision of 2026-10-05), the **Daily search limit** (60
  until changed, 0 for none) and "N searches in the last 24 hours.", with its own **Save**.
  `GET /api/profile/recommendations` answers `{ movies, series, cartoons, documentaries,
  dailySearchLimit, searchesLast24Hours }`; `PUT` takes the first five, every one required, and
  answers 200 with the settings, or 400 "Say whether the Movies tab is on." (and so for Series,
  Cartoons and Documentaries), "The daily search limit is a whole number from 0 to 500.", or for a
  body that isn't the settings "Send the settings as JSON: movies, series, cartoons and documentaries
  true or false, and dailySearchLimit a whole number from 0 to 500.". Each user reads and saves only
  their own, and a save writes no audit row. A save that switches on a tab no other enabled user has
  on — a user who never saved has every tab on — queues a computation in the saver's name
  (`TabEnabled`) or joins the one waiting; a full queue never fails the save.
- **The Search page takes a film's TMDb id in its address (MAM-33).**
  `/search?q=Alien (1979)&tmdbId=348` narrows the search it creates by that id, as the **TMDb id**
  field does; anything but digits is ignored.
- **Assigning a TMDb id is Jellyfin's Identify, and Remove link forgets only (MAM-34, Vasyl's
  decision of 2026-10-06, ADR 0026).** The first builds of this version wrote a link's ids into the
  item and then asked Jellyfin to look up what the item lacked; the ids changed nothing anyone could
  see, as 217 of the 220 items the reference installation had linked still showed the title their
  file name gave. Saving a link now makes Jellyfin do what the **Identify** button of its own web
  page does, with the same two calls and one read: `POST /Items/RemoteSearch/Movie` (a film, and an
  episode Jellyfin lists as a film) or `POST /Items/RemoteSearch/Series` (a show) with the id under
  Jellyfin's exact `Tmdb` or `Imdb` key, then `POST
  /Items/RemoteSearch/Apply/{id}?replaceAllImages=true` with the one row it found, then the item
  read again to confirm it. Jellyfin replaces the item's title, overview and images with TMDb's, in
  the metadata language of the item's library: that is the library's own setting and the app doesn't
  choose it — «Фільми» is Ukrainian on the reference installation, «Мульфільми» and «Документалки»
  have none set, so Jellyfin uses its server's default, English in the live check —, and a field
  locked in Jellyfin stays as locked: an item locked as a whole, or by its name or overview, is
  refused with the reason. A TMDb id goes only to the search of its own kind, as the other kind's
  search answers an unrelated work for it (a film search for the show's id 7869 answered “The
  Fest”). A library that saves artwork or metadata next to the media — an `Nfo` saver, "save artwork
  into media folders", trickplay or downloaded subtitles saved with the media, or options the app
  can't read — is refused before any search, so the app writes no file or folder and moves or
  renames nothing; the live check found the folder listings of three identified items identical
  before and after. Identify has no undo, and the app sends it only when the owner saves a link,
  retries one, accepts the confident suggestions or chooses **Update Jellyfin**. In Vasyl's live
  check of 2026-10-06 on Jellyfin 12.1.0, where he typed `y` for each apply, a film took 1.8 s, an
  episode through The Open Movie Database 21.3 s and The Penguins of Madagascar (3 seasons, 149
  episodes) 159.3 s, which is why the app never waits for an identification inside a request.
- **The owner links a library work to its TMDb film or show, and Jellyfin identifies the item as it
  (MAM-34, ADR 0026, ADR 0005).** Each card of the Library page gains **TMDb…**, the owner's (anyone
  else sees it disabled with "Only the owner can link works to TMDb."), which opens a dialog: the
  work's items with the ids Jellyfin reports and their links, the prepared suggestions, a field for
  a pasted link and a free search. Above **Save** it says what Jellyfin will replace — the item's
  title, overview and images —, that there is no undo and that files and folders are not touched.
  `GET /api/library/works/{id}/tmdb` answers it as `{ work, items, catalogConnected, suggestions,
  search, searchTitle, searchYear }`: `work` as the works list gives it; each item `{ linkId,
  sourceItemId, sourceName, itemTitle, itemKind, canWrite, cannotWriteReason, target, catalogKind,
  catalogId, catalogTitle, seasonNumber, episodeNumber, imdbId, writeState, writeError,
  writeStateUtc, setUtc, setByUserName, libraryIdentifiers, conflictingValue, episodeImdbId,
  identifiedUtc }`, `libraryIdentifiers` being what the last sync read for the item,
  `conflictingValue` a third TMDb id Jellyfin holds beside the link, `canWrite` whether Jellyfin
  would identify the item — it does for a film or a series item of a library whose adapter can,
  while the integration is switched on, and `cannotWriteReason` says why not — and `identifiedUtc`
  when the app last read the item showing the link's match, `null` for a link it never did, which is
  every link set under the first rules; `search` `{ searchedUtc, noneFitsUtc, pending, inPopulation
  }`; `searchTitle` and `searchYear` what the free search starts from — the work's title cleaned as
  the suggestions' searches clean it ("Landing Stripling" of "120. Landing Stripling", "The Penguins
  Of Madagascar" of a series named after its release's folder), and the year that title names in
  brackets (the first of a span, 2008 of "(2008-2015)") else the work's, as TMDb finds nothing by
  the raw title; a bare 404 for an unknown work. `PUT /api/library/works/{id}/tmdb` with `{
  catalogKind, catalogId, seasonNumber, episodeNumber, sourceItemIds }`, every field a string and
  `sourceItemIds` left out for every item of the work, links each item — one link per library item,
  as a work can hold several — to the TMDb film (`Movie`) or show (`Series`), or replaces its link,
  and answers 200 with the dialog at once, each item to be identified `Queued`: the request never
  waits for Jellyfin, and the dialog asks again every 2 seconds while an item waits or is being
  identified. A film item with a film and a series item with a show are linked as that work (`Work`)
  and identified by their TMDb id, a series' apply also refreshing its seasons and episodes: on The
  Penguins of Madagascar they kept their names, overviews and images, and the series its played
  state. A film item, or an episode of no series, with a show is an episode of it (`ShowEpisode`),
  optionally with a season (0–999) and an episode (1–9999), identified as the next bullet says. 400
  "Choose a TMDb film (Movie) or show (Series).", "“x” is not a TMDb kind. Use Movie or Series.",
  "Give the TMDb id to link.", "“x” is not a TMDb id.", "“x” is not a season number. Use 0 to 999.",
  "“x” is not an episode number. Use 1 to 9999.", "An episode number needs a season number.",
  "Season and episode numbers go only with a show linked to a film or to an episode.", "A show or an
  episode can be linked only to a TMDb show.", "Link its series instead: an episode is known by its
  series' ids." or "“x” is not an item of this work."; 404 "TMDb has no film 223021." (or "show");
  409 "Connect a metadata catalog under Integrations first.", "No library reports this work, so
  there is nothing to link." or, while one of its links is being identified, "A write to the library
  is under way for this work; try again in a moment."; 502 with TMDb's sentence, masked of the
  token, only when TMDb can't be read. A save that changes nothing records and queues nothing. Every
  link and its audit row (`LibraryLinkSet` or `LibraryLinkReplaced`, with the ids the item held and
  the ids linked) are saved first, in one transaction. Then a background worker identifies each
  queued link whose library can identify its kind of item and is switched on, the owner's own clicks
  before a batch's: the connection test, `GET /Users` for the first enabled administrator, `GET
  /Items/{id}?userId=` as that user, `GET /Library/VirtualFolders` for the options of the item's
  library, the search — one request, the id under its exact key, with the library's metadata
  language when it has one —, one apply carrying the row found, on a client of its own with a
  20-minute timeout, never sent a second time whatever becomes of its answer, and the same read of
  the item again. The identification is confirmed only when the item shows the row's name and year,
  holds its id under the identification's key, has a changed `Etag` or primary image and, when the
  row has an image, a primary image: an item that took only the ids, or nothing, is `NotWritten`
  with a sentence saying so, never "Written". An item that already shows the whole match — the name,
  the year when the row has one, the id, a primary image when the row has an image and, when the row
  has an overview, TMDb's — is marked identified with no apply at all. An apply that left the app is recorded (`LibraryItemIdentified`, with the
  ids before and after) whether or not it was confirmed. A link the owner sets or retries replaces
  the ids Jellyfin holds, as Jellyfin's own Identify does; one queued by **Accept all** or **Update
  Jellyfin** does not (below). A set, a replace or a removal queues a computation of the
  recommendations (`LinkChanged`). A series link takes its episodes' file identities from a sync
  when its work holds several series items of the library, as which episode is whose is known only
  there.
- **Each linked item says whether Jellyfin identified it (MAM-34, ADR 0026, ADR 0008).** A link's
  `writeState` is `KeptInApp` (an episode's link that has no id to identify it by, an episode item's
  link, or a library type that can't identify: "“x” can't identify items, so a link stays in the
  app."), which no sync changes, `Queued`, which no sync changes either, `Writing` (being
  identified), `NotWritten` with `writeError` (owner only), `Written` once the app confirmed the
  identification — `identifiedUtc` says when —, `InLibrary` once a sync read the link's id from
  Jellyfin, or `Lost` when a sync reads an identified link without it; the card and the dialog show
  "Kept in the app", "Waiting to identify", "Identifying…", "Not identified yet", "Identified in
  Jellyfin" and "Jellyfin lost it". A link set under the first rules, whose item may hold its id,
  shows "Not identified yet" until it is identified, on its Library card too: the card's `link` says
  so with two booleans, `identified` and `underWay`, and the page asks again while an identification
  runs. A switched-off Jellyfin is never asked: the link is `NotWritten` with "“Jellyfin” is
  switched off. Switch it on under Integrations, then retry.". Every other refusal or failure is
  `NotWritten` with its sentence, nothing identified: "Jellyfin's library “x” saves {what} next to
  the media, so identifying this item could change files in your media folders; the app left it
  alone. Switch that off in the library's settings in Jellyfin, then retry." (an `Nfo` saver,
  artwork and metadata, trickplay images or downloaded subtitles saved with the media), "The app
  couldn't tell what this item's Jellyfin library saves next to the media (…), so it left the item
  alone.", "This item is locked in Jellyfin (the whole item, its name or its overview), so Jellyfin
  wouldn't take the match. Unlock it there, then retry.", "Jellyfin found no film with TMDb id
  223021. Check the id, and that TheMovieDb is among this library's metadata sources in Jellyfin."
  (and the show's, and the Open Movie Database's, and "Jellyfin found more than one result for …, so
  the app picked none."), "Jellyfin took the match, but the item read again doesn't show it (its
  name, year or id): it may be locked, or its metadata sources failed. Its own log says why; Retry
  checks again.", and, when the answer to the apply was lost, late or a 5xx, "The match was sent to
  Jellyfin, but its answer never came, and the item read again doesn't show it. Jellyfin may still
  be working on it: Retry in a few minutes checks again before sending anything." (and "… didn't
  answer within 20 minutes …", "Jellyfin answered the match with …"); Jellyfin's own refusals say
  why: "Jellyfin refused to identify the item. Check that the API key belongs to an administrator."
  (403), "Jellyfin refused the match it was sent; nothing was changed." (400), "Jellyfin no longer
  has this item; sync the library and link it again." (404), "Jellyfin has no enabled administrator
  account to read the item as.", "Jellyfin's administrator account can't see this item (a parental
  limit, a blocked tag or no access to its library), so the app can't read it as that account." —
  told apart from an item that is gone by the user-less `GET /Items?ids=` —. The apply is sent at
  most once: a **Retry** a few minutes after a lost answer reads the item first, and marks it
  identified without a second apply when it shows the match; a series whose apply was sent less than
  10 minutes ago waits instead, with "Jellyfin may still be working on this series after the last
  attempt; try again after … UTC.", nothing asked of Jellyfin. A sync never erases a refusal: it
  judges only `Written`, `InLibrary` and `Lost` links, so a `NotWritten` link stays so, with its
  sentence, while its item holds the id. An item's **Retry**, **Identify again** and **Identify in
  Jellyfin** queue its link again with `POST /api/library/tmdb/links/{linkId}/write`; `POST
  /api/library/works/{id}/tmdb/write` does the same for every link of the work; each answers 200
  with the work's dialog, the items `Queued`, and each asks for a confirmation in the dialog first,
  as it replaces the item's title, overview and images and can't be undone. The owner's click lifts
  the protection a batch put on the link (below). 409 "There is nothing to write: no item of this
  work is linked.", "There is nothing to write: " with why a link stays in the app ("Without a
  season and an episode the app has no id of this episode to identify it by.", "TMDb knows no IMDb
  id of this episode, so its link stays in the app.", "Jellyfin identifies movie and series items
  only, so a link on an episode stays in the app." or "“x” can't identify items, so a link stays in
  the app."), "There is nothing to write: the library no longer reports this item.", the
  switched-off sentence, or the identification-under-way one; a bare 404 for an unknown work or
  link.
- **Removing a link forgets it in the app only (MAM-34, ADR 0026).** **Remove link** in the dialog,
  after a confirmation that says Jellyfin keeps what the identification gave the item: `DELETE
  /api/library/works/{id}/tmdb` deletes the work's links with `LibraryLinkRemoved` (the link's ids
  before, none after) and calls Jellyfin never, so it works while the integration is switched off or
  gone; 200 `{ dialog }`, 204 when the work has no link, 409 "A write to the library is under way
  for this work; try again in a moment." while one of its links is being identified, a bare 404 for
  an unknown work. A link still waiting in the queue is simply deleted, and the worker passes it
  over. Nothing is put back: the title, overview, images and ids the identification gave the item
  stay in Jellyfin — and the ids an earlier build that only wrote ids put there —, and replacing a
  link is a new identification of the new id. `DELETE /api/library/tmdb/links/{linkId}` removes one
  link the same way — the **Unattached** list's **Remove**, whose item Jellyfin no longer lists —:
  204, 409 as above, a bare 404 for an unknown link.
- **TMDb suggestions are prepared in the background after every sync (MAM-34, ADR 0026, ADR 0025).**
  A run (202 + run row + reconciler) starts after every successful library sync (`LibrarySynced`)
  while a catalog is connected, when a TMDb integration is added switched on or switched back on
  (`CatalogConnected`), and on the owner's **Prepare again** (`Requested`), which searches every work
  again; a trigger that finds a run waiting joins it, at most one is queued and one running
  (`IX_LibraryLinkSuggestionRuns_OneRunPerActiveStatus`). It searches TMDb for every film without a
  TMDb id, series without a TMDb TV id and episode of no series, none with a link: by its titles
  cleaned of what a file name adds and its folder's name, with its year and without, films and shows
  for a film or an episode, shows for a series; keeps ten candidates a work, each read through the
  catalog cache, and the search for 30 days unless the work's titles, folder or year change — one
  question asked once a run, however many works of a folder ask it —; and reads up to 50 identified
  works of each library so its genre profile is known. A catalog failure
  ends it with the catalog's masked sentence, keeping the works done; "The app restarted before these
  TMDb suggestions were prepared. The works searched so far are kept.", "The TMDb suggestions stopped
  being prepared because of an error in the app. Its log says more." and "The metadata catalog was
  removed while the TMDb suggestions were prepared. The works searched so far are kept." end the
  others. `GET /api/library/tmdb/runs/latest` answers `{ id, status, trigger, requestedByUserName,
  createdUtc, startedUtc, finishedUtc, worksTotal, worksDone, error }`, the running run else the
  newest, or 204 before the first; `POST /api/library/tmdb/runs` 202 with the run, 409 without a
  catalog, 503 "Too many TMDb suggestion runs are waiting. Try again in a minute.". The dialog shows
  the best five candidates, each `{ catalogKind, catalogId, title, originalTitle, year, overview,
  runtimeMinutes, posterUrl, imdbId, isConfident, evidence, seasonNumber, episodeNumber }`, its
  `evidence` computed when read: the title (how alike, exact, and which of TMDb's titles), the years
  apart, the runtime against the closest duration ffprobe measured of the work's versions ("6 min ≈
  6 min" when within 2 minutes or a tenth of TMDb's), the kind, the library's genre (animation or
  documentary, when 60% of a library's identified works, three or more, are) and a linked work in
  the same folder that shares its collection, a director or its show, which ranks first. A
  suggestion is **confident** when it is the one candidate of the work's kind with the work's title
  key and year, the only such, with a runtime that agrees (Vasyl's decision 8); it is always among
  the five. A show suggested for a film item comes with the season and episode its name gives
  (`S01E08`, `_E08`).
- **Paste a link or search TMDb yourself (MAM-34).**
  `GET /api/library/works/{id}/tmdb/lookup?text=&kind=` reads a TMDb film or show link
  (`themoviedb.org/movie/223021-landing-stripling`, `themoviedb.org/tv/1044/season/1/episode/8` with
  its season and episode), an IMDb link or id
  (`tt0056164`), or a bare TMDb number with `kind` `Movie` or `Series`, and answers `{ items }`, the
  film or show ranked for the work — both when an IMDb id names a film and a show. 400 "Paste a TMDb
  or IMDb link, an IMDb id like tt0056164, or a TMDb number.", "Choose film or show for a bare
  number." or "“x” is not a TMDb kind. Use Movie or Series."; 404 "TMDb has no film 223021." (or
  "show"), or "TMDb has no film or show with that IMDb id. If it is an episode's, paste the show's link
  instead.". `GET /api/library/works/{id}/tmdb/search?title=&year=` answers the first page of TMDb's
  films and shows of the kinds the work could be, of the year when given, ranked the same way; 400
  "Type a title to search for." or "“x” is not a year.". Both: 409 without a catalog, 502 with the
  catalog's masked sentence, a bare 404 for an unknown work; every film or show they show is stored
  as a catalog work, so its poster is served.
- **The TMDb page reviews the works without an id (MAM-34, ADR 0026).** **TMDb** in the header, right
  after **Library**, opens it for the installation owner — a member isn't offered the link, as every
  route behind the page answers them with a 403 — and the Library's link is marked only on
  `/library`, the TMDb one on `/library/tmdb`. "Review works without a TMDb id", beside the Library
  page's **Missing an id** filter, opens `/library/tmdb` too, with five tabs.
  **Review** walks the works one at a time with the dialog inline: 1–5 pick a suggestion, Enter
  saves, 0 marks that none fits, → skips, / goes to the search field — `GET
  /api/library/tmdb/queue?index=` answers `{ totalCount, index, current }`, the works without an id,
  a link or the owner's "none fits" by title, 400 "“x” is not a place in the queue. Use 0 or more.";
  `PUT` and `DELETE /api/library/works/{id}/tmdb/none-fits` set and clear the mark, 204, a bare 404
  for an unknown work. **Confident** lists every work with a confident suggestion, all ticked, with
  each library's numbers: `GET /api/library/tmdb/confident?locationId=&page=1&pageSize=25` answers
  `{ items: [{ work, candidate }], totalCount, page, pageSize, locations: [{ locationId,
  locationName, populationCount, confidentCount }] }`, `locations` the libraries the Library page's
  filter offers — those that hold a file, so not Jellyfin's box sets library —, works marked "none
  fits" left out; 400 "“x” is not a library id." or a page's sentence. **Link N works** sends `POST
  /api/library/tmdb/confident` with `{ items: [{ workId, catalogKind, catalogId }] }`, 1 to 100:
  each work is linked only while that is still its confident suggestion, computed again then, and
  202 `{ batchId, items: [{ workId, outcome, reason, links }] }` says what became of each —
  `Linked`, or `Skipped` with "It is no longer the work's confident suggestion.", "You marked that
  none fits.", "It was linked meanwhile.", "Its library reports a TMDb id for it now.", "The work is
  no longer in the library." or the catalog's or the save's own sentence; 400 "Tick at least one
  work to link.", "At most 100 works can be linked at once.", "Each ticked work needs its work, its
  kind and its TMDb id.", "Give the work to link.", "“x” is not a work id." or "“x” is ticked
  twice."; 409 without a catalog. Each work's links are saved as it is done and the worker woken
  then, so a request the browser cancels halfway still has the works saved before it queued and
  identified. Its links are saved `Queued`, each with its audit row, and a background worker
  identifies them — two films or episodes at a time, a series alone, a link the owner queued from a
  dialog before one a batch queued —, each with its own audit rows in the name of whoever queued it;
  the page polls `GET /api/library/tmdb/batches/{batchId}` (a bare 404 for an unknown click) every 2
  seconds until none is waiting or being identified. That answer holds only the linked works, as
  only links are stored: each row keeps its skip reason from the click's own answer, a link is
  followed by its id whichever work a sync files it under, and a click that linked no work is never
  asked for. A confident show of a series is queued and identified like a film, in the series' own
  lane. At a start an identification a restart cut is `NotWritten` with "The app restarted while
  identifying the item in Jellyfin. Jellyfin may still be working on it: Retry in a few minutes
  checks what it holds before anything is sent.", a link the rules keep in the app that a restart
  caught claimed goes back to `KeptInApp`, and the queued ones are identified. A batch whose
  identifications fail to confirm twice in a row is stopped, as the catch-up's is (below). The
  worker counts each link by where its identification left it, and a database failing under it ends
  its pass with the links still queued rather than stopping the app. **Not identified** lists the
  attached links Jellyfin or the app refused or failed to identify, each with its reason and its
  work's **TMDb…** to Retry, to link another film or show, or to remove the link. **Conflicts** and
  **Unattached** list the links whose item Jellyfin gives another TMDb id — or a film id given after
  the owner linked the item as an episode of a show, which the card flags too and the
  recommendations seed by neither — and those whose item is gone, the latter with **Remove**: `GET
  /api/library/tmdb/links?filter=&page=1&pageSize=25`, `filter` `Unattached`, `Pending`, `Conflict`,
  `NotWritten` or `Lost`, answers `{ items, totalCount, page, pageSize }`, newest first, each `{
  linkId, workId, workTitle, sourceName, sourceItemId, itemTitle, target, catalogKind, catalogId,
  catalogTitle, seasonNumber, episodeNumber, writeState, writeError, setUtc, setByUserName,
  conflictingValue, identifiedUtc }`; `NotWritten` lists attached links only, as an unattached one
  has no work to open; 400 "Choose which links to list: Unattached, Pending, Conflict, NotWritten or
  Lost." or "“x” is not a link filter. Use Unattached, Pending, Conflict, NotWritten or Lost.". A
  status line shows how the suggestions were prepared, with **Prepare suggestions** (**Prepare
  again** once they have been), disabled with the reason while a preparation is under way.
- **An episode Jellyfin lists as a film is identified through The Open Movie Database by its own
  IMDb id (MAM-34, Vasyl's decision of 2026-10-06, ADR 0026, ADR 0005).** A film item's link to an
  episode of a show that names its season and episode is identified by the episode's own IMDb id,
  which TMDb gives (`GET
  /3/tv/{id}/season/{season}/episode/{episode}?append_to_response=external_ids`; each dialog item
  carries it as `episodeImdbId`): `POST /Items/RemoteSearch/Movie` with the id under the `Imdb` key
  and the provider "The Open Movie Database", then the same apply. No TMDb id is ever sent for it,
  as on a movie item it names a film, so the library needs The Open Movie Database among its movie
  metadata downloaders. On "Greatest Events of World War II in Colour.S01E01.Blitzkrieg" Vasyl's
  live check corrected the name and the year ("Blitzkrieg", 2019), kept the overview, as the row has
  none, and replaced the image by a 1920x1080 one — probably a frame of the video, not the 960x1440
  poster; it took 21.3 s. Without both numbers the link stays in the app with "Without a season and
  an episode the app has no id of this episode to identify it by."; for an episode TMDb gives no
  IMDb id, with "TMDb knows no IMDb id of this episode, so its link stays in the app." (both as the
  item's `cannotWriteReason`). An item holding another IMDb id — typically its show's — is refused
  when a batch queued the link, with the held-id sentence below, and identified anyway by the
  owner's own save or **Retry**; a Save whose episode TMDb can't be asked about is 502 with TMDb's
  sentence, nothing saved. A sync judges an episode's link by that IMDb id, and keeps reading a film
  id the item holds as giving way to the link. An episode item no series holds keeps its link in the
  app: "Jellyfin identifies movie and series items only, so a link on an episode stays in the app."
  Saved again with the same season and episode once TMDb gives that episode an IMDb id, a link kept
  for having none is queued. Nothing but reads, the film and series searches and the apply is ever
  sent to Jellyfin about a link: no refresh, no item update, no image, no lock
  (`LibraryItemIdentifierIdentifiesOnlyTests`).
- **Update Jellyfin repairs the links set before: it identifies them, on the owner's click (MAM-34,
  Vasyl's decision of 2026-10-06, ADR 0026).** `GET /api/library/tmdb/catch-up` answers `{
  toIdentify, progress }`: `toIdentify` the attached links, in a library switched on, that Jellyfin
  would identify and whose item the app never read showing the link's match (`identifiedUtc` empty)
  — every such link set before the upgrade, kept in the app under the first rules or not; a
  `NotWritten` one only when no apply was ever sent for it —, and `progress` `{ batchId, total, done }` for the batch under way, or
  `null`. `POST` queues them under one new batch (202, the same shape: what it queued and its batch,
  `progress` `null` when nothing waited), and the background worker identifies each — one apply per
  item, none for an item that already shows the match —, every apply with its own audit row in the
  name of whoever set the link; the progress survives a reload, as the batch's id is kept on the links,
  and the links list's `Pending` filter follows them. Nothing happens without the click, and the
  page asks to confirm it first. A batch is protected: an item that holds a TMDb or IMDb id other
  than its link's — or an IMDb id when TMDb gives the link none to compare it with — is refused with
  "Jellyfin holds IMDb id … for this item, not …, and the app doesn't replace a library's own ids on
  its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
  nothing sent, and **Retry** is the owner's override; the same protects **Accept all**. A link
  refused or failed before anything was sent is taken again by the next click; a link whose apply
  was sent and not confirmed is not, as a second apply would delete the item's images again — it
  waits for **Retry**, and the **Not identified** list shows it; so does one a restart cut while it
  was identifying (Jellyfin goes on with that apply after the app is gone, and the audit row is
  written only after its answer). A batch whose identifications fail
  to confirm twice in a row is stopped: its queued links are `NotWritten` with "The app stopped this
  batch after Jellyfin failed to confirm two identifications in a row, and sent nothing for this
  item. Check Jellyfin, then Retry, or press Update Jellyfin again.". Both endpoints are the owner's
  alone, by where their controller lives.
- **Every TMDb link endpoint is the owner's, reads included (MAM-34, ADR 0026).** The controllers
  live in `Controllers/Library/Links`, and `LibraryLinksAreOwnerOnlyConvention` gates every action
  there: 403 "Only the installation owner can do this." for anyone else.
  `LibraryLinksAreOwnerOnlyHttpTests` sends each of them as a member and without a token.
- **An audit row names the ids it changed (MAM-34, ADR 0006, ADR 0026).** `AuditAction` gains
  `LibraryLinkSet`, `LibraryLinkReplaced`, `LibraryLinkRemoved` and `LibraryItemIdentified` (23), on
  a target of kind `LibraryLink` named after the work's title (200 characters at most, cut with
  "…"), each with the ids before and after in `AuditEventIdentifiers`. `LibraryItemIdentified` is
  written once for every apply that left the app, confirmed or not, with the ids the item held
  before and after — or, when the item couldn't be read again, the ids the identification meant to
  leave —, in the name of whoever set or queued the link; an item already showing the match, a
  refusal, a search that found no match and a failure before the apply write none, and no title,
  overview or image is recorded. `LibraryIdsWritten`, `LibraryIdsRestored` and
  `LibraryMetadataLookupRequested` are history: the first builds of this version wrote them, nothing
  writes them now, and the values stay so old rows read.
- **The Library sorts its works, and its sort says "Sort by" (MAM-8, Vasyl's request).** A **Sort by**
  select stands beside the Library page's filters, at the right, with **Title A–Z** (as before),
  **Title Z–A**, **Year, newest first**, **Year, oldest first**, **Largest on disk** and **Recently
  added**. `GET /api/library/works?sort=` takes `Title` (when blank), `TitleDescending`, `Newest`,
  `Oldest`, `Largest` or `Added`, by name in any letter case; `Newest` and `Oldest` go by the year the
  card shows, a work with none last either way; `Largest` by the sizes its files report added up, a
  series by every episode's, a work with no file or no size last; `Added` by when the app first saw the
  work, a series by its latest episode, so a new season brings the show up. Every order falls back to
  the title order, then the id, so pages never repeat or drop a work. 400 `{ error }` "“x” is not a
  sort. Use Title, TitleDescending, Newest, Oldest, Largest or Added." for another value, a number
  included. The sort is no filter: **Clear filters** leaves it, a new one starts at page 1, and the
  episodes a season lists keep their season and number order. `LibraryWorksSortHttpTests`,
  `LibraryPage.sort.test.tsx` and `SortSelect.test.tsx` pin it.

- **Refresh all runs the three steps one after another (MAM-36, Vasyl's decision of 2026-10-06).** One
  button in the Library page's Sources card, the owner's (anyone else sees it disabled with "Only the
  owner can refresh the library."), starts a sync of every enabled library the app can read, then a
  scan of the media folders, then an inspection of the files, and starts each only once the one before
  it has ended; the inspection goes last because it reads the files the syncs and the scan stored. It
  runs from the page, on the endpoints that start each step on its own, and waits on the sources the
  page already polls. A sync refused because it is already under way is joined, not a failure; any
  other refusal, or a run that ends Failed, stops the chain at that step with a red line under the bar
  ("Refreshing stopped. The sync of Home Jellyfin failed.") and starts no step after it. Without an
  allowed media folder only the syncs run, without a readable enabled library only the scan and the
  inspection, and with neither the button is disabled ("Connect a media library or choose a media
  folder first."). The button says the step it is on while it runs ("Syncing libraries…", "Scanning
  folders…", "Inspecting files…"). Leaving the page stops the chain after the step under way — that
  run goes on in the API, which queues an inspection after a sync and after a scan by itself — and
  opening the page again doesn't resume it. A chain that survives a closed tab would need an endpoint
  of its own, which is not built. No endpoint or table changed. `LibrarySyncPanel.refreshAll.test.tsx`
  pins it.

- **Each Library and Search card shows the work's own poster from Jellyfin (MAM-35, ADR 0027,
  Vasyl's decisions of 2026-10-06).** Beside the title block of every card on the Library page, and
  under **In your library** on the Search page, sits the picture Jellyfin holds for the work's item —
  its Primary image —, in a box the size of a recommendation's poster: a series' card shows the
  series' picture, the episodes of an opened season show none, and an episode of no series shows its
  own. A picture of another shape, such as a frame of the video, is shown whole, and a film or TV icon
  stands in while it loads, when Jellyfin has none and when it can't be fetched. It is never the
  Recommendations page's TMDb poster; for an item Jellyfin identified through TMDb it is the image
  Jellyfin took from TMDb then. Each sync stores the tag Jellyfin gives each item's own picture, never
  its series' or season's, as a fact of its work with its source (`MediaWorkPosters`): replaced when
  the picture changes, removed when it goes and purged with the integration. Every work the API gives
  as `GET /api/library/works` does — a season's episodes, the Search page's library works and the
  owner's TMDb dialog's work included — carries `posterUrl` (after `link`):
  `/api/library/posters/{itemId}-{tag}.jpg` for the item whose title the card shows, else for the
  next item that has a picture, or `null`. `GET /api/library/posters/{fileName}`, every signed-in
  user's, answers `image/jpeg` with `Cache-Control: private, max-age=2592000, immutable`, and only for
  an item id and tag a sync stored: from `<data>/library-posters/`, or fetched once from the Jellyfin
  that reported it — `GET /Items/{id}/Images/Primary?tag=…&format=Jpg&maxWidth=342&quality=90`, the
  key in the header, about 35 KB — and kept; a bare 404 for a name that is no poster's or that no
  library reported, for one not kept while its library is switched off, for any of a removed
  library's, kept or not, as its rows go with it, and for an item Jellyfin has no picture of or no
  longer knows; 502 when Jellyfin couldn't send it, with its sentence, masked of the key, for the
  owner and "The media library couldn't send this poster." for everyone else, nothing kept. The page
  fetches each picture with the user's token when its card comes within about a screen of the window
  and shows it as a `data:` URL; a finished sync, an inspection or **Refresh all** doesn't fetch it
  again.
  `LibraryPostersHttpTests`, `LibrarySnapshotPostersHttpTests`, `LibraryWorksPostersHttpTests`,
  `SearchesLibraryPostersHttpTests`, `JellyfinAdapterPostersTests`, `LibraryPoster.test.tsx` and
  `LibraryWorkCard.poster.test.tsx` pin it.

- **The library's file names are grouped into the four duplicate classes of the specification
  (MAM-20, ADR 0028, Vasyl's decisions of 2026-10-07).** Worked out on every read from what the
  syncs, the scan, the inspection, its hashing and the owner's TMDb links stored, nothing stored of
  its own: the names of one file by the device and inode the app read (`SameFile`); files of two
  identities or more with one SHA-256, each with all its names (`ByteIdentical`); film and episode
  works of every library and integration that share a registry key — an IMDb id whatever the kind,
  a TMDb or TVDB id within the work's kind, an episode's series' ids with its season and episode,
  and an episode-to-show link with its numbers, so a film item linked as S01E01 and the library's
  own S01E01 are one work — and hold two different files in two different copies (`SameWork`); and
  works, and names only the scan found, that share a title key with its own year and that no kind,
  id or episode place keeps apart (`Possible`), a scanned name read by its file name, then its
  folder's name, and never when either names seasons or episodes. The 65 id-less files of one item
  make no group, and no copy of a work is called redundant. Works whose ids disagree stay one group,
  flagged for review with the disagreement (`IdentifierConflict` with its scheme, `ofSeries` for two
  shows, `EpisodeNumbers` for one episode's ids at two places); copies whose edition words — sixteen,
  English and Ukrainian, read from the file name and the library's version name, the words of the
  work's own titles left out — or whose measured dynamic ranges differ are flagged `Edition` or
  `DynamicRange`; a possible match always needs review and says whether the measured durations agree
  (2 minutes or 10 %). (MAM-37 below changes this: two episodes of one show at different places
  are never one group, so `EpisodeNumbers` is gone, and copies whose names say different editions
  are no copies of each other, so `Edition` flags a possible match only.) A file a torrent seeds is
  classed like any other, and no download client is asked (ADR 0018).
  `GET /api/library/duplicates?class=&page=1&pageSize=25` answers `{ items, totalCount, page,
  pageSize }`, each group `{ key, class, needsReview, distinctions: [{ kind, scheme, ofSeries }],
  evidence: { identity: { deviceId, inode, linkCount, nameCount }, sha256, identifiers: [{ scheme,
  value, seasonNumber, episodeNumber }], titles: [{ text, year }], durationsAgree }, files: [{ file,
  readSizeBytes, deviceId, inode, linkCount, sha256, editions, works: [{ id, kind, title, year,
  episode, seriesTitle, versionId, editionLabel, sourceName, identifiers }] }], space, recoverable
  }`, by class in that order, then by the title the Library card of the group's first work shows
  (an episode's group by its series, season and number since MAM-21, below), then by `key` — 32 hex
  characters that name the group on every read while what makes it one stays, for the Duplicates
  page (MAM-21) to address it by —; each class fills its part of `evidence`. A
  `file` is the Library's own (`appPath`, `pathStatus`, `inspection`…), `space` counts each file once
  by its identity, never with a library's size, and `recoverable` `{ sizeBytes, gap }` is `0` for a
  same-file group, `null` for the copies of a work and for a possible match, and for a byte-identical
  group every copy's size but the largest's only where each copy's file system, read from Linux's
  mount table by device, is known not to share blocks and its link count is the names the app knows;
  else no size, with `gap` `SharingFileSystem` (btrfs, ZFS, XFS…), `UnknownFileSystem` (a network
  share, an overlay, a device the table doesn't name, and always on Windows) or `UnlistedNames`, the
  first that applies in that order. `class` is `SameFile`, `ByteIdentical`, `SameWork` or `Possible`
  in any letter case, never a number; 400 "Page must be 1 or more." or "Page size must be between 1
  and 100." (checked first), or "“x” is not a duplicate class. Use SameFile, ByteIdentical, SameWork
  or Possible.", the value cut to 32 characters and "…"; a page past the end is empty with the total.
  Every signed-in user reads it, like the Library; a file's `pathProblem` and inspection `problem`
  and the name of who set a linked id are the owner's, `null` for everyone else. It writes no row and
  no audit event, and removes nothing; the Duplicates page shows it (MAM-21, below).
  `LibraryDuplicatesHttpTests`, `LibraryDuplicatesSameFileHttpTests`,
  `LibraryDuplicatesByteIdenticalHttpTests`, `LibraryDuplicatesSameWorkHttpTests`,
  `LibraryDuplicatesPossibleHttpTests`, `RegistryKeysTests`, `EditionLabelsTests`,
  `FileSystemSharingTests` and `DurationAgreementTests` pin it.
- **The file inventory names the file system a device holds (MAM-20, ADR 0028).**
  `IFileInventory.DescribeFileSystemsAsync` answers each device asked, in order, with the type the
  system's mount table gives it: on Linux `/proc/self/mountinfo`, read once per call, the device
  found by its `major:minor` (the inverse of glibc's `makedev`), its type after the line's ` - `;
  `null` for a device no line names or whose lines disagree, for every device when the table can't be
  read, and on any other platform. It reads no library file, so it takes no allowed root, and never
  throws. The Duplicates list asks it only for the devices of byte-identical groups, and only when
  the list asked for is of every class or of `ByteIdentical` — never of decided matches (MAM-21,
  below). `MountTableTests`,
  `LocalFileInventoryFileSystemLinuxTests` (Linux only) and
  `LibraryDuplicatesByteIdenticalHttpTests.List_asks_the_file_system_only_of_byte_identical_groups`
  pin it.
- **The Duplicates page shows each group with the evidence behind it (MAM-21, ADR 0028, ADR 0029,
  Vasyl's decisions of 2026-10-07).** **Duplicates** in the header, after **TMDb**, opens
  `/library/duplicates` for every signed-in user, and so does **Duplicates** at the right of the
  Library page's header; there the header marks the Duplicates link alone. Its underline tabs —
  **All**, **Same file**, **Byte-identical**, **Same work**, **Possible** and, last, **Dismissed** —
  are taken by a click or the arrow keys, the current one brought into view when the row is wider
  than a phone, and kept in the address (`?tab=SameFile`, left out for All, one it doesn't know read
  as All). A class's tab asks the server for that class, **Dismissed** for `decision=Dismissed` and
  **All** for every group but the dismissed ones, paged on the server with the Library's pager, the
  page size kept per device and another tab starting at page 1. A card is headed by its first work by
  title in Ukrainian order — an episode by its series, with "Season 3 · Episode 31" and its own title
  under it —, else by the title and year a possible match offers or its first file's name, with its
  class, **Needs review** while it needs review and is undecided, **Confirmed** or **Dismissed** once
  decided, and what tells its files apart as warning badges ("TMDb ids differ", "Their shows' TVDB
  ids differ", "Episode numbers differ", "Editions differ", "Dynamic ranges differ"; MAM-37 below
  drops "Episode numbers differ", leaves "Editions differ" to possible matches and adds "Running
  times differ"). The server
  lists the groups in the order of those headings: by class, then by the title the card is headed by
  — an episode's group by its series' title, then its season (the specials after the numbered
  seasons, a place without a season last) and its number —, then by key; ordered by the episode's
  own title, as MAM-20 had it, one show's episode groups came in no order the page shows and two
  shows' came between each other. Under the badges come the evidence its class gives — the one
  file's device, inode and the names the file system counts; the SHA-256; the ids that make the
  works one, a show's with its season and episode; the title and year they share and whether the
  running times agree, differ or aren't measured —, the room its files take with the names not
  measured yet ("2 names not measured yet" alone when none is, never "0 files · 0 B"), and the room
  removing all but one copy would free only where the server estimates it: "Removing a name frees
  nothing while the file has another." for one file's names, "Removing all but one copy would free
  about 4.2 GiB." for byte-identical copies, or the reason there is none; nothing for the copies of
  a work or a possible match. Its files are the Library's version rows, or its comparison table for
  two to six (the same Rows/Table switch and choice, rows always below 48rem). Each file is named, in
  its row and in the table's column header, by the end of its path that tells it from the group's
  other files: its file name when no other file has it, else the nearest folder that differs with
  the folders between left out — "hardlinks/Arrival (2016) 2160p.mkv" beside "Arrival (2016)/Arrival
  (2016) 2160p.mkv", "tv-4k/…/S01E01.mkv" beside "tv/…/S01E01.mkv" —, every folder kept where
  leaving them out would make two read alike; its chips go on the line under the name. An open row
  adds its name over its folder, its identity or "Not looked at yet", its SHA-256 or "Not hashed",
  the editions its names give and the works that hold it, each with its kind, series and place,
  version name, library and ids, or "No library reports this file; the folder scan found it."; the
  table has Folder, Identity, SHA-256, Editions and Held by as rows. A refresh that fails keeps the
  cards under a banner with **Retry**, a first load that fails shows the server's sentence with
  **Try again** where asking again can help, and each tab says when it has nothing. Nothing on the
  page removes, moves or renames a file, and nothing acts on several groups at once. `VersionRows`
  and `VersionTable` show any version-like `{ id, editionLabel, files }` and take optional details,
  rows, a label and a caption, the Library's cards unchanged without them; the tabs are a shared
  `PageTabs`, the TMDb and Recommendations pages keeping their own. `DuplicatesPage.test.tsx`,
  `DuplicateGroupCard.test.tsx`, `DuplicateGroupCard.files.test.tsx`, `PageTabs.test.tsx`,
  `duplicates.test.ts`, `duplicates-api.test.ts`, `nav-items.test.ts`, `AppLayout.test.tsx`,
  `App.test.tsx`, `LibraryPage.test.tsx` and
  `LibraryDuplicatesHttpTests.List_orders_episode_groups_by_their_series_then_season_and_episode`
  pin it.
- **The owner confirms or dismisses a possible match, kept by its group's key with an audit row
  (MAM-21, ADR 0029, Vasyl's decisions of 2026-10-07).** An undecided possible match's card offers
  the owner **Confirm** and **Dismiss**, and a decided one **Undo**; anyone else sees them disabled
  with "Only the owner confirms or dismisses a possible match." and never reaches the server. A
  confirmed match stays where it was, marked **Confirmed**, with who confirmed it for the owner and
  when for everyone, and "It stays a possible match: nothing is removed because of it."; a dismissed
  one leaves the list for **Dismissed**, and a short dashed row takes its card's place, "“Arrival
  (2016)” is dismissed: it is listed under Dismissed now." with **Undo**, which takes the keyboard
  focus, as **Not interested** leaves a recommendation. On **Dismissed**, **Undo** on a card leaves
  "“Arrival (2016)” is back among the possible matches." with **Dismiss again**. Either way back puts
  the card back where it was, and the rows stay until the tab or the page changes; a row names the
  group by its card's heading with the film's year or the episode's place. The button pressed says
  "Confirming…", "Dismissing…" or "Undoing…", and a failure is a line in the card or the row. A
  Confirm, Dismiss or Undo answered by a bare 404 — the group's members changed since the page
  loaded, so its key names no group — makes the page ask for the list again, and the card or the row
  says "This group has changed since the page loaded: the list shows it again as it is now."
  `GET /api/library/duplicates` takes `decision` — `Confirmed` or `Dismissed` by name, in any letter
  case, never a number —, checked after the page and the class (400 "“x” is not a decision. Use
  Confirmed or Dismissed.", the value cut to 32 characters and "…"): without it every group but the
  dismissed ones, on every class and in the totals; with it only the possible matches decided so,
  and no file system is asked, whatever the class, as a possible match shows no estimate. Each group
  gains `decision`, last: `null` while undecided and for every other class, else `{ decision,
  decidedUtc, decidedByUserName }`, the name the owner's only; `needsReview` is unchanged.
  `PUT /api/library/duplicates/{key}/decision` with `{ "decision": "Confirmed" }` or `"Dismissed"`,
  and `DELETE` the same, are the installation owner's (403 "Only the installation owner can do
  this." for anyone else) and answer 204, also for the same decision again and an undo of none,
  which write nothing. The decision is checked first: 400 "A decision is needed: Confirmed or
  Dismissed.", "“x” is not a decision. Use Confirmed or Dismissed." or, for a body that isn't JSON
  with a decision as text, "The body must be JSON with a decision, such as { "decision": "Confirmed"
  }."; then a key that isn't 32 lowercase hex characters, or that no group has now, is a bare 404 —
  a possible match's key is made of its works and the files no library reports, so another work, or
  a scanned file no library reports, joining or leaving gives it another key that asks again, while
  another file or version of a work already in it changes nothing —, and a group of another class
  is 400 "Only a possible match is confirmed or dismissed: this group is one by its files' identity,
  bytes or registry ids, which no one has to confirm." A decision is a row of
  `DuplicateMatchDecisions`, one per group key, with who decided last and when: a change rewrites it,
  an undo deletes it, and a row whose key no group has now stays, matching nothing. `AuditAction`
  gains `DuplicateMatchConfirmed` (24), `DuplicateMatchDismissed` (25) and
  `DuplicateMatchDecisionUndone` (26), and `AuditTargetKind` `DuplicateMatch` (6): the target's id is
  the key read as a Guid, its name the first title and year the group was offered by, "Arrival
  (2016)". Two first decisions racing are settled by the key's unique index, the second applied to
  the row the first wrote. Nothing is written to the library, a file or a download client.
  `LibraryDuplicateDecisionsHttpTests`, `LibraryDuplicatesHttpTests`,
  `LibraryDuplicatesByteIdenticalHttpTests.List_asks_the_file_system_only_of_byte_identical_groups`,
  `DuplicateMatchDecisionPersistenceTests`, `AuditTrailTests`, `LibraryWritesAreOwnerOnlyHttpTests`,
  `ControllerAccessDeclarationTests`, `DuplicateGroupCard.decisions.test.tsx` and
  `DuplicatesPage.decisions.test.tsx` pin it.
- **Phase B's acceptance bar holds on the reference installation, and the README and the release
  note say what was tried live and what wasn't (MAM-22, Vasyl's decisions of 2026-10-08).** Checked
  on 2026-10-08 on production rather than a copy beside it, the published 0.2.1 image (digest
  `sha256:d9c6149a3dbf0e4924ff0d889873996008425dc3b9c2592c56f373fb07cfa78e`), against Jellyfin
  12.2.0, with ffprobe 6.1.1-3ubuntu5; Vasyl signed in himself. **Refresh all** (MAM-36) went
  through "Syncing libraries…", "Scanning folders…" and "Inspecting files…" with no "Refreshing
  stopped" line: the sync read 1,360 items into 1,291 works and 7 series, 1,384
  versions and 1,380 files in 5.8 s, the scan found 1,397 files, 17 in no library and no problem, in
  1.6 s, and two inspections ran, not three: the one the sync queued, and the one the scan queued,
  still waiting when Refresh all's own inspection step joined it. Each checked 1,397 files, ran
  ffprobe on none and had no file to hash. The space added up to the byte: the 1,380 files of
  Jellyfin's libraries took 7,974,593,521,562 B (7.25 TiB), the size the app read equal to
  Jellyfin's for every one, and the 17 only the scan found — 12 shorts, 4 Blu-ray streams and an ISO
  image, the one file ffprobe couldn't read — 93,092,661,375 B (86.7 GiB), together
  8,067,686,182,937 B, the bar's "1,397 files · 7.34 TiB"; `GET /api/library/sources` counted as
  many names as files in every row. Each of the 1,397 files had a link count of 1 and no two shared
  a size, so `FileHashes` held no row and neither inspection read a file whole.
  `GET /api/library/duplicates` then listed 46 groups, all of class `SameWork` and the same 46 keys
  as before the refresh, 34 of them needing review: 13 "Editions differ" (Alien (1979) Theatrical
  and Director's Cut, Terminator 2's three files, three Tom & Jerry shorts with a Widescreen file
  beside the plain one…), 20 pairs of The Penguins of Madagascar episodes with "TVDB ids differ" and
  "Episode numbers differ", and one Mickey Mouse Clubhouse episode at two places; 12, among them 8
  of that show's episodes held in two halves, had no flag. Two groups are each one work by the ids
  the library gives, not by their titles, and are recorded as they are:
  `[01] Blade Runner (1982) [The Final Cut]` with `[02] Blade Runner 2049 (2017)`, whose item
  Jellyfin gives the 1982 film's IMDb tt0083658 and TMDb 78, flagged only through The Final Cut, and
  «Ніж у воді» (1962), the film with a 26.7 min featurette under IMDb tt0056291 and TMDb 11502, not
  flagged, as the class compared no running times then. (MAM-37 below changes this list: of these
  46 groups it keeps 4, 1 needing review, «Ніж у воді»'s, flagged "Running times differ"; Blade
  Runner's left by the years its file names claim, the Penguins of Madagascar pairs and the episode
  at two places as two episodes at different places, the 8 episodes held in two halves as an
  episode's "a" and "b" files, and the other 12 "Editions differ" groups as different editions.)
  Ghostbusters of 1984 and 2016, RoboCop of 1987 and 2014, the two «Апгрейд»
  (Upgrade, 2018, and Upgraded, 2024), Planet Earth's Deserts, Jungles and Mountains of 2006 and
  2016, and the Penguins of Madagascar film of 2014 and the series stayed apart, and no possible
  match was offered at all, so nothing was confirmed or dismissed:
  `LibraryDuplicateDecisionsHttpTests`, `LibraryWritesAreOwnerOnlyHttpTests` and the 2026-10-07
  browser check against a throwaway database stand for **Confirm**, **Dismiss**, **Undo** and a
  member's view. The Duplicates page showed the 46 groups on two pages of 25, the empty tabs'
  sentences, a card's "One work by its ids: …" and an open file row's "Device 2081 · inode 164632817
  · 1 link" and "Not hashed", and no control that deletes, moves or renames a file; at desktop
  width, 375px and 320px the page didn't scroll sideways, only its tab row did, and at phone width
  the files showed as rows. The header search "Alien (1979)" showed «Чужий» (1979) and «Чужий³»
  (1992) under **In your library** with Jellyfin's own posters. `tools/mam22/report.py`, new and
  read-only, read a copy of production's database and the container's mounts before and after: none
  of the 1,397 names changed its library size, read size, modification time, device, inode or link
  count, none came or went, no audit row was added and the TMDb links stayed as they were.
  Production mounts `/media` read-write, not `:ro`, so that comparison, with the app opening files
  read-only, is what shows no file changed. Not tried live yet: a file with two names and two files
  with the same bytes, which the reference installation doesn't have, and with them the space
  counted once per file, the hashing, the `SameFile` and `ByteIdentical` classes and the space
  estimate; the "Dynamic ranges differ" flag; **Confirm**, **Dismiss** and **Undo** on production
  and a member's view of a decision; a read-only `/media`; and Identify with, and a poster fetched
  from, Jellyfin 12.2.0; the other tickets' open items stay open. The README (**Tested with**) and
  the release note list both; the README's and the roadmap's MAM-19, MAM-20, MAM-21 and MAM-35
  lines no longer say MAM-22 does the live check or that the Search page's library cards weren't
  opened live, and the README's **What it will do** no longer lists the duplicates as still to come.
- **Copies of one work whose running times differ widely are flagged "Running times differ" for
  review (MAM-37, ADR 0028, Vasyl's decision 1 of 2026-10-08).** MAM-22 found «Ніж у воді» (1962),
  the film beside a 26.7 min featurette under its IMDb and TMDb ids, as one same-work group with no
  flag. A `SameWork` group now gains the distinction `RunningTimes`, and so `needsReview`, when its
  shortest measured copy runs under three quarters of its longest, decided in whole milliseconds as
  4 × shortest < 3 × longest, so exactly three quarters doesn't flag. A copy is what one version
  holds, as the class counts copies: its running time is its files' measured durations summed, so a
  film in two parts runs as long as both; two libraries' versions of one path, or of one hardlinked
  file, are one copy, counted once; a copy with any file never inspected, or whose reading failed,
  has no running time and decides nothing, and fewer than two copies with one give no distinction.
  It comes beside any other distinction, never instead of one, and last in kind order, after
  `IdentifierConflict` and `DynamicRange`; running times split no group and join none.
  `DuplicateDistinctionKind` gains `RunningTimes` (5), written by name, and each group's `evidence`
  gains `runningTimes`, last: `{ shortestMilliseconds, longestMilliseconds }`, the shortest and the
  longest measured copy whenever two copies of a same-work group have a running time, flagged or
  not, and `null` otherwise and for every other class; each part MAM-37 splits a work's copies into
  (Changed, below) has its own. A possible match keeps `durationsAgree` (2 minutes or 10 %) and gets
  no `RunningTimes`, and the same-file and byte-identical classes are unchanged; registry ids stay
  the only join, nothing is written, and no copy is called redundant. On the Duplicates page the
  warning badge reads "Running times differ", after the others, and a flagged group's card says
  under "One work by its ids:" "The shortest copy runs 26:41, under three quarters of the longest,
  1:35:32.", both durations as the Library page shows a file's; a group the rule doesn't flag shows
  no sentence. It needs no migration and nothing switched on: it reads the durations the inspection
  already measured. Rejected with decision 1: the possible matches' 2 minutes or 10 %, and "more
  than 40 minutes apart", which no episode or short could reach. Checked on 2026-10-08, with the
  whole of MAM-37, on a copy of production's database, not on production, which keeps running the
  published 0.2.1 image until Vasyl publishes (his decision 5 that day): `tools/mam37/copy_db.py`,
  new and the owner's to run, which Vasyl ran at 11:31:03 UTC, copied `mam.db` (30,281,728 B), its
  `-wal` (7,321,272 B) and `-shm` (32,768 B), never `.secrets`, in `tools/mam22/report.py`'s one ssh
  call, which only reads, from the container running the published image
  (`vahac/media-acquisition-manager:latest`, digest
  `sha256:d9c6149a3dbf0e4924ff0d889873996008425dc3b9c2592c56f373fb07cfa78e`) since 10:19:47 UTC:
  `quick_check` ok, 20 migrations, the last `20261007173129_AddDuplicateMatchDecisions`, and 1,397
  names, 1,396 inspected and one ffprobe couldn't read, 1,396 with a measured duration. In the copy
  alone it removed the allowed folder `/media`, without which the local start would have queued an
  inspection that, `/media` being no path on Windows, writes every name "outside the allowed
  folders" and drops the durations, and pointed the four integrations at `http://127.0.0.1:9`. The
  MAM-37 working tree on commit 27fcd79 ran on the copy with `dotnet run -c Release`, logging "No
  migrations were applied", and Vasyl signed in himself. `GET /api/library/duplicates` as the owner
  listed 4 groups, all `SameWork`, each under the key it had in MAM-22's list that morning and each
  with `evidence.runningTimes`: The Gods Must Be Crazy (1980), two WEB releases of 6,545,205 and
  6,545,206 ms; «Ніж у воді» (1962), the film's 5,731,959 ms and the featurette's 1,600,699 (27.9
  %), flagged `RunningTimes` and needing review, the only group that does; Seksmisja (1984),
  "Seksmisja (1984).mp4" and "Seksmisja [1983 DVDRemux].mkv", 6,980,120 and 7,005,013 ms; and Alien:
  Romulus (2024), a 2160p remux of 7,141,927 ms and a 1080p rip of 7,140,926. No group of another
  class was listed, and none was dismissed. The Duplicates page said "1–4 of 4": «Ніж у воді»'s card
  "Same work", "Needs review", "Running times differ" and "The shortest copy runs 26:41, under three
  quarters of the longest, 1:35:32.", the other three "Same work" alone, again after a reload; at
  desktop width and 375px it didn't scroll sideways, the server logged no error, and the browser's
  console had only the 401s of the session's refresh, before the sign-in and after the reload. Vasyl
  looked at the page himself and found it fine. `copy_db.py --verify`, at the start and after the
  check, found the 14 tables the duplicates are read from as copied, and the copy was then deleted.
  Earlier that day the first version of this change had listed production's 46 groups, and the
  second, after decision 2, 17 of films and shorts; decisions 3 and 4 (Changed, below) came from
  those lists. Production's groups have no copy in two parts and no copy unmeasured or whose reading
  failed beside a measured one, so those rest on the tests; production shows all of MAM-37 once
  Vasyl publishes and deploys an image that holds it. `RunningTimeSpreadTests`,
  `LibraryDuplicatesRunningTimesHttpTests`,
  `LibraryDuplicatesSameWorkHttpTests.List_gives_each_segment_group_its_own_running_times`,
  `LibraryDuplicatesHttpTests.List_writes_classes_distinctions_and_editions_by_name`,
  `LibraryDuplicatesPossibleHttpTests.List_gives_a_possible_match_no_running_times_distinction_whatever_its_durations`
  and `DuplicateGroupCard.test.tsx` pin it.
- **Phase C's replacement is decided before any code can move a file (MAM-23, ADR 0030 to ADR 0033,
  Vasyl's decisions of 2026-10-08).** Specification §7 questions 3, 4 and 6 are answered for
  replacement; nothing of it is built, and nothing in this release writes to the media folders. A
  replacement will be the installation owner's alone and off until the owner mounts the media
  folders read-write, runs the app as their owner (`APP_USER_ID`, `APP_GROUP_ID`), switches it on in
  the app and ticks the library folders it may touch. It may target a film, one or more episodes or
  the held episodes of whole seasons. A Toloka release, which lists no ids, may be scheduled on the
  owner's confirmation of the work, but the old copy moves only once Jellyfin has given the new file
  the old copy's own work by its ids — never on the owner's word after the download —, each old copy
  paired with one new file and every unclear pair left to the owner; a `.torrent` that would land on
  an existing file, and a magnet, are never sent as a replacement. The old copy and its own
  companion files then move by one atomic rename per name that never replaces and never copies
  (`renameat2(RENAME_NOREPLACE)`: .NET's `File.Move` copies and deletes when a rename fails) into
  `.mam-quarantine` at the top of the allowed folder, which neither Jellyfin nor the app's folder
  scan reads, and stay there until the owner restores them or deletes them for good after a second
  confirmation; there is no automatic cleanup, and where the move can't be atomic nothing is
  deleted, a departure from §3.4. The app never writes to a download client: an old copy any torrent
  lists — 823 of the 1394 library video files on 2026-10-04 — waits until the owner removes that
  torrent in qBittorrent without its files, since moving a seeding torrent's only copy goes
  unnoticed by qBittorrent until a restart stops the torrent, and a recheck would then download the
  old copy back into the library. Jellyfin is only asked to look at the moved paths.
  `tools/mam23/facts.py`, new and read-only, measured the media host: every file and all but four
  folders (root's) belong to qBittorrent's user, 1000 and group 100, and the app's uid 10001 may
  write in none of them, so for replacement the app will run as that owner, and no folder's owner or
  mode changes. `docker-compose.yml` now runs the app as `APP_USER_ID`:`APP_GROUP_ID`, 10001 for
  both unless `.env` says otherwise, so nothing changes by default; `.env.example` says how to hand
  the data volume to other ids once and what that costs. ROADMAP's Phase C now lists MAM-23 to
  MAM-28; the specification, the README and the compose file's comments say the same.
- **A download whose files would land on a different file already in the destination's folder is
  refused before anything reaches the client (MAM-38, ADR 0034, Vasyl's decisions of 2026-10-08).**
  qBittorrent 5.2.4 keeps every file it finds at a new torrent's paths, and libtorrent 2.0 checks it
  and downloads the pieces that don't match into it, in place: another release under a name already
  in the library is written over, and stays a mix of both if the swarm stalls. On the reference
  installation every category saves straight into a Jellyfin library folder, and the app can't stop
  a torrent once sent. Right before the one submission, after the client is asked whether it already
  has the torrent, the sender now reads the .torrent's list of files — v1, v2 or hybrid, pad files
  left out, each name cleaned as libtorrent 2.0's `sanitize_append_path_element` cleans it on Linux,
  ported byte for byte: a broken UTF-8 sequence one `_` for the length its lead byte claims, a name
  past 240 bytes ended after the character that reaches them with an extension from its last 9
  bytes, `.`, `..` and an empty path element `_`, the torrent's folder `_` for an empty name and the
  v1 info hash for one cleaning drops — and works out every place each file may be written below the
  category's folder under qBittorrent's three content layouts, Original, Subfolder and NoSubfolder,
  as the app never reads `app/preferences`; then it looks there inside the Media folders. It refuses
  the send when a place holds a file of another size; a file of the same size whose sampled pieces —
  up to three wholly inside it, the first, one in the middle and the last, hashed with SHA-1 for v1
  or as a SHA-256 tree for v2, a chunk at a time so a piece never sits in memory whole — don't hash
  as the torrent says; something that isn't a file; a file it can't read, or one that changed while
  it was compared; or a place a torrent the client already has in the category will write. Such a
  torrent's files end up below its save path, so it is compared there by its content or its name,
  and, for at most 20 torrents that may write one of the places, by its files — one still in
  qBittorrent's folder for incomplete torrents included. The job is still written first (ADR 0013)
  and ends `Failed` with the app's sentence, which everyone who sent the release reads, and nothing
  is handed to the client: "“qBittorrent” would write over “Film.2001.mkv” in the folder of the
  category “Movies”: a file of another size is already there, so nothing was sent.", the file named
  by its path below the folder, with how many more of the release's files land on a file already
  there, each counted once. Files already there with the same bytes don't stop it, so a Toloka
  update that re-adds a series' old episodes still goes through, and a folder that is only the same
  folder passes. What can't be looked at is sent as before and said so: a magnet, a .torrent whose
  list the app can't read (a piece longer than libtorrent takes, or a lone file with no usable name,
  among them), a destination folder with no path in the app or outside the Media folders, a place
  behind a link that leads out of them, a file name with a character the app's paths refuse (DEL or
  a C1 control, which libtorrent keeps), and a client that can't tell its torrents in the category
  or their files, or holds one still fetching its metadata. A place that can't be looked at never
  hides one that refuses the send, and a check that breaks on a bug ends the job `Failed` with "The
  app couldn't check whether this release would write over a file in the folder of the category
  “Movies”, so nothing was sent. The app's log says why.", nothing sent. Each job gains
  `overwriteCheck`, written by name: `Checked`, `NotCheckedMagnet`, `NotCheckedFileList`,
  `NotCheckedFolderUnknown`, `NotCheckedFolderOutsideMediaFolders`, `NotCheckedClientJobs` or
  `NotCheckedFileName`, and `null` for a job sent before the check existed or one it refused; the
  Jobs page and a job's own page show an unchecked job's reason as an amber line under its status,
  such as "Not checked for files it would write over: a magnet link names its files only once the
  client has fetched them.", but not on a failed job. The new
  `GET /api/downloads/overwrite-check?destinationId=` answers every signed-in user
  `{ notCheckedReason }`: `null`, `NotCheckedFolderUnknown` (no path in the app, or a client that is
  switched off or can't say where the category saves) or `NotCheckedFolderOutsideMediaFolders`,
  never a path; 400 "Pick the destination to send it to." and 404 "The destination you picked no
  longer exists. Reload the page and pick one again.". The Download dialog asks it whenever a
  destination is picked, holds **Send** until that first answer arrives ("Checking the destination's
  folder…" on hover), never after a failed question, and, for a folder that can't be checked, warns
  above the summary that a release sent there goes without the check, the warning read with **Send**
  by a screen reader; its summary now reads "Kept: this version of the app never replaces or removes
  a file, and doesn't send a release it finds would write over a different one." The check only
  reads — a few pieces of a file already there, the client's torrents in the category and the files
  of those that may write there —, and it suggests nothing about the file it found: the client's one
  write is still the submission. It needs migration `AddDownloadJobOverwriteCheck` (Notes for
  upgraders, below) and nothing switched on. Not tried against qBittorrent or the reference
  installation: what qBittorrent and libtorrent do with a file already there was read from their
  source, and the check rests on the tests. `DownloadsSendOverwriteHttpTests` (another size, the
  same size with other bytes, a file the app can't read or that changed while it was compared, a
  lone file in a folder of its name, something that isn't a file, several collisions and one file
  colliding under two layouts counted once, a collision found beside a place that can't be looked
  at, a torrent qBittorrent already has — its file, a file in its folder, one still in the folder
  for incomplete torrents, one listed after more than 20 others —, the member's sentence, the check
  after "already has this torrent", a check that throws, the same bytes sent as checked for v1 with
  pad files and for v2, a folder that is only the same folder, each unchecked case, and no write to
  qBittorrent but the sign-in and the one add), `DownloadsOverwriteCheckHttpTests` (the dialog's
  question, a member's included, and a client that can't list its categories or no longer lists the
  destination's), `TorrentContentReaderTests`, `TorrentTargetPathsTests`, `TorrentPathElementTests`,
  `TorrentPieceHashTests`, `BencodeParserTests`, `DownloadRulesTests`,
  `LocalFileInventoryPieceHashTests`, `QBittorrentAdapterJobsTests`, `DownloadDialog.test.tsx` and
  `JobsPage.test.tsx` pin it.

### Changed

- **A film filed in TV/Anime or TV/Documentary is a movie, and the movie search finds it (MAM-33, ADR
  0020, ADR 0004).** Toloka files films beside series in TV/Anime (5070) and TV/Documentary (5080). A
  release listed there and in no other TV category is TV only when its title or its description names
  seasons or episodes — "S1-5", "Сезон 1", "E1-31 of 37", "серії 1-4 з 12" — and a movie otherwise
  (Vasyl's decision 6), and the movie search asks those two categories too
  (`cat=2000,2020,2040,2070,5070,5080` for Toloka). So "Akira (1988)" or a documentary film filed
  there is listed as a movie and matches the film you have by title and year, while "Death Note
  (E1-31 of 37)" stays TV. A marker of episodes alone now also ends a TV release's title, as a season
  marker does, so "Death Note (E1-31 of 37) (2006)" is read as "Death Note" and matches the series in
  your library, and TMDb's, by its title. 0.2.0's rule, a release is TV "when its source lists it in a
  TV category", now holds for every TV category but those two; a listing in no category keeps it.
- **A release shows its source's own title under its own (MAM-33, ADR 0004, ADR 0006).** Toloka's
  topic title, Ukrainian first, comes in each listing's `<description>`, where Prowlarr's title keeps
  the English part alone. Each release now keeps it (`Releases.Description`) when it says more than the
  title, and `POST /api/releases/search` and `GET /api/searches/{id}/releases` answer it as
  `description`, after `title` and before `kind`, `null` when there is none. The Search page shows it
  under the release's title and in **Details**.
- **The contracts learn the catalog and the newest releases (MAM-33, ADR 0005).** `IntegrationKind`
  gains `Metadata` and `ProvenanceSourceKind` `Metadata`; `IMetadataResolver` is implemented for the
  first time, by the TMDb adapter, beside the new `IMetadataCatalog` (a work's details with credits
  and recommendations, a collection, a person's works, search, find by IMDb id, discovery by genre,
  genres and posters). `ReleaseSearchMode` gains `Latest`, planned by `ReleaseSearchPlanning.ForLatest`
  and sent as `t=search` with no `q`; `ReleaseSearchHit` gains `Description`.
- **The Integrations list's kind filter takes Metadata (MAM-33).** An unknown kind is 400 "“x” is not
  a kind of integration. Use MediaLibrary, ReleaseSearch, DownloadClient or Metadata.".
- **Pages are wider on a desktop (Vasyl's choice of 2026-10-05).** Every page's column is at most
  80rem (1,280 px) wide, its padding included, instead of 64rem (1,024 px). The Recommendations page's
  cards are at least 30rem wide instead of 24rem, so a desktop shows two a row, never three, and a
  window narrower than about 1,020 px one.
- **The media library contract learns to identify an item, as an optional capability (MAM-34, ADR
  0005, ADR 0026).** `IMediaLibraryItemIdentifier` is a further interface of a library's adapter,
  found by the registry like a kind contract and required of none: `IdentifyAsync` takes an item,
  its kind and the one id the identification is by — a film's TMDb id, a show's TMDb TV id, or an
  episode's IMDb id on a film item, and no other pair, so an id never reaches the search of the
  other kind — and answers the ids before and after, whether anything was sent, and what became of
  it; `IdentifiableKinds` names the kinds of item the library identifies and
  `UnidentifiableKindReason` says why not the others, so the app keeps such a link in the app
  because the library says so, not by checking which product it is. It replaces
  `IMediaLibraryIdentifierWriter`, with its edit rules and its metadata lookup. Jellyfin's adapter
  implements it for films and series, never an episode item; its apply goes out on a named client of
  its own, `jellyfin-identify`, with a 20-minute timeout, chosen by an optional client name on
  `HttpIntegrationAdapter`, while every other call keeps the shared client's 15 seconds; its class
  summary no longer says it only reads. `IMetadataCatalog` gains `GetEpisodeAsync`, an episode of a
  show with its own IMDb id. `ProvenanceSourceKind` gains `Person`, `AuditTargetKind` `LibraryLink`,
  `RecommendationRunTrigger` `LinkChanged`, and `CatalogWork` a runtime, an overview and its other
  titles, with a show's IMDb id read too.
- **The Library list counts a linked id as the library's own (MAM-34, ADR 0026, ADR 0006).** `GET
  /api/library/works` gives each identifier `sourceKind` — `MediaLibrary`, `Person` for an id the
  owner set, `Metadata` for the IMDb id TMDb gave the link (only where Jellyfin gives none) — and
  `setUtc`; a linked id's `sourceName` is the name of who set it for the owner and `null` for
  everyone else. Each work gains `link` (`null` without one) `{ target, catalogKind, catalogId,
  catalogTitle, seasonNumber, episodeNumber, imdbId, setByUserName, setUtc, writeState, writeError,
  conflictingValue, identified, underWay }`, the worst write state of its items' links,
  `setByUserName` and `writeError` for the owner only, `identified` whether the app confirmed every
  item of the work whose id Jellyfin holds — the card says "Identified in Jellyfin", else "Not
  identified yet" — and `underWay` whether any item's link waits or is being identified, which makes
  the page ask again while one does; the two booleans are everyone's. A linked work misses no id:
  `missingIdentifiers` is `[]` and `identifiers=Missing` leaves it out, and the episodes of a linked
  series too (Vasyl's decision 9). `search` finds a work by its linked id and by the IMDb id TMDb
  gave. The id a link replaces is hidden until Jellyfin holds the link; a third TMDb id Jellyfin
  gives is shown beside it as a conflict. The cards show a linked id once whatever its sources, its
  tooltip saying who reported it and who set it when, the link's state, an episode link as "Episode
  of Планета Земля · S1 E8", and "TMDb ids disagree: 1234 in Jellyfin, 223021 linked" for a
  conflict.
- **A search and its releases match a linked id (MAM-34, ADR 0012).** `POST /api/searches` and
  `GET /api/searches/{id}` count a work's linked ids like its library ids: a linked id finds the work
  and matches a release that gives it, `matchedIdentifiers` items gain `sourceKind` and `setUtc` and
  say "set by" on the page, and a linked id beside another library id of its scheme is flagged as a
  conflict. The id a link replaces matches nothing until Jellyfin holds the link.
- **A sync re-applies the owner's links (MAM-34, ADR 0008).** A sync groups a linked item with its
  linked id, so it joins the work of the library's other items of that id, and a third value keeps
  it alone; it stores nothing of the link as Jellyfin's. A link follows its item to whatever work it
  lands on, stays unattached when the item is gone, and, when Jellyfin re-created the item under a
  new id, moves to it by the device, inode and size of its files — after the next file inspection,
  or at the following sync —, a series by its episodes' files, never when two items or two links
  claim one file. The sync judges a link by the id its identification sets — a film's TMDb id, a
  show's TMDb TV id, an episode's IMDb id — and marks a link `InLibrary` once Jellyfin reports it
  and `Lost` once it reports an identified link without it; a link kept in the app stays so whatever
  Jellyfin reports, and so does a `NotWritten` one, with its sentence, even when its item holds the
  id, as the app never identified it; the id it replaced giving way. A sync never changes when a
  link was identified, but a link it moves to an item Jellyfin re-created is not identified again,
  as the new item is a fresh one. A successful sync also queues a preparation of the TMDb
  suggestions.
- **Recommendations seed by a link (MAM-34, ADR 0023).** A work with a link seeds and is held by its
  linked id, and is never looked up by title; a work whose link and library disagree seeds nothing
  and holds both ids; an episode link holds and seeds its show, its season numbers counting as held.
- **TMDb's details carry a runtime, an overview, other titles and a show's IMDb id (MAM-34, ADR
  0025).** `GET /3/movie/{id}` and `/3/tv/{id}` append `alternative_titles,external_ids`: a film's
  `runtime`, a show's first `episode_run_time`, else its latest episode's runtime (Planet Earth's is
  `[]` and 53), the overview cut to 2,000 characters, at most 50 other titles cleaned and each once,
  and a show's `external_ids.imdb_id`. `CatalogWorks` keeps them, and details stored before this
  version are read again when next needed (`DetailsVersion`). The poster endpoint serves the
  candidates the TMDb dialog shows, as each is stored as a catalog work.
- **The order of the Search and Recommendations lists stands beside the filters, at the right, under
  "Sort by" (MAM-8, Vasyl's request).** The order was a select with no label, left among the filters,
  that read like one more filter — and, as one of the items of the row they wrap in, it could drop onto a
  line of its own between the filters and the list or its empty state, tearing the filters and **Clear
  filters** apart. It now stands outside that row (`FilterBar`), after a sort icon and **Sort by**, as
  the Library's does, and the filters wrap among themselves; under 48rem it goes above the filters
  instead of beside them, so it is never between them and their answer. The Library's search field
  gives up a little width so the filters, the **Review works without a TMDb id** button and the order
  meet on one line on a wide window. The Search page's label was "Sort". One shared `SortSelect` makes all three,
  and its select keeps its own accessible name, "Sort by".

- **The Library page's Sources card is one status bar, and its rows are under Details (MAM-36, Vasyl's
  pick of 2026-10-06).** The card was about 480 px of three rows, each of three or four sentences with
  a button of its own, and the same numbers three times; the works started below the first screen. It
  is now one bar: an item for each media library, for the media folders and for the file inspection,
  each with an icon (done, running, failed, never run) and when it last ran — "syncing 600 of 1,365",
  "scanning… 800 files", "inspecting 700 of 1,396" while it runs —, amber chips for what needs a look
  (files without an app path, files in no library, folders not scanned, files that couldn't be read),
  and at the end what all the files take ("1,396 files · 7.33 TiB"). **Details** opens the rows as
  they were — the sentences and the buttons of each library, of Media folders and of the inspection —
  under the card's description, in place of the bar, and **Hide details** brings the bar back; it is
  closed to begin with, on every visit. What a state of its own says stays in view with the details
  closed: loading, a failed load or refresh, the sentence about a missing media library, and a
  stopped Refresh all. Without an allowed media folder the bar says so, with the way to Media folders
  for the owner. On a phone the items stack and the buttons go under them, as icons. The heading
  "Sources" stays, for the page's outline and a screen reader. No API changed.
  `LibrarySyncPanel.bar.test.tsx` pins the bar; the other panel and page tests open Details first.

- **The media library contract reads an item's poster, and a sync asks Jellyfin for each item's
  image tag (MAM-35, ADR 0005, ADR 0008, ADR 0027).** `IMediaLibraryProvider` gains `GetPosterAsync`,
  which every library provider implements — reading is what one does, so it is no optional
  capability, and `IMediaLibraryItemIdentifier` still names no image —, answering a `LibraryPoster`,
  a JPEG at most 342 pixels wide, or `NotFound`; `LibraryItem` gains `PosterTag`. The sync's lists
  ask Jellyfin for `enableImages=true&enableImageTypes=Primary&imageTypeLimit=1` instead of
  `enableImages=false`, so each page carries one tag more per item, while an Identify's item lookup
  keeps `enableImages=false`; the sync still only reads. On the Library card the poster and the title
  block share a row of their own: the badges and actions keep the header's right edge, going under
  both on a phone, and the versions, table and seasons keep the card's whole width. The box the
  recommendations' and the TMDb dialog's posters sit in is one shared component now (`PosterBox`,
  class `.poster-box` in `poster-box.css`, replacing `.catalog-poster` and `catalog-poster.css`), and
  looks as before.

- **A file no library reported keeps a work out of the recommendations by its folder's name on a
  Windows-style path too (MAM-20, MAM-33, ADR 0028).** The reading of a scanned file's name — its
  file name without the extension, then its folder's name, each a title with its year — moved from
  the recommendations to Core (`FileNameClaims`), so the Duplicates list's possible matches read a
  name the same way. It moved with one fix: a backslash separates folders as a slash does, so a path
  like `D:\Movies\Aliens (1986)\aliens.mkv` now claims "Aliens" of 1986 by its folder, where before
  the whole path was read as one name and claimed nothing. A path with slashes, as the scan stores
  on Linux, reads as before.
  `RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_a_scan_found_files_name_claims` and
  `FileNameClaimsTests.Read_claims_by_the_folders_name_when_the_file_name_has_no_year` pin it.
- **The header makes room for Duplicates (MAM-21).** With **Duplicates** among the links, for
  everyone, the centre column is at most 52rem instead of 42rem, the links are icons with their
  titles below 76rem instead of 64rem, the brand is the logo alone below 53rem instead of 48rem, and
  the search field takes a row of its own below 560px instead of 480px, so the field keeps room to
  type beside one more link. At 360px and narrower the links sit with no gap between them and
  slimmer padding, each 30px wide and 28px tall, so the owner's five icons stay on one line at 320px
  and the header stays 97px tall, under its 7rem scroll padding; before, Jobs wrapped onto a line of
  its own at 339px and narrower. Measured in Chrome on 2026-10-07 at 1280, 1217, 1216, 1024, 849,
  848, 768, 600, 561, 560, 375, 340 and 320px: labelled, TMDb is 81px and Duplicates 107px, and the
  owner's five links take 549px, 196px as icons; the owner's search field is 227px at 1217px and
  271px from about 1261px. `AppLayout.test.tsx` and `nav-items.test.ts` pin the links and their
  order: Library, the owner's TMDb, Duplicates, Recommendations, Jobs.
- **The Duplicates page lists only real copies of one film or episode (MAM-37, ADR 0028, Vasyl's
  decisions 2 to 4 of 2026-10-08).** MAM-20 made one same-work group of the works sharing a registry
  key and flagged what told their copies apart; on production that listed 46 groups, 42 of them no
  copies of each other. Two episodes of one show at different places are never one work now,
  whatever ids they share (decision 2, after the first list, as their file names show different
  episodes of one season), which reverses MAM-20's decision 1 of 2026-10-07 for episodes; films keep
  it, one IMDb id with two TMDb ids still one work flagged "TMDb ids differ". A show is every id and
  title one place names, so a show the library knows by no id, as The Penguins of Madagascar was, is
  one show by its title; the works are joined in id order, a join refused where two components sit
  at different places of a show both hold or either sits at two places of one, and a film item whose
  ids reach two such places joins neither, so no chain of ids brings two places together; a work the
  library itself holds at two places of a show is no group; and two such episodes with one title and
  year are never offered as a possible match. Copies of one episode under two shows' ids stay one
  group flagged for their shows' ids ("Their shows' TVDB ids differ") — at one place when the shows
  share an id or a title, at any places when they share neither.
  `DuplicateDistinctionKind.EpisodeNumbers` (2) is removed, and with it "Episode numbers differ";
  the other kinds keep their numbers, and every kind is written by name. An episode's files whose
  names glue different segment letters to their first `SxxEyy` are no copies of each other either
  (decision 2): `Mickey Mouse Clubhouse_S03E09a` and `_S03E09b`, which Jellyfin reads as one episode
  and the sync makes one work of two versions, make no group, and an "a" file is a copy of another
  "a" file only, never of a "b" or of a whole episode. The letter is a lowercase a to d standing
  alone after the first marker (`EpisodeSegments`); a range ("S03E09a-b"), a capital letter and a
  word glued to the number are read as none. A film's copies whose file names claim years more than
  one apart are no copies (decision 3, that Blade Runner 2049 must not be among the duplicates at
  all, by the year rule put to Vasyl for it): a year counts only alone in round or square brackets,
  1880 to 2099 (`FileNameYear`) — never a bare year ("Blade Runner 2049"), a list, a span or a year
  sharing its bracket with a word ("[1983 DVDRemux]") —, and a name claiming two different years
  claims none. Years each within one of the next are one film's; with two runs of years or more each
  run is a part, and the copies claiming none one more. So `[01] Blade Runner (1982) [The Final
  Cut]` and `[02] Blade Runner 2049 (2017)`, which Jellyfin holds as one movie of two versions under
  the 1982 film's ids, are no group, while "Seksmisja (1984).mp4" and "Seksmisja [1983
  DVDRemux].mkv" stay one. Films only: in an episode's name a bracketed year is its show's or its
  season's. And copies whose names say different editions are not duplicates (decision 4, on Rambo:
  Last Blood's Theatrical and Extended Cut, which are different versions), which reverses
  specification §3.3's reading, in its MAM-20 note, that different editions are "distinctions
  needing review", and MAM-20's `Edition` flag for this class:
  the whole set of editions all of a copy's names say decides — Theatrical, Extended, Director's
  Cut, Special Edition, Unrated, Assembly, Open Matte, Widescreen, Remastered, Final Cut… —, a copy
  that says none beside one that says one being another edition; and a hardlinked file's names say
  one thing between them, so a file one library names by its cut and another by none is that cut.
  `Edition` now flags a possible match only, which keeps "Editions differ" as evidence for the
  owner, and the same-file and byte-identical classes are untouched, as their bytes are one whatever
  their names say. The copies are split by their letters, then (films only) by their years, then by
  their editions, each part a group with two copies and two files or more; its key adds `|Segments:`
  when its copies carry letters, then `|Years:` and `|Editions:` when those parted something, so a
  group with none keeps the key it had. On production's database (checked as Added, above, says) 42
  of MAM-22's 46 groups left the page: the 20 Penguins of Madagascar pairs and Mickey Mouse
  Clubhouse S03E31/S03E33, as episodes at different places; 8 Mickey Mouse Clubhouse episodes held
  as "a" and "b" files; Blade Runner, by its years and editions; and 12 groups of different editions
  — The Bourne Identity (Theatrical, Open Matte), Rambo: Last Blood, Terminator 2's three editions,
  Terminator Salvation, Alien, Aliens, Alien³, Alien: Resurrection, AvP: Requiem, and Tom & Jerry's
  Feedin' the Kiddie, The Egg and Jerry and Tops with Pops, plain beside Widescreen. Not tried live,
  as production has none, and resting on the tests: a hardlinked file whose names say different
  editions, a whole episode file beside an "a" file, a film item linked as an episode at another
  place, the keys of a split group and a possible match. Nothing in the library, a file or a
  download client changes, only what the list holds, and no field of the answer is removed.
  `DuplicateWorkComponentsTests`, `EpisodeSegmentsTests`, `FileNameYearTests`,
  `LibraryDuplicatesSameWorkHttpTests` (among them
  `List_makes_no_group_of_two_episodes_of_one_show_at_different_places_under_one_imdb_id`,
  `List_makes_no_group_of_an_episodes_a_and_b_files`,
  `List_makes_no_group_of_a_films_versions_whose_names_claim_years_far_apart` and
  `List_makes_no_group_of_the_theatrical_and_the_extended_cut_of_one_film`),
  `LibraryDuplicatesPossibleHttpTests.List_never_offers_two_episodes_with_one_imdb_id_at_different_places_with_one_title_and_year`
  and `List_flags_a_possible_matchs_files_whose_editions_differ`,
  `LibraryDuplicatesHttpTests.List_writes_classes_distinctions_and_editions_by_name` and
  `DuplicateGroupCard.test.tsx` pin it.

### Notes for upgraders

- **Applying `AddReleaseDescription`, `AddRecommendations` and `AddRecommendationRunProgress`
  rebuilds no table, so it logs no warning 20410 (MAM-33).** `AddReleaseDescription` adds one
  nullable column, `Releases.Description`; `AddRecommendations` only creates tables — the catalog's
  eleven and the recommendations' eight — and their 28 indexes; `AddRecommendationRunProgress` adds
  three nullable columns to `RecommendationRuns`, a running run's stage and its two counts.
- **Nothing is recommended until the owner connects TMDb (MAM-33).** Create a free account at
  themoviedb.org, open **Settings → API**, and copy the **API Read Access Token** (the long one, not
  the API key). In the app, open your user menu → **Integrations** → **Add integration**, choose
  **TMDb** as the service, name it, enter `https://api.themoviedb.org` as the address and the token
  as the API key, choose **Test connection**, then **Add integration**.
  The first computation starts by itself; on a library of about 900 films it takes a few minutes of
  calls to TMDb (3 min 15 s on the reference installation), its progress shown on the
  Recommendations page, later ones a handful. Every user then sees all four lists and up to 60 searches a day
  for them; each changes that under **Profile → Recommendations**.
- **The release sources are asked more (MAM-33).** Each user's lists may cost up to their daily limit
  of searches — 60 by default —, at most 4 in each pass of 15 minutes for the whole installation, plus
  one query per source every 3 hours for its newest releases. The first pass of each runs as the app
  starts. Set a limit to 0 to ask nothing for that user.
- **Releases stored before the upgrade have no description (MAM-33).** They go with the next day's
  prune of a search's releases, unless a download job keeps them.
- **Posters are cached in the data directory (MAM-33).** `<data>/posters/` holds every poster the
  Recommendations page has shown, a few dozen kilobytes each, and is never pruned; it needs no backup,
  as a missing one is fetched again while TMDb is connected.
- **Applying `AddLibraryLinks` rebuilds no table, so it logs no warning 20410 (MAM-34).** It adds
  three columns to `CatalogWorks` — `RuntimeMinutes`, `Overview` and `DetailsVersion` (0) — and
  creates seven tables, `LibraryItemLinks`, `LibraryItemLinkFiles`, `AuditEventIdentifiers`,
  `LibraryLinkSuggestionRuns`, `LibraryLinkSearches`, `LibraryLinkCandidates` and
  `CatalogWorkAlternativeTitles`, with their 16 indexes. Every cached TMDb film and show keeps its row
  and is read again the next time a run needs it, so the first computation of the recommendations
  after the upgrade reads every seed's details from TMDb again, a few minutes as the first one took.
- **Applying `AddLibraryLinkLookups` rebuilds no table, so it logs no warning 20410 (MAM-34).** It
  adds three nullable columns to `LibraryItemLinks`: `EpisodeImdbId`, `LookupRequestedUtc` and
  `LookupError`. Every link comes through whole, none of them set.
- **Applying `AddLibraryLinkIdentified` rebuilds no table, so it logs no warning 20410 (MAM-34).**
  It adds one nullable column, `IdentifiedUtc`, to `LibraryItemLinks`; every link comes through
  whole with none set, so every attached link counts for **Update Jellyfin**. `LookupRequestedUtc`
  and `LookupError` stay as dormant columns that nothing writes or reads, so the previous image
  still runs on the migrated database.
- **Bring the links set before up to date with Update Jellyfin (MAM-34, Vasyl's decision of
  2026-10-06).** Links set under the first rules put ids on items and, later, asked Jellyfin to look
  up what they lacked; Jellyfin still shows those items the titles, overviews and images it took
  from their file names. Nothing changes them by itself. Identify has no undo, so back up Jellyfin's
  data folder first if a way back matters. Each Jellyfin library gives its items the metadata
  language set on it, and one with none set uses the server's default: set the language of a library
  before the run if you want another. On the reference installation «Фільми» is Ukrainian and the
  others have none; the live check ran only in libraries with none or English, so identify one film
  of a library with a language first — open its work's **TMDb…** dialog and choose **Identify in
  Jellyfin** — and look at it in Jellyfin. Then open the TMDb links page: the panel counts the links
  not identified yet (223 on the reference installation), and **Update Jellyfin**, after one
  confirmation, makes Jellyfin identify each of those items in the background — two films or
  episodes at a time, a series alone and for about a second per episode: about 15 minutes for the
  223 — while the panel counts "Identifying in Jellyfin: 41 of 223 links…". The page can be closed
  or reloaded meanwhile. A link Jellyfin or the app refuses stays as it is with its reason, listed
  under **Not identified**; the refusals include any item that holds an IMDb or TMDb id other than
  its link's, which a repair never overwrites — **Retry** on it identifies it anyway, after you have
  read the sentence —, and a library that saves beside the media, which is refused before any
  search.
- **Nothing is linked until the owner links it (MAM-34).** TMDb must be connected (see above); the
  suggestions are then prepared after the next library sync, or at once with **Prepare again** on
  the TMDb page (Library → **Missing an id** → **Review works without a TMDb id**). The runtime
  evidence, and so every confident suggestion, needs the media folders allowed under **Media
  folders** and the files inspected. To have Jellyfin identify items, the integration's API key must
  be an administrator's — one made under Jellyfin's **Dashboard → API Keys** is —, Jellyfin needs an
  enabled administrator account to read an item as, and the item's library must not save artwork or
  metadata next to the media: no `Nfo` metadata saver, no saving artwork into media folders, no
  trickplay or downloaded subtitles saved with the media. The app refuses to identify in a library
  that does, and says so on the item. A series with watch history kept its played state on the live
  check (The Penguins of Madagascar, 15.4 % played); a film with watch history was not tried.
- **Back up `mam.db` (MAM-34).** The links are the owner's work and live only there. The suggestions
  are a cache the next run rebuilds. Identify changes Jellyfin's own data — titles, overviews and
  images — and can't be undone, and removing a link doesn't change Jellyfin back: back up Jellyfin's
  data folder before **Update Jellyfin** if you want a way back.
- **Applying `AddLibraryPosters` rebuilds no table, so it logs no warning 20410 (MAM-35).** It only
  creates `MediaWorkPosters` and its three indexes.
- **The Library's posters appear after each library's next sync (MAM-35).** The tags they are fetched
  by come only from a sync: until the owner's **Sync now** of each Jellyfin library, or **Refresh
  all** on the Library page, every card shows its placeholder. Nothing needs switching on.
- **Library posters are cached in the data directory (MAM-35).** `<data>/library-posters/` holds every
  poster a Library or Search card has shown, about 35 KB each, and is never pruned — an older
  picture's file stays after Jellyfin's picture changes —; it needs no backup, as a missing one is
  fetched again while its Jellyfin integration is switched on.
- **The duplicate groups need no migration and nothing switched on (MAM-20).** They are worked out
  from what the database already holds whenever they are asked for, so no table or column is added.
  What they can show grows with what the app has read: a same-file or byte-identical group needs the
  files inspected — and the possible copies hashed — inside the media folders the owner allowed, a
  name only the scan found needs a scan, and a link the owner sets counts at once. On the reference
  installation, where no two video files share a size, the byte-identical class stays empty. The
  **Duplicates** page shows them (MAM-21).
- **Applying `AddDuplicateMatchDecisions` rebuilds no table, so it logs no warning 20410 (MAM-21).**
  It only creates `DuplicateMatchDecisions` and its two indexes. The owner's decisions on possible
  matches live there alone, with their audit rows, so they are in a backup of `mam.db` and nowhere
  else; nothing needs switching on, and a group the owner never decided shows as before.
- **Applying `AddDownloadJobOverwriteCheck` rebuilds no table, so it logs no warning 20410
  (MAM-38).** It only adds the nullable column `DownloadJobs.OverwriteCheck`; the jobs sent before
  the upgrade keep `null` there and show no line about the check. The check needs nothing switched
  on, but it looks only inside the Media folders (the user menu → **Media folders**): a destination
  whose category saves outside them, or whose folder has no path in the app (ADR 0003's path
  mappings), is sent unchecked, and the Download dialog says so before **Send**.

## [0.2.0] — 2026-10-04

### Added

- **Duplicate detection will cover episodes as well as movies, and where the library's files come
  from was measured (MAM-17, ADR 0017, ADR 0018).** Specification §7 questions 3 and 6 are answered
  against the reference installation. Vasyl chose movies and TV in full: series, seasons and
  episodes will be imported from Jellyfin in every library (MAM-32). An episode is a work known by
  its own IMDb and TVDB ids and by its series' ids with its season and episode numbers, never by
  Jellyfin's series item, as The Grand Tour is four of them; a series' TMDb id never matches a
  movie's. Whether an item is a movie or an episode is the library's word alone: the 38 episodes
  Jellyfin lists as movies stay movies, and no file name changes that. On the media disk no file
  has a second name; 822 of the 1394 video files in the library folders are a seeding torrent's own
  data and 571 belong to no torrent; Radarr has never imported a file; and no two of those video
  files have the same size. A seeding file is a duplicate like any other, so Phase B reads no
  download client. Jellyfin's answers for series, seasons and episodes are new samples in
  `tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/Jellyfin/`, among them an episode
  item that holds 65 different episodes and a virtual episode whose only media source is a
  placeholder without a path.
- **A library sync imports the series and episodes of every Jellyfin library beside its movies
  (MAM-32, ADR 0019, ADR 0008).** A sync reads three lists — the movies, the series and the episodes
  (`includeItemTypes=Movie`, `Series`, `Episode`) — each in pages that overlap by one, with its own
  total and its own checks, the first page of each read first so the progress knows the whole
  library's total from the third request on, and stores all of it in one transaction or nothing. A
  series is a work that holds the show's titles and its IMDb, TMDb and TVDB ids and no version, its
  TMDb id in a scheme of its own (`TmdbTv`) that never matches a movie's. An episode is a work with its versions and files, known by
  its own IMDb and TVDB ids and by its series' ids with its season and episode numbers, and placed in
  its series by a row per library item (`MediaEpisodePositions`: the series work, the season and the
  episode number), named after its source like a title. Each kind is grouped on its own, series
  first, so The Grand Tour's four series items are one series, the two halves of a Mickey Mouse
  Clubhouse episode are one episode with two versions, and a movie and a series that share a TMDb
  number stay two works. An episode Jellyfin lists without a file (`LocationType` `Virtual`) is read,
  so the pages add up, and stored as nothing (Vasyl's decision 1). A run counts its series apart
  (`seriesCount` on `GET /api/library/syncs/{runId}`); its works are its movies and episodes.
  Removing the Jellyfin integration removes its series, episodes and their positions too.
- **The Library page lists each series as one card among the movies, with its seasons (MAM-32, ADR
  0019).** A series card shows "Series", the show's titles, its ids and the ids it lacks, and its
  seasons as rows that open — "Season N" in order, then "Specials" (season 0), then "No season" —
  each with how many episodes it holds; the seasons are read from the episodes' own numbers, never
  from Jellyfin's season list (Vasyl's decisions 2 and 3). Opening a season loads its episodes from
  the server a page at a time, with the list's search, library and identifiers filters, each as "N.
  Title" with the ids it lacks and its versions as rows; several seasons can be open at once, and the
  pager shows only past 10 episodes, its page size kept per device. An episode of no series, such as
  the Tom & Jerry Kids item, is a card of its own with its place, "Season 1 · Episode 4".
  `GET /api/library/works` lists the movies, the series and the episodes of no series in title
  order, a series once: it is listed when at least one of its stored episodes passes the search and
  the filters; a search or `identifiers=Missing` that the series itself answers keeps all its
  episodes, and `locationId` goes by the episodes' files. Each work gains `episode`
  (`{ seriesId, seasonNumber, episodeNumber }`, for an episode) and `seasons`
  (`[{ number, episodeCount }]`, for a series, counting the episodes the list keeps), and
  `missingIdentifiers` names the ids of its kind: IMDb and TMDb for a movie, IMDb and TVDB for an
  episode, IMDb, TMDb TV and TVDB for a series. `kind=Movie` or `kind=Series`, in any letter case,
  keeps one kind (`Series` takes the episodes of no series too), with a **Kind** filter on the page
  (Movies and series, Movies, Series); any other value is 400 "“x” is not a kind filter. Use Movie
  or Series.". `GET /api/library/works/{id}/episodes` pages a series' episodes, searched and
  filtered as the works are, in the order of its seasons, then by episode number (a missing one
  last), then by title: `season` is a number up to 9999 (0 for the specials), `None` for the
  episodes without one, or blank for all, and anything else is 400 "“x” is not a season. Use a
  season number, or None for the episodes without one."; an id that isn't a series' is a bare 404,
  checked first. The scheme labels now include TVDB and TMDb TV, and the versions table is offered
  for 2 to 6 versions only, so the 65 versions of one item show as rows.
- **The release search finds TV releases beside the movies, and takes a season (MAM-32, ADR 0020,
  ADR 0004).** Every enabled source gets the movie queries, as before, then the TV queries, one after
  the other: in the source's TV categories (5000 to 5999), an IMDb id first where its TV search takes
  one, then the title without the year — a TV release names the years of its seasons, so the show's
  year would find only the packs that hold its first season — through the TV search (`t=tvsearch`)
  or, when that takes no title, the general search. A source without a TV category gets no TV
  query. The movie releases come first, then the TV ones, and a listing both find is kept once; any
  failing query fails the source, as before; each source lists up to 100 releases of each kind.
  `POST /api/searches` and `POST /api/releases/search` take `season`, a number from 0 to 9999, 0 for
  the specials, blank for none, checked after the ids ("“x” is not a season. Use a season number,
  such as 1, or 0 for the specials."); with one, only the TV queries run, with `season=N` where the
  TV search takes it. The stored search answers `season` after `tmdbId`, and the Search page's
  narrow form has a **Season (optional)** field after Year. Each stored release keeps its kind — TV
  when its source lists it in a TV category, or in none to a TV query —, its categories in the
  listing's order, its TVDB id and, on a TV release, its show's TMDb id, and
  `POST /api/releases/search` gives each release `kind` (`Movie` or `Tv`) after `title`. A source
  that offers no search the app can use says "“X” offers no search this app can use: no search by
  text, and no movie or TV search by title or by an id that was asked for.", or with a season "“X”
  offers no search this app can use for a season: no TV category, or neither a search by text nor a
  TV search by title or by an id that was asked for."
- **A TV release's title is read for its seasons, and the release is matched to the series in your
  library only (MAM-32, ADR 0020, ADR 0012).** The first season marker gives the seasons and
  episodes a title claims — `S01E01`, `S01E01-E10`, `S1E1-9 of 9`, `S1-3`, `S01-S03`, "Season 1",
  "Seasons 1-2", "Сезон 1", "Сезони 1-3", ", серії 1-9 з 9", bare or in brackets — and a bracketed
  span such as "(2018-2024)" ends the title as a year does, so Prowlarr's "Yellowstone (S1-5)
  (2018-2024) WEB-DL-AVC" is "Yellowstone", seasons 1 to 5, of 2018 to 2024; a movie's title is read
  as before. A TV release is matched to series only — by a show's IMDb, TMDb TV or TVDB id, then by a
  title with a year its seasons allow, then by the title alone — and a movie release to movies only,
  so a film and a show of one title, or of one TMDb number, never match each other's releases, and
  an episode of no series is matched by none. A release names its seasons' years, never the show's
  (Vasyl's choice): with season 1 among the seasons its first year must be the series' year, so
  "Yellowstone (S1-5) (2018-2024)" is Yellowstone (2018) by title and year and the BBC's "Yellowstone
  (S1) (2009)" by title alone; with later seasons only, or the specials alone, the series may have
  begun in that first year or before, so "(S5) (2022-2024)" and "(S4) (2021)" are Yellowstone (2018)
  by title and year too — as they would be for an older show of the same title; with no season, any
  year of the span, as for "Yellowstone (2018-2024)". Each TV match carries `heldEpisodeCount`: how
  many of the series' episodes sit in the seasons or episodes the title claims, `null` when it claims
  no season. `GET /api/searches/{id}/releases` takes `kind=Movie` or
  `kind=Tv` in any letter case, checked right after `source` ("“x” is not a kind filter. Use Movie or
  Tv."); each release gains `kind` after `title`, `claimedWork` gains `lastYear` and `episodes`
  (`{ firstSeason, lastSeason, firstEpisode, lastEpisode }`), each library match
  `heldEpisodeCount`, and the search's `facets` gain `kinds`, Movie before Tv. **In your library**
  shows a series as the Library page's card with its seasons, and an episode of no series as its own
  card: an episode the search finds by a title, its IMDb id or a file path lists its series once,
  on the stronger evidence of the series' own and the episode's, and a series counts once towards
  the 20 works listed by their words. On the Search page a TV release's **Claimed** chips
  start with "TV", then its seasons ("Seasons 1–5", "Season 1", "Specials") and episodes ("Episodes
  1–9"); a TV match says how many of its episodes your library holds ("None of its episodes in your
  library" when none); **Details** adds "Listed as", the span ("2018–2024") and the seasons and
  episodes read from the title, with the matched series' seasons; and the list has a **Kind** filter
  (Movies and TV, Movies, TV) of the kinds the search found.
- **The owner maps a release source's sections to a destination, and the Download dialog opens on it
  (MAM-32, ADR 0020).** In the **Add destination** and **Edit** dialogs of the Destinations page,
  **Tracker sections (optional)** lists each release source as a row that opens to its sections —
  its own sections, then the standard categories — read from the source each time the dialog opens,
  the categories of one name one section, Prowlarr's leading "|-" dropped: Toloka's three
  «Мультсеріали» categories are one. A section ticked for another destination says "now Cartoons"
  and moves here when ticked, and a source that couldn't be asked says why beside the sections
  already mapped to it. `GET /api/settings/destinations/sections` (owner only, 200 whatever the
  sources answered) gives `{ sources: [{ id, name, sections: [{ name, categoryIds, isSourceSpecific,
  destinationId, destinationName }], error }] }` for every release source by name, a switched-off
  one unasked with its mapped sections. `POST` and `PUT /api/settings/destinations` take
  `sections: [{ releaseSourceId, categoryIds, name, isSourceSpecific }]`, written in the
  destination's own save and transaction with its one audit row: on an update, left out keeps them
  and `[]` removes them; each category of a source is mapped to at most one destination, and one
  mapped elsewhere moves here. A section that breaks a rule is 400 ("Pick the release source of each
  section.", "A section needs its name.", "A section's name can't contain line breaks or other
  control characters.", "A section needs at least one category of its release source.", "“x” is not
  a category id of a release source."), and one whose source isn't a release source any more is 404
  "A release source you picked sections of no longer exists. Reload the page and pick them again.";
  a save never asks the source. The rows go with their destination or their source.
  `GET /api/downloads/options` gains `suggestedDestinationId` (Vasyl's decision 5): among the
  destinations it offers, the one the release's stored categories are mapped to, the source's own
  section winning over a standard category; none when two different destinations tie at that rank;
  else the default, when it is offered. The Download dialog opens on it and its client; with none,
  on the placeholders, but a lone client is picked, and so is its destination when it has one only.
- **The owner scans the media folders for the files no library reported (MAM-18, ADR 0021, ADR
  0009).** `POST /api/library/scans` answers 202 with the queued scan and its `Location`
  (`/api/library/scans/{runId}`), or with the one already waiting, which the request joins; one asked
  for while a scan runs is queued behind it. It is owner-only (`[RequireInstallationOwner]`, swept by
  `LibraryWritesAreOwnerOnlyHttpTests`: 403 "Only the installation owner can do this." for anyone
  else), 409 with "No media folders are allowed yet. Add the folders the app may read under Media
  folders in the user menu, then scan again." while no folder is allowed, and 503 with "Too many
  folder scans are waiting. Try again in a minute." when the queue is full, the run kept as failed
  with that sentence. At most one scan is queued and one running
  (`IX_FolderScanRuns_OneRunPerActiveStatus`). `GET /api/library/scans/{runId}` gives every
  signed-in user `{ id, status, requestedByUserName, createdUtc, startedUtc, finishedUtc,
  filesFound, filesUnreported, problemCount, problem, error }` — `filesFound` the files the walk has
  reached so far, each once however many links lead to it, `filesUnreported` how many no library
  reported once it stored them, `problem` the first place it didn't scan — the requester, the problem
  and the error for the owner only; a bare 404 for an unknown run. A scan runs only when the owner
  asks — no timer, not after a sync, not at the start — and reads no download client (ADR 0018). The
  reconciler fails a scan a stop interrupted ("The app restarted before this scan finished, so
  nothing it found was stored. Scan the folders again.") and queues a queued one again, but queues
  none of its own; a running scan the next claim finds dead fails with "The scan stopped because of
  an error in the app. Its log says more.", and one whose folders were all removed before it started
  with "No media folders are allowed any more, so there was nothing to scan. Add one under Media
  folders in the user menu." The log gives counts, never a path.
- **The scan walks every allowed folder, follows a link only inside them and opens no file (MAM-18,
  ADR 0021).** The local file inventory walks the roots in the owner's order, depth first, each
  folder's entries in ordinal name order, hidden ones included, and lists every media file: a regular
  file whose name ends in `mkv`, `mp4`, `m4v`, `avi`, `mov`, `ts`, `m2ts`, `mts`, `mpg`, `mpeg`,
  `vob`, `wmv`, `asf`, `flv`, `webm`, `ogv`, `rm`, `rmvb` or `iso`, letter case ignored (Vasyl's
  decision 2), the clips of Blu-ray and DVD folders included. A symbolic link or junction inside a
  root is resolved as a look at a file resolves it (40 links at most) and followed when it ends inside
  a root, its files listed under the link's own path; one leading out is never followed, and a folder
  that leads back to the one it is in or one above it — by its path, and on Linux by its device and
  inode too — isn't walked again. On Linux each folder is opened from `/` one name at a time with
  `O_NOFOLLOW` and listed through `/proc/self/fd/<n>`, so a folder swapped for a link after the walk
  checked it is refused. A root that is missing or lists nothing ("“/media” is empty; if it is a
  mount, it may be away, so the files found in it before are kept."), a folder the app may not list
  or that changed while it was checked, and a link that leads nowhere now are places the scan
  couldn't list, and what it found there before is kept; a link leading out, a loop ("“…” leads back
  to a folder above it, so the app doesn't walk it again."), a name that isn't valid text ("A name in
  “…” isn't valid text, so the app skips it.") and a path longer than 4096 characters are places it
  refuses. Both kinds are counted as problems, never a failure. A walk lists at most 100,000 folders
  and gives at most 200,000 entries; past either the scan fails with "The media folders hold more than
  200,000 media files or 100,000 folders, or links inside them lead to the same folders many times
  over, so the scan stopped.", storing nothing and queuing no inspection.
- **A scan stores one name, with no version, of each file no library reported (MAM-18, ADR 0021,
  ADR 0006).** The names that lead to one file through links are one file. A file is reported when
  one of its names is where the app finds a file a library reported — a movie's or an episode's,
  through its integration's current path mappings — by path alone, never by a file name, a title or
  an id. Each file no library reported keeps one `PhysicalFiles` row: source kind `FileInspection`,
  no connection, no version, the app's own path as `ServicePath`, the row one of its names already
  had (its id, first-seen time and readings kept, its last-seen time moved) or else its first name.
  In one transaction with the run's outcome, a name stored before goes when the walk reached it under
  a library's path or as another name of a file, stays when it lies outside every root this scan
  walked or at or under a place it couldn't list, and goes otherwise; a scan that fails or is
  stopped stores nothing. Such a name belongs to no work: the works list with its search and filters,
  the list of Jellyfin libraries, each library's counts and a search's library matches never show it
  (Vasyl's decision 1).
- **The Library page has a Media folders row with Scan folders (MAM-18).** Under **Sources**, after
  the Jellyfin libraries, the row **Media folders** says how the scan stands: "Waiting to scan the
  media folders…", "Scanning the media folders… N files found", "Scanned <time>: N files, U in no
  library.", "Scan failed <time>: …" — "The last scan failed. The owner can see why." for everyone
  else — or "The media folders have not been scanned yet.", and that another scan follows the
  running one when one waits. A scan with problems warns "N folders or links weren't scanned:" with
  the first one's sentence to the owner, and "N folders or links weren't scanned. Ask the owner
  which." to everyone else. "Files in no library: I inspected · P couldn't be read · W not inspected
  yet" counts the scan's names by the app's last look at each, as each library's row counts its
  files, with the first unreadable one's sentence for the owner. With no folder
  allowed the row says "Scanning is off: no media folder is allowed yet.", with **Choose media
  folders** for the owner. **Scan folders** is disabled with its reason for anyone but the owner,
  with no folder allowed and while a scan waits, and stays enabled while one runs; a refused start
  shows its sentence under the row. The page asks again every 2 seconds while a scan is under way
  (Vasyl's decision 3).
- **A scan queues the file inspection, which records each file's size and modification time
  (MAM-18, ADR 0009).** Once a scan has stored its names it queues an inspection with the new trigger
  `FolderScanned`, or joins the one waiting, as a sync does; when none can be queued, the scan still
  succeeds and the log says why. The inspection reads the scan's names with ffprobe through the
  same cache as the library's, so a file already read isn't read again. Every name it looks at, a
  library's too, now keeps the size and the modification time the disk said
  (`PhysicalFiles.ReadSizeBytes` and `ReadModifiedUtc`) with its identity: empty until the app has
  looked, and empty again when its last look found nothing there.
- **The file inspection ends by hashing the files that could be byte-identical copies (MAM-19, ADR
  0022, ADR 0009).** Once a run has looked at every name and read what the cache didn't hold, it
  lists the files a copy could hide among: one per identity, size and modification time, named by
  its first name in path order, whose size a file of another identity shares and whose media facts
  don't tell the two apart — the duration, the number of tracks and, track by track, the kind,
  codec, width, height, channels and language, a value either reading lacks, or a file ffprobe
  couldn't read, ruling nothing out. A file whose size no other file shares is never read, nor are
  two hardlink names of one file alone. Each candidate is read whole for its SHA-256, one at a time
  (`FileInspectionRunner.MaximumConcurrentHashes`, Vasyl's decision 3), inside the allowed roots and
  from the file the checks opened, and kept in the new `FileHashes` table under the file's device,
  inode, size and modification time, the readings' key: a file is read whole once, and again only
  when one of the four changes, while a renamed file, or another name of it, finds its hash without
  a read. A file that changed while it was read ("“…” changed while the app was reading it for its
  hash, so the hash can't be said to belong to either version."), and one gone, outside the roots or
  unreadable, keeps nothing and is tried again by the next run, counted as a problem, never a
  failure of the run; one that changed between the look and the hash keeps the hash of what was read
  under its new key and counts "A file changed after this inspection looked at it, so its hash
  counts from the next inspection." A hash goes once neither a name carries its key nor a kept
  reading has it, so a mount that is away costs no hash of a file the app has a reading of, and a
  changed file's old hash goes with its old reading. Hashing is the last step of every inspection —
  after a sync, after a scan, at the start and on **Inspect files** (Vasyl's decision 1) —: a
  restart fails the run as before ("The app restarted before this inspection finished. What it had
  read is kept."), and the start's run answers the stored hashes from the cache and reads the rest.
  A file a torrent seeds is hashed like any other, and no download client is asked (ADR 0018). The
  hashes are kept, not shown yet: grouping the copies is MAM-20's.
- **The Library page says how much room the files take, each file counted once (MAM-19, ADR 0018,
  ADR 0022).** Under its files line and the warnings about it, every Jellyfin source's row and the
  **Media folders** row say "Takes <size> on disk: N files.", then "M more names lead to files
  counted once." when files have several names and "U files not measured yet." for the names the app
  has no measure of; once a media folder is allowed, the file inspection's row says "All files take
  <size> on disk: N files." for every stored name, so a file a library and the scan, or two
  libraries, name counts in each row and once in the total. A file is its device and inode, and its
  size the one the app's last look read, never the size a library reported; a row none of whose
  files the app has measured shows no such line (Vasyl's decision 2). Every signed-in user reads the
  same figures.
- **The Library page follows the hashing (MAM-19).** While a run hashes, the file inspection's
  status line reads "Hashing the files that could be copies… C of T"; once the run has succeeded, a
  line under it says "Files that could be copies: H of T hashed, R read in full this time." or "No
  file needed a hash: no two files of one size could be copies of each other.", and "N files
  couldn't be hashed:" with the first one's sentence warns the owner, "N files couldn't be hashed.
  The owner can see why." everyone else. The page asks again every 2 seconds while the run is under
  way, as before.

### Changed

- **A sync's progress and its result count items, works and series, and its failures name the list
  (MAM-32, ADR 0008).** The Library page counts a running sync in items ("Syncing… N of M items")
  and a finished one as "Synced <time>: read N items into W works and S series, V versions, F
  files.", the items being movies, series and episodes and the works movies and episodes; a sync
  from before the upgrade keeps its line without the series. A list that changes while it is read
  fails the run with the list's noun: "Jellyfin's list of series shifted between two pages, so its
  library changed while it was being read. Nothing was changed here; sync again once Jellyfin's
  library scan has finished."
- **The media library and release search contracts learn TV (MAM-32, ADR 0005).**
  `LibraryPageRequest` names the list it reads (`Kind`), and `LibraryItem` gains where an episode
  sits (`Episode`) and whether the library holds no file for it (`IsMissing`). `ReleaseCategory`
  gains `HoldsTv` (Torznab's 5000 to 5999) and `IsSourceSpecific` (ids of 100000 and up),
  `ReleaseSearchCapabilities` the source's `TvSearch`, `ReleaseSearchQuery` a `Season` and the mode
  `Tv`, and `ReleaseSearchHit` its `Kind`. The core plans the TV queries
  (`ReleaseSearchPlanning.ForTv`), and the Torznab adapter sends them as `t=tvsearch` with
  `season=`, `tvdbid=` or a show's `tmdbid=`. Its connection test names the TV search: "Connected to
  Prowlarr over Torznab; movie search by title, TV search by title, season and episode."
- **The texts that said "movie" cover series (MAM-32).** The header field says "Search a movie or a
  series by title, e.g. Interstellar (2014)"; the Library and Search pages' leads, empty states and
  filters speak of movies and series, or of works; the Library page's identifiers filter offers
  "Any identifiers" and "Missing an id", an id of the work's kind; and removing a media library
  integration says its "movies, series, versions and files" leave the Library page.
- **`IFileInventory` walks the allowed roots (MAM-18, ADR 0005).** The contract gains
  `ListMediaFilesAsync(allowedRoots)`, an `IAsyncEnumerable` of `FileListingEntry` — a status
  (`MediaFile`, `Unlisted`, `Skipped` or `LimitReached`), the app path, where a media file really
  is, and a sentence for anything else — and which names are media files is the core's
  `MediaFileNames`. The platform's file reader gains `OpenFolder`.
- **`GET /api/library/sources` gains `scan` (MAM-18).** `{ activeRun, lastFinishedRun,
  hasQueuedRun, files }`: the scan under way — the running one, else the queued one —, the latest
  that succeeded or failed, each as `GET /api/library/scans/{runId}` gives it, whether one waits, and
  `files` `{ inspectedCount, problemCount, notInspectedCount, problem }` counted when read over the
  scan's names that no file a library reported maps to now, so a sync that starts reporting a path,
  or a fixed path mapping, takes a name off at once, without another scan; `problem` for the owner
  only. A scanned name never counts in a library's own row.
- **A file name may have no version (MAM-18, ADR 0006).** `PhysicalFiles.MediaVersionId` is nullable,
  null only on the names a scan found that no library reported; a library's name still has its
  version and goes with it. Everything that reads the library's files keeps those names out.
- **The file inspection reads every stored name and records more of each (MAM-18, ADR 0009).** A run
  looks at the names the scans stored as well as the libraries', and writes on every name the size
  and modification time the disk said beside its identity. `GET /api/library/inspections/{runId}`'s
  `trigger` may be `FolderScanned`.
- **The Library page's Sources card shows its rows without a media library too (MAM-18).** With no
  Jellyfin connected, the card says how to connect one, then shows the Media folders row and the
  file inspection's row, as neither needs a library. Its description, and the Media folders page's,
  say what **Scan folders** does.
- **`GET /api/library/sources` gains `space`, and every file inspection run gains `hashing` (MAM-19,
  ADR 0022).** Each item, `inspection` and `scan` gain `space` `{ sizeBytes, fileCount, nameCount,
  unmeasuredCount }`, counted when read — over the integration's file names, over every stored file
  name, and over the scan's names no library reports now —, each file once by device and inode, with
  the size the app read; `unmeasuredCount` is the names it has no identity or size for, left out of
  `sizeBytes`. Every signed-in user gets the same. `GET /api/library/inspections/{runId}`, the 202 of
  `POST /api/library/inspections` and the runs in `GET /api/library/sources` gain `hashing`
  `{ candidates, checked, read, problemCount, problem }`: how many files need a hash (`null` until
  the step has listed them), how many it has dealt with, how many it read in full in this run, how
  many it couldn't hash, and the first one's sentence, for the owner only.
- **`IFileInventory` hashes a file (MAM-19, ADR 0005).** The contract gains
  `HashAsync(appPath, allowedRoots)`, answering a `FileContentHash` — the file's facts and the
  SHA-256 of every byte in lowercase hex — after the checks of `DescribeAsync`, or
  `ChangedWhileReading` when the facts asked again after the read differ or the read didn't end at
  their size. The local inventory reads 1 MiB at a time from the file its platform reader opened,
  which gains `ReadAsync`.
- **The file inspection's log line counts the hashing (MAM-19).** A finished run logs "File
  inspection {RunId} finished: {Checked} of {Total} files checked, {Probed} read by the media
  inspector; {HashCandidates} files could be copies, {HashRead} read whole for their hash,
  {HashProblems} not hashed." — counts only, never a path.

### Notes for upgraders

- **Applying `AddSeriesAndTvReleases` rebuilds no table, so it logs no warning 20410 (MAM-32).** It
  adds `Searches.Season`, `Releases.Kind` (`Movie` for every release already stored),
  `Releases.CategoryIds`, `Releases.TvdbId` and `LibrarySyncRuns.SeriesCount`, and the tables
  `MediaEpisodePositions` and `DestinationSections` with their indexes. A release stored before the
  upgrade has no categories, so the Download dialog offers it the default destination; the next
  search a day later removes it, unless a download job keeps it.
- **Series and episodes appear after the next sync (MAM-32).** After the upgrade, choose **Sync
  now** on the Library page; until then the Library page and the Search page's library side list the
  movies alone. A sync reads two more lists than before, so it takes longer on a library with many
  episodes.
- **Tracker sections are picked by the owner, and none is at first (MAM-32).** Until a section is
  ticked, the Download dialog opens on the default destination, as before. Open **Destinations** in
  the user menu, choose **Edit** on a destination, open the release source under **Tracker sections
  (optional)**, tick the sections whose releases go there — for example «Мультсеріали» and
  «Мультфільми» for Cartoons, «Телесеріали» for TV Shows, the «Документальні фільми» sections for
  Documentary — and choose **Save changes**.
- **Each search asks every source with TV categories for TV too (MAM-32).** The TV queries go after
  the movie ones — one more for Toloka —, so a source's answer takes longer, and a search stores up
  to 100 movie and 100 TV releases per source. Searching needs nothing turned on.
- **Applying `AddFolderScan` rebuilds the `PhysicalFiles` table, and logs EF Core warning 20410
  once: back up `mam.db` before you upgrade (MAM-18).** Letting a file name have no version makes
  SQLite rebuild `PhysicalFiles`, which EF Core does with foreign keys off outside the migration's
  transaction, so the log shows `Microsoft.EntityFrameworkCore.Migrations[20410]` for
  `PRAGMA foreign_keys = 0`, as for `AddStoredSearches`. Unlike `AddLibraryInventory`'s rebuild of
  the same table, empty then, this one copies every file name your libraries reported, with what the
  app read of each: every value comes through, but a process killed in the middle leaves the
  migration half applied, and the next start can't apply it again without a repair by hand. Back up
  the data directory with the container stopped (README, Deployment → Data and backups), then let
  the first start after the upgrade finish before you stop the container. It also adds
  `PhysicalFiles.ReadSizeBytes` and `ReadModifiedUtc`, empty until the next inspection, and the
  `FolderScanRuns` table.
- **Nothing is scanned until the owner asks (MAM-18).** With a media folder allowed under **Media
  folders** in the user menu, choose **Scan folders** on the **Media folders** row under **Sources**
  on the Library page; the inspection it queues then reads the files it found, which takes a while
  the first time. Scan again after you add files no library lists: the app doesn't watch the folders.
- **Applying `AddFileHashes` rebuilds no table, so it logs no warning 20410 (MAM-19).** It adds the
  `FileHashes` table with its unique index on `(DeviceId, Inode, SizeBytes, ModifiedUtc)`, and to
  `FileInspectionRuns` the columns `HashCandidates`, `HashCandidatesChecked`, `HashCandidatesRead`,
  `HashProblemCount` and `HashProblem`: a run from before the upgrade lists no file to hash, with
  empty candidates and counts of 0.
- **The first inspection after the upgrade reads whole the files that could be copies (MAM-19).**
  With a media folder allowed, the inspection at the next start hashes every file whose size a file
  of another identity shares and whose facts don't tell them apart: on a library where many files
  share a size, that reads each of them in full once, one after the other, and keeps the disk busy
  for as long as that takes; later runs read only what is new or changed, and a file a new file of
  its size could be a copy of. On a library where no two
  files share a size — the reference installation's, as MAM-17 measured — it reads no file whole.
  The room the files take is counted from what the inspection read, so a file counts in it once an
  inspection has looked at it. There is nothing to turn on.

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
- **Every signed-in user searches the library and every release source at once, and the search is
  stored, so every filter, sort and page reads what it found (MAM-12, ADR 0011).**
  `POST /api/searches` with `{ text, year, imdbId, tmdbId }` — every field a string, the title
  required, with MAM-10's rules and sentences — asks the library and every enabled release source at
  the same time, the library through a database context of its own, and answers 201 with its
  `Location` (`/api/searches/{id}`) whatever the sources answered: the search as asked, trimmed; the
  works of the library that match it or hold its words (see below); each source in name order with
  its `status`, its `notSearchedReason`, the sentence of a failure (`error`, for the owner only) and
  how many releases it listed; and the audio languages and resolutions those releases claim, for the
  filters. A broken rule answers 400 with its sentence, stores nothing and asks no source. The search
  is a `Searches` row of the caller's, with a `SearchSources` row per source — its name copied, its
  sentence masked and cut to 1000 characters — and every release a new MAM-10 row linked to its
  source, with its place in the source's answer, its seeders and peers and the ids its listing gives.
  `GET /api/searches/{id}` reads it again: the sources as stored, never asked again, and the library
  matched again as it is now, so a sync since shows. `GET /api/searches/{id}/releases` filters the
  stored releases by source (`source`, a search source's id), audio language (`language`, a
  three-letter code in any letter case, or `unknown`), resolution (`quality`, such as `1080`, or
  `unknown`) and size (`minSizeBytes` and `maxSizeBytes`, both inclusive, a release of unknown size
  left out once either is set), sorts them (`seeders` by default, `largest`, `smallest`, `newest`,
  `quality`; a value the listing doesn't give always last, and ties by the source's place in the
  search, then the release's in its source's answer) and pages them (page size 1–100, default 25),
  on the server and in memory, since what a title claims is read from it on every read and a search
  holds at most 100 releases per source. A value it can't read answers 400 with its sentence ("“uk”
  is not a language code. Use a three-letter code such as ukr, or unknown."), and a well-formed source
  id of another search lists nothing. Each release carries what its title claims — MAM-10's quality
  and languages, and the titles and year it is read as — the ids its listing gives, and every work of
  the search's library side it matches. A search is its creator's: asked for by anyone else, the
  owner included, it answers a bare 404 on both reads, and so does one more than a day old, even
  before a prune removes it. The next search, stored or plain, removes the searches more than a day
  old with their sources, and a release a download job sent stays without its search; deleting a
  user removes their searches. MAM-10's `POST /api/releases/search` stays as it was (owner's
  choice), and both searches ask and store through one implementation (see Changed). One migration
  (`AddStoredSearches`) adds the `Searches` and `SearchSources` tables and the new release columns
  (see Notes for upgraders). `SearchesConcurrencyHttpTests` holds the library's first read until a
  feed is asked, so a search that asked one after the other fails, and `SearchesSecretsHttpTests`
  finds no planted key, link token, session or passkey in the three answers, the stored rows or the
  log.
- **A search and each of its releases are matched to the library's works by identifier, then by
  title and year, then by title alone, and every match is listed, strongest first — never picked,
  merged or acted on (MAM-12, ADR 0012).** The library side reads the snapshot the last sync
  imported, never Jellyfin itself. A work matches by `Identifier` when it holds an IMDb or TMDb id
  asked for (trimmed, in any letter case), by `TitleAndYear` when one of its titles — displayed or
  original, from any source — has a claimed title's key and that title's own year is the year
  claimed, and by `TitleOnly`, a candidate to check, when only the key does; nothing else counts, no
  distance between titles and no year ± 1. A title's key is the title upper-cased, its apostrophes
  dropped so the letters around them join ("Ocean's Eleven" is `OCEANS ELEVEN`, like the scene's
  "Oceans.Eleven", and "Дев'ять", "Дев’ять" and "Девʼять" are one key), and every other run of
  characters that aren't letters, digits or combining marks one space. Each match carries the ids
  that matched with every source that holds them, the title that matched with its kind, year and
  source, and `conflictingSchemes`: a scheme the claim and the work both hold values of but share
  none of, or one the work itself holds two values of — flagged, never resolved, and no bar to a
  title match. The works come by evidence, then by title in Ukrainian alphabetical order, each as the
  Library page's card, versions, files and verified facts included, with the owner-only fields kept
  the owner's. A release is matched to every work its search lists — the works by words (see the
  next entry) included — by the ids its listing gives and by the titles and year read from its title
  (owner's choice): the first bracketed year with a title before it ("Interstellar (2014) BDRemux
  1080p" is "Interstellar" of 2014), else the scene's bare year ("Interstellar.2014.1080p.BluRay"),
  the part before it split at " / " into alternatives ("Інтерстеллар / Interstellar"), and no title
  at all without a year, since nothing then says where the title ends; it lists every work it
  matches, strongest first. Nothing is stored about a match.
- **After the matches, the search's library side also lists the works one of whose titles or file
  paths holds every word of the title searched for — at most 20, marked `Words`, never a match — and
  a release is matched to every work the search lists, never by words (MAM-12 review, ADR 0012).**
  Searched for "rambo", the Library page found five films and the search page one: only "Rambo"
  (2008) has the key asked for, "Rambo III" and the others hold the word in a longer title, and
  "First Blood" (1982) only in its file's path (`/media/movies/Rambo. Collection/…`). The three
  tiers stay as they were. A work none of them matches is listed with the evidence `Words` when one
  of its titles, from any source, displayed or original, or one of its files' service paths holds
  every word of the title's key, each whole, in any order: "rambo" finds "Rambo III" and the folder
  `Rambo. Collection`, and "alien" doesn't find the title "Aliens". The year doesn't narrow it, ids
  aren't searched for words, and a title with no letter or digit lists nothing. `matchedTitle` is the
  title holding the words, `null` when only a path does, and conflicting ids are flagged as for a match.
  These works come after every match, at most `LibraryMatcher.MaximumWorksByWords` (20), in the same
  order, and the answer's new `libraryUnlistedCount` says how many more hold the words; the cap is
  applied before any card is read, and the database narrows the candidates by file path as it does
  by title. A release is still matched by identifier, title and year or title alone, never by words,
  but against every work the search lists, so "Rambo III (1988) BDRip" says "In your library: Рембо
  III (1988) — matched by title and year"; a release's `evidence` is never `Words`. Look-alikes the
  matching refuses, "Interstellar Wars" for "Interstellar", show there too, labelled as no match.
  Nothing about them is stored.
- **A release's title is read without the square-bracketed tags that end it, so an edition of a film
  you own matches it by title and year (MAM-12 review, ADR 0012).** Searched for "rambo", Toloka's
  "Rambo: Last Blood [Extended Cut] (2019) BDRip-AVC" and "V / Rambo [Theatrical Cut] (2008)" matched
  nothing: the tag between the title and the year was read as part of the title, `RAMBO LAST BLOOD
  EXTENDED CUT`, which no work holds. Each title alternative, once trimmed, now loses every
  square-bracketed group that ends it, so they read as "Rambo: Last Blood" of 2019 and "V" and "Rambo"
  of 2008 and match those films by title and year; scene names the same. Only at the end (owner's
  choice): a title that opens with or holds square brackets keeps them ("[REC] 2"), a group that is
  all that is left stays ("[REC]"), and round brackets are never dropped, as they hold titles
  ("Birdman or (The Unexpected Virtue of Ignorance)"). A tag that opens a title, "[HDRip] Rambo
  (2008)", still stays in it. `claimedWork.titles` shows the titles without the tag; the release's
  `title` is unchanged.
- **The Torznab adapter reads the IMDb and TMDb ids a listing gives (MAM-12, ADR 0004).** The
  attributes `imdbid` (`tt0816692` or its digits), `imdb` (the digits, which Jackett writes without
  their leading zeros: `816692`) and `tmdbid` give a release at most one id per scheme, IMDb first,
  an IMDb id as a lower-case `tt` and its digits padded to seven, so it equals the id a library
  holds. A blank value, one of zeros only (Jackett's `0` for none) or one `MediaIdentifierRules`
  refuses is no id, and a listing whose attributes give two different values of one scheme gives none
  of it, as a listing that contradicts itself can't be matched. Both searches store the ids with the
  release (`ImdbId`, `TmdbId`). Toloka through Prowlarr lists none, so its releases match by title
  and year.
- **The Search page (MAM-12).** **Search** in the header, first, before **Library**, opens it for
  every signed-in user. Its form takes the title and, optionally, the year, the IMDb id and the TMDb
  id, each a field of its own (owner's choice), and shows the API's sentence under it; the search's
  id goes into the address (`/search?id=…`), so a reload or Back keeps it, and a search that is gone
  says "This search is no longer available. Search again." **In your library** shows each matching
  work as the Library page's card with a badge for how it matched — "Matched by IMDb id", "Matched by
  title and year", "Possible match — check it" — the matched title or id with its source on hover,
  and a warning badge per conflicting scheme. Below the matches, under a line of their own, come the
  works that hold every word searched for, each badged "Title contains “rambo” — not a match" or "A
  file path contains “rambo” — not a match"; when more than 20 do, a line says how many more, with a
  link to the Library page searched for the same text, which the Library page now reads from its
  address (`/library?search=…`). **Available releases** shows a line per source — how many releases
  it listed, its failure's status with the owner's sentence, or why it wasn't searched — then the
  filters (source, audio language, quality, size in GiB from and to, sort), each change going back
  to page 1, and each release with its listed facts, its claimed quality under a visible **Claimed**
  label, a badge for its strongest library match, and **Details** (a dialog with the
  listing, what its title was read as and its ids, beside every matching work's versions and
  verified facts), **Open source** (the source's page in a new tab, greyed out with the reason when
  there is none) and **Download**, greyed out with "Sending a release to a download client comes in a
  later update." The list is paged with a page size remembered per device; the dialog has two
  columns on a wide screen and one on a phone. The Library page's version, file and track lines are
  components of their own now, shared with the dialog.
- **Every signed-in user sends a release one of their searches found to a download client, and one
  request sends it at most once: the job is written, with the request's id and an audit row, before
  anything is fetched or sent (MAM-13, ADR 0013).** Every signed-in user may send, as everyone may
  search (MAM-13's decision 1), so the endpoints live in `Controllers/Downloads`, outside the
  namespaces whose writes are owner-only. `GET /api/downloads/options?releaseId=…` answers the
  Download dialog's choices from the database alone, asking no client: every switched-on download
  client of a type this build can send to that can take the release's payload — one whose kind the
  listing didn't state, a gateway's link, needs a client that takes a .torrent file or a magnet
  link — and has a destination, by name, each `{ id, name, typeName, destinations }` with its
  destinations by name (`{ id, name, clientDestinationKey, isDefault }`): names only, no address,
  folder or path mapping. It answers 400 "Pick the release to send." without a well-formed
  `releaseId`, and 404 "This release isn't available any more: a search keeps what it found for a
  day. Search again." when the release isn't one of the caller's own searches' from the last day —
  another user's, an older search's, or one MAM-10's plain search found. `POST /api/downloads` with
  `{ requestId, releaseId, downloadClientId, destinationId }` looks the request id up first, before
  anything else is read: the caller's same id asking for the same release, client and destination
  answers 200 with the job it made, even after the release, the client or the destination went, and
  fetches, sends and audits nothing; asking for anything else, it is 409 "This request id was
  already used to send a different download. Reload the page and try again." A new request is then
  checked in this order, each refusal writing and sending nothing: 400 for a missing id ("The
  download has no request id, which keeps a retry from sending it twice. Reload the page and try
  again.", "Pick the release to send.", "Pick the download client to send it to.", "Pick the
  destination to send it to."); 404 for the release as above, for a client that isn't there or
  isn't a download client ("The download client you picked no longer exists. Reload the page and
  pick one again.") and for a destination that isn't there ("The destination you picked no longer
  exists. Reload the page and pick one again."); 400 for a destination of another client ("“Movies”
  is a destination of another download client. Pick one of “qBittorrent”’s."); 409 for a
  switched-off client ("“qBittorrent” is switched off. The owner can switch it on under
  Integrations.") or one of a type this build can't send to ("This version of the app can't send
  downloads to “…”."); 400 for a client that can't take the payload ("“qBittorrent” can't take NZB
  files, which this release is."); 409 for a source that was removed ("The source that listed this
  release has been removed, so the release can't be fetched. Search again."), is switched off
  ("“Toloka” is switched off, so its releases can't be fetched. The owner can switch it on under
  Integrations.") or is of a type this build can't fetch from ("This version of the app can't fetch
  releases from “…”."); and 409 "This release was already sent to “qBittorrent”, so it isn't sent
  twice." when a job that didn't fail — anyone's, as the client is the household's — already sent
  it to that client, recognised by its source and the source's id for it, or by the info hash its
  listing states, since every search stores its own copy of a listing (MAM-13's decision 3,
  ADR 0006). Then the job is written `Created` — the request id, who sent it with their name copied,
  the release, and the client and the destination with their names and the category copied — with
  an audit row `DownloadRequested` (target kind `DownloadJob`, named by the release's title cut to
  200 characters with "…"), in one save, and sent (next entry); the answer is 201 with the job and
  its `Location` (`/api/downloads/{id}`) whatever came of it, a client that couldn't be reached
  included. `GET /api/downloads/{id}` reads the caller's own job; another user's answers a bare 404,
  like a missing one. A job is `{ id, releaseId, releaseTitle, sourceName, downloadClientName,
  destinationName, clientDestinationKey, status, clientJobId, error, createdUtc, statusChangedUtc }`,
  `status` being `Created` (being sent, or how that went isn't known), `Submitted` (the client took
  it; `clientJobId` is `null` while the client hasn't listed it), `NeedsLinking`, or `Failed` with
  `error` (`Downloading` and `Downloaded` are MAM-14's). Two copies of one request racing make one
  job: a unique index lets one commit, and the other answers its job. One migration
  (`AddDownloadJobRequests`) adds `RequestId`, `RequestedByUserId` (a foreign key to `Users` that
  sets null when the user is deleted, so their jobs stay with `RequestedByUserName`),
  `RequestedByUserName`, `InfoHashV1`, `InfoHashV2` and `ErrorOrigin` to `DownloadJobs`, and the unique
  index `IX_DownloadJobs_RequestedByUserId_RequestId`; a deleted user's request ids no longer collide, as
  SQLite never counts two nulls the same. It rebuilds `DownloadJobs` on upgrade, a table no build
  before MAM-13 wrote to (see Notes for upgraders).
  `DownloadsIdempotencyHttpTests.Create_twice_with_one_request_id_answers_the_same_job_and_sends_once`,
  `DownloadsIdempotencyHttpTests.Create_twice_at_once_with_one_request_id_makes_one_job_and_sends_once`,
  `DownloadsIdempotencyHttpTests.Create_with_a_request_id_used_for_another_download_is_409_and_writes_nothing`
  and `DownloadsIdempotencyHttpTests.Create_of_a_release_already_sent_to_the_client_is_409_and_sends_nothing`
  pin the request id and decision 3,
  `DownloadsValidationHttpTests.Create_to_a_client_that_can_not_take_the_release_is_400_and_asks_nothing`
  and `DownloadsValidationHttpTests.Create_to_an_unknown_destination_is_404_and_asks_nothing` the
  refusals, and
  `DownloadOptionsHttpTests.Options_lists_the_switched_on_clients_that_take_the_release_with_their_destinations`
  the dialog's choices.
- **A job's sending fetches the release, checks the category and the client's own list, and sends it
  once; the client's id or the torrent's info hash confirms it, never a name (MAM-13, ADR 0013).**
  Once the job is written, its sending runs to its end whether or not the browser is still there,
  and each step that fails ends the job `Failed` with its own sentence: the payload fetched from the
  source (MAM-10's fetch, the integration's current key put back) — the source's sentence when it
  fails, and "The release's download link can't be read with this installation's encryption key, so
  nothing was sent. Search again." for a link the installation's key can't open, logged as a warning
  naming the release's id only; its kind checked against the client ("“qBittorrent” can't take a
  magnet link, which this release turned out to be, so nothing was sent."); its info hashes worked
  out by Core's `TorrentInfoHashReader` — the SHA-1 (v1) and SHA-256 (v2, BEP 52) of a .torrent's
  info dictionary exactly as written, read defensively, or a magnet's `xt` topics (`urn:btih:` in
  hex or base32, `urn:btmh:1220…`) — and both stored on the job (`InfoHashV1`, `InfoHashV2`), as a
  client names a hybrid torrent by either; the destination's category confirmed live, since
  qBittorrent creates a category it lacks in its default folder ("“qBittorrent” has no category
  “Movies” any more, so nothing was sent: it would have created one in its default folder. Create
  the category again in “qBittorrent”, or the owner edits the destination."); the client asked
  whether it already has the torrent, which fails the job rather than adopting a download the app
  didn't add ("“qBittorrent” already has this torrent, so nothing was sent.", MAM-13's decision 4);
  and only then one submission. A receipt with the client's id makes the job `Submitted` with it.
  Without one, the client's list is read for the hashes, and once more after half a second when the
  torrent isn't listed yet: one job holding them makes it `Submitted` with that job's id, none
  `Submitted` without an id and with its hashes kept for MAM-14, and several, or a payload with no
  hash at all, `NeedsLinking` — the specification's linking by hand, which the app never guesses at
  (`ClientJobCorrelation`). A submission whose answer leaves open whether the client took it — no
  answer in time, the connection lost, a server error, an answer the app can't read — is never sent
  again: the list is read instead, once more after half a second when the torrent isn't listed yet,
  and the torrent listed there was taken (listed under several jobs, it needs linking); not listed,
  the job fails with the client's sentence. A refusal, refused credentials and a version too old
  fail it at once. Each outcome is a conditional write on a job still `Created`, never cancelled; an
  error is masked of the call's secrets once more and cut to 1000 characters with "…", and the log
  says the status only. A service's sentence — the source's or the client's, which can name its
  address — goes to the owner only, MAM-10's rule: anyone else who sent the release reads
  "“qBittorrent” couldn't take the release, so nothing was sent. Ask the owner to check
  “qBittorrent” under Integrations." (or "“Toloka” couldn't hand over the release, …" for the
  source), in the send's answer and every read of the job, and reads the app's own sentences as they
  are (`ErrorOrigin` says whose a job's error is; `DownloadsErrorVisibilityHttpTests`). A copy of a
  request racing the first, which finds the first's job only when it asks whether the release was
  already sent, is answered with that job, never refused as already sent. A send waits for the fetch
  and up to five calls to qBittorrent, each of their requests bounded at 15 seconds.
  `DownloadsSendHttpTests.Create_is_201_with_the_job_the_client_took_its_row_and_its_audit_row`,
  `DownloadsSendFailuresHttpTests.Create_whose_category_is_gone_fails_and_sends_nothing`,
  `DownloadsSendFailuresHttpTests.Create_for_a_torrent_qbittorrent_already_has_fails_and_sends_nothing`,
  `DownloadsSendFailuresHttpTests.Create_whose_add_answered_500_but_listed_the_torrent_records_it_submitted`
  and `DownloadsSendFailuresHttpTests.Create_with_two_jobs_listed_for_the_hash_needs_linking` pin
  it, `TorrentInfoHashReaderTests` and `ClientJobCorrelationTests` the hashes and the matching, and
  `DownloadsSecretsHttpTests.No_answer_log_job_or_audit_row_of_a_download_carries_a_planted_secret`
  that no API key, link token, password, session or tracker passkey reaches an answer, the log, a
  job's error or an audit row.
- **qBittorrent takes a release into the destination's category with automatic torrent management,
  and finds its torrents by info hash (MAM-13, ADR 0005, ADR 0010).** A submission signs in, or uses
  the kept session, and reads `app/version` first: the read renews a session qBittorrent has
  forgotten, so the write goes out on a session it has just accepted, and the version gate refuses a
  qBittorrent older than 4.4.1 before anything is posted. Then one `POST api/v2/torrents/add`, as
  `multipart/form-data` the way qBittorrent's Web UI sends it: the .torrent as a `torrents` file part
  (`release.torrent`, `application/x-bittorrent`) or the magnet as `urls`, `category` — the
  destination's key exactly as stored, never trimmed — and `autoTMM=true` (MAM-13's decision 2:
  under manual management, qBittorrent's default for a new torrent, a category alone decides no
  folder), and never `savepath`, `downloadPath`, `useDownloadPath`, `rename`, `tags`, `paused` or
  `stopped`. It is never sent again, a 403 included: without a session that is the sign-in
  qBittorrent asks for, and with one the session is dropped, the call fails and the next call signs
  in. The answer is read by its status and body shape, which covers 4.4.1 to 5.1 and 5.2 alike:
  `Ok.` (4.4.1 to 5.1) is taken without an id; `Fails.` is refused ("qBittorrent didn't add the
  release: it already has this torrent, or it couldn't use what it was sent."); a JSON object (5.2)
  is taken, its `added_torrent_ids` naming the job only when it lists exactly one id of 40
  hexadecimal characters; 202 (5.2) is taken and still pending; 409 (5.2) is "qBittorrent added
  nothing: it already has this torrent, or it couldn't use what it was sent."; 415 is "qBittorrent
  couldn't read the .torrent file it was sent (415 Unsupported Media Type)."; and any other 200 is
  "The service at this address answered, but not the way qBittorrent answers a new torrent." A
  magnet's text, and every tracker and source address in it as an echo would spell it, are masked
  like a session in whatever the call says, since a tracker's address can carry a passkey. The job
  lookup reads `app/version` the same way, then sends the hashes as qBittorrent names a torrent — the
  v1 hash, or the v2 one cut to 40 characters — lower-case, each once, joined by `|`, as
  `GET api/v2/torrents/info?hashes=…`, never the whole list; no hash asks nothing. It reads the
  answer as an allowlist: each torrent's hash, `infohash_v1` and `infohash_v2`, its name cleaned and
  cut to 1000 characters, its state mapped onto the contract's (`Queued`, `Downloading`, `Paused`,
  `Seeding`, `Completed` for `pausedUP` and `stoppedUP`, `Errored` for `error` and `missingFiles`,
  and `Unknown` for `moving` or a state it doesn't know), its progress from 0 to 1, its category and
  its content path; a torrent without a usable hash is skipped, and an answer that isn't an array of
  objects fails ("The service at this address answered, but not with qBittorrent's torrents.").
  `QBittorrentAdapterSubmitTests` pins the submission against every answer. Verified live on
  2026-10-02 against the reference installation (qBittorrent v5.2.4, Toloka through Prowlarr): a
  release sent from the Search page was fetched through Prowlarr, taken by `torrents/add` with the id
  qBittorrent listed, matching the v1 info hash the app worked out itself, and saved in the Movies
  category's folder (`/downloads/movies`) under automatic torrent management, with no save path sent.
  A first try of the same release failed in Prowlarr (its "Download failed" for Toloka's answer); the
  job recorded the feed's sentence, nothing reached qBittorrent, and the next Send went through. The
  answers of 4.4.1 to 5.1 to `torrents/add` are written from qBittorrent's source, and none was
  recorded.
- **The Download dialog on the Search page (MAM-13).** A release's **Download** opens it for every
  signed-in user. **Download client** lists only the clients the server offers for that release, and
  **Destination** the picked client's destinations; it opens on the default destination and the
  client that holds it, otherwise on the only client and its only destination, otherwise on the
  placeholders, with **Send** disabled and "Pick a download client and a destination first." A
  summary lists the source, the release, the client, the destination with its category, and
  "Existing copies — Kept: this version of the app never replaces or removes a file." **Send** comes
  first, **Cancel** last. The dialog makes the request id when it opens, from
  `crypto.getRandomValues` (a LAN address over plain HTTP is no secure context, so
  `crypto.randomUUID()` isn't there), sends the same id again after the server couldn't be reached
  or refused — a Send while the browser says it is offline is tried at once and fails into that
  sentence, never held back to go out on its own once the browser is online (`networkMode:
  'always'`) — and makes a new one only after a `Failed` job, whose sentence shows in the form with
  the pickers kept; a second click before **Sending…** shows sends nothing. Any other outcome
  replaces the form with a line — "qBittorrent took the release and lists it among its downloads.",
  "qBittorrent took the release, but hasn't listed it among its downloads yet.", "qBittorrent took
  the release, but the app can't tell which of its downloads it is, so it has to be linked by
  hand.", or "This release is already being sent to qBittorrent. Its outcome isn't known yet." — the
  summary and **Close**. With no client to offer, it says "No download client can take this release
  yet. The owner sets them up under Integrations and Destinations." The dialog is mounted only while
  it is open, so every opening is a new request.
- **The app follows every download it sent by reading its client every 2 seconds, until the
  download finishes, and a restart goes on where the rows are (MAM-14, ADR 0014).** A background
  service (`DownloadJobTracker`) runs a pass right after the app starts, then 2 seconds after each
  pass ends, so two never overlap; each pass reads the followed jobs from the database and keeps
  nothing in memory. Followed are `Submitted`, `Downloading`, the new `Paused`, and a `Created` job at
  least 5 minutes old — a send a stop interrupted, as no send takes that long, so a send still under
  way is never raced; `Downloaded`, `Failed`, `NeedsLinking` and the new `Cancelled` are never read
  again. Per client, one lookup of every job by its stored id and info hashes (each valid 40- or
  64-character hexadecimal value, lower-case, once, at most 50 to a request) through `FindJobsAsync`.
  A job found takes the client's word through the core's `DownloadJobFollowing`: downloading is
  `Downloading`, queued `Submitted` until the job has started and `Downloading` after, paused
  `Paused`, and a finished state is `Downloaded` — which ends the following — only with a progress of
  1, never either alone, as a re-check reports its own progress under a finished state; a finished
  state short of 1, `moving` and a state this build doesn't know move nothing but a `Created` job to
  `Submitted`, and a progress the client doesn't give keeps the stored one. The job keeps the client's
  current id, as qBittorrent renames a hybrid torrent once a magnet's metadata arrives, and the v2 hash
  the client lists when it had none; a job found twice is `NeedsLinking`. A job its ids don't find,
  sent with a v1 hash only, is looked for among its destination's jobs, once per destination and pass:
  ADR 0013's hybrid that qBittorrent built on libtorrent 2 renames to its v2 hash. An interrupted send
  with no hash stored fails without asking the client ("The app stopped while sending this download,
  before it could tell whether “qBittorrent” took it, so it isn't followed. Check “qBittorrent” before
  sending the release again."); found, it is followed from then on; found twice, it needs linking; not
  listed, it fails ("The app stopped while sending this download, and “qBittorrent” doesn't list it,
  so it isn't followed. Send the release again if you still want it."). A job's files are read when it
  has none stored or its status or progress changed, so a finished, idle or paused job isn't read
  every pass. They are read from the client before anything is written, then replaced by position in
  the same transaction as the job's own write, when anything differs, so a job never reads as
  `Downloaded` with the files of the pass before; a failed read keeps them as they are, and the job is
  written all the same. Once a files read or a destination listing times out, can't reach the client or
  throws, the pass asks that client nothing more: its other jobs' files stay as stored and a job that
  needs a listing shows that call's sentence. Every write a pass makes to a job — its row and its files
  together — is conditional on the status the pass read, so a cancel racing a pass wins and leaves both
  as they were; `StatusChangedUtc` changes only with the status, and a job nothing happened to is not
  written. Each status change logs the job's id with both statuses, never a title, a path, an address
  or a sentence; one client's bug costs that client the pass, and a pass that throws is logged and
  the next one runs as usual. One migration (`AddDownloadJobTracking`) adds `Progress`, `BlockReason`
  and `BlockReasonOrigin` to `DownloadJobs`, the table `DownloadJobFiles` (the job, the file's place in
  the client's list — unique per job — its path exactly as the client names it, relative to the job's
  folder, its size, its progress and whether it is wanted, deleted with its job), a partial index of
  the followed jobs (`IX_DownloadJobs_Followed`, its filter the core's list of followed statuses) and
  `IX_DownloadJobs_RequestedByUserId_CreatedUtc` for the Jobs list; it rebuilds no table (see Notes for
  upgraders). `DownloadJobTrackingHttpTests.Poll_maps_what_qBittorrent_reports_onto_the_job`,
  `DownloadJobTrackingHttpTests.Poll_of_a_finished_download_ends_its_following_and_never_asks_about_it_again`,
  `DownloadJobTrackingHttpTests.Poll_stores_a_jobs_files_and_reads_them_again_only_when_the_job_moved`,
  `DownloadJobTrackingHttpTests.Poll_reads_the_files_before_the_job_turns_downloaded_and_writes_them_with_it`,
  `DownloadJobTrackingHttpTests.Poll_of_a_client_that_stops_answering_after_the_lookup_asks_it_nothing_more_that_pass`,
  `DownloadsCancelHttpTests.Poll_racing_a_cancel_while_it_reads_the_files_writes_neither_the_job_nor_its_files`
  and `DownloadJobTrackingHttpTests.Poll_writes_nothing_for_a_job_nothing_happened_to` pin the
  following,
  `DownloadJobRecoveryHttpTests.Poll_after_a_restart_follows_the_jobs_a_previous_run_left_and_asks_about_no_ended_one`,
  `DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_qBittorrent_took_follows_it_from_then_on`
  and `DownloadJobRecoveryHttpTests.Poll_finds_a_hybrid_torrent_qBittorrent_renamed_after_its_magnet_through_its_destination`
  the restart and ADR 0013's gaps, `DownloadJobFollowingTests` and `ClientJobCorrelationTests` the
  mapping and the matching, and `DownloadJobPersistenceTests` the new rows and indexes.
- **A followed job that isn't moving on keeps its status and says why, and stays followed (MAM-14's
  decisions 2 and 3, ADR 0014).** A torrent qBittorrent reports an error for (`error`,
  `missingFiles`) shows "“qBittorrent” reports an error for this download, so it isn't moving on.
  Check it in “qBittorrent”: the job goes on by itself once the error clears."; one qBittorrent no
  longer lists, "“qBittorrent” doesn't list this download any more. It is still followed in case it
  comes back; to send the release again, cancel this job first.", gone once the torrent comes back
  with the same hash; and one taken without an id and still unlisted a minute later — a qBittorrent
  before 5.2 lists a torrent a moment after it took it — "“qBittorrent” took this download but hasn't
  listed it since. It is still followed; to send the release again, cancel this job first." A client
  that can't be asked never makes a job missing: every followed job of it shows why and keeps its
  status — "The download client “qBittorrent” was removed, so this download can't be followed. Cancel
  the job to stop tracking it.", "“qBittorrent” is switched off, so this download isn't followed for
  now. The owner can switch it on under Integrations.", "This version of the app can't follow
  downloads in “…”.", the sentence for a stored secret the app can't read, qBittorrent's own sentence
  for a call that failed, masked of the call's secrets once more, or "The app couldn't read what
  “qBittorrent” answered." for an adapter that throws, logged with its type and stack but never its
  message. A reason is written only when it differs from the stored one, and cut to 1000 characters
  with "…". As for a failed job's error (MAM-13), qBittorrent's own sentence is the owner's: anyone
  else reads "“qBittorrent” couldn't be asked about this download. Ask the owner to check
  “qBittorrent” under Integrations." (`BlockReasonOrigin`). After qBittorrent refused the
  credentials, the passes never sign in again until the owner changes them or a connection test with
  them works, so they never add to its ban.
  `DownloadJobTrackingHttpTests.Poll_of_a_torrent_qBittorrent_reports_an_error_for_keeps_its_status_with_why_until_the_error_clears`,
  `DownloadJobMissingHttpTests.Poll_of_a_torrent_removed_from_qBittorrent_keeps_the_job_with_why_and_follows_it_until_it_comes_back`,
  `DownloadJobMissingHttpTests.Poll_of_a_client_that_can_not_be_asked_never_makes_a_job_missing`,
  `DownloadJobMissingHttpTests.Poll_after_qBittorrent_refused_the_credentials_never_signs_in_again_until_the_owner_tests_them`,
  `DownloadJobMissingHttpTests.Poll_of_a_removed_torrent_whose_destination_listing_fails_shows_the_clients_sentence_never_missing`,
  `DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_whose_destination_listing_fails_stays_created_with_why_until_it_answers`
  and `DownloadJobMissingHttpTests.Get_and_list_show_a_clients_own_sentence_to_the_owner_and_the_apps_to_a_member`
  pin it: a failed destination listing, like a failed lookup, decides no job.
- **Every signed-in user lists their own download jobs and a job's files, and cancels a job, which
  only stops following it: nothing is sent to the client (MAM-14, ADR 0014).** A job stays its
  sender's (MAM-14's decision 1): the list, the files and Cancel answer the caller's own jobs, the
  installation owner's included, and another user's job is the same bare 404 as one that doesn't
  exist. `GET /api/downloads?status=&page=1&pageSize=25` answers
  `{ items, totalCount, page, pageSize }`, newest first (by when it was sent, then by id), each item a
  job as `GET /api/downloads/{id}` answers it; `status` is a status by name in any letter case, a
  blank one lists every status, and anything else is 400 "“x” is not a download status. Use Created,
  Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled."; a page below 1 or a
  page size outside 1–100 is 400. `GET /api/downloads/{id}/files?page=1&pageSize=25` answers the job's
  files as its client last listed them, in the client's order, each
  `{ position, path, sizeBytes, progress, isWanted }`, and an empty page while the client hasn't
  listed any; the job is checked before the paging. `POST /api/downloads/{id}/cancel` makes a job
  still followed, or one that needs linking, `Cancelled`, clearing its reason, with an audit row
  `DownloadCancelled` (target kind `DownloadJob`, named by the release's title), in one transaction,
  and answers 200 with the job; a job that already ended is 409 "This download is already finished,
  so there's nothing to stop following.", "This download has already failed, so there's nothing to
  stop following." or "This download is already cancelled, so there's nothing to stop following.",
  and changes nothing. A `Created` job counts as followed only once it is 5 minutes old, as for the
  poller: younger, its send may still be under way, and it is 409 "This download is still being sent,
  so it can't be cancelled yet. Try again in a few minutes.", changing nothing — the age is checked in
  the same conditional update, so a cancel never throws away the outcome of a send still working. It is a conditional update: a pass that moved the job on since it was read
  doesn't stop it, and no later pass undoes it. qBittorrent is never asked: `IDownloadClient` has no
  member that removes, deletes, stops or pauses anything, and `DownloadClientNeverRemovesTests` keeps
  it so. `DownloadsListHttpTests.List_answers_the_callers_jobs_newest_first_with_every_field`,
  `DownloadsListHttpTests.List_of_the_installation_owner_holds_only_their_own_jobs`,
  `DownloadJobFilesHttpTests.Files_of_the_callers_job_are_200_in_qBittorrents_order_as_the_poller_read_them`,
  `DownloadsCancelHttpTests.Cancel_is_200_with_the_job_cancelled_its_row_and_its_audit_row`,
  `DownloadsCancelHttpTests.Cancel_sends_nothing_to_qBittorrent_and_no_later_pass_asks_about_the_job`,
  `DownloadsCancelHttpTests.Poll_racing_a_cancel_never_undoes_it`,
  `DownloadsCancelHttpTests.Cancel_of_a_created_job_still_being_sent_is_409_with_why_and_changes_nothing`
  and `DownloadsCancelHttpTests.Cancel_of_another_users_job_is_a_bare_404_even_for_the_installation_owner`
  pin them, and `DownloadsSecretsHttpTests.No_answer_log_job_or_audit_row_of_a_download_carries_a_planted_secret`
  now also runs two passes, the second against a redirect that echoes the session and the password,
  and reads the list, the files and a cancel.
- **qBittorrent reads a torrent's files and a category's torrents (MAM-14, ADR 0005).** Both sign in,
  or use the kept session, and read `app/version` with its gate first, as every read does. A torrent's
  files are `GET api/v2/torrents/files?hash=…` with the torrent's id, lower-case and cut to 40
  characters, read as an allowlist in qBittorrent's order: the `name` kept exactly — never trimmed or
  cleaned, as qBittorrent's own names carry double spaces — and up to 4096 characters, a file without a
  usable one failing the whole answer; the `size` as a whole number of bytes, beyond 32 bits too; the
  `progress` from 0 to 1; and `priority` 0, qBittorrent's "do not download", as not wanted. A 404 there
  means the torrent is gone ("qBittorrent doesn't have this torrent any more."), a torrent still
  fetching its metadata lists `[]`, and an answer that isn't an array of objects fails ("The service at
  this address answered, but not with qBittorrent's files."). A category's torrents are
  `GET api/v2/torrents/info?category=…`, the key sent exactly as stored, read as the lookup by hash
  reads them; a blank key is refused before anything is sent, as qBittorrent would read it as "no
  category". A key holding a `+` is never sent: qBittorrent's request parser percent-decodes a query
  value and only then reads `+` as a space, so `%2B` arrives as a space too and would ask for another
  category; such a key asks for every torrent and keeps those of exactly that category. The test
  qBittorrent decodes a query the same way. The adapter still sends one write, `torrents/add`.
  `QBittorrentAdapterFilesTests.ListJobFiles_reads_the_three_files_of_the_sample_exactly` reads the
  recorded pack whole, and `QBittorrentAdapterFilesTests` and `QBittorrentAdapterDestinationJobsTests`
  pin every answer.
- **The Jobs page and a job's details (MAM-14).** **Jobs** in the header, last, for every signed-in
  user, lists their own jobs, newest first: the release, which opens the job; its status, with why a
  followed job isn't moving on as a warning line and why it failed as an error line; its progress as a
  bar and a percentage that never rounds up to 100 before the download is complete, or "Not known yet";
  where it went (the client and the destination); and when it was sent. It filters by status on the
  server, starting again at page 1, pages with a page size remembered per device, turns into cards on
  a phone, and asks again every 2 seconds while it shows a job the app still follows. With no job
  yet it says "No download jobs yet. Send a release from Search, and it is followed here until it
  finishes.", linking to Search. A job's page shows its source, client, destination with its category,
  status, progress, when it was sent and its status last changed, and the client's id, then its files
  — the path, the size, the progress, and **Skipped** for one the client doesn't download — paged,
  asked for again when the job moves or while a followed job lists none yet. **Cancel job** is on both
  pages for a job still followed or one that needs linking; a `Created` one gets it only once it is 5
  minutes old, judged at the time of the page's last answer, since the server refuses a younger one
  with its sentence. It asks first — "The torrent and its files
  stay in qBittorrent: nothing is paused, stopped or removed there." — with **Cancel job** and **Keep
  following**. A job that isn't the caller's, or an address that isn't a job's, reads "There is no
  download job of yours at this address. It may belong to someone else, or the link is wrong."
  `JobsPage.test.tsx`, `JobsPage.polling.test.tsx`, `JobDetailsPage.test.tsx` and
  `JobProgress.test.tsx` pin it.
- **Phase A's acceptance bar holds on the reference installation, and the README and the release
  note say which integrations were tried live and which weren't (MAM-15).** Checked on 2026-10-03
  through the app run from the repository against Jellyfin 12.1.0, Prowlarr 2.6.5 serving the
  Toloka.to indexer, and qBittorrent 5.2.4. The header search "Sherlock Holmes (2009)" found the
  movie inside `/media/movies/Guy Ritchie. Collection` where it is, matched by title and year,
  beside Toloka's 12 releases, six of them matched to it. With the Torznab feed pointed for one
  search at an indexer Prowlarr doesn't have (`/999/api`, its key kept), the Search page still
  showed that match and said under Available releases "Prowlarr · Answered in a way this app can't
  read. Nothing answers Torznab at this address (404 Not Found)…"; the address was then put back,
  and both changes are `IntegrationUpdated` audit rows. MAM-13's request of 2026-10-02, sent again
  with its request id, answered 200 with that same job, and the same release (Toloka's topic
  `t126881`) from a new search answered 409 "This release was already sent to “qBittorrent”, so it
  isn't sent twice."; the database still held two jobs and two `DownloadRequested` audit rows, and
  the following passes still found the torrent missing from qBittorrent, removed there since
  MAM-13's check, so nothing was added. The destination Movies is qBittorrent's category `Movies`,
  saving to `/downloads/movies` (`/media/movies` for the app) inside the Jellyfin library "Фільми",
  and MAM-13's send landed there under automatic torrent management with no save path. A library
  sync afterwards (876 items, 872 files) listed each of the 866 files of the 2026-10-01 sync at the
  same path with the same size; the six new files are a collection added outside the app. Not tried
  live yet: Jackett and other gateways and indexers, a feed that searches by IMDb or TMDb id, lists
  them or gives magnet links, qBittorrent before 5.2 or on a Windows host, a Jellyfin older than
  12.1 or one that merges versions, a download followed to `Downloaded`, a paused one, a job's files
  and a job that needs linking, and the file inspection on the installation's own files. The README
  (**Tested with**) and the release note list both; the README's and the roadmap's MAM-12 lines no
  longer say the Search page wasn't tried against the reference installation.
- **Every signed-in user changes their own password by giving the current one, which ends their
  other sessions and keeps this one (MAM-16, ADR 0016).** `POST /api/auth/password` takes
  `{ currentPassword, newPassword }` and answers 204. In one transaction it stores the new hash,
  writes a `UserPasswordChanged` audit row (the user is both actor and target) and revokes every
  other session of the user, so another device is refused with its next request and its refresh;
  the session the request came through, named by the access token's `sid`, stays, and the refresh
  cookie is never read. A blank current password is 400 "Enter your current password." and a new
  one that breaks the rule is 400 with the rule's sentence, both before any hash runs; a wrong
  current password is 400 "The current password is incorrect." after exactly one hash, or none for
  one over 128 characters. It is never a 401, which the web app would take for an expired token. An
  owner's reset, disable or delete that commits while the current password is being checked wins,
  and the change gets the wrong-password answer, as a sign-in does. Ten attempts a minute per user,
  from whatever address and counted apart from sign-in, then 429 with the sign-in sentence and
  `Retry-After: 60`: a stolen access token can't spread its guesses over many addresses, and a
  mistyped password doesn't use up the sign-ins of everyone behind the same router. `GET /api/auth/me`
  now answers `{ id, userName, isOwner, createdUtc, lastSignInUtc, sessionExpiresUtc }`, the last
  being the end of the token's own session; sign-in, refresh and setup answer the user as before.
  `AuditAction` is stored by name, so there is no migration, and ADR 0002 gains a section on this
  fifth way a session ends.
  `AuthPasswordHttpTests.ChangePassword_keeps_the_calling_session_and_ends_the_users_other_sessions_at_once`,
  `AuthPasswordHttpTests.ChangePassword_overtaken_by_a_change_to_the_user_during_the_password_check_is_400_and_the_owners_change_stands`,
  `AuthPasswordHttpTests.ChangePassword_is_rate_limited_per_user_whatever_the_client_address` and
  `AuthMeHttpTests.Me_says_when_the_calling_session_ends_not_another_session_of_the_user` pin it.
- **A Profile page for every signed-in user (MAM-16).** **Profile** in the user menu, first under
  the user's name, which links there too and is tinted while the page is open, opens `/profile`:
  the user's initial, name (with an Owner badge for the owner) and what their role may do; an
  Account card with the user name, the role, when the account was created and when it last signed
  in on any device, in the browser's locale; a Password card; the theme, with its labels, saved in
  this browser; and a Session card that says when this session ends unless it is used again, with
  **Sign out**. **Change password** opens a form for the current password, the new one (8–128
  characters) and the new one again. It checks those before sending anything and puts each sentence
  under its field; a 400 the server still answers can only be about the current password, so its
  sentence goes there, while too many attempts or an unreachable server show in a banner above the
  buttons. On success the form closes and a banner at the top says the other devices are signed out
  and this one stays signed in. The web app now refreshes an expired access token for
  `/api/auth/me` and `/api/auth/password` as for any other call, and skips that only for sign-in,
  refresh, sign-out and setup, so Profile left open past the 15-minute token doesn't read "Sign in
  to continue." `ProfilePage.test.tsx`, `ChangePasswordCard.test.tsx`, `UserMenu.test.tsx`,
  `api.test.ts` and `App.test.tsx` pin it.
- **Light, Dark or System, picked in the user menu (MAM-16).** MAM-1 applied the operating
  system's dark-mode preference before the first paint, and nothing could change it. The menu's
  **Theme** row now stores Light or Dark in `localStorage` under `mam:theme`, the key the pre-paint
  script in `index.html` already read, and removes the key for System, so a reload opens in the
  picked theme with no flash while the script and its CSP hash stay as they were. A store
  (`lib/theme-store.ts`) sets `.dark` on `<html>`, follows the operating system live while the
  theme is System, whether or not the menu is open, and takes a choice made in another tab. The
  switch is a shared `SegmentedControl` of native radio buttons: one tab stop, and the arrow keys
  move the choice. `theme-store.test.ts`, `ThemeSwitch.test.tsx` and `SegmentedControl.test.tsx`
  pin it.
- **A strip under the header says when the server can't be reached, and goes once it answers
  (MAM-16).** Before, only the session gate said so, while a tab restored its session; a page open
  when the server stopped filled with red lines. Now the first request that gets no answer — or a
  502, 503 or 504 with no JSON body, which is what the Vite dev proxy and a reverse proxy answer for
  a stopped API — shows a strip under the header, sticky with it: "The server could not be
  reached. Check that it is running, then try again." and "Trying again every few seconds."
  with **Retry now**. It asks `GET /api/health` at once and every 5 seconds, also while the browser
  says it is offline, and goes on the first answer to any request; the page keeps what it showed,
  and the lists and panels that failed meanwhile are asked for again by themselves. An action taken
  meanwhile reads the same sentence, now for the proxies' empty 502, 503 and 504 too. The strip
  stays hidden while the session gate or the sign-in page says it themselves. One axios interceptor
  in `lib/api.ts` feeds `lib/connection-store.ts`. `OfflineStrip.test.tsx`, `connection-store.test.ts`,
  `api.test.ts` and `utils.test.ts` pin it.

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
- **MAM-10's release search keeps more of each listing, and asks and stores through the code the
  stored search uses (MAM-12, ADR 0006, ADR 0011).** `POST /api/releases/search` answers exactly as
  before, but each release row it stores now also keeps its seeders and peers, its place in its
  source's answer and the ids its listing gives, with no search source
  (`ReleaseSearchHttpTests.Search_stores_each_releases_place_seeders_peers_and_listed_ids_outside_any_stored_search`).
  Asking the sources, storing in one transaction and the prune are one implementation for both
  searches (`ReleaseSources`, `ReleaseStore`), moved rather than rewritten: its prune also removes the
  stored searches more than a day old, and its log lines, worded as before, now log under the
  categories `MediaAcquisitionManager.Api.Services.Releases.ReleaseSources` and `….ReleaseStore`.
- **`ReleaseSourceNotSearchedReason` moved to the core (MAM-12).** From
  `MediaAcquisitionManager.Api.Contracts` to `MediaAcquisitionManager.Core.Enums`, with the same
  members, values and docs, since a stored column (`SearchSources.NotSearchedReason`) holds it now;
  the JSON is unchanged.
- **`IMetadataResolver` stays unimplemented (MAM-12, ADR 0005).** MAM-7 said MAM-12 would first
  implement it from the media server's data. MAM-12 matches a search and its releases against the
  library snapshot MAM-8 imported instead, which needs no call to the media server, so the contract's
  summary says so and its members are as they were.
- **`IDownloadClient` gets its submission back, and finds the client's jobs by info hash instead of
  listing them (MAM-13, ADR 0005).** MAM-11 left `SubmitAsync`, `ListJobsAsync` and
  `ListJobFilesAsync` out for MAM-13 and MAM-14. `SubmitAsync` is back, sent exactly once and never
  again by the call or a retry of it. `FindJobsAsync(settings, infoHashes)` takes the place of MAM-7's
  `ListJobsAsync`: the client's jobs that hold any of the hashes, each with every hash the client
  lists for it (`ClientJob.InfoHashes`, new). A submission is confirmed by the torrents that hold its
  hashes, an "every job" listing would read a whole client to find one torrent, and following a job
  by the id stored on it (MAM-14) asks the same question. `DownloadSubmission.ExpectedInfoHash` is now
  the hash the app worked out from the payload, never the listing's claim. `ListJobFilesAsync` stays
  MAM-14's.
- **The Search page's Download sends now (MAM-13).** MAM-12 greyed it out with "Sending a release to
  a download client comes in a later update."; it opens the Download dialog (see Added), and that
  sentence is gone.
- **The qBittorrent adapter sends one write (MAM-13, ADR 0010).** MAM-11's adapter sent nothing but
  the sign-in and GETs; a submission adds the one `torrents/add`, never sent again. MAM-11's renewal
  of a kept session qBittorrent forgot — sign in once more and send "that one read again, once" —
  still covers reads only: a submission reads `app/version` first, which renews such a session, so
  the write goes out on a session qBittorrent has just accepted, and a 403 to it is its answer. The
  destinations still send only the sign-in and GETs, and so does the job lookup.
- **The payload fetch is called (MAM-13, ADR 0004).** MAM-7 sealed a release's download link "ready
  for the server-side fetch of MAM-13", and MAM-10 built the fetch "the download step will use".
  Sending a release now calls it, once per job, after the job is written and before the client is
  asked anything, with the integration's current key put back; what it fetched goes to the download
  client and nowhere else.
- **`IDownloadClient` gets its files member back and lists a destination's jobs, and has no member
  that removes (MAM-14, ADR 0005, ADR 0014).** `ListJobFilesAsync(settings, clientJobId)`, which
  MAM-11 left out and MAM-13 left to MAM-14, is back: the job's files in the client's order, `[]`
  while the client doesn't know them, and `NotFound` for a job the client no longer has.
  `ListDestinationJobsAsync(settings, destinationKey)` is new: every job of one destination, each with
  every hash the client lists for it, for a job a client renamed past what its stored hashes find.
  `ClientJobFile.ServicePath` is now `RelativePath`, the path relative to the job's folder exactly as
  the client names it — not a service path, which nothing had read yet. No member removes, deletes,
  stops or pauses a job or its files; the one write is still `SubmitAsync`.
- **A download job has two more statuses, its progress and why it isn't moving on (MAM-14).**
  MAM-13 left `Downloading` and `Downloaded` to MAM-14; the following writes them now, and
  `DownloadJobStatus` gains `Paused` (7) and `Cancelled` (8), stored by name like the others. A job's
  JSON, in every answer that carries one, gains `progress` (0 to 1 as the client last reported it,
  `null` until it has) right after `status`, and `blockReason` (why a followed job isn't moving on,
  `null` when nothing blocks it) right after `error`:
  `{ id, releaseId, releaseTitle, sourceName, downloadClientName, destinationName,
  clientDestinationKey, status, progress, clientJobId, error, blockReason, createdUtc,
  statusChangedUtc }`.
- **A cancelled job no longer counts as already sent (MAM-14, ADR 0013).** MAM-13's decision 3
  refuses a release a job that didn't fail already sent to the client; a `Cancelled` job no longer
  counts, like a `Failed` one, as cancelling is how a person sends a release again — and while the
  torrent is still in qBittorrent, decision 4 still refuses a second copy. A `Paused` job counts.
  `DownloadsIdempotencyHttpTests.Create_of_a_release_whose_earlier_job_was_cancelled_is_201_and_sends_it`
  and `DownloadsCancelHttpTests.Cancel_lets_the_release_be_sent_again_once_qBittorrent_no_longer_has_it`
  pin it.
- **The Download dialog knows a paused and a cancelled job, and a send refreshes the Jobs page
  (MAM-14).** Sending again under the same request id answers the job already made, which may be
  paused in qBittorrent or cancelled by then: the dialog says "qBittorrent took the release, but its
  download is paused there." or "qBittorrent took the release, but its job has been cancelled since,
  so the app doesn't follow it." as a warning. Every send that answers makes the Jobs list ask again.
- **Error sentences in the dark theme are a lighter red (MAM-16).** The dark `--destructive`
  (#dc2626) reaches only 3.85:1 on the dark card, short of the 4.5:1 WCAG AA wants for text, so every
  red sentence (sign-in, settings lists and dialogs, the home page, a connection test, a failed sync)
  now takes the new `--text-danger` token: #f87171 in the dark theme, and `--destructive` as before
  in the light one. Fills, borders and the red Delete and Remove buttons keep `--destructive`. The
  selected and warning tints are tokens too (`--tint-selected`, `--tint-warning`,
  `--tint-warning-border`) with the colours they had, and `design-tokens.test.ts` fails a sheet that
  colours text with `--destructive` or writes those tints out again.
- **Every paged list shows the range on screen and numbered pages (MAM-16).** MAM-7's pager read
  "Page 2 of 5" beside Previous and Next. Under Users, Integrations, Destinations, Library, Search,
  Jobs and a job's files it now shows the range on the left ("26–50 of 812", from each list's
  `totalCount`), ‹ the page numbers › in the middle — the first and last page and the current one
  ±1, with "…" for the pages between, the current page marked `aria-current="page"` — and Per page
  on the right. The arrows are icon buttons named "Previous page" and "Next page". A list with no
  items shows no pager, and a page past the end of a list that shrank says "Page 3 of 2" rather than
  a range that ends before it starts. Below 43rem the numbers take a row of their own above the
  range and Per page, and on the narrowest phones they wrap rather than widen the page.
- **The header stays at the top of the window, with the version under the app name (MAM-16).**
  MAM-1's header scrolled away with the page, with the version beside the user menu. It is now
  sticky, in three columns: the logo with the app name and the version under it on the left (one
  home link, named "Media Acquisition Manager v0.1.0"), the search field (see the next entry) and
  the **Library** and **Jobs** links in a centre column of at most 42rem, and the user menu on the
  right. Each link carries its label as a title. Under 48rem the header shows the logo alone and the
  links as icons, under 480px the search field takes a row of its own, and a focused control or an
  anchor stops below the header rather than behind it.
- **A search starts in a field in the header, and the Search page narrows it (MAM-16, ADR 0015).**
  MAM-12's **Search** link and the page's Title field are gone. Every page has a search field in the
  header once someone is signed in, and pressing `/` anywhere else on the page jumps to it — not
  while typing in a field, holding Ctrl, Cmd or Alt, or with a dialog open. Enter opens
  `/search?q=<the line>`, and the page makes that search at once and puts its id in the address in
  place of the line, so Back goes to the page before and never asks the sources again (ADR 0011);
  React's StrictMode doesn't make it twice, and when the person searches again or goes Back before
  an answer comes, the newest line wins; Forward to that line again waits for its search or shows the
  answer that came meanwhile, and asks again only after a refusal. Only a year from 1900 to 2099 in
  brackets ending the line becomes the year: "Interstellar (2014)" searches "Interstellar" of 2014,
  while "Blade Runner 2049" and "Interstellar 2014" are titles as typed, and an id typed there is a
  title too (ADR 0015, with a section added to ADR 0012). The page says "Results for
  “Interstellar”." in bold, with "Searching…" beside it while the search is made; its form keeps
  **Year**, **IMDb id** and **TMDb id** and **Apply** makes a new search with the same title as a
  new history entry. A refusal shows the server's sentence in a banner under the form that
  **Dismiss** clears. Without a search, the page says how to start one. On the Search page the field
  shows the title of the search on screen, read from the cache without asking the server.
  `search-query.test.ts`, `HeaderSearch.test.tsx` and `SearchPage.test.tsx` pin it.
- **The user menu says who is signed in and holds the theme (MAM-16).** MAM-4's menu button showed a
  user icon and the name; it is now a pill with the user's initial, the name (hidden on phones, as
  before) and a chevron. The open menu starts with the user name over "Owner" or "Member", the
  version and the home page's readiness words ("Ready", "Ready, with warnings", "Not ready") beside a
  dot in the status colour. It asks `GET /api/health/ready` only while it is open, and leaves the
  status out until the answer arrives and after a check fails. **Profile** follows (see Added), then
  the owner's settings under a separator (a member has none, and no separator), then the **Theme**
  row and **Sign out**. It is still a disclosure that closes on Escape, a click outside and the focus
  leaving it.
- **A library card shows its versions as rows, or side by side in a table (MAM-16).** MAM-8's card
  listed every version open, with all its files, one under another. Each version is now a row: its
  edition label ("Version 2" when it has none, by its place in the card), its file summed up in
  chips and its size. The chips are the resolution class from 2160p down to 480p, read from the
  width as much as the height (1920×800 is 1080p), the video codec, the dynamic range and the
  language of every audio track ("UKR · UKR · ENG"); a file that gives none of them reads "No video
  track" or "Unknown" instead. A row opens to the file lines the card showed before, one row at a
  time; a work of one version shows it open, as a row that is no button. From two versions on, a
  **Rows** / **Table** switch under the badges shows a table instead: a column per version and a row
  each for resolution, video, HDR, bit rate, duration, audio and subtitles (a track a line), size
  and file, over the note that size and file come from Jellyfin and the rest from ffprobe. A file
  the app hasn't read, or couldn't, says so in its row and in the table's media rows, never "No
  audio tracks"; one it has no path for says so in its row, and in the table unless an earlier
  reading left its facts. A version of several files is summed up by its first, with the sizes added
  up and every file listed; one without a file says so in a row that doesn't open. The choice holds
  for every card on the Library and Search pages at once and is kept in this browser under
  `mam:versionsView`; below 48rem the switch is hidden and the versions are always rows (owner's
  choice). The search page's Details dialog lists the versions as before.
  `LibraryWorkCard.versions.test.tsx`, `use-versions-view.test.ts`, `use-narrow-screen.test.ts` and
  `media-labels.test.ts` pin it.
- **Every failed request shows one of a few error states, each with a way on (MAM-16).** A list,
  panel or dialog whose first load fails shows what failed and the server's sentence in a box in its
  place, with **Try again**, which reads "Retrying…" while it asks, and the box stays through the
  polls that ask again meanwhile instead of turning into "Loading…". A 400, 403 or 404 gets no Try
  again: an expired search, someone else's job, a member on an owner's page, a filter the server
  refuses. Before, each was one red sentence: the server's or the page's, never both. A refresh that
  fails over data on screen keeps the banner it had and gains **Retry**; a refused row or page
  action (Remove, Disable, Delete, Cancel job, Test) and a connection test's answer are banners
  above the list that **Dismiss** clears, and a test that didn't connect now reads as an error, not
  as news; one that connected, there and in the integration dialog, reads in the text colour beside
  a green icon, as green text fell short of the 4.5:1 contrast text needs. A refused save, sign-in
  or setup is a banner inside the form, above its buttons, with every field kept. Two passwords that
  differ on Setup are said under Confirm password, which is marked invalid until either is edited,
  and a size filter that isn't a size marks its box red. A refused Sync now or Inspect files is a
  red status line on its row, with an icon. Sign in, Setup, Add user, Reset password and the
  Integrations, Destinations and Download dialogs follow the same rules. The shared pieces are
  `Banner`, `ErrorState`, `Field`, `FieldErrorLine`, `RetryButton` and `useLoadRetry`, and the old
  `.settings-banner`, `.settings-error`, `.settings-dialog-error`, `.auth-error` and `.home-banner`
  lines are gone. Every page's and dialog's tests pin it.
- **Add user and Reset password say each refusal where it belongs, and the Users page says when a
  password was reset (MAM-16).** MAM-4's two dialogs showed every refusal as one red line under the
  form. Two passwords that differ are now said under **Confirm password**, marked invalid until
  either password is edited, and a taken name — the 409 — under **User name** until the name is
  edited. Every other sentence is a banner above the buttons, with every field kept: a user-name or
  password rule (both are a 400 that names its field only in English, and the dialogs copy none of
  the server's rules to place them), a user who is gone, a server that can't be reached. A reset
  that goes through closes its dialog, and a banner above the list says "The password of olena was
  reset." until **Dismiss** or the next action; a refused reset stays in its dialog with what was
  typed. `CreateUserDialog.test.tsx`, `ResetPasswordDialog.test.tsx` and `UsersPage.test.tsx` pin
  it.

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
- **Applying `AddStoredSearches` rebuilds the `Releases` table, and logs EF Core warning 20410 once
  (MAM-12).** Giving each release a foreign key to the search source that found it makes SQLite
  rebuild `Releases`, which EF Core does in three transactions with foreign keys off between them: it
  copies the rows into `ef_temp_Releases` and commits, switches foreign keys off, drops `Releases`,
  renames the copy and commits, then switches them on again and recreates the indexes. So the log
  shows `Microsoft.EntityFrameworkCore.Migrations[20410]` for `PRAGMA foreign_keys = 0`, as for
  `AddLibraryInventory`. A process killed between the transactions leaves the migration half
  applied, and the next start can't apply it again without a repair by hand — the reason ADR 0009
  kept `PhysicalFiles` without such a key. It is accepted here because `Releases` holds only a day
  of search results, a few thousand rows at most, so the copy takes a moment: let the first start
  after the upgrade finish before you stop the container. Releases stored before the upgrade keep no
  seeders, place or ids and belong to no search; the next search a day later removes them, as no
  download job uses one yet. Searching needs no setting: the Search page works once a release source
  is switched on under Integrations, and its library side once the library is synced.
- **Applying `AddDownloadJobRequests` rebuilds the `DownloadJobs` table, and logs EF Core warning
  20410 once; it is harmless (MAM-13).** Giving each job a foreign key to the user who sent it makes
  SQLite rebuild `DownloadJobs`, which EF Core does with foreign keys off outside the migration's
  transaction, so the log shows `Microsoft.EntityFrameworkCore.Migrations[20410]` for
  `PRAGMA foreign_keys = 0`, as for `AddLibraryInventory` and `AddStoredSearches`. Nothing can be
  lost to it: no earlier build writes a download job, so the table rebuilt is empty everywhere.
  There is nothing to do.
- **Sending needs a download client and a destination, and nothing else to turn on (MAM-13).** Once
  the owner has a switched-on qBittorrent (4.4.1 or later) under Integrations and at least one of its
  destinations under Destinations — the steps of MAM-11 above — every signed-in user's **Download**
  on the Search page sends to it, offering the default destination first. Each torrent is added with
  automatic torrent management, so it saves in its category's folder even when qBittorrent's default
  for new torrents is manual; the destinations' check already shows where each category saves.
- **Applying `AddDownloadJobTracking` rebuilds no table, so it logs no warning 20410 (MAM-14).** It
  adds three nullable columns to `DownloadJobs`, the `DownloadJobFiles` table and three indexes; the
  jobs already there keep everything they had, with no progress or reason until the first pass.
- **Following needs nothing turned on, and picks up the jobs MAM-13 left (MAM-14).** The passes start
  with the app. The first ones follow every job already `Submitted`, find the id of one qBittorrent
  took without listing it, and settle a job left `Created` by a stop in the middle of its sending:
  followed if qBittorrent has it once, `NeedsLinking` if twice, and `Failed` with the reason if it
  doesn't or the app never learnt what it sent. A job `Failed` or `NeedsLinking` stays so. A
  qBittorrent that has refused the app's credentials is not signed in to again by the passes:
  correct them under Integrations, or test the connection there once qBittorrent accepts them, and
  its jobs move on with the next pass.
