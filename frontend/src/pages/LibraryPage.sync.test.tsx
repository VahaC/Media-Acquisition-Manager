import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { libraryApi } from '@/lib/library-api'
import type { LibrarySourceList, LibrarySyncRun } from '@/lib/types'
import { LibraryPage } from '@/pages/LibraryPage'
import { owner, signInAs } from '@/test/auth-session'
import { aliens, locations, runOf, sourceOf, sourcesOf, worksPage } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-8 — the Library page while a sync runs: the sources are polled every 2 s while a run is queued
// or running, and the works are asked for again exactly when a run the page saw under way succeeds.

const earlierRun = runOf({})
const syncId = '5e000000-0000-4000-8000-000000000009'
const queued = runOf({ id: syncId, status: 'Queued', createdUtc: '2026-09-30T09:00:00Z', startedUtc: null, finishedUtc: null, itemsRead: 0, itemsTotal: null, worksCount: null, versionsCount: null, filesCount: null })
const running = { ...queued, status: 'Running', startedUtc: '2026-09-30T09:00:01Z', itemsRead: 50, itemsTotal: 120 } satisfies LibrarySyncRun
const succeeded = { ...running, status: 'Succeeded', finishedUtc: '2026-09-30T09:00:05Z', itemsRead: 120, worksCount: 118, versionsCount: 120, filesCount: 120 } satisfies LibrarySyncRun
const failed = { ...running, status: 'Failed', finishedUtc: '2026-09-30T09:00:05Z', error: 'Jellyfin refused the API key.' } satisfies LibrarySyncRun

/** The next answers of the sources: the latest run of Home Jellyfin is `run`. */
function latestRunIs(run: LibrarySyncRun) {
  vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf(sourceOf({ latestRun: run, lastSucceededRun: earlierRun })))
}

const officeJellyfinId = '5d1c7a90-0000-4000-8000-000000000005'

/**
 * The sources with a second media library: Home Jellyfin's latest run is `homeRun` (by default the
 * sync that finished before the page opened), and Office Jellyfin runs a sync of its own as `officeRun`.
 */
function twoSources(officeRun: LibrarySyncRun, homeRun: LibrarySyncRun = earlierRun): LibrarySourceList {
  return sourcesOf(
    sourceOf({ latestRun: homeRun, lastSucceededRun: earlierRun }),
    sourceOf({
      integrationId: officeJellyfinId,
      name: 'Office Jellyfin',
      latestRun: { ...officeRun, id: '5e000000-0000-4000-8000-000000000019', integrationId: officeJellyfinId, integrationName: 'Office Jellyfin' },
      lastSucceededRun: null,
    }),
  )
}

/** The status line of Home Jellyfin's latest sync. */
const syncStatus = () => within(screen.getByText('Home Jellyfin').closest('li')!).getByRole('status')

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

function renderPage() {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup({ advanceTimers: vi.advanceTimersByTime }) }
}

