# Media Acquisition Manager — design system

Self-hosted web app for people who pick media releases by hand: it searches the Jellyfin library and every release source (Toloka through Prowlarr/Torznab) at once, shows the copies you own beside the releases you could download, and sends the chosen release to qBittorrent in the right category. Later phases add duplicate detection and safe replacement (never silent deletion). Phase A, v0.1.0, in progress.

**Sources** (read-only, may not be accessible to the reader):
- GitHub: https://github.com/VahaC/Media-Acquisition-Manager — explore it for exact component source before designing new screens.
- Local mount `Media-Acquisition-Manager/`: `frontend/src/index.css` (tokens), `frontend/src/styles/*.css`, `frontend/src/components/**`, `frontend/src/locales/en/*.json` (all copy), `docs/Media_Acquisition_Manager_Spec.md` §5 (UI), `ROADMAP.md`, `docs/adr/*`.

## Approved design — 2 Oct 2026
The owner approved `ui_kits/current` as the target design, and the app implements it since MAM-16 (release 0.1.0). `design_handoff_approved_layout/README.md` was the implementation brief for Claude Code; where the app departs from the brief or the kit, the lines below say what the app does.
- **Header** (variant 2d): brand and version on the left; search field, Library and Jobs in the centre; account menu on the right (Profile, owner settings, theme, sign out). Sticky. Under 48rem the brand is the logo alone and Library and Jobs are icons with a title; under 480px the search field takes a row of its own.
- **Search**: the title is typed only in the header (press `/` anywhere). Only a year in brackets ending the line becomes the year — "Interstellar (2014)" — while a bare number stays in the title, "Blade Runner 2049" (owner's choice, ADR 0015). The page keeps Year, IMDb id and TMDb id to narrow the results.
- **Versions in a library card**: rows by default (1b), with a Rows/Table toggle (1c) when a work has 2+ versions. One choice per browser (`mam:versionsView`) for Search and Library. A single version is shown open. Under 48rem the versions are always rows and the toggle is hidden (owner's choice); the stored choice holds for wider screens.
- **Pager**: range on the left, numbered pages in the centre, Per page on the right. The range comes from the list's `totalCount` (every list passes it); without one the status reads "Page 2 of 5". Two rows when the window is under 43rem — an `@media` rule rather than the kit's container query: inside the 64rem column with its 1.5rem padding, that is where the pager is 640px wide — and on the narrowest phones the numbers wrap.
- **Users** page aligned to the design, except where its refusals go: a taken name (409) under User name, two passwords that differ under Confirm password, and every other server sentence, the user-name and password rules included, in the banner above the buttons (the dialogs copy none of the server's rules). **Profile** page.
- **Own password**: every user changes their own password on Profile, with the current password (`POST /api/auth/password`, ADR 0016).
- **Errors**: the patterns below, including the server-unreachable strip.
- **Library card poster** (MAM-35, Vasyl's decisions of 2026-10-06; not in the kit): every Library and Search card shows the work's own picture from Jellyfin in a 6rem box of a poster's 2:3 shape — a recommendation's size — beside the title block only (`.library-work-lead`), the badges keeping the header's right edge and the versions, table and seasons the card's whole width; under 48rem the poster stays beside the title and the badges go below both. The picture is shown whole (`object-fit: contain`) on `--muted`, and a Film (movie) or Tv (series, episode) icon fills the box while it loads and when there is none. A series' card shows the series' picture; the episodes of an opened season show none. The box is `PosterBox` (`.poster-box`), shared with the recommendations' and the TMDb dialog's posters. The kit's `WorkCard` (`components/media/`) is not updated and shows no poster.
- `ui_kits/proposal` (sidebar shell) was not adopted. It is kept for reference only.

## Products / surfaces
One product: the web app (React 19, plain CSS tokens, Radix Dialog, lucide-react). Pages: Setup, Sign in, Home (service status), **Search** (primary), **Library**, **Jobs**, **Profile**, and owner-only settings: Integrations, Destinations, Media folders, Users. **Duplicates** (MAM-21, `/library/duplicates`) exists in the app but is not covered by this design system: underline tabs, one card per group reusing the Library's version rows and table. Jobs, Job details and the Download dialog exist in the app but are not covered by this design system yet.

## Content fundamentals
- English only, every string from a locale file (ADR 0001). Sentence case everywhere: "Sync now", "Add integration", "Media folders".
- Plain, literal, complete sentences that say what happened: "Synced 1 Oct 2026, 09:12: read 870 movies into 812 works…", "Refresh failed, showing the last loaded movies."
- Second person for the user's things ("your library", "In your library"); the app calls itself "this app" / "the app". No "we", no "I".
- Never guesses: missing values are named — "Year unknown", "HDR unknown", "Not inspected yet". Claims are marked as claims ("Claimed"), verified facts aren't.
- Disabled controls explain why ("The installation owner can't be disabled.", "Only the owner can sync the library.").
- Errors are the server's own sentence; owner-only detail is hidden from others ("The owner can see why.").
- Ellipsis for progress: "Searching…", "Syncing… 312 of 870 movies". Separator " · ". No emoji, no exclamation marks.
- Placeholders are real examples: "Interstellar", "tt0816692", "157336". Ukrainian titles appear as data (Фільми, Інтерстеллар) and sort in Ukrainian order.

## Visual foundations
- **Palette**: neutral greys + one indigo accent (`--primary #4f46e5`, dark `#6366f1`). Page `#f1f1f1`, cards white, borders `#e5e5e5`. Status: green up, red down/destructive, amber attention (amber only on icons/borders — text in amber fails 4.5:1). Tints are `color-mix` 8–12% of a token. Full dark theme via `.dark` on `<html>` (localStorage `mam:theme`, applied before first paint), picked as Light, Dark or System in the account menu and on Profile.
- **Type**: system UI sans (`ui-sans-serif, system-ui`), 13px body, tracking −0.011em, `cv11 ss01 ss03`. Page titles 24/600, card titles 18/600, controls 14/500, facts 12. Monospace for paths, ids and version. No webfonts.
- **Spacing**: rem steps 0.125–2rem; page grid gap 1rem, lists 0.75rem, card padding 1.5rem (1rem on phones), row actions gap 6px. Content column max 64rem.
- **Shape**: radii 3px (badges, chips), 4px (buttons, inputs), 6px (menus), 8px (cards, dialogs). 1px borders do the separating; cards have **no shadow**. Shadows only on floating layers (menu, dialog `--shadow-lg`).
- **Nested fills** (expanded rows, table headers inside a card): use `--muted` (#f5f5f5 / dark #1a1a1a), never `--surface` — in dark mode `--surface` is #050505, darker than the card, and reads as a black hole.
- **Backgrounds**: flat colour only — no imagery, gradients, textures or illustration.
- **Motion**: 160ms ease colour/background transitions; dialog 200ms opacity/transform; spinning LoaderCircle for work in progress. No bounces.
- **States**: hover = `--accent` grey fill (outline/ghost) or primary at 90%; selected nav = 12% primary tint + foreground text; focus = 2px `--ring` box-shadow; disabled = 50% opacity with a reason tooltip on a wrapper.
- **Layout**: sticky header (brand + version left · search field, Library, Jobs centred · account menu right) + one centred 64rem column; lists page with numbered pages (‹ 1 … 4 5 6 … 33 ›, range "26–50 of 812"); lists are tables that become one card per row under 48rem; labels hide on row buttons under 560px (icon + title remain). Long paths break anywhere, never widen the page.
- **Transparency/blur**: only the 50% black dialog overlay. No blur.

## Errors — when a request fails
Pick the pattern by what is still on screen (all copy from `locales/en/*.json`). Where data failed to load or refresh, the banner or `ErrorState` gives the UI's sentence and then the API's own; a failed form or action gives the API's sentence alone, or the UI's when the API sent none:
- **Field** — a rule the input broke (client check or the server's 400 sentence): red border + `Field error` line replacing the hint. `<Field error="…">`.
- **Form / dialog** — the submit failed for a reason no field owns (the server's sentence, or "Could not add the user." when it sent none): `Banner` inside the form, above the buttons. Fields keep their values.
- **Action on a row or page** (Sync, Test, Delete, Disable): `Banner` above the list with dismiss, or a `StatusLine status="error"` on the row it was for (Sync now).
- **Refresh failed, data on screen** — `Banner` "Refresh failed, showing the last loaded …" with **Retry**; stale data stays visible.
- **First load failed, nothing to show** — `ErrorState` in place of the list/panel: what failed, the server's sentence, **Try again**. Never an empty list.
- **One source of many** — its own line in the sources list; the others' results still show.
- **Owner-only detail** — members read "… The owner can see why."
- **Server unreachable** — warning strip under the header, retries itself, "Retry now". Data stays, actions report the same sentence. The app counts a request with no answer as unreachable, and also a 502, 503 or 504 without a JSON body, which is what the Vite dev proxy and a reverse proxy answer for a stopped API. The strip waits until the session is known: while a tab restores its session, the session gate says the same sentence with its own retry.
- **Page crash / chunk failure** — `AppErrorFallback` card: "Something went wrong" + Try again, or "Could not load this page" + Reload. **404** — "Page not found" + Go to the home page.
- Error text uses `--text-danger` (= `--destructive` in light, red-400 `#f87171` in dark for 4.5:1 on the dark card).
- In `ui_kits/current` the **Simulate failures** pill (bottom-left) switches each case on.

## Iconography
lucide-react (stroke 2, round caps, currentColor) at 12px (badges), 14px (sm buttons, status lines) and 16px (nav, default buttons). In this DS the same set is loaded from CDN (`lucide@0.468.0` UMD) and rendered by the `Icon` component — same glyphs, no substitution. Key icons: Search, LibraryBig, Plug, FolderInput, FolderOpen, Users, CircleUser, LogOut, RefreshCw, ScanSearch, Download, Info, ExternalLink, PlugZap, Pencil, Trash2, CircleCheck, CircleAlert, TriangleAlert, CircleHelp, LoaderCircle. No emoji, no unicode icons, no icon font. Logo: `assets/logo.svg` (clapperboard + download arrow on an indigo tile; inline in the app so it follows `--primary`).

## Index
- **Viewing locally:** the kit pages fetch `components/*.jsx`, which browsers block on `file://`. Serve the folder instead: `npx serve docs/design-system` (or `python -m http.server` inside it), then open `/ui_kits/current/index.html`.
- `styles.css` → `tokens/colors.css`, `typography.css`, `spacing.css`, `base.css`, `primitives.css` (the app's `.ui-*` classes)
- `guidelines/` — foundation specimen cards (Colors, Type, Spacing, Brand)
- `assets/logo.svg`
- `components/core/` — Button, Input (+Field), Select, Badge, Card, Dialog, Menu, Banner (+StatusLine), ErrorState, ListPager, AppLogo, Icon, ThemeSwitch
- `components/media/` — ChipRow (+Facts), MatchBadges, ReleaseRow, WorkCard
- `ui_kits/current/` — **the approved design**: Search, Library, Users, Profile, Integrations, Destinations, Sign in, Home, plus error states (Simulate failures pill). Jobs is a placeholder.
- `ui_kits/proposal/` — earlier layout proposal (sidebar shell, two-pane Search, table Library). **Not adopted**; reference only.
- `design_handoff_approved_layout/README.md` — implementation brief for Claude Code (order of work, exact values, copy, API changes), applied in MAM-16.
- `SKILL.md`

## Components
Button, Input, Field, Select, Badge, Card, Dialog, Menu, Banner, StatusLine, ErrorState, ListPager, AppLogo, Icon, ThemeSwitch, ChipRow, Facts, MatchBadges, ReleaseRow, WorkCard.

### Intentional additions
- **Icon** — wraps the lucide CDN build, since lucide-react can't load here.
- **ErrorState** — the server sentence and Try again where a first load failed, in place of the bare red sentence the app wrote before; the app has its own since MAM-16 (`components/shared/ErrorState.tsx`).
- **ThemeSwitch** — Light / Dark / System. The app has its own since MAM-16 (`components/ThemeSwitch.tsx` over `lib/theme-store.ts`), in the account menu and on Profile.
- **Banner** — the app has its own since MAM-16 (`components/shared/Banner.tsx`, `.ui-banner`), in place of the inline `.settings-banner` lines.
- **StatusLine, ChipRow, Facts** — React wrappers for CSS patterns the app writes inline (`.library-source-status`, `.library-inspection-track`, `.library-file-facts`).
