import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SeriesSeasons } from '@/components/library/SeriesSeasons'
import { libraryApi } from '@/lib/library-api'
import type { LibraryEpisodeFilters, LibraryWork } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { locations, worksPage, yellowstone, yellowstoneEpisodeOf } from '@/test/library'

vi.mock('@/lib/library-api')

const daybreak = yellowstoneEpisodeOf({})
const messenger = yellowstoneEpisodeOf({
  id: '7a1e0000-0000-4000-8000-000000000112',
  title: 'Kill the Messenger',
  originalTitle: 'Убити гінця',
  missingIdentifiers: [],
  versions: [],
  episode: { seriesId: yellowstone.id, seasonNumber: 1, episodeNumber: 2 },
})

function renderSeasons(filters?: LibraryEpisodeFilters) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const seasons = (next?: LibraryEpisodeFilters) => (
    <QueryClientProvider client={client}>
      <SeriesSeasons seriesId={yellowstone.id} seasons={yellowstone.seasons ?? []} filters={next} />
    </QueryClientProvider>
  )
  const view = render(seasons(filters))
  return { rerender: (next?: LibraryEpisodeFilters) => view.rerender(seasons(next)), user: userEvent.setup() }
}

/** The row of the season named `name`, which opens it. */
const seasonRow = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}`) })

/** The episodes of the season named `name`, once it is open. */
const episodesOf = (name: string) => screen.findByRole('list', { name: `Episodes of ${name}` })

/** The series and the query of the last request for episodes. */
const lastQuery = () => vi.mocked(libraryApi.episodes).mock.lastCall

describe('SeriesSeasons', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryApi.episodes).mockImplementation(async (_, query) => worksPage(query, [daybreak, messenger]))
  })

  it('asks for nothing until a season is opened', () => {
    // Arrange
    renderSeasons()

    // Act
    const rows = within(screen.getByRole('list', { name: 'Seasons' })).getAllByRole('button')

    // Assert
    expect(rows).toHaveLength(4)
    expect(libraryApi.episodes).not.toHaveBeenCalled()
  })

  it('opens a season to its first page of episodes, each with its number, title, missing ids and versions', async () => {
    // Arrange
    const { user } = renderSeasons()

    // Act
    await user.click(seasonRow('Season 1'))

    // Assert
    const list = await episodesOf('Season 1')
    expect(seasonRow('Season 1')).toHaveAttribute('aria-expanded', 'true')
    expect(lastQuery()).toEqual([yellowstone.id, { season: '1', page: 1, pageSize: 25 }])
    const episodes = within(list).getAllByRole('article')
    expect(episodes[0]).toHaveAccessibleName('1. Daybreak')
    expect(episodes[1]).toHaveAccessibleName('2. Kill the Messenger')
    expect(within(episodes[0]).getByText('No TVDB id')).toHaveClass('ui-badge', 'library-badge-warning')
    expect(within(episodes[0]).getByRole('list', { name: 'Versions' })).toBeInTheDocument()
    expect(within(episodes[1]).getByText('Original title: Убити гінця')).toBeInTheDocument()
    expect(within(episodes[1]).getByText('No versions reported.')).toBeInTheDocument()
    expect(within(episodes[1]).queryByRole('list', { name: 'Identifiers' })).not.toBeInTheDocument()
    // Two episodes fit the shortest page: no pager.
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it.each([
    ['Specials', '0'],
    ['No season', 'None'],
  ])('asks for the %s by season %s', async (name, season) => {
    // Arrange
    const { user } = renderSeasons()

    // Act
    await user.click(seasonRow(name))

    // Assert
    await episodesOf(name)
    expect(lastQuery()).toEqual([yellowstone.id, { season, page: 1, pageSize: 25 }])
  })

  it('passes the search and filters of the works list on, so the season lists what its count was made of', async () => {
    // Arrange
    const filters = { search: 'daybreak', locationId: locations.items[1].id, identifiers: 'Missing' as const }
    const { user } = renderSeasons(filters)

    // Act
    await user.click(seasonRow('Season 2'))

    // Assert
    await episodesOf('Season 2')
    expect(lastQuery()).toEqual([yellowstone.id, { season: '2', ...filters, page: 1, pageSize: 25 }])
  })

  it('closes an open season and keeps another open beside it', async () => {
    // Arrange
    const { user } = renderSeasons()
    await user.click(seasonRow('Season 1'))
    await user.click(seasonRow('Season 2'))
    await episodesOf('Season 2')

    // Act
    await user.click(seasonRow('Season 1'))

    // Assert
    expect(seasonRow('Season 1')).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('list', { name: 'Episodes of Season 1' })).not.toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Episodes of Season 2' })).toBeInTheDocument()
  })

  it('pages a long season on the server, and starts again at page 1 when the filters change', async () => {
    // Arrange
    vi.mocked(libraryApi.episodes).mockImplementation(async (_, query) => worksPage(query, [daybreak], 30))
    const { rerender, user } = renderSeasons()
    await user.click(seasonRow('Season 1'))
    await screen.findByText('1–25 of 30')

    // Act
    await user.click(screen.getByRole('button', { name: 'Page 2' }))
    await screen.findByText('26–30 of 30')
    rerender({ search: 'day' })

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual([yellowstone.id, { season: '1', search: 'day', page: 1, pageSize: 25 }]))
    expect(vi.mocked(libraryApi.episodes).mock.calls.map(([, query]) => query.page)).toEqual([1, 2, 1])
  })

  it('says when the episodes could not be loaded, with Try again for a failure that can pass', async () => {
    // Arrange
    vi.mocked(libraryApi.episodes).mockRejectedValue(networkError())
    const { user } = renderSeasons()

    // Act
    await user.click(seasonRow('Season 1'))

    // Assert
    expect(await screen.findByText('Could not load the episodes of this season.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('offers no Try again when the series is gone', async () => {
    // Arrange — a sync removed it since the page loaded.
    vi.mocked(libraryApi.episodes).mockRejectedValue(apiError(404))
    const { user } = renderSeasons()

    // Act
    await user.click(seasonRow('Season 1'))

    // Assert
    expect(await screen.findByText('Could not load the episodes of this season.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('says when a season has no episodes any more', async () => {
    // Arrange
    vi.mocked(libraryApi.episodes).mockImplementation(async (_, query) => worksPage(query, [] as LibraryWork[]))
    const { user } = renderSeasons()

    // Act
    await user.click(seasonRow('Season 1'))

    // Assert
    expect(await screen.findByText('This season has no episodes any more: the library changed since the page loaded.')).toBeInTheDocument()
  })
})
