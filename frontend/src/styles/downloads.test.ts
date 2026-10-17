import { readFileSync } from 'node:fs'
import { join } from 'node:path'
// Imported, not global: this file is type-checked under tsconfig.node.json, as design-tokens.test.ts
// is, the one config that loads Node's types.
import { describe, expect, it } from 'vitest'

// MAM-24 — pins what the Download dialog's list of old copies needs of the sheets. jsdom renders no
// CSS, so the sheets are read from disk, as design-tokens.test.ts reads them (a `?raw` import comes
// back empty under `css: false`). MAM-27 — and what the Jobs page's replacement tables, which show the
// same chips, need of them, and what its confirmations, which quote a release or a file name, need.
// MAM-41 — and how wide the confirmation that lists paths is. MAM-30 — and how the Destinations and
// Integrations sheets keep a folder or an address within a phone's screen. MAM-31 — and how wide the
// destination dialog is, with a source's sections in columns and its name beside its client, and the
// other dialogs the owner asked wider, with their lists in columns and the integration dialog's short
// fields in pairs.

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

/**
 * The width inside a settings dialog at its cap `cap`, in rem: the cap less the dialog's padding and the
 * scrolling body's (2px), on both sides. The dialog's padding of a screen at least 640px wide, as every
 * screen wider than the cap is, not its phone padding.
 */
function inside(cap: string): number {
  const dialog = declared('ui-primitives.css', '.ui-dialog-content', '(min-width: 640px)')
  const body = declared('settings-dialog.css', '.settings-dialog-body')
  return parseFloat(cap) - 2 * parseFloat(dialog.padding) - 2 * parseFloat(body.padding) / 16
}

/** How many columns of `repeat(auto-…, minmax(min(Nrem, 100%), 1fr))` fit in `width` rem with `gap` (row, then column) between. */
function perRow(width: number, template: string, gap: string): number {
  const column = parseFloat(/min\(([\d.]+)rem/.exec(template)![1])
  const between = parseFloat(gap.split(' ').at(-1)!)
  return Math.floor((width + between) / (column + between))
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

  // MAM-41 — Delete for good's list of paths and sizes asks in a dialog half as wide again, on the job's
  // page, the Jobs row and the Duplicates page's Removed copies alike.
  it('make the wide confirmation half as wide again as the plain one, a cap only, so a phone keeps its own width', () => {
    // Arrange
    const plain = declared('ui-primitives.css', '.ui-dialog-content.confirm-dialog')
    const dialog = declared('ui-primitives.css', '.ui-dialog-content')

    // Act
    const wide = declared('ui-primitives.css', '.ui-dialog-content.confirm-dialog-wide')

    // Assert
    expect(plain).toEqual({ 'max-width': '26rem' })
    expect(wide).toEqual({ 'max-width': '39rem' })
    expect(parseFloat(wide['max-width'])).toBe(parseFloat(plain['max-width']) * 1.5)
    // The dialog is the screen's width up to its cap, so a phone narrower than the cap keeps its own.
    expect(dialog).toMatchObject({ width: '100%' })
  })
})

// MAM-30 — what the folders of a client that saves to folders (Transmission) need of the sheets: a
// folder or an address is one long word, which must neither widen a table or a dialog past a phone's
// screen nor hide its end.
describe('the folders of a client that saves to folders', () => {
  it('show a folder key in the paths\' font, free to shrink and break inside a phone card', () => {
    // Arrange
    const PHONE_CARD = '(max-width: 48rem)'
    const card = declared('settings-list.css', '.settings-table td', PHONE_CARD)

    // Act
    const key = { ...declared('destinations.css', '.destinations-key'), ...declared('destinations.css', '.destinations-key-folder') }

    // Assert
    // A phone card lays a cell out as a row: the key, its flex item, shrinks and breaks there.
    expect(card).toMatchObject({ display: 'flex' })
    expect(key).toEqual({ 'min-width': '0', 'overflow-wrap': 'break-word', 'font-family': 'var(--font-mono)' })
    expect(declared('destinations.css', '.destinations-paths')['font-family']).toBe(key['font-family'])
  })

  it('break each line under the Folder field, and the Address hint, anywhere', () => {
    // Arrange
    const lines = [
      declared('destinations.css', '.destinations-folder-problems'),
      declared('integrations.css', '.integrations-address-hint'),
      // Each line under the Folder field is one of these, the Jobs page's warning line.
      declared('library.css', '.library-source-warning'),
    ]

    // Act
    const wraps = lines.map((rule) => rule['overflow-wrap'])

    // Assert
    // `anywhere`, not `break-word`: only it also shrinks the min-content size a grid track takes.
    expect(wraps).toEqual(['anywhere', 'anywhere', 'anywhere'])
  })
})

