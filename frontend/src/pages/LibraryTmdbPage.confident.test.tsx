import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import { qk } from '@/lib/queries'
import { LibraryTmdbPage } from '@/pages/LibraryTmdbPage'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import {
  batchId,
  batchItemOf,
  batchOf,
  confidentCalypso,
  confidentPage,
  catchUpOf,
  confidentStripling,
  filmsCount,
  linksPage,
  penguins,
  penguinsShow,
  queueOf,
  suggestionRunOf,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb page's Confident tab (R24): every confident suggestion a page at a time with each
// library's counts, every row ticked, "Link N works" in one click, and then the rows as they were
// clicked, each following its write every 2 s until none waits or is being written. A ticked series is
// linked in the same click as the films, with no question.

/** The series and its confident show. */
const confidentPenguins = { work: penguins, candidate: { ...penguinsShow, isConfident: true } }

function renderPage() {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={['/library/tmdb?tab=Confident']}>
          <Routes>
            <Route path="/library/tmdb" element={<LibraryTmdbPage />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

const rows = () => screen.getByRole('list', { name: 'Confident suggestions' })
/** The rows themselves, not the evidence badges' items inside them. */
const rowCount = () => rows().querySelectorAll(':scope > li').length
const box = (name: string) => screen.getByRole('checkbox', { name })

describe('LibraryTmdbPage Confident tab', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockResolvedValue(queueOf(0, 0, null))
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [confidentStripling, confidentCalypso]))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, []))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('lists each library\'s counts and the confident suggestions, every row ticked', async () => {
    // Arrange
    renderPage()

    // Act
    const counts = within(await screen.findByRole('list', { name: 'Works without a TMDb id, by library' })).getAllByRole('listitem')

    // Assert
    expect(counts.map((count) => count.textContent)).toEqual(['Мультфільми: 168 confident of 240', 'Фільми: 3 confident of 12'])
    expect(rowCount()).toBe(2)
    expect(box('120. Landing Stripling (1962)')).toBeChecked()
    expect(box('121. Calypso Cat (1962)')).toBeChecked()
    expect(within(rows()).getByText('Посадка Стріплінга (1962)')).toBeInTheDocument()
    expect(within(rows()).getAllByText('6 min ≈ 6 min')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Link 2 works' })).toBeEnabled()
    expect(libraryLinksApi.confident).toHaveBeenCalledExactlyOnceWith({ locationId: undefined, page: 1, pageSize: 25 })
  })

  it('counts only the ticked rows, and says why Link is off with none ticked', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(box('121. Calypso Cat (1962)'))
    const one = screen.queryByRole('button', { name: 'Link 1 work' }) !== null
    await user.click(box('120. Landing Stripling (1962)'))

    // Assert
    expect(one).toBe(true)
    const none = screen.getByRole('button', { name: 'Link 0 works' })
    expect(none).toBeDisabled()
    expect(none.parentElement).toHaveAttribute('title', 'Tick at least one work.')
  })

  it('links the ticked rows in one click, then keeps them on screen, each following its write until none is under way', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Queued')]))
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Writing')]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })
    await user.click(box('121. Calypso Cat (1962)'))

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 1 work' }))
    const queued = await screen.findByText('Waiting to be written')
    await nextPoll()
    await screen.findByText('Writing…', { selector: '.ui-badge' })
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Written')]))
    await nextPoll()
    await screen.findByText('Written to Jellyfin')
    await nextPoll()
    await nextPoll()

    // Assert
    expect(queued).toHaveClass('ui-badge')
    expect(libraryLinksApi.acceptConfident).toHaveBeenCalledExactlyOnceWith([
      { workId: confidentStripling.work.id, catalogKind: 'Movie', catalogId: '223021' },
    ])
    expect(libraryLinksApi.batch).toHaveBeenCalledTimes(2)
    expect(libraryLinksApi.batch).toHaveBeenCalledWith(batchId)
    expect(screen.getByText('1 of 1 work linked.')).toBeInTheDocument()
    // The rows as they were clicked: the list wasn't asked for again under them.
    expect(libraryLinksApi.confident).toHaveBeenCalledOnce()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('links a ticked series in the same click as the films, with no question', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [confidentStripling, confidentPenguins]))
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Queued'),
      batchItemOf(penguins, 'Queued'),
    ]))
    vi.mocked(libraryLinksApi.batch).mockImplementation(() => new Promise(() => {}))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))

    // Assert
    expect(libraryLinksApi.acceptConfident).toHaveBeenCalledExactlyOnceWith([
      { workId: confidentStripling.work.id, catalogKind: 'Movie', catalogId: '223021' },
      { workId: penguins.id, catalogKind: 'Series', catalogId: '7869' },
    ])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await screen.findAllByText('Waiting to be written')).toHaveLength(2)
  })

  it('says how far the writes are while they are under way', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Written'),
      batchItemOf(confidentCalypso.work, 'Queued'),
    ]))
    vi.mocked(libraryLinksApi.batch).mockImplementation(() => new Promise(() => {}))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))

    // Assert
    expect(await screen.findByText('Writing the ids into Jellyfin, one work at a time: 1 of 2…')).toBeInTheDocument()
  })

  it('shows a write Jellyfin refused with its sentence, and a work skipped with why', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'NotWritten', { writeError: 'Jellyfin refused to change the item\'s ids.' }),
      { workId: confidentCalypso.work.id, outcome: 'Skipped', reason: 'The suggestion is no longer confident.', links: [] },
    ]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))

    // Assert
    expect(await screen.findByText('Not in Jellyfin yet')).toBeInTheDocument()
    expect(screen.getByText('Jellyfin refused to change the item\'s ids.', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('Not linked: The suggestion is no longer confident.')).toBeInTheDocument()
    expect(screen.getByText('1 of 2 works linked.')).toBeInTheDocument()
    expect(libraryLinksApi.batch).not.toHaveBeenCalled()
  })

  it('keeps a skipped work\'s reason once a poll brings the linked works\' writes', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Queued'),
      { workId: confidentCalypso.work.id, outcome: 'Skipped', reason: 'The suggestion is no longer confident.', links: [] },
    ]))
    // A poll is rebuilt from the stored links: the skipped work, never stored, isn't in it.
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Written')]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))
    await screen.findByText('Waiting to be written')
    await nextPoll()
    await screen.findByText('Written to Jellyfin')

    // Assert
    expect(libraryLinksApi.batch).toHaveBeenCalledOnce()
    expect(screen.getByText('Not linked: The suggestion is no longer confident.')).toBeInTheDocument()
    expect(screen.getByText('1 of 2 works linked.')).toBeInTheDocument()
  })

  it('follows each link by its id, under whichever work a sync put it, and says when its item is gone', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const stripling = batchItemOf(confidentStripling.work, 'Queued')
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([stripling, batchItemOf(confidentCalypso.work, 'Queued')]))
    // A sync meanwhile joined the short's work with another, and no longer found Calypso Cat's item.
    const joinedWorkId = '7a1e0000-0000-4000-8000-000000000099'
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([{
      ...stripling,
      workId: joinedWorkId,
      links: [{ ...stripling.links[0], workId: joinedWorkId, writeState: 'Written' }],
    }]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))
    await screen.findAllByText('Waiting to be written')
    await nextPoll()
    await screen.findByText('Written to Jellyfin')

    // Assert
    const [strip, calypso] = rows().querySelectorAll<HTMLElement>(':scope > li')
    expect(within(strip).getByText('Written to Jellyfin')).toBeInTheDocument()
    expect(within(calypso).getByText('Linked, with no library item to write into.')).toBeInTheDocument()
    expect(screen.queryByText('Waiting to be written')).not.toBeInTheDocument()
    expect(screen.getByText('2 of 2 works linked.')).toBeInTheDocument()
  })

  it('never asks for a click that linked no work, not even when every link query is refreshed', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      { workId: confidentStripling.work.id, outcome: 'Skipped', reason: 'You marked that none fits.', links: [] },
    ]))
    // The API has no link of such a batch to answer with.
    vi.mocked(libraryLinksApi.batch).mockRejectedValue(apiError(404))
    const { client, user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })
    await user.click(box('121. Calypso Cat (1962)'))
    await user.click(screen.getByRole('button', { name: 'Link 1 work' }))
    await screen.findByText('Not linked: You marked that none fits.')

    // Act — as the end of a sync or of a preparation of the suggestions does
    await act(() => client.invalidateQueries({ queryKey: qk.library.links.all }))

    // Assert
    expect(libraryLinksApi.batch).not.toHaveBeenCalled()
    expect(screen.getByText('Not linked: You marked that none fits.')).toBeInTheDocument()
    expect(screen.getByText('0 of 1 work linked.')).toBeInTheDocument()
    expect(screen.queryByText(/^Refresh failed/)).not.toBeInTheDocument()
  })

  it('goes back to the confident suggestions as they now are', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Written')]))
    const { user, client } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })
    await user.click(box('121. Calypso Cat (1962)'))
    await user.click(screen.getByRole('button', { name: 'Link 1 work' }))
    await screen.findByText('Written to Jellyfin')
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [confidentCalypso]))

    // Act
    await user.click(screen.getByRole('button', { name: 'Back to the confident suggestions' }))

    // Assert
    expect(await screen.findByRole('button', { name: 'Link 1 work' })).toBeInTheDocument()
    expect(rowCount()).toBe(1)
    expect(libraryLinksApi.confident).toHaveBeenCalledTimes(2)
    expect(client.getQueryData(qk.library.links.confident({ locationId: undefined, page: 1, pageSize: 25 }))).toBeDefined()
  })

  it('shows why the click was refused and keeps the rows', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockRejectedValue(apiError(409, 'Connect a metadata catalog under Integrations first.'))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))

    // Assert
    expect(await screen.findByText('Connect a metadata catalog under Integrations first.')).toBeInTheDocument()
    expect(box('120. Landing Stripling (1962)')).toBeChecked()
  })

  it('asks for one library\'s suggestions, and offers every library again when it has none', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, []))

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Library' }), 'Фільми')
    await screen.findByText('No work of this library has a confident suggestion.')
    await user.click(screen.getByRole('button', { name: 'All libraries' }))

    // Assert
    expect(vi.mocked(libraryLinksApi.confident).mock.calls.map(([query]) => query)).toEqual([
      { locationId: undefined, page: 1, pageSize: 25 },
      { locationId: filmsCount.locationId, page: 1, pageSize: 25 },
      { locationId: undefined, page: 1, pageSize: 25 },
    ])
  })

  it('asks the server for the next page, every row of it ticked', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) =>
      confidentPage(query, query.page === 1 ? [confidentStripling] : [confidentCalypso], 30))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 1 work' })
    await user.click(box('120. Landing Stripling (1962)'))

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))

    // Assert
    expect(await screen.findByRole('checkbox', { name: '121. Calypso Cat (1962)' })).toBeChecked()
    expect(screen.getByRole('button', { name: 'Link 1 work' })).toBeEnabled()
    expect(libraryLinksApi.confident).toHaveBeenLastCalledWith({ locationId: undefined, page: 2, pageSize: 25 })
  })

  it('says when no work has a confident suggestion', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [], 0, []))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(/^No work has a confident suggestion/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Link/ })).not.toBeInTheDocument()
  })
})
