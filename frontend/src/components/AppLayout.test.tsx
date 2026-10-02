import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/AppLayout'
import { useConnectionStore } from '@/lib/connection-store'
import { healthApi } from '@/lib/health-api'
import type { HealthReport } from '@/lib/types'
import { member, owner, resetAuthStore, signInAs } from '@/test/auth-session'
import { resetConnectionStore } from '@/test/connection'

vi.mock('@/lib/health-api')

function CrashingPage(): never {
  throw new Error('boom')
}

/** The home link: the logo, then the app name over the version. */
const brandName = `Media Acquisition Manager v${__APP_VERSION__}`

function renderAt(path: string) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<p>Home content</p>} />
            <Route path="search" element={<p>Search content</p>} />
            <Route path="library" element={<p>Library content</p>} />
            <Route path="jobs" element={<p>Jobs content</p>} />
            <Route path="crash" element={<CrashingPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AppLayout', () => {
  beforeEach(() => {
    resetAuthStore()
    // React reports every caught render error to the console; the boundary adds its own line.
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
    resetConnectionStore()
  })

  it('shows a crashed page\'s fallback inside the content column, under the header', () => {
    // Arrange
    const path = '/crash'

    // Act
    renderAt(path)

    // Assert
    expect(within(screen.getByRole('main')).getByRole('heading', { name: 'Something went wrong' }))
      .toBeInTheDocument()
    expect(screen.getByRole('banner')).toContainElement(
      screen.getByRole('link', { name: brandName }),
    )
  })

  it('leaves the crashed page through the header link', async () => {
    // Arrange
    const user = userEvent.setup()
    renderAt('/crash')

    // Act
    await user.click(screen.getByRole('link', { name: brandName }))

    // Assert
    expect(screen.getByText('Home content')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Something went wrong' })).not.toBeInTheDocument()
  })

  it('names the app and the version running in its home link, before anyone is signed in too', () => {
    // Arrange
    const path = '/'

    // Act
    renderAt(path)

    // Assert
    const brand = within(screen.getByRole('banner')).getByRole('link', { name: brandName })
    expect(brand).toHaveAttribute('href', '/')
    expect(brand).toHaveAttribute('title', 'The version you are running')
    expect(within(brand).getByText(`v${__APP_VERSION__}`)).toBeInTheDocument()
  })

  it('shows the user menu in the header once someone is signed in', () => {
    // Arrange
    signInAs(owner)

    // Act
    renderAt('/')

    // Assert
    expect(within(screen.getByRole('banner')).getByRole('button', { name: 'Signed in as vasyl' }))
      .toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('links to the Library, then Jobs in the header for %s, and opens the Library from there', async (_, signedIn) => {
    // Arrange
    signInAs(signedIn)
    const user = userEvent.setup()
    renderAt('/')
    const nav = within(screen.getByRole('banner')).getByRole('navigation', { name: 'Main' })

    // Act
    await user.click(within(nav).getByRole('link', { name: 'Library' }))

    // Assert
    expect(within(nav).getAllByRole('link').map((link) => link.textContent)).toEqual(['Library', 'Jobs'])
    expect(within(nav).getByRole('link', { name: 'Library' })).toHaveAttribute('href', '/library')
    expect(screen.getByText('Library content')).toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('opens Search from the search field in the header for %s, which takes the place of a Search link', async (_, signedIn) => {
    // Arrange
    signInAs(signedIn)
    const user = userEvent.setup()
    renderAt('/')
    const banner = screen.getByRole('banner')

    // Act
    await user.type(within(banner).getByRole('searchbox', { name: 'Search for a movie' }), 'Aliens{Enter}')

    // Assert
    expect(screen.getByText('Search content')).toBeInTheDocument()
    expect(within(banner).getByRole('search')).toBeInTheDocument()
    expect(within(banner).queryByRole('link', { name: 'Search' })).not.toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('opens Jobs from its header link for %s', async (_, signedIn) => {
    // Arrange
    signInAs(signedIn)
    const user = userEvent.setup()
    renderAt('/')
    const nav = within(screen.getByRole('banner')).getByRole('navigation', { name: 'Main' })

    // Act
    await user.click(within(nav).getByRole('link', { name: 'Jobs' }))

    // Assert
    expect(within(nav).getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/jobs')
    expect(within(nav).getByRole('link', { name: 'Jobs' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByText('Jobs content')).toBeInTheDocument()
  })

  it('gives every header link its label as a title, which names it once a phone shows the icon alone', () => {
    // Arrange
    signInAs(member)

    // Act
    renderAt('/')

    // Assert
    const nav = within(screen.getByRole('banner')).getByRole('navigation', { name: 'Main' })
    expect(within(nav).getAllByRole('link').map((link) => link.getAttribute('title'))).toEqual(['Library', 'Jobs'])
  })

  it('marks the header link of the page on screen', () => {
    // Arrange
    signInAs(member)

    // Act
    renderAt('/library')

    // Assert
    expect(screen.getByRole('link', { name: 'Library' })).toHaveAttribute('aria-current', 'page')
  })

  it('has no search field, user menu or links before anyone is signed in', () => {
    // Arrange
    const path = '/'

    // Act
    renderAt(path)

    // Assert
    expect(within(screen.getByRole('banner')).queryByRole('search')).not.toBeInTheDocument()
    expect(within(screen.getByRole('banner')).queryByRole('button')).not.toBeInTheDocument()
    expect(within(screen.getByRole('banner')).queryByRole('navigation')).not.toBeInTheDocument()
    expect(screen.getByText('Home content')).toBeInTheDocument()
  })

  it('shows the offline strip right under the header, outside the page, while the server cannot be reached', () => {
    // Arrange — the strip's probe stays unanswered.
    vi.mocked(healthApi.live).mockReturnValue(new Promise<HealthReport>(() => {}))
    signInAs(member)
    useConnectionStore.setState({ unreachable: true })

    // Act
    renderAt('/')

    // Assert
    const strip = screen.getByRole('alert')
    expect(strip).toHaveTextContent('The server could not be reached.')
    expect(screen.getByRole('banner').nextElementSibling).toBe(strip)
    expect(screen.getByRole('main')).not.toContainElement(strip)
    expect(screen.getByText('Home content')).toBeInTheDocument()
  })
})
