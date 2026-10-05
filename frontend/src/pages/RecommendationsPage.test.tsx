import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { profileApi } from '@/lib/profile-api'
import { qk } from '@/lib/queries'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { CurrentUser, RecommendationList, RecommendationSettings } from '@/lib/types'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { allTabsOff, allTabsOn, covenant, firstQueryOf, guardians, listOf, martian, runOf, yellowstone } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/profile-api')

// MAM-33 — the Recommendations page's tabs, the address that holds the current one, and the page's
// states: the settings that decide the tabs, then the list of the tab on screen.

/** The search part of the address the page is at. */
function LocationProbe() {
  return <output data-testid="location">{useLocation().search}</output>
}

const addressSearch = () => screen.getByTestId('location').textContent

/** The query of the last request for a list. */
const lastQuery = () => vi.mocked(recommendationsApi.list).mock.lastCall?.[0]

/** The titles of the cards on screen, in order. */
const cardTitles = () =>
  screen.queryAllByRole('article').map((card) => within(card).getByRole('heading', { level: 2 }).textContent)

/** The tabs on screen, as their names. */
const tabNames = () => screen.getAllByRole('tab').map((tab) => tab.textContent)

function renderPage({ signedIn = owner, path = '/recommendations', client = newClient() }: {
  signedIn?: CurrentUser
  path?: string
  client?: QueryClient
} = {}) {
  signInAs(signedIn)
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/recommendations" element={<><RecommendationsPage /><LocationProbe /></>} />
          <Route path="/profile" element={<p>The Profile page</p>} />
          <Route path="/settings/integrations" element={<p>The Integrations page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const newClient = () => new QueryClient({ defaultOptions: { queries: { retry: false } } })

describe('RecommendationsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians, martian, covenant]))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(runOf())
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
  })

  it('shows the user\'s tabs in order, opens the first and lists its first page as cards', async () => {
    // Arrange
    const signedIn = member

    // Act
    renderPage({ signedIn })

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'Recommendations' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: guardians.title })).toBeInTheDocument()
    expect(within(screen.getByRole('tablist', { name: 'Lists' })).getAllByRole('tab').map((tab) => tab.textContent))
      .toEqual(['Movies', 'Series', 'Cartoons', 'Documentaries'])
    expect(screen.getByRole('tab', { name: 'Movies' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tabpanel', { name: 'Movies' })).toContainElement(screen.getByRole('heading', { level: 2, name: guardians.title }))
    expect(cardTitles()).toEqual([guardians.title, martian.title, covenant.title])
    expect(vi.mocked(recommendationsApi.list).mock.calls).toEqual([[firstQueryOf('Movies')]])
    expect(screen.getByText('1–3 of 3')).toBeInTheDocument()
  })

  it('opens the tab named in the address', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [yellowstone]))

    // Act
    renderPage({ path: '/recommendations?tab=Series' })

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: yellowstone.title })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Series' })).toHaveAttribute('aria-selected', 'true')
    expect(vi.mocked(recommendationsApi.list).mock.calls).toEqual([[firstQueryOf('Series')]])
  })

  it.each<[string, string, RecommendationSettings]>([
    ['a tab that is none', '?tab=Anime', allTabsOn],
    ['a tab in another letter case', '?tab=series', allTabsOn],
    ['a tab the user has off', '?tab=Movies', { ...allTabsOn, movies: false }],
  ])('opens the first tab the user has on for %s in the address', async (_, search, settings) => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(settings)
    const first = settings.movies ? 'Movies' : 'Series'

    // Act
    renderPage({ path: `/recommendations${search}` })

    // Assert
    expect(await screen.findByRole('tab', { name: first })).toHaveAttribute('aria-selected', 'true')
    await vi.waitFor(() => expect(lastQuery()).toEqual(firstQueryOf(first)))
    expect(recommendationsApi.list).toHaveBeenCalledOnce()
  })

  it('offers only the tabs the user has on, in the page\'s order', async () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue({ ...allTabsOn, movies: false, cartoons: false })

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('tab', { name: 'Series' })).toHaveAttribute('aria-selected', 'true')
    expect(tabNames()).toEqual(['Series', 'Documentaries'])
  })

  it('puts the tab picked in the address and asks for its first page', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })

    // Act
    await user.click(screen.getByRole('tab', { name: 'Documentaries' }))

    // Assert
    expect(screen.getByRole('tab', { name: 'Documentaries' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Movies' })).toHaveAttribute('aria-selected', 'false')
    expect(addressSearch()).toBe('?tab=Documentaries')
    await vi.waitFor(() => expect(lastQuery()).toEqual(firstQueryOf('Documentaries')))
  })

  it('moves between the tabs with the arrow keys, Home and End, with one tab stop', async () => {
    // Arrange
    const { user } = renderPage()
    const movies = await screen.findByRole('tab', { name: 'Movies' })
    movies.focus()
    const stops = screen.getAllByRole('tab').map((tab) => tab.getAttribute('tabindex'))

    // Act
    await user.keyboard('{ArrowRight}')
    const afterRight = document.activeElement?.textContent
    await user.keyboard('{End}')
    const afterEnd = document.activeElement?.textContent
    await user.keyboard('{ArrowRight}')
    const afterWrap = document.activeElement?.textContent
    await user.keyboard('{ArrowLeft}')
    const afterLeft = document.activeElement?.textContent
    await user.keyboard('{Home}')

    // Assert
    expect(stops).toEqual(['0', '-1', '-1', '-1'])
    expect([afterRight, afterEnd, afterWrap, afterLeft]).toEqual(['Series', 'Documentaries', 'Movies', 'Documentaries'])
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Movies' }))
    expect(screen.getByRole('tab', { name: 'Movies' })).toHaveAttribute('aria-selected', 'true')
    expect(addressSearch()).toBe('?tab=Movies')
  })

  it('says every list is off and links to the Profile when the user has every tab off, and asks for no list', async () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOff)
    const { user } = renderPage()

    // Act
    await user.click(await screen.findByRole('link', { name: 'Open Profile' }))

    // Assert
    expect(screen.getByText('The Profile page')).toBeInTheDocument()
    expect(recommendationsApi.list).not.toHaveBeenCalled()
    expect(recommendationsApi.latestRun).not.toHaveBeenCalled()
  })

  it('names what to do when every list is off', async () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOff)

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Every recommendation list is switched off. Turn one on in your Profile.')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('says Loading… while the settings that decide the tabs are on their way', () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings).mockReturnValue(new Promise<RecommendationSettings>(() => {}))

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(recommendationsApi.list).not.toHaveBeenCalled()
  })

  it('says the settings could not be loaded, and Try again loads them', async () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockResolvedValueOnce(allTabsOn)
    const { user } = renderPage()
    const errorState = await screen.findByRole('alert')
    const said = errorState.textContent

    // Act
    await user.click(within(errorState).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(said).toContain('Could not load your recommendation settings.')
    expect(said).toContain('The database is unavailable.')
    expect(await screen.findByRole('heading', { level: 2, name: guardians.title })).toBeInTheDocument()
  })

  it('says Loading… while the first page of the tab is on its way', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockReturnValue(new Promise<RecommendationList>(() => {}))

    // Act
    renderPage()

    // Assert
    await screen.findByText(/^Computed /)
    expect(screen.getAllByText('Loading…')).toHaveLength(1)
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it('tells the owner to connect TMDb under Integrations, and takes them there', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [], { catalogConnected: false, lastRun: null }))
    const { user } = renderPage({ signedIn: owner })
    const sentence = await screen.findByText('No metadata catalog is connected. Connect TMDb under Integrations to get recommendations.')

    // Act
    await user.click(screen.getByRole('link', { name: 'Open Integrations' }))

    // Assert
    expect(sentence).not.toBeInTheDocument()
    expect(screen.getByText('The Integrations page')).toBeInTheDocument()
  })

  it('tells anyone else that the owner connects TMDb, with no link they could not use', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [], { catalogConnected: false, lastRun: null }))

    // Act
    renderPage({ signedIn: member })

    // Assert
    expect(await screen.findByText('No metadata catalog is connected. The owner connects TMDb under Integrations.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Open Integrations' })).not.toBeInTheDocument()
  })

  it('says nothing is recommended yet in a tab without works, with no filters to offer', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, []))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Nothing to recommend yet. The list is computed after each library sync.')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Reason' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('says the recommendations could not be loaded, and Try again loads them', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockImplementation(async (query) => listOf(query, [guardians]))
    const { user } = renderPage()
    const errorState = await screen.findByRole('alert')
    const said = errorState.textContent

    // Act
    await user.click(within(errorState).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(said).toContain('Could not load the recommendations.')
    expect(said).toContain('The database is unavailable.')
    expect(await screen.findByRole('heading', { level: 2, name: guardians.title })).toBeInTheDocument()
  })

  it('offers no Try again when the server refuses the tab, as a second try gets the same answer', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockRejectedValue(apiError(400, 'This tab is switched off in your profile.'))

    // Act
    renderPage()

    // Assert
    const errorState = await screen.findByRole('alert')
    expect(errorState).toHaveTextContent('This tab is switched off in your profile.')
    expect(within(errorState).queryByRole('button')).not.toBeInTheDocument()
  })

  it('keeps the cards on screen under a banner when a refresh of the list fails', async () => {
    // Arrange
    const client = newClient()
    client.setQueryData(qk.profile.recommendations, allTabsOn)
    client.setQueryData(qk.recommendations.list(firstQueryOf('Movies')), listOf(firstQueryOf('Movies'), [guardians, martian]))
    vi.mocked(recommendationsApi.list).mockRejectedValue(apiError(500, 'The database is unavailable.'))

    // Act
    renderPage({ client })

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the recommendations as last loaded. The database is unavailable.')
    expect(within(banner).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(cardTitles()).toEqual([guardians.title, martian.title])
  })

  it('keeps the tabs and cards on screen under a banner when a refresh of the settings fails, and Retry asks again', async () => {
    // Arrange
    const client = newClient()
    client.setQueryData(qk.profile.recommendations, { ...allTabsOn, series: false })
    vi.mocked(profileApi.recommendationSettings)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockResolvedValueOnce({ ...allTabsOn, series: false })
    const { user } = renderPage({ client })
    const banner = await screen.findByRole('alert')
    const said = banner.textContent
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    const tabsUnderBanner = tabNames()

    // Act
    await user.click(within(banner).getByRole('button', { name: 'Retry' }))

    // Assert
    expect(said).toBe('Refresh failed, showing the tabs from your recommendation settings as last loaded. The database is unavailable.Retry')
    expect(tabsUnderBanner).toEqual(['Movies', 'Cartoons', 'Documentaries'])
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(profileApi.recommendationSettings).toHaveBeenCalledTimes(2)
    expect(cardTitles()).toEqual([guardians.title, martian.title, covenant.title])
  })

  it('names the catalog its data comes from at the foot of the page', async () => {
    // Arrange
    const signedIn = member

    // Act
    renderPage({ signedIn })

    // Assert
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    expect(screen.getByText('Film and show data from TMDB. This product uses the TMDB API but is not endorsed or certified by TMDB.'))
      .toBeInTheDocument()
  })
})
