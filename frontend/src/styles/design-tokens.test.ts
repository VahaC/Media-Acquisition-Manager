import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
// Imported, not global: this file is type-checked under tsconfig.node.json, the one config that loads
// Node's types, and that config doesn't load vitest/globals. Under tsconfig.app.json the Node types
// would reach every component, and `process` or `Buffer` would type-check and fail in the browser.
import { describe, expect, it } from 'vitest'

// MAM-16 — pins the colour tokens of index.css and the sheets' use of them. The sheets are read from
// disk: jsdom renders no CSS, and under `css: false` an `import.meta.glob` of a sheet with `?raw`
// comes back as an empty string, so a check built on it would pass on nothing. reset.css is left
// out: it is a frozen extract of utility classes. That leaves one gap: its .text-destructive colours
// text with --destructive and this test can't see it. No component uses it today; an error sentence
// takes --text-danger instead.

interface Rule {
  sheet: string
  selector: string
  declarations: { property: string; value: string }[]
}

// A path, not a URL: under jsdom `URL` is jsdom's, which resolves against the page, not the file.
const STYLES_DIR = import.meta.dirname

function rulesOf(sheet: string, css: string): Rule[] {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '')
  // Innermost blocks only: a rule inside @media matches on its own, and the @media prelude is never
  // taken for a selector because the selector part can't hold a brace. A statement before a rule
  // (index.css opens with an @import) ends at its semicolon, which no selector holds.
  return [...withoutComments.matchAll(/([^{}]*)\{([^{}]*)\}/g)].map(([, selector, body]) => ({
    sheet,
    selector: selector.slice(selector.lastIndexOf(';') + 1).trim().replace(/\s+/g, ' '),
    declarations: body
      .split(';')
      .map((declaration) => declaration.trim())
      .filter((declaration) => declaration.includes(':'))
      .map((declaration) => {
        const separator = declaration.indexOf(':')
        return {
          property: declaration.slice(0, separator).trim(),
          value: declaration.slice(separator + 1).trim().replace(/\s+/g, ' '),
        }
      }),
  }))
}

const sheets = [
  { sheet: 'index.css', css: readFileSync(join(STYLES_DIR, '..', 'index.css'), 'utf8') },
  ...readdirSync(STYLES_DIR)
    .filter((file) => file.endsWith('.css') && file !== 'reset.css')
    .map((file) => ({ sheet: `styles/${file}`, css: readFileSync(join(STYLES_DIR, file), 'utf8') })),
]
const rules = sheets.flatMap(({ sheet, css }) => rulesOf(sheet, css))

function tokensOf(selector: string): Record<string, string> {
  const block = rules.find((rule) => rule.sheet === 'index.css' && rule.selector === selector)
  return Object.fromEntries(
    (block?.declarations ?? []).filter((d) => d.property.startsWith('--')).map((d) => [d.property, d.value]),
  )
}

const where = (rule: Rule, declaration: Rule['declarations'][number]) =>
  `${rule.sheet}: ${rule.selector} { ${declaration.property}: ${declaration.value} }`

describe('design tokens', () => {
  it('defines the shared tints and the error text colour on :root and in .dark', () => {
    // Arrange
    const tints = {
      '--tint-selected': 'color-mix(in oklab, var(--primary) 12%, transparent)',
      '--tint-warning': 'color-mix(in oklab, var(--status-attention) 8%, transparent)',
      '--tint-warning-border': 'color-mix(in oklab, var(--status-attention) 55%, transparent)',
    }

    // Act
    const light = tokensOf(':root')
    const dark = tokensOf('.dark')

    // Assert
    expect(light).toMatchObject({ ...tints, '--text-danger': 'var(--destructive)' })
    // red-400: the dark --destructive reaches only 3.85:1 on the dark card, short of 4.5:1.
    expect(dark).toMatchObject({ ...tints, '--text-danger': '#f87171' })
  })

  it('writes the selected and warning tints only through their tokens', () => {
    // Arrange
    const rawTint = /var\(--primary\)\s*12%|var\(--status-attention\)\s*(8|55)%/

    // Act
    const raw = rules.flatMap((rule) =>
      rule.declarations
        // The token definitions themselves are the one place the mix is written out.
        .filter((d) => !(rule.sheet === 'index.css' && d.property.startsWith('--tint-')))
        .filter((d) => rawTint.test(d.value))
        .map((d) => where(rule, d)),
    )

    // Assert
    expect(sheets.map((s) => s.sheet)).toEqual(expect.arrayContaining(['index.css', 'styles/ui-primitives.css']))
    expect(raw).toEqual([])
  })

  it('colours no text with --destructive, which reads 3.85:1 on the dark card', () => {
    // Arrange
    // Button and menu labels in the danger colour, neither a sentence nor a fill; MAM-16 leaves
    // their dark contrast as it is.
    const allowed = ['.ui-button-danger', '.ui-button-danger:hover', '.ui-menu-item--danger']

    // Act
    const destructiveText = rules.flatMap((rule) =>
      rule.declarations
        .filter((d) => d.property === 'color' && d.value.includes('var(--destructive)'))
        .map((d) => ({ selector: rule.selector, where: where(rule, d) })),
    )

    // Assert
    expect(destructiveText.filter((found) => !allowed.includes(found.selector)).map((found) => found.where)).toEqual([])
    // Each allowed rule is still found, so the list can't go stale and the scan is seen to read colours.
    expect(allowed.filter((selector) => !destructiveText.some((found) => found.selector === selector))).toEqual([])
  })
})
