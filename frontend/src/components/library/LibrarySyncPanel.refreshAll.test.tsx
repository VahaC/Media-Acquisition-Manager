import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type {
  CurrentUser,
  FileInspectionOverview,
  FileInspectionRun,
  FolderScanOverview,
  FolderScanRun,
  LibrarySource,
  LibrarySourceList,
  LibrarySyncRun,
} from '@/lib/types'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { inspectionOf, inspectionRunOf, runOf, scanOf, scanRunOf, sourceOf } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-36 — Refresh all: a sync of every enabled library, then a scan of the media folders, then an
// inspection of the files, each started only once the one before it has ended. The sources the page
// polls every 2 s while a run is under way are what the chain waits on, so each test sets what the
// next poll brings.

const earlier = runOf({})
const syncId = '5e000000-0000-4000-8000-000000000009'
const syncRunning = runOf({ id: syncId, status: 'Running', createdUtc: '2026-09-30T09:00:00Z', startedUtc: '2026-09-30T09:00:01Z', finishedUtc: null, itemsRead: 50, itemsTotal: 120 })
const syncDone = { ...syncRunning, status: 'Succeeded', finishedUtc: '2026-09-30T09:00:05Z', itemsRead: 120 } satisfies LibrarySyncRun
const syncFailed = { ...syncRunning, status: 'Failed', finishedUtc: '2026-09-30T09:00:05Z', error: 'Jellyfin refused the API key.' } satisfies LibrarySyncRun
const scanRunning = scanRunOf({ id: '5ca00000-0000-4000-8000-000000000009', status: 'Running', finishedUtc: null, filesFound: 100, filesUnreported: null })
const scanDone = { ...scanRunning, status: 'Succeeded', finishedUtc: '2026-09-30T09:01:00Z', filesUnreported: 3 } satisfies FolderScanRun
const scanFailed = { ...scanRunning, status: 'Failed', finishedUtc: '2026-09-30T09:01:00Z', error: 'The disk is gone.' } satisfies FolderScanRun
const inspectionRunning = inspectionRunOf({ id: '1a000000-0000-4000-8000-000000000009', status: 'Running', finishedUtc: null, filesTotal: 870, filesChecked: 100 })
const inspectionDone = { ...inspectionRunning, status: 'Succeeded', finishedUtc: '2026-09-30T09:05:00Z', filesChecked: 870 } satisfies FileInspectionRun
const inspectionFailed = { ...inspectionRunning, status: 'Failed', finishedUtc: '2026-09-30T09:05:00Z', error: 'ffprobe is missing.' } satisfies FileInspectionRun

interface State {
  /** Home Jellyfin's latest run. */
  sync?: LibrarySyncRun
  scan?: Partial<FolderScanOverview>
  inspection?: Partial<FileInspectionOverview>
  /** Replaces Home Jellyfin as the list's libraries. */
  libraries?: LibrarySource[]
}

/** What the sources answer from now on: a library that has synced before, the folders scanned, the files inspected. */
function serve({ sync = earlier, scan, inspection, libraries }: State = {}) {
  const list: LibrarySourceList = {
    items: libraries ?? [sourceOf({ latestRun: sync, lastSucceededRun: earlier })],
    scan: scanOf(scan ?? {}),
    inspection: inspectionOf(inspection ?? {}),
  }
  vi.mocked(libraryApi.sources).mockResolvedValue(list)
}

/** Each start answers the run it queued, and from then on the sources show it under way. */
function startsAnswer(order: string[], steps: { sync?: State; scan?: State; inspect?: State }) {
  vi.mocked(libraryApi.startSync).mockImplementation(async () => {
    order.push('sync')
    serve(steps.sync)
    return syncRunning
  })
  vi.mocked(libraryApi.startScan).mockImplementation(async () => {
    order.push('scan')
    serve(steps.scan)
    return scanRunning
  })
  vi.mocked(libraryApi.startInspection).mockImplementation(async () => {
    order.push('inspect')
    serve(steps.inspect)
    return inspectionRunning
  })
}

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

function renderPanel(signedIn: CurrentUser = owner, client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  signInAs(signedIn)
  const { unmount } = render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibrarySyncPanel />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup({ advanceTimers: vi.advanceTimersByTime }), unmount }
}

const refreshAll = () => screen.findByRole('button', { name: 'Refresh all' })

