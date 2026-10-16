import { copyFieldText } from '@/lib/clipboard'

/** A field holding `text`, in the page so it can take the focus and a selection. */
function fieldWith(text: string): HTMLTextAreaElement {
  const field = document.createElement('textarea')
  field.value = text
  document.body.append(field)
  return field
}

/** Makes the page a secure one (HTTPS, or localhost) with `clipboard` as its clipboard API. */
function inSecureContext(clipboard: Pick<Clipboard, 'writeText'>) {
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true })
}

/** Makes the page a plain-HTTP one, where browsers give no `navigator.clipboard`. */
function onPlainHttp() {
  Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })
}

/** `document.execCommand`, which jsdom doesn't implement, answering as `answer` does. */
function execCommandThat(answer: () => boolean) {
  const execCommand = vi.fn((command: string) => command === 'copy' && answer())
  Object.defineProperty(document, 'execCommand', { value: execCommand, configurable: true, writable: true })
  return execCommand
}

const secret = 'mam_pat_Nas4Kq7Rt2Vw9Yz1Bc3Df5Gh7Jk9Lm1Np3Qr5St7Uvw'

describe('copyFieldText', () => {
  afterEach(() => {
    // The own properties above shadow jsdom's; deleting them brings jsdom's back.
    Reflect.deleteProperty(window, 'isSecureContext')
    Reflect.deleteProperty(navigator, 'clipboard')
    Reflect.deleteProperty(document, 'execCommand')
    document.body.innerHTML = ''
  })

  it('writes the field\'s text through the clipboard API in a secure context', async () => {
    // Arrange
    const writeText = vi.fn(async () => {})
    inSecureContext({ writeText })
    const execCommand = execCommandThat(() => true)

    // Act
    const copied = await copyFieldText(fieldWith(secret))

    // Assert
    expect(copied).toBe(true)
    expect(writeText).toHaveBeenCalledExactlyOnceWith(secret)
    expect(execCommand).not.toHaveBeenCalled()
  })

  it('selects the field and copies the selection on plain HTTP, where there is no clipboard API', async () => {
    // Arrange
    onPlainHttp()
    const field = fieldWith(secret)
    let selectedWhenCopied = ''
    const execCommand = execCommandThat(() => {
      selectedWhenCopied = field.value.slice(field.selectionStart, field.selectionEnd)
      return true
    })

    // Act
    const copied = await copyFieldText(field)

    // Assert
    expect(copied).toBe(true)
    expect(execCommand).toHaveBeenCalledExactlyOnceWith('copy')
    expect(selectedWhenCopied).toBe(secret)
    expect(document.activeElement).toBe(field)
  })

  it('falls back to the selection when the clipboard API refuses', async () => {
    // Arrange
    const writeText = vi.fn(async () => { throw new DOMException('Document is not focused.', 'NotAllowedError') })
    inSecureContext({ writeText })
    const execCommand = execCommandThat(() => true)

    // Act
    const copied = await copyFieldText(fieldWith(secret))

    // Assert
    expect(copied).toBe(true)
    expect(writeText).toHaveBeenCalledOnce()
    expect(execCommand).toHaveBeenCalledExactlyOnceWith('copy')
  })

  it('answers false, with the text left selected, when the browser won\'t copy the selection', async () => {
    // Arrange
    onPlainHttp()
    const field = fieldWith(secret)
    execCommandThat(() => false)

    // Act
    const copied = await copyFieldText(field)

    // Assert
    expect(copied).toBe(false)
    expect(field.value.slice(field.selectionStart, field.selectionEnd)).toBe(secret)
  })

  it('answers false instead of throwing in a browser without execCommand', async () => {
    // Arrange
    onPlainHttp()
    Object.defineProperty(document, 'execCommand', { value: undefined, configurable: true, writable: true })

    // Act
    const copied = await copyFieldText(fieldWith(secret))

    // Assert
    expect(copied).toBe(false)
  })
})
