import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ResetPasswordDialog } from '@/components/users/ResetPasswordDialog'
import type { UserSummary } from '@/lib/types'
import { usersApi } from '@/lib/users-api'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'

vi.mock('@/lib/users-api')

const olena: UserSummary = {
  id: member.id,
  userName: 'olena',
  isOwner: false,
  isDisabled: false,
  createdUtc: '2026-09-10T08:00:00Z',
  lastSignInUtc: null,
}

function renderDialog(target: UserSummary = olena) {
  signInAs(owner)
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ResetPasswordDialog user={target} onClose={onClose} />
    </QueryClientProvider>,
  )
  const dialog = screen.getByRole('dialog', { name: `Reset the password of ${target.userName}` })
  return { onClose, user: userEvent.setup(), dialog }
}

async function fillIn(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, password: string, confirmPassword: string) {
  await user.type(within(dialog).getByLabelText('New password'), password)
  await user.type(within(dialog).getByLabelText('Confirm password'), confirmPassword)
}

describe('ResetPasswordDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('warns that the user is signed out on every device', () => {
    // Arrange
    const target = olena

    // Act
    const { dialog } = renderDialog(target)

    // Assert
    expect(dialog).toHaveTextContent('olena is signed out on every device and signs in again with the new password.')
  })

  it('tells the owner resetting their own password that this device stays signed in', () => {
    // Arrange
    const ownRow: UserSummary = { ...olena, id: owner.id, userName: 'vasyl', isOwner: true }

    // Act
    const { dialog } = renderDialog(ownRow)

    // Assert
    expect(dialog).toHaveTextContent('Your other devices are signed out. This one stays signed in.')
  })

  it('refuses two different passwords without asking the server', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse batery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Assert
    expect(within(dialog).getByRole('alert')).toHaveTextContent('The two passwords are different.')
    expect(usersApi.resetPassword).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('sets the new password and closes', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockResolvedValue()
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(usersApi.resetPassword).toHaveBeenCalledWith(olena.id, 'correct horse battery')
  })

  it('shows the API sentence and stays open when the server refuses the password', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockRejectedValue(apiError(400, 'A password must be at least 8 characters long.'))
    const { dialog, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'short', 'short')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('A password must be at least 8 characters long.')
    expect(onClose).not.toHaveBeenCalled()
  })

  it('puts Reset password first and Cancel last', () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    const footer = within(dialog).getByRole('button', { name: 'Cancel' }).parentElement!

    // Assert
    expect(within(footer).getAllByRole('button').map((button) => button.textContent)).toEqual(['Reset password', 'Cancel'])
  })
})
