import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { libraryApi } from '@/lib/library-api'
import { qk } from '@/lib/queries'
import type { CurrentUser, LibraryWork } from '@/lib/types'
import { LibraryPage } from '@/pages/LibraryPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { aliens, continental, locations, sourceOf, sourcesOf, unknownWork, worksPage } from '@/test/library'

vi.mock('@/lib/library-api')

/** What the last request for the works asked for. */
const lastQuery = () => vi.mocked(libraryApi.works).mock.lastCall?.[0]

/** The titles of the work cards on screen, in order. */
const cardTitles = () =>
  screen.queryAllByRole('article').map((card) => within(card).getByRole('heading', { level: 2 }).textContent)

const firstQuery = { search: undefined, locationId: undefined, identifiers: undefined, page: 1, pageSize: 25 }

function renderPage(signedIn: CurrentUser = owner, path = '/library') {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

describe('LibraryPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, [aliens, continental, unknownWork]))
    vi.mocked(libraryApi.locations).mockResolvedValue(locations)
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf(sourceOf({})))
  })

  it('lists the works of the first page as cards, under the title and the sources', async () => {
    // Arrange
    const signedIn = member

    // Act
    renderPage(signedIn)

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'Library' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(cardTitles()).toEqual(['Чужі', 'The Continental', 'Unknown title'])
    expect(lastQuery()).toEqual(firstQuery)
    expect(screen.getByRole('heading', { level: 2, name: 'Sources' })).toBeInTheDocument()
    expect(screen.getByText('1–3 of 3')).toBeInTheDocument()
  })

  it('says Loading… while the first page is on its way', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockReturnValue(new Promise(() => {}))

    // Act
    renderPage()

    // Assert
    await screen.findByText('Home Jellyfin')
    expect(screen.getAllByText('Loading…')).toHaveLength(1)
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it('asks the server for the search once typing pauses and starts again at page 1', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.search ? [aliens] : query.page === 1 ? [continental] : [unknownWork], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.type(screen.getByRole('searchbox', { name: 'Search the library' }), ' чужі ')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...firstQuery, search: 'чужі' }))
    expect(vi.mocked(libraryApi.works).mock.calls.map(([query]) => query)).toEqual([
      firstQuery,
      { ...firstQuery, page: 2 },
      { ...firstQuery, search: 'чужі' },
    ])
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(screen.getByText('1–25 of 30')).toBeInTheDocument()
  })

  it('opens searched for the text in the address, as the Search page links to it, and asks the server for that alone', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, query.search ? [aliens] : [aliens, continental]))
    const path = '/library?search=Rambo%3A+First+Blood'

    // Act
    renderPage(owner, path)

    // Assert
    expect(screen.getByRole('searchbox', { name: 'Search the library' })).toHaveValue('Rambo: First Blood')
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(cardTitles()).toEqual(['Чужі'])
    expect(vi.mocked(libraryApi.works).mock.calls.map(([query]) => query)).toEqual([{ ...firstQuery, search: 'Rambo: First Blood' }])
  })

  it('filters by library on the server and starts again at page 1', async () => {
    // Arrange
    const films = locations.items[1]
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      query.locationId ? worksPage(query, [aliens]) : worksPage(query, query.page === 1 ? [continental] : [unknownWork], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Library' }), 'Фільми')

    // Assert
    expect(await screen.findByText('1–1 of 1')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, locationId: films.id })
    expect(cardTitles()).toEqual(['Чужі'])
    expect([...screen.getByRole('combobox', { name: 'Library' }).querySelectorAll('option')].map((option) => option.textContent))
      .toEqual(['All libraries', 'Мультфільми', 'Фільми'])
  })

  it('lets go of a library filter once the refreshed libraries no longer list it', async () => {
    // Arrange — a sync emptied Мультфільми, so the libraries asked for again leave it out.
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.locationId ? [] : [aliens, continental, unknownWork]))
    const { client, user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Library' }), 'Мультфільми')
    await screen.findByText('No movies match these filters.')
    const filteredQuery = lastQuery()
    vi.mocked(libraryApi.locations).mockResolvedValue({ items: [locations.items[1]] })

    // Act
    await client.refetchQueries({ queryKey: qk.library.locations })

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    await vi.waitFor(() => expect(lastQuery()).toEqual(firstQuery))
    expect(filteredQuery).toEqual({ ...firstQuery, locationId: locations.items[0].id })
    const select = screen.getByRole('combobox', { name: 'Library' })
    expect(select).toHaveValue('')
    expect([...select.querySelectorAll('option')].map((option) => option.textContent)).toEqual(['All libraries', 'Фільми'])
    expect(screen.queryByText('No movies match these filters.')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument()
  })

  it('says why the library filter offers no library when the libraries can\'t be loaded', async () => {
    // Arrange
    vi.mocked(libraryApi.locations).mockRejectedValue(networkError())

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('alert'))
      .toHaveTextContent('Could not load the libraries to filter by. The server could not be reached.')
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect([...screen.getByRole('combobox', { name: 'Library' }).querySelectorAll('option')].map((option) => option.textContent))
      .toEqual(['All libraries'])
  })

  it('asks for the libraries again from Retry on that banner, and offers them once they come', async () => {
    // Arrange
    vi.mocked(libraryApi.locations).mockRejectedValueOnce(networkError())
    const { user } = renderPage()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByRole('combobox', { name: 'Library' }).querySelectorAll('option').length).toBeGreaterThan(1)
  })

  it('names the integration beside a library name two integrations share', async () => {
    // Arrange
    vi.mocked(libraryApi.locations).mockResolvedValue({
      items: [
        { id: '10c00000-0000-4000-8000-000000000002', name: 'Фільми', sourceName: 'Home Jellyfin' },
        { id: '10c00000-0000-4000-8000-000000000003', name: 'Фільми', sourceName: 'Office Jellyfin' },
        { id: '10c00000-0000-4000-8000-000000000004', name: 'Мультфільми', sourceName: 'Office Jellyfin' },
      ],
    })

    // Act
    renderPage()

    // Assert
    const select = screen.getByRole('combobox', { name: 'Library' })
    await within(select).findByRole('option', { name: 'Фільми (Office Jellyfin)' })
    expect([...select.querySelectorAll('option')].map((option) => option.textContent))
      .toEqual(['All libraries', 'Фільми (Home Jellyfin)', 'Фільми (Office Jellyfin)', 'Мультфільми'])
  })

  it('asks the server for the works missing an identifier and starts again at page 1', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      query.identifiers ? worksPage(query, [continental, unknownWork]) : worksPage(query, [aliens], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Identifiers' }), 'Missing an IMDb or TMDb id')

    // Assert
    expect(await screen.findByText('1–2 of 2')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, identifiers: 'Missing' })
    expect(cardTitles()).toEqual(['The Continental', 'Unknown title'])
  })

  it('remembers the page size on this device', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, [aliens], 120))
    const { user } = renderPage()
    await screen.findByText('1–25 of 120')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–50 of 120')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('1–50 of 120')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ ...firstQuery, pageSize: 50 })
    expect(localStorage.getItem('mam:library:pageSize')).toBe('50')
  })

  it('says when nothing matches, and Clear filters shows everything again', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.search || query.locationId || query.identifiers ? [] : [aliens]))
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Identifiers' }), 'Missing')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Library' }), 'Мультфільми')
    await user.type(screen.getByRole('searchbox', { name: 'Search the library' }), 'zz')
    await screen.findByText('No movies match these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(lastQuery()).toEqual(firstQuery)
    expect(screen.getByRole('searchbox', { name: 'Search the library' })).toHaveValue('')
    expect(screen.getByRole('combobox', { name: 'Library' })).toHaveValue('')
    expect(screen.getByRole('combobox', { name: 'Identifiers' })).toHaveValue('')
    expect(screen.queryByText('No movies match these filters.')).not.toBeInTheDocument()
  })

  it('says there are no movies yet while the library is empty, before or after a sync', async () => {
    // Arrange — a sync that found nothing: the empty state must not contradict "Synced …" above it.
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, []))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('No movies yet. Once a media library is synced, its movies are listed here.'))
      .toBeInTheDocument()
    expect(await screen.findByText(/^Synced /)).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument()
  })

  it('moves to the last page when a refresh finds fewer works than the page on screen needs', async () => {
    // Arrange — a sync removed five movies while page 2 was on screen.
    let secondPage: LibraryWork[] = [unknownWork]
    let total = 30
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.page === 1 ? [aliens, continental] : secondPage, total))
    const { client, user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')
    secondPage = []
    total = 25

    // Act
    await client.refetchQueries()

    // Assert
    expect(await screen.findByText('1–25 of 25')).toBeInTheDocument()
    expect(lastQuery()).toEqual(firstQuery)
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
  })

  it('keeps the cards on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    vi.mocked(libraryApi.works).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(/^Refresh failed, showing the last loaded movies\./)
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(cardTitles()).toEqual(['Чужі', 'The Continental', 'Unknown title'])
  })

  it('asks again from Retry on the refresh banner, and drops the banner once the movies are back', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    vi.mocked(libraryApi.works).mockRejectedValueOnce(networkError())
    await client.refetchQueries({ queryKey: qk.library.worksAll })
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(cardTitles()).toEqual(['Чужі', 'The Continental', 'Unknown title'])
  })

  it('shows the API sentence in place of the list when the first page is refused, with no Try again', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockRejectedValue(apiError(400, '“x” is not a library.'))

    // Act
    renderPage()

    // Assert
    const state = await screen.findByRole('alert')
    expect(state).toHaveTextContent('Could not load the library.')
    expect(state).toHaveTextContent('“x” is not a library.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('names the failure when the first page fails without a sentence, and Try again loads the cards', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockRejectedValueOnce(apiError(500))
    const { user } = renderPage()
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the library.')
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('lists the works while the sources can\'t be loaded', async () => {
    // Arrange
    vi.mocked(libraryApi.sources).mockRejectedValue(networkError())

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent('The server could not be reached.')
    expect(cardTitles()).toEqual(['Чужі', 'The Continental', 'Unknown title'])
  })
})
