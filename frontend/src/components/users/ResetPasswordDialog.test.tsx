import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ResetPasswordDialog } from '@/components/users/ResetPasswordDialog'
import type { UserSummary } from '@/lib/types'
import { usersApi } from '@/lib/users-api'
import { apiError, networkError } from '@/test/api-error'
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
  const onSaved = vi.fn()
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ResetPasswordDialog user={target} onSaved={onSaved} onClose={onClose} />
    </QueryClientProvider>,
  )
  const dialog = screen.getByRole('dialog', { name: `Reset the password of ${target.userName}` })
  return { onSaved, onClose, user: userEvent.setup(), dialog }
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

  it('refuses two different passwords under Confirm password, without asking the server', async () => {
    // Arrange
    const { dialog, onSaved, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse batery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Assert
    const confirm = within(dialog).getByLabelText('Confirm password')
    expect(confirm).toHaveAttribute('aria-invalid', 'true')
    expect(confirm).toHaveAccessibleDescription('The two passwords are different. Type the same password in both fields.')
    expect(within(dialog).getByLabelText('New password')).not.toHaveAttribute('aria-invalid')
    expect(usersApi.resetPassword).not.toHaveBeenCalled()
    expect(onSaved).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('lets go of the mismatch once a password is edited', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse batery')
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Act
    await user.type(within(dialog).getByLabelText('New password'), 'y')

    // Assert
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(within(dialog).getByLabelText('Confirm password')).not.toHaveAttribute('aria-invalid')
  })

  it('sets the new password, tells the page, then closes', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockResolvedValue()
    const { dialog, onSaved, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'correct horse battery', 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(onSaved).toHaveBeenCalledOnce()
    expect(onSaved.mock.invocationCallOrder[0]).toBeLessThan(onClose.mock.invocationCallOrder[0])
    expect(usersApi.resetPassword).toHaveBeenCalledWith(olena.id, 'correct horse battery')
  })

  it.each([
    ['a refused password', apiError(400, 'A password must be at least 8 characters long.'),
      'A password must be at least 8 characters long.'],
    ['a user who is gone, the bare 404', apiError(404), 'Could not reset the password.'],
    ['a server that could not be reached', networkError(),
      'The server could not be reached. Check that it is running, then try again.'],
  ])('keeps the dialog open on %s, with the sentence in a banner above the buttons', async (_, failure, sentence) => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockRejectedValue(failure)
    const { dialog, onSaved, onClose, user } = renderDialog()
    await fillIn(user, dialog, 'short', 'short')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))

    // Assert
    const banner = await within(dialog).findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    expect(banner).toHaveClass('ui-banner')
    expect(banner.nextElementSibling).toContainElement(within(dialog).getByRole('button', { name: 'Reset password' }))
    expect(within(dialog).getByLabelText('New password')).toHaveValue('short')
    expect(within(dialog).getByLabelText('New password')).not.toHaveAttribute('aria-invalid')
    expect(onSaved).not.toHaveBeenCalled()
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
