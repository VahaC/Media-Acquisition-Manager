import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { formatDateTime } from '@/lib/format'
import { profileApi } from '@/lib/profile-api'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { CurrentUser, RecommendationRun, RecommendationRunStage } from '@/lib/types'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { allTabsOn, guardians, listOf, martian, runOf } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/profile-api')

// MAM-33 — the run panel: when the lists were computed, polled every 2 s while a computation is under
// way, what a running one is doing and how far through it is, the owner's Recompute now, and the lists
// asked for again when a computation may have replaced them: the one the page followed ended, or a
// success it hadn't seen is the latest.

/** The format the panel shows its instants in. */
const format: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

const finished = runOf()
const queued = runOf({ id: '7e000000-0000-4000-8000-000000000002', status: 'Queued', trigger: 'Requested', startedUtc: null, finishedUtc: null, seedCount: null, candidateCount: null, createdUtc: '2026-10-05T09:00:00Z' })
const running = { ...queued, status: 'Running', startedUtc: '2026-10-05T09:00:01Z' } satisfies RecommendationRun
const succeeded = { ...running, status: 'Succeeded', finishedUtc: '2026-10-05T09:00:07Z', seedCount: 118, candidateCount: 241 } satisfies RecommendationRun
const failed = { ...running, status: 'Failed', finishedUtc: '2026-10-05T09:00:03Z', error: 'TMDb refused the API key.' } satisfies RecommendationRun
/** The computation queued behind `running` (a library sync's), in its three states before a success. */
const queuedNext = runOf({ id: '7e000000-0000-4000-8000-000000000003', status: 'Queued', trigger: 'LibrarySynced', startedUtc: null, finishedUtc: null, seedCount: null, candidateCount: null, createdUtc: '2026-10-05T09:00:05Z' })
const runningNext = { ...queuedNext, status: 'Running', startedUtc: '2026-10-05T09:00:08Z' } satisfies RecommendationRun
const failedNext = { ...runningNext, status: 'Failed', finishedUtc: '2026-10-05T09:00:09Z', error: 'TMDb refused the API key.' } satisfies RecommendationRun

/** The panel's status line. */
const runLine = () => within(screen.getByRole('region', { name: 'Computation' })).getByRole('status')

/** The bar of a running stage, if the panel shows one. */
const stageBar = () => within(screen.getByRole('region', { name: 'Computation' })).queryByRole('progressbar')

/**
 * The browser's clock while a test runs: two minutes after `running` started. The polls bring the
 * runs at about this time, which "started 2 minutes ago" is measured from.
 */
const NOW = new Date('2026-10-05T09:02:01Z')

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <RecommendationsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup({ advanceTimers: vi.advanceTimersByTime }) }
}

