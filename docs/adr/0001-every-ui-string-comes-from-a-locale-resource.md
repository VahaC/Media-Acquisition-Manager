# 1. Every UI string comes from a locale resource, even while English is the only language

Status: accepted (0.1.0, MAM-2)

## Context

The house conventions this project is built on say the web app has no i18n library and its
strings are English. This app departs from that on purpose. At the kickoff on 2026-09-28 the owner
chose localisation from the first release: 0.1.0 ships in English only, but other languages are
expected later.

Adding a translation layer later is a rewrite: every component, and every test that asserts on its
text, changes at once, with more screens each ticket. The scaffold (MAM-1) already wrote its text
into six components and one helper.

## Decision

**Every text the web app shows comes from an English resource through react-i18next; a component
never contains a literal sentence.**

**Resources.** `frontend/src/locales/<language>/<namespace>.json`, one namespace per feature
(`common`, `confirm`, `errors`, `home`, `notFound`); a new feature brings its own file. JSON,
because translators and translation tools work with it.

**One registration point.** `lib/i18n.ts` lists the languages in `resources`. `pickLanguage` runs
the UI in the first locale the browser prefers whose language has resources, else English, and
keeps the full tag (`en-GB`): text comes from `en`, dates and numbers follow the British
conventions. A preferred locale without resources (`uk-UA` today) is skipped rather than used for
formatting, so an English sentence never carries a Ukrainian date. There is no language picker until
a second language exists. The resources are bundled and i18next starts synchronously, so the first
render already has its text.

**Typed keys.** `src/i18next.d.ts` types every key against the English resources with
`strictKeyChecks`, so a mistyped key fails `tsc -b` instead of showing the key on screen.

**Formatting.** Dates, numbers and file sizes go through `lib/format.ts`, which formats with Intl in
the UI locale. File sizes count in 1024 steps with the binary labels (`7.94 GiB`), as qBittorrent
shows them. Trackers such as Toloka count in the same steps but label them GB, so a release shows
the same number here as on its source page; decimal units would show 8.53 GB for it.

**Lint.** `eslint.config.js` refuses literal text in component markup with `no-restricted-syntax`:
text between tags, the attributes a user reads or hears (`alt`, `title`, `placeholder`, `label`,
`aria-label` and similar), and a string or template placed in the markup, directly or through up
to three levels of ternaries, `&&`, `||`, `??` and `+`. Tests are exempt: their fixtures and their
assertions are English on purpose.

**What stays English.** API messages (`{ "error": … }` and health check descriptions) are shown as
the API sends them: translating them needs stable error codes, decided later. So are the
`index.html` title before the script runs, console output and errors meant for developers.

| Rejected option | Why not |
| --- | --- |
| No i18n until a second language is requested (the house rule) | The rewrite described above, later and larger |
| Lingui or FormatJS (react-intl) | An extraction or compile step for messages; plain JSON namespaces are enough |
| Resources as TypeScript objects | Translation tools and translators expect JSON |
| `i18next-browser-languagedetector` | A dependency for one loop over `navigator.languages`; with no picker there is no choice to persist |

`src/test/no-literal-ui-text.test.ts` asserts the lint rule through the real config,
`src/lib/i18n.test.ts` the language choice and registration, and `src/lib/format.test.ts` the
formats. Component tests run with the real English resources (`src/test/setup.ts`), so their
assertions on English text also prove the keys resolve.

## Consequences

Adding a language is a copy of `locales/en/` to `locales/<code>/`, translated, plus its line in
`resources`; browsers that prefer it then get it. A key the new language lacks falls back to
English. The types check the English resources only, so the second language should bring a test
that its keys match English.

Every new text needs a key, and a reviewer reads the English in `locales/en/` instead of in the
component. Tests keep asserting on English text.

The lint rule sees only markup, and inside it only those operators: a string passed to a function
there is not followed, because `cn('active', …)` and `status === 'Healthy'` are not text. Text
assembled elsewhere, such as an object of labels, a helper's return value or the options a caller
passes to `useConfirm()`, is caught only by review; the typed keys at least make every `t()` call
point at real text.

