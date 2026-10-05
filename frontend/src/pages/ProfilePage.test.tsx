import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime } from '@/lib/format'
import { profileApi } from '@/lib/profile-api'
import { qk } from '@/lib/queries'
import { queryClient as appQueryClient } from '@/lib/query-client'
import { useThemeStore } from '@/lib/theme-store'
import type { CurrentUser, CurrentUserProfile } from '@/lib/types'
import { ProfilePage } from '@/pages/ProfilePage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { allTabsOn } from '@/test/recommendations'

vi.mock('@/lib/auth-api')
vi.mock('@/lib/profile-api')

/** The format the page shows its instants in. */
const format: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

const ownerProfile: CurrentUserProfile = {
  ...owner,
  createdUtc: '2026-09-28T09:15:00Z',
  lastSignInUtc: '2026-10-01T18:42:00Z',
  sessionExpiresUtc: '2026-10-31T18:42:00Z',
}

const memberProfile: CurrentUserProfile = {
  ...member,
  createdUtc: '2026-09-29T07:30:00Z',
  lastSignInUtc: null,
  sessionExpiresUtc: '2026-11-01T07:30:00Z',
}

function renderPage(user: CurrentUser, client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  signInAs(user)
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

/** The value beside `label` in the Account card. */
const rowValue = (label: string) => screen.getByText(label, { selector: 'dt' }).nextElementSibling

const sessionSentence = (profile: CurrentUserProfile) =>
  `A session lasts 30 days from its last use. This one ends ${formatDateTime(profile.sessionExpiresUtc, format)} unless you use the app again.`

describe('ProfilePage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
  })

  afterEach(() => {
    appQueryClient.clear()
    useThemeStore.setState({ mode: 'system' })
    document.documentElement.classList.remove('dark')
    localStorage.clear()
  })

  it('shows the owner\'s name with their initial, the Owner badge and the owner\'s sentence', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)

    // Act
    renderPage(owner)

    // Assert
    const heading = screen.getByRole('heading', { level: 1 })
    expect(heading).toHaveTextContent('vasyl')
    expect(within(heading).getByText('Owner')).toHaveClass('ui-badge')
    expect(screen.getByText('V')).toBeInTheDocument()
    expect(screen.getByText('The installation owner: manages users, integrations and settings.')).toBeInTheDocument()
    expect(await screen.findByText(formatDateTime(ownerProfile.createdUtc, format))).toBeInTheDocument()
  })

  it('shows a member\'s sentence and no badge', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(memberProfile)

    // Act
    renderPage(member)

    // Assert
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(/^olena$/)
    expect(screen.getByText('Can search, browse the library and send downloads.')).toBeInTheDocument()
    expect(await screen.findByText('Member')).toBeInTheDocument()
  })

  it('shows the user name, the role, and when the account was created and last signed in, in the UI locale', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText('User name', { selector: 'dt' })
    expect(rowValue('User name')).toHaveTextContent('vasyl')
    expect(rowValue('User name')).toHaveClass('profile-mono')
    expect(rowValue('Role')).toHaveTextContent('Owner')
    expect(rowValue('Created')).toHaveTextContent(formatDateTime(ownerProfile.createdUtc, format))
    expect(rowValue('Last sign-in')).toHaveTextContent(formatDateTime(ownerProfile.lastSignInUtc!, format))
    expect(authApi.me).toHaveBeenCalledOnce()
  })

  it('shows Never when the account has no sign-in yet', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(memberProfile)

    // Act
    renderPage(member)

    // Assert
    await screen.findByText('Last sign-in', { selector: 'dt' })
    expect(rowValue('Last sign-in')).toHaveTextContent('Never')
    expect(rowValue('Role')).toHaveTextContent('Member')
  })

  it('says when this session ends', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)

    // Act
    renderPage(owner)

    // Assert
    expect(await screen.findByText(sessionSentence(ownerProfile))).toBeInTheDocument()
  })

  it('shows the head and every card at once, and leaves the dates out until the profile arrives', () => {
    // Arrange
    vi.mocked(authApi.me).mockReturnValue(new Promise<CurrentUserProfile>(() => {}))

    // Act
    renderPage(owner)

    // Assert
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('vasyl')
    expect(within(screen.getByRole('region', { name: 'Account' })).getByText('Loading…')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change password' })).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Theme' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Recommendations' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.queryByText(/A session lasts 30 days/)).not.toBeInTheDocument()
  })

  it('shows an error state with the server\'s sentence when the profile cannot load, and Try again asks again', async () => {
    // Arrange
    vi.mocked(authApi.me)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockResolvedValueOnce(ownerProfile)
    const { user } = renderPage(owner)
    const errorState = await screen.findByRole('alert')

    // Act
    await user.click(within(errorState).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(errorState).toHaveTextContent('Could not load your profile.')
    expect(errorState).toHaveTextContent('The database is unavailable.')
    expect(await screen.findByText(sessionSentence(ownerProfile))).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(authApi.me).toHaveBeenCalledTimes(2)
  })

  it('keeps the rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(qk.auth.me, ownerProfile)
    vi.mocked(authApi.me).mockRejectedValue(apiError(500, 'The database is unavailable.'))

    // Act
    renderPage(owner, client)

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing your profile as last loaded. The database is unavailable.')
    expect(within(banner).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(rowValue('Created')).toHaveTextContent(formatDateTime(ownerProfile.createdUtc, format))
    expect(screen.getByText(sessionSentence(ownerProfile))).toBeInTheDocument()
  })

  it('signs out from the Session card', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)
    vi.mocked(authApi.signOut).mockResolvedValue()
    const { user } = renderPage(owner)

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    // Assert
    expect(authApi.signOut).toHaveBeenCalledOnce()
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    expect(useAuthStore.getState()).toMatchObject({ accessToken: null, user: null, deliberateSignOut: true })
  })

  it('switches the theme from the Appearance card, with the options named', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)
    const { user } = renderPage(owner)

    // Act
    await user.click(screen.getByRole('radio', { name: 'Dark' }))

    // Assert
    expect(screen.getByText('Saved in this browser.')).toBeInTheDocument()
    // Named beside their icons, not only for a screen reader as in the user menu.
    expect(screen.getByText('Dark')).not.toHaveClass('sr-only')
    expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
    expect(document.documentElement).toHaveClass('dark')
  })

  it('says at the top that the password changed, until the banner is dismissed', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)
    vi.mocked(authApi.changePassword).mockResolvedValue()
    const { user } = renderPage(owner)
    await user.click(screen.getByRole('button', { name: 'Change password' }))
    await user.type(screen.getByLabelText('Current password'), 'old horse battery')
    await user.type(screen.getByLabelText('New password'), 'correct horse battery')
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse battery')
    await user.click(screen.getByRole('button', { name: 'Change password' }))
    const banner = await screen.findByRole('status')
    const said = banner.textContent
    const aboveTheCards = banner.compareDocumentPosition(screen.getByRole('heading', { name: 'Account' }))

    // Act
    await user.click(within(banner).getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(said).toBe('Password changed. Your other devices are signed out; this one stays signed in.')
    expect(aboveTheCards).toBe(Node.DOCUMENT_POSITION_FOLLOWING)
    expect(banner).not.toBeInTheDocument()
    expect(screen.getByText('••••••••••')).toBeInTheDocument()
  })

  it('shows the Recommendations card after Appearance and before Session, with its own Save', async () => {
    // Arrange
    vi.mocked(authApi.me).mockResolvedValue(ownerProfile)

    // Act
    renderPage(owner)

    // Assert
    expect(await screen.findByRole('textbox', { name: 'Daily search limit' })).toHaveValue('60')
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent))
      .toEqual(['Account', 'Password', 'Appearance', 'Recommendations', 'Session'])
    expect(within(screen.getByRole('region', { name: 'Recommendations' })).getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })
})
