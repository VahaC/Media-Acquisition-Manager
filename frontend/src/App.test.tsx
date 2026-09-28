import { render, screen } from '@testing-library/react'
import App from '@/App'
import { healthApi } from '@/lib/health-api'
import { queryClient } from '@/lib/query-client'

vi.mock('@/lib/health-api')

// App brings its own BrowserRouter and the app-wide query client, so these tests drive it the
// way the browser does: through the address bar, with the shared cache emptied between tests.
function renderAt(path: string) {
  window.history.pushState({}, '', path)
  render(<App />)
}

describe('App', () => {
  beforeEach(() => {
    vi.mocked(healthApi.ready).mockResolvedValue({ status: 'Healthy', checks: [] })
  })

  afterEach(() => {
    queryClient.clear()
    window.history.pushState({}, '', '/')
  })

  it('shows the app name and the running version in the header', async () => {
    // Arrange
    const version = `v${__APP_VERSION__}`

    // Act
    renderAt('/')

    // Assert
    expect(screen.getByRole('link', { name: 'Media Acquisition Manager' })).toHaveAttribute('href', '/')
    expect(screen.getByText(version)).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Service status' })).toBeInTheDocument()
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
})
