import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, type InitialEntry } from 'react-router-dom'
import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { queryClient as appQueryClient } from '@/lib/query-client'
import { setupApi } from '@/lib/setup-api'
import { LoginPage } from '@/pages/LoginPage'
import { apiError, networkError } from '@/test/api-error'
import { owner, resetAuthStore, sessionFor } from '@/test/auth-session'

vi.mock('@/lib/auth-api')
vi.mock('@/lib/setup-api')

function renderPage(entry: InitialEntry = '/login') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/setup" element={<p>Setup page</p>} />
          <Route path="/" element={<p>Home page</p>} />
          <Route path="/settings/users" element={<p>Users page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

async function signIn(user: ReturnType<typeof userEvent.setup>, userName: string, password: string) {
  await user.type(await screen.findByLabelText('User name'), userName)
  await user.type(screen.getByLabelText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    // A tab that has already found it has no session, unless a test says otherwise.
    resetAuthStore()
    useAuthStore.setState({ status: 'anonymous' })
    vi.mocked(setupApi.status).mockResolvedValue({ isRequired: false })
  })

  afterEach(() => {
    vi.useRealTimers()
    appQueryClient.clear()
  })

  it('sends the visitor to first-run setup while no owner exists', async () => {
    // Arrange
    vi.mocked(setupApi.status).mockResolvedValue({ isRequired: true })

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Setup page')).toBeInTheDocument()
  })

  it('signs in and goes on to the page the user was going to', async () => {
    // Arrange
    vi.mocked(authApi.signIn).mockResolvedValue(sessionFor(owner))
    const { user } = renderPage({ pathname: '/login', state: { from: '/settings/users' } })

    // Act
    await signIn(user, '  vasyl ', 'correct horse battery')

    // Assert
    expect(await screen.findByText('Users page')).toBeInTheDocument()
    expect(authApi.signIn).toHaveBeenCalledWith({ userName: 'vasyl', password: 'correct horse battery' })
    expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', user: owner })
  })

  it('goes home after signing in when the user was not going anywhere in particular', async () => {
    // Arrange
    vi.mocked(authApi.signIn).mockResolvedValue(sessionFor(owner))
    const { user } = renderPage()

    // Act
    await signIn(user, 'vasyl', 'correct horse battery')

    // Assert
    expect(await screen.findByText('Home page')).toBeInTheDocument()
  })

  it.each([
    [401, 'The user name or password is incorrect.'],
    [429, 'Too many attempts. Wait a minute and try again.'],
  ])('shows the API sentence of a %i under the form and stays signed out', async (status, sentence) => {
    // Arrange
    vi.mocked(authApi.signIn).mockRejectedValue(apiError(status, sentence))
    const { user } = renderPage()

    // Act
    await signIn(user, 'vasyl', 'wrong password')

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled()
    expect(useAuthStore.getState().status).toBe('anonymous')
  })

  it('goes straight on when the tab still has a session to restore', async () => {
    // Arrange
    useAuthStore.setState({ status: 'initializing' })
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Home page')).toBeInTheDocument()
    expect(authApi.signIn).not.toHaveBeenCalled()
  })

  it('shows the form once restoring finds no session', async () => {
    // Arrange
    useAuthStore.setState({ status: 'initializing' })
    vi.mocked(authApi.refresh).mockRejectedValue(apiError(401, 'Your session has ended. Sign in again.'))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByLabelText('User name')).toBeInTheDocument()
    expect(useAuthStore.getState().status).toBe('anonymous')
  })

  it('asks the server again every 3 seconds while it cannot be reached, and goes on once the session is back', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    useAuthStore.setState({ status: 'unavailable' })
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner))
    renderPage({ pathname: '/login', state: { from: '/settings/users' } })
    await screen.findByLabelText('User name')

    // Act
    await act(() => vi.advanceTimersByTimeAsync(3_000))

    // Assert
    expect(await screen.findByText('Users page')).toBeInTheDocument()
    expect(authApi.refresh).toHaveBeenCalledOnce()
    expect(authApi.signIn).not.toHaveBeenCalled()
  })

  it('stops asking once the server answers that there is no session', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    useAuthStore.setState({ status: 'unavailable' })
    vi.mocked(authApi.refresh).mockRejectedValue(apiError(401, 'Your session has ended. Sign in again.'))
    renderPage()
    await act(() => vi.advanceTimersByTimeAsync(3_000))

    // Act
    await act(() => vi.advanceTimersByTimeAsync(9_000))

    // Assert
    expect(authApi.refresh).toHaveBeenCalledOnce()
    expect(useAuthStore.getState().status).toBe('anonymous')
    expect(screen.getByLabelText('User name')).toBeInTheDocument()
  })

  it('also restores the session on Retry when the server could not be reached', async () => {
    // Arrange
    useAuthStore.setState({ status: 'unavailable' })
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner))
    vi.mocked(setupApi.status)
      .mockRejectedValueOnce(networkError())
      .mockResolvedValueOnce({ isRequired: false })
    const { user } = renderPage()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(await screen.findByText('Home page')).toBeInTheDocument()
    expect(authApi.refresh).toHaveBeenCalledOnce()
  })

  it('says so when it cannot check whether setup is needed, and checks again on Retry', async () => {
    // Arrange
    vi.mocked(setupApi.status)
      .mockRejectedValueOnce(apiError(500, 'The server hit an unexpected error.'))
      .mockResolvedValueOnce({ isRequired: false })
    const { user } = renderPage()
    const alert = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(alert).toHaveTextContent('The server hit an unexpected error.')
    expect(await screen.findByLabelText('User name')).toBeInTheDocument()
  })
})
