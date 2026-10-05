import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { profileApi } from '@/lib/profile-api'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { RecommendationsQuery } from '@/lib/types'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { apiError } from '@/test/api-error'
import { member, signInAs } from '@/test/auth-session'
import { allTabsOn, covenant, documentaryFacets, firstQueryOf, guardians, listOf, martian, runOf } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/profile-api')

// MAM-33 — the toolbar of a tab: every filter is a query param to the server, and any change starts
// again at page 1; the page and its size, as on every server-paged list.

/** The query of the last request for a list. */
const lastQuery = () => vi.mocked(recommendationsApi.list).mock.lastCall?.[0]

const movies = firstQueryOf('Movies')

/** The options of the select named `name`, as their text. */
const optionsOf = (name: string) =>
  within(screen.getByRole('combobox', { name })).getAllByRole('option').map((option) => option.textContent)

function renderPage(path = '/recommendations') {
  signInAs(member)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <RecommendationsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

/** Thirty works in every tab, unless a filter is set: then the films alone. */
function thirtyUnlessFiltered(query: RecommendationsQuery) {
  const filtered = query.reason ?? query.genreId ?? query.decade ?? query.kind ?? query.available
  return Promise.resolve(filtered === undefined
    ? listOf(query, query.page === 1 ? [guardians] : [martian], { totalCount: 30 })
    : listOf(query, [covenant]))
}

describe('RecommendationsPage filters', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
    vi.mocked(recommendationsApi.list).mockImplementation(thirtyUnlessFiltered)
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(runOf())
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
  })

  it('offers the tab\'s reasons, genres and decades with how many works have each, and no films/series filter in Movies', async () => {
    // Arrange
    const path = '/recommendations?tab=Movies'

    // Act
    renderPage(path)

    // Assert
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    expect(optionsOf('Reason')).toEqual(['All reasons', 'Collections (1)', 'Directors (1)', 'Similar to what you have (1)'])
    expect(optionsOf('Genre')).toEqual(['All genres', 'Фантастика (3)', 'Драма (1)'])
    expect(optionsOf('Decade')).toEqual(['All decades', '2010s (2)', '2020s (1)'])
    expect(screen.queryByRole('combobox', { name: 'Films or series' })).not.toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Available only' })).not.toBeChecked()
  })

  it.each<[string, (user: ReturnType<typeof userEvent.setup>) => Promise<void>, Partial<RecommendationsQuery>]>([
    ['a reason', (user) => user.selectOptions(screen.getByRole('combobox', { name: 'Reason' }), 'Directors (1)'), { reason: 'Director' }],
    ['a genre', (user) => user.selectOptions(screen.getByRole('combobox', { name: 'Genre' }), 'Фантастика (3)'), { genreId: 878 }],
    ['a decade', (user) => user.selectOptions(screen.getByRole('combobox', { name: 'Decade' }), '2010s (2)'), { decade: 2010 }],
    ['Available only', (user) => user.click(screen.getByRole('checkbox', { name: 'Available only' })), { available: true }],
  ])('asks the server for %s and starts again at page 1', async (_, pick, expected) => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await pick(user)

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, ...expected }))
    expect(vi.mocked(recommendationsApi.list).mock.calls.map(([query]) => query)).toEqual([
      movies,
      { ...movies, page: 2 },
      { ...movies, ...expected },
    ])
    expect(await screen.findByRole('heading', { level: 2, name: covenant.title })).toBeInTheDocument()
  })

  it('combines the filters in one request', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Reason' }), 'Collections (1)')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Genre' }), 'Драма (1)')
    await user.click(screen.getByRole('checkbox', { name: 'Available only' }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, reason: 'Collection', genreId: 18, available: true }))
  })

  it('sends Available only while it is ticked, and leaves it out once it is not', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    await user.click(screen.getByRole('checkbox', { name: 'Available only' }))
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, available: true }))

    // Act
    await user.click(screen.getByRole('checkbox', { name: 'Available only' }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual(movies))
  })

  it('offers films or series in Documentaries, which hold both, and sends the kind picked', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians], { facets: documentaryFacets }))
    const { user } = renderPage('/recommendations?tab=Documentaries')
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    const options = optionsOf('Films or series')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Films or series' }), 'Series (1)')

    // Assert
    expect(options).toEqual(['Films and series', 'Films (2)', 'Series (1)'])
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQueryOf('Documentaries'), kind: 'Series' }))
  })

  it('offers films or series in Cartoons too', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians], { facets: documentaryFacets }))

    // Act
    renderPage('/recommendations?tab=Cartoons')

    // Assert
    expect(await screen.findByRole('combobox', { name: 'Films or series' })).toBeInTheDocument()
  })

  it('starts another tab unfiltered, and the tab it left unfiltered again on the way back', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Reason' }), 'Directors (1)')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, reason: 'Director' }))

    // Act
    await user.click(screen.getByRole('tab', { name: 'Series' }))
    await vi.waitFor(() => expect(lastQuery()).toEqual(firstQueryOf('Series')))
    await user.click(screen.getByRole('tab', { name: 'Movies' }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual(movies))
    expect(await screen.findByRole('combobox', { name: 'Reason' })).toHaveValue('')
  })

  it('says no work matches the filters, and Clear filters brings every work back', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) =>
      query.decade === undefined ? listOf(query, [guardians, martian]) : listOf(query, []))
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Decade' }), '2020s (1)')
    await screen.findByText('No recommendation matches these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: martian.title })).toBeInTheDocument()
    expect(lastQuery()).toEqual(movies)
    expect(screen.getByRole('combobox', { name: 'Decade' })).toHaveValue('')
  })

  it('shows the server\'s sentence for a filter it refuses, with no Try again', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => {
      if (query.genreId !== undefined) throw apiError(400, 'The genre is not one of this tab\'s.')
      return listOf(query, [guardians])
    })
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Genre' }), 'Драма (1)')

    // Assert
    const errorState = await screen.findByRole('alert')
    expect(errorState).toHaveTextContent('The genre is not one of this tab\'s.')
    expect(within(errorState).queryByRole('button')).not.toBeInTheDocument()
    // The filters stay, to change the one refused.
    expect(screen.getByRole('combobox', { name: 'Genre' })).toHaveValue('18')
  })

  it('pages through the works on the server and keeps the page size on this device', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, pageSize: 50 }))
    expect(localStorage.getItem('mam:recommendations:pageSize')).toBe('50')
  })

  it('opens at the page size this device chose last', async () => {
    // Arrange
    localStorage.setItem('mam:recommendations:pageSize', '10')

    // Act
    renderPage()

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, pageSize: 10 }))
  })

  it('moves to the last page there is when a refresh leaves fewer works than the page on screen needs', async () => {
    // Arrange
    let total = 30
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians], { totalCount: total }))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')
    total = 20

    // Act
    await user.click(screen.getByRole('button', { name: 'Previous page' }))
    await user.click(screen.getByRole('button', { name: 'Next page' }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual(movies))
    expect(await screen.findByText('1–20 of 20')).toBeInTheDocument()
  })
})
