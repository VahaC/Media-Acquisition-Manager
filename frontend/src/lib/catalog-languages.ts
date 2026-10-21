import { capitalizeFirst } from './format'

// MAM-46 — a metadata catalog's language (TMDb's primary translations: uk-UA, en-US, pt-BR…), as the
// integration dialog offers it and the Integrations list names it.

/** The catalog language a new catalog falls back to: TMDb's own default. */
export const FALLBACK_CATALOG_LANGUAGE = 'en-US'

/**
 * MAM-48 — the names the app keeps for catalog languages a browser's Intl may have none for, by the UI
 * language, written as Intl writes language names there (Ukrainian's in lower case). Chrome's own ICU
 * names no Chamorro, so TMDb's `ch-GU` showed as "Ch-GU", the one code among its 144 languages
 * (2026-10-21); Node's full ICU names it, with these same words.
 */
const OWN_LANGUAGE_NAMES: Readonly<Partial<Record<string, Readonly<Partial<Record<string, string>>>>>> = {
  ch: { en: 'Chamorro', uk: 'чаморро' },
}

const namesByLocale = new Map<string, { names: Intl.DisplayNames; known: Intl.DisplayNames; regions: Intl.DisplayNames }>()

function displayNamesFor(uiLanguage: string) {
  let entry = namesByLocale.get(uiLanguage)
  if (!entry) {
    entry = {
      // "English (United States)" rather than "American English": a language's regions then read as
      // one family, and sort side by side.
      names: new Intl.DisplayNames(uiLanguage, { type: 'language', languageDisplay: 'standard' }),
      // Only to tell a language Intl has a name for from one it would echo back as a code.
      known: new Intl.DisplayNames(uiLanguage, { type: 'language', fallback: 'none' }),
      // The region beside one of the app's own language names.
      regions: new Intl.DisplayNames(uiLanguage, { type: 'region' }),
    }
    namesByLocale.set(uiLanguage, entry)
  }
  return entry
}

/**
 * A catalog language's name in the UI language, as running text has it (`Ukrainian (Ukraine)`; in
 * Ukrainian, all in lower case). Where Intl has no name for the language, or gives back its code
 * (`ch (Guam)`), the app's own name stands in, with Intl's name of the region (`Chamorro (Guam)`). A
 * tag whose language neither names — TMDb's own `cn-CN` for Cantonese — is shown as the tag rather
 * than as half a name.
 */
export function catalogLanguageName(tag: string, uiLanguage: string): string {
  try {
    const { names, known, regions } = displayNamesFor(uiLanguage)
    const { language, region } = new Intl.Locale(tag)
    const name = known.of(language) === undefined ? undefined : names.of(tag)
    if (name !== undefined && !readsAsCode(name, language)) return name
    const own = OWN_LANGUAGE_NAMES[language]?.[languageOf(uiLanguage)]
    if (own === undefined) return tag
    // Both UI languages put the region in brackets after the language, as Intl does: "English (United States)".
    return region === undefined ? own : `${own} (${regions.of(region) ?? region})`
  } catch {
    return tag /* not a valid language tag */
  }
}

/**
 * Whether Intl gave back the language's code instead of a name — "ch", "ch-GU", "ch (Guam)" — told by
 * the code at the start, in the lower case Intl echoes it in, with no letter after it, so "English" for
 * `en` is a name, and so is "Fon (Benin)" for `fon-BJ`, a name spelt as its code.
 */
function readsAsCode(name: string, language: string): boolean {
  return name.startsWith(language) && !/^\p{L}/u.test(name.slice(language.length))
}

/** One option of the catalog language select. */
export interface CatalogLanguageOption {
  tag: string
  /** The name with its first letter capitalised, as an option starts with it. */
  name: string
}

/**
 * The options of the catalog language select: Ukrainian (`uk-UA`) and American English (`en-US`)
 * first, the other Englishes after them, then every other language by name in the UI language's
 * alphabetical order. `current`, when the catalog's list no longer has it, is kept as an option, so an
 * edit shows what is stored rather than a different language.
 */
export function catalogLanguageOptions(tags: readonly string[], uiLanguage: string, current?: string | null): CatalogLanguageOption[] {
  const all = current && !tags.includes(current) ? [...tags, current] : [...tags]
  const collator = new Intl.Collator(uiLanguage)
  const options = all.map((tag) => ({ tag, name: capitalizeFirst(catalogLanguageName(tag, uiLanguage), uiLanguage) }))
  return options.sort((a, b) => rank(a.tag) - rank(b.tag) || collator.compare(a.name, b.name))
}

/** uk-UA, then en-US, then any other English, then the rest. */
function rank(tag: string): number {
  if (tag === 'uk-UA') return 0
  if (tag === 'en-US') return 1
  if (languageOf(tag) === 'en') return 2
  return 3
}

/**
 * The catalog language a new catalog starts at, from the UI's locale — the mapping the server applies
 * to a catalog added without one (Core's `CatalogLanguageRules.Map`): the same tag in any letter case
 * (`en-GB`), else the language's own country (`de` → `de-DE`), else `en-US` for English (not
 * whichever English the list has first), else the language's first entry (`uk` → `uk-UA`), else
 * `en-US`, else the list's first.
 */
export function defaultCatalogLanguage(tags: readonly string[], uiLanguage: string): string {
  const wanted = uiLanguage.toLowerCase()
  const exact = tags.find((tag) => tag.toLowerCase() === wanted)
  if (exact) return exact
  const language = languageOf(uiLanguage)
  const ownCountry = tags.find((tag) => tag.toLowerCase() === `${language}-${language}`)
  if (ownCountry) return ownCountry
  if (language === languageOf(FALLBACK_CATALOG_LANGUAGE) && tags.includes(FALLBACK_CATALOG_LANGUAGE)) return FALLBACK_CATALOG_LANGUAGE
  const sameLanguage = tags.find((tag) => languageOf(tag) === language)
  if (sameLanguage) return sameLanguage
  return tags.includes(FALLBACK_CATALOG_LANGUAGE) ? FALLBACK_CATALOG_LANGUAGE : tags[0] ?? FALLBACK_CATALOG_LANGUAGE
}

/** A tag's language part, lower-cased: `en-GB` → `en`. */
function languageOf(tag: string): string {
  return tag.split(/[-_]/)[0].toLowerCase()
}
