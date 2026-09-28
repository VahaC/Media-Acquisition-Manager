import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from '@/App'
import { authApi } from '@/lib/auth-api'
import { healthApi } from '@/lib/health-api'
import { queryClient } from '@/lib/query-client'
import { setupApi } from '@/lib/setup-api'
import { usersApi } from '@/lib/users-api'
import { apiError } from '@/test/api-error'
import { member, owner, resetAuthStore, sessionFor } from '@/test/auth-session'

vi.mock('@/lib/auth-api')
vi.mock('@/lib/health-api')
vi.mock('@/lib/setup-api')
vi.mock('@/lib/users-api')

// App brings its own BrowserRouter and the app-wide query client, so these tests drive it the
// way the browser does: through the address bar, with the shared cache emptied and the session
// forgotten between tests, as on a fresh page load.
function renderAt(path: string) {
  window.history.pushState({}, '', path)
  render(<App />)
  return { user: userEvent.setup() }
}

const sessionEnded = apiError(401, 'Your session has ended. Sign in again.')

describe('App', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    resetAuthStore()
    vi.mocked(healthApi.ready).mockResolvedValue({ status: 'Healthy', checks: [] })
    vi.mocked(setupApi.status).mockResolvedValue({ isRequired: false })
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner))
  })

  afterEach(() => {
    queryClient.clear()
    window.history.pushState({}, '', '/')
  })

  it('shows the app name, the running version and the signed-in user in the header', async () => {
    // Arrange
    const version = `v${__APP_VERSION__}`

    // Act
    renderAt('/')

    // Assert
    expect(screen.getByRole('link', { name: 'Media Acquisition Manager' })).toHaveAttribute('href', '/')
    expect(screen.getByText(version)).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Service status' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Signed in as vasyl' })).toBeInTheDocument()
  })

  it('renders the not-found page for an unknown address, inside the shell', async () => {
    // Arrange
    const unknownPath = '/no-such-page'

    // Act
    renderAt(unknownPath)

    // Assert
    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toHaveAttribute('href', '/')
    expect(screen.getByText(`v${__APP_VERSION__}`)).toBeInTheDocument()
    expect(healthApi.ready).not.toHaveBeenCalled()
  })

  it('sends a visitor without a session to the sign-in page', async () => {
    // Arrange
    vi.mocked(authApi.refresh).mockRejectedValue(sessionEnded)

    // Act
    renderAt('/')

    // Assert
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/login')
    expect(healthApi.ready).not.toHaveBeenCalled()
  })

  it('sends the first visitor to setup while the installation has no owner', async () => {
    // Arrange
    vi.mocked(authApi.refresh).mockRejectedValue(sessionEnded)
    vi.mocked(setupApi.status).mockResolvedValue({ isRequired: true })

    // Act
    renderAt('/')

    // Assert
    expect(await screen.findByRole('heading', { name: 'Create the owner account' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/setup')
  })

  it('brings the user back to the page they opened once they have signed in', async () => {
    // Arrange
    vi.mocked(authApi.refresh).mockRejectedValue(sessionEnded)
    vi.mocked(authApi.signIn).mockResolvedValue(sessionFor(owner))
    vi.mocked(usersApi.list).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    const { user } = renderAt('/settings/users')
    await user.type(await screen.findByLabelText('User name'), 'vasyl')
    await user.type(screen.getByLabelText('Password'), 'correct horse battery')

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    // Assert
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/settings/users')
  })

  it('starts the next person at home after the last one signed out on another page', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    vi.mocked(authApi.signOut).mockResolvedValue()
    vi.mocked(authApi.signIn).mockResolvedValue(sessionFor(member))
    const { user } = renderAt('/settings/users')
    await screen.findByRole('heading', { name: 'Users' })
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))
    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    await user.type(await screen.findByLabelText('User name'), 'olena')
    await user.type(screen.getByLabelText('Password'), 'correct horse battery')

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    // Assert
    expect(await screen.findByRole('heading', { name: 'Service status' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/')
    expect(screen.getByRole('button', { name: 'Signed in as olena' })).toBeInTheDocument()
  })
})
