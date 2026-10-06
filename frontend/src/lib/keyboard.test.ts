import { isTextEntry, movesWithArrows, reviewKeyOf } from '@/lib/keyboard'

// MAM-34 — which keystrokes the TMDb page's Review tab takes (R24): 1–5, 0, →, / and Enter, but
// never one typed into a field, an arrow of a radio group or a tab list, a key while a dialog is open,
// or one with Ctrl, Alt or Cmd.

const PICK_FORM = 'tmdb-review-pick'

/** A keydown as the document's capture listener sees it, coming from `target`. */
function keydown(key: string, target: EventTarget = document.body, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init })
  Object.defineProperty(event, 'target', { value: target })
  return event
}

/** `html` put in the page; the element it starts with. */
function mount<T extends Element>(html: string, selector: string): T {
  document.body.innerHTML = html
  return document.querySelector<T>(selector)!
}

describe('keyboard', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  describe('isTextEntry', () => {
    it.each(['text', 'search', 'url', 'number', 'email', 'tel', 'password'])('counts an input of type %s as typing', (type) => {
      // Arrange
      const input = mount<HTMLInputElement>(`<input type="${type}">`, 'input')

      // Act
      const typing = isTextEntry(input)

      // Assert
      expect(typing).toBe(true)
    })

    it.each(['radio', 'checkbox', 'button', 'submit'])('does not count an input of type %s as typing', (type) => {
      // Arrange
      const input = mount<HTMLInputElement>(`<input type="${type}">`, 'input')

      // Act
      const typing = isTextEntry(input)

      // Assert
      expect(typing).toBe(false)
    })

    it('counts a text area, a select and editable content as typing, and a button or the page not', () => {
      // Arrange
      document.body.innerHTML = '<textarea></textarea><select></select><div contenteditable="true"></div><button></button>'
      const editable = document.querySelector<HTMLElement>('[contenteditable]')!
      // jsdom computes no isContentEditable of its own.
      Object.defineProperty(editable, 'isContentEditable', { value: true })

      // Act
      const typing = [
        document.querySelector('textarea'),
        document.querySelector('select'),
        editable,
        document.querySelector('button'),
        document.body,
      ].map(isTextEntry)

      // Assert
      expect(typing).toEqual([true, true, true, false, false])
    })
  })

  describe('movesWithArrows', () => {
    it('is true inside a radio group, a tab list and a segmented control, and false elsewhere', () => {
      // Arrange
      document.body.innerHTML = `
        <div role="radiogroup"><input type="radio" id="radio"></div>
        <div role="tablist"><button id="tab"></button></div>
        <div class="segmented-control"><span id="pill"></span></div>
        <button id="plain"></button>`

      // Act
      const moves = ['radio', 'tab', 'pill', 'plain'].map((id) => movesWithArrows(document.getElementById(id)))

      // Assert
      expect(moves).toEqual([true, true, true, false])
    })
  })

  describe('reviewKeyOf', () => {
    it('reads 1 to 5 as picking the suggestions from the first, 0 as none fits, → as skip and / as search', () => {
      // Arrange
      const keys = ['1', '2', '3', '4', '5', '0', 'ArrowRight', '/']

      // Act
      const actions = keys.map((key) => reviewKeyOf(keydown(key), PICK_FORM))

      // Assert
      expect(actions).toEqual([
        { action: 'pick', index: 0 },
        { action: 'pick', index: 1 },
        { action: 'pick', index: 2 },
        { action: 'pick', index: 3 },
        { action: 'pick', index: 4 },
        { action: 'noneFits' },
        { action: 'skip' },
        { action: 'search' },
      ])
    })

    it('leaves every other key alone, 6 to 9 included', () => {
      // Arrange
      const keys = ['6', '9', 'a', 'ArrowLeft', 'Escape', ' ']

      // Act
      const actions = keys.map((key) => reviewKeyOf(keydown(key), PICK_FORM))

      // Assert
      expect(actions).toEqual([null, null, null, null, null, null])
    })

    it('leaves the keys to a text field being typed into', () => {
      // Arrange
      const input = mount<HTMLInputElement>('<input type="search">', 'input')

      // Act
      const actions = ['1', '0', '/', 'ArrowRight', 'Enter'].map((key) => reviewKeyOf(keydown(key, input), PICK_FORM))

      // Assert
      expect(actions).toEqual([null, null, null, null, null])
    })

    it('takes the digits on a focused suggestion radio, but leaves → to its radio group', () => {
      // Arrange
      const radio = mount<HTMLInputElement>(`<form id="${PICK_FORM}"><div role="radiogroup"><input type="radio"></div></form>`, 'input')

      // Act
      const pick = reviewKeyOf(keydown('2', radio), PICK_FORM)
      const arrow = reviewKeyOf(keydown('ArrowRight', radio), PICK_FORM)

      // Assert
      expect(pick).toEqual({ action: 'pick', index: 1 })
      expect(arrow).toBeNull()
    })

    it('leaves → to a tab list and to a segmented control', () => {
      // Arrange
      document.body.innerHTML = '<div role="tablist"><button id="tab"></button></div><div class="segmented-control"><label id="pill"></label></div>'

      // Act
      const onTab = reviewKeyOf(keydown('ArrowRight', document.getElementById('tab')!), PICK_FORM)
      const onPill = reviewKeyOf(keydown('ArrowRight', document.getElementById('pill')!), PICK_FORM)

      // Assert
      expect(onTab).toBeNull()
      expect(onPill).toBeNull()
    })

    it('saves on Enter on the page and on a box of the pick form, and leaves it to a button and another form', () => {
      // Arrange
      document.body.innerHTML = `
        <form id="${PICK_FORM}"><input type="checkbox" id="item"></form>
        <input type="radio" id="result" form="${PICK_FORM}">
        <form role="search"><input type="radio" id="kind"></form>
        <button id="skip"></button>`
      const target = (id: string) => document.getElementById(id)!

      // Act
      const actions = [document.body, target('item'), target('result'), target('kind'), target('skip')]
        .map((element) => reviewKeyOf(keydown('Enter', element), PICK_FORM))

      // Assert
      expect(actions).toEqual([{ action: 'save' }, { action: 'save' }, { action: 'save' }, null, null])
    })

    it.each([
      ['Ctrl', { ctrlKey: true }],
      ['Alt', { altKey: true }],
      ['Cmd', { metaKey: true }],
      ['an input method composing', { isComposing: true }],
    ])('leaves every key alone with %s', (_, init) => {
      // Arrange
      const keys = ['1', '0', '/', 'ArrowRight', 'Enter']

      // Act
      const actions = keys.map((key) => reviewKeyOf(keydown(key, document.body, init), PICK_FORM))

      // Assert
      expect(actions).toEqual([null, null, null, null, null])
    })

    it('leaves Shift+→ and Shift+Enter alone, but takes a digit or / typed with Shift', () => {
      // Arrange — on French AZERTY a digit is typed with Shift.
      const shifted = { shiftKey: true }

      // Act
      const actions = ['ArrowRight', 'Enter', '1', '/'].map((key) => reviewKeyOf(keydown(key, document.body, shifted), PICK_FORM))

      // Assert
      expect(actions).toEqual([null, null, { action: 'pick', index: 0 }, { action: 'search' }])
    })

    it('leaves every key alone while a dialog is open', () => {
      // Arrange
      document.body.innerHTML = '<div role="dialog" data-state="open"></div>'

      // Act
      const actions = ['1', '0', '/', 'ArrowRight', 'Enter'].map((key) => reviewKeyOf(keydown(key), PICK_FORM))

      // Assert
      expect(actions).toEqual([null, null, null, null, null])
    })
  })
})
