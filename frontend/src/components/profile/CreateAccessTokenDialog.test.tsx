import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateAccessTokenDialog } from '@/components/profile/CreateAccessTokenDialog'
import { profileApi } from '@/lib/profile-api'
import type { CreatedAccessToken } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { member, signInAs } from '@/test/auth-session'
import { createdToken } from '@/test/access-tokens'

vi.mock('@/lib/profile-api')

const formTitle = 'Create an access token'
const secretTitle = 'Your new access token'

function renderDialog() {
  signInAs(member)
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <CreateAccessTokenDialog onClose={onClose} />
    </QueryClientProvider>,
  )
  return { client, onClose, user: userEvent.setup() }
}

/** Fills in a name and creates the token with the access and lifetime the dialog opened with. */
async function createWithDefaults(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Claude on the NAS')
  await user.click(screen.getByRole('button', { name: 'Create token' }))
  return screen.findByRole('dialog', { name: secretTitle })
}

/** The part of the secret field that is selected. */
const selectedText = (field: HTMLTextAreaElement) => field.value.slice(field.selectionStart, field.selectionEnd)

/** Makes the page a plain-HTTP one: no secure context, so no `navigator.clipboard`. */
function onPlainHttp() {
  Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })
}

/** `document.execCommand`, which jsdom doesn't implement, answering `copied`. */
function execCommandAnswering(copied: boolean) {
  const execCommand = vi.fn(() => copied)
  Object.defineProperty(document, 'execCommand', { value: execCommand, configurable: true, writable: true })
  return execCommand
}

