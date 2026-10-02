import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { useAuthStore } from '@/lib/auth-store'
import { queryClient as appQueryClient } from '@/lib/query-client'
import { qk } from '@/lib/queries'
import { setupApi } from '@/lib/setup-api'
import { SetupPage } from '@/pages/SetupPage'
import { apiError, networkError } from '@/test/api-error'
import { owner, resetAuthStore, sessionFor } from '@/test/auth-session'

vi.mock('@/lib/setup-api')

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/setup']}>
        <Routes>
          <Route path="/setup" element={<SetupPage />} />
          <Route path="/login" element={<p>Sign-in page</p>} />
          <Route path="/" element={<p>Home page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

async function fillIn(user: ReturnType<typeof userEvent.setup>, password: string, confirmPassword: string) {
  await user.type(await screen.findByLabelText('User name'), ' vasyl ')
  await user.type(screen.getByLabelText('Password'), password)
  await user.type(screen.getByLabelText('Confirm password'), confirmPassword)
  await user.click(screen.getByRole('button', { name: 'Create the owner account' }))
}

describe('SetupPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    resetAuthStore()
    vi.mocked(setupApi.status).mockResolvedValue({ isRequired: true })
  })

  afterEach(() => {
    appQueryClient.clear()
  })

  it('sends the visitor to sign-in once the installation has its owner', async () => {
    // Arrange
    vi.mocked(setupApi.status).mockResolvedValue({ isRequired: false })

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Sign-in page')).toBeInTheDocument()
  })

  it('describes Password with its hint, which stays out of the field name', async () => {
    // Act
    renderPage()

    // Assert
    const password = await screen.findByLabelText('Password')
    expect(password).toHaveAccessibleName('Password')
    expect(password).toHaveAccessibleDescription(
      'At least 8 characters. There is no email reset, so keep it somewhere safe.',
    )
    expect(screen.getByLabelText('User name')).not.toHaveAttribute('aria-describedby')
  })

  it('refuses two different passwords under Confirm password, without asking the server', async () => {
    // Arrange
    const { user } = renderPage()

    // Act
    await fillIn(user, 'correct horse battery', 'correct horse batery')

    // Assert
    const confirm = screen.getByLabelText('Confirm password')
    expect(screen.getByRole('alert')).toHaveTextContent('The two passwords are different.')
    expect(confirm).toHaveAttribute('aria-invalid', 'true')
    expect(confirm).toHaveAccessibleDescription('The two passwords are different. Type the same password in both fields.')
    expect(confirm).toHaveAccessibleName('Confirm password')
    expect(screen.getByLabelText('Password')).not.toHaveAttribute('aria-invalid')
    expect(setupApi.complete).not.toHaveBeenCalled()
  })

  it.each(['Password', 'Confirm password'])('lets go of the mismatch once %s is edited', async (label) => {
    // Arrange
    const { user } = renderPage()
    await fillIn(user, 'correct horse battery', 'correct horse batery')

    // Act
    await user.type(screen.getByLabelText(label), 'y')

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Confirm password')).not.toHaveAttribute('aria-invalid')
  })

  it('creates the owner, signs them in and opens the home page', async () => {
    // Arrange
    vi.mocked(setupApi.complete).mockResolvedValue(sessionFor(owner))
    const { client, user } = renderPage()

    // Act
    await fillIn(user, 'correct horse battery', 'correct horse battery')

    // Assert
    expect(await screen.findByText('Home page')).toBeInTheDocument()
    expect(setupApi.complete).toHaveBeenCalledWith({ userName: 'vasyl', password: 'correct horse battery' })
    expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', user: owner })
    expect(client.getQueryData(qk.setup.status)).toEqual({ isRequired: false })
  })

  it('shows the API sentence of a 400 in a banner above the button, and keeps what was typed', async () => {
    // Arrange
    vi.mocked(setupApi.complete).mockRejectedValue(apiError(400, 'A password must be at least 8 characters long.'))
    const { client, user } = renderPage()

    // Act
    await fillIn(user, 'short', 'short')

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('A password must be at least 8 characters long.')
    expect(banner).toHaveClass('ui-banner')
    expect(banner.nextElementSibling).toBe(screen.getByRole('button', { name: 'Create the owner account' }))
    expect(screen.getByLabelText('Password')).toHaveValue('short')
    expect(useAuthStore.getState().status).toBe('initializing')
    expect(client.getQueryData(qk.setup.status)).toEqual({ isRequired: true })
  })

  it('says so when it cannot check whether setup is needed, and checks again on Try again', async () => {
    // Arrange
    vi.mocked(setupApi.status)
      .mockRejectedValueOnce(networkError())
      .mockResolvedValueOnce({ isRequired: true })
    const { user } = renderPage()
    const alert = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(alert).toHaveTextContent('Could not check whether this installation is set up.')
    expect(alert).toHaveTextContent('The server could not be reached.')
    expect(await screen.findByLabelText('User name')).toBeInTheDocument()
    expect(setupApi.status).toHaveBeenCalledTimes(2)
  })

  it('moves on to sign-in when another setup has created the owner meanwhile (409)', async () => {
    // Arrange
    vi.mocked(setupApi.complete).mockRejectedValue(apiError(409, 'This installation is already set up. Sign in instead.'))
    const { client, user } = renderPage()

    // Act
    await fillIn(user, 'correct horse battery', 'correct horse battery')

    // Assert
    expect(await screen.findByText('Sign-in page')).toBeInTheDocument()
    expect(client.getQueryData(qk.setup.status)).toEqual({ isRequired: false })
    expect(setupApi.status).toHaveBeenCalledOnce()
    expect(useAuthStore.getState().status).toBe('initializing')
  })
})
