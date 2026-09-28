import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { useAuthStore } from '@/lib/auth-store'
import { queryClient as appQueryClient } from '@/lib/query-client'
import { qk } from '@/lib/queries'
import { setupApi } from '@/lib/setup-api'
import { SetupPage } from '@/pages/SetupPage'
import { apiError } from '@/test/api-error'
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

  it('refuses two different passwords without asking the server', async () => {
    // Arrange
    const { user } = renderPage()

    // Act
    await fillIn(user, 'correct horse battery', 'correct horse batery')

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('The two passwords are different.')
    expect(setupApi.complete).not.toHaveBeenCalled()
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

  it('shows the API sentence of a 400 under the form', async () => {
    // Arrange
    vi.mocked(setupApi.complete).mockRejectedValue(apiError(400, 'A password must be at least 8 characters long.'))
    const { client, user } = renderPage()

    // Act
    await fillIn(user, 'short', 'short')

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('A password must be at least 8 characters long.')
    expect(useAuthStore.getState().status).toBe('initializing')
    expect(client.getQueryData(qk.setup.status)).toEqual({ isRequired: true })
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
