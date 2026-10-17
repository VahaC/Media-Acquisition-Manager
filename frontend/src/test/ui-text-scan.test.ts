import { collectStrings, englishIn, rawKeysIn, screenTexts, unresolvedIn } from '@/test/ui-text-scan'

// MAM-46 — the checks test/ukrainian-pages.test.tsx runs over every page: each finds what it is for,
// and lets pass what a Ukrainian page legitimately holds in Latin letters. The Cyrillic words here are
// the fixtures' library data (test/library.ts), standing in for a page's Ukrainian text.

/** A body holding `html`, as a page would render it. */
function pageOf(html: string): HTMLElement {
  const root = document.createElement('div')
  root.innerHTML = html
  return root
}

describe('screenTexts', () => {
  it('reads every text node and every attribute a user reads or hears, and nothing else', () => {
    // Arrange
    const root = pageOf('<p>Фільми</p><button aria-label="Чужі" title="Чужі (1986)" data-key="tabs.All"><svg></svg></button><style>.a{}</style>')

    // Act
    const texts = screenTexts(root).map(({ text }) => text)

    // Assert
    expect(texts).toEqual(expect.arrayContaining(['Фільми', 'Чужі', 'Чужі (1986)']))
    expect(texts).toHaveLength(3)
  })
})

describe('rawKeysIn', () => {
  it('finds a dotted key in a text node and in an attribute', () => {
    // Arrange
    const root = pageOf('<p>tabs.Possible</p><button aria-label="oldCopies.restore.action"></button>')

    // Act
    const keys = rawKeysIn(screenTexts(root))

    // Assert
    expect(keys).toEqual([
      '<p>: “tabs.Possible” in “tabs.Possible”',
      '<button>[aria-label]: “oldCopies.restore.action” in “oldCopies.restore.action”',
    ])
  })

  it('finds a key with its namespace', () => {
    // Arrange
    const root = pageOf('<p>Фільми: common:loading</p>')

    // Act
    const keys = rawKeysIn(screenTexts(root))

    // Assert
    expect(keys).toEqual(['<p>: “common:loading” in “Фільми: common:loading”'])
  })

  it('lets file names, hosts and addresses pass', () => {
    // Arrange
    const root = pageOf('<p>/media/movies/Alien (1979)/Alien.en.srt</p><p>https://api.themoviedb.org</p><p>Aliens.1986.2160p.mkv, 12:05</p>')

    // Act
    const keys = rawKeysIn(screenTexts(root))

    // Assert
    expect(keys).toEqual([])
  })
})

describe('unresolvedIn', () => {
  it('finds a placeholder, a nesting and a key that holds an object', () => {
    // Arrange
    const root = pageOf('<p>Чужі, {{name}}</p><p>$t(common:save)</p><p>key \'tabs (uk)\' returned an object instead of string.</p><p>Фільми</p>')

    // Act
    const found = unresolvedIn(screenTexts(root))

    // Assert
    expect(found).toHaveLength(3)
  })
})

describe('englishIn', () => {
  const allowed = ['Home Jellyfin', 'tt0090605', 'Special Edition', 'IMDb', 'TMDb TV', 'ID']

  it('finds an English phrase that is neither the server\'s data nor a name the resources keep', () => {
    // Arrange
    const root = pageOf('<p>Фільми: Not ready</p><span title="Show the files">Мультфільми</span>')

    // Act
    const found = englishIn(screenTexts(root), allowed)

    // Assert
    expect(found).toEqual([
      '<p>: “Not ready” in “Фільми: Not ready”',
      '<span>[title]: “Show the files” in “Show the files”',
    ])
  })

  it('lets the server\'s data pass, and names and codes the allowed strings hold as words', () => {
    // Arrange
    const root = pageOf('<p>Фільми: Home Jellyfin</p><p>IMDb tt0090605</p><p>Special Edition</p><p>Єллоустоун: TMDb TV ID</p><p>Jellyfin</p>')

    // Act
    const found = englishIn(screenTexts(root), allowed)

    // Assert
    expect(found).toEqual([])
  })

  it('accounts for a phrase with a word of prose only when an allowed string holds it whole', () => {
    // Arrange
    const root = pageOf('<p>Home edition</p>')

    // Act
    const found = englishIn(screenTexts(root), allowed)

    // Assert
    expect(found).toEqual(['<p>: “Home edition” in “Home edition”'])
  })
})

describe('collectStrings', () => {
  it('collects every string of a value, however deep', () => {
    // Arrange
    const strings = new Set<string>()

    // Act
    collectStrings({ items: [{ title: 'Чужі', versions: [{ sourceName: 'Home Jellyfin', size: 1 }] }], total: 3, next: null }, strings)

    // Assert
    expect([...strings]).toEqual(['Чужі', 'Home Jellyfin'])
  })
})
