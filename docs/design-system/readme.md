# Media Acquisition Manager — design system

Self-hosted web app for people who pick media releases by hand: it searches the Jellyfin library and every release source (Toloka through Prowlarr/Torznab) at once, shows the copies you own beside the releases you could download, and sends the chosen release to qBittorrent in the right category. Later phases add duplicate detection and safe replacement (never silent deletion). Phase A, v0.1.0, in progress.

**Sources** (read-only, may not be accessible to the reader):
- GitHub: https://github.com/VahaC/Media-Acquisition-Manager — explore it for exact component source before designing new screens.
- Local mount `Media-Acquisition-Manager/`: `frontend/src/index.css` (tokens), `frontend/src/styles/*.css`, `frontend/src/components/**`, `frontend/src/locales/en/*.json` (all copy), `docs/Media_Acquisition_Manager_Spec.md` §5 (UI), `ROADMAP.md`, `docs/adr/*`.

## Products / surfaces
One product: the web app (React 19, plain CSS tokens, Radix Dialog, lucide-react). Pages: Setup, Sign in, Home (service status), **Search** (primary), **Library**, and owner-only settings: Integrations, Destinations, Media folders, Users. Planned: Jobs, Duplicates.

## Content fundamentals
- English only, every string from a locale file (ADR 0001). Sentence case everywhere: "Sync now", "Add integration", "Media folders".
- Plain, literal, complete sentences that say what happened: "Synced 1 Oct 2026, 09:12: read 870 movies into 812 works…", "Refresh failed, showing the last loaded movies."
- Second person for the user's things ("your library", "In your library"); the app calls itself "this app" / "the app". No "we", no "I".
- Never guesses: missing values are named — "Year unknown", "HDR unknown", "Not inspected yet". Claims are marked as claims ("Claimed"), verified facts aren't.
- Disabled controls explain why ("Sending a release to a download client comes in a later update.", "Only the owner can sync the library.").
- Errors are the server's own sentence; owner-only detail is hidden from others ("The owner can see why.").
- Ellipsis for progress: "Searching…", "Syncing… 312 of 870 movies". Separator " · ". No emoji, no exclamation marks.
- Placeholders are real examples: "Interstellar", "tt0816692", "157336". Ukrainian titles appear as data (Фільми, Інтерстеллар) and sort in Ukrainian order.

## Visual foundations
- **Palette**: neutral greys + one indigo accent (`--primary #4f46e5`, dark `#6366f1`). Page `#f1f1f1`, cards white, borders `#e5e5e5`. Status: green up, red down/destructive, amber attention (amber only on icons/borders — text in amber fails 4.5:1). Tints are `color-mix` 8–12% of a token. Full dark theme via `.dark` on `<html>` (localStorage `mam:theme`, applied before first paint).
- **Type**: system UI sans (`ui-sans-serif, system-ui`), 13px body, tracking −0.011em, `cv11 ss01 ss03`. Page titles 24/600, card titles 18/600, controls 14/500, facts 12. Monospace for paths, ids and version. No webfonts.
- **Spacing**: rem steps 0.125–2rem; page grid gap 1rem, lists 0.75rem, card padding 1.5rem (1rem on phones), row actions gap 6px. Content column max 64rem.
- **Shape**: radii 3px (badges, chips), 4px (buttons, inputs), 6px (menus), 8px (cards, dialogs). 1px borders do the separating; cards have **no shadow**. Shadows only on floating layers (menu, dialog `--shadow-lg`).
- **Nested fills** (expanded rows, table headers inside a card): use `--muted` (#f5f5f5 / dark #1a1a1a), never `--surface` — in dark mode `--surface` is #050505, darker than the card, and reads as a black hole.
- **Backgrounds**: flat colour only — no imagery, gradients, textures or illustration.
- **Motion**: 160ms ease colour/background transitions; dialog 200ms opacity/transform; spinning LoaderCircle for work in progress. No bounces.
- **States**: hover = `--accent` grey fill (outline/ghost) or primary at 90%; selected nav = 12% primary tint + foreground text; focus = 2px `--ring` box-shadow; disabled = 50% opacity with a reason tooltip on a wrapper.
- **Layout**: header bar + one centred column; lists page with numbered pages (‹ 1 … 4 5 6 … 33 ›, range "26–50 of 812"); lists are tables that become one card per row under 48rem; labels hide on row buttons under 560px (icon + title remain). Long paths break anywhere, never widen the page.
- **Transparency/blur**: only the 50% black dialog overlay. No blur.

## Errors — when a request fails
Pick the pattern by what is still on screen (all copy from `locales/en/*.json`; the API's own sentence follows the UI's):
- **Field** — a rule the input broke (client check or the server's 400 sentence): red border + `Field error` line replacing the hint. `<Field error="…">`.
- **Form / dialog** — the submit failed for a reason no field owns ("Could not add the user." + server sentence): `Banner` inside the form, above the buttons. Fields keep their values.
- **Action on a row or page** (Sync, Test, Delete, Disable): `Banner` above the list with dismiss, or a `StatusLine status="error"` on the row it was for (Sync now).
- **Refresh failed, data on screen** — `Banner` "Refresh failed, showing the last loaded …" with **Retry**; stale data stays visible.
- **First load failed, nothing to show** — `ErrorState` in place of the list/panel: what failed, the server's sentence, **Try again**. Never an empty list.
- **One source of many** — its own line in the sources list; the others' results still show.
- **Owner-only detail** — members read "… The owner can see why."
- **Server unreachable** — warning strip under the header, retries itself, "Retry now". Data stays, actions report the same sentence. *(Proposed — the app shows this only while restoring a session.)*
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
- `components/core/` — Button, Input (+Field), Select, Badge, Card, Dialog, Menu, Banner (+StatusLine), ListPager, AppLogo, Icon, ThemeSwitch
- `components/media/` — ChipRow (+Facts), MatchBadges, ReleaseRow, WorkCard
- `ui_kits/current/` — recreation of the app as built (Search, Library, Integrations, Destinations, Sign in, Home)
- `ui_kits/proposal/` — **layout proposal** (sidebar shell, two-pane Search with claimed-vs-verified comparison, table Library, grouped Integrations, theme switch). Not yet approved; no app code changed.
- `SKILL.md`

## Components
Button, Input, Field, Select, Badge, Card, Dialog, Menu, Banner, StatusLine, ErrorState, ListPager, AppLogo, Icon, ThemeSwitch, ChipRow, Facts, MatchBadges, ReleaseRow, WorkCard.

### Intentional additions
- **Icon** — wraps the lucide CDN build, since lucide-react can't load here.
- **ErrorState** — the app writes a bare red sentence when a first load fails; this adds the server sentence and Retry.
- **ThemeSwitch** — Light / Dark / System. The app already stores `mam:theme` and applies it before paint but has no control for it yet.
- **Banner, StatusLine, ChipRow, Facts** — React wrappers for CSS patterns the app writes inline (`.settings-banner`, `.library-source-status`, `.library-inspection-track`, `.library-file-facts`).