## Anti-consequence

This does not license translating API messages in the browser by matching their English text, and
it does not call for a language picker before a second language exists.

## A second language, chosen on Profile (added 0.5.0, MAM-46)

Phase E brings the second language this ADR was written for (Vasyl's request of 2026-10-14: the
whole app in Ukrainian for him, while a member may read English). The statements above that assumed
one language are superseded here, as the last paragraphs list; the rest of the Decision stands.
**The web app ships in English and Ukrainian (`resources = { en, uk }`, `locales/uk/` holding the
same namespaces as `locales/en/`), and each user chooses on Profile — "As in the browser" (the
default), English or Українська —, a choice saved with the account, so it follows the user to every
browser and device; before sign-in the browser decides.**

**Where the choice lives.** `Users.Language`, `en`, `uk` or `null` for "as in the browser", on the
account rather than in the browser as the theme is, so it follows the user and MAM-47's server can
read it. It arrives with the session: sign-in, setup and refresh answer `user.language`, as every
start of the app goes through a refresh, and `GET /api/auth/me` answers `language`.
`PUT /api/profile/language` saves it, `{ "language": "uk" | "en" | null }`, the property required so
a body that forgot it doesn't quietly switch the user back to the browser's; it writes no audit row,
as a preference, and refuses tokens (ADR 0040's MAM-46 section). Profile's **Language** card, after
**Appearance**, is a segmented control of three. Its two languages are endonyms, the same in either
UI language and marked with `lang`, so a person who can't read the current one still finds their
own. A choice saves at once, as the theme does, and a failed one goes back with the server's reason.

**Which locale runs** (`uiLanguageFor`, `applyAccountLanguage` in `lib/i18n.ts`). No choice: the
first browser locale whose language has resources — `pickLanguage`, now over `en` and `uk`, so a
browser that prefers `uk-UA` gets Ukrainian. The Decision's "`uk-UA` today is skipped" no longer
holds; its rule does — a locale without resources is never used for formatting. English or
Ukrainian: the browser's first locale of that language, so English on an `en-GB` browser keeps
British dates, else the bare code (`uk`). It is applied when a session is installed (sign-in, setup,
refresh), when Profile saves, and back to the browser's on sign-out, so the next person at the device
signs in on the browser's language; the sign-in and setup pages and "Restoring your session…" follow
the browser. Other tabs of the browser signed in as the same user follow a saved change over the
`mam-auth` BroadcastChannel (else the `mam:auth:language` storage key); a change made on another
device reaches an open tab with its next refresh, or as soon as Profile opens there, as the page reads
`GET /api/auth/me` and takes the language it answers — unless the answer is another user's, a save
of the language is under way, or the tab followed a language since the request went out, so an
older answer never undoes a newer choice. While a new token's secret is on screen, a language
followed from another tab or a refresh goes on the signed-in user at once and switches the UI only
when the dialog closes (`holdLanguage`), so the remount can't take the secret away.

**Remount.** `ProtectedRoute`'s `<Outlet key>` is the user id and the UI locale, so on a change the
signed-in pages mount afresh and every date, number and remembered label is worked out again in the
new locale. A refresh that brings the language already running changes nothing; one that brings a
change made elsewhere drops a half-typed form, a rare cost accepted.

**`Accept-Language`.** Every API request sends the locale the UI runs in (`lib/api.ts`), for MAM-47
to answer in. Today the server reads it only for a new TMDb integration's default catalog language
(ADR 0025's MAM-46 section). The card's **Description** keeps asking TMDb in the UI language's code,
`uk` or `en` now (decision 2 of 2026-10-17).

**Plural forms.** Ukrainian has four, `_one`, `_few`, `_many` and `_other`, and its `_one` also
stands for 21, 31 and so on. Every counted English key passes `count`, and a translation writes the
number in its `_one` wherever English leaves it out for "exactly one"; where the words differ,
"exactly one" has a key of its own, such as `reasons.missingSeason`, `replace.heldByOneCopy` and the
Jobs page's `…OneCopy` keys. A list is a whole sentence per shape, so a translation places its own
words around the names.

**The key-parity test.** The types still check `en` alone (`i18next.d.ts`), so
`src/locales/locales.test.ts` holds every `uk` namespace to its English one through `parity.ts`, the
test the Consequences asked the second language to bring: exactly the English keys, each English
plural as the four Ukrainian forms, the same placeholders (each plural form drawing on the English
forms' and all of them used), the same `<Trans>` tags and leading and trailing whitespace, no empty
value, and no English quotes or ASCII `'` and `"` inside Ukrainian text but in a command shown as it
is. **The glossary**, `docs/i18n/glossary-uk.md`, binds every Ukrainian string — its style, its
counted words and units, and its terms, four of them chosen by Vasyl on 2026-10-17 (folder «тека»,
library «медіатека», work «твір», episode «серія») —, and a change to a term changes every string
that uses it in the same commit.

**The page test.** The parity test reads only the files: it can't tell a value left in English from
a translation, nor see a key built at run time, so `src/test/ukrainian-pages.test.tsx` renders every
route of `App.tsx` in Ukrainian, on canned API answers, with no English fallback, and fails on a key
the Ukrainian resources lack, a raw key or an unresolved placeholder on screen, or English words
that came neither from the canned data nor from a name the resources keep as it is
(`src/test/ui-text-scan.ts`).

**What stays English.** Everything the server writes — `{ "error": … }`, health descriptions, a
job's or a run's reasons — until MAM-47, whose ADR amends this one; the release notes, English only
(Vasyl's decision of 2026-10-14); the `index.html` title before the script runs, console output and
the MCP tools' answers. Data stays as its source wrote it: Jellyfin's titles, release names, and
TMDb's titles in the catalog's language, which is the owner's setting, not the reader's.

**Superseded.** The Decision's "`uk-UA` today" and "There is no language picker until a second
language exists"; the rejected `i18next-browser-languagedetector`'s "with no picker there is no
choice to persist" — the choice persists on the server, and one loop over `navigator.languages`
still needs no dependency; the Consequences' "browsers that prefer it then get it", which now holds
only for "As in the browser", and their "should bring a test", which the key-parity test does; and
the Anti-consequence's second half. Its first half stands.

`src/lib/i18n.test.ts` pins the resolution ("prefers a registered second language over English when
the browser lists it first", "runs the UI in the account's language, and back in the browser's
without one", the `uiLanguageFor` cases) and the registration ("registers every namespace in
Ukrainian too"); `src/lib/auth-store.test.ts` the session's language, sign-out, the other tabs,
the language `/me` answers and the hold a token's secret takes; `src/pages/ProfilePage.test.tsx`
Profile taking the account's language from `/me`;
`src/components/ProtectedRoute.test.tsx` the remount, "mounts the page afresh when the UI language
changes"; `src/lib/api.test.ts` "sends the UI locale as Accept-Language on every request";
`src/components/profile/LanguageCard.test.tsx` the card; `src/locales/locales.test.ts` "hold every
namespace to the English keys, plural forms, placeholders, tags and typography";
`src/test/ukrainian-pages.test.tsx` "opens every route of App.tsx", "reports a key the Ukrainian
resources lack instead of showing its English text" and "shows … in Ukrainian only" for each page;
and `src/test/plural-probe.ts`'s Ukrainian probes the counted keys MAM-46 changed. On the server,
`UserLanguageHttpTests` pins the save, and `AuthSignInHttpTests.SignIn_carries_the_language_the_user_saved`,
`AuthRefreshHttpTests.Refresh_carries_the_language_the_user_saved` and
`AuthMeHttpTests.Me_carries_the_language_the_user_saved` the answers that carry it.