describe('CreateAccessTokenDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.createAccessToken).mockResolvedValue(createdToken)
  })

  afterEach(() => {
    // The own properties the tests define shadow jsdom's (or user-event's); deleting them brings those back.
    Reflect.deleteProperty(window, 'isSecureContext')
    Reflect.deleteProperty(navigator, 'clipboard')
    Reflect.deleteProperty(document, 'execCommand')
  })

  it('opens with Read only and 90 days picked, each access described by what it may do', () => {
    // Arrange
    renderDialog()

    // Act
    const readOnly = screen.getByRole('radio', { name: 'Read only' })
    const readAndDownload = screen.getByRole('radio', { name: 'Read and download' })
    const expires = screen.getByRole('combobox', { name: 'Expires' })

    // Assert
    expect(screen.getByRole('dialog', { name: formTitle })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus()
    expect(readOnly).toBeChecked()
    expect(readOnly).toHaveAccessibleDescription('Reads the library, searches, jobs, duplicates and recommendations.')
    expect(readAndDownload).not.toBeChecked()
    expect(readAndDownload).toHaveAccessibleDescription(
      'Also sends downloads (Keep both), cancels following and marks Not interested.')
    expect(screen.getByRole('group', { name: 'Access' })).toBeInTheDocument()
    expect(expires).toHaveValue('Days90')
    expect(within(expires).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['In 30 days', 'In 90 days', 'In 1 year', 'Never'])
    // A browser names a select wrapped in its label by its value too (MAM-4).
    expect(expires.closest('label')).toBeNull()
  })

  it('creates a token from the trimmed name and the access and lifetime picked', async () => {
    // Arrange
    const { user } = renderDialog()
    await user.type(screen.getByRole('textbox', { name: 'Name' }), '  Claude on the NAS  ')
    await user.click(screen.getByRole('radio', { name: 'Read and download' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Expires' }), 'In 1 year')

    // Act
    await user.click(screen.getByRole('button', { name: 'Create token' }))

    // Assert
    expect(await screen.findByRole('dialog', { name: secretTitle })).toBeInTheDocument()
    expect(profileApi.createAccessToken).toHaveBeenCalledExactlyOnceWith(
      { name: 'Claude on the NAS', access: 'ReadAndDownload', lifetime: 'OneYear' })
  })

  it('shows the secret once, selected in a read-only field, with the warning and an example of a script sending it', async () => {
    // Arrange
    const { user } = renderDialog()

    // Act
    const dialog = await createWithDefaults(user)

    // Assert
    const field = within(dialog).getByRole('textbox', { name: 'Token' }) as HTMLTextAreaElement
    expect(field).toHaveValue(createdToken.secret)
    expect(field).toHaveAttribute('readonly')
    expect(field).toHaveFocus()
    expect(selectedText(field)).toBe(createdToken.secret)
    expect(dialog).toHaveTextContent(
      '“Claude on the NAS” is ready. Keep it like a password: anyone who has it can use the app as you.')
    expect(within(dialog).getByRole('status')).toHaveTextContent('Copy it now: it won\'t be shown again.')
    expect(within(dialog).getByText(`curl -H "Authorization: Bearer mam_pat_Nas4Kq…" ${window.location.origin}/api/downloads`))
      .toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Create token' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Done' })).toBeInTheDocument()
  })

  it('copies the secret through the clipboard in a secure context, and says so', async () => {
    // Arrange
    const { user } = renderDialog()
    Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
    const dialog = await createWithDefaults(user)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Copy' }))

    // Assert
    expect(await navigator.clipboard.readText()).toBe(createdToken.secret)
    expect(await within(dialog).findByText('Copied.')).toHaveAttribute('role', 'status')
  })

  it('copies by selecting the token on a plain-HTTP address, where there is no clipboard API', async () => {
    // Arrange
    const { user } = renderDialog()
    const dialog = await createWithDefaults(user)
    onPlainHttp()
    const execCommand = execCommandAnswering(true)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Copy' }))

    // Assert
    expect(execCommand).toHaveBeenCalledExactlyOnceWith('copy')
    expect(selectedText(within(dialog).getByRole('textbox', { name: 'Token' }) as HTMLTextAreaElement)).toBe(createdToken.secret)
    expect(await within(dialog).findByText('Copied.')).toBeInTheDocument()
  })

  it('says to press Ctrl+C, with the token selected, when the browser copies neither way', async () => {
    // Arrange
    const { user } = renderDialog()
    const dialog = await createWithDefaults(user)
    onPlainHttp()
    execCommandAnswering(false)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Copy' }))

    // Assert
    expect(await within(dialog).findByText('Select the token and press Ctrl+C.')).toHaveAttribute('role', 'status')
    expect(within(dialog).queryByText('Copied.')).not.toBeInTheDocument()
    const field = within(dialog).getByRole('textbox', { name: 'Token' }) as HTMLTextAreaElement
    expect(field).toHaveFocus()
    expect(selectedText(field)).toBe(createdToken.secret)
  })

  it('puts a refused name under Name, in the server\'s words, and stays on the form', async () => {
    // Arrange
    vi.mocked(profileApi.createAccessToken).mockRejectedValue(apiError(400, 'Give the token a name, such as “Claude on the laptop”.'))
    const { user } = renderDialog()

    // Act
    await user.click(screen.getByRole('button', { name: 'Create token' }))

    // Assert
    const name = screen.getByRole('textbox', { name: 'Name' })
    expect(await screen.findByText('Give the token a name, such as “Claude on the laptop”.')).toBeInTheDocument()
    expect(name).toHaveAttribute('aria-invalid', 'true')
    expect(name).toHaveAccessibleDescription('Give the token a name, such as “Claude on the laptop”.')
    expect(screen.getByRole('dialog', { name: formTitle })).toBeInTheDocument()
    expect(profileApi.createAccessToken).toHaveBeenCalledExactlyOnceWith({ name: '', access: 'ReadOnly', lifetime: 'Days90' })
  })

  it('takes the name\'s refusal away once the name is edited', async () => {
    // Arrange
    vi.mocked(profileApi.createAccessToken).mockRejectedValue(apiError(400, 'A token\'s name can be at most 100 characters.'))
    const { user } = renderDialog()
    await user.click(screen.getByRole('button', { name: 'Create token' }))
    await screen.findByText('A token\'s name can be at most 100 characters.')

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'x')

    // Assert
    expect(screen.queryByText('A token\'s name can be at most 100 characters.')).not.toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Name' })).not.toHaveAttribute('aria-invalid')
  })

  it('says any other failure above the buttons and keeps what was typed', async () => {
    // Arrange
    vi.mocked(profileApi.createAccessToken).mockRejectedValue(networkError())
    const { user } = renderDialog()
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Claude on the NAS')
    await user.click(screen.getByRole('radio', { name: 'Read and download' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Create token' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The server could not be reached.')
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Claude on the NAS')
    expect(screen.getByRole('textbox', { name: 'Name' })).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('radio', { name: 'Read and download' })).toBeChecked()
  })

  it('keeps the secret out of the query cache, and the create out of the mutation cache once the secret shows', async () => {
    // Arrange
    const { client, user } = renderDialog()

    // Act
    await createWithDefaults(user)

    // Assert
    expect(JSON.stringify(client.getQueryCache().getAll().map((query) => query.state.data))).not.toContain(createdToken.secret)
    await vi.waitFor(() => expect(client.getMutationCache().getAll()).toEqual([]))
  })

  it('stays open on a click outside while the secret shows, and Done closes it', async () => {
    // Arrange
    const { onClose, user } = renderDialog()
    const dialog = await createWithDefaults(user)
    await user.click(document.querySelector('.ui-dialog-overlay')!)
    const closedByClickOutside = onClose.mock.calls.length

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Done' }))

    // Assert
    expect(closedByClickOutside).toBe(0)
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('cannot be closed while the token is being created, so the token it makes always shows its secret', async () => {
    // Arrange
    let answer: (created: CreatedAccessToken) => void = () => {}
    vi.mocked(profileApi.createAccessToken).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { onClose, user } = renderDialog()
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Claude on the NAS')
    await user.click(screen.getByRole('button', { name: 'Create token' }))
    const form = screen.getByRole('dialog', { name: formTitle })
    const cancel = within(form).getByRole('button', { name: 'Cancel' })
    const cancelDisabledWhileCreating = cancel.hasAttribute('disabled')

    // Act
    await user.keyboard('{Escape}')
    await user.click(document.querySelector('.ui-dialog-overlay')!)
    await user.click(within(form).getByRole('button', { name: 'Close' }))
    await user.click(cancel)
    const closedWhileCreating = onClose.mock.calls.length
    answer(createdToken)

    // Assert
    expect(cancelDisabledWhileCreating).toBe(true)
    expect(closedWhileCreating).toBe(0)
    expect(await screen.findByRole('dialog', { name: secretTitle })).toHaveTextContent(createdToken.secret)
    expect(profileApi.createAccessToken).toHaveBeenCalledOnce()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('can be closed again once a create has failed', async () => {
    // Arrange
    vi.mocked(profileApi.createAccessToken).mockRejectedValue(networkError())
    const { onClose, user } = renderDialog()
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Claude on the NAS')
    await user.click(screen.getByRole('button', { name: 'Create token' }))
    await screen.findByRole('alert')

    // Act
    await user.keyboard('{Escape}')

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('closes on a click outside before anything is created', async () => {
    // Arrange
    const { onClose, user } = renderDialog()

    // Act
    await user.click(document.querySelector('.ui-dialog-overlay')!)

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
    expect(profileApi.createAccessToken).not.toHaveBeenCalled()
  })

  it('closes on Cancel without creating anything', async () => {
    // Arrange
    const { onClose, user } = renderDialog()
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Claude on the NAS')

    // Act
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
    expect(profileApi.createAccessToken).not.toHaveBeenCalled()
  })
})
