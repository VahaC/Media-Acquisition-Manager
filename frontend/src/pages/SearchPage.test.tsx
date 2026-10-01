import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { qk } from '@/lib/queries'
import { searchApi } from '@/lib/search-api'
import type { CurrentUser, SearchResponse, SearchReleasesQuery } from '@/lib/types'
import { SearchPage } from '@/pages/SearchPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import {
  aliensMatch,
  aliensSearch,
  continentalMatch,
  firstBloodByWords,
  other,
  otherUhd,
  ramboIIIByWords,
  ramboIIIRip,
  ramboSearch,
  releasesPage,
  searchId,
  searchOf,
  toloka,
  tolokaRemux,
} from '@/test/search'

vi.mock('@/lib/search-api')

/** The query of the last request for releases. */
const lastQuery = () => vi.mocked(searchApi.releases).mock.lastCall?.[1]

const firstQuery: SearchReleasesQuery = {
  source: undefined,
  language: undefined,
  quality: undefined,
  minSizeBytes: undefined,
  maxSizeBytes: undefined,
  sort: 'seeders',
  page: 1,
  pageSize: 25,
}

/** The search part of the address the page is at. */
function LocationProbe() {
  return <output data-testid="location">{useLocation().search}</output>
}

const addressSearch = () => screen.getByTestId('location').textContent

/** The whole address the app is at: its path, then its search. */
function AddressProbe() {
  const { pathname, search } = useLocation()
  return <output data-testid="address">{pathname + search}</output>
}

function renderPage({ signedIn = owner, path = '/search' }: { signedIn?: CurrentUser; path?: string } = {}) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <SearchPage />
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The page opened at the stored search, as F5 or Back brings it. */
const openStored = (signedIn: CurrentUser = owner) => renderPage({ signedIn, path: `/search?id=${searchId}` })

/** The titles of the release rows on screen, in order. */
const releaseTitles = () =>
  within(screen.getByRole('region', { name: 'Available releases' })).queryAllByRole('article')
    .map((row) => within(row).getByRole('heading', { level: 3 }).textContent)

/** The titles of the work cards in `container`, in order. */
const cardTitles = (container: HTMLElement) =>
  within(container).getAllByRole('article').map((card) => within(card).getByRole('heading', { level: 3 }).textContent)

/** The badges of why a card is listed, as their text. */
const why = (card: HTMLElement, name = 'Why it matched') =>
  within(within(card).getByRole('list', { name })).getAllByRole('listitem').map((item) => item.textContent)

/** The list of the works holding every word of `text`, named by the line over it. */
const wordsList = (text: string) =>
  screen.getByRole('list', { name: `Not matched, but a title or a file path holds every word of “${text}”:` })

/** The source lines, as their text. */
const sourceLines = () =>
  within(screen.getByRole('list', { name: 'Release sources' })).getAllByRole('listitem').map((item) => item.textContent)

