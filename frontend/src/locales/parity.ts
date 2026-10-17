// MAM-46 — holds a translated namespace to the English one it was translated from. `tsc` checks the
// keys components use against `en` only (i18next.d.ts), so a Ukrainian file with a key missing, a plural
// form left out or a placeholder misspelt would show English or a raw `{{name}}` without failing the
// build. locales.test.ts runs this over every namespace; the rules are DESIGN's for MAM-46 and
// docs/i18n/glossary-uk.md's.

/** The plural suffixes i18next picks between (CLDR categories); English uses `_one` and `_other`. */
const PLURAL_SUFFIX = /_(zero|one|two|few|many|other)$/

/** `{{name}}` or `{{name, format}}`; the content, without spaces, names the placeholder. */
const PLACEHOLDER = /\{\{([^}]*)\}\}/g

/** A `<Trans>` tag: `<strong>`, `</strong>`, `<name/>`, `<0>`. */
const TRANS_TAG = /<\/?[A-Za-z0-9]+\s*\/?>/g

/** English typographic quotes: the glossary's quotes are «…» and „…“, so these never belong. */
const ENGLISH_QUOTES = /[“”]/

/** ASCII apostrophe and quote: the glossary's apostrophe is ’, but a command shown as it is keeps them. */
const ASCII_QUOTES = /['"]/

export interface ParityRules {
  /** The plural forms the translation's language has, e.g. `['one', 'few', 'many', 'other']` for Ukrainian. */
  pluralForms: readonly string[]
}

/** The Ukrainian plural forms (CLDR): 1, 21…; 2–4, 22–24…; 0, 5–20, 25–30…; fractions. */
export const UKRAINIAN_RULES: ParityRules = { pluralForms: ['one', 'few', 'many', 'other'] }

type Leaves = Map<string, unknown>

/** Every leaf of a resource tree, by its dotted key. */
function leavesOf(tree: unknown, prefix = '', into: Leaves = new Map()): Leaves {
  if (typeof tree === 'object' && tree !== null && !Array.isArray(tree)) {
    for (const [key, value] of Object.entries(tree)) leavesOf(value, `${prefix}${prefix ? '.' : ''}${key}`, into)
  } else {
    into.set(prefix, tree)
  }
  return into
}

/** The placeholders of a value, each as written without spaces (`names,list`). */
function placeholdersOf(value: string): Set<string> {
  return new Set([...value.matchAll(PLACEHOLDER)].map((match) => match[1].replace(/\s+/g, '')))
}

/** The `<Trans>` tags of a value, sorted, so two values compare as multisets. */
function tagsOf(value: string): string[] {
  return [...value.matchAll(TRANS_TAG)].map((match) => match[0].replace(/\s+/g, '')).sort()
}

const leadingSpace = (value: string) => /^\s*/.exec(value)![0]
const trailingSpace = (value: string) => /\s*$/.exec(value)![0]

const show = (set: Iterable<string>) => `[${[...set].sort().join(', ')}]`

/**
 * Every way `translated` departs from `source`, one sentence each, prefixed by the key; empty when it
 * holds. The rules:
 * - the keys are exactly the source's, except that each source plural base (`x_one` / `x_other`)
 *   becomes exactly the translation's forms (`x_one`, `x_few`, `x_many`, `x_other` in Ukrainian);
 * - no value is empty, and none is anything but a string;
 * - a value starts and ends with the same whitespace as the source's (a fragment code joins to
 *   another keeps its leading space);
 * - a value has the source's placeholders; each plural form uses only placeholders of the source's
 *   forms, and the forms together use all of them;
 * - a value has the source's `<Trans>` tags;
 * - no “…” quotes, and no ASCII apostrophe or "…" quotes unless the same word is in the source (a
 *   command shown as it is).
 */
export function localeParityProblems(source: object, translated: object, rules: ParityRules): string[] {
  const problems: string[] = []
  const sourceLeaves = leavesOf(source)
  const translatedLeaves = leavesOf(translated)

  // The source's keys, with each plural base's forms gathered under the base.
  const plain = new Map<string, string>()
  const plurals = new Map<string, Map<string, string>>()
  for (const [key, value] of sourceLeaves) {
    const text = typeof value === 'string' ? value : String(value)
    const plural = PLURAL_SUFFIX.exec(key)
    if (plural) {
      const base = key.slice(0, plural.index)
      plurals.set(base, (plurals.get(base) ?? new Map()).set(plural[1], text))
    } else {
      plain.set(key, text)
    }
  }

  const expected = new Set<string>(plain.keys())
  for (const base of plurals.keys()) for (const form of rules.pluralForms) expected.add(`${base}_${form}`)
  for (const key of expected) if (!translatedLeaves.has(key)) problems.push(`${key}: missing`)
  for (const key of translatedLeaves.keys()) if (!expected.has(key)) problems.push(`${key}: not in the source`)

  const checkText = (key: string, value: unknown, sourceTexts: string[], reference: string): string | null => {
    if (typeof value !== 'string') {
      problems.push(`${key}: not a string`)
      return null
    }
    if (value.trim() === '') {
      problems.push(`${key}: empty`)
      return null
    }
    if (leadingSpace(value) !== leadingSpace(reference) || trailingSpace(value) !== trailingSpace(reference)) {
      problems.push(`${key}: starts or ends with other whitespace than the source`)
    }
    if (tagsOf(value).join() !== tagsOf(reference).join()) {
      problems.push(`${key}: tags ${show(tagsOf(value))}, the source has ${show(tagsOf(reference))}`)
    }
    const sourceWords = new Set(sourceTexts.flatMap((text) => text.split(/\s+/)))
    const words = value.split(/\s+/)
    const englishQuotes = words.filter((word) => ENGLISH_QUOTES.test(word))
    if (englishQuotes.length > 0) problems.push(`${key}: “…” quotes in ${englishQuotes.join(' ')}`)
    const asciiQuotes = words.filter((word) => ASCII_QUOTES.test(word) && !sourceWords.has(word))
    if (asciiQuotes.length > 0) problems.push(`${key}: an ASCII apostrophe or quote in ${asciiQuotes.join(' ')}`)
    return value
  }

  for (const [key, sourceText] of plain) {
    if (!translatedLeaves.has(key)) continue
    const value = checkText(key, translatedLeaves.get(key), [sourceText], sourceText)
    if (value === null) continue
    const want = placeholdersOf(sourceText)
    const have = placeholdersOf(value)
    if (show(want) !== show(have)) problems.push(`${key}: placeholders ${show(have)}, the source has ${show(want)}`)
  }

  for (const [base, sourceForms] of plurals) {
    const sourceTexts = [...sourceForms.values()]
    // The source's `_other` is the reference for whitespace and tags.
    const reference = sourceForms.get('other') ?? sourceTexts.at(-1)!
    const allowed = new Set(sourceTexts.flatMap((text) => [...placeholdersOf(text)]))
    const used = new Set<string>()
    let complete = true
    for (const form of rules.pluralForms) {
      const key = `${base}_${form}`
      if (!translatedLeaves.has(key)) {
        complete = false
        continue
      }
      const value = checkText(key, translatedLeaves.get(key), sourceTexts, reference)
      if (value === null) {
        complete = false
        continue
      }
      const have = placeholdersOf(value)
      const unknown = [...have].filter((name) => !allowed.has(name))
      if (unknown.length > 0) problems.push(`${key}: placeholders ${show(unknown)} are in none of the source's forms`)
      for (const name of have) used.add(name)
    }
    const unused = [...allowed].filter((name) => !used.has(name))
    if (complete && unused.length > 0) problems.push(`${base}: no form uses ${show(unused)}`)
  }

  return problems
}
