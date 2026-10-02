import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { ChangePasswordCard } from '@/components/profile/ChangePasswordCard'
import { authApi } from '@/lib/auth-api'
import { apiError, networkError } from '@/test/api-error'

vi.mock('@/lib/auth-api')

const CURRENT_PASSWORD = 'old horse battery'
const NEW_PASSWORD = 'correct horse battery staple'

function renderCard() {
  const onChanged = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ChangePasswordCard onChanged={onChanged} />
    </QueryClientProvider>,
  )
  return { user: userEvent.setup(), onChanged }
}

const currentField = () => screen.getByLabelText('Current password')
const newField = () => screen.getByLabelText('New password')
const confirmField = () => screen.getByLabelText('Confirm password')

/** Opens the form and fills it; an empty value leaves that field empty. Pasted, so 129 characters stay quick. */
async function openAndFill(user: UserEvent, current: string, next: string, confirm = next) {
  await user.click(screen.getByRole('button', { name: 'Change password' }))
  for (const [field, value] of [[currentField(), current], [newField(), next], [confirmField(), confirm]] as const) {
    if (value === '') continue
    await user.click(field)
    await user.paste(value)
  }
}

const submit = (user: UserEvent) => user.click(screen.getByRole('button', { name: 'Change password' }))

describe('ChangePasswordCard', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('shows the password as dots and Change password until it is opened', () => {
    // Arrange
    const card = <ChangePasswordCard onChanged={() => {}} />

    // Act
    render(<QueryClientProvider client={new QueryClient()}>{card}</QueryClientProvider>)

    // Assert
    expect(screen.getByRole('heading', { name: 'Password' })).toBeInTheDocument()
    expect(screen.getByText('There is no email reset, so keep it somewhere safe.')).toBeInTheDocument()
    expect(screen.getByText('••••••••••')).toBeInTheDocument()
    // The page's first render leaves the focus where it was.
    expect(screen.getByRole('button', { name: 'Change password' })).not.toHaveFocus()
    expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument()
  })

  it('opens a form for the current password, the new one with its hint and the new one again, the current one focused', async () => {
    // Arrange
    const { user } = renderCard()

    // Act
    await user.click(screen.getByRole('button', { name: 'Change password' }))

    // Assert
    expect(currentField()).toHaveFocus()
    expect(currentField()).toHaveAttribute('autocomplete', 'current-password')
    expect(newField()).toHaveAccessibleDescription('8–128 characters.')
    expect(newField()).toHaveAttribute('autocomplete', 'new-password')
    expect(confirmField()).toHaveAttribute('autocomplete', 'new-password')
    expect(screen.getByText('Your other devices are signed out. This one stays signed in.')).toBeInTheDocument()
    // Primary first, Cancel last.
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Change password', 'Cancel'])
    expect(screen.queryByText('••••••••••')).not.toBeInTheDocument()
  })

  it('asks for the current password and sends nothing without it', async () => {
    // Arrange
    const { user } = renderCard()
    await openAndFill(user, '', NEW_PASSWORD)

    // Act
    await submit(user)

    // Assert
    expect(currentField()).toHaveAccessibleDescription('Enter your current password.')
    expect(currentField()).toHaveAttribute('aria-invalid', 'true')
    expect(newField()).toHaveAccessibleDescription('8–128 characters.')
    expect(authApi.changePassword).not.toHaveBeenCalled()
  })

  it.each([
    [7, 'A password is at least 8 characters.'],
    [129, 'A password is at most 128 characters.'],
  ])('puts a new password of %i characters under New password, in place of the hint, and sends nothing', async (length, sentence) => {
    // Arrange
    const { user } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, 'p'.repeat(length))

    // Act
    await submit(user)

    // Assert
    expect(newField()).toHaveAccessibleDescription(sentence)
    expect(screen.queryByText('8–128 characters.')).not.toBeInTheDocument()
    expect(currentField()).not.toHaveAttribute('aria-invalid')
    expect(confirmField()).not.toHaveAttribute('aria-invalid')
    expect(authApi.changePassword).not.toHaveBeenCalled()
  })

  it.each([8, 128])('sends a new password of exactly %i characters', async (length) => {
    // Arrange
    vi.mocked(authApi.changePassword).mockResolvedValue()
    const { user } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, 'p'.repeat(length))

    // Act
    await submit(user)

    // Assert
    expect(authApi.changePassword).toHaveBeenCalledExactlyOnceWith({ currentPassword: CURRENT_PASSWORD, newPassword: 'p'.repeat(length) })
  })

  it('puts different passwords under Confirm password and sends nothing', async () => {
    // Arrange
    const { user } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, NEW_PASSWORD, `${NEW_PASSWORD}!`)

    // Act
    await submit(user)

    // Assert
    expect(confirmField()).toHaveAccessibleDescription('The two passwords are different. Type the same password in both fields.')
    expect(newField()).not.toHaveAttribute('aria-invalid')
    expect(authApi.changePassword).not.toHaveBeenCalled()
  })

  it('sends only the current and the new password, then closes and reports the change', async () => {
    // Arrange
    vi.mocked(authApi.changePassword).mockResolvedValue()
    const { user, onChanged } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, NEW_PASSWORD)

    // Act
    await submit(user)

    // Assert
    expect(authApi.changePassword).toHaveBeenCalledExactlyOnceWith({ currentPassword: CURRENT_PASSWORD, newPassword: NEW_PASSWORD })
    expect(onChanged).toHaveBeenCalledOnce()
    expect(await screen.findByText('••••••••••')).toBeInTheDocument()
    expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change password' })).toHaveFocus()
  })

  it('puts the server\'s sentence about the current password under Current password, and keeps the form', async () => {
    // Arrange
    vi.mocked(authApi.changePassword).mockRejectedValue(apiError(400, 'The current password is incorrect.'))
    const { user, onChanged } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, NEW_PASSWORD)

    // Act
    await submit(user)

    // Assert
    expect(await screen.findByText('The current password is incorrect.')).toBeInTheDocument()
    expect(currentField()).toHaveAccessibleDescription('The current password is incorrect.')
    // The line under the field is the only alert: no banner repeats it.
    expect(screen.getByRole('alert')).toHaveTextContent('The current password is incorrect.')
    expect(newField()).toHaveValue(NEW_PASSWORD)
    expect(onChanged).not.toHaveBeenCalled()
  })

  it.each([
    ['too many attempts', apiError(429, 'Too many attempts. Wait a minute and try again.'), 'Too many attempts. Wait a minute and try again.'],
    ['an unreachable server', networkError(), 'The server could not be reached. Check that it is running, then try again.'],
    ['a failure with no sentence', apiError(500), 'Could not change the password.'],
  ])('shows a banner above the buttons for %s, and keeps what was typed', async (_label, failure, sentence) => {
    // Arrange
    vi.mocked(authApi.changePassword).mockRejectedValue(failure)
    const { user } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, NEW_PASSWORD)

    // Act
    await submit(user)

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    expect(banner.compareDocumentPosition(screen.getByRole('button', { name: 'Change password' })))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING)
    expect(currentField()).not.toHaveAttribute('aria-invalid')
    expect(currentField()).toHaveValue(CURRENT_PASSWORD)
    expect(newField()).toHaveValue(NEW_PASSWORD)
  })

  it('reads Changing… and is disabled while the change is under way', async () => {
    // Arrange
    vi.mocked(authApi.changePassword).mockReturnValue(new Promise<void>(() => {}))
    const { user } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, NEW_PASSWORD)

    // Act
    await submit(user)

    // Assert
    expect(await screen.findByRole('button', { name: 'Changing…' })).toBeDisabled()
    expect(authApi.changePassword).toHaveBeenCalledOnce()
  })

  it('clears a field\'s error once that field is edited, and leaves the others', async () => {
    // Arrange
    const { user } = renderCard()
    await openAndFill(user, '', 'short')
    await submit(user)

    // Act
    await user.type(currentField(), 'x')

    // Assert
    expect(currentField()).not.toHaveAttribute('aria-invalid')
    expect(newField()).toHaveAccessibleDescription('A password is at least 8 characters.')
  })

  it('takes the mismatch off Confirm password once the new password is edited', async () => {
    // Arrange
    const { user } = renderCard()
    await openAndFill(user, CURRENT_PASSWORD, NEW_PASSWORD, 'something else')
    await submit(user)

    // Act
    await user.type(newField(), '!')

    // Assert
    expect(confirmField()).not.toHaveAttribute('aria-invalid')
  })

  it('Cancel closes the form and forgets what was typed', async () => {
    // Arrange
    const { user } = renderCard()
    await openAndFill(user, '', NEW_PASSWORD, 'x')
    await submit(user)

    // Act
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    const closed = screen.queryByLabelText('Current password') === null
    const focusBack = document.activeElement === screen.getByRole('button', { name: 'Change password' })
    await user.click(screen.getByRole('button', { name: 'Change password' }))

    // Assert
    expect(closed).toBe(true)
    expect(focusBack).toBe(true)
    expect(currentField()).toHaveValue('')
    expect(newField()).toHaveValue('')
    expect(confirmField()).toHaveValue('')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(authApi.changePassword).not.toHaveBeenCalled()
  })
})
