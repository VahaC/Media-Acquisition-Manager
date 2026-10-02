import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { UserMenu } from '@/components/UserMenu'
import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { healthApi } from '@/lib/health-api'
import { qk } from '@/lib/queries'
import { queryClient as appQueryClient } from '@/lib/query-client'
import { useThemeStore } from '@/lib/theme-store'
import type { CurrentUser, HealthReport } from '@/lib/types'
import { networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'

vi.mock('@/lib/auth-api')
vi.mock('@/lib/health-api')

const healthy: HealthReport = { status: 'Healthy', checks: [] }

/** The line under the user's name in the open menu: the role, the version and the readiness. */
const facts = () => screen.getByText(/^(Owner|Member) ·/)

function renderMenu(
  user: CurrentUser,
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  path = '/',
) {
  signInAs(user)
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <p>Outside the menu</p>
        <UserMenu user={user} />
        {/* The next focus stop after the menu, like Add user on the Users page. */}
        <button type="button">Next on the page</button>
        <Routes>
          <Route path="/" element={<p>Home page</p>} />
          <Route path="/profile" element={<p>Profile page</p>} />
          <Route path="/settings/integrations" element={<p>Integrations page</p>} />
          <Route path="/settings/users" element={<p>Users page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

describe('UserMenu', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(healthApi.ready).mockResolvedValue(healthy)
  })

  afterEach(() => {
    appQueryClient.clear()
    useThemeStore.setState({ mode: 'system' })
    document.documentElement.classList.remove('dark')
    localStorage.clear()
  })

  it('names the signed-in user on its button, beside their initial, and starts closed', () => {
    // Arrange
    const signedIn = owner

    // Act
    renderMenu(signedIn)

    // Assert
    const trigger = screen.getByRole('button', { name: 'Signed in as vasyl' })
    expect(within(trigger).getByText('vasyl')).toBeInTheDocument()
    expect(within(trigger).getByText('V')).toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
  })

  it('says at its top who is signed in: the installation owner, the version running and that the server is ready', async () => {
    // Arrange
    const { user } = renderMenu(owner)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Assert
    expect(await screen.findByText('Ready')).toBeInTheDocument()
    expect(facts()).toHaveTextContent(`Owner · v${__APP_VERSION__} · Ready`)
    expect(facts().previousElementSibling).toHaveTextContent('vasyl')
    expect(healthApi.ready).toHaveBeenCalledOnce()
  })

  it('names any other user a member', async () => {
    // Arrange
    const { user } = renderMenu(member)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as olena' }))

    // Assert
    expect(await screen.findByText('Ready')).toBeInTheDocument()
    expect(facts()).toHaveTextContent(`Member · v${__APP_VERSION__} · Ready`)
  })

  it.each([
    ['Degraded', 'Ready, with warnings'],
    ['Unhealthy', 'Not ready'],
  ] as const)('says a %s server is "%s", in the words of the home page', async (status, words) => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValue({ status, checks: [{ name: 'database', status, description: null }] })
    const { user } = renderMenu(owner)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Assert
    expect(await screen.findByText(words)).toHaveAttribute('data-status', status)
    expect(facts()).toHaveTextContent(`Owner · v${__APP_VERSION__} · ${words}`)
  })

  it('asks the server nothing while it is closed', () => {
    // Arrange
    const signedIn = owner

    // Act
    renderMenu(signedIn)

    // Assert
    expect(screen.getByRole('button', { name: 'Signed in as vasyl' })).toBeInTheDocument()
    expect(healthApi.ready).not.toHaveBeenCalled()
  })

  it('leaves the status out until the server answers', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockReturnValue(new Promise<HealthReport>(() => {}))
    const { user } = renderMenu(owner)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Assert
    expect(healthApi.ready).toHaveBeenCalledOnce()
    expect(facts().textContent).toBe(`Owner · v${__APP_VERSION__}`)
  })

  it('shows the last status at once, and leaves it out once the new check fails', async () => {
    // Arrange
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(qk.health.ready, healthy)
    let failCheck: (error: unknown) => void = () => {}
    vi.mocked(healthApi.ready).mockReturnValue(new Promise<HealthReport>((_, reject) => { failCheck = reject }))
    const { user } = renderMenu(owner, client)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))
    const shownAtOnce = screen.queryByText('Ready') !== null
    failCheck(networkError())
    await vi.waitFor(() => expect(screen.queryByText('Ready')).not.toBeInTheDocument())

    // Assert
    expect(shownAtOnce).toBe(true)
    expect(facts().textContent).toBe(`Owner · v${__APP_VERSION__}`)
    expect(healthApi.ready).toHaveBeenCalledOnce()
  })

  it('switches the theme from its Theme row and stays open', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('radio', { name: 'Dark' }))

    // Assert
    expect(document.documentElement).toHaveClass('dark')
    expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
    expect(screen.getByRole('button', { name: 'Signed in as vasyl' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('offers Users to the installation owner, and opens the page from there', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('link', { name: 'Users' }))

    // Assert
    expect(screen.getByText('Users page')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument()
  })

  it('offers Integrations, Destinations, Media folders and Users to the installation owner, and opens the page from there', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))
    const entries = screen.getAllByRole('link').map((link) => link.textContent)
    const separators = screen.getAllByRole('separator')

    // Act
    await user.click(screen.getByRole('link', { name: 'Integrations' }))

    // Assert
    expect(entries).toEqual([expect.stringMatching(/^vasylOwner/), 'Profile', 'Integrations', 'Destinations', 'Media folders', 'Users'])
    // Under Profile, around the four entries, and above Sign out.
    expect(separators).toHaveLength(3)
    expect(screen.getByText('Integrations page')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Integrations' })).not.toBeInTheDocument()
  })

  it('does not offer Integrations, Destinations, Media folders or Users to anyone else', async () => {
    // Arrange
    const { user } = renderMenu(member)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as olena' }))

    // Assert
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Profile' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Integrations' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Destinations' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Media folders' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument()
    // Above the theme and above Sign out, with no empty group where the entries would be.
    expect(screen.getAllByRole('separator')).toHaveLength(2)
  })

  it.each([
    ['who is signed in', /^olena/],
    ['the Profile entry', 'Profile'],
  ])('opens the Profile page from %s, and closes', async (_label, name) => {
    // Arrange
    const { user } = renderMenu(member)
    await user.click(screen.getByRole('button', { name: 'Signed in as olena' }))

    // Act
    await user.click(screen.getByRole('link', { name }))

    // Assert
    expect(screen.getByText('Profile page')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
  })

  it.each([
    ['/profile', 'page'],
    ['/', null],
  ])('at %s, marks who is signed in as the page on screen with %s, and never the Profile entry', async (path, current) => {
    // Arrange
    const { user } = renderMenu(member, undefined, path)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as olena' }))

    // Assert
    // The tint of the open Profile page hangs on this mark (user-menu.css).
    expect(screen.getByRole('link', { name: /^olena/ }).getAttribute('aria-current')).toBe(current)
    expect(screen.getByRole('link', { name: 'Profile' })).not.toHaveAttribute('aria-current')
  })

  it('signs out through the API and ends the session in this tab', async () => {
    // Arrange
    vi.mocked(authApi.signOut).mockResolvedValue()
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    // Assert
    expect(authApi.signOut).toHaveBeenCalledOnce()
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    expect(useAuthStore.getState()).toMatchObject({ accessToken: null, user: null, deliberateSignOut: true })
  })

  it('ends the session in this tab even when the server cannot be reached', async () => {
    // Arrange
    vi.mocked(authApi.signOut).mockRejectedValue(networkError())
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    // Assert
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    expect(authApi.signOut).toHaveBeenCalledOnce()
  })

  it('closes on Escape and gives the focus back to its button', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    const trigger = screen.getByRole('button', { name: 'Signed in as vasyl' })
    await user.click(trigger)

    // Act
    await user.keyboard('{Escape}')

    // Assert
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(trigger).toHaveFocus()
  })

  it('closes when Tab moves the focus on past its last entry', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))
    // Who is signed in, Profile, Integrations, Destinations, Media folders, Users, the theme (one stop
    // for its three radios), then Sign out.
    await user.tab()
    await user.tab()
    await user.tab()
    await user.tab()
    await user.tab()
    await user.tab()
    await user.tab()
    await user.tab()
    const lastEntry = screen.getByRole('button', { name: 'Sign out' })
    const focusedOnLastEntry = document.activeElement === lastEntry

    // Act
    await user.tab()

    // Assert
    expect(focusedOnLastEntry).toBe(true)
    expect(lastEntry).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Signed in as vasyl' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: 'Next on the page' })).toHaveFocus()
  })

  it('closes on a click anywhere outside it', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByText('Outside the menu'))

    // Assert
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
  })
})
