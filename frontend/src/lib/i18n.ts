import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import { en } from '../locales/en'

/**
 * Every language the UI ships in, by language code. Adding one is a folder under `src/locales/`
 * with the same files as `en/`, plus its line here; nothing in a component changes. ADR 0001.
 */
export const resources = { en }

/** The namespace `t` reads when a component names none: the app-wide words. */
export const defaultNS = 'common'

/** The language of the resources, and the one the UI falls back to. */
export const FALLBACK_LANGUAGE = 'en'

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
  for (const tag of preferred) {
    let locale: Intl.Locale
    try {
      locale = new Intl.Locale(tag)
    } catch {
      continue /* not a valid BCP 47 tag; the next preference may be */
    }
    if (available.includes(locale.language)) return locale.toString()
  }
  return FALLBACK_LANGUAGE
}

// <html lang> follows the UI locale, so screen readers pronounce the text in its language and the
// browser hyphenates it correctly; the tab title is UI text like any other.
i18n.on('languageChanged', (language) => {
  document.documentElement.lang = language
  document.title = i18n.t('appName')
})

void i18n.use(initReactI18next).init({
  resources,
  lng: pickLanguage(navigator.languages, Object.keys(resources)),
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
