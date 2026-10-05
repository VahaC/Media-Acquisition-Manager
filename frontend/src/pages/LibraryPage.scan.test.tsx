import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { libraryApi } from '@/lib/library-api'
import { qk } from '@/lib/queries'
import type { FileInspectionOverview, FileInspectionRun, FolderScanOverview, FolderScanRun } from '@/lib/types'
import { LibraryPage } from '@/pages/LibraryPage'
import { owner, signInAs } from '@/test/auth-session'
import { aliens, inspectionOf, inspectionRunOf, locations, scanOf, scanRunOf, sourceOf, sourcesOf, worksPage } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-18 — the Library page while the media folders are scanned: the sources are polled every 2 s
// while a scan is queued or running, and no more once it has ended. The works stay as they were: a
// scan stores no work, and the inspection it queues is followed on its own — queued a moment after
// the scan ends, so an answer that shows the scan ended with no inspection under way is followed by
// one more ask 2 s later, and the polling goes on from there when that shows the inspection.

const scanId = '5ca00000-0000-4000-8000-000000000009'
const queued = scanRunOf({ id: scanId, status: 'Queued', createdUtc: '2026-09-30T09:00:00Z', startedUtc: null, finishedUtc: null, filesFound: 0, filesUnreported: null })
const running = { ...queued, status: 'Running', startedUtc: '2026-09-30T09:00:01Z', filesFound: 400 } satisfies FolderScanRun
const succeeded = { ...running, status: 'Succeeded', finishedUtc: '2026-09-30T09:01:00Z', filesFound: 1_394, filesUnreported: 12 } satisfies FolderScanRun

/** The inspection the scan queued, reading the twelve names it stored. */
const inspecting = inspectionRunOf({ id: '1a000000-0000-4000-8000-000000000018', trigger: 'FolderScanned', status: 'Running', requestedByUserName: null, createdUtc: '2026-09-30T09:01:01Z', startedUtc: '2026-09-30T09:01:02Z', finishedUtc: null, filesTotal: 12, filesChecked: 3, filesProbed: 3 })
const inspected = { ...inspecting, status: 'Succeeded', finishedUtc: '2026-09-30T09:01:30Z', filesChecked: 12, filesProbed: 12 } satisfies FileInspectionRun

/** The next answers of the sources: the scan stands as `changes` set it. */
function scanIs(changes: Partial<FolderScanOverview>) {
  vi.mocked(libraryApi.sources).mockResolvedValue({ ...sourcesOf(sourceOf({})), scan: scanOf(changes) })
}

/** The next answers of the sources: the scan has ended, and the inspection stands as `changes` set it. */
function scannedAndInspectionIs(changes: Partial<FileInspectionOverview>) {
  vi.mocked(libraryApi.sources).mockResolvedValue({
    ...sourcesOf(sourceOf({})),
    scan: scanOf({ lastFinishedRun: succeeded }),
    inspection: inspectionOf(changes),
  })
}

/** The status line of the Media folders row. */
const scanStatus = () => within(screen.getByText('Media folders').closest('li')!).getByRole('status')

/** The status line of the inspection's row. */
const inspectionStatus = () => within(document.querySelector<HTMLElement>('.library-inspection')!).getByRole('status')

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
  return { client, user: userEvent.setup({ advanceTimers: vi.advanceTimersByTime }) }
}

