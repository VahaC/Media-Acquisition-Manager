import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { formatNumber } from '@/lib/format'
import { profileApi } from '@/lib/profile-api'
import { qk } from '@/lib/queries'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { Recommendation } from '@/lib/types'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import { allTabsOn, covenant, firstQueryOf, guardians, listOf, martian, runOf, yellowstone } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/profile-api')

// MAM-33 — a recommendation's card: what it shows, Releases, and Not interested with its Undo.

/** The address the app is at, path and search. */
function AddressProbe() {
  const { pathname, search } = useLocation()
  return <output data-testid="address">{pathname + search}</output>
}

const address = () => screen.getByTestId('address').textContent

/** The card titled `title`. */
const card = (title: string) => screen.getByRole('article', { name: title })

/** The grid's entries in order: a card's title, or a hidden work's whole line. */
const gridEntries = () =>
  [...document.querySelectorAll('.recommendation-grid > li')].map((entry) =>
    within(entry as HTMLElement).queryByRole('heading')?.textContent ?? entry.textContent)

/** The card titled `title`, once the list has arrived. */
const cardOnScreen = (title: string) => screen.findByRole('article', { name: title })

function renderPage(items: Recommendation[] = [guardians, martian, covenant], path = '/recommendations') {
  signInAs(owner)
  vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, items))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const view = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/recommendations" element={<RecommendationsPage />} />
          <Route path="/search" element={<p>The Search page</p>} />
          <Route path="/library" element={<p>The Library page</p>} />
        </Routes>
        <AddressProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, view, user: userEvent.setup() }
}

