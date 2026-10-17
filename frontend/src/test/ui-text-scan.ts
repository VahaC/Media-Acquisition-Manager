// MAM-46 — what a page shows that a translation would never put there: a resource key in place of its
// text, a placeholder or a nested lookup left unresolved, and English words that came from neither the
// server's data nor a name the Ukrainian resources keep as it is. test/ukrainian-pages.test.tsx runs
// these over every page rendered in Ukrainian.

/** The attributes a user reads or hears, as the literal-UI-text lint rule lists them (eslint.config.js). */
const UI_TEXT_ATTRIBUTES = ['alt', 'title', 'placeholder', 'label', 'aria-label', 'aria-description', 'aria-placeholder', 'aria-roledescription', 'aria-valuetext']

/** One piece of text on screen and where it is, for the failure message. */
export interface ScreenText {
  text: string
  where: string
}

/**
 * Every text node under `root` and every UI attribute value, with the element that holds it. jsdom
 * computes no layout, so text a stylesheet would hide counts too: it is in the page all the same, and
 * a screen reader may read it.
 */
export function screenTexts(root: HTMLElement): ScreenText[] {
  const texts: ScreenText[] = []
  const walker = root.ownerDocument.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const parent = node.parentElement
    if (!parent || parent.closest('script, style')) continue
    const text = node.textContent ?? ''
    if (text.trim() !== '') texts.push({ text, where: describe(parent) })
  }
  for (const element of root.querySelectorAll('*')) {
    for (const attribute of UI_TEXT_ATTRIBUTES) {
      const value = element.getAttribute(attribute)
      if (value && value.trim() !== '') texts.push({ text: value, where: `${describe(element)}[${attribute}]` })
    }
  }
  return texts
}

/** `<tag class>` of `element`, short enough to read in a failure message. */
function describe(element: Element): string {
  const className = element.getAttribute('class')
  return className ? `<${element.tagName.toLowerCase()} class="${className}">` : `<${element.tagName.toLowerCase()}>`
}

// A dotted key such as `tabs.Possible` or `oldCopies.restore.action`, the shape i18next leaves on
// screen for a key it can't resolve. A segment after a dot may start with a capital: the keys built
// from an API value (`tabs.${value}`, `status.${job.status}`) do.
const DOTTED_KEY = /\b[a-z][A-Za-z]*\.[A-Za-z][A-Za-z.]*\b/g

// A key with its namespace, `common:loading`, which a lookup can leave the same way.
const NAMESPACED_KEY = /\b[a-z][A-Za-z]*:[a-z][A-Za-z.]*\b/g

// What a dotted word may end in when it is a file name, an address or a host rather than a key.
const FILE_OR_HOST_ENDINGS = new Set([
  // files a library or a download holds, and the app's own
  'mkv', 'mp4', 'm4v', 'avi', 'mov', 'wmv', 'ts', 'm2ts', 'iso', 'vob', 'webm', 'srt', 'ass', 'ssa', 'sub', 'idx', 'sup',
  'nfo', 'jpg', 'jpeg', 'png', 'webp', 'txt', 'json', 'torrent', 'db',
  // hosts
  'com', 'org', 'net', 'io', 'ua', 'dev', 'local', 'lan', 'tv', 'me', 'localhost', 'test',
])

const URL_SCHEMES = new Set(['http', 'https', 'mailto', 'magnet'])

/** The words in `texts` shaped like a resource key, each with where it is. */
export function rawKeysIn(texts: readonly ScreenText[]): string[] {
  const found: string[] = []
  for (const { text, where } of texts) {
    for (const [match] of text.matchAll(DOTTED_KEY)) {
      const ending = match.slice(match.lastIndexOf('.') + 1).toLowerCase()
      if (!FILE_OR_HOST_ENDINGS.has(ending)) found.push(`${where}: “${match}” in “${text.trim()}”`)
    }
    for (const [match] of text.matchAll(NAMESPACED_KEY)) {
      if (!URL_SCHEMES.has(match.slice(0, match.indexOf(':')))) found.push(`${where}: “${match}” in “${text.trim()}”`)
    }
  }
  return found
}

/**
 * What i18next leaves on screen when it can't finish a value: a `{{placeholder}}` it had no value
 * for, a `$t(…)` nesting, or its sentence about a key that holds an object.
 */
export function unresolvedIn(texts: readonly ScreenText[]): string[] {
  return texts
    .filter(({ text }) => /\{\{|\}\}|\$t\(|returned an object instead of string/.test(text))
    .map(({ text, where }) => `${where}: “${text.trim()}”`)
}

// Two or more Latin words in a row: a phrase, rather than a name, a code or an abbreviation alone.
const LATIN_PHRASE = /[A-Za-z][A-Za-z0-9'’]*(?:[ \u00a0]+[A-Za-z][A-Za-z0-9'’]*)+/g

const LATIN_WORD = /[A-Za-z][A-Za-z0-9'’]*/g

/** A word with a capital after its first letter or a digit: a name or a code (IMDb, TV, tt0090605), not prose. */
const NAME_LIKE = /^.+[A-Z0-9]|^[A-Z0-9]+$/

/**
 * The Latin phrases in `texts` that come from neither `allowed` — the server's data the page was
 * served and the values the Ukrainian resources keep in Latin letters (product names such as "Claude
 * Code", "Dolby Vision") — so a component wrote them in English: a string outside the resources, or
 * a lookup that fell back to English.
 *
 * A phrase is accounted for when an allowed string holds it whole, or when it is made of names and
 * codes only, each of which an allowed string holds as a word: "IMDb tt0090605" puts a name the
 * resources keep beside an id from the server. A phrase with a word of plain prose ("Not ready",
 * "Special edition") is accounted for only whole.
 */
export function englishIn(texts: readonly ScreenText[], allowed: readonly string[]): string[] {
  const allowedWords = new Set(allowed.flatMap((source) => source.match(LATIN_WORD) ?? []))
  const accountedFor = (phrase: string) =>
    allowed.some((source) => source.includes(phrase))
    || phrase.split(' ').every((word) => NAME_LIKE.test(word) && allowedWords.has(word))
  const found: string[] = []
  for (const { text, where } of texts) {
    for (const [match] of text.matchAll(LATIN_PHRASE)) {
      const phrase = match.replace(/[ \u00a0]+/g, ' ')
      if (!accountedFor(phrase)) found.push(`${where}: “${phrase}” in “${text.trim()}”`)
    }
  }
  return found
}

/** Every string in `value`, however deep, added to `into`. */
export function collectStrings(value: unknown, into: Set<string>): void {
  if (typeof value === 'string') {
    into.add(value)
  } else if (Array.isArray(value)) {
    for (const item of value) collectStrings(item, into)
  } else if (typeof value === 'object' && value !== null) {
    for (const item of Object.values(value)) collectStrings(item, into)
  }
}
