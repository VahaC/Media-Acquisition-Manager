import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import { qk } from '@/lib/queries'
import type { CurrentUser } from '@/lib/types'
import { LibraryTmdbPage } from '@/pages/LibraryTmdbPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import {
  catchUpOf,
  confidentPage,
  conflictSentence,
  linkRowOf,
  linksPage,
  progressOf,
  queueOf,
  suggestionRunOf,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — "Update Jellyfin" on the TMDb page (Vasyl's decision of 2026-10-06): the REPAIR of the links
// set under the first rules, which only put TMDb's ids on the items. The panel counts the links not
// identified yet and, after one question that says what Jellyfin will replace, queues them all; the
// background worker identifies them and the panel follows the batch as "done of total". Afterwards it
// says how many links were refused or failed, with a button to the Not identified tab. A link queued
// from the dialog or the Review tab carries no batch, and the panel still learns when it ends.

/** The address the page is at, so a test can read what "Show them" put there. */
function Address() {
  const location = useLocation()
  return <output aria-label="Address">{location.pathname + location.search}</output>
}

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
          <Address />
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const address = () => screen.getByRole('status', { name: 'Address' }).textContent

/** The panel, by its heading. */
const panel = () => screen.getByRole('region', { name: 'Update Jellyfin' })
const updateButton = () => within(panel()).getByRole('button', { name: 'Update Jellyfin' })

/** How many links the lists hold: waiting or being identified (Pending), and refused or failed (NotWritten). */
const served = { pending: 0, notWritten: 0 }

/** Answers the Pending and the Not identified lists from `served`, and every other list empty. */
function serveLinks() {
  vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => {
    if (query.filter === 'Pending') {
      return linksPage(query, served.pending > 0 ? [linkRowOf({ writeState: 'Queued', identifiedUtc: null })] : [], served.pending)
    }
    if (query.filter === 'NotWritten') {
      const refused = linkRowOf({ writeState: 'NotWritten', writeError: conflictSentence, identifiedUtc: null })
      return linksPage(query, served.notWritten > 0 ? [refused] : [], served.notWritten)
    }
    return linksPage(query, [])
  })
}

/** Answers the Pending list with `count` links waiting or being identified. */
function servePending(count: number) {
  served.pending = count
}

/** Answers the Not identified list (and the panel's count of it) with `count` links. */
function serveNotWritten(count: number) {
  served.notWritten = count
}

/** The asks of the Not identified list as the panel makes them: a link at a time, for the count. */
const notWrittenCountAsks = () => vi.mocked(libraryLinksApi.links).mock.calls
  .filter(([query]) => query.filter === 'NotWritten' && query.pageSize === 1)

/** The Pending list's asks. */
const pendingAsks = () => vi.mocked(libraryLinksApi.links).mock.calls.filter(([query]) => query.filter === 'Pending')

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

/** Presses the confirmation's own button, which carries the panel's label. */
async function confirmUpdate(user: ReturnType<typeof userEvent.setup>) {
  const confirmation = await screen.findByRole('dialog', { name: 'Update Jellyfin?' })
  await user.click(within(confirmation).getByRole('button', { name: 'Update Jellyfin' }))
}

/** A start that queues everything: the counts empty, the batch under way, as the server answers. */
function startsABatch() {
  vi.mocked(libraryLinksApi.startCatchUp).mockImplementation(async () => {
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0, progress: progressOf({ done: 41 }) }))
    return catchUpOf({ toIdentify: 223, progress: progressOf({ done: 0 }) })
  })
}

