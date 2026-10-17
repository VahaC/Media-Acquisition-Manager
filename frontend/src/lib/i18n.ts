import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import { en } from '../locales/en'
import { uk } from '../locales/uk'
import type { UiLanguage } from './types'

/**
 * Every language the UI ships in, by language code. Adding one is a folder under `src/locales/`
 * with the same files as `en/`, plus its line here and in `UI_LANGUAGES`; nothing in a component
 * changes. ADR 0001. MAM-46 — Ukrainian.
 */
export const resources = { en, uk }

/** The namespace `t` reads when a component names none: the app-wide words. */
export const defaultNS = 'common'

/** The language of the resources, and the one the UI falls back to. */
export const FALLBACK_LANGUAGE = 'en'

/** MAM-46 — the languages a user can choose in their Profile, in the order the choice lists them. */
export const UI_LANGUAGES: readonly UiLanguage[] = ['en', 'uk']

/**
 * MAM-46 — each language's name in itself, as the Profile's language choice shows it: the same in
 * every UI language, so a person who can't read the current one still finds their own. Names, not
 * UI text, so they stay out of the locale resources (like the codec names in media-labels.ts).
 */
export const LANGUAGE_ENDONYMS: Readonly<Record<UiLanguage, string>> = {
  en: 'English',
  uk: 'Українська',
}

/** Whether `value` is one of the languages the UI ships in; anything else follows the browser. */
export function isUiLanguage(value: unknown): value is UiLanguage {
  return typeof value === 'string' && (UI_LANGUAGES as readonly string[]).includes(value)
}

/**
 * The locale to run the UI in: the first of the browser's preferred locales whose language has
 * resources, else English.
 *
 * The full tag is kept (`en-GB`, not `en`). i18next reads the `en` resources for it, while dates and
 * numbers (lib/format.ts) follow the regional conventions the user chose in the browser. A
 * preferred locale in a language without resources is skipped rather than used for formatting, so
 * an English sentence never carries a date written the Ukrainian way.
 */
export function pickLanguage(preferred: readonly string[], available: readonly string[]): string {
  return firstLocaleOf(preferred, available) ?? FALLBACK_LANGUAGE
}

/**
 * MAM-46 — the locale the UI runs in for a user whose account says `account`.
 *
 * - No choice (null), or one this build doesn't ship: the browser decides, as before sign-in
 *   (`pickLanguage` over every shipped language), so a Ukrainian browser gets Ukrainian.
 * - English or Ukrainian: the browser's first locale of that language, so English on a British
 *   browser keeps British dates (`en-GB`); without one, the bare language code (`uk`), whose
 *   formats are that language's own.
 */
export function uiLanguageFor(account: string | null | undefined, preferred: readonly string[] = navigator.languages): string {
  if (!isUiLanguage(account)) return pickLanguage(preferred, UI_LANGUAGES)
  return firstLocaleOf(preferred, [account]) ?? account
}

/**
 * MAM-46 — runs the UI in the locale `uiLanguageFor` gives for `account`; nothing happens when it
 * already runs in it. Called when a session is installed (sign-in, setup, refresh), when the Profile
 * saves a language, and with null on sign-out, which gives the page back to the browser's language.
 * Resolves once every component has been told (the resources are bundled, so at once).
 */
export async function applyAccountLanguage(account: string | null | undefined): Promise<void> {
  const locale = uiLanguageFor(account)
  if (i18n.language !== locale) await i18n.changeLanguage(locale)
}

/** The first of `preferred` whose language is one of `languages`, as a full tag; undefined when none is. */
function firstLocaleOf(preferred: readonly string[], languages: readonly string[]): string | undefined {
  for (const tag of preferred) {
    let locale: Intl.Locale
    try {
      locale = new Intl.Locale(tag)
    } catch {
      continue /* not a valid BCP 47 tag; the next preference may be */
    }
    if (languages.includes(locale.language)) return locale.toString()
  }
  return undefined
}

// <html lang> follows the UI locale, so screen readers pronounce the text in its language and the
// browser hyphenates it correctly; the tab title is UI text like any other.
i18n.on('languageChanged', (language) => {
  document.documentElement.lang = language
  document.title = i18n.t('appName')
})

void i18n.use(initReactI18next).init({
  resources,
  lng: pickLanguage(navigator.languages, UI_LANGUAGES),
  fallbackLng: FALLBACK_LANGUAGE,
  // Resources are keyed by language only: `en-GB` reads `en` without looking for an `en-GB` set.
  load: 'languageOnly',
  ns: Object.keys(en),
  defaultNS,
  // React escapes everything it renders; escaping here as well would put `&amp;` on screen.
  interpolation: { escapeValue: false },
  // The resources are bundled, so there is nothing to wait for. A synchronous init means the first
  // render already has its text: no Suspense fallback, no key names flashing on screen.
  initAsync: false,
})

export { i18n }
