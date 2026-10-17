import { createInstance } from 'i18next'
import { oldCopiesWords, type OldCopiesAction, type OldCopiesActionPart } from '@/components/jobs/old-copies-words'
import { resources } from '@/lib/i18n'

// MAM-46 — Restore's and Delete for good's words on the old copies: exactly one copy reads keys of its
// own, as Ukrainian's singular form also counts 21, 31 and 101 and so can't say "the old copy" without
// its number; every other count reads the counted key's plural form.

/** The jobs namespace's `t` in `language`, on an i18n instance of the test's own: the shared one stays English. */
function jobsT(language: 'en' | 'uk') {
  const instance = createInstance()
  void instance.init({
    lng: language,
    fallbackLng: 'en',
    resources,
    ns: Object.keys(resources.en),
    defaultNS: 'common',
    interpolation: { escapeValue: false },
    initAsync: false,
  })
  return instance.getFixedT(language, 'jobs')
}

/** The jobs resource of `language` at `path` (keys joined by dots). */
function resource(language: 'en' | 'uk', path: string): string {
  return path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)[key], resources[language].jobs) as string
}

const ACTIONS: readonly OldCopiesAction[] = ['restore', 'deleteForGood']
const PARTS: readonly OldCopiesActionPart[] = ['action', 'title', 'confirmTitle']
const EVERY_WORDING = ACTIONS.flatMap((action) => PARTS.map((part) => [action, part] as const))

describe('oldCopiesWords', () => {
  it.each(EVERY_WORDING)('reads the exactly-one key for one copy and the plural forms for any other count in Ukrainian: %s %s', (action, part) => {
    // Arrange
    const t = jobsT('uk')
    const base = `oldCopies.${action}.${part}`

    // Act
    const byCount = [1, 21, 3, 5].map((count) => oldCopiesWords(t, action, part, count))

    // Assert
    expect(byCount).toEqual([
      resource('uk', `${base}OneCopy`),
      resource('uk', `${base}_one`),
      resource('uk', `${base}_few`),
      resource('uk', `${base}_many`),
    ])
    // The reason the exactly-one key exists: 21 copies must not read "the old copy".
    expect(byCount[0]).not.toBe(byCount[1])
  })

  it.each(EVERY_WORDING)('reads the same words in English, where only one is singular: %s %s', (action, part) => {
    // Arrange
    const t = jobsT('en')
    const base = `oldCopies.${action}.${part}`

    // Act
    const byCount = [1, 2, 21].map((count) => oldCopiesWords(t, action, part, count))

    // Assert
    expect(byCount).toEqual([
      resource('en', `${base}OneCopy`),
      resource('en', `${base}_other`),
      resource('en', `${base}_other`),
    ])
  })

  it('names the old copy and the old copies in English', () => {
    // Arrange
    const t = jobsT('en')

    // Act
    const words = [oldCopiesWords(t, 'restore', 'action', 1), oldCopiesWords(t, 'deleteForGood', 'confirmTitle', 2)]

    // Assert
    expect(words).toEqual(['Restore old copy', 'Delete the old copies permanently?'])
  })
})
