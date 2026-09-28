import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateUserDialog } from '@/components/users/CreateUserDialog'
import { usersApi } from '@/lib/users-api'
import { apiError } from '@/test/api-error'

vi.mock('@/lib/users-api')

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

  it('refuses two different passwords without asking the server', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse batery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    expect(within(dialog).getByRole('alert')).toHaveTextContent('The two passwords are different.')
    expect(usersApi.create).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
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

  it.each([
    [400, 'A user name can contain only letters, digits, dots, hyphens and underscores.'],
    [409, 'A user named “ivan” already exists.'],
  ])('shows the API sentence of a %i and stays open', async (status, sentence) => {
    // Arrange
    vi.mocked(usersApi.create).mockRejectedValue(apiError(status, sentence))
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(sentence)
    expect(onClose).not.toHaveBeenCalled()
    expect(within(dialog).getByRole('button', { name: 'Add user' })).toBeEnabled()
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
