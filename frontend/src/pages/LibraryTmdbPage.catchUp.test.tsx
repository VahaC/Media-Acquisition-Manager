import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { CurrentUser, LibraryLinksQuery } from '@/lib/types'
import { LibraryTmdbPage } from '@/pages/LibraryTmdbPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { catchUpOf, confidentPage, linkRowOf, linksPage, queueOf, suggestionRunOf } from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — "Update Jellyfin" on the TMDb page (Vasyl's decision of 2026-10-06): the links set before
// the app wrote series' and episodes' links and asked Jellyfin to look up an item's metadata are
// counted, sent with one click with no question — a series' link like any other —, and followed
// through the Pending list of links until none waits or is being written. A lookup Jellyfin didn't
// take is recorded as asked, so it isn't counted again. Off with the reason when there is nothing to
// send or while links are written, and for a member, who is asked for nothing.

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={['/library/tmdb']}>
          <Routes>
            <Route path="/library/tmdb" element={<LibraryTmdbPage />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

/** The panel, by its heading. */
const panel = () => screen.getByRole('region', { name: 'Update Jellyfin' })
const updateButton = () => within(panel()).getByRole('button', { name: 'Update Jellyfin' })

/** Answers the Pending list with `count` links waiting or being written, and every other list empty. */
function servePending(count: number) {
  vi.mocked(libraryLinksApi.links).mockImplementation(async (query: LibraryLinksQuery) =>
    linksPage(query, query.filter === 'Pending' && count > 0 ? [linkRowOf({ writeState: 'Queued' })] : [], query.filter === 'Pending' ? count : 0))
}

/** The Pending list's asks: one link at a time, for its count. */
const pendingAsks = () => vi.mocked(libraryLinksApi.links).mock.calls.filter(([query]) => query.filter === 'Pending')

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

describe('LibraryTmdbPage Update Jellyfin', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockResolvedValue(queueOf(0, 0, null))
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, []))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    servePending(0)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('says how many links it would write and look up, above the suggestions, with the button on', async () => {
    // Arrange
    renderPage()

    // Act
    const toWrite = await within(panel()).findByText('Links to write into Jellyfin: 3')

    // Assert
    expect(within(panel()).getByText('Links Jellyfin wasn\'t asked to look up: 12')).toBeInTheDocument()
    expect(panel()).not.toHaveTextContent(/rating/)
    expect(panel()).toHaveTextContent('A lookup Jellyfin didn\'t take isn\'t counted again: Retry in the work\'s TMDb dialog asks again.')
    expect(toWrite.closest('[role="status"]')).not.toBeNull()
    expect(updateButton()).toBeEnabled()
    const suggestions = screen.getByRole('region', { name: 'Suggestions' })
    expect(panel().compareDocumentPosition(suggestions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(pendingAsks()).toEqual([[{ filter: 'Pending', page: 1, pageSize: 1 }]])
  })

  it('keeps the button off with the reason when there is nothing to send', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toWrite: 0, toLookUp: 0 }))
    const { user } = renderPage()
    await within(panel()).findByText('Links to write into Jellyfin: 0')

    // Act
    await user.click(updateButton())

    // Assert
    expect(updateButton()).toBeDisabled()
    expect(updateButton().parentElement).toHaveAttribute('title', 'Nothing to update: Jellyfin holds every link the app can write, and was asked to look up each one.')
    expect(libraryLinksApi.startCatchUp).not.toHaveBeenCalled()
  })

  it.each([
    ['links to write', catchUpOf({ toLookUp: 0 })],
    ['links to look up', catchUpOf({ toWrite: 0 })],
  ])('keeps the button on with only %s', async (_, counts) => {
    // Arrange
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(counts)

    // Act
    renderPage()
    await within(panel()).findByText(/^Links to write into Jellyfin/)

    // Assert
    expect(updateButton()).toBeEnabled()
  })

  it('sends the links once, with no question, follows their writes every 2 s until none is left, then reads the counts again', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.startCatchUp).mockImplementation(async () => {
      // The POST queues them before it answers: the counts lose them, the Pending list has them.
      vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toWrite: 0, toLookUp: 0 }))
      servePending(15)
      return catchUpOf()
    })
    const { user } = renderPage()
    await within(panel()).findByText('Links to write into Jellyfin: 3')

    // Act
    await user.click(updateButton())
    const progress = await within(panel()).findByText('Writing into Jellyfin: 15 links left…')
    expect(progress.closest('[role="status"]')).not.toBeNull()
    expect(updateButton()).toBeDisabled()
    expect(updateButton().parentElement).toHaveAttribute('title', 'Links are being written into Jellyfin.')
    servePending(1)
    await nextPoll()
    await within(panel()).findByText('Writing into Jellyfin: 1 link left…')
    // A lookup Jellyfin didn't take is recorded as asked (with its error), so the counts read 0 again:
    // Retry in its work's dialog asks again, not another click here.
    servePending(0)
    await nextPoll()
    const lookUp = await within(panel()).findByText('Links Jellyfin wasn\'t asked to look up: 0')
    // Read again once the last write ended: on the page's load, after the click, and now.
    await vi.waitFor(() => expect(libraryLinksApi.catchUp).toHaveBeenCalledTimes(3))
    const asks = pendingAsks().length
    await nextPoll()

    // Assert
    expect(libraryLinksApi.startCatchUp).toHaveBeenCalledOnce()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(lookUp).toBeInTheDocument()
    expect(within(panel()).getByText('Links to write into Jellyfin: 0')).toBeInTheDocument()
    expect(updateButton()).toBeDisabled()
    expect(updateButton().parentElement).toHaveAttribute('title', 'Nothing to update: Jellyfin holds every link the app can write, and was asked to look up each one.')
    // No poll once nothing is left.
    expect(pendingAsks()).toHaveLength(asks)
  })

  it('sends the links once on a double click', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.startCatchUp).mockImplementation(async () => {
      // As the server does: the queued links leave the counts for the Pending list.
      vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toWrite: 0, toLookUp: 0 }))
      servePending(15)
      return catchUpOf()
    })
    const { user } = renderPage()
    await within(panel()).findByText('Links to write into Jellyfin: 3')

    // Act
    await user.dblClick(updateButton())

    // Assert
    expect(await within(panel()).findByText('Writing into Jellyfin: 15 links left…')).toBeInTheDocument()
    expect(libraryLinksApi.startCatchUp).toHaveBeenCalledOnce()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('finds writes still under way after a reload, with the button off, and follows them', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    servePending(4)
    renderPage()
    await within(panel()).findByText('Writing into Jellyfin: 4 links left…')

    // Act
    servePending(0)
    await nextPoll()

    // Assert
    expect(await within(panel()).findByText('Links to write into Jellyfin: 3')).toBeInTheDocument()
    expect(libraryLinksApi.catchUp).toHaveBeenCalledTimes(2)
    expect(updateButton()).toBeEnabled()
  })

  it('says why Update Jellyfin was refused, under the counts, and keeps them', async () => {
    // Arrange
    const sentence = 'A write to the library is under way; try again in a moment.'
    vi.mocked(libraryLinksApi.startCatchUp).mockRejectedValue(apiError(409, sentence))
    const { user } = renderPage()
    await within(panel()).findByText('Links to write into Jellyfin: 3')

    // Act
    await user.click(updateButton())

    // Assert
    expect(await within(panel()).findByRole('alert')).toHaveTextContent(sentence)
    expect(within(panel()).getByText('Links to write into Jellyfin: 3')).toBeInTheDocument()
    expect(updateButton()).toBeEnabled()
  })

  it('shows why the counts couldn\'t load, with Try again', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.catchUp).mockRejectedValue(apiError(500, 'The database is locked.'))

    // Act
    renderPage()

    // Assert
    const failure = await within(panel()).findByRole('alert')
    expect(failure).toHaveTextContent('What Jellyfin is missing couldn\'t be loaded.')
    expect(failure).toHaveTextContent('The database is locked.')
    expect(within(failure).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    expect(within(panel()).queryByRole('button', { name: 'Update Jellyfin' })).not.toBeInTheDocument()
  })

  it('shows a member the button off with the owner\'s reason, and asks for nothing', () => {
    // Arrange
    renderPage(member)

    // Act
    const button = updateButton()

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner can update Jellyfin.')
    expect(within(panel()).getByText('Only the owner can update Jellyfin.')).toBeInTheDocument()
    expect(libraryLinksApi.catchUp).not.toHaveBeenCalled()
    expect(libraryLinksApi.links).not.toHaveBeenCalled()
  })
})
