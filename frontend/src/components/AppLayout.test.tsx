import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/AppLayout'
import { member, owner, resetAuthStore, signInAs } from '@/test/auth-session'

function CrashingPage(): never {
  throw new Error('boom')
}

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
      screen.getByRole('link', { name: 'Media Acquisition Manager' }),
    )
  })

  it('leaves the crashed page through the header link', async () => {
    // Arrange
    const user = userEvent.setup()
    renderAt('/crash')

    // Act
    await user.click(screen.getByRole('link', { name: 'Media Acquisition Manager' }))

    // Assert
    expect(screen.getByText('Home content')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Something went wrong' })).not.toBeInTheDocument()
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
  ])('links to Search, the Library, then Jobs in the header for %s, and opens the Library from there', async (_, signedIn) => {
    // Arrange
    signInAs(signedIn)
    const user = userEvent.setup()
    renderAt('/')
    const nav = within(screen.getByRole('banner')).getByRole('navigation', { name: 'Main' })

    // Act
    await user.click(within(nav).getByRole('link', { name: 'Library' }))

    // Assert
    expect(within(nav).getAllByRole('link').map((link) => link.textContent)).toEqual(['Search', 'Library', 'Jobs'])
    expect(within(nav).getByRole('link', { name: 'Library' })).toHaveAttribute('href', '/library')
    expect(screen.getByText('Library content')).toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('opens Search from its header link for %s', async (_, signedIn) => {
    // Arrange
    signInAs(signedIn)
    const user = userEvent.setup()
    renderAt('/')
    const nav = within(screen.getByRole('banner')).getByRole('navigation', { name: 'Main' })

    // Act
    await user.click(within(nav).getByRole('link', { name: 'Search' }))

    // Assert
    expect(within(nav).getByRole('link', { name: 'Search' })).toHaveAttribute('href', '/search')
    expect(within(nav).getByRole('link', { name: 'Search' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByText('Search content')).toBeInTheDocument()
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

  it('marks the header link of the page on screen', () => {
    // Arrange
    signInAs(member)

    // Act
    renderAt('/library')

    // Assert
    expect(screen.getByRole('link', { name: 'Library' })).toHaveAttribute('aria-current', 'page')
  })

  it('has no user menu or links before anyone is signed in', () => {
    // Arrange
    const path = '/'

    // Act
    renderAt(path)

    // Assert
    expect(within(screen.getByRole('banner')).queryByRole('button')).not.toBeInTheDocument()
    expect(within(screen.getByRole('banner')).queryByRole('navigation')).not.toBeInTheDocument()
    expect(screen.getByText('Home content')).toBeInTheDocument()
  })
})
