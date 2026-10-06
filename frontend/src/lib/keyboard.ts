// MAM-34 — the TMDb page's Review tab takes single keys, as HeaderSearch takes "/": 1–5 pick a
// suggestion, 0 says none fits, → skips, / goes to the search field, Enter saves the pick. Which
// keystroke is whose is decided here, apart from the listener, so every rule has a test of its own.

/** The input types a keystroke types into; a radio, a checkbox or a button only takes a key as a command. */
const TEXT_ENTRY_TYPES = new Set(['text', 'search', 'url', 'number', 'email', 'tel', 'password'])

/** What a key does in the Review tab. `index` counts the suggestions from 0. */
export type ReviewKey =
  | { action: 'pick'; index: number }
  | { action: 'noneFits' }
  | { action: 'skip' }
  | { action: 'search' }
  | { action: 'save' }

/**
 * Whether a keystroke at `target` types text into it: a text field, a text area, a select (which
 * takes letters and digits to jump to an option) or editable content. Unlike HeaderSearch's own rule,
 * a radio or a checkbox is not one: the Review tab's 1–5 must still work once a suggestion's radio
 * was clicked, which leaves the focus on it.
 */
export function isTextEntry(target: EventTarget | null): boolean {
  if (target instanceof HTMLInputElement) return TEXT_ENTRY_TYPES.has(target.type)
  return target instanceof HTMLTextAreaElement
    || target instanceof HTMLSelectElement
    || (target instanceof HTMLElement && target.isContentEditable === true)
}

/**
 * Whether → at `target` already moves something of its own: the picked radio of a group (the
 * suggestions, a SegmentedControl) or the tab of a tab list. Taking it there would skip a work and
 * move the pick, or the tab, at once.
 */
export function movesWithArrows(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="radiogroup"], [role="tablist"], .segmented-control') !== null
}

/**
 * What a keydown means to the Review tab, or null to leave it to the page. `pickFormId` is the Save
 * form's id: Enter saves on the page itself, or on a radio or checkbox of the pick (whose form is
 * that one); anywhere else — a button, a link, the paste form's film-or-show choice — Enter keeps its
 * own meaning.
 *
 * Every key is left alone while a dialog is open (a confirmation over the page would otherwise act on
 * the work behind it; Radix marks an open dialog `data-state="open"`), while an input method composes
 * a character, and with Ctrl, Alt or Cmd, which are other programs' shortcuts. Shift is left alone
 * only on Enter and →: on some layouts a digit or "/" is typed with Shift (French AZERTY's digits).
 */
export function reviewKeyOf(event: KeyboardEvent, pickFormId: string): ReviewKey | null {
  if (event.isComposing || event.ctrlKey || event.metaKey || event.altKey) return null
  if (document.querySelector('[role="dialog"][data-state="open"]') !== null) return null
  if (isTextEntry(event.target)) return null

  switch (event.key) {
    case '1':
    case '2':
    case '3':
    case '4':
    case '5':
      return { action: 'pick', index: Number(event.key) - 1 }
    case '0':
      return { action: 'noneFits' }
    case '/':
      return { action: 'search' }
    case 'ArrowRight':
      return event.shiftKey || movesWithArrows(event.target) ? null : { action: 'skip' }
    case 'Enter':
      return event.shiftKey || !savesOnEnter(event.target, pickFormId) ? null : { action: 'save' }
    default:
      return null
  }
}

/** Whether Enter at `target` is the Review tab's Save: on the page itself, or on a box of the pick. */
function savesOnEnter(target: EventTarget | null, pickFormId: string): boolean {
  if (target === document.body || target === document.documentElement || target === document) return true
  return target instanceof HTMLInputElement
    && (target.type === 'radio' || target.type === 'checkbox')
    && target.form?.id === pickFormId
}
