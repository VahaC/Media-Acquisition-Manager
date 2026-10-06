import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { libraryApi } from '@/lib/library-api'
import { qk } from '@/lib/queries'
import type { FileInspectionOverview, FileInspectionRun, LibraryWork } from '@/lib/types'
import { LibraryPage } from '@/pages/LibraryPage'
import { owner, signInAs } from '@/test/auth-session'
import { aliens, aliensInspection, inspectionOf, inspectionRunOf, locations, sourceOf, sourcesOf, worksPage } from '@/test/library'
import { openSourceDetails } from '@/test/sources-details'

vi.mock('@/lib/library-api')

// MAM-9 — the Library page while the files are inspected: the sources are polled every 2 s while a
// run is queued or running, and the works are asked for again whenever an answer shows the run
// checked more files, or that it has finished, so the facts fill in on the cards without a reload.

const earlierRun = inspectionRunOf({})
const runId = '1a000000-0000-4000-8000-000000000009'
const queued = inspectionRunOf({ id: runId, trigger: 'Requested', status: 'Queued', createdUtc: '2026-09-30T09:00:00Z', startedUtc: null, finishedUtc: null, filesTotal: null, filesChecked: 0, filesProbed: 0 })
const started = { ...queued, status: 'Running', startedUtc: '2026-09-30T09:00:01Z', filesTotal: 870 } satisfies FileInspectionRun
const succeeded = { ...started, status: 'Succeeded', finishedUtc: '2026-09-30T09:10:00Z', filesChecked: 870, filesProbed: 12 } satisfies FileInspectionRun

/** Aliens as the works answer it: its Special Edition not inspected yet, as in the fixture. */
const before = aliens
/** Aliens once the run has read its Special Edition too. */
const after: LibraryWork = {
  ...aliens,
  versions: aliens.versions.map((version) => ({
    ...version,
    files: version.files.map((file) => ({ ...file, inspection: aliensInspection })),
  })),
}

/** The next answers of the sources: the inspection stands as `changes` set it. */
function inspectionIs(changes: Partial<FileInspectionOverview>) {
  vi.mocked(libraryApi.sources).mockResolvedValue({ ...sourcesOf(sourceOf({})), inspection: inspectionOf({ lastFinishedRun: earlierRun, ...changes }) })
}

/** The next answers of the works: Aliens as `work`. */
function worksAre(work: LibraryWork) {
  vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, [work]))
}

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

async function renderPage({ details = true } = {}) {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
  if (details) await openSourceDetails(user)
  return { client, user }
}

