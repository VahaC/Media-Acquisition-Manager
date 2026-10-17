import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { i18n as I18n } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { i18n } from '@/lib/i18n'
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
  lockedSentence,
  penguins,
  penguinsShow,
  queueOf,
  suggestionRunOf,
} from '@/test/library-links'
import { pluralFormProbe } from '@/test/plural-probe'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb page's Confident tab (R24): every confident suggestion a page at a time with each
// library's counts, every row ticked, one line above the toolbar saying what linking does to the items
// in Jellyfin (it identifies them, replacing their title, overview and images), "Link N works" in one
// click with no further question — the owner reviewed and unticked the list first —, and then the rows
// as they were clicked, each following its identification every 2 s until none waits or is being
// identified. A ticked series is linked in the same click as the films.

/** The series and its confident show. */
const confidentPenguins = { work: penguins, candidate: { ...penguinsShow, isConfident: true } }

/** `translations`: the i18n instance the page reads its text from, the app's own unless a test brings one. */
function renderPage(translations: I18n = i18n) {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <I18nextProvider i18n={translations}>
      <QueryClientProvider client={client}>
        <ConfirmProvider>
          <MemoryRouter initialEntries={['/library/tmdb?tab=Confident']}>
            <Routes>
              <Route path="/library/tmdb" element={<LibraryTmdbPage />} />
            </Routes>
          </MemoryRouter>
        </ConfirmProvider>
      </QueryClientProvider>
    </I18nextProvider>,
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

  it('says before the click what Jellyfin will replace, with no extra question', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Queued'),
      batchItemOf(confidentCalypso.work, 'Queued'),
    ]))
    vi.mocked(libraryLinksApi.batch).mockImplementation(() => new Promise(() => {}))
    const { user } = renderPage()
    const hint = await screen.findByText(/^Linking the ticked works makes Jellyfin identify each as its suggestion/)
    const toolbarButton = screen.getByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(toolbarButton)

    // Assert
    expect(hint).toHaveTextContent('it replaces the title, overview and images of the item with TMDb\'s, a series\' in minutes, and there is no undo. Your files and folders are not touched.')
    expect(hint.compareDocumentPosition(toolbarButton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(libraryLinksApi.acceptConfident).toHaveBeenCalledOnce()
  })

  it('leaves the statement out when no work has a confident suggestion to link', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [], 0, []))

    // Act
    renderPage()
    await screen.findByText(/^No work has a confident suggestion/)

    // Assert
    expect(screen.queryByText(/^Linking the ticked works/)).not.toBeInTheDocument()
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

  it('links the ticked rows in one click, then keeps them on screen, each following its identification until none is under way', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Queued')]))
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Writing')]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })
    await user.click(box('121. Calypso Cat (1962)'))

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 1 work' }))
    const queued = await screen.findByText('Waiting to identify')
    await nextPoll()
    await screen.findByText('Identifying…', { selector: '.ui-badge' })
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([batchItemOf(confidentStripling.work, 'Written')]))
    await nextPoll()
    await screen.findByText('Identified in Jellyfin')
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

  // MAM-46 — Ukrainian words "of N links" by N's plural form: 2 takes another form than 5 or 1.
  it('picks the plural form of the identifications under way by the count of links', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [confidentStripling, confidentPenguins]))
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Queued'),
      batchItemOf(penguins, 'Queued'),
    ]))
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Written'),
      batchItemOf(penguins, 'Writing'),
    ]))
    const probe = pluralFormProbe('libraryLinks', 'confident.identifying', (form) => `${form}: {{done}}/{{total}}`)
    const { user } = renderPage(probe)
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))
    await nextPoll()

    // Assert
    expect(await screen.findByText('few: 1/2')).toBeInTheDocument()
  })

  it('shows a series linked in the same click like a film, queued then identified', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [confidentStripling, confidentPenguins]))
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Queued'),
      batchItemOf(penguins, 'Queued'),
    ]))
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Written'),
      batchItemOf(penguins, 'Writing'),
    ]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))
    expect(await screen.findAllByText('Waiting to identify')).toHaveLength(2)
    await nextPoll()
    await screen.findByText('Identifying…', { selector: '.ui-badge' })
    // Read now: the same line says the work done once the batch ends.
    const progress = screen.getByText(/^Identifying in Jellyfin/).textContent
    vi.mocked(libraryLinksApi.batch).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Written'),
      batchItemOf(penguins, 'Written'),
    ]))
    await nextPoll()
    await screen.findByText('2 of 2 works linked.')

    // Assert
    expect(progress).toBe('Identifying in Jellyfin: 1 of 2 links…')
    expect(screen.getAllByText('Identified in Jellyfin')).toHaveLength(2)
    expect(screen.queryByText(/refused or failed/)).not.toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows a link Jellyfin holds the ids of but the app never identified as not identified yet', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'Written', { identifiedUtc: null }),
    ]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })
    await user.click(box('121. Calypso Cat (1962)'))

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 1 work' }))

    // Assert
    expect(await screen.findByText('Not identified yet', { selector: '.ui-badge' })).toHaveClass('library-badge-warning')
    expect(screen.queryByText('Identified in Jellyfin')).not.toBeInTheDocument()
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
    expect(await screen.findAllByText('Waiting to identify')).toHaveLength(2)
  })

  it('says how far the identifications are while they are under way', async () => {
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
    expect(await screen.findByText('Identifying in Jellyfin: 1 of 2 links…')).toBeInTheDocument()
  })

  it('shows an identification Jellyfin or the app refused with its sentence, and a work skipped with why', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.acceptConfident).mockResolvedValue(batchOf([
      batchItemOf(confidentStripling.work, 'NotWritten', { writeError: lockedSentence, identifiedUtc: null }),
      { workId: confidentCalypso.work.id, outcome: 'Skipped', reason: 'The suggestion is no longer confident.', links: [] },
    ]))
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Link 2 works' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Link 2 works' }))

    // Assert
    const badge = await screen.findByText('Not identified yet')
    expect(badge).toHaveClass('library-badge-warning')
    expect(badge).toHaveAttribute('title', lockedSentence)
    // On screen too, verbatim: a phone shows no tooltip.
    expect(screen.getByText(lockedSentence, { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('Not linked: The suggestion is no longer confident.')).toBeInTheDocument()
    expect(screen.getByText('1 of 2 works linked.')).toBeInTheDocument()
    // Counted in the summary, so a refusal among a hundred rows isn't missed.
    expect(screen.getByText('1 link was refused or failed: its reason is under its row.').closest('[role="status"]')).toHaveTextContent('1 of 2 works linked.')
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
    await screen.findByText('Waiting to identify')
    await nextPoll()
    await screen.findByText('Identified in Jellyfin')

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
    await screen.findAllByText('Waiting to identify')
    await nextPoll()
    await screen.findByText('Identified in Jellyfin')

    // Assert
    const [strip, calypso] = rows().querySelectorAll<HTMLElement>(':scope > li')
    expect(within(strip).getByText('Identified in Jellyfin')).toBeInTheDocument()
    expect(within(calypso).getByText('Linked, with no library item to identify.')).toBeInTheDocument()
    expect(screen.queryByText('Waiting to identify')).not.toBeInTheDocument()
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
    await screen.findByText('Identified in Jellyfin')
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