describe('SearchPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(searchApi.create).mockResolvedValue(aliensSearch)
    vi.mocked(searchApi.get).mockResolvedValue(aliensSearch)
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, [tolokaRemux, otherUhd]))
  })

  it('sends the trimmed search, keeps its id in the address and shows what came back', async () => {
    // Arrange
    const { user } = renderPage()
    await user.type(screen.getByRole('textbox', { name: 'Title' }), '  Aliens  ')
    await user.type(screen.getByRole('textbox', { name: 'Year (optional)' }), ' 1986 ')
    await user.type(screen.getByRole('textbox', { name: 'IMDb id (optional)' }), ' tt0090605')

    // Act
    await user.click(screen.getByRole('button', { name: 'Search' }))

    // Assert
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: '1986', imdbId: 'tt0090605', tmdbId: undefined })
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(addressSearch()).toBe(`?id=${searchId}`)
    expect(screen.getByRole('heading', { level: 2, name: 'In your library' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Available releases' })).toBeInTheDocument()
    expect(vi.mocked(searchApi.releases).mock.calls).toEqual([[searchId, firstQuery]])
    // The answer went into the cache: the search isn't asked for again.
    expect(searchApi.get).not.toHaveBeenCalled()
  })

  it('shows the server\'s sentence under the form when it refuses the search, and stays where it was', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockRejectedValue(apiError(400, 'Enter a title to search for.'))
    const { user } = renderPage()

    // Act
    await user.click(screen.getByRole('button', { name: 'Search' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Enter a title to search for.')
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: '', year: undefined, imdbId: undefined, tmdbId: undefined })
    expect(addressSearch()).toBe('')
    expect(screen.getByRole('button', { name: 'Search' })).toBeEnabled()
  })

  it('falls back to its own sentence when the refusal has none', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockRejectedValue(apiError(502))
    const { user } = renderPage()
    await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Aliens')

    // Act
    await user.click(screen.getByRole('button', { name: 'Search' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not search.')
  })

  it('disables Search and says Searching… while the sources are asked', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Aliens')

    // Act
    await user.click(screen.getByRole('button', { name: 'Search' }))

    // Assert
    expect(await screen.findByRole('button', { name: 'Searching…' })).toBeDisabled()
  })

  it('stays on the page the person went to when the search they left finishes', async () => {
    // Arrange
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    signInAs(owner)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={['/search']}>
          <Link to="/library">Library</Link>
          <Routes>
            <Route path="/search" element={<SearchPage />} />
            <Route path="/library" element={<p>The Library page</p>} />
          </Routes>
          <AddressProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    const user = userEvent.setup()
    await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Aliens')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByRole('button', { name: 'Searching…' })
    await user.click(screen.getByRole('link', { name: 'Library' }))
    await screen.findByText('The Library page')

    // Act
    answer(aliensSearch)

    // Assert
    // The answer still lands in the cache, so the search shows at once if the person goes back to it.
    await vi.waitFor(() => expect(client.getQueryData(qk.search.detail(searchId))).toEqual(aliensSearch))
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/library$/)
    expect(screen.getByText('The Library page')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 1, name: 'Search' })).not.toBeInTheDocument()
  })

  it('brings the search in the address back into an untouched form', async () => {
    // Arrange
    const path = `/search?id=${searchId}`

    // Act
    renderPage({ path })

    // Assert
    expect(await screen.findByDisplayValue('Aliens')).toBe(screen.getByRole('textbox', { name: 'Title' }))
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('1986')
    expect(screen.getByRole('textbox', { name: 'IMDb id (optional)' })).toHaveValue('tt0090605')
    expect(screen.getByRole('textbox', { name: 'TMDb id (optional)' })).toHaveValue('')
    expect(searchApi.get).toHaveBeenCalledExactlyOnceWith(searchId)
  })

  it('keeps what the person typed when the search in the address arrives after it', async () => {
    // Arrange
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.get).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { user } = openStored()
    await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Interstellar')

    // Act
    answer(aliensSearch)

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'In your library' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Title' })).toHaveValue('Interstellar')
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('')
  })

  it('lists the library matches as cards, strongest first, with why each matched and every conflict', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({
      library: [{ ...aliensMatch, conflictingSchemes: ['Tmdb'] }, continentalMatch],
    }))

    // Act
    openStored()

    // Assert
    const section = await screen.findByRole('region', { name: 'In your library' })
    const cards = within(section).getAllByRole('article')
    expect(cardTitles(section)).toEqual(['Чужі', 'The Continental'])
    expect(why(cards[0])).toEqual(['Matched by IMDb id', '“Aliens”, 1986', 'Conflicting TMDb ids'])
    expect(within(cards[0]).getByText('Matched by IMDb id')).toHaveAttribute('title', 'IMDb tt0090605, reported by Home Jellyfin')
    expect(why(cards[1])).toEqual(['Possible match — check it', '“The Continental”, 2023'])
    // The Library page's card as it is: its identifiers and its versions with their facts.
    expect(within(cards[0]).getByRole('list', { name: 'Identifiers' })).toBeInTheDocument()
    expect(within(cards[0]).getByText('2:17:17')).toBeInTheDocument()
  })

  it('says when nothing in the library matches the search', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({ library: [] }))

    // Act
    openStored()

    // Assert
    expect(await screen.findByText('Nothing in your library matches this search.')).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'In your library' })).queryByRole('article')).not.toBeInTheDocument()
  })

  it('lists the works holding every word of the title after the matches, under a line saying they are no match', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(ramboSearch)

    // Act
    openStored()

    // Assert
    const section = await screen.findByRole('region', { name: 'In your library' })
    expect(cardTitles(section)).toEqual(['Рембо IV', 'Рембо III', 'Рембо. Перша кров'])
    const byWords = wordsList('rambo')
    expect(cardTitles(byWords)).toEqual(['Рембо III', 'Рембо. Перша кров'])
    const [ramboIII, firstBlood] = within(byWords).getAllByRole('article')
    expect(why(ramboIII, 'Why it is listed')).toEqual(['Title contains “rambo” — not a match', '“Rambo III”, 1988'])
    expect(why(firstBlood, 'Why it is listed')).toEqual(['A file path contains “rambo” — not a match'])
    expect(why(within(section).getAllByRole('article')[0])).toEqual(['Possible match — check it', '“Rambo”, 2008'])
    // The line comes between the match and the works it introduces.
    const lead = screen.getByText('Not matched, but a title or a file path holds every word of “rambo”:')
    expect(lead).toHaveClass('settings-muted')
    expect(screen.getByRole('heading', { level: 3, name: 'Рембо IV' }).compareDocumentPosition(lead))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING)
    // Every work holding the words is listed: nothing more to count, nowhere else to look.
    expect(screen.queryByText('Nothing in your library matches this search.')).not.toBeInTheDocument()
    expect(screen.queryByText(/more movies? in your library holds? these words/)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /on the Library page$/ })).not.toBeInTheDocument()
  })

  it('says nothing in the library matches, then lists the works holding every word of the title', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({ ...ramboSearch, library: [ramboIIIByWords, firstBloodByWords] }))

    // Act
    openStored()

    // Assert
    expect(await screen.findByText('Nothing in your library matches this search.')).toBeInTheDocument()
    expect(cardTitles(wordsList('rambo'))).toEqual(['Рембо III', 'Рембо. Перша кров'])
    expect(cardTitles(screen.getByRole('region', { name: 'In your library' }))).toEqual(['Рембо III', 'Рембо. Перша кров'])
  })

  it.each([
    [1, '1 more movie in your library holds these words.'],
    [35, '35 more movies in your library hold these words.'],
  ])('counts the %s works holding the words past the ones listed, and links to the Library page searched for the title', async (count, line) => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({ ...ramboSearch, text: 'Rambo: First Blood', libraryUnlistedCount: count }))

    // Act
    openStored()

    // Assert
    const link = await screen.findByRole('link', { name: 'Search “Rambo: First Blood” on the Library page' })
    expect(link).toHaveAttribute('href', '/library?search=Rambo%3A+First+Blood')
    expect(link).toHaveClass('library-link')
    expect(link.closest('p')).toHaveTextContent(`${line} Search “Rambo: First Blood” on the Library page`)
    expect(cardTitles(wordsList('Rambo: First Blood'))).toEqual(['Рембо III', 'Рембо. Перша кров'])
  })

  it('names a work listed by words on a release that matches it on its own evidence, and in its Details', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(ramboSearch)
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, [ramboIIIRip]))
    const { user } = openStored()
    const row = await screen.findByRole('article', { name: ramboIIIRip.title })

    // Act
    await user.click(within(row).getByRole('button', { name: 'Details' }))

    // Assert
    // The dialog hides the page from the accessibility tree, so the row's badge is found by its text.
    expect(within(row).getByText('In your library: Рембо III (1988) — matched by title and year')).toBeInTheDocument()
    const dialog = await screen.findByRole('dialog', { name: ramboIIIRip.title })
    const work = within(dialog).getByRole('region', { name: 'Рембо III' })
    expect(within(work).getByText('Matched by title and year')).toBeInTheDocument()
    expect(within(work).queryByText(/not a match/)).not.toBeInTheDocument()
  })

  it('counts the releases of each source that answered', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({
      sources: [other, { ...toloka, releaseCount: 0 }],
    }))

    // Act
    openStored()

    // Assert
    await screen.findByRole('list', { name: 'Release sources' })
    expect(sourceLines()).toEqual(['Other · 1 release', 'Toloka · No releases'])
  })

  it.each([
    ['any other user', member, null, 'Toloka · Couldn\'t be reached.'],
    [
      'the owner',
      owner,
      'Toloka could not be reached at http://127.0.0.1:9/1/api.',
      'Toloka · Couldn\'t be reached. Toloka could not be reached at http://127.0.0.1:9/1/api.',
    ],
  ])('shows a failing source\'s status to %s, with the sentence the API gives, while the other source\'s releases show', async (_, signedIn, error, line) => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({
      sources: [other, { ...toloka, status: 'Unreachable', error, releaseCount: 0 }],
    }))
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, [otherUhd]))

    // Act
    openStored(signedIn)

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: otherUhd.title })).toBeInTheDocument()
    expect(sourceLines()).toEqual(['Other · 1 release', line])
    expect(screen.getByText('Toloka').closest('li')).toHaveAttribute('data-status', 'Failed')
    expect(releaseTitles()).toEqual([otherUhd.title])
  })

  it('says why a source was not asked', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({
      sources: [other, { ...toloka, status: null, notSearchedReason: 'SecretUnreadable', releaseCount: 0 }],
    }))

    // Act
    openStored()

    // Assert
    await screen.findByRole('list', { name: 'Release sources' })
    expect(sourceLines()[1])
      .toBe('Toloka · Couldn\'t search: its stored API key can\'t be read. The owner enters it again under Integrations.')
    expect(screen.getByText('Toloka').closest('li')).toHaveClass('library-source-warning')
  })

  it('says when no source listed anything, and offers no filters', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({
      sources: [{ ...other, releaseCount: 0 }, { ...toloka, releaseCount: 0 }],
    }))
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, []))

    // Act
    openStored()

    // Assert
    expect(await screen.findByText('No source listed a release for this search.')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Source' })).not.toBeInTheDocument()
  })

  it('says when no release source was searched at all', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({ sources: [] }))
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => releasesPage(query, []))

    // Act
    openStored()

    // Assert
    expect(await screen.findByText(/^No release source was searched: none is switched on\./)).toBeInTheDocument()
    expect(screen.queryByText('No source listed a release for this search.')).not.toBeInTheDocument()
  })

  it.each([
    ['source', 'Source', 'Toloka', { source: toloka.id }],
    ['audio language', 'Audio language', 'Ukrainian', { language: 'ukr' }],
    ['unknown audio language', 'Audio language', 'Unknown', { language: 'unknown' }],
    ['quality', 'Quality', '2160p', { quality: '2160' }],
    ['unknown quality', 'Quality', 'Unknown', { quality: 'unknown' }],
    ['order', 'Sort', 'Newest', { sort: 'newest' as const }],
  ])('sends the %s to the server and starts again at page 1', async (_, name, option, changes) => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.page === 1 ? [tolokaRemux] : [otherUhd], 30))
    const { user } = openStored()
    await screen.findByText('Page 1 of 2')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Page 2 of 2')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name }), option)

    // Assert
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, ...changes })
    expect(vi.mocked(searchApi.releases).mock.calls.map(([id]) => id)).toEqual([searchId, searchId, searchId])
  })

  it('sends the size bounds in bytes once typing pauses, from page 1', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.page === 1 ? [tolokaRemux] : [otherUhd], 30))
    const { user } = openStored()
    await screen.findByText('Page 1 of 2')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Page 2 of 2')

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Smallest size in GiB' }), '4.5')
    await user.type(screen.getByRole('textbox', { name: 'Largest size in GiB' }), '30')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQuery, minSizeBytes: '4831838208', maxSizeBytes: '32212254720' }))
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
  })

  it('leaves a bound that is not a size out of the request, and says so', async () => {
    // Arrange
    const { user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Largest size in GiB' }), 'big')
    await user.type(screen.getByRole('textbox', { name: 'Smallest size in GiB' }), '1')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQuery, minSizeBytes: '1073741824' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a size in GiB, such as 4.5.')
  })

  it('shows the server\'s sentence for bounds it refuses, with the filters still there to fix them', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) => {
      if (query.minSizeBytes && query.maxSizeBytes) throw apiError(400, 'The smallest size can\'t be larger than the largest.')
      return releasesPage(query, [tolokaRemux])
    })
    const { user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Smallest size in GiB' }), '30')
    await user.type(screen.getByRole('textbox', { name: 'Largest size in GiB' }), '4')

    // Assert
    expect(await screen.findByText('The smallest size can\'t be larger than the largest.')).toHaveAttribute('role', 'alert')
    expect(releaseTitles()).toEqual([])
    expect(screen.getByRole('textbox', { name: 'Largest size in GiB' })).toHaveValue('4')
  })

  it('says no release matches the filters, and Clear filters brings every release back in the same order', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.source ? [] : [tolokaRemux, otherUhd]))
    const { user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort' }), 'Largest')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Source' }), 'Toloka')
    await screen.findByText('No release matches these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, sort: 'largest' })
    expect(screen.getByRole('combobox', { name: 'Source' })).toHaveValue('')
    expect(screen.getByRole('combobox', { name: 'Sort' })).toHaveValue('largest')
    expect(screen.queryByText('No release matches these filters.')).not.toBeInTheDocument()
  })

  it('pages through the releases on the server and keeps the page size on this device', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.page === 1 ? [tolokaRemux] : [otherUhd], 60))
    const { user } = openStored()
    await screen.findByText('Page 1 of 3')

    // Act
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('heading', { level: 3, name: otherUhd.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(vi.mocked(searchApi.releases).mock.calls.map(([, query]) => query)).toEqual([
      firstQuery,
      { ...firstQuery, page: 2 },
      { ...firstQuery, pageSize: 50 },
    ])
    expect(localStorage.getItem('mam:search:pageSize')).toBe('50')
  })

  it('says a search that has expired, or is someone else\'s, is no longer available', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockRejectedValue(apiError(404))

    // Act
    openStored()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('This search is no longer available. Search again.')
    expect(searchApi.releases).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Search' })).toBeEnabled()
  })

  it.each([
    ['a way out to another API path', '..%2Flibrary%2Fworks'],
    ['the parent of the searches\' path', '..'],
    ['not an id at all', 'aliens'],
  ])('asks nothing for an id in the address that is %s, and says the search is no longer available', async (_, id) => {
    // Arrange
    const path = `/search?id=${id}`

    // Act
    renderPage({ path })

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('This search is no longer available. Search again.')
    expect(searchApi.get).not.toHaveBeenCalled()
    expect(searchApi.releases).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Search' })).toBeEnabled()
  })

  it.each([
    ['the API\'s sentence', apiError(500, 'Database is locked.'), 'Database is locked.'],
    ['its own sentence when the failure has none', apiError(500), 'Could not load this search.'],
    ['that the server is out of reach', networkError(), 'The server could not be reached. Check that it is running, then try again.'],
  ])('shows %s when the search fails to load', async (_, failure, sentence) => {
    // Arrange
    vi.mocked(searchApi.get).mockRejectedValue(failure)

    // Act
    openStored()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
  })

  it('keeps the results on screen under a banner when a refresh of the search fails', async () => {
    // Arrange
    const { client } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    vi.mocked(searchApi.get).mockRejectedValue(apiError(500, 'Database is locked.'))

    // Act
    await client.refetchQueries({ queryKey: qk.search.detail(searchId) })

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the search as it was last loaded. Database is locked.')
    expect(banner).toHaveClass('settings-banner')
    expect(screen.getByRole('heading', { level: 3, name: 'Чужі' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
  })

  it('keeps the releases on screen under a banner when a refresh of them fails', async () => {
    // Arrange
    const { client } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    vi.mocked(searchApi.releases).mockRejectedValue(apiError(500, 'Database is locked.'))

    // Act
    await client.refetchQueries({ queryKey: qk.search.releases(searchId, firstQuery) })

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refresh failed, showing the last loaded releases. Database is locked.')
    expect(releaseTitles()).toEqual([tolokaRemux.title, otherUhd.title])
  })

  it('says the search is no longer available when its releases answer 404', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockRejectedValue(apiError(404))

    // Act
    openStored()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('This search is no longer available. Search again.')
    expect(screen.getByRole('heading', { level: 3, name: 'Чужі' })).toBeInTheDocument()
  })

  it('opens the Details of a release and closes them again', async () => {
    // Arrange
    const { user } = openStored()
    const row = await screen.findByRole('article', { name: tolokaRemux.title })

    // Act
    await user.click(within(row).getByRole('button', { name: 'Details' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: tolokaRemux.title })
    expect(within(dialog).getByRole('region', { name: 'Чужі' })).toBeInTheDocument()
    await user.click(within(dialog).getAllByRole('button', { name: 'Close' })[0])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('starts a new search with filters of its own', async () => {
    // Arrange
    const nextId = '5ea4c000-0000-4000-8000-000000000002'
    vi.mocked(searchApi.create).mockResolvedValue(searchOf({ id: nextId, text: 'Aliens', year: null, imdbId: null }))
    const { user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Source' }), 'Toloka')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQuery, source: toloka.id }))
    await user.clear(screen.getByRole('textbox', { name: 'Year (optional)' }))
    await user.clear(screen.getByRole('textbox', { name: 'IMDb id (optional)' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Search' }))

    // Assert
    await vi.waitFor(() => expect(addressSearch()).toBe(`?id=${nextId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: undefined, imdbId: undefined, tmdbId: undefined })
    await vi.waitFor(() => expect(vi.mocked(searchApi.releases).mock.lastCall).toEqual([nextId, firstQuery]))
    expect(screen.getByRole('combobox', { name: 'Source' })).toHaveValue('')
  })
})
