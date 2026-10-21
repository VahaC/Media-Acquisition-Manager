import { catalogLanguageName, catalogLanguageOptions, defaultCatalogLanguage } from './catalog-languages'
import { capitalizeFirst } from './format'
import { tmdbLanguages, tmdbPrimaryTranslations } from '@/test/integrations'

const RealDisplayNames = Intl.DisplayNames

/**
 * MAM-48 — Intl.DisplayNames as Chrome's own ICU has it: no name for Chamorro, so `ch-GU` reads
 * "ch (Guam)", and asked without a fallback there is nothing. Node's full ICU names Chamorro, so the
 * app's own name is proven only with this one.
 */
class DisplayNamesWithoutChamorro extends RealDisplayNames {
  of(code: string): string | undefined {
    const { locale, type, fallback } = this.resolvedOptions()
    if (type !== 'language' || new Intl.Locale(code).language !== 'ch') return super.of(code)
    if (fallback === 'none') return undefined
    const { region } = new Intl.Locale(code)
    return region === undefined ? 'ch' : `ch (${new RealDisplayNames(locale, { type: 'region' }).of(region)})`
  }
}

/** The tags among `options` shown as a code rather than a name, each with what it shows. */
function shownAsCodes(options: readonly { tag: string; name: string }[]): string[] {
  return options.filter(({ tag, name }) => readsAsCode(name, tag)).map(({ tag, name }) => `${tag}: ${name}`)
}

/**
 * Whether a name is its tag's code: the tag itself in any letter case ("Ch-GU"), or one starting with
 * the bare language code ("ch (Guam)", "Ch (Guam)").
 */
function readsAsCode(name: string, tag: string): boolean {
  const language = tag.split('-')[0]
  return name.toLowerCase() === tag.toLowerCase() || new RegExp(`^${language}(?!\\p{L})`, 'iu').test(name)
}

describe('catalogLanguageName', () => {
  it.each([
    ['uk-UA', 'Ukrainian (Ukraine)'],
    ['en-US', 'English (United States)'],
    ['pt-BR', 'Portuguese (Brazil)'],
  ])('names %s in the UI language', (tag, name) => {
    // Act
    const shown = catalogLanguageName(tag, 'en-US')

    // Assert
    expect(shown).toBe(name)
  })

  // MAM-48 — a name spelt as its language's code, capitalised as a name, is no code given back.
  it.each([
    ['fon-BJ', 'Fon (Benin)'],
    ['luo-KE', 'Luo (Kenya)'],
  ])('names %s by Intl\'s name although it is spelt as the code', (tag, name) => {
    // Act
    const shown = catalogLanguageName(tag, 'en-US')

    // Assert
    expect(shown).toBe(name)
  })

  it('shows a tag whose language neither Intl nor the app has a name for as the tag, such as TMDb\'s own cn-CN', () => {
    // Act
    const shown = catalogLanguageName('cn-CN', 'en-US')

    // Assert
    expect(shown).toBe('cn-CN')
  })

  it('shows a tag that is no language tag at all as it is', () => {
    // Act
    const shown = catalogLanguageName('not a tag', 'en-US')

    // Assert
    expect(shown).toBe('not a tag')
  })
})

describe('catalogLanguageOptions', () => {
  it('lists Ukrainian, American English, the other Englishes, then the rest by name', () => {
    // Act
    const options = catalogLanguageOptions(tmdbLanguages, 'en-US')

    // Assert
    expect(options.map((option) => option.tag)).toEqual([
      'uk-UA', 'en-US', 'en-AU', 'en-GB', 'fr-FR', 'de-DE', 'pl-PL', 'pt-BR', 'es-ES',
    ])
    expect(options[0]).toEqual({ tag: 'uk-UA', name: 'Ukrainian (Ukraine)' })
  })

  it('starts every name with a capital in a UI language that writes language names in lower case', () => {
    // Act
    const options = catalogLanguageOptions(tmdbLanguages, 'uk')

    // Assert
    for (const { tag, name } of options) {
      const plain = catalogLanguageName(tag, 'uk')
      expect(plain.charAt(0)).not.toBe(plain.charAt(0).toLocaleUpperCase('uk'))
      expect(name).toBe(capitalizeFirst(plain, 'uk'))
    }
    expect(options.slice(0, 2).map((option) => option.tag)).toEqual(['uk-UA', 'en-US'])
  })

  it('sorts the rest by the UI language\'s alphabet', () => {
    // Arrange
    const tags = ['uk-UA', 'en-US', 'de-DE', 'fr-FR', 'pl-PL']
    const collator = new Intl.Collator('uk')

    // Act
    const rest = catalogLanguageOptions(tags, 'uk').slice(2).map((option) => option.name)

    // Assert
    expect(rest).toEqual([...rest].sort(collator.compare))
    expect(rest).toHaveLength(3)
  })

  it('keeps a stored language the catalog no longer lists', () => {
    // Act
    const options = catalogLanguageOptions(['uk-UA', 'en-US'], 'en-US', 'it-IT')

    // Assert
    expect(options).toEqual([
      { tag: 'uk-UA', name: 'Ukrainian (Ukraine)' },
      { tag: 'en-US', name: 'English (United States)' },
      { tag: 'it-IT', name: 'Italian (Italy)' },
    ])
  })
})

