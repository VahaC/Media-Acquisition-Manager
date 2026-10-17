import { applyAccountLanguage, FALLBACK_LANGUAGE, i18n, isUiLanguage, pickLanguage, resources, UI_LANGUAGES, uiLanguageFor } from './i18n'

/** Every leaf of a resource tree as `[dotted.key, value]`. */
function leaves(tree: object, prefix = ''): [string, unknown][] {
  return Object.entries(tree).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? leaves(value, `${prefix}${key}.`)
      : [[`${prefix}${key}`, value] as [string, unknown]])
}

describe('pickLanguage', () => {
  it('keeps the region of the first preferred locale whose language has resources', () => {
    // Arrange
    const preferred = ['uk-UA', 'en-GB', 'en']

    // Act
    const language = pickLanguage(preferred, ['en'])

    // Assert
    expect(language).toBe('en-GB')
  })

  it('falls back to English when no preferred language has resources', () => {
    // Arrange
    const preferred = ['uk-UA', 'de-DE']

    // Act
    const language = pickLanguage(preferred, ['en'])

    // Assert
    expect(language).toBe(FALLBACK_LANGUAGE)
  })

  it('falls back to English when the browser states no preference', () => {
    // Arrange
    const preferred: string[] = []

    // Act
    const language = pickLanguage(preferred, ['en'])

    // Assert
    expect(language).toBe('en')
  })

  it('prefers a registered second language over English when the browser lists it first', () => {
    // Arrange
    const preferred = ['uk-UA', 'en-US']

    // Act
    const language = pickLanguage(preferred, ['en', 'uk'])

    // Assert
    expect(language).toBe('uk-UA')
  })

  it('skips a preference that is not a valid locale tag', () => {
    // Arrange
    const preferred = ['not a locale', 'en-GB']

    // Act
    const language = pickLanguage(preferred, ['en'])

    // Assert
    expect(language).toBe('en-GB')
  })
})

describe('uiLanguageFor', () => {
  it.each<[string, string | null, string[], string]>([
    ['a Ukrainian browser gets Ukrainian when the account follows the browser', null, ['uk-UA', 'en-US'], 'uk-UA'],
    ['an English browser gets its own English when the account follows the browser', null, ['en-GB', 'uk'], 'en-GB'],
    ['a browser in no shipped language gets English when the account follows the browser', null, ['de-DE', 'fr'], 'en'],
    ['English keeps the browser\'s first English locale, and its formats', 'en', ['uk-UA', 'en-GB', 'en-US'], 'en-GB'],
    ['English on a browser with no English locale is plain English', 'en', ['uk-UA', 'de-DE'], 'en'],
    ['Ukrainian keeps the browser\'s Ukrainian locale', 'uk', ['en-US', 'uk-UA'], 'uk-UA'],
    ['Ukrainian on a browser with no Ukrainian locale is plain Ukrainian', 'uk', ['en-US', 'en'], 'uk'],
    ['a language this build doesn\'t ship follows the browser', 'de', ['uk-UA', 'en-US'], 'uk-UA'],
  ])('%s', (_label, account, preferred, expected) => {
    // Act
    const locale = uiLanguageFor(account, preferred)

    // Assert
    expect(locale).toBe(expected)
  })

  it('reads the browser\'s own preferences when it is given none', () => {
    // Arrange
    const browser = [...navigator.languages]

    // Act
    const locale = uiLanguageFor(null)

    // Assert
    expect(browser).toEqual(['en-US', 'en'])
    expect(locale).toBe('en-US')
  })

  it('knows the shipped languages, English and Ukrainian, and nothing else', () => {
    // Act
    const known = ['en', 'uk', 'de', 'en-GB', '', null, undefined].map(isUiLanguage)

    // Assert
    expect(UI_LANGUAGES).toEqual(['en', 'uk'])
    expect(known).toEqual([true, true, false, false, false, false, false])
  })
})

describe('i18n', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en-US')
  })

  it('registers every namespace in Ukrainian too', () => {
    // Arrange
    const namespaces = Object.keys(resources.en)

    // Act
    const registered = namespaces.filter((namespace) => i18n.hasResourceBundle('uk', namespace))

    // Assert
    expect(Object.keys(resources)).toEqual(['en', 'uk'])
    expect(registered).toEqual(namespaces)
  })

  it('reads the Ukrainian resources for a regional Ukrainian locale', async () => {
    // Act
    await i18n.changeLanguage('uk-UA')

    // Assert
    expect(i18n.language).toBe('uk-UA')
    expect(i18n.resolvedLanguage).toBe('uk')
    expect(document.documentElement.lang).toBe('uk-UA')
  })

  it('runs the UI in the account\'s language, and back in the browser\'s without one', async () => {
    // Act
    await applyAccountLanguage('uk')
    const ukrainian = i18n.language
    await applyAccountLanguage(null)
    const browser = i18n.language

    // Assert
    expect(ukrainian).toBe('uk')
    expect(browser).toBe('en-US')
  })

  it('leaves the UI alone when it already runs in the account\'s language', async () => {
    // Arrange
    const changed = vi.fn()
    i18n.on('languageChanged', changed)

    // Act
    await applyAccountLanguage('en')
    i18n.off('languageChanged', changed)

    // Assert
    expect(i18n.language).toBe('en-US')
    expect(changed).not.toHaveBeenCalled()
  })

  it('reads the English resources for a regional English locale', async () => {
    // Arrange
    const locale = 'en-GB'

    // Act
    await i18n.changeLanguage(locale)

    // Assert
    expect(i18n.language).toBe('en-GB')
    expect(i18n.resolvedLanguage).toBe('en')
    expect(i18n.t('appName')).toBe('Media Acquisition Manager')
  })

  it('keeps the page language and the tab title in step with the UI locale', async () => {
    // Arrange
    const locale = 'en-GB'

    // Act
    await i18n.changeLanguage(locale)

    // Assert
    expect(document.documentElement.lang).toBe('en-GB')
    expect(document.title).toBe('Media Acquisition Manager')
  })

  it('registers every English namespace', () => {
    // Arrange
    const namespaces = Object.keys(resources.en)

    // Act
    const registered = namespaces.filter((namespace) => i18n.hasResourceBundle('en', namespace))

    // Assert
    expect(registered).toEqual(namespaces)
  })

  it('has no blank English text', () => {
    // Arrange
    const entries = leaves(resources.en)

    // Act
    const blank = entries.filter(([, value]) => typeof value !== 'string' || value.trim() === '')

    // Assert
    expect(entries.length).toBeGreaterThan(0)
    expect(blank).toEqual([])
  })

  it('writes an ellipsis as one character, never three dots', () => {
    // Arrange
    const entries = leaves(resources.en)

    // Act
    const threeDots = entries.filter(([, value]) => String(value).includes('...'))

    // Assert
    expect(threeDots).toEqual([])
  })
})
