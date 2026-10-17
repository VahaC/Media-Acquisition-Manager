import { resources } from '@/lib/i18n'
import { localeParityProblems, UKRAINIAN_RULES } from './parity'

// MAM-46 — the checker first, on small namespaces made up here, then on every real one.

describe('localeParityProblems', () => {
  it('passes a translation with the same keys, placeholders, tags and whitespace', () => {
    // Arrange
    const source = { title: 'Jobs', page: { status: 'Page {{page}} of {{pages}}', separator: ' · ' }, link: 'See <page>Removed copies</page>.' }
    const translated = { title: 'Zadannia', page: { status: 'Storinka {{page}} z {{pages}}', separator: ' · ' }, link: 'Dyvit <page>Vyluchene</page>.' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual([])
  })

  it('names a key the translation is missing, and one the source does not have', () => {
    // Arrange
    const source = { title: 'Jobs', empty: 'No jobs yet.' }
    const translated = { title: 'Zadannia', emptyy: 'Nemaie.' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual(['empty: missing', 'emptyy: not in the source'])
  })

  it('wants exactly the four Ukrainian plural forms for every English plural', () => {
    // Arrange
    const source = { copies_one: '{{value}} copy', copies_other: '{{value}} copies' }
    const translated = { copies_one: '{{value}} kopiia', copies_few: '{{value}} kopii', copies_other: '{{value}} kopii', copies_two: '{{value}} kopii' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual(['copies_many: missing', 'copies_two: not in the source'])
  })

  it('takes a plural the source writes with _other alone', () => {
    // Arrange
    const source = { heldBy_other: 'Held ({{value}} of these copies)' }
    const translated = { heldBy_one: 'A {{value}}', heldBy_few: 'B {{value}}', heldBy_many: 'C {{value}}', heldBy_other: 'D {{value}}' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual([])
  })

  it('names an empty value and one that is not a string', () => {
    // Arrange
    const source = { title: 'Jobs', lead: 'Your downloads.' }
    const translated = { title: '  ', lead: 7 }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual(['title: empty', 'lead: not a string'])
  })

  it('wants the same leading and trailing whitespace as the source', () => {
    // Arrange
    const source = { told: ' Told {{client}}.', separator: ', ' }
    const translated = { told: 'Povidomleno {{client}}.', separator: ',' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual([
      'told: starts or ends with other whitespace than the source',
      'separator: starts or ends with other whitespace than the source',
    ])
  })

  it('wants a plain value to use exactly the source placeholders, formats included', () => {
    // Arrange
    const source = { title: 'Edit {{name}}', folders: 'In {{names, list}}' }
    const translated = { title: 'Redahuvaty {{nmae}}', folders: 'U {{names}}' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual([
      'title: placeholders [nmae], the source has [name]',
      'folders: placeholders [names], the source has [names,list]',
    ])
  })

  it('lets each plural form use any of the source forms placeholders, as long as together they use all', () => {
    // Arrange
    const source = { restore_one: 'Restore the old copy', restore_other: 'Restore {{value}} old copies' }
    const showsTheNumber = { restore_one: 'Vidnovyty {{value}} staru kopiiu', restore_few: '{{value}} kopii', restore_many: '{{value}} kopii', restore_other: '{{value}} kopii' }
    const dropsTheNumber = { restore_one: 'A', restore_few: 'B', restore_many: 'C', restore_other: 'D' }
    const misspelt = { restore_one: 'A', restore_few: '{{valeu}}', restore_many: '{{value}}', restore_other: '{{value}}' }

    // Act
    const shown = localeParityProblems(source, showsTheNumber, UKRAINIAN_RULES)
    const dropped = localeParityProblems(source, dropsTheNumber, UKRAINIAN_RULES)
    const wrong = localeParityProblems(source, misspelt, UKRAINIAN_RULES)

    // Assert
    expect(shown).toEqual([])
    expect(dropped).toEqual(['restore: no form uses [value]'])
    expect(wrong).toEqual(['restore_few: placeholders [valeu] are in none of the source\'s forms'])
  })

  it('wants the source <Trans> tags', () => {
    // Arrange
    const source = { searched: 'Nothing for <strong>“<text/>”</strong>.', link: 'See <page>Removed</page>.' }
    const translated = { searched: 'Nichoho dlia <strong>«<text/>»</strong>.', link: 'Dyvit <pg>Vyluchene</pg>.' }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual(['link: tags [</pg>, <pg>], the source has [</page>, <page>]'])
  })

  it('refuses English quotes always, and an ASCII apostrophe or quote unless the word is the source own', () => {
    // Arrange
    const source = {
      name: 'Remove “{{name}}”?',
      note: 'It can\'t be undone.',
      command: 'curl -H "Authorization: Bearer {{token}}" {{origin}}/api',
    }
    const translated = {
      name: 'Vydalyty “{{name}}”?',
      note: 'Tse ne mozhna skasuvaty, im\'ia "tse".',
      command: 'curl -H "Authorization: Bearer {{token}}" {{origin}}/api',
    }

    // Act
    const problems = localeParityProblems(source, translated, UKRAINIAN_RULES)

    // Assert
    expect(problems).toEqual([
      'name: “…” quotes in “{{name}}”?',
      'note: an ASCII apostrophe or quote in im\'ia "tse".',
    ])
  })
})

describe('the Ukrainian resources', () => {
  it('have every English namespace, and no other', () => {
    // Arrange
    const english = Object.keys(resources.en).sort()

    // Act
    const ukrainian = Object.keys(resources.uk).sort()

    // Assert
    expect(ukrainian).toEqual(english)
  })

  // Fails until the Ukrainian translation replaces the placeholder copies of the English files: a
  // copy has English's two plural forms where Ukrainian needs four.
  it('hold every namespace to the English keys, plural forms, placeholders, tags and typography', () => {
    // Arrange
    const namespaces = Object.keys(resources.en) as (keyof typeof resources.en)[]

    // Act
    const problems = namespaces.flatMap((namespace) =>
      localeParityProblems(resources.en[namespace], resources.uk[namespace], UKRAINIAN_RULES)
        .map((problem) => `${namespace}:${problem}`))

    // Assert
    expect(problems).toEqual([])
  })
})