describe('every one of TMDb\'s languages', () => {
  it.each(['en-US', 'uk'])('has a name in %s, none of them its code', (uiLanguage) => {
    // Act
    const options = catalogLanguageOptions(tmdbPrimaryTranslations, uiLanguage)
    const names = tmdbPrimaryTranslations.map((tag) => ({ tag, name: catalogLanguageName(tag, uiLanguage) }))

    // Assert
    expect(options).toHaveLength(144)
    expect(shownAsCodes(options)).toEqual([])
    expect(shownAsCodes(names)).toEqual([])
  })
})

describe('catalog languages in a browser whose Intl has no name for one', () => {
  // MAM-48 — the module makes its DisplayNames once per UI language, so each test loads a fresh copy
  // of it with Chrome's Intl in place.
  async function loadWithoutChamorro() {
    vi.stubGlobal('Intl', Object.create(Intl, { DisplayNames: { value: DisplayNamesWithoutChamorro } }))
    vi.resetModules()
    return import('./catalog-languages')
  }

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each([
    ['en-US', 'ch (Guam)', 'Chamorro (Guam)', 'Chamorro (Guam)'],
    ['uk', 'ch (Гуам)', 'чаморро (Гуам)', 'Чаморро (Гуам)'],
  ])('names ch-GU in %s by the app\'s own name for Chamorro and Intl\'s for the region', async (uiLanguage, intlName, name, option) => {
    // Arrange
    const languages = await loadWithoutChamorro()
    const known = new Intl.DisplayNames(uiLanguage, { type: 'language', fallback: 'none' })
    const names = new Intl.DisplayNames(uiLanguage, { type: 'language' })

    // Act
    const shown = languages.catalogLanguageName('ch-GU', uiLanguage)
    const options = languages.catalogLanguageOptions(['uk-UA', 'en-US', 'ch-GU'], uiLanguage)

    // Assert
    expect(known.of('ch')).toBeUndefined()
    expect(names.of('ch-GU')).toBe(intlName)
    expect(shown).toBe(name)
    expect(options.find((entry) => entry.tag === 'ch-GU')).toEqual({ tag: 'ch-GU', name: option })
  })

  it.each(['en-US', 'uk'])('still has a name in %s for every one of TMDb\'s languages', async (uiLanguage) => {
    // Arrange
    const languages = await loadWithoutChamorro()

    // Act
    const options = languages.catalogLanguageOptions(tmdbPrimaryTranslations, uiLanguage)

    // Assert
    expect(options).toHaveLength(144)
    expect(shownAsCodes(options)).toEqual([])
  })

  it('names a language without a region by the app\'s name alone', async () => {
    // Arrange
    const languages = await loadWithoutChamorro()

    // Act
    const shown = languages.catalogLanguageName('ch', 'uk')

    // Assert
    expect(shown).toBe('чаморро')
  })
})

describe('defaultCatalogLanguage', () => {
  it.each([
    ['the same tag', 'en-GB', 'en-GB'],
    ['the same tag in another letter case', 'EN-gb', 'en-GB'],
    ['Ukrainian\'s own country', 'uk', 'uk-UA'],
    ['a regional Ukrainian the catalog doesn\'t list, by its own country', 'uk-PL', 'uk-UA'],
    ['German\'s own country', 'de', 'de-DE'],
    ['English (United States) for plain English, not the first English of the list', 'en', 'en-US'],
    ['the language\'s first entry, for a language without one of its own country', 'pt', 'pt-BR'],
    ['English (United States), for a language the catalog doesn\'t have', 'ja-JP', 'en-US'],
  ])('starts a new catalog at %s', (_, uiLanguage, expected) => {
    // Act
    const language = defaultCatalogLanguage(tmdbLanguages, uiLanguage)

    // Assert
    expect(language).toBe(expected)
  })

  it('starts at the list\'s first language when the list has no English (United States) either', () => {
    // Act
    const language = defaultCatalogLanguage(['pl-PL', 'uk-UA'], 'ja')

    // Assert
    expect(language).toBe('pl-PL')
  })
})
