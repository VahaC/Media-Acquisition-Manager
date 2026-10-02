# Handoff: approved layout (2 Oct 2026)

## Overview
The owner approved a layout update for the web app in `frontend/`. Colours and type stay as they are. These things change:
- The search field moves into the header.
- Lists get numbered pages.
- Library cards show a work's versions as rows or as a table.
- Every failed request gets a consistent error state.
- The Users page is aligned to the design, and there is a new Profile page.

## About the design files
Everything in `docs/design-system/` is a **design reference built in HTML** (React in the browser, inline styles). It is not production code. Recreate the designs in the app's own stack:
- React 19, react-router, react-i18next, TanStack Query and lucide-react.
- Plain CSS files under `src/styles/`, plus the `.ui-*` primitives in `src/styles/ui-primitives.css`.

Do not copy the kit's inline styles or its `window.MAM_DS` plumbing. Translate them into the app's CSS files and components.

To open the reference, run `npx serve docs/design-system` and go to `/ui_kits/current/index.html`. Use the account menu (top right) for Profile and Users.

The **Simulate failures** pill in the bottom-left corner switches on each error state. It is a review tool only, so do not build it.

## Fidelity
The design is high-fidelity: colours, radii, spacing and type sizes are final. The tokens were extracted from `frontend/src/index.css`, so values map 1:1 onto existing tokens. The four new tokens are in step 1.

## Ground rules from the codebase
- Every visible string goes into `src/locales/en/*.json`. This is ADR 0001, and `src/test/no-literal-ui-text.test.ts` fails otherwise. New keys are listed per step. The copy below is final English.
- Keep the intent of the existing MAM-x comments. For example, `UserMenu` stays a disclosure, not an ARIA menu, and the pager keeps the `useId` label/select tie.
- Update each touched component's `*.test.tsx` alongside it.

## Suggested order (one PR each)
1. Tokens and primitives
2. ListPager
3. Header: AppLayout, UserMenu, nav-items
4. Search page reads its title from the header
5. Versions view in library cards
6. Error states
7. Users page alignment
8. Profile page and API

---

## 1. Tokens and primitives
Add these tokens to `src/index.css`. The light values go under `:root`; the dark column is the `.dark` block.

