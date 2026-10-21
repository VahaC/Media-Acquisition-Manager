import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateAccessTokenDialog } from '@/components/profile/CreateAccessTokenDialog'
import { useAuthStore } from '@/lib/auth-store'
import { i18n } from '@/lib/i18n'
import { profileApi } from '@/lib/profile-api'
import type { CreatedAccessToken } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { member, resetAuthStore, signInAs } from '@/test/auth-session'
import { createdToken } from '@/test/access-tokens'

vi.mock('@/lib/profile-api')

const formTitle = 'Create an access token'
const secretTitle = 'Your new access token'

/** MAM-45 — the lines that connect an AI assistant, spelled out rather than read from the locale. */
const ASSISTANT_TITLE = 'Connect an AI assistant'
const READ_ONLY_NOTE = 'This token is read only: the assistant reads what you can, but can\'t send downloads, cancel following or mark Not interested.'
/** MAM-48 — Claude Code takes up a new server only in a new session. */
const CLAUDE_CODE_HINT = 'Run it once in a terminal; Claude Code then has the app in all your projects, from its next session on.'
/** MAM-48 — Claude Desktop writes its configuration while it runs, so an edit made meanwhile can be lost. */
const CLAUDE_DESKTOP_HINT = 'Quit Claude Desktop completely (closing its window isn\'t enough), paste it into claude_desktop_config.json (into its mcpServers, if it has one already), then start Claude Desktop again. It needs Node.js 18 or later.'
/**
 * MAM-48 — the address both lines carry is this page's, which an assistant can't pass behind Cloudflare
 * Access. Opened at the home-network address instead, Profile builds both lines for it, --allow-http
 * included, so nothing is edited by hand.
 */
const ADDRESS_HINT = 'The command and the configuration below use this page\'s address, which the assistant must reach from its own computer: on your home network or through a VPN. If this address asks for a sign-in in the browser (Cloudflare Access, for example), open the app at its home-network address instead and create a new token there: both then use that address.'
/** jsdom's page address, http://localhost:3000: plain HTTP. CreateAccessTokenDialog.https.test.tsx has the HTTPS one. */
const ORIGIN = window.location.origin
/** The whole secret, unlike the curl example's prefix, so the line runs as it is pasted (Vasyl, 2026-10-16). */
const CLAUDE_CODE_LINE =
  `claude mcp add --transport http --scope user mam ${ORIGIN}/mcp --header "Authorization: Bearer ${createdToken.secret}"`

