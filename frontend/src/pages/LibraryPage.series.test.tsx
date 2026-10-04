import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { libraryApi } from '@/lib/library-api'
import { LibraryPage } from '@/pages/LibraryPage'
import { owner, signInAs } from '@/test/auth-session'
import { aliens, locations, sourceOf, sourcesOf, tomAndJerryKids, worksPage, yellowstone, yellowstoneEpisodeOf } from '@/test/library'

// MAM-32 — the Library page with series: a series is one card among the movies, an episode of no
// series a card of its own, and a kind filter shows either. The search and filters reach into a
// series' seasons.

vi.mock('@/lib/library-api')

/** What the last request for the works asked for. */
const lastQuery = () => vi.mocked(libraryApi.works).mock.lastCall?.[0]

/** The titles of the work cards on screen, in order: the cards of the list, not the episodes inside them. */
const cardTitles = () =>
  screen.queryAllByRole('article')
    .filter((card) => card.classList.contains('library-work'))
    .map((card) => within(card).getByRole('heading', { level: 2 }).textContent)

const firstQuery = { search: undefined, locationId: undefined, identifiers: undefined, kind: undefined, page: 1, pageSize: 25 }

function renderPage() {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/library']}>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

describe('LibraryPage with series', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.kind === 'Series' ? [tomAndJerryKids, yellowstone] : [aliens, tomAndJerryKids, yellowstone]))
    vi.mocked(libraryApi.episodes).mockImplementation(async (_, query) => worksPage(query, [yellowstoneEpisodeOf({})]))
    vi.mocked(libraryApi.locations).mockResolvedValue(locations)
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf(sourceOf({})))
  })

  it('lists a series as one card among the movies, and an episode of no series as a card of its own', async () => {
    // Arrange
    renderPage()

    // Act
    await screen.findByRole('heading', { level: 2, name: 'Єллоустоун' })

    // Assert
    expect(cardTitles()).toEqual(['Чужі', 'Tom & Jerry Kids', 'Єллоустоун'])
    expect(lastQuery()).toEqual(firstQuery)
    expect(screen.getByRole('list', { name: 'Seasons' })).toBeInTheDocument()
    expect(screen.getByText('Season 1 · Episode 4')).toBeInTheDocument()
  })

  it('asks the server for one kind of work and starts again at page 1', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      query.kind === 'Series' ? worksPage(query, [yellowstone]) : worksPage(query, [aliens], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Series')

    // Assert
    expect(await screen.findByText('1–1 of 1')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, kind: 'Series' })
    expect(cardTitles()).toEqual(['Єллоустоун'])
  })

  it('offers both kinds and the movies alone', async () => {
    // Arrange
    renderPage()

    // Act
    const select = await screen.findByRole('combobox', { name: 'Kind' })

    // Assert
    expect([...select.querySelectorAll('option')].map((option) => option.textContent))
      .toEqual(['Movies and series', 'Movies', 'Series'])
    expect(select).toHaveValue('')
  })

  it('opens a season with the search and filters of the list, but not its kind', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Єллоустоун' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Series')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Identifiers' }), 'Missing')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Library' }), 'Фільми')
    await user.type(screen.getByRole('searchbox', { name: 'Search the library' }), 'daybreak')
    await vi.waitFor(() => expect(lastQuery()?.search).toBe('daybreak'))

    // Act
    await user.click(await screen.findByRole('button', { name: /^Season 1/ }))

    // Assert
    await screen.findByRole('list', { name: 'Episodes of Season 1' })
    expect(vi.mocked(libraryApi.episodes).mock.lastCall).toEqual([
      yellowstone.id,
      { season: '1', search: 'daybreak', locationId: locations.items[1].id, identifiers: 'Missing', page: 1, pageSize: 25 },
    ])
  })

  it('lets Clear filters show every kind again', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.kind === 'Movie' && query.identifiers ? [] : [aliens]))
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Movie')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Identifiers' }), 'Missing')
    await screen.findByText('Nothing in the library matches these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(lastQuery()).toEqual(firstQuery)
    expect(screen.getByRole('combobox', { name: 'Kind' })).toHaveValue('')
  })
})