describe('LibraryPage while the media folders are scanned', () => {
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

  it('asks for the sources every 2 s while a scan is under way, and shows how many files it found', async () => {
    // Arrange
    scanIs({ activeRun: queued, hasQueuedRun: true })
    renderPage()
    await screen.findByText('Waiting to scan the media folders…')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    scanIs({ activeRun: running })
    await nextPoll()
    const whileRunning = scanStatus().textContent
    scanIs({ activeRun: { ...running, filesFound: 900 } })

    // Act
    await nextPoll()

    // Assert
    expect(await screen.findByText('Scanning the media folders… 900 files found')).toBeInTheDocument()
    expect(whileRunning).toBe('Scanning the media folders… 400 files found')
    expect(libraryApi.sources).toHaveBeenCalledTimes(3)
    expect(libraryApi.works).toHaveBeenCalledOnce()
  })

  it('stops polling once the scan has ended and the one more ask shows no inspection, and leaves the works as they were', async () => {
    // Arrange
    scanIs({ activeRun: running })
    renderPage()
    await screen.findByText('Scanning the media folders… 400 files found')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    scanIs({ lastFinishedRun: succeeded })
    await nextPoll()
    await screen.findByText(/^Scanned .*: 1,394 files, 12 in no library\.$/)
    const pollsUntilDone = vi.mocked(libraryApi.sources).mock.calls.length

    // Act
    await nextPoll()
    await nextPoll()
    await nextPoll()

    // Assert
    // The answer that showed the scan ended showed no inspection under way, so the sources were asked
    // once more 2 s later; that answer showed nothing under way either, and the polling stopped.
    expect(pollsUntilDone).toBe(2)
    expect(libraryApi.sources).toHaveBeenCalledTimes(3)
    expect(libraryApi.works).toHaveBeenCalledOnce()
    expect(libraryApi.locations).toHaveBeenCalledOnce()
  })

  it('asks for the sources once more 2 s after a scan ends with no inspection under way, and shows the inspection it queued', async () => {
    // Arrange — the scan ends between two answers, and the inspection it queues shows only in the next.
    scanIs({ activeRun: running })
    renderPage()
    await screen.findByText('Scanning the media folders… 400 files found')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    scannedAndInspectionIs({})
    await nextPoll()
    await screen.findByText(/^Scanned .*: 1,394 files, 12 in no library\.$/)
    const pollsWhenTheScanEnded = vi.mocked(libraryApi.sources).mock.calls.length
    const inspectionWhenTheScanEnded = inspectionStatus().textContent
    scannedAndInspectionIs({ activeRun: inspecting })

    // Act
    await nextPoll()

    // Assert
    await vi.waitFor(() => expect(inspectionStatus()).toHaveTextContent('Inspecting files… 3 of 12 checked'))
    expect(pollsWhenTheScanEnded).toBe(2)
    expect(inspectionWhenTheScanEnded).toMatch(/^Files last inspected /)
    expect(libraryApi.sources).toHaveBeenCalledTimes(3)
    // From here the inspection is followed as any other: polled while it is under way.
    await nextPoll()
    expect(libraryApi.sources).toHaveBeenCalledTimes(4)
  })

  it('asks for nothing beyond its polling when the answer that shows the scan ended already shows its inspection', async () => {
    // Arrange
    scanIs({ activeRun: running })
    const { client } = renderPage()
    await screen.findByText('Scanning the media folders… 400 files found')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    scannedAndInspectionIs({ activeRun: inspecting })
    await nextPoll()
    await vi.waitFor(() => expect(inspectionStatus()).toHaveTextContent('Inspecting files… 3 of 12 checked'))
    scannedAndInspectionIs({ lastFinishedRun: inspected })

    // Act
    await nextPoll()
    await nextPoll()
    await nextPoll()

    // Assert
    // The inspection was in sight, so the polling followed it to its end and nothing else asked for the
    // sources. An extra ask would fall on the same 2 s as a poll and merge with it, so it is told by the
    // sources being invalidated.
    await vi.waitFor(() => expect(inspectionStatus()).toHaveTextContent(/^Files last inspected .*: 12 checked, 12 read by ffprobe\.$/))
    expect(libraryApi.sources).toHaveBeenCalledTimes(3)
    expect(invalidate).not.toHaveBeenCalledWith({ queryKey: qk.library.sources })
  })

  it('starts polling when the owner starts a scan on a page that was not polling', async () => {
    // Arrange — the page opens on a finished scan, so nothing is polled until Scan folders.
    scanIs({})
    vi.mocked(libraryApi.startScan).mockImplementation(async () => {
      scanIs({ activeRun: queued, hasQueuedRun: true })
      return queued
    })
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await nextPoll()
    const pollsBeforeStart = vi.mocked(libraryApi.sources).mock.calls.length
    await user.click(await screen.findByRole('button', { name: 'Scan folders' }))
    await screen.findByText('Waiting to scan the media folders…')
    await vi.waitFor(() => expect(libraryApi.sources).toHaveBeenCalledTimes(2))
    scanIs({ activeRun: running })

    // Act
    await nextPoll()

    // Assert
    expect(await screen.findByText('Scanning the media folders… 400 files found')).toBeInTheDocument()
    expect(pollsBeforeStart).toBe(1)
    expect(libraryApi.sources).toHaveBeenCalledTimes(3)
    expect(libraryApi.startScan).toHaveBeenCalledOnce()
  })
})