describe('LibraryPage while the files are inspected', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    worksAre(before)
    vi.mocked(libraryApi.locations).mockResolvedValue(locations)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('asks for the works again as the run checks more files, and once more when it has finished', async () => {
    // Arrange
    inspectionIs({ activeRun: { ...started, filesChecked: 100 } })
    await renderPage()
    await screen.findByText('Inspecting files… 100 of 870 checked')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await nextPoll()
    const worksWhileStill = vi.mocked(libraryApi.works).mock.calls.length
    inspectionIs({ activeRun: { ...started, filesChecked: 400 } })
    await nextPoll()
    await screen.findByText('Inspecting files… 400 of 870 checked')
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    worksAre(after)
    inspectionIs({ lastFinishedRun: succeeded })

    // Act
    await nextPoll()

    // Assert
    expect(await screen.findByText(/^Files last inspected .*: 870 checked, 12 read by ffprobe\.$/)).toBeInTheDocument()
    expect(worksWhileStill).toBe(1)
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(3))
    await vi.waitFor(() => expect(screen.queryByText('Not inspected yet')).not.toBeInTheDocument())
    expect(libraryApi.locations).toHaveBeenCalledOnce()
  })

  it('drops the TMDb links\' queries once a run has finished, not as it checks more files', async () => {
    // Arrange — MAM-34: the measured lengths feed the suggestions' runtime evidence, but asking for an
    // open dialog's suggestions on every poll of a run would be too often.
    inspectionIs({ activeRun: { ...started, filesChecked: 100 } })
    const { client } = await renderPage()
    await screen.findByText('Inspecting files… 100 of 870 checked')
    client.setQueryData(qk.library.links.queue(0), { totalCount: 0, index: 0, current: null })
    inspectionIs({ activeRun: { ...started, filesChecked: 400 } })
    await nextPoll()
    await screen.findByText('Inspecting files… 400 of 870 checked')
    const whileChecking = client.getQueryState(qk.library.links.queue(0))?.isInvalidated
    inspectionIs({ lastFinishedRun: succeeded })

    // Act
    await nextPoll()

    // Assert
    expect(whileChecking).toBe(false)
    await vi.waitFor(() => expect(client.getQueryState(qk.library.links.queue(0))?.isInvalidated).toBe(true))
  })

  it('does not ask for the works again while a run only waits or has just started', async () => {
    // Arrange
    inspectionIs({ activeRun: queued })
    await renderPage()
    await screen.findByText('Waiting to inspect files…')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    inspectionIs({ activeRun: { ...started, filesTotal: null } })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(await screen.findByText('Inspecting files…')).toBeInTheDocument()
    expect(libraryApi.sources).toHaveBeenCalledTimes(3)
    expect(libraryApi.works).toHaveBeenCalledOnce()
  })

  it('stops polling once the run has ended', async () => {
    // Arrange
    inspectionIs({ activeRun: { ...started, filesChecked: 100 } })
    await renderPage()
    await screen.findByText('Inspecting files… 100 of 870 checked')
    inspectionIs({ lastFinishedRun: succeeded })
    await nextPoll()
    await screen.findByText(/^Files last inspected /)
    const pollsUntilDone = vi.mocked(libraryApi.sources).mock.calls.length

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(pollsUntilDone).toBe(2)
    expect(libraryApi.sources).toHaveBeenCalledTimes(2)
  })

  it('asks for the works again when a run the owner started has finished by the next answer', async () => {
    // Arrange — a small library: the refetch after Inspect files already finds the run finished.
    inspectionIs({})
    vi.mocked(libraryApi.startInspection).mockImplementation(async () => {
      worksAre(after)
      inspectionIs({ lastFinishedRun: succeeded })
      return queued
    })
    const { user } = await renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await screen.findByText(/^Files last inspected /)

    // Act
    await user.click(screen.getByRole('button', { name: 'Inspect files' }))

    // Assert
    expect(await screen.findByText(/: 870 checked, 12 read by ffprobe\.$/)).toBeInTheDocument()
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    expect(libraryApi.sources).toHaveBeenCalledTimes(2)
    expect(libraryApi.startInspection).toHaveBeenCalledOnce()
  })

  it('asks for the works once when a finished sync and a moved inspection arrive in the same answer', async () => {
    // Arrange — the sync's run succeeds while the inspection under way checked more files.
    const syncing = { ...sourceOf({}).latestRun!, id: '5e000000-0000-4000-8000-000000000009', status: 'Running' as const, finishedUtc: null }
    vi.mocked(libraryApi.sources).mockResolvedValue({
      ...sourcesOf(sourceOf({ latestRun: syncing })),
      inspection: inspectionOf({ activeRun: { ...started, filesChecked: 100 } }),
    })
    await renderPage()
    await screen.findByText('Inspecting files… 100 of 870 checked')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    vi.mocked(libraryApi.sources).mockResolvedValue({
      ...sourcesOf(sourceOf({ latestRun: { ...syncing, status: 'Succeeded', finishedUtc: '2026-09-30T09:00:05Z' } })),
      inspection: inspectionOf({ activeRun: { ...started, filesChecked: 200 } }),
    })

    // Act
    await nextPoll()

    // Assert
    await screen.findByText('Inspecting files… 200 of 870 checked')
    await vi.waitFor(() => expect(libraryApi.works).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(libraryApi.locations).toHaveBeenCalledTimes(2))
    expect(libraryApi.works).toHaveBeenCalledTimes(2)
  })
})
