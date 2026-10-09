import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from '@/App'
import { allowedRootsApi } from '@/lib/allowed-roots-api'
import { authApi } from '@/lib/auth-api'
import { destinationsApi } from '@/lib/destinations-api'
import { downloadsApi } from '@/lib/downloads-api'
import { duplicatesApi } from '@/lib/duplicates-api'
import { healthApi } from '@/lib/health-api'
import { libraryApi } from '@/lib/library-api'
import { libraryLinksApi } from '@/lib/library-links-api'
import { profileApi } from '@/lib/profile-api'
import { queryClient } from '@/lib/query-client'
import { recommendationsApi } from '@/lib/recommendations-api'
import { searchApi } from '@/lib/search-api'
import { setupApi } from '@/lib/setup-api'
import { usersApi } from '@/lib/users-api'
import { allowedRootsOf } from '@/test/allowed-roots'
import { apiError } from '@/test/api-error'
import { member, owner, resetAuthStore, sessionFor } from '@/test/auth-session'
import { destinationsPage, movies } from '@/test/destinations'
import { downloadingJob, filesPage, jobsPage } from '@/test/downloads'
import { duplicatesPage, possibleGroupOf } from '@/test/duplicates'
import { sourcesOf } from '@/test/library'
import { catchUpOf, linksPage, queueOf, suggestionRunOf } from '@/test/library-links'
import { allTabsOn, guardians, listOf, runOf } from '@/test/recommendations'
import { aliensSearch, releasesPage, searchId, tolokaRemux } from '@/test/search'