describe('RecommendationsPage cards', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(runOf())
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
  })

  it('shows a film\'s title, original title, year, genres and rating with its votes', async () => {
    // Arrange
    const items = [guardians]

    // Act
    renderPage(items)

    // Assert
    const film = await cardOnScreen(guardians.title)
    expect(within(film).getByRole('heading', { level: 2 })).toHaveTextContent('Вартові Галактики 3')
    expect(within(film).getByText('Original title: Guardians of the Galaxy Vol. 3')).toBeInTheDocument()
    expect(within(film).getByText('2023')).toBeInTheDocument()
    expect(within(film).getByText('Фантастика, Пригоди, Комедія')).toBeInTheDocument()
    expect(within(film).getByText(`7.9 (${formatNumber(8905)} votes)`)).toBeInTheDocument()
  })

  it('puts the genres on a line of their own', async () => {
    // Arrange
    const items = [guardians]

    // Act
    renderPage(items)

    // Assert
    const genres = within(await cardOnScreen(guardians.title)).getByText('Фантастика, Пригоди, Комедія')
    expect(genres.tagName).toBe('P')
  })

  it('shows the rating beside the title with its votes in short, and the full count on hover', async () => {
    // Arrange
    const items = [guardians]

    // Act
    renderPage(items)

    // Assert
    const film = await cardOnScreen(guardians.title)
    const rating = within(film).getByTitle(`7.9 (${formatNumber(8905)} votes)`)
    expect(rating.previousElementSibling).toContainElement(within(film).getByRole('heading', { level: 2 }))
    expect(within(rating).getByText('7.9 (8.9K)')).toHaveAttribute('aria-hidden', 'true')
  })

  it('leaves out an original title that is the title, a rating nobody gave and a year the catalog lacks', async () => {
    // Arrange
    const items = [{ ...guardians, originalTitle: guardians.title, rating: null, voteCount: 0, year: null, genres: [] }]

    // Act
    renderPage(items)

    // Assert
    const film = await cardOnScreen(guardians.title)
    expect(within(film).queryByText(/^Original title/)).not.toBeInTheDocument()
    expect(within(film).queryByText(/votes?\)$/)).not.toBeInTheDocument()
    expect(within(film).queryByText('2023')).not.toBeInTheDocument()
  })

  it('says one vote in the singular', async () => {
    // Arrange
    const items = [covenant]

    // Act
    renderPage(items)

    // Assert
    expect(within(await cardOnScreen(covenant.title)).getByText('6.1 (1 vote)')).toBeInTheDocument()
  })

  it.each([
    [guardians, ['Guardians of the Galaxy · 2 of 3']],
    [martian, ['Ridley Scott · 12 in your library']],
    [covenant, ['Because you have Alien, Prometheus and 3 more', 'Top rated', 'New on Toloka.to · 2 days ago']],
    [yellowstone, ['Missing seasons 4–5', 'Taylor Sheridan · 2 in your library']],
  ])('says why $title is recommended, a chip per reason', async (item, expected) => {
    // Arrange
    const items = [item]

    // Act
    renderPage(items)

    // Assert
    const reasons = within(await cardOnScreen(item.title)).getByRole('list', { name: 'Why it is recommended' })
    expect(within(reasons).getAllByRole('listitem').map((chip) => chip.textContent)).toEqual(expected)
  })

  it.each([
    [guardians, '4 releases · up to 2160p · Ukr'],
    [yellowstone, '1 release · up to 1080p'],
    [martian, 'Not found (checked 3 days ago)'],
    [covenant, 'Not checked yet'],
  ])('says what the release sources had for $title', async (item, expected) => {
    // Arrange
    const items = [item]

    // Act
    renderPage(items)

    // Assert
    expect(within(await cardOnScreen(item.title)).getByText((_, element) =>
      element?.tagName === 'P' && element.textContent === expected)).toBeInTheDocument()
  })

  it('names the Ukrainian audio in full on hover', async () => {
    // Arrange
    const items = [guardians]

    // Act
    renderPage(items)

    // Assert
    expect(within(await cardOnScreen(guardians.title)).getByTitle('With Ukrainian audio')).toHaveTextContent('Ukr')
  })

  it('marks a work a followed download matches as Downloading, and no other', async () => {
    // Arrange
    const items = [yellowstone, guardians]

    // Act
    renderPage(items, '/recommendations?tab=Series')

    // Assert
    expect(within(await cardOnScreen(yellowstone.title)).getByText('Downloading')).toHaveClass('ui-badge')
    expect(within(card(guardians.title)).queryByText('Downloading')).not.toBeInTheDocument()
  })

  it.each([
    ['a film, narrowed by its TMDb id', guardians, '/search?q=Guardians%20of%20the%20Galaxy%20Vol.%203%20(2023)&tmdbId=447365'],
    ['a show, by its title and year alone', yellowstone, '/search?q=Yellowstone%20(2018)'],
  ])('opens the Search page for %s from Releases', async (_, item, expected) => {
    // Arrange
    const { user } = renderPage([item])
    const releases = within(await cardOnScreen(item.title)).getByRole('link', { name: 'Releases' })
    const href = releases.getAttribute('href')

    // Act
    await user.click(releases)

    // Assert
    expect(href).toBe(expected)
    expect(address()).toBe(expected)
    expect(screen.getByText('The Search page')).toBeInTheDocument()
  })

  it('keeps a title holding & or # whole in the Releases link', async () => {
    // Arrange
    const item = { ...guardians, searchText: 'Fast & Furious #9 (2021)', searchTmdbId: '385128' }

    // Act
    renderPage([item])

    // Assert
    const releases = within(await cardOnScreen(item.title)).getByRole('link', { name: 'Releases' })
    expect(new URL(releases.getAttribute('href')!, 'http://app').searchParams.get('q')).toBe('Fast & Furious #9 (2021)')
  })

  it('gives every action a title, which names it once a phone shows the icon alone', async () => {
    // Arrange
    const items = [guardians]

    // Act
    renderPage(items)

    // Assert
    const actions = within(await cardOnScreen(guardians.title)).getByRole('group', { name: 'Actions' })
    expect([...actions.children].map((action) => action.getAttribute('title'))).toEqual(['Releases', 'Not interested'])
  })

  it('hides a work on Not interested, leaving a line with Undo in its place, and asks for no list again', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockResolvedValue()
    const { user } = renderPage()
    await cardOnScreen(guardians.title)

    // Act
    await user.click(within(card(guardians.title)).getByRole('button', { name: 'Not interested' }))

    // Assert
    expect(recommendationsApi.dismiss).toHaveBeenCalledExactlyOnceWith('Movie', '447365')
    expect(await screen.findByText('Hidden: Вартові Галактики 3.')).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: guardians.title })).not.toBeInTheDocument()
    expect(gridEntries()).toEqual(['Hidden: Вартові Галактики 3.Undo', martian.title, covenant.title])
    expect(screen.getByRole('button', { name: 'Undo' })).toHaveFocus()
    expect(recommendationsApi.list).toHaveBeenCalledOnce()
  })

  it('brings the card back where it was on Undo', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockResolvedValue()
    vi.mocked(recommendationsApi.undismiss).mockResolvedValue()
    const { user } = renderPage()
    await user.click(within(await cardOnScreen(guardians.title)).getByRole('button', { name: 'Not interested' }))

    // Act
    await user.click(await screen.findByRole('button', { name: 'Undo' }))

    // Assert
    expect(recommendationsApi.undismiss).toHaveBeenCalledExactlyOnceWith('Movie', '447365')
    expect(await cardOnScreen(guardians.title)).toBeInTheDocument()
    expect(screen.queryByText(/^Hidden:/)).not.toBeInTheDocument()
    expect(gridEntries()).toEqual([guardians.title, martian.title, covenant.title])
    expect(within(card(guardians.title)).getByRole('button', { name: 'Not interested' })).toHaveFocus()
    expect(recommendationsApi.list).toHaveBeenCalledOnce()
  })

  it('keeps a show\'s kind in what it hides', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockResolvedValue()
    const { user } = renderPage([yellowstone], '/recommendations?tab=Series')

    // Act
    await user.click(within(await cardOnScreen(yellowstone.title)).getByRole('button', { name: 'Not interested' }))

    // Assert
    expect(recommendationsApi.dismiss).toHaveBeenCalledExactlyOnceWith('Series', '73586')
  })

  it('says inside the card why Not interested was refused, and keeps the card', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockRejectedValue(apiError(400, 'The catalog id is not one.'))
    const { user } = renderPage()

    // Act
    await user.click(within(await cardOnScreen(guardians.title)).getByRole('button', { name: 'Not interested' }))

    // Assert
    expect(await within(card(guardians.title)).findByRole('alert')).toHaveTextContent('The catalog id is not one.')
    expect(screen.queryByText(/^Hidden:/)).not.toBeInTheDocument()
  })

  it('falls back to its own sentence when the refusal has none', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockRejectedValue(apiError(500))
    const { user } = renderPage()

    // Act
    await user.click(within(await cardOnScreen(guardians.title)).getByRole('button', { name: 'Not interested' }))

    // Assert
    expect(await within(card(guardians.title)).findByRole('alert')).toHaveTextContent('Could not hide it.')
  })

  it('says in the line why Undo was refused, and keeps the work hidden', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockResolvedValue()
    vi.mocked(recommendationsApi.undismiss).mockRejectedValue(apiError(503, 'The server is busy.'))
    const { user } = renderPage()
    await user.click(within(await cardOnScreen(guardians.title)).getByRole('button', { name: 'Not interested' }))

    // Act
    await user.click(await screen.findByRole('button', { name: 'Undo' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The server is busy.')
    expect(screen.getByText('Hidden: Вартові Галактики 3.')).toBeInTheDocument()
  })

  it('keeps a hidden work a line when the person comes back to its tab, until its list, asked again, drops it', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockResolvedValue()
    const { user } = renderPage()
    await user.click(within(await cardOnScreen(guardians.title)).getByRole('button', { name: 'Not interested' }))
    await screen.findByText('Hidden: Вартові Галактики 3.')
    let answer: (value: ReturnType<typeof listOf>) => void = () => {}
    vi.mocked(recommendationsApi.list).mockImplementation((query) =>
      query.tab === 'Movies' ? new Promise((resolve) => { answer = resolve }) : Promise.resolve(listOf(query, [yellowstone])))
    await user.click(screen.getByRole('tab', { name: 'Series' }))
    await cardOnScreen(yellowstone.title)

    // Act
    await user.click(screen.getByRole('tab', { name: 'Movies' }))
    const whileAsking = screen.queryByText('Hidden: Вартові Галактики 3.')
    answer(listOf(firstQueryOf('Movies'), [martian, covenant]))

    // Assert
    expect(whileAsking).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: guardians.title })).not.toBeInTheDocument()
    await vi.waitFor(() => expect(screen.queryByText('Hidden: Вартові Галактики 3.')).not.toBeInTheDocument())
    expect(screen.getAllByRole('article').map((article) => within(article).getByRole('heading').textContent))
      .toEqual([martian.title, covenant.title])
  })

  it('drops the cached lists when the person leaves the page after hiding a work, so the next visit asks again', async () => {
    // Arrange
    vi.mocked(recommendationsApi.dismiss).mockResolvedValue()
    const { client, view } = renderPage()
    const user = userEvent.setup()
    await user.click(within(await cardOnScreen(guardians.title)).getByRole('button', { name: 'Not interested' }))
    await screen.findByText('Hidden: Вартові Галактики 3.')
    const cachedBefore = client.getQueryData(qk.recommendations.list(firstQueryOf('Movies')))

    // Act
    view.unmount()

    // Assert
    expect(cachedBefore).toBeDefined()
    expect(client.getQueryData(qk.recommendations.list(firstQueryOf('Movies')))).toBeUndefined()
  })

  it('keeps the cached lists when the person leaves the page without hiding anything', async () => {
    // Arrange
    const { client, view } = renderPage()
    await cardOnScreen(guardians.title)

    // Act
    view.unmount()

    // Assert
    expect(client.getQueryData(qk.recommendations.list(firstQueryOf('Movies')))).toBeDefined()
  })
})