/** The three texts the shown-once step offers to copy: the field, its Copy, and what a failed copy says. */
const COPYABLE = [
  { field: 'Token', copy: 'Copy the token', failed: 'Select the token and press Ctrl+C.' },
  { field: 'Claude Code', copy: 'Copy the Claude Code command', failed: 'Select the command and press Ctrl+C.' },
  { field: 'Claude Desktop', copy: 'Copy the Claude Desktop configuration', failed: 'Select the configuration and press Ctrl+C.' },
]

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
    // MAM-45 — a settings dialog's width: only the shown-once step widens.
    expect(screen.getByRole('dialog', { name: formTitle })).not.toHaveClass('profile-secret-dialog')
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
    // MAM-45 — wider than the form, for the Claude Desktop block.
    expect(dialog).toHaveClass('profile-secret-dialog')
  })

  it('gives the Claude Code command with the whole token, to run once in a terminal', async () => {
    // Arrange
    const { user } = renderDialog()

    // Act
    const dialog = await createWithDefaults(user)

    // Assert
    const assistant = within(dialog).getByRole('region', { name: ASSISTANT_TITLE })
    const field = within(assistant).getByRole('textbox', { name: 'Claude Code' })
    expect(field).toHaveValue(CLAUDE_CODE_LINE)
    expect(field).toHaveAttribute('readonly')
    expect(field).toHaveAccessibleDescription(CLAUDE_CODE_HINT)
    expect(within(assistant).getByRole('button', { name: 'Copy the Claude Code command' })).toHaveTextContent('Copy')
  })

  it('gives the Claude Desktop configuration through the pinned mcp-remote, the header without spaces and --allow-http on a plain-HTTP address', async () => {
    // Arrange
    const { user } = renderDialog()

    // Act
    const dialog = await createWithDefaults(user)

    // Assert
    const assistant = within(dialog).getByRole('region', { name: ASSISTANT_TITLE })
    const field = within(assistant).getByRole('textbox', { name: 'Claude Desktop' }) as HTMLTextAreaElement
    // MAM-48 — the release checked live on 2026-10-21, never whatever npm has newest: the token passes through it.
    expect(JSON.parse(field.value)).toEqual({
      mcpServers: {
        mam: {
          command: 'npx',
          args: ['-y', 'mcp-remote@0.15.2', `${ORIGIN}/mcp`, '--transport', 'http-only', '--allow-http', '--header', 'Authorization:${AUTH_HEADER}'],
          env: { AUTH_HEADER: `Bearer ${createdToken.secret}` },
        },
      },
    })
    // Laid out two spaces a level, one value a line, and every line in view.
    expect(field.value).toContain('\n  "mcpServers": {\n    "mam": {\n')
    expect(field).toHaveAttribute('rows', String(field.value.split('\n').length))
    expect(field).toHaveAttribute('readonly')
    expect(field).toHaveAccessibleDescription(CLAUDE_DESKTOP_HINT)
    expect(within(assistant).getByRole('button', { name: 'Copy the Claude Desktop configuration' })).toHaveTextContent('Copy')
  })

  it('says above both lines that the assistant must reach this page\'s address from its own computer', async () => {
    // Arrange
    const { user } = renderDialog()

    // Act
    const dialog = await createWithDefaults(user)

    // Assert
    const assistant = within(dialog).getByRole('region', { name: ASSISTANT_TITLE })
    const hint = within(assistant).getByText(ADDRESS_HINT)
    expect(hint).toHaveClass('profile-note')
    for (const name of ['Claude Code', 'Claude Desktop']) {
      const field = within(assistant).getByRole('textbox', { name })
      expect(hint.compareDocumentPosition(field) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      // Said once for both, not in either line's own hint.
      expect(field).not.toHaveAccessibleDescription(ADDRESS_HINT)
    }
  })

  it('says a Read only token\'s assistant reads but can\'t send', async () => {
    // Arrange
    const { user } = renderDialog()

    // Act
    const dialog = await createWithDefaults(user)

    // Assert
    expect(createdToken.token.access).toBe('ReadOnly')
    expect(within(within(dialog).getByRole('region', { name: ASSISTANT_TITLE })).getByText(READ_ONLY_NOTE)).toBeInTheDocument()
  })

  it('says nothing of the kind for a Read and download token, whose lines are the same', async () => {
    // Arrange
    vi.mocked(profileApi.createAccessToken).mockResolvedValue(
      { ...createdToken, token: { ...createdToken.token, access: 'ReadAndDownload' } })
    const { user } = renderDialog()

    // Act
    const dialog = await createWithDefaults(user)

    // Assert
    const assistant = within(dialog).getByRole('region', { name: ASSISTANT_TITLE })
    expect(within(assistant).queryByText(READ_ONLY_NOTE)).not.toBeInTheDocument()
    expect(within(assistant).getByText(ADDRESS_HINT)).toBeInTheDocument()
    expect(within(assistant).getByRole('textbox', { name: 'Claude Code' })).toHaveValue(CLAUDE_CODE_LINE)
  })

  it.each(COPYABLE)('copies $field through the clipboard in a secure context, and says so beside its own Copy', async ({ field, copy }) => {
    // Arrange
    const { user } = renderDialog()
    Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
    const dialog = await createWithDefaults(user)
    const button = within(dialog).getByRole('button', { name: copy })

    // Act
    await user.click(button)

    // Assert
    expect(await navigator.clipboard.readText()).toBe((within(dialog).getByRole('textbox', { name: field }) as HTMLTextAreaElement).value)
    const copied = await within(dialog).findByText('Copied.')
    expect(copied).toHaveAttribute('role', 'status')
    // Each text has its own line: the other two Copy buttons say nothing.
    expect(button.parentElement).toContainElement(copied)
    expect(within(dialog).getAllByText('Copied.')).toHaveLength(1)
  })

  it.each(COPYABLE)('copies $field by selecting it on a plain-HTTP address, where there is no clipboard API', async ({ field, copy }) => {
    // Arrange
    const { user } = renderDialog()
    const dialog = await createWithDefaults(user)
    onPlainHttp()
    const execCommand = execCommandAnswering(true)
    const button = within(dialog).getByRole('button', { name: copy })

    // Act
    await user.click(button)

    // Assert
    const text = within(dialog).getByRole('textbox', { name: field }) as HTMLTextAreaElement
    expect(execCommand).toHaveBeenCalledExactlyOnceWith('copy')
    expect(text).toHaveFocus()
    expect(selectedText(text)).toBe(text.value)
    expect(button.parentElement).toContainElement(await within(dialog).findByText('Copied.'))
  })

  it.each(COPYABLE)('says to press Ctrl+C, with $field selected, when the browser copies neither way', async ({ field, copy, failed }) => {
    // Arrange
    const { user } = renderDialog()
    const dialog = await createWithDefaults(user)
    onPlainHttp()
    execCommandAnswering(false)
    const button = within(dialog).getByRole('button', { name: copy })

    // Act
    await user.click(button)

    // Assert
    const line = await within(dialog).findByText(failed)
    expect(line).toHaveAttribute('role', 'status')
    expect(button.parentElement).toContainElement(line)
    expect(within(dialog).queryByText('Copied.')).not.toBeInTheDocument()
    const text = within(dialog).getByRole('textbox', { name: field }) as HTMLTextAreaElement
    expect(text).toHaveFocus()
    expect(selectedText(text)).toBe(text.value)
  })

  it('copies the whole secret with the token\'s Copy', async () => {
    // Arrange
    const { user } = renderDialog()
    Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
    const dialog = await createWithDefaults(user)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Copy the token' }))

    // Assert
    expect(await navigator.clipboard.readText()).toBe(createdToken.secret)
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

describe('CreateAccessTokenDialog and the UI language', () => {
  // MAM-46 — the signed-in pages mount afresh when the UI's language changes, which would take the
  // shown-once secret with them: a language followed from another tab waits until the secret step closes.
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.createAccessToken).mockResolvedValue(createdToken)
  })

  afterEach(async () => {
    resetAuthStore()
    // The app's one i18n instance: a test that switched it to Ukrainian must not leave it there.
    await act(() => i18n.changeLanguage('en-US'))
  })

  it('keeps the UI in its language while the secret shows, and takes another tab\'s language once it closes', async () => {
    // Arrange
    const { user } = renderDialog()
    const dialog = await createWithDefaults(user)
    const otherTab = new BroadcastChannel('mam-auth')

    // Act
    otherTab.postMessage({ type: 'language', userId: member.id, language: 'uk' })
    await vi.waitFor(() => expect(useAuthStore.getState().user?.language).toBe('uk'))
    otherTab.close()
    const languageWhileShown = i18n.language
    const secretStillShown = within(dialog).getByRole('textbox', { name: 'Token' })
    cleanup()

    // Assert
    expect(languageWhileShown).toBe('en-US')
    expect(secretStillShown).toHaveValue(createdToken.secret)
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
  })
})
