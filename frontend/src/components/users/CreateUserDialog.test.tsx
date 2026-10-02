import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateUserDialog } from '@/components/users/CreateUserDialog'
import { usersApi } from '@/lib/users-api'
import { apiError, networkError } from '@/test/api-error'

vi.mock('@/lib/users-api')

const PASSWORDS_DIFFER = 'The two passwords are different. Type the same password in both fields.'
const NAME_TAKEN = 'A user named “ivan” already exists.'

function renderDialog() {
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <CreateUserDialog onClose={onClose} />
    </QueryClientProvider>,
  )
  return { onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name: 'Add user' }) }
}

async function fillIn(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, password: string, confirmPassword: string) {
  await user.type(within(dialog).getByLabelText('User name'), ' ivan ')
  await user.type(within(dialog).getByLabelText('Password'), password)
  await user.type(within(dialog).getByLabelText('Confirm password'), confirmPassword)
}

describe('CreateUserDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('puts Add user first and Cancel last', () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    const footer = within(dialog).getByRole('button', { name: 'Cancel' }).parentElement!

    // Assert
    expect(within(footer).getAllByRole('button').map((button) => button.textContent)).toEqual(['Add user', 'Cancel'])
  })

  it('refuses two different passwords under Confirm password, without asking the server', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse batery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    const confirm = within(dialog).getByLabelText('Confirm password')
    expect(confirm).toHaveAttribute('aria-invalid', 'true')
    expect(confirm).toHaveAccessibleDescription(PASSWORDS_DIFFER)
    expect(confirm).toHaveAccessibleName('Confirm password')
    expect(within(dialog).getAllByRole('alert')).toHaveLength(1)
    expect(within(dialog).getByLabelText('Password')).not.toHaveAttribute('aria-invalid')
    expect(within(dialog).getByLabelText('User name')).not.toHaveAttribute('aria-invalid')
    expect(usersApi.create).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it.each(['Password', 'Confirm password'])('lets go of the mismatch once %s is edited', async (label) => {
    // Arrange
    const { dialog, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse batery')
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Act
    await user.type(within(dialog).getByLabelText(label), 'y')

    // Assert
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(within(dialog).getByLabelText('Confirm password')).not.toHaveAttribute('aria-invalid')
  })

  it('adds the user and closes', async () => {
    // Arrange
    vi.mocked(usersApi.create).mockResolvedValue({
      id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000004',
      userName: 'ivan',
      isOwner: false,
      isDisabled: false,
      createdUtc: '2026-09-28T12:00:00Z',
      lastSignInUtc: null,
    })
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(usersApi.create).toHaveBeenCalledWith({ userName: 'ivan', password: 'correct horse battery' })
  })

  it('shows a taken name, the 409, under User name and stays open', async () => {
    // Arrange
    vi.mocked(usersApi.create).mockRejectedValue(apiError(409, NAME_TAKEN))
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    const line = await within(dialog).findByRole('alert')
    expect(line).toHaveTextContent(NAME_TAKEN)
    expect(line).toHaveClass('ui-field-error')
    const userName = within(dialog).getByLabelText('User name')
    expect(userName).toHaveAttribute('aria-invalid', 'true')
    expect(userName).toHaveAccessibleDescription(NAME_TAKEN)
    expect(userName).toHaveValue(' ivan ')
    expect(within(dialog).getByLabelText('Confirm password')).not.toHaveAttribute('aria-invalid')
    expect(onClose).not.toHaveBeenCalled()
    expect(within(dialog).getByRole('button', { name: 'Add user' })).toBeEnabled()
  })

  it('lets go of a taken name once the user name is edited', async () => {
    // Arrange
    vi.mocked(usersApi.create).mockRejectedValue(apiError(409, NAME_TAKEN))
    const { dialog, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))
    await within(dialog).findByRole('alert')

    // Act
    await user.type(within(dialog).getByLabelText('User name'), '2')

    // Assert
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(within(dialog).getByLabelText('User name')).not.toHaveAttribute('aria-invalid')
  })

  it.each([
    // A rule the server words in English only: no copy of it here says which field it is about.
    ['a 400 rule', apiError(400, 'A user name can contain only letters, digits, dots, hyphens and underscores.'),
      'A user name can contain only letters, digits, dots, hyphens and underscores.'],
    ['a 500 without a sentence', apiError(500), 'Could not add the user.'],
    ['a server that could not be reached', networkError(),
      'The server could not be reached. Check that it is running, then try again.'],
  ])('shows %s in a banner above the buttons, and keeps what was typed', async (_, failure, sentence) => {
    // Arrange
    vi.mocked(usersApi.create).mockRejectedValue(failure)
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    const banner = await within(dialog).findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    // Above the buttons, outside the scrolling fields.
    expect(banner).toHaveClass('ui-banner')
    expect(banner.nextElementSibling).toContainElement(within(dialog).getByRole('button', { name: 'Add user' }))
    for (const label of ['User name', 'Password', 'Confirm password']) {
      expect(within(dialog).getByLabelText(label)).not.toHaveAttribute('aria-invalid')
    }
    expect(within(dialog).getByLabelText('User name')).toHaveValue(' ivan ')
    expect(within(dialog).getByLabelText('Password')).toHaveValue('correct horse battery')
    expect(onClose).not.toHaveBeenCalled()
  })

  it('reads "Adding…" and is disabled while the server answers', async () => {
    // Arrange
    vi.mocked(usersApi.create).mockReturnValue(new Promise(() => {}))
    const { dialog, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    const submit = await within(dialog).findByRole('button', { name: 'Adding…' })
    expect(submit).toBeDisabled()
    expect(usersApi.create).toHaveBeenCalledOnce()
  })

  it('closes on Cancel without adding anyone', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog()

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
    expect(usersApi.create).not.toHaveBeenCalled()
  })
})