describe('LibraryTmdbPage Update Jellyfin', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    servePending(0)
    serveNotWritten(0)
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockResolvedValue(queueOf(0, 0, null))
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, []))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    serveLinks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('says how many links are not identified yet above the suggestions, with the button on and the plain statement in the description', async () => {
    // Arrange
    renderPage()

    // Act
    const toIdentify = await within(panel()).findByText('Links not identified yet: 223')

    // Assert
    expect(panel()).toHaveTextContent('it replaces the item\'s title, overview and images with TMDb\'s')
    expect(panel()).toHaveTextContent('there is no undo')
    expect(panel()).toHaveTextContent('Your files and folders are not touched.')
    expect(panel()).not.toHaveTextContent(/rating/)
    expect(toIdentify.closest('[role="status"]')).not.toBeNull()
    expect(within(panel()).queryByText(/refused or failed/)).not.toBeInTheDocument()
    expect(within(panel()).queryByRole('button', { name: 'Show them' })).not.toBeInTheDocument()
    expect(updateButton()).toBeEnabled()
    const suggestions = screen.getByRole('region', { name: 'Suggestions' })
    expect(panel().compareDocumentPosition(suggestions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(pendingAsks()).toEqual([[{ filter: 'Pending', page: 1, pageSize: 1 }]])
    expect(notWrittenCountAsks()).toEqual([[{ filter: 'NotWritten', page: 1, pageSize: 1 }]])
  })

  it('keeps the button off with the reason when every link the app can identify is identified', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0 }))
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 0')

    // Act
    await user.click(updateButton())

    // Assert
    expect(updateButton()).toBeDisabled()
    expect(updateButton().parentElement).toHaveAttribute('title', 'Nothing to update: no link is waiting to be identified.')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(libraryLinksApi.startCatchUp).not.toHaveBeenCalled()
  })

  it('asks before it sends, saying how many items Jellyfin will be asked to identify, that it replaces title, overview and images, and that there is no undo', async () => {
    // Arrange
    startsABatch()
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 223')

    // Act
    await user.click(updateButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Update Jellyfin?' })
    const message = confirmation.textContent
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(message).toContain('Jellyfin will be asked to identify 223 items as their TMDb links')
    expect(message).toContain('it replaces each item\'s title, overview and images with TMDb\'s')
    expect(message).toContain('and there is no undo')
    expect(message).toContain('Your files and folders are not touched.')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(libraryLinksApi.startCatchUp).not.toHaveBeenCalled()
    expect(updateButton()).toBeEnabled()

    // Act
    await user.click(updateButton())
    await confirmUpdate(user)

    // Assert
    expect(await within(panel()).findByText('Identifying in Jellyfin: 41 of 223 links…')).toBeInTheDocument()
    expect(libraryLinksApi.startCatchUp).toHaveBeenCalledOnce()
  })

  it('asks in the singular when one link is left, and the question\'s button is destructive', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 1 }))
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 1')

    // Act
    await user.click(updateButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Update Jellyfin?' })

    // Assert
    expect(confirmation).toHaveTextContent('Jellyfin will be asked to identify 1 item as its TMDb link: it replaces the item\'s title, overview and images with TMDb\'s')
    expect(within(confirmation).getByRole('button', { name: 'Update Jellyfin' })).toHaveClass('ui-button-destructive')
  })

  it('sends once on a double click', async () => {
    // Arrange
    startsABatch()
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 223')

    // Act: both clicks land before the question has opened, which a real double click does too.
    const button = updateButton()
    fireEvent.click(button)
    fireEvent.click(button)
    const confirmations = await screen.findAllByRole('dialog', { name: 'Update Jellyfin?' })
    await confirmUpdate(user)

    // Assert
    expect(confirmations).toHaveLength(1)
    expect(await within(panel()).findByText('Identifying in Jellyfin: 41 of 223 links…')).toBeInTheDocument()
    expect(libraryLinksApi.startCatchUp).toHaveBeenCalledOnce()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('follows the batch every 2 s as "done of total" until it ends, then reads the counts and the lists again', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    startsABatch()
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 223')

    // Act
    await user.click(updateButton())
    await confirmUpdate(user)
    const progress = await within(panel()).findByText('Identifying in Jellyfin: 41 of 223 links…')

    // Assert
    expect(progress.closest('[role="status"]')).not.toBeNull()
    expect(within(panel()).getByText('A series takes minutes, so the count can wait on one at the end.')).toBeInTheDocument()
    expect(updateButton()).toBeDisabled()
    expect(updateButton().parentElement).toHaveAttribute('title', 'Jellyfin is identifying links.')
    expect(within(panel()).queryByText(/^Links not identified yet/)).not.toBeInTheDocument()

    // Act
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0, progress: progressOf({ done: 120 }) }))
    await nextPoll()

    // Assert
    expect(await within(panel()).findByText('Identifying in Jellyfin: 120 of 223 links…')).toBeInTheDocument()

    // Act: the batch ends with two links refused and one left for another click.
    serveNotWritten(2)
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 1, progress: null }))
    await nextPoll()

    // Assert
    expect(await within(panel()).findByText('Links not identified yet: 1')).toBeInTheDocument()
    expect(await within(panel()).findByText('2 links were refused or failed: their reasons are under Not identified.')).toBeInTheDocument()
    // On the page's load, after the click, and when the batch ended.
    expect(notWrittenCountAsks()).toHaveLength(3)
    expect(updateButton()).toBeEnabled()
    const asks = vi.mocked(libraryLinksApi.catchUp).mock.calls.length

    // Act
    await nextPoll()

    // Assert: no poll once nothing is under way.
    expect(vi.mocked(libraryLinksApi.catchUp).mock.calls).toHaveLength(asks)
    expect(libraryLinksApi.startCatchUp).toHaveBeenCalledOnce()
  })

  it('asks for the works again when the batch ends, so the cards show what Jellyfin took', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ progress: progressOf() }))
    const { client } = renderPage()
    await within(panel()).findByText('Identifying in Jellyfin: 41 of 223 links…')
    const invalidated = vi.spyOn(client, 'invalidateQueries')
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0, progress: null }))

    // Act
    await nextPoll()
    await within(panel()).findByText('Links not identified yet: 0')

    // Assert
    expect(invalidated).toHaveBeenCalledWith({ queryKey: qk.library.worksAll })
  })

  it('finds a batch still under way after a reload, with the button off, and follows it', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    serveNotWritten(2)
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0, progress: progressOf({ done: 41 }) }))
    renderPage()

    // Act
    await within(panel()).findByText('Identifying in Jellyfin: 41 of 223 links…')

    // Assert: the refused links wait for the end, when the Show them button comes back.
    expect(updateButton()).toBeDisabled()
    expect(updateButton().parentElement).toHaveAttribute('title', 'Jellyfin is identifying links.')
    expect(within(panel()).queryByText(/refused or failed/)).not.toBeInTheDocument()
    expect(within(panel()).queryByRole('button', { name: 'Show them' })).not.toBeInTheDocument()

    // Act
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0, progress: progressOf({ done: 223 }) }))
    await nextPoll()
    await within(panel()).findByText('Identifying in Jellyfin: 223 of 223 links…')
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf({ toIdentify: 0, progress: null }))
    await nextPoll()

    // Assert
    expect(await within(panel()).findByText('Links not identified yet: 0')).toBeInTheDocument()
    expect(await within(panel()).findByText('2 links were refused or failed: their reasons are under Not identified.')).toBeInTheDocument()
    expect(within(panel()).getByRole('button', { name: 'Show them' })).toBeInTheDocument()
    expect(libraryLinksApi.startCatchUp).not.toHaveBeenCalled()
  })

  it('says how many links were refused or failed, with a Show them button that opens the Not identified tab', async () => {
    // Arrange
    serveNotWritten(4)
    const { user } = renderPage()
    const line = await within(panel()).findByText('4 links were refused or failed: their reasons are under Not identified.')

    // Act
    await user.click(within(panel()).getByRole('button', { name: 'Show them' }))

    // Assert
    expect(line.closest('[role="status"]')).not.toBeNull()
    // Outside the live region: a control in it would be read out again at every poll.
    expect(within(panel()).getByRole('button', { name: 'Show them' }).closest('[role="status"]')).toBeNull()
    expect(address()).toBe('/library/tmdb?tab=NotIdentified')
    expect(screen.getByRole('tab', { name: 'Not identified' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByText(conflictSentence)).toBeInTheDocument()
  })

  it('says it in the singular for one link, and says nothing about refusals when there are none', async () => {
    // Arrange
    serveNotWritten(1)

    // Act
    renderPage()

    // Assert
    expect(await within(panel()).findByText('1 link was refused or failed: its reason is under Not identified.')).toBeInTheDocument()
  })

  it('says why Update Jellyfin was refused, under the counts, and keeps them', async () => {
    // Arrange
    const sentence = 'A library sync is running; try again in a moment.'
    vi.mocked(libraryLinksApi.startCatchUp).mockRejectedValue(apiError(409, sentence))
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 223')

    // Act
    await user.click(updateButton())
    await confirmUpdate(user)

    // Assert
    expect(await within(panel()).findByRole('alert')).toHaveTextContent(sentence)
    expect(within(panel()).getByText('Links not identified yet: 223')).toBeInTheDocument()
    expect(updateButton()).toBeEnabled()
  })

  it('says Jellyfin couldn\'t be updated when the refusal has no sentence of its own', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.startCatchUp).mockRejectedValue(new Error('Network Error'))
    const { user } = renderPage()
    await within(panel()).findByText('Links not identified yet: 223')

    // Act
    await user.click(updateButton())
    await confirmUpdate(user)

    // Assert
    expect(await within(panel()).findByRole('alert')).toHaveTextContent('Jellyfin couldn\'t be updated.')
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

  it('keeps the last counts on screen under a banner when a refresh fails', async () => {
    // Arrange
    serveNotWritten(2)
    const { client } = renderPage()
    await within(panel()).findByText('2 links were refused or failed: their reasons are under Not identified.')
    vi.mocked(libraryLinksApi.links).mockRejectedValue(apiError(500, 'The database is locked.'))

    // Act
    await act(() => client.invalidateQueries({ queryKey: qk.library.links.all }))

    // Assert
    expect(await within(panel()).findByText(/Refresh failed, showing the last known counts\./)).toBeInTheDocument()
    expect(within(panel()).getByText('Links not identified yet: 223')).toBeInTheDocument()
    expect(within(panel()).getByText('2 links were refused or failed: their reasons are under Not identified.')).toBeInTheDocument()
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

  it('follows a link the dialog queued, and shows it refused in the counts without a reload', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    servePending(1)
    renderPage()
    await within(panel()).findByText('Links not identified yet: 223')
    expect(within(panel()).queryByText(/refused or failed/)).not.toBeInTheDocument()

    // Act: the one link settles, refused; the batch the catch-up reports never held it.
    servePending(0)
    serveNotWritten(1)
    await nextPoll()

    // Assert
    expect(await within(panel()).findByText('1 link was refused or failed: its reason is under Not identified.')).toBeInTheDocument()
    expect(vi.mocked(libraryLinksApi.catchUp).mock.calls.length).toBeGreaterThanOrEqual(2)
    // The Pending list is read twice only: the one that found the link and the one that found it gone.
    expect(pendingAsks()).toHaveLength(2)
    await nextPoll()
    expect(pendingAsks()).toHaveLength(2)
  })
})