// MAM-31 — the owner asked for the destination dialog at least 1,024 px wide in the live check: a source's
// tracker sections are a long list, and the Folder field's lines quote paths.
describe('the destination dialog', () => {
  it('is at most 64rem wide, over a settings dialog\'s cap, a cap only so a phone keeps its own width', () => {
    // Arrange
    const settings = declared('settings-dialog.css', '.ui-dialog-content.settings-dialog')
    const dialog = declared('ui-primitives.css', '.ui-dialog-content')

    // Act
    // Three classes, so it wins over the settings dialog's two whichever sheet loads first.
    const wide = declared('destinations.css', '.ui-dialog-content.settings-dialog.destinations-dialog')

    // Assert
    expect(settings).toMatchObject({ 'max-width': '28rem' })
    expect(wide).toEqual({ 'max-width': '64rem' })
    expect(parseFloat(wide['max-width']) * 16).toBeGreaterThanOrEqual(1024)
    // The dialog is the screen's width up to its cap, so a phone narrower than the cap keeps its own.
    expect(dialog).toMatchObject({ width: '100%' })
  })

  it('sets a source\'s sections in columns at least 14rem wide, one no wider than the dialog where two don\'t fit', () => {
    // Arrange
    const selector = '.destinations-sections'

    // Act
    const sections = declared('destinations.css', selector)

    // Assert
    expect(sections).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fill, minmax(min(14rem, 100%), 1fr))',
      gap: '0.375rem 1rem',
    })
    // Nothing of its own on a phone: `min(14rem, 100%)` alone makes it one column there.
    expect(declared('destinations.css', selector, PHONE)).toEqual({})
  })

  it('sets the name beside the download client as the integration dialog sets its pairs, one column where two don\'t fit', () => {
    // Arrange
    // MAM-31 — the owner asked for the two side by side on a wide screen in the live check.
    const dialog = declared('destinations.css', '.ui-dialog-content.settings-dialog.destinations-dialog')
    const integrations = declared('integrations.css', '.integrations-pair')

    // Act
    const pair = declared('destinations.css', '.destinations-pair')

    // Assert
    expect(pair).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fit, minmax(min(16rem, 100%), 1fr))',
      'align-items': 'start',
      gap: '1rem',
    })
    expect(pair).toEqual(integrations)
    // Room for two at the dialog's full width; `auto-fit` folds the empty columns, so the two share the row.
    expect(perRow(inside(dialog['max-width']), pair['grid-template-columns'], pair.gap)).toBeGreaterThanOrEqual(2)
    // A phone 480px wide, less the dialog's padding there, has no room for two: one, as wide as the dialog.
    const phonePadding = parseFloat(declared('ui-primitives.css', '.ui-dialog-content').padding)
    expect(perRow(480 / 16 - 2 * phonePadding, pair['grid-template-columns'], pair.gap)).toBe(1)
    expect(declared('destinations.css', '.destinations-pair', PHONE)).toEqual({})
  })
})

// MAM-31 — the owner asked in the live check for the other dialogs wider too: no long list down the middle
// of a wide screen with room empty on both sides. A short form and a confirmation keep their widths.
describe('the dialogs widened after the destination dialog', () => {
  it('share its cap of 64rem where a list runs long: the Download dialog with copies, Files in no library, Details and the TMDb link', () => {
    // Arrange
    const destinations = declared('destinations.css', '.ui-dialog-content.settings-dialog.destinations-dialog')

    // Act
    // Three classes each, so each wins over the settings dialog's two whichever sheet loads first.
    const caps = [
      declared('downloads.css', '.ui-dialog-content.settings-dialog.download-dialog-wide'),
      declared('unreported-files.css', '.ui-dialog-content.settings-dialog.unreported-files-dialog'),
      // The Details dialog's, which the TMDb link dialog takes too.
      declared('search.css', '.ui-dialog-content.settings-dialog.search-details-dialog'),
    ]

    // Assert
    expect(destinations).toEqual({ 'max-width': '64rem' })
    expect(caps).toEqual([destinations, destinations, destinations])
  })

  it('set an open season\'s episodes two a row at the Download dialog\'s full width, one where two don\'t fit', () => {
    // Arrange
    const wide = declared('downloads.css', '.ui-dialog-content.settings-dialog.download-dialog-wide')
    // A season pads its episodes on both sides (the later of its two rules: 0.5rem 0.75rem).
    const season = declared('downloads.css', '.download-season')
    const width = inside(wide['max-width']) - 2 * parseFloat(season.padding.split(' ')[1])

    // Act
    const episodes = declared('downloads.css', '.download-season-episodes')

    // Assert
    expect(episodes).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fill, minmax(min(22rem, 100%), 1fr))',
      'align-items': 'start',
      gap: '0.75rem 1.5rem',
      'padding-top': '0.25rem',
    })
    // Two, not three: a copy's file name keeps to a line or two.
    expect(perRow(width, episodes['grid-template-columns'], episodes.gap)).toBe(2)
    // Nothing of its own on a phone: `min(22rem, 100%)` alone makes it one column there.
    expect(declared('downloads.css', '.download-season-episodes', PHONE)).toEqual({})
  })

  it('set Files in no library\'s files two a row at full width, each closed by a line under it', () => {
    // Arrange
    const dialog = declared('unreported-files.css', '.ui-dialog-content.settings-dialog.unreported-files-dialog')

    // Act
    const files = declared('unreported-files.css', '.unreported-files')
    const file = declared('unreported-files.css', '.unreported-file')

    // Assert
    expect(files).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fill, minmax(min(26rem, 100%), 1fr))',
      gap: '0.75rem 2rem',
    })
    expect(perRow(inside(dialog['max-width']), files['grid-template-columns'], files.gap)).toBe(2)
    // A line under each, none over any: the second column's first file is no :first-child, and a line
    // over it would set it lower than the file beside it.
    expect(file).toMatchObject({ 'padding-bottom': '0.75rem', 'border-bottom': '1px solid var(--border)' })
    expect(file['border-top']).toBeUndefined()
    expect(declared('unreported-files.css', '.unreported-file:first-child')).toEqual({})
    // Nothing of its own under the sheet's phone query, 560px, not PHONE's 480px.
    expect(declared('unreported-files.css', '.unreported-files', '(max-width: 560px)')).toEqual({})
  })

  it('make the integration dialog 48rem wide, its short fields two a row where two 16rem columns fit, never three', () => {
    // Arrange
    const body = declared('settings-dialog.css', '.settings-dialog-body')

    // Act
    const dialog = declared('integrations.css', '.ui-dialog-content.settings-dialog.integrations-dialog')
    const pair = declared('integrations.css', '.integrations-pair')

    // Assert
    expect(dialog).toEqual({ 'max-width': '48rem' })
    // `auto-fit`, so a lone API key takes the row; `align-items: start`, so a field with a hint or a box
    // under it doesn't push down the input beside it.
    expect(pair).toEqual({
      display: 'grid',
      'grid-template-columns': 'repeat(auto-fit, minmax(min(16rem, 100%), 1fr))',
      'align-items': 'start',
      gap: '1rem',
    })
    expect(perRow(inside(dialog['max-width']), pair['grid-template-columns'], pair.gap)).toBe(2)
    // A pair's fields stand as far apart as the body's, so a phone's one column reads as before.
    expect(pair.gap).toBe(body.gap)
    // Nothing of its own under the sheet's phone query, 560px, not PHONE's 480px.
    expect(declared('integrations.css', '.integrations-pair', '(max-width: 560px)')).toEqual({})
  })
})

