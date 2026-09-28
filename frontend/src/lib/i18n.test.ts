import { FALLBACK_LANGUAGE, i18n, pickLanguage, resources } from './i18n'

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

describe('i18n', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en-US')
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