describe('RecommendationsPage run panel', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(NOW)
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [guardians]))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(finished)
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('tells %s when the lists were last computed', async (_, signedIn) => {
    // Arrange
    const time = formatDateTime(finished.finishedUtc!, format)

    // Act
    renderPage(signedIn)

    // Assert
    expect(await screen.findByText(`Computed ${time}.`)).toBe(runLine().querySelector('span'))
  })

  it('says the lists were never computed before the first computation', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(null)

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Not computed yet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Recompute now' })).toBeEnabled()
  })

  it('offers Recompute now to the owner only', async () => {
    // Arrange
    const signedIn = member

    // Act
    renderPage(signedIn)

    // Assert
    await screen.findByText(/^Computed /)
    expect(screen.queryByRole('button', { name: 'Recompute now' })).not.toBeInTheDocument()
  })

  it('disables Recompute now with the reason while no metadata catalog is connected', async () => {
    // Arrange
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [], { catalogConnected: false }))

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText('No metadata catalog is connected. Connect TMDb under Integrations to get recommendations.')
    const button = screen.getByRole('button', { name: 'Recompute now' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Connect a metadata catalog under Integrations first.')
  })

  it('tells the owner why the last computation failed', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(failed)

    // Act
    renderPage(owner)

    // Assert
    expect(await screen.findByText(/^Computing failed/))
      .toHaveTextContent(`Computing failed ${formatDateTime(failed.finishedUtc, format)}: TMDb refused the API key.`)
  })

  it('tells anyone else that the last computation failed and the owner can see why', async () => {
    // Arrange — the API leaves the sentence out for anyone but the owner.
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...failed, error: null })

    // Act
    renderPage(member)

    // Assert
    expect(await screen.findByText('The last computation failed. The owner can see why.')).toBeInTheDocument()
  })

  it('queues a computation from Recompute now and follows it to the end, then asks for the list once more', async () => {
    // Arrange
    vi.mocked(recommendationsApi.startRun).mockResolvedValue(queued)
    const { user } = renderPage(owner)
    await screen.findByText(/^Computed /)
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(queued)

    // Act
    await user.click(screen.getByRole('button', { name: 'Recompute now' }))
    await vi.waitFor(() => expect(runLine()).toHaveTextContent('Waiting to start'))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(running)
    await nextPoll()
    const whileRunning = runLine().textContent
    const listsWhileRunning = vi.mocked(recommendationsApi.list).mock.calls.length
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [martian, guardians]))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(succeeded)
    await nextPoll()

    // Assert
    expect(recommendationsApi.startRun).toHaveBeenCalledOnce()
    expect(whileRunning).toBe('Computing · started 2 minutes ago')
    expect(listsWhileRunning).toBe(1)
    expect(await screen.findByText(`Computed ${formatDateTime(succeeded.finishedUtc, format)}.`)).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: martian.title })).toBeInTheDocument()
    expect(recommendationsApi.list).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['waiting', queuedNext],
    ['running', runningNext],
    ['failed', failedNext],
  ])('asks for the lists again when the computation it followed ended between two polls and the next one is already %s', async (_, next) => {
    // Arrange — the server answers with the running computation, else the newest, so the success of
    // the one followed is never seen.
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(running)
    renderPage(owner)
    await screen.findByText('Computing · started 2 minutes ago')
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [martian, guardians]))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(next)

    // Act
    await nextPoll()

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: martian.title })).toBeInTheDocument()
    expect(recommendationsApi.list).toHaveBeenCalledTimes(2)
  })

  it('asks for the lists again when the latest computation is a success it never saw under way', async () => {
    // Arrange — a library sync's computation ran while nothing was polled; a refused Recompute now
    // asks for the latest run again and finds it.
    vi.mocked(recommendationsApi.startRun).mockRejectedValue(apiError(503, 'Too many computations are waiting.'))
    const { user } = renderPage(owner)
    await screen.findByText(/^Computed /)
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, [martian, guardians]))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(succeeded)

    // Act
    await user.click(screen.getByRole('button', { name: 'Recompute now' }))

    // Assert
    expect(await screen.findByText(`Computed ${formatDateTime(succeeded.finishedUtc, format)}.`)).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: martian.title })).toBeInTheDocument()
    expect(recommendationsApi.list).toHaveBeenCalledTimes(2)
  })

  it('stops polling once the computation has ended, and asks for nothing when one fails', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(running)
    renderPage(owner)
    await screen.findByText('Computing · started 2 minutes ago')
    await screen.findByRole('heading', { level: 2, name: guardians.title })
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(failed)
    await nextPoll()
    await screen.findByText(/^Computing failed/)
    const pollsUntilDone = vi.mocked(recommendationsApi.latestRun).mock.calls.length

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(pollsUntilDone).toBe(2)
    expect(recommendationsApi.latestRun).toHaveBeenCalledTimes(2)
    expect(recommendationsApi.list).toHaveBeenCalledOnce()
  })

  it.each<[RecommendationRunStage, string]>([
    ['ReadingLibrary', 'Reading your library'],
    ['FindingLibraryWorks', 'Looking up works that have no catalog id'],
    ['ReadingWorks', 'Reading your films and shows in the catalog'],
    ['ReadingCollections', 'Reading collections'],
    ['ReadingPeople', 'Reading directors and creators'],
    ['Discovering', 'Finding top-rated works for tabs with few seeds'],
    ['Saving', 'Saving the lists'],
  ])('says what a running computation is doing at %s, how far through it is and when it started', async (stage, text) => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...running, stage, stageDone: 240, stageTotal: 815 })

    // Act
    renderPage(member)

    // Assert
    await vi.waitFor(() => expect(runLine()).toHaveTextContent(`Computing · ${text}: 240 of 815 · started 2 minutes ago`))
    const bar = stageBar()
    expect(bar).toHaveAccessibleName(text)
    expect(bar).toHaveAttribute('aria-valuenow', '240')
    expect(bar).toHaveAttribute('aria-valuemax', '815')
    expect(bar).toHaveAttribute('aria-valuetext', '240 of 815')
  })

  it('writes the counts of a stage as the locale writes numbers', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...running, stage: 'ReadingWorks', stageDone: 1_240, stageTotal: 12_815 })

    // Act
    renderPage(member)

    // Assert
    await vi.waitFor(() => expect(runLine()).toHaveTextContent('Reading your films and shows in the catalog: 1,240 of 12,815'))
    expect(stageBar()).toHaveAttribute('aria-valuetext', '1,240 of 12,815')
  })

  it('names a stage without counts, with no bar', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...running, stage: 'Saving' })

    // Act
    renderPage(member)

    // Assert
    await vi.waitFor(() => expect(runLine()).toHaveTextContent('Computing · Saving the lists · started 2 minutes ago'))
    expect(stageBar()).not.toBeInTheDocument()
  })

  it('moves the progress and the time since the start on with each poll', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...running, stage: 'ReadingWorks', stageDone: 240, stageTotal: 815 })
    renderPage(member)
    await vi.waitFor(() => expect(runLine()).toHaveTextContent('240 of 815 · started 2 minutes ago'))
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...running, stage: 'ReadingWorks', stageDone: 610, stageTotal: 815 })
    vi.setSystemTime(new Date(NOW.getTime() + 60_000))

    // Act
    await nextPoll()

    // Assert
    await vi.waitFor(() => expect(runLine()).toHaveTextContent('Computing · Reading your films and shows in the catalog: 610 of 815 · started 3 minutes ago'))
    expect(stageBar()).toHaveAttribute('aria-valuenow', '610')
  })

  it('says a queued computation waits to start, with no stage and no bar', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(queued)

    // Act
    renderPage(member)

    // Assert
    await vi.waitFor(() => expect(runLine()).toHaveTextContent('Waiting to start'))
    expect(runLine().textContent).toBe('Waiting to start')
    expect(stageBar()).not.toBeInTheDocument()
  })

  it.each([
    ['succeeded', succeeded, `Computed ${formatDateTime(succeeded.finishedUtc, format)}.`],
    ['failed', failed, `Computing failed ${formatDateTime(failed.finishedUtc, format)}: TMDb refused the API key.`],
  ])('shows no stage once the computation has %s', async (_, ended, said) => {
    // Arrange — a stage left in an ended run's answer is not shown either.
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...running, stage: 'ReadingWorks', stageDone: 240, stageTotal: 815 })
    renderPage(owner)
    await vi.waitFor(() => expect(stageBar()).toBeInTheDocument())
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue({ ...ended, stage: 'Saving', stageDone: 815, stageTotal: 815 })

    // Act
    await nextPoll()

    // Assert
    await vi.waitFor(() => expect(runLine()).toHaveTextContent(said))
    expect(runLine().textContent).toBe(said)
    expect(stageBar()).not.toBeInTheDocument()
  })

  it('says why Recompute now was refused under the status line', async () => {
    // Arrange
    vi.mocked(recommendationsApi.startRun).mockRejectedValue(apiError(409, 'Connect a metadata catalog under Integrations first.'))
    const { user } = renderPage(owner)
    await screen.findByText(/^Computed /)

    // Act
    await user.click(screen.getByRole('button', { name: 'Recompute now' }))

    // Assert
    const refusal = await within(screen.getByRole('region', { name: 'Computation' })).findByRole('alert')
    expect(refusal).toHaveTextContent('Connect a metadata catalog under Integrations first.')
  })

  it('says the latest computation could not be loaded, and Try again loads it', async () => {
    // Arrange
    vi.mocked(recommendationsApi.latestRun)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockResolvedValueOnce(finished)
    const { user } = renderPage(member)
    const panel = await screen.findByRole('region', { name: 'Computation' })
    const errorState = await within(panel).findByRole('alert')
    const said = errorState.textContent

    // Act
    await user.click(within(errorState).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(said).toContain('Could not load the latest computation.')
    expect(said).toContain('The database is unavailable.')
    expect(await within(panel).findByText(/^Computed /)).toBeInTheDocument()
  })
})