| Token | Light | Dark |
|---|---|---|
| `--tint-selected` | `color-mix(in oklab, var(--primary) 12%, transparent)` | same formula |
| `--tint-warning` | `color-mix(in oklab, var(--status-attention) 8%, transparent)` | same formula |
| `--tint-warning-border` | `color-mix(in oklab, var(--status-attention) 55%, transparent)` | same formula |
| `--text-danger` | `var(--destructive)` (#b91c1c) | `#f87171` |

`--text-danger` exists because the dark `--destructive` (#dc2626) reads only about 3.2:1 on the dark card (#131313), and error text needs 4.5:1. Use it for every red sentence: banners, field errors and status lines. Keep `--destructive` for fills and borders.

If a stylesheet already writes the 12% primary mix inline (the active nav link, for example), switch it to `--tint-selected`.

New primitives live in `docs/design-system/tokens/primitives.css`, written in the app's `.ui-*` style. Copy them into `ui-primitives.css`:
- `.ui-banner[data-tone="warning"]`, `.ui-banner-text`, `.ui-banner-actions`
- `.ui-field[data-invalid]`, `.ui-field-error`
- `.ui-error-state`, `-head`, `-icon`, `-title`, `-body`
- `.ui-pager`, `.ui-pager-grid`, `-status`, `-buttons`, `-size`

## 2. ListPager
Files: `src/components/shared/ListPager.tsx` and `src/styles/list-pager.css`. Reference: `components/core/ListPager.jsx`.

Today the pager shows "Page 2 of 5", Per page, then Previous/Next. The new layout:
- **Container:** `nav { container-type: inline-size; width: 100% }`. Inner grid: `grid-template-columns: 1fr auto 1fr; grid-template-areas: "status pages size"; align-items: center; gap: .5rem 1rem`.
- **Status (left):** muted-foreground, tabular numbers. Show the range "26–50 of 812" when `totalCount` is given, otherwise "Page 2 of 5".
- **Pages (centre):** ‹ (ghost icon button, ChevronLeft 16, title "Previous page", disabled on page 1), then the page numbers, then › (ChevronRight, "Next page"). The gap between buttons is 2px.
- **Size (right):** "Per page" label and select (`height: 2rem; padding: 0 .5rem; width: auto`).
- **Narrow container:** at `@container (max-width: 640px)`, switch to `grid-template-columns: 1fr auto; grid-template-areas: "pages pages" "status size"`.
- **Which numbers show:** always the first and last page, plus the current page ±1, with "…" for gaps. A gap that would hide a single page shows that number instead. Examples: `1 … 4 5 6 … 33`, `1 2 3 … 33`, `1 … 31 32 33`.
- **Number button:** min-width 32px, height 32px, padding `0 .5rem`, radius `var(--radius-md)`, 13px (0.8125rem), tabular-nums.
  - Idle: transparent, muted-foreground, weight 500, hover background `var(--accent)`.
  - Current: `aria-current="page"`, background `var(--tint-selected)`, 1px border `var(--primary)`, foreground, weight 600, not clickable.
  - The "…" is 24px wide in `var(--subtle-foreground)`.
- **Props:** add `totalCount?: number`. Every list response already carries `totalCount` (UserList and the library and search lists). The range runs from `(page-1)*pageSize+1` to `min(page*pageSize, totalCount)`. A list with no items doesn't render the pager.
- **Copy** (`common.json` → `pager`):
  - Add `"range": "{{from}}–{{to}} of {{total}}"` and `"page": "Page {{page}}"` (the aria-label of a number).
  - Change `previous` to "Previous page" and `next` to "Next page". They are now titles and aria-labels only.
  - Keep `label`, `status` and `pageSize`.

## 3. Header
Files: `src/components/AppLayout.tsx`, `src/components/UserMenu.tsx`, `src/lib/nav-items.ts`, `src/styles/app-layout.css` and `src/styles/user-menu.css`. Reference: `ui_kits/current/Shell.jsx` (`AppHeader`).

**Container** (`header.app-header`): `position: sticky; top: 0; z-index: 30; display: grid; grid-template-columns: minmax(max-content,1fr) minmax(0,42rem) minmax(max-content,1fr); align-items: center; gap: 1.25rem; padding: .625rem 1.5rem; background: var(--card); border-bottom: 1px solid var(--border)`. The header ends up about 57px tall.

**Left: brand.** A `Link to="/"` with `justify-self: start` and `title` = `common:versionTitle`.
- AppLogo at 30px, gap .625rem.
- Beside it, a two-line block (`display: grid; gap: 1px; line-height: 1.15`): the app name at 15px (0.9375rem)/600 in foreground, and under it `v0.1.0` in the mono stack at 11px (0.6875rem), `var(--subtle-foreground)`.
- The version leaves `.app-header-end`.

**Centre:** `display: flex; align-items: center; gap: .75rem; min-width: 0`.
- **Search form:** `role="search"`, `flex: 1 1 auto; min-width: 0; position: relative`.
  - The field is a `.ui-input` with `type="search"`, aria-label `search:form.label`, and placeholder "Search a movie by title, e.g. Interstellar 2014".
  - Field styles: `padding-left: 36px`, `background: var(--muted)`, `border-color: transparent`. `padding-right` is `40px` while the field is empty and `12px` once there is text, so the browser's clear button sits at the right edge.
  - A Search icon (16px) sits at left 11px / top 10px. It is `var(--primary)` on /search and muted-foreground elsewhere.
  - While the field is empty, a `/` hint (`<kbd>`) shows at right 8px / top 8px, with title "Press / to search". Style: mono 11px, padding `1px 6px`, `1px solid var(--border-strong)`, radius `var(--radius-sm)`, background `var(--card)`, muted-foreground, `cursor: text`. On mousedown it focuses the input (with preventDefault).
  - Pressing `/` anywhere focuses the field, unless focus is in an input, textarea or select. Use preventDefault so the slash isn't typed.
  - Enter navigates to `/search?q=<trimmed text>` and blurs the field. An empty Enter goes to `/search`, which shows the hint state.
  - On /search the field shows the text of the search on screen (`data.text`). On every other page it starts empty.
- **Nav links:** `navItemsFor('header', user)` without Search, because the field replaces it. That leaves Library and Jobs.
  - Each link: `inline-flex; gap: 6px; padding: .375rem .625rem; border-radius: var(--radius-md); font-size: 14px; font-weight: 500; flex-shrink: 0`, icon 16px.
  - Idle: muted-foreground on transparent. Active (`aria-current="page"` from NavLink): foreground on `var(--tint-selected)`. Hover: `var(--accent)` background.
- **`nav-items.ts` changes:**
  - Keep the Search entry, but have the header skip it, either with a new placement or by filtering on `/search` in AppLayout.
  - Add `{ path: '/profile', labelKey: 'nav.profile', icon: CircleUser, ownerOnly: false, placement: 'userMenu' }` first.

**Right: account menu.** `justify-self: end`. This is the existing disclosure; keep Escape, outside-pointer and focusin-outside closing.
- **Trigger:** `inline-flex; gap: 8px; padding: 2px 6px 2px 2px; border-radius: 9999px`. Inside: a 28px avatar circle (background `var(--tint-selected)`, initial in `var(--primary)` at 12px/600), the user name at weight 500, and a ChevronDown (14px, muted). Its aria-label and title stay `auth:menu.label`.
- **Popup:** `.ui-menu`, width 240px, at `top: calc(100% + .375rem); right: 0`. Contents in order:
  1. **Identity row:** a link to /profile (`display: grid; gap: 2px; padding: 6px 12px 8px`), with a `var(--tint-selected)` background while on /profile. The user name is bold. Under it, at 12px muted: "Owner" or "Member" · `v0.1.0` (mono) · a green dot (7px, `var(--status-up)`) and "Ready".
  2. **Profile** (CircleUser 14).
  3. Separator, then the owner-only entries from `navItemsFor('userMenu')`: Integrations, Destinations, Media folders, Users. These are unchanged.
  4. Separator, then a Theme row (`display: flex; justify-content: space-between; padding: 4px 8px 4px 12px`) with "Theme" and the ThemeSwitch.
  5. Separator, then Sign out (LogOut 14). Its behaviour is unchanged.
- **"Ready"** comes from the health query in `src/lib/queries/health.ts`, the one HomePage uses. When the server isn't ready, show HomePage's own status wording and colour.
- **ThemeSwitch:** a three-option segmented radiogroup (Light / Dark / System with Sun / Moon / Monitor icons), styled like the versions toggle in step 5.
  - It writes `localStorage['mam:theme']` as `light` or `dark`, removes the key for System, and toggles `.dark` on `<html>`. The app already applies this key before first paint.
  - Reference: `components/core/ThemeSwitch.jsx`.
- **Narrow screens were not designed.** Suggestion: under 48rem, hide the brand text and keep the logo, and hide the nav labels, keeping icon and title as `.label-text` does today. The search field takes the remaining width.
- **Copy:**
  - `common.json`: `nav.profile` "Profile", `headerSearch.placeholder`, `headerSearch.hint` "Press / to search", `theme.label` "Theme", `theme.light` "Light", `theme.dark` "Dark", `theme.system` "System".
  - `auth.json`: `menu.role.owner` "Owner", `menu.role.member` "Member", `menu.ready` "Ready".

## 4. Search page reads its title from the header
Files: `src/pages/SearchPage.tsx`, `src/styles/search.css` and `locales/en/search.json`. Reference: `ui_kits/current/SearchScreen.jsx`.

**The Title field leaves the page.** The page form keeps **Year**, **IMDb id** and **TMDb id**, all optional, to narrow the search on screen.

**`?q=` handling.** On `/search?q=…` without an `id`, create the search at once with `useCreateSearch`, using q as `text`.
- If q ends with a four-digit year from 1900 to 2099, optionally in brackets ("Interstellar 2014", "Interstellar (2014)"), send that as `year` and the rest as `text`.
- Then call `setSearchParams({ id }, { replace: true })` so Back skips the `?q=` entry.
- The server needs text even with an id (`ReleaseSearchRules`). A bare id typed in the header is sent as text, and the page shows the server's sentence.

**Narrow form submit.** Create a new search with the current search's `text` plus the three fields. This is today's `submit` minus the Title input; keep `setSearchParams({ id })` as a new history entry.

**`/search` with neither `q` nor `id`.** Show a hint block instead of results: `display: grid; gap: .375rem; padding: 1.25rem 1.5rem; border: 1px dashed var(--border-strong); border-radius: var(--radius-lg); background: var(--card)`. Line 1 is weight 600; line 2 is muted.

**With a search on screen.** Above the narrow form, show "Results for “Interstellar”." with the title in `<strong>` (`overflow-wrap: anywhere`), followed by a muted "Add a year or an id to narrow them." While a search is being created, a running status line "Searching…" (spinning LoaderCircle) replaces the muted sentence.

**Narrow form layout.** `display: grid; grid-template-columns: minmax(0,6rem) minmax(0,11rem) minmax(0,11rem) auto; justify-content: start; align-items: end; gap: .75rem`. The fields keep their existing labels, placeholders and "(optional)". The button is an outline "Apply" that shows a spinner while pending.

**Errors.** If creating the search fails, show a dismissible Banner under the form with the server's sentence (fallback `form.failed`), like `createError` today. Load and refresh failures are covered in step 6.

**Copy** (`search.json`):
- Add `form.apply` "Apply".
- Add `form.resultsFor` "Results for “{{text}}”." (bold via `<Trans>`).
- Add `form.narrow` "Add a year or an id to narrow them."
- Add `empty.title` "Type a movie title in the search field at the top, then press Enter."
- Add `empty.hint` "Add the year after the title to narrow it: “Interstellar 2014”. Press / on any page to jump to the field."
- `form.title` and `form.titlePlaceholder` are no longer used on the page.

## 5. Versions in a library card
This applies to "In your library" on Search and to every card on Library.

Files: `src/components/library/LibraryWorkCard.tsx`, `VersionItem.tsx`, `FileLines.tsx`, `MediaLines.tsx` and `src/styles/library.css`. Reference: `ui_kits/current/LibraryMatch.jsx`.

**Card** (`.ui-card`): `padding: 1.25rem 1.5rem; display: grid; gap: .875rem`.
- **Head, left:** the title at 18px/600 (line-height 1.25), then a muted meta line ("Original title: …", the year or "Year unknown", "From {source}"; gap `.25rem .75rem`). On Search, MatchBadges follow.
- **Head, right:** ids as outline badges, and missing ids as warning badges "No {x} id" (TriangleAlert). Under them, right-aligned, the view toggle. The toggle shows only when the work has two or more versions.

**View toggle.** A radiogroup labelled "Show versions as".
- Container: `display: inline-flex; padding: 2px; gap: 2px; background: var(--secondary); border: 1px solid var(--border); border-radius: var(--radius-md)`.
- Options: `height: 28px; padding: 0 .625rem; font-size: 12px; font-weight: 500; gap: 6px`, with Rows3 / Table2 icons at 14px, labels "Rows" / "Table" and titles "Versions as rows" / "Versions as table".
- Selected option: `background: var(--card); color: var(--foreground); box-shadow: var(--shadow-sm)`. Idle options are muted-foreground.
- The choice is stored per browser in `localStorage['mam:versionsView']` (`rows`, the default, or `table`), shared by Search and Library.

**Rows view (default).** A `ul` with a 1px `var(--border)` border, radius `var(--radius-md)` and `overflow: hidden`; rows are separated by a 1px top border.
- Each row is a button with `aria-expanded`: `display: grid; grid-template-columns: 16px 6rem minmax(0,1fr) auto; gap: .75rem; align-items: center; padding: .5rem .75rem`. Its cells, in order:
  - A ChevronRight/ChevronDown (16px, muted).
  - The version label at weight 600 ("Version N" when it has no label).
  - The chips: resolution as 2160p / 1080p / …, the first word of the video codec, the HDR range, and audio languages as three-letter codes joined with " · ". If the file has no path, show "No path in this app" with a TriangleAlert in `--status-attention` instead. If the file isn't inspected, show a muted "Not inspected yet".
  - The size, in tabular-nums.
- One row is open at a time. The expanded part (`padding: .25rem .75rem .75rem 2.5rem; gap: 4px`) shows, in order:
  - The file name, mono 500.
  - The folder, mono 12 muted.
  - "In this app: …", mono 12 muted.
  - The warning status line when there is no path.
  - The facts line, then the verified facts.
  - The Audio and Subtitles chip rows (existing FileLines / MediaLines).
- **A single version is shown open.** There is no chevron (`grid-template-columns: 6rem minmax(0,1fr) auto`), the row is not a button, and the details padding is `.25rem .75rem .75rem`. With nothing to compare, there is nothing to collapse.

**Table view.**
- Grid: `display: grid; grid-template-columns: 7rem repeat(N, minmax(0,1fr))`, with a 1px border, radius `var(--radius-md)` and `overflow: hidden`.
- The header row has a `var(--muted)` background and labels at weight 600.
- Rows, in order: Resolution, Video, HDR, Bit rate, Duration, Audio, Subtitles (one per line), Size, File (mono 12).
- Cells: `padding: .5rem .75rem; border-top: 1px solid var(--border); overflow-wrap: anywhere; white-space: pre-line`. The first column is muted.
- Missing values read "Unknown", "No audio tracks" or "No subtitles".
- Under the table, a 12px muted caption: "Every value was read from the file by ffprobe."

**Copy** (`library.json`): add `versions.view.label`, `versions.view.rows`, `versions.view.table`, `versions.table.*` (the row labels) and `versions.table.caption`. Reuse existing "Unknown" / "No audio tracks" keys where they exist.

## 6. Error states
References: the "Errors" section of `readme.md`, `components/core/Banner.jsx`, `ErrorState.jsx` and `Input.jsx` (Field `error`), plus the kit screens with Simulate failures.

Pick the pattern by what failed:
- **A field rule** (a client check, the server's 400 sentence about one field, or 409 when the name is taken): add `data-invalid` to `.ui-field`. The input gets a `var(--destructive)` border and a focus ring of destructive at 55%. A `.ui-field-error` line **replaces** the hint: CircleAlert 12px and the sentence at 12px in `var(--text-danger)`.
- **A submit that failed for no single field:** an error Banner inside the form, above the buttons. The fields keep their values.
- **A row or page action** (Sync, Test, Disable, Delete): a dismissible Banner above the list, or a red StatusLine on the row it was for (Sync now).
- **A failed refresh while data is on screen:** a Banner "Refresh failed, showing the last loaded …" with an outline sm "Retry" button (RotateCw) in `.ui-banner-actions`. The stale list stays visible.
- **A failed first load:** an **ErrorState** in place of the list.
  - Container: border `1px dashed color-mix(in oklab, var(--destructive) 35%, var(--border))`, radius `var(--radius-lg)`, background `var(--card)`, padding `1.25rem 1.5rem`.
  - Icon: a 32px disc with a destructive 12% background and CircleAlert at 16px.
  - Title: 14px/600, from the page's existing `*.loadFailed` key.
  - Body: the server's sentence, muted.
  - Button: outline sm "Try again" (RotateCw), reading "Retrying…" while pending.
- **One release source out of several:** its own red line in the sources list, while the other sources' results still show. This is today's behaviour.
- **Server unreachable:** a sticky strip directly under the header.
  - Style: background `var(--tint-warning)`, border-bottom `var(--tint-warning-border)`, centred, padding `.5rem 1.5rem`.
  - Content: WifiOff (16px, `var(--status-attention)`), then `errors.serverUnreachable`, then a muted "Trying again every few seconds.", then an outline sm "Retry now".
  - Behaviour: it shows on a network error (no response) and retries the health endpoint every 5 s. It hides on the first answer. Actions taken meanwhile report the same sentence.
- **A page crash, chunk failure or 404:** the existing AppErrorFallback and NotFoundPage, unchanged.

**Banner changes.** The banner gets `role="alert"` for errors and `role="status"` for ok messages. It gains an optional `action` slot and an optional dismiss button (ghost, 24px, X at 14px, title "Dismiss"). A new `warning` tone uses `var(--tint-warning)`, `var(--tint-warning-border)` and a `var(--status-attention)` icon.

**Copy** (add where missing):
- `common.json`: `retry` "Retry", `retrying` "Retrying…", `tryAgain` "Try again", `dismiss` "Dismiss".
- `errors.json`: `offline.retrying` "Trying again every few seconds.", `offline.retryNow` "Retry now".

## 7. Users page
Files: `src/pages/settings/UsersPage.tsx` and `src/components/users/*`. Reference: `ui_kits/current/UsersScreen.jsx`, which was built from the current page and its copy. Only these things change:
- ListPager now gets `totalCount`.
- CreateUserDialog shows errors per field (Field `error`):
  - A user-name rule or a taken name (409) goes under User name.
  - A password rule goes under Password.
  - A mismatch goes under Confirm password.
  - Anything else shows as a Banner above the buttons.
  - The submit button reads "Adding…" while pending.
- Failed actions (Disable, Enable, Delete, Reset password) show a dismissible error Banner above the table. Success messages, such as a password reset, show as a dismissible ok Banner.
- A failed first load shows an ErrorState.

## 8. Profile page (new) and API
Add a `/profile` route for every signed-in user, inside ProtectedRoute and lazy like the other pages. The account menu links to it from both the identity row and the Profile entry. Reference: `ui_kits/current/ProfileScreen.jsx`.

**Layout:** a column with `max-width: 44rem; display: grid; gap: 1.25rem`.

**Head.**
- A 56px avatar circle (`var(--tint-selected)` background, initial in `var(--primary)` at 22px/600).
- The h1 user name (24px/600, line-height 1.2), with an "Owner" badge (default variant) for the owner.
- Under it, at 14px muted: for the owner, "The installation owner: manages users, integrations and settings."; for a member, "Can search, browse the library and send downloads."

**Account card.** Rows use `grid-template-columns: 11rem minmax(0,1fr); padding: .875rem 0; border-bottom: 1px solid var(--border)`, with no border on the last row. The rows are User name (mono), Role, Created and Last sign-in.

**Password card.** Description: "There is no email reset, so keep it somewhere safe."
- **Collapsed:** `••••••••••` in mono muted, and an outline sm "Change password" button (KeyRound).
- **Expanded:** a form with `max-width: 22rem; gap: 1rem`, containing:
  - Current password.
  - New password, with the hint "8–128 characters.".
  - Confirm password.
  - Field errors inline, and a form-level error Banner for any other failure.
  - A 12px muted note: "Your other devices are signed out. This one stays signed in."
  - A primary "Change password" button (reads "Changing…" while pending) and an outline Cancel.
- **Errors:**
  - "Enter your current password."
  - "A password is at least 8 characters."
  - "A password is at most 128 characters."
  - "The two passwords are different. Type the same password in both fields."
  - "The current password is incorrect." (the server's 400; shown under Current password)
- **On success:** the card collapses, and a dismissible ok Banner at the top reads "Password changed. Your other devices are signed out; this one stays signed in."

**Appearance card.** Description: "Saved in this browser." Contains "Theme" and the ThemeSwitch with labels.

**Session card.** Description: "A session lasts 30 days from its last use. This one ends {date} unless you use the app again." Contains an outline "Sign out" button (LogOut).

**API changes** (backend in `src/`):
- **New:** `POST /api/auth/password` with `{ currentPassword, newPassword }`, for the signed-in user.
  - 204 on success. It ends the user's other sessions and keeps the caller's, the same as today's owner reset (`POST /api/settings/users/{id}/password`).
  - 400 `{ error }` when a password rule fails or the current password is wrong.
  - 429 when tried too often, like sign-in.
  - Today only the owner can change a password, through Users. This endpoint lets every user change their own.
- **Extend:** add `createdUtc`, `lastSignInUtc` and the session's `sessionExpiresUtc` to `GET /api/auth/me`. The client can't read the session expiry itself because the refresh cookie is HttpOnly. Until these fields exist, leave out the Created and Last sign-in rows and the date sentence in Session.

**Copy:** a new `profile.json` namespace, registered in `locales/en/index.ts`.

## Not covered by this design
These keep their current layouts inside the new header:
- Home and Setup.
- Sign in: only the error Banner changes.
- Jobs, Job details and the Download dialog.
- Media folders and Destinations.
- Integrations: only the pager and error states change.

## Design tokens
Every value is in `docs/design-system/tokens/*.css` and matches `frontend/src/index.css`. The ones this brief uses:

**Colours (light / dark)**
- Primary: #4f46e5 / #6366f1. Primary hover: #4338ca / #818cf8.
- Muted: #f5f5f5 / #1a1a1a. Muted foreground: #525252 / #a3a3a3. Subtle foreground: #737373 (both).
- Border: #e5e5e5 / #262626. Border strong: #d4d4d4 / #404040.
- Destructive: #b91c1c / #dc2626. Status up: #16a34a / #22c55e. Status attention: #d97706.
- Card: #ffffff / #131313.

**Radii:** sm 0.1875rem (3px), md 0.25rem (4px), lg 0.5rem (8px).

**Type:** system UI sans with 13px body text. Page titles are 24/600, card titles 18/600, controls 14/500 and facts 12px. Mono is for ids, paths and the version.

**Shadows:**
- `--shadow-sm`: only on the selected option of a segmented control.
- `--shadow-lg`: menus and dialogs.
- Cards have no shadow.

**Motion:** 160ms ease on colour and background. Pending work shows a spinning LoaderCircle.

## Assets
- `assets/logo.svg` is the app's AppLogo, which is already inline in the app.
- Icons are lucide-react, already a dependency. New in this brief: Rows3, Table2, CircleUser, WifiOff, RotateCw, Sun, Moon, Monitor and X.

## Files in docs/design-system
- `ui_kits/current/index.html`: the clickable reference (Search, Library, a Jobs placeholder, Users, Profile, Integrations, Destinations, Sign in, Home).
- `ui_kits/current/Shell.jsx`: the header and account menu.
- `ui_kits/current/SearchScreen.jsx`, `LibraryMatch.jsx`, `LibraryScreen.jsx`, `UsersScreen.jsx`, `ProfileScreen.jsx`, `SettingsScreens.jsx`, `SignIn.jsx`: the screens.
- `ui_kits/current/Sim.jsx`: the failure simulator (review only), the offline strip, and the crash and 404 cards.
- `components/core/ListPager.jsx`, `ThemeSwitch.jsx`, `Banner.jsx`, `ErrorState.jsx`, `Input.jsx`: the components that change.
- `tokens/colors.css`, `tokens/primitives.css`: tokens and primitives.
- `readme.md`: foundations, content rules and error patterns.
- `ui_kits/proposal/`: an earlier layout proposal that was **not adopted**. Ignore it.
