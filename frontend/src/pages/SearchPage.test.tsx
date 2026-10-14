import { StrictMode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { downloadsApi } from '@/lib/downloads-api'
import { qk } from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import { searchApi } from '@/lib/search-api'
import type { CurrentUser, SearchResponse, SearchReleasesQuery } from '@/lib/types'
import { SearchPage } from '@/pages/SearchPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { localClient, optionsOf, overwriteOf } from '@/test/downloads'
import { optionsUnavailable } from '@/test/replacements'
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
vi.mock('@/lib/downloads-api')
// MAM-24 — the owner's Download dialog asks which copies the release may replace.
vi.mock('@/lib/replacements-api')

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

/** The browser's Back button. */
function BackButton() {
  const navigate = useNavigate()
  return <button type="button" onClick={() => navigate(-1)}>Back</button>
}

/** The browser's Forward button. */
function ForwardButton() {
  const navigate = useNavigate()
  return <button type="button" onClick={() => navigate(1)}>Forward</button>
}

/**
 * The page with the history `entries`, the last one open, beside the Library page, Back and Forward
 * buttons and a link to each of `links` named by its address — the way the header's field moves the
 * page.
 */
function renderInHistory(entries: string[], { links = [], strict = false }: { links?: string[]; strict?: boolean } = {}) {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const app = (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
        <BackButton />
        <ForwardButton />
        <Link to="/library">Library</Link>
        {links.map((to) => <Link key={to} to={to}>{to}</Link>)}
        <Routes>
          <Route path="/search" element={<SearchPage />} />
          <Route path="/library" element={<p>The Library page</p>} />
        </Routes>
        <AddressProbe />
      </MemoryRouter>
    </QueryClientProvider>
  )
  render(strict ? <StrictMode>{app}</StrictMode> : app)
  return { client, user: userEvent.setup() }
}

const address = () => screen.getByTestId('address').textContent

/** The "Results for" line, found by its whole text. */
const resultsFor = (text: string) =>
  screen.getByText((_, element) => element?.textContent === `Results for “${text}”.`)

/** The line beside "Results for “`text`”.": how to narrow the results, or that a search is being made. */
const noteBeside = (text: string) => within(resultsFor(text).parentElement as HTMLElement).getByRole('status')

/** `/search?q=` with `line` encoded, as the header's field opens it. */
const searchFor = (line: string) => `/search?${new URLSearchParams({ q: line })}`

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
    vi.mocked(replacementsApi.options).mockResolvedValue(optionsUnavailable('SwitchedOff'))
  })

  it('shows how to start a search at a bare /search, and asks nothing', async () => {
    // Arrange
    const path = '/search'

    // Act
    renderPage({ path })

    // Assert
    expect(screen.getByText('Type a title in the search field at the top, then press Enter.')).toBeInTheDocument()
    expect(screen.getByText('Add the year in brackets after the title to narrow it: “Interstellar (2014)”. A season goes in the Season field once the results are on screen. Press / on any page to jump to the field.'))
      .toBeInTheDocument()
    // Nothing to narrow yet.
    expect(screen.queryByRole('button', { name: 'Apply' })).not.toBeInTheDocument()
    expect(screen.queryByText(/^Results for/)).not.toBeInTheDocument()
    expect(searchApi.create).not.toHaveBeenCalled()
    expect(searchApi.get).not.toHaveBeenCalled()
  })

  it.each([
    ['blank', '%20%20'],
    ['empty', ''],
  ])('treats a %s ?q= as no search, and shows how to start one', (_, line) => {
    // Arrange
    const path = `/search?q=${line}`

    // Act
    renderPage({ path })

    // Assert
    expect(screen.getByText('Type a title in the search field at the top, then press Enter.')).toBeInTheDocument()
    expect(searchApi.create).not.toHaveBeenCalled()
  })

  it('makes the search in ?q= at once, a bracketed year ending it as the year, and puts its id in the address in its place', async () => {
    // Arrange
    const path = searchFor('  Aliens (1986)  ')

    // Act
    renderPage({ path })

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: '1986' })
    expect(addressSearch()).toBe(`?id=${searchId}`)
    expect(screen.getByRole('heading', { level: 2, name: 'In your library' })).toBeInTheDocument()
    expect(vi.mocked(searchApi.releases).mock.calls).toEqual([[searchId, firstQuery]])
    // The answer went into the cache: the search isn't asked for again.
    expect(searchApi.get).not.toHaveBeenCalled()
  })

  it('makes the search in ?q= once, though StrictMode runs the page\'s effects twice', async () => {
    // Arrange
    const entries = [searchFor('Aliens')]

    // Act
    renderInHistory(entries, { strict: true })

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(address()).toBe(`/search?id=${searchId}`)
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens' })
  })

  it('sends a bare id typed in the header as the title, as the header has no field for ids', async () => {
    // Arrange
    const path = searchFor('tt0816692')

    // Act
    renderPage({ path })

    // Assert
    await vi.waitFor(() => expect(addressSearch()).toBe(`?id=${searchId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'tt0816692' })
  })

  it('makes the search in ?q= narrowed by the TMDb id beside it, as the Recommendations page links a film', async () => {
    // Arrange
    const path = '/search?q=Guardians%20of%20the%20Galaxy%20Vol.%203%20(2023)&tmdbId=447365'

    // Act
    renderPage({ path })

    // Assert
    await vi.waitFor(() => expect(addressSearch()).toBe(`?id=${searchId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Guardians of the Galaxy Vol. 3', year: '2023', tmdbId: '447365' })
  })

  it('shows the TMDb id of the address in its field while the search is made', () => {
    // Arrange
    vi.mocked(searchApi.create).mockReturnValue(new Promise<SearchResponse>(() => {}))
    const path = '/search?q=Alien%20(1979)&tmdbId=348'

    // Act
    renderPage({ path })

    // Assert
    expect(screen.getByRole('textbox', { name: 'TMDb id (optional)' })).toHaveValue('348')
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('1979')
  })

  it.each([
    ['letters', 'abc'],
    ['an IMDb id', 'tt0090605'],
    ['a negative number', '-5'],
    ['nothing', ''],
  ])('leaves out a ?tmdbId= of %s, which is no TMDb id, and searches the title alone', async (_, tmdbId) => {
    // Arrange
    const path = `/search?q=Aliens%20(1986)&tmdbId=${tmdbId}`

    // Act
    renderPage({ path })

    // Assert
    await vi.waitFor(() => expect(addressSearch()).toBe(`?id=${searchId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: '1986' })
  })

  it('ignores a ?tmdbId= without ?q=, and asks nothing', () => {
    // Arrange
    const path = '/search?tmdbId=447365'

    // Act
    renderPage({ path })

    // Assert
    expect(screen.getByText('Type a title in the search field at the top, then press Enter.')).toBeInTheDocument()
    expect(searchApi.create).not.toHaveBeenCalled()
  })

  it('ignores ?q= in an address that names a search, and shows that search', async () => {
    // Arrange
    const path = `/search?id=${searchId}&q=Interstellar`

    // Act
    renderPage({ path })

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(resultsFor('Aliens')).toBeInTheDocument()
    expect(searchApi.create).not.toHaveBeenCalled()
    expect(searchApi.get).toHaveBeenCalledExactlyOnceWith(searchId)
  })

  it('goes Back from a search made from ?q= to the page before it, never to the ?q= that would ask again', async () => {
    // Arrange
    const { user } = renderInHistory(['/library', searchFor('Aliens')])
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${searchId}`))

    // Act
    await user.click(screen.getByRole('button', { name: 'Back' }))

    // Assert
    expect(await screen.findByText('The Library page')).toBeInTheDocument()
    expect(address()).toBe('/library')
    expect(searchApi.create).toHaveBeenCalledOnce()
  })

  it('moves to the answer of the newest ?q= only, when the person searched again before the first answered', async () => {
    // Arrange
    const firstId = '5ea4c000-0000-4000-8000-000000000002'
    let answerFirst: (search: SearchResponse) => void = () => {}
    let answerSecond: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create)
      .mockReturnValueOnce(new Promise((resolve) => { answerFirst = resolve }))
      .mockReturnValueOnce(new Promise((resolve) => { answerSecond = resolve }))
    const { client, user } = renderInHistory([searchFor('Alien')], { links: [searchFor('Aliens')] })
    await user.click(screen.getByRole('link', { name: searchFor('Aliens') }))
    await vi.waitFor(() => expect(searchApi.create).toHaveBeenCalledTimes(2))

    // Act
    answerFirst(searchOf({ id: firstId, text: 'Alien', year: null, imdbId: null }))

    // Assert
    // The first answer still lands in the cache, but the page stays on the newer line.
    await vi.waitFor(() => expect(client.getQueryData(qk.search.detail(firstId))).toBeDefined())
    expect(address()).toBe(searchFor('Aliens'))
    expect(resultsFor('Aliens')).toBeInTheDocument()
    answerSecond(aliensSearch)
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${searchId}`))
    expect(vi.mocked(searchApi.create).mock.calls).toEqual([[{ text: 'Alien' }], [{ text: 'Aliens' }]])
  })

  it('keeps the search Back returned to on screen, not searching, when the ?q= search left behind answers late', async () => {
    // Arrange
    const lateId = '5ea4c000-0000-4000-8000-000000000002'
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { client, user } = renderInHistory([`/search?id=${searchId}`], { links: [searchFor('Alien')] })
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.click(screen.getByRole('link', { name: searchFor('Alien') }))
    await vi.waitFor(() => expect(searchApi.create).toHaveBeenCalledOnce())
    await user.click(screen.getByRole('button', { name: 'Back' }))
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    expect(noteBeside('Aliens')).toHaveTextContent('Add a year, a season or an id to narrow them.')
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled()

    // Act
    answer(searchOf({ id: lateId, text: 'Alien', year: null, imdbId: null }))

    // Assert
    await vi.waitFor(() => expect(client.getQueryData(qk.search.detail(lateId))).toBeDefined())
    expect(address()).toBe(`/search?id=${searchId}`)
    expect(resultsFor('Aliens')).toBeInTheDocument()
  })

  it('moves to the answer that came while the person was Back when they go Forward to its ?q=, without asking again', async () => {
    // Arrange
    const lateId = '5ea4c000-0000-4000-8000-000000000002'
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { client, user } = renderInHistory([`/search?id=${searchId}`], { links: [searchFor('Alien')] })
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.click(screen.getByRole('link', { name: searchFor('Alien') }))
    await vi.waitFor(() => expect(searchApi.create).toHaveBeenCalledOnce())
    await user.click(screen.getByRole('button', { name: 'Back' }))
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${searchId}`))
    answer(searchOf({ id: lateId, text: 'Alien', year: null, imdbId: null }))
    await vi.waitFor(() => expect(client.getQueryData(qk.search.detail(lateId))).toBeDefined())

    // Act
    await user.click(screen.getByRole('button', { name: 'Forward' }))

    // Assert
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${lateId}`))
    expect(resultsFor('Alien')).toBeInTheDocument()
    expect(searchApi.create).toHaveBeenCalledOnce()
    // The answer is in the cache.
    expect(searchApi.get).not.toHaveBeenCalledWith(lateId)
    // It replaced the ?q= entry, as an answer on screen does.
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(address()).toBe(`/search?id=${searchId}`)
  })

  it('waits for the search of a ?q= the person went Back from and Forward to again before it answered', async () => {
    // Arrange
    const lateId = '5ea4c000-0000-4000-8000-000000000002'
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { user } = renderInHistory([`/search?id=${searchId}`], { links: [searchFor('Alien')] })
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.click(screen.getByRole('link', { name: searchFor('Alien') }))
    await vi.waitFor(() => expect(searchApi.create).toHaveBeenCalledOnce())
    await user.click(screen.getByRole('button', { name: 'Back' }))
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${searchId}`))

    // Act
    await user.click(screen.getByRole('button', { name: 'Forward' }))

    // Assert
    expect(noteBeside('Alien')).toHaveTextContent(/^Searching…$/)
    expect(searchApi.create).toHaveBeenCalledOnce()
    answer(searchOf({ id: lateId, text: 'Alien', year: null, imdbId: null }))
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${lateId}`))
  })

  it('asks again for a ?q= the server refused when the person goes Back and then Forward to it', async () => {
    // Arrange
    const nextId = '5ea4c000-0000-4000-8000-000000000002'
    vi.mocked(searchApi.create)
      .mockRejectedValueOnce(apiError(500, 'Database is locked.'))
      .mockResolvedValueOnce(searchOf({ id: nextId, text: 'Alien', year: null, imdbId: null }))
    const { user } = renderInHistory([`/search?id=${searchId}`], { links: [searchFor('Alien')] })
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.click(screen.getByRole('link', { name: searchFor('Alien') }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Database is locked.')
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    // Act
    await user.click(screen.getByRole('button', { name: 'Forward' }))

    // Assert
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${nextId}`))
    expect(vi.mocked(searchApi.create).mock.calls).toEqual([[{ text: 'Alien' }], [{ text: 'Alien' }]])
    expect(resultsFor('Alien')).toBeInTheDocument()
  })

  it('says what is being searched for, Searching… in place of how to narrow it, and disables Apply while the search is made', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockReturnValue(new Promise(() => {}))

    // Act
    renderPage({ path: searchFor('Interstellar (2014)') })

    // Assert
    expect(await screen.findByText('Searching…')).toBeInTheDocument()
    expect(noteBeside('Interstellar')).toHaveTextContent(/^Searching…$/)
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled()
    // The year the line ended with fills the field it went to.
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('2014')
    expect(screen.getByRole('textbox', { name: 'IMDb id (optional)' })).toHaveValue('')
  })

  it('names the search on screen in bold and says how to narrow it', async () => {
    // Arrange
    const path = `/search?id=${searchId}`

    // Act
    renderPage({ path })

    // Assert
    const line = await screen.findByText((_, element) => element?.textContent === 'Results for “Aliens”.')
    expect(within(line).getByText('Aliens').closest('strong')).toHaveTextContent('“Aliens”')
    expect(noteBeside('Aliens')).toHaveTextContent(/^Add a year, a season or an id to narrow them\.$/)
  })

  it('shows a title that looks like markup or a placeholder exactly as it was typed', async () => {
    // Arrange
    const typed = '<strong>Alien</strong> {{text}} <0>Aliens</0>'
    vi.mocked(searchApi.create).mockReturnValue(new Promise(() => {}))

    // Act
    renderPage({ path: searchFor(typed) })

    // Assert
    const line = await screen.findByText((_, element) => element?.textContent === `Results for “${typed}”.`)
    expect(line.querySelectorAll('strong')).toHaveLength(1)
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: typed })
  })

  it('shows the server\'s sentence in a banner under the form when it refuses the search, and stays where it was', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockRejectedValue(apiError(400, 'The title is at most 200 characters.'))

    // Act
    renderPage({ path: searchFor('Aliens') })

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('The title is at most 200 characters.')
    expect(screen.getByRole('button', { name: 'Apply' }).compareDocumentPosition(banner)).toBe(Node.DOCUMENT_POSITION_FOLLOWING)
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens' })
    expect(addressSearch()).toBe('?q=Aliens')
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled()
  })

  it('clears the refusal\'s banner on Dismiss', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockRejectedValue(apiError(400, 'The title is at most 200 characters.'))
    const { user } = renderPage({ path: searchFor('Aliens') })
    const banner = await screen.findByRole('alert')

    // Act
    await user.click(within(banner).getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(resultsFor('Aliens')).toBeInTheDocument()
  })

  it('falls back to its own sentence when the refusal has none', async () => {
    // Arrange
    vi.mocked(searchApi.create).mockRejectedValue(apiError(500))

    // Act
    renderPage({ path: searchFor('Aliens') })

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not search.')
  })

  it('stays on the page the person went to when the search they left finishes', async () => {
    // Arrange
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { client, user } = renderInHistory([searchFor('Aliens')])
    await screen.findByText('Searching…')
    await user.click(screen.getByRole('link', { name: 'Library' }))
    await screen.findByText('The Library page')

    // Act
    answer(aliensSearch)

    // Assert
    // The answer still lands in the cache; only the move to it is skipped.
    await vi.waitFor(() => expect(client.getQueryData(qk.search.detail(searchId))).toEqual(aliensSearch))
    expect(address()).toBe('/library')
    expect(screen.getByText('The Library page')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 1, name: 'Search' })).not.toBeInTheDocument()
  })

  it('brings the search in the address back into an untouched form', async () => {
    // Arrange
    const path = `/search?id=${searchId}`

    // Act
    renderPage({ path })

    // Assert
    expect(await screen.findByDisplayValue('1986')).toBe(screen.getByRole('textbox', { name: 'Year (optional)' }))
    expect(screen.getByRole('textbox', { name: 'IMDb id (optional)' })).toHaveValue('tt0090605')
    expect(screen.getByRole('textbox', { name: 'TMDb id (optional)' })).toHaveValue('')
    expect(searchApi.get).toHaveBeenCalledExactlyOnceWith(searchId)
  })

  it('keeps what the person typed when the search on screen is read again', async () => {
    // Arrange
    const { client, user } = openStored()
    const year = await screen.findByRole('textbox', { name: 'Year (optional)' })
    await user.clear(year)
    await user.type(year, '1979')
    vi.mocked(searchApi.get).mockResolvedValue(searchOf({ year: 1990 }))

    // Act
    await client.refetchQueries({ queryKey: qk.search.detail(searchId) })

    // Assert
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('1979')
    expect(screen.getByRole('textbox', { name: 'IMDb id (optional)' })).toHaveValue('tt0090605')
  })

  it('keeps what the person typed while the ?q= search was made, once the page moves to it', async () => {
    // Arrange
    let answer: (search: SearchResponse) => void = () => {}
    vi.mocked(searchApi.create).mockReturnValue(new Promise((resolve) => { answer = resolve }))
    const { user } = renderPage({ path: searchFor('Aliens') })
    await screen.findByText('Searching…')
    await user.type(screen.getByRole('textbox', { name: 'Year (optional)' }), '1979')

    // Act
    answer(searchOf({ year: null, imdbId: null }))

    // Assert
    await vi.waitFor(() => expect(addressSearch()).toBe(`?id=${searchId}`))
    expect(resultsFor('Aliens')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('1979')
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled()
  })

  it('fills the form from the next search after the person changed it and searched again from the header', async () => {
    // Arrange
    const nextId = '5ea4c000-0000-4000-8000-000000000002'
    vi.mocked(searchApi.create).mockResolvedValue(searchOf({ id: nextId, text: 'Alien', year: 1979, imdbId: null }))
    const { user } = renderInHistory([`/search?id=${searchId}`], { links: [searchFor('Alien')] })
    const year = await screen.findByRole('textbox', { name: 'Year (optional)' })
    await user.clear(year)
    await user.type(year, '2000')

    // Act
    await user.click(screen.getByRole('link', { name: searchFor('Alien') }))

    // Assert
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${nextId}`))
    expect(resultsFor('Alien')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('1979')
    expect(screen.getByRole('textbox', { name: 'IMDb id (optional)' })).toHaveValue('')
  })

  it('narrows the search on screen with its own title and the fields, as a new history entry', async () => {
    // Arrange
    const nextId = '5ea4c000-0000-4000-8000-000000000002'
    vi.mocked(searchApi.create).mockResolvedValue(searchOf({ id: nextId, year: 1987 }))
    const { user } = renderInHistory([`/search?id=${searchId}`])
    const year = await screen.findByRole('textbox', { name: 'Year (optional)' })
    await user.clear(year)
    await user.type(year, ' 1987 ')
    await user.type(screen.getByRole('textbox', { name: 'TMDb id (optional)' }), '679')

    // Act
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    // Assert
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${nextId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: '1987', imdbId: 'tt0090605', tmdbId: '679' })
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(address()).toBe(`/search?id=${searchId}`)
  })

  it('narrows a ?q= search the server refused in place of its entry, so Back skips it', async () => {
    // Arrange
    vi.mocked(searchApi.create)
      .mockRejectedValueOnce(apiError(500, 'Database is locked.'))
      .mockResolvedValueOnce(aliensSearch)
    const { user } = renderInHistory(['/library', searchFor('Aliens (19x6)')])
    await screen.findByRole('alert')
    const year = screen.getByRole('textbox', { name: 'Year (optional)' })
    await user.type(year, '1986')

    // Act
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    // Assert
    await vi.waitFor(() => expect(address()).toBe(`/search?id=${searchId}`))
    expect(vi.mocked(searchApi.create).mock.calls).toEqual([
      [{ text: 'Aliens (19x6)' }],
      [{ text: 'Aliens (19x6)', year: '1986', imdbId: undefined, tmdbId: undefined }],
    ])
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(address()).toBe('/library')
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
    // The Library page's card as it is: its identifiers and its versions, each summed up in its row.
    expect(within(cards[0]).getByRole('list', { name: 'Identifiers' })).toBeInTheDocument()
    expect(within(cards[0]).getByRole('button', { name: /^Theatrical Cut/ })).toHaveTextContent('1080p')
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
    [1, '1 more work in your library holds these words.'],
    [35, '35 more works in your library hold these words.'],
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
    ['kind', 'Kind', 'Movies', { kind: 'Movie' as const }],
    ['audio language', 'Audio language', 'Ukrainian', { language: 'ukr' }],
    ['unknown audio language', 'Audio language', 'Unknown', { language: 'unknown' }],
    ['quality', 'Quality', '2160p', { quality: '2160' }],
    ['unknown quality', 'Quality', 'Unknown', { quality: 'unknown' }],
    ['order', 'Sort by', 'Newest', { sort: 'newest' as const }],
  ])('sends the %s to the server and starts again at page 1', async (_, name, option, changes) => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.page === 1 ? [tolokaRemux] : [otherUhd], 30))
    const { user } = openStored()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name }), option)

    // Assert
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, ...changes })
    expect(vi.mocked(searchApi.releases).mock.calls.map(([id]) => id)).toEqual([searchId, searchId, searchId])
  })

  it('sends the size bounds in bytes once typing pauses, from page 1', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.page === 1 ? [tolokaRemux] : [otherUhd], 30))
    const { user } = openStored()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Smallest size in GiB' }), '4.5')
    await user.type(screen.getByRole('textbox', { name: 'Largest size in GiB' }), '30')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQuery, minSizeBytes: '4831838208', maxSizeBytes: '32212254720' }))
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
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
    const state = await screen.findByRole('alert')
    expect(state).toHaveTextContent('Could not load the releases.')
    expect(state).toHaveTextContent('The smallest size can\'t be larger than the largest.')
    // The same bounds would be refused again: the filters above are the way on, not Try again.
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(releaseTitles()).toEqual([])
    expect(screen.getByRole('textbox', { name: 'Largest size in GiB' })).toHaveValue('4')
  })

  it('says no release matches the filters, and Clear filters brings every release back in the same order', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.source ? [] : [tolokaRemux, otherUhd]))
    const { user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort by' }), 'Largest')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Source' }), 'Toloka')
    await screen.findByText('No release matches these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, sort: 'largest' })
    expect(screen.getByRole('combobox', { name: 'Source' })).toHaveValue('')
    expect(screen.getByRole('combobox', { name: 'Sort by' })).toHaveValue('largest')
    expect(screen.queryByText('No release matches these filters.')).not.toBeInTheDocument()
  })

  it('pages through the releases on the server and keeps the page size on this device', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.page === 1 ? [tolokaRemux] : [otherUhd], 60))
    const { user } = openStored()
    await screen.findByText('1–25 of 60')

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByRole('heading', { level: 3, name: otherUhd.title })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument()
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
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(searchApi.releases).not.toHaveBeenCalled()
    // No title to narrow: searching again starts in the header.
    expect(screen.queryByRole('button', { name: 'Apply' })).not.toBeInTheDocument()
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
    expect(screen.queryByRole('button', { name: 'Apply' })).not.toBeInTheDocument()
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
    expect(banner).toHaveClass('ui-banner')
    expect(screen.getByRole('heading', { level: 3, name: 'Чужі' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
  })

  it('asks for the search again from Retry on its refresh banner, and drops the banner once it is back', async () => {
    // Arrange
    const { client, user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    vi.mocked(searchApi.get).mockRejectedValueOnce(apiError(500, 'Database is locked.'))
    await client.refetchQueries({ queryKey: qk.search.detail(searchId) })
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { level: 3, name: 'Чужі' })).toBeInTheDocument()
  })

  it('says the search could not be loaded, and Try again loads it', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockRejectedValueOnce(networkError())
    const { user } = openStored()
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load this search.')
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
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

  it('asks for the releases again from Retry on their refresh banner', async () => {
    // Arrange
    const { client, user } = openStored()
    await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })
    vi.mocked(searchApi.releases).mockRejectedValueOnce(apiError(500, 'Database is locked.'))
    await client.refetchQueries({ queryKey: qk.search.releases(searchId, firstQuery) })
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(releaseTitles()).toEqual([tolokaRemux.title, otherUhd.title])
  })

  it('says the releases could not be loaded under the source lines, and Try again loads them', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockRejectedValueOnce(networkError())
    const { user } = openStored()
    const releases = within(await screen.findByRole('region', { name: 'Available releases' }))
    const state = await releases.findByRole('alert')

    // Act
    await user.click(releases.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the releases.')
    expect(state).toHaveTextContent('The server could not be reached.')
    expect(await screen.findByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says the search is no longer available when its releases answer 404', async () => {
    // Arrange
    vi.mocked(searchApi.releases).mockRejectedValue(apiError(404))

    // Act
    openStored()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('This search is no longer available. Search again.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
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

  it('opens the Download dialog for the release whose Download was clicked, and closes it again', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(localClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf(null))
    const { user } = openStored()
    const row = await screen.findByRole('article', { name: otherUhd.title })

    // Act
    await user.click(within(row).getByRole('button', { name: 'Download' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: `Download ${otherUhd.title}` })
    expect(await within(dialog).findByRole('combobox', { name: 'Download client' })).toHaveValue(localClient.id)
    expect(downloadsApi.options).toHaveBeenCalledExactlyOnceWith(otherUhd.id)
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
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
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    // Assert
    await vi.waitFor(() => expect(addressSearch()).toBe(`?id=${nextId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Aliens', year: undefined, imdbId: undefined, tmdbId: undefined })
    await vi.waitFor(() => expect(vi.mocked(searchApi.releases).mock.lastCall).toEqual([nextId, firstQuery]))
    expect(screen.getByRole('combobox', { name: 'Source' })).toHaveValue('')
  })
})
