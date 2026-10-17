import { catalogLanguageName, catalogLanguageOptions, defaultCatalogLanguage } from './catalog-languages'
import { capitalizeFirst } from './format'
import { tmdbLanguages } from '@/test/integrations'

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

  it('shows a tag whose language Intl has no name for as the tag, such as TMDb\'s own cn-CN', () => {
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