describe('LibrarySyncPanel Refresh all', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    serve()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('syncs, then scans, then inspects, starting each once the one before it has ended, and says which step it is on', async () => {
    // Arrange
    const order: string[] = []
    startsAnswer(order, {
      sync: { sync: syncRunning },
      scan: { sync: syncDone, scan: { activeRun: scanRunning, hasQueuedRun: true } },
      inspect: { sync: syncDone, scan: { lastFinishedRun: scanDone }, inspection: { activeRun: inspectionRunning, hasQueuedRun: true } },
    })
    const { user } = renderPanel()

    // Act
    await user.click(await refreshAll())

    // Assert — the sync is under way: nothing else starts, however many polls pass.
    expect(await screen.findByRole('button', { name: 'Syncing libraries…' })).toBeDisabled()
    await nextPoll()
    expect(order).toEqual(['sync'])

    serve({ sync: syncDone })
    await nextPoll()
    await vi.waitFor(() => expect(order).toEqual(['sync', 'scan']))
    expect(await screen.findByRole('button', { name: 'Scanning folders…' })).toBeDisabled()
    await nextPoll()
    expect(order).toEqual(['sync', 'scan'])

    serve({ sync: syncDone, scan: { lastFinishedRun: scanDone } })
    await nextPoll()
    await vi.waitFor(() => expect(order).toEqual(['sync', 'scan', 'inspect']))
    expect(await screen.findByRole('button', { name: 'Inspecting files…' })).toBeDisabled()
    await nextPoll()
    expect(order).toEqual(['sync', 'scan', 'inspect'])

    serve({ sync: syncDone, scan: { lastFinishedRun: scanDone }, inspection: { lastFinishedRun: inspectionDone } })
    await nextPoll()
    expect(await refreshAll()).toBeEnabled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(libraryApi.startSync).toHaveBeenCalledExactlyOnceWith(earlier.integrationId)
  })

  it('syncs every enabled library the app can read, and waits for all of them before the scan', async () => {
    // Arrange
    const officeId = '5d1c7a90-0000-4000-8000-000000000005'
    const home = (run: LibrarySyncRun) => sourceOf({ latestRun: run, lastSucceededRun: earlier })
    const office = (run: LibrarySyncRun) => sourceOf({ integrationId: officeId, name: 'Office Jellyfin', latestRun: { ...run, integrationId: officeId }, lastSucceededRun: earlier })
    const switchedOff = sourceOf({ integrationId: 'off', name: 'Old Jellyfin', isEnabled: false })
    const cannotRead = sourceOf({ integrationId: 'cannot', name: 'Strange', canReadLibrary: false })
    const both = (homeRun: LibrarySyncRun, officeRun: LibrarySyncRun) =>
      ({ libraries: [home(homeRun), office(officeRun), switchedOff, cannotRead] })
    serve(both(earlier, earlier))
    vi.mocked(libraryApi.startSync).mockImplementation(async () => {
      serve(both(syncRunning, syncRunning))
      return syncRunning
    })
    vi.mocked(libraryApi.startScan).mockResolvedValue(scanRunning)
    const { user } = renderPanel()

    // Act
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })
    serve(both(syncDone, syncRunning))
    await nextPoll()

    // Assert
    expect(vi.mocked(libraryApi.startSync).mock.calls.map(([id]) => id)).toEqual([earlier.integrationId, officeId])
    expect(libraryApi.startScan).not.toHaveBeenCalled()

    serve(both(syncDone, syncDone))
    await nextPoll()
    await vi.waitFor(() => expect(libraryApi.startScan).toHaveBeenCalledOnce())
  })

  it('joins a sync that is already under way instead of stopping at its refusal', async () => {
    // Arrange
    serve({ sync: syncRunning })
    vi.mocked(libraryApi.startSync).mockRejectedValue(apiError(409, '“Home Jellyfin” is already being synced.'))
    vi.mocked(libraryApi.startScan).mockResolvedValue(scanRunning)
    const { user } = renderPanel()
    await refreshAll()

    // Act
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })
    await nextPoll()

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(libraryApi.startScan).not.toHaveBeenCalled()

    serve({ sync: syncDone })
    await nextPoll()
    await vi.waitFor(() => expect(libraryApi.startScan).toHaveBeenCalledOnce())
  })

  it('stops at a sync the API refuses for any other reason, says why, and starts nothing more', async () => {
    // Arrange
    vi.mocked(libraryApi.startSync).mockRejectedValue(apiError(500, 'Database is locked.'))
    const { user } = renderPanel()

    // Act
    await user.click(await refreshAll())

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refreshing stopped. Database is locked.')
    expect(await refreshAll()).toBeEnabled()
    expect(libraryApi.startScan).not.toHaveBeenCalled()
    expect(libraryApi.startInspection).not.toHaveBeenCalled()
  })

  it('stops when a sync ends Failed, names the library, and clears that sentence when it starts again', async () => {
    // Arrange
    const order: string[] = []
    startsAnswer(order, { sync: { sync: syncRunning } })
    const { user } = renderPanel()
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })

    // Act
    serve({ sync: syncFailed })
    await nextPoll()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refreshing stopped. The sync of Home Jellyfin failed.')
    expect(order).toEqual(['sync'])
    expect(await refreshAll()).toBeEnabled()

    serve()
    await user.click(await refreshAll())
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('stops when the scan ends Failed, and does not inspect', async () => {
    // Arrange
    const order: string[] = []
    startsAnswer(order, {
      sync: { sync: syncRunning },
      scan: { sync: syncDone, scan: { activeRun: scanRunning, hasQueuedRun: true } },
    })
    const { user } = renderPanel()
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })
    serve({ sync: syncDone })
    await nextPoll()
    await screen.findByRole('button', { name: 'Scanning folders…' })

    // Act
    serve({ sync: syncDone, scan: { lastFinishedRun: scanFailed } })
    await nextPoll()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refreshing stopped. The scan of the media folders failed.')
    expect(order).toEqual(['sync', 'scan'])
  })

  it('stops when the inspection ends Failed', async () => {
    // Arrange
    const order: string[] = []
    const scanned = { sync: syncDone, scan: { lastFinishedRun: scanDone } }
    startsAnswer(order, {
      sync: { sync: syncRunning },
      scan: { sync: syncDone, scan: { activeRun: scanRunning, hasQueuedRun: true } },
      inspect: { ...scanned, inspection: { activeRun: inspectionRunning, hasQueuedRun: true } },
    })
    const { user } = renderPanel()
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })
    serve({ sync: syncDone })
    await nextPoll()
    await screen.findByRole('button', { name: 'Scanning folders…' })
    serve(scanned)
    await nextPoll()
    await screen.findByRole('button', { name: 'Inspecting files…' })

    // Act
    serve({ ...scanned, inspection: { lastFinishedRun: inspectionFailed } })
    await nextPoll()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refreshing stopped. The file inspection failed.')
    expect(order).toEqual(['sync', 'scan', 'inspect'])
  })

  it('says the API\'s sentence when it refuses the scan', async () => {
    // Arrange
    vi.mocked(libraryApi.startSync).mockImplementation(async () => {
      serve({ sync: syncRunning })
      return syncRunning
    })
    vi.mocked(libraryApi.startScan).mockRejectedValue(apiError(409, 'No media folder is allowed yet.'))
    const { user } = renderPanel()
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })

    // Act
    serve({ sync: syncDone })
    await nextPoll()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refreshing stopped. No media folder is allowed yet.')
    expect(libraryApi.startInspection).not.toHaveBeenCalled()
  })

  it('only syncs when no media folder is allowed', async () => {
    // Arrange
    const noFolders = { inspection: { hasAllowedRoots: false, lastFinishedRun: null }, scan: { lastFinishedRun: null } }
    serve({ ...noFolders })
    vi.mocked(libraryApi.startSync).mockImplementation(async () => {
      serve({ ...noFolders, sync: syncRunning })
      return syncRunning
    })
    const { user } = renderPanel()
    await user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })

    // Act
    serve({ ...noFolders, sync: syncDone })
    await nextPoll()

    // Assert
    expect(await refreshAll()).toBeEnabled()
    expect(libraryApi.startScan).not.toHaveBeenCalled()
    expect(libraryApi.startInspection).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('goes straight to the scan when no library is enabled', async () => {
    // Arrange
    const libraries = [sourceOf({ isEnabled: false })]
    serve({ libraries })
    vi.mocked(libraryApi.startScan).mockImplementation(async () => {
      serve({ libraries, scan: { activeRun: scanRunning, hasQueuedRun: true } })
      return scanRunning
    })
    const { user } = renderPanel()

    // Act
    await user.click(await refreshAll())

    // Assert
    expect(await screen.findByRole('button', { name: 'Scanning folders…' })).toBeDisabled()
    expect(libraryApi.startSync).not.toHaveBeenCalled()
  })

  it('is disabled with the reason for a member', async () => {
    // Arrange
    renderPanel(member)

    // Act
    const button = await refreshAll()

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner can refresh the library.')
  })

  it('is disabled with the reason when there is no library to sync and no media folder to scan', async () => {
    // Arrange
    serve({ libraries: [], inspection: { hasAllowedRoots: false, lastFinishedRun: null }, scan: { lastFinishedRun: null } })
    const { user } = renderPanel()

    // Act
    const button = await refreshAll()
    await user.click(button)

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Connect a media library or choose a media folder first.')
    expect(libraryApi.startSync).not.toHaveBeenCalled()
    expect(libraryApi.startScan).not.toHaveBeenCalled()
  })

  it('stops after the step under way when the page is left, and does not pick the chain up when the page is opened again', async () => {
    // Arrange
    startsAnswer([], { sync: { sync: syncRunning } })
    const first = renderPanel()
    await first.user.click(await refreshAll())
    await screen.findByRole('button', { name: 'Syncing libraries…' })
    first.unmount()
    serve({ sync: syncDone })

    // Act — the sources are read again, and show the sync ended.
    renderPanel(owner, first.client)
    await screen.findByRole('button', { name: 'Refresh all' })
    await act(() => vi.advanceTimersByTimeAsync(4_000))

    // Assert
    expect(libraryApi.startScan).not.toHaveBeenCalled()
    expect(libraryApi.startInspection).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Refresh all' })).toBeEnabled()
  })

  it('reads out the step it is on', async () => {
    // Arrange
    startsAnswer([], { sync: { sync: syncRunning } })
    const { user } = renderPanel()

    // Act
    await user.click(await refreshAll())

    // Assert
    await screen.findByRole('button', { name: 'Syncing libraries…' })
    const statuses = screen.getAllByRole('status').map((status) => status.textContent)
    expect(statuses).toContain('Syncing libraries…')
  })
})