vi.mock('@/lib/allowed-roots-api')
vi.mock('@/lib/auth-api')
vi.mock('@/lib/destinations-api')
vi.mock('@/lib/downloads-api')
vi.mock('@/lib/duplicates-api')
vi.mock('@/lib/health-api')
vi.mock('@/lib/library-api')
vi.mock('@/lib/library-links-api')
vi.mock('@/lib/profile-api')
vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/search-api')
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
    // MAM-33 — the header reads the user's recommendation tabs once someone is signed in.
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
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
    expect(screen.getByRole('link', { name: `Media Acquisition Manager ${version}` })).toHaveAttribute('href', '/')
    expect(screen.getByText(version)).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Service status' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Signed in as vasyl' })).toBeInTheDocument()
  })

  it('opens the Library from the header link', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    vi.mocked(libraryApi.locations).mockResolvedValue({ items: [] })
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf())
    const { user } = renderAt('/')

    // Act
    await user.click(await screen.findByRole('link', { name: 'Library' }))

    // Assert
    // The page's code loads on its first visit, which on a busy machine takes past the 1 s a find
    // waits by default.
    expect(await screen.findByRole('heading', { level: 1, name: 'Library' }, { timeout: 5_000 })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/library')
    // The heading renders before the works query starts, so its first call is awaited, not assumed.
    await waitFor(() => expect(libraryApi.works).toHaveBeenCalledOnce())
  })

  it('opens the TMDb page at its address, with only its own link marked in the header', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockResolvedValue(queueOf(0, 0, null))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, []))

    // Act
    renderAt('/library/tmdb')

    // Assert
    // The page's code loads on its first visit: in a cold, fully parallel run its transform alone
    // can take past the 1 s a find waits by default.
    expect(await screen.findByRole('heading', { level: 1, name: 'TMDb links' }, { timeout: 5_000 })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Main' })
    expect(within(nav).getByRole('link', { name: 'TMDb' })).toHaveAttribute('aria-current', 'page')
    expect(within(nav).getByRole('link', { name: 'Library' })).not.toHaveAttribute('aria-current')
    expect(await screen.findByText(/No work is left to review/)).toBeInTheDocument()
  })

  it('opens the TMDb page from the header link', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockResolvedValue(queueOf(0, 0, null))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, []))
    const { user } = renderAt('/')

    // Act
    await user.click(await screen.findByRole('link', { name: 'TMDb' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'TMDb links' }, { timeout: 5_000 })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/library/tmdb')
  })

  it('opens the Duplicates page at its address and tab, with only its own link marked in the header', async () => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, [possibleGroupOf()]))

    // Act
    renderAt('/library/duplicates?tab=Possible')

    // Assert
    // The page's code loads on its first visit, which on a busy machine takes past the 1 s a find
    // waits by default.
    expect(await screen.findByRole('heading', { level: 1, name: 'Duplicates' }, { timeout: 5_000 })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Main' })
    expect(within(nav).getByRole('link', { name: 'Duplicates' })).toHaveAttribute('aria-current', 'page')
    expect(within(nav).getByRole('link', { name: 'Library' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('tab', { name: 'Possible' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(duplicatesApi.list).toHaveBeenCalledExactlyOnceWith({ class: 'Possible', page: 1, pageSize: 25 })
  })

  it('opens the Duplicates page from the Library\'s Duplicates button', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    vi.mocked(libraryApi.locations).mockResolvedValue({ items: [] })
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf())
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, []))
    const { user } = renderAt('/library')
    const main = await screen.findByRole('main')
    await within(main).findByRole('heading', { level: 1, name: 'Library' }, { timeout: 5_000 })

    // Act
    await user.click(within(main).getByRole('link', { name: 'Duplicates' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'Duplicates' }, { timeout: 5_000 })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/library/duplicates')
    await waitFor(() => expect(duplicatesApi.list).toHaveBeenCalledExactlyOnceWith({ page: 1, pageSize: 25 }))
  })

  it('opens Recommendations from the header link, at its first tab', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians]))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(runOf())
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
    const { user } = renderAt('/')

    // Act
    await user.click(await screen.findByRole('link', { name: 'Recommendations' }))

    // Assert
    // The page's code loads on its first visit: in a cold, fully parallel run its transform alone
    // can take past the 1 s a find waits by default.
    expect(await screen.findByRole('heading', { level: 1, name: 'Recommendations' }, { timeout: 5_000 })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: guardians.title })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/recommendations')
    expect(screen.getByRole('tab', { name: 'Movies' })).toHaveAttribute('aria-selected', 'true')
  })

  it('opens Jobs from the header link, and a job\'s details from its title', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, [downloadingJob]))
    vi.mocked(downloadsApi.get).mockResolvedValue(downloadingJob)
    vi.mocked(downloadsApi.files).mockImplementation(async (_, query) => filesPage(query, []))
    const { user } = renderAt('/')

    // Act
    await user.click(await screen.findByRole('link', { name: 'Jobs' }))
    await user.click(await screen.findByRole('link', { name: downloadingJob.releaseTitle }))

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
    expect(window.location.pathname).toBe(`/jobs/${downloadingJob.id}`)
    expect(downloadsApi.list).toHaveBeenCalledExactlyOnceWith({ status: undefined, page: 1, pageSize: 25 })
    expect(downloadsApi.get).toHaveBeenCalledExactlyOnceWith(downloadingJob.id)
  })

  it('searches from the header field: Enter makes the search, the address takes its id, and Back returns to the page before', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockResolvedValue(aliensSearch)
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, [tolokaRemux]))
    const { user } = renderAt('/')
    await screen.findByRole('heading', { name: 'Service status' })

    // Act
    await user.type(screen.getByRole('searchbox', { name: 'Search for a movie or a series' }), 'Aliens (1986){Enter}')

    // Assert
    // The page's code loads first, on its first visit, while the home page stays on screen.
    expect(await screen.findByRole('heading', { level: 1, name: 'Search' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(window.location.pathname + window.location.search).toBe(`/search?id=${searchId}`)
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: '1986' })
    expect(screen.getByRole('searchbox', { name: 'Search for a movie or a series' })).toHaveValue('Aliens')
    expect(searchApi.get).not.toHaveBeenCalled()
    window.history.back()
    expect(await screen.findByRole('heading', { name: 'Service status' })).toBeInTheDocument()
    expect(window.location.pathname + window.location.search).toBe('/')
  })

  it('says how to search at a bare /search, asking nothing until a search is made', async () => {
    // Arrange
    const path = '/search'

    // Act
    renderAt(path)

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'Search' })).toBeInTheDocument()
    expect(screen.getByText('Type a title in the search field at the top, then press Enter.')).toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search for a movie or a series' })).toHaveValue('')
    expect(searchApi.create).not.toHaveBeenCalled()
    expect(searchApi.get).not.toHaveBeenCalled()
    expect(searchApi.releases).not.toHaveBeenCalled()
  })

  it('brings a stored search back from its address, as F5 does', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(aliensSearch)
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, [tolokaRemux]))

    // Act
    renderAt(`/search?id=${searchId}`)

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: 'Чужі' })).toBeInTheDocument()
    expect(screen.getByText((_, element) => element?.textContent === 'Results for “Aliens”.')).toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search for a movie or a series' })).toHaveValue('Aliens')
    expect(searchApi.get).toHaveBeenCalledExactlyOnceWith(searchId)
    expect(searchApi.create).not.toHaveBeenCalled()
  })

  it('opens Media folders from the user menu of the owner', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.get).mockResolvedValue(allowedRootsOf(['/media']))
    const { user } = renderAt('/')
    await user.click(await screen.findByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('link', { name: 'Media folders' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'Media folders' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/settings/media-folders')
    expect(await screen.findByRole('textbox', { name: 'Folder 1' })).toHaveValue('/media')
  })

  it('opens Destinations from the user menu of the owner', async () => {
    // Arrange
    vi.mocked(destinationsApi.list).mockImplementation(async (query) => destinationsPage(query, [movies]))
    const { user } = renderAt('/')
    await user.click(await screen.findByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('link', { name: 'Destinations' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'Destinations' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/settings/destinations')
    expect(await screen.findByText('In the media library Фільми')).toBeInTheDocument()
    expect(destinationsApi.list).toHaveBeenCalledExactlyOnceWith({ search: undefined, page: 1, pageSize: 25 })
  })

  it('opens Profile from the user menu of a member', async () => {
    // Arrange
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(member))
    vi.mocked(authApi.me).mockResolvedValue({
      ...member,
      createdUtc: '2026-09-29T07:30:00Z',
      lastSignInUtc: null,
      sessionExpiresUtc: '2026-11-01T07:30:00Z',
    })
    const { user } = renderAt('/')
    await user.click(await screen.findByRole('button', { name: 'Signed in as olena' }))

    // Act
    await user.click(screen.getByRole('link', { name: 'Profile' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'olena' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/profile')
    expect(await screen.findByText('Never')).toBeInTheDocument()
    expect(authApi.me).toHaveBeenCalledOnce()
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
