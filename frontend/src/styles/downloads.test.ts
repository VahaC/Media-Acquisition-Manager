import { readFileSync } from 'node:fs'
import { join } from 'node:path'
// Imported, not global: this file is type-checked under tsconfig.node.json, as design-tokens.test.ts
// is, the one config that loads Node's types.
import { describe, expect, it } from 'vitest'

// MAM-24 — pins what the Download dialog's list of old copies needs of the sheets. jsdom renders no
// CSS, so the sheets are read from disk, as design-tokens.test.ts reads them (a `?raw` import comes
// back empty under `css: false`). MAM-27 — and what the Jobs page's replacement tables, which show the
// same chips, need of them, and what its confirmations, which quote a release or a file name, need.

interface Rule {
  /** The @media query the rule sits in; null at the top level. */
  media: string | null
  selectors: string[]
  declarations: Record<string, string>
}

// A path, not a URL: under jsdom `URL` is jsdom's, which resolves against the page, not the file.
const STYLES_DIR = import.meta.dirname
/** An @media block with the rules in it; the sheets nest nothing deeper. */
const MEDIA_BLOCK = /@media([^{]*)\{((?:[^{}]*\{[^{}]*\})*[^{}]*)\}/g
const PHONE = '(max-width: 480px)'

function rulesIn(block: string, media: string | null): Rule[] {
  return [...block.matchAll(/([^{}]*)\{([^{}]*)\}/g)].map(([, selector, body]) => ({
    media,
    selectors: selector.split(',').map((part) => part.trim().replace(/\s+/g, ' ')),
    declarations: Object.fromEntries(
      body
        .split(';')
        .filter((declaration) => declaration.includes(':'))
        .map((declaration) => {
          const separator = declaration.indexOf(':')
          return [declaration.slice(0, separator).trim(), declaration.slice(separator + 1).trim()]
        }),
    ),
  }))
}

/** What `selector` is given in the sheet `sheet` of styles/ under `media` (the top level when null), in source order. */
function declared(sheet: string, selector: string, media: string | null = null): Record<string, string> {
  const css = readFileSync(join(STYLES_DIR, sheet), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
  const rules = [
    ...rulesIn(css.replace(MEDIA_BLOCK, ''), null),
    ...[...css.matchAll(MEDIA_BLOCK)].flatMap(([, query, block]) => rulesIn(block, query.trim())),
  ]
  return rules
    .filter((rule) => rule.media === media && rule.selectors.includes(selector))
    .reduce<Record<string, string>>((all, rule) => ({ ...all, ...rule.declarations }), {})
}

describe('the Download dialog\'s old copies', () => {
  it('break a long path anywhere in every line under Existing copies, the hint under the radios and each copy\'s reason included', () => {
    // Arrange
    // The two containers of every sentence that can quote a path or a scene name: the radios'
    // fieldset (the destination's sentence under Replace), and the list of copies (the path mapper's
    // sentence under a copy).
    const containers = ['.download-mode', '.download-replacement']

    // Act
    const wraps = containers.map((selector) => declared('downloads.css', selector)['overflow-wrap'])

    // Assert
    // `anywhere`, not `break-word`: only it also shrinks the min-content size a grid track takes.
    expect(wraps).toEqual(['anywhere', 'anywhere'])
  })

  it('keep a copy\'s chips after its folder and facts on a phone, where the Library gives a version row\'s chips a row of their own', () => {
    // Arrange
    // The rule the dialog's copies share by class, meant for the Library's version rows.
    const library = declared('library.css', '.library-version-chips', PHONE)

    // Act
    const dialog = declared('downloads.css', '.download-copy-body .library-version-chips', PHONE)

    // Assert
    expect(library).toMatchObject({ 'grid-row': '2' })
    expect(dialog).toEqual({ 'grid-row': 'auto', 'grid-column': 'auto' })
  })
})

describe('the Jobs page\'s replacement tables', () => {
  it('keep a new file\'s chips after its path and facts, and its ids after the lines they belong under, on a phone', () => {
    // Arrange
    // The rule the tables share by class, meant for the Library's version rows: in the one-column grid
    // of a cell's lines it would lift the chips above the lines before them.
    const library = declared('library.css', '.library-version-chips', PHONE)

    // Act
    // The paired file and the disk cell (.jobs-name-paths), and the Jellyfin cell's ids (.jobs-status).
    const cells = ['.jobs-name-paths .library-version-chips', '.jobs-status .library-version-chips']
      .map((selector) => declared('jobs.css', selector, PHONE))

    // Assert
    expect(library).toMatchObject({ 'grid-row': '2' })
    expect(cells).toEqual([{ 'grid-row': 'auto', 'grid-column': 'auto' }, { 'grid-row': 'auto', 'grid-column': 'auto' }])
  })
})

describe('the Jobs page\'s confirmations', () => {
  it('ask Keep both and Drop in a confirmation whose title breaks a scene name or a file name anywhere, clear of the close button', () => {
    // Arrange
    // The browser check of 2026-10-11: "Keep both copies of Heat.1995.2160p.UHD.BluRay.REMUX.HDR.HEVC?" set
    // the width of the dialog's one grid column, so the title, the message and the buttons ran past its
    // right edge and the dialog scrolled sideways, at 1280 and at 375 px. The shared title sets none of this.
    const shared = declared('ui-primitives.css', '.ui-dialog-title')

    // Act
    const title = declared('ui-primitives.css', '.confirm-dialog .ui-dialog-title')

    // Assert
    // `anywhere`, not `break-word`: only it also shrinks the min-content size a grid track takes.
    expect(shared['overflow-wrap']).toBeUndefined()
    expect(title).toEqual({ 'line-height': '1.3', 'overflow-wrap': 'anywhere', 'padding-right': '1.5rem' })
  })
})