describe('LibraryPage while a sync runs', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, [aliens]))
    vi.mocked(libraryApi.locations).mockResolvedValue(locations)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('follows a sync from queued through its progress to success, then asks for the works once more', async () => {
    // Arrange
    latestRunIs(queued)
    renderPage()
    await screen.findByText('Queued…')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    latestRunIs(running)
    await nextPoll()
    const progress = syncStatus().textContent
    const worksWhileRunning = vi.mocked(libraryApi.works).mock.calls.length
    latestRunIs(succeeded)

    // Act
    await nextPoll()

    // Assert
    expect(await screen.findByText(/^Synced .*: read 120 items into 118 works, 120 versions, 120 files\.$/)).toBeInTheDocument()
    expect(progress).toBe('Syncing… 50 of 120 items')
    expect(worksWhileRunning).toBe(1)
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    expect(libraryApi.locations).toHaveBeenCalledTimes(2)
  })

  it('stops polling once the sync has ended', async () => {
    // Arrange
    latestRunIs(running)
    renderPage()
    await screen.findByText('Syncing… 50 of 120 items')
    latestRunIs(succeeded)
    await nextPoll()
    await screen.findByText(/^Synced /)
    const pollsUntilDone = vi.mocked(libraryApi.sources).mock.calls.length

    // Act
    await nextPoll()
    await nextPoll()
    await nextPoll()

    // Assert
    expect(pollsUntilDone).toBe(2)
    expect(libraryApi.sources).toHaveBeenCalledTimes(2)
  })

  it('does not ask for the works again after a failed sync', async () => {
    // Arrange
    latestRunIs(running)
    renderPage()
    await screen.findByText('Syncing… 50 of 120 items')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    latestRunIs(failed)

    // Act — the poll that brings the failure, and one more interval, in which nothing is polled.
    await nextPoll()
    await nextPoll()

    // Assert
    expect(await screen.findByText(/^Sync failed .*: Jellyfin refused the API key\.$/)).toBeInTheDocument()
    expect(screen.getByText(/^Showing the library as synced /)).toBeInTheDocument()
    expect(libraryApi.works).toHaveBeenCalledOnce()
    expect(libraryApi.locations).toHaveBeenCalledOnce()
    expect(libraryApi.sources).toHaveBeenCalledTimes(2)
  })

  it('neither polls nor asks for the works again when the page opens on a finished sync', async () => {
    // Arrange
    latestRunIs(earlierRun)
    renderPage()
    await screen.findByText(/^Synced /)
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(libraryApi.sources).toHaveBeenCalledOnce()
    expect(libraryApi.works).toHaveBeenCalledOnce()
  })

  it('asks for the works again when a sync the owner started has finished by the next answer', async () => {
    // Arrange — a small library: the refetch after Sync now already finds the new run Succeeded.
    latestRunIs(earlierRun)
    vi.mocked(libraryApi.startSync).mockImplementation(async () => {
      latestRunIs(succeeded)
      return queued
    })
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })

    // Act
    await user.click(await screen.findByRole('button', { name: 'Sync now' }))

    // Assert
    expect(await screen.findByText(/read 120 items into 118 works/)).toBeInTheDocument()
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    expect(libraryApi.sources).toHaveBeenCalledTimes(2)
  })

  it('asks for the works once while one source syncs beside another that has finished, and once more when it succeeds', async () => {
    // Arrange — Home Jellyfin's sync finished before the page opened and every poll repeats it:
    // only Office Jellyfin's run, seen under way, may bring the works again.
    vi.mocked(libraryApi.sources).mockResolvedValue(twoSources(queued))
    renderPage()
    await screen.findByText('Queued…')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    vi.mocked(libraryApi.sources).mockResolvedValue(twoSources(running))
    await nextPoll()
    vi.mocked(libraryApi.sources).mockResolvedValue(twoSources({ ...running, itemsRead: 100 }))
    await nextPoll()
    await screen.findByText('Syncing… 100 of 120 items')
    const worksWhileRunning = vi.mocked(libraryApi.works).mock.calls.length
    const locationsWhileRunning = vi.mocked(libraryApi.locations).mock.calls.length
    vi.mocked(libraryApi.sources).mockResolvedValue(twoSources(succeeded))

    // Act
    await nextPoll()

    // Assert
    expect(await screen.findByText(/read 120 items into 118 works/)).toBeInTheDocument()
    expect(worksWhileRunning).toBe(1)
    expect(locationsWhileRunning).toBe(1)
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    expect(libraryApi.locations).toHaveBeenCalledTimes(2)
    expect(libraryApi.sources).toHaveBeenCalledTimes(4)
  })

  it('asks for the works once when Sync now cancels a poll that was bringing a success', async () => {
    // Arrange — Office Jellyfin syncs, and a poll is on its way when the owner starts Home Jellyfin.
    // The start's refetch cancels that poll; both answers bring Office's success, the cancelled one
    // first.
    vi.mocked(libraryApi.sources).mockResolvedValue(twoSources(running))
    vi.mocked(libraryApi.startSync).mockResolvedValue(queued)
    const { user } = renderPage()
    await screen.findByText('Syncing… 50 of 120 items')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    let answerPoll: (list: LibrarySourceList) => void = () => {}
    let answerRefetch: (list: LibrarySourceList) => void = () => {}
    vi.mocked(libraryApi.sources)
      .mockImplementationOnce(() => new Promise((resolve) => {
        answerPoll = resolve
      }))
      .mockImplementationOnce(() => new Promise((resolve) => {
        answerRefetch = resolve
      }))
      .mockResolvedValue(twoSources(succeeded, queued))
    await nextPoll()
    await user.click(screen.getByRole('button', { name: 'Sync now' }))
    await vi.waitFor(() => expect(libraryApi.sources).toHaveBeenCalledTimes(3))
    const pollSignal = vi.mocked(libraryApi.sources).mock.calls[1][0]

    // Act
    await act(async () => answerPoll(twoSources(succeeded)))
    await act(async () => answerRefetch(twoSources(succeeded, queued)))

    // Assert
    expect(await screen.findByText(/read 120 items into 118 works/)).toBeInTheDocument()
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    await nextPoll()
    expect(libraryApi.works).toHaveBeenCalledTimes(2)
    expect(libraryApi.locations).toHaveBeenCalledTimes(2)
    expect(pollSignal?.aborted).toBe(true)
  })
})
