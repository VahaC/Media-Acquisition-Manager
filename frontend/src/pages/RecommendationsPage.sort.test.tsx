import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { profileApi } from '@/lib/profile-api'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { RecommendationsQuery } from '@/lib/types'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { apiError } from '@/test/api-error'
import { member, signInAs } from '@/test/auth-session'
import { allTabsOn, covenant, firstQueryOf, guardians, listOf, martian, runOf } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/profile-api')

// MAM-33 — the order of a tab's works: sorted by the server, sent only when it isn't Score (its
// default), kept in the address so F5 and the other tabs keep it, and a new order starts at page 1.

/** The search part of the address the page is at. */
function LocationProbe() {
  return <output data-testid="location">{useLocation().search}</output>
}

const addressSearch = () => screen.getByTestId('location').textContent

/** The query of the last request for a list. */
const lastQuery = () => vi.mocked(recommendationsApi.list).mock.lastCall?.[0]

const sortSelect = () => screen.getByRole('combobox', { name: 'Sort by' })

const movies = firstQueryOf('Movies')

function renderPage(path = '/recommendations') {
  signInAs(member)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/recommendations" element={<><RecommendationsPage /><LocationProbe /></>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

/** Thirty works in every tab and order: the first page starts with one film, the second with another. */
function thirty(query: RecommendationsQuery) {
  return Promise.resolve(listOf(query, query.page === 1 ? [guardians] : [martian], { totalCount: 30 }))
}

describe('RecommendationsPage sort', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
    vi.mocked(recommendationsApi.list).mockImplementation(thirty)
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(runOf())
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
  })

  it('offers the orders, Best match first and picked, and sends none for it', async () => {
    // Arrange
    const path = '/recommendations'

    // Act
    renderPage(path)

    // Assert
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    expect(within(sortSelect()).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Best match', 'Rating', 'Most votes', 'Newest', 'Oldest', 'Title A–Z', 'Most releases'])
    expect(sortSelect()).toHaveValue('Score')
    expect(lastQuery()).toEqual(movies)
    expect(lastQuery()?.sort).toBeUndefined()
  })

  it('asks the server for the order picked, starts again at page 1 and puts the order in the address', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(sortSelect(), 'Most votes')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, sort: 'Votes' }))
    expect(vi.mocked(recommendationsApi.list).mock.calls.map(([query]) => query)).toEqual([
      movies,
      { ...movies, page: 2 },
      { ...movies, sort: 'Votes' },
    ])
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(addressSearch()).toBe('?tab=Movies&sort=Votes')
  })

  it('keeps the filters when the order changes', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians, covenant]))
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Reason' }), 'Directors (1)')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, reason: 'Director' }))

    // Act
    await user.selectOptions(sortSelect(), 'Newest')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...movies, reason: 'Director', sort: 'Newest' }))
    expect(screen.getByRole('combobox', { name: 'Reason' })).toHaveValue('Director')
  })

  it('opens in the order the address names, as after F5', async () => {
    // Arrange
    const path = '/recommendations?tab=Series&sort=Title'

    // Act
    renderPage(path)

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQueryOf('Series'), sort: 'Title' }))
    expect(await screen.findByRole('combobox', { name: 'Sort by' })).toHaveValue('Title')
    expect(recommendationsApi.list).toHaveBeenCalledOnce()
  })

  it('keeps the order on another tab', async () => {
    // Arrange
    const { user } = renderPage('/recommendations?sort=Rating')
    await screen.findByRole('heading', { level: 2, name: guardians.title })

    // Act
    await user.click(screen.getByRole('tab', { name: 'Documentaries' }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQueryOf('Documentaries'), sort: 'Rating' }))
    expect(addressSearch()).toBe('?tab=Documentaries&sort=Rating')
    expect(await screen.findByRole('combobox', { name: 'Sort by' })).toHaveValue('Rating')
  })

  it('leaves Best match out of the address and the request', async () => {
    // Arrange
    const { user } = renderPage('/recommendations?tab=Movies&sort=Oldest')
    await screen.findByRole('heading', { level: 2, name: guardians.title })

    // Act
    await user.selectOptions(sortSelect(), 'Best match')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual(movies))
    expect(lastQuery()?.sort).toBeUndefined()
    expect(addressSearch()).toBe('?tab=Movies')
  })

  it.each([
    ['an order that is none', 'Popularity'],
    ['an order in another letter case', 'rating'],
  ])('reads %s in the address as Best match', async (_, sort) => {
    // Arrange
    const path = `/recommendations?tab=Movies&sort=${sort}`

    // Act
    renderPage(path)

    // Assert
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    expect(sortSelect()).toHaveValue('Score')
    expect(lastQuery()).toEqual(movies)
    expect(lastQuery()?.sort).toBeUndefined()
  })
})
