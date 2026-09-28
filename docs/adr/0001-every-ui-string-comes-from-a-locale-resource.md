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
