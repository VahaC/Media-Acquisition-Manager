import { capitalizeFirst } from './format'

// MAM-46 — a metadata catalog's language (TMDb's primary translations: uk-UA, en-US, pt-BR…), as the
// integration dialog offers it and the Integrations list names it.

/** The catalog language a new catalog falls back to: TMDb's own default. */
export const FALLBACK_CATALOG_LANGUAGE = 'en-US'

const namesByLocale = new Map<string, { names: Intl.DisplayNames; known: Intl.DisplayNames }>()

function displayNamesFor(uiLanguage: string) {
  let entry = namesByLocale.get(uiLanguage)
  if (!entry) {
    entry = {
      // "English (United States)" rather than "American English": a language's regions then read as
      // one family, and sort side by side.
      names: new Intl.DisplayNames(uiLanguage, { type: 'language', languageDisplay: 'standard' }),
      // Only to tell a language Intl has a name for from one it would echo back as a code.
      known: new Intl.DisplayNames(uiLanguage, { type: 'language', fallback: 'none' }),
    }
    namesByLocale.set(uiLanguage, entry)
  }
  return entry
}

/**
 * A catalog language's name in the UI language, as running text has it (`Ukrainian (Ukraine)`; in
 * Ukrainian, all in lower case). A tag whose language Intl has no name for — TMDb's own `cn-CN` for
 * Cantonese — is shown as the tag rather than as half a name.
 */
export function catalogLanguageName(tag: string, uiLanguage: string): string {
  try {
    const { names, known } = displayNamesFor(uiLanguage)
    if (known.of(new Intl.Locale(tag).language) === undefined) return tag
    return names.of(tag) ?? tag
  } catch {
    return tag /* not a valid language tag */
  }
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