// MAM-46 — what the Ukrainian text needs of the sheets: its labels are longer than the English ones the
// widths were measured with. Each was measured in Chrome with the app's sheets and the Ukrainian strings.
describe('the room the Ukrainian text takes', () => {
  it('lets a token date\'s label wrap on a phone, the date beside it on one line, whichever sheet loads first', () => {
    // Act
    const label = declared('profile.css', '.settings-table td.profile-date[data-label]::before', '(max-width: 48rem)')
    const dateCell = declared('profile.css', '.profile-date')

    // Assert
    // «Востаннє використано» beside a date and time is wider than a 320px phone's token card. Three classes
    // and an attribute, over settings-list.css's `.settings-table td[data-label]::before` (two and one).
    expect(label).toEqual({ 'white-space': 'normal', 'flex-shrink': '1' })
    expect(declared('settings-list.css', '.settings-table td[data-label]::before', '(max-width: 48rem)'))
      .toMatchObject({ 'flex-shrink': '0' })
    expect(dateCell).toEqual({ 'white-space': 'nowrap' })
  })

  it('gives the search form\'s year 7rem, room for «(необов’язково)» on a line of its own', () => {
    // Act
    const fields = declared('search.css', '.search-form-fields')

    // Assert
    expect(fields['grid-template-columns'])
      .toBe('minmax(0, 7rem) minmax(0, 7.5rem) minmax(0, 11rem) minmax(0, 11rem) auto')
  })

  it('lets the sign-in and setup forms\' full-width button wrap, as tall as before on one line', () => {
    // Act
    const button = declared('auth.css', '.auth-form .ui-button.w-full')
    const size = declared('ui-primitives.css', '.ui-button-size-default')

    // Assert
    expect(button).toEqual({ height: 'auto', 'min-height': '2.25rem', 'line-height': '1.25rem', 'white-space': 'normal', 'text-align': 'center' })
    // One line: the size's padding above and below a 1.25rem line makes its 2.25rem.
    expect(2 * parseFloat(size.padding.split(' ')[0]) + parseFloat(button['line-height'])).toBe(parseFloat(size.height))
  })

  it('keeps the header\'s links icons in Ukrainian up to 77.5rem, beyond the 76rem English needs', () => {
    // Act
    const english = declared('app-layout.css', '.app-nav-link .label-text', '(max-width: 76rem)')
    const ukrainian = declared('app-layout.css', 'html:lang(uk) .app-nav-link .label-text', '(max-width: 77.5rem)')

    // Assert
    expect(english).toEqual({ display: 'none' })
    expect(ukrainian).toEqual({ display: 'none' })
    expect(declared('app-layout.css', 'html:lang(uk) .app-header', '(max-width: 77.5rem)'))
      .toEqual(declared('app-layout.css', '.app-header', '(max-width: 76rem)'))
    expect(declared('app-layout.css', 'html:lang(uk) .app-nav', '(max-width: 77.5rem)'))
      .toEqual(declared('app-layout.css', '.app-nav', '(max-width: 76rem)'))
  })
})
