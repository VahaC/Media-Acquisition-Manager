import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { replacementsApi } from '@/lib/replacements-api'
import type { CurrentUser, DownloadJob, DownloadJobReplacement, DownloadJobStatus, ReplacementDetails, ReplacementJobState } from '@/lib/types'
import { JobsPage } from '@/pages/JobsPage'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, jobOf, jobsPage, sentMinutesAgo } from '@/test/downloads'
import { jobCopyOf, replacementDetailsOf, replacementOf } from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-14 — the Jobs page asks for its list again every 2 s while it shows a job the app still
// follows (Created, Submitted, Downloading, Paused), and stops once every job on it has ended. MAM-25 —
// and while it shows a downloaded job whose replacement's new files are still checked (Downloaded,
// Inspecting), as the check ends by itself. MAM-26 — and while its old copies are about to move or
// moving (Verified with replacement on, Quarantining, Restoring, Deleting), until it rests
// (Quarantined) or ends. MAM-27 — an owner's row opened on its replacement's copies asks for them every
// 2 s while the check runs, and otherwise again only when the list brings its replacement changed.
// MAM-41 — whatever the tab: Needs you, whose jobs wait for the user, asks nothing more; the copies are
// in the details the row's chevron opens.

/** A downloaded job carrying a replacement in `state`; `switchedOff` while replacement is switched off. */
const replacingJob = (state: ReplacementJobState, switchedOff = false) =>
  jobOf({ status: 'Downloaded', progress: 1, replacement: replacementOf({ state, switchedOff }) })

/** The server lists `items` for every request from now on. */
function listing(...items: DownloadJob[]) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items))
}

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

/** The table row of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, { selector: '.settings-name-text' }).closest('tr')!

function renderPage(signedIn: CurrentUser = member, path = '/jobs') {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <ConfirmProvider>
          <JobsPage />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  // The fake clock moves with real time, so the clicks' own short waits pass.
  return { user: userEvent.setup({ advanceTimers: vi.advanceTimersByTime }) }
}

/** The owner's downloaded job carrying a replacement with `changes`. */
const ownersJob = (changes: Partial<DownloadJobReplacement>) =>
  jobOf({ status: 'Downloaded', progress: 1, replacement: replacementOf(changes) })

/** The replacement's details in `state`, with one ticked copy, so its Copies show a table. */
const detailsIn = (state: ReplacementJobState): ReplacementDetails => replacementDetailsOf({
  state,
  canRestore: false,
  canDeleteForGood: false,
  names: [],
  copies: [jobCopyOf({ status: state === 'Verified' ? 'Paired' : state === 'NeedsReview' ? 'NotPaired' : 'Waiting' })],
})

/** Opens the details of the job's row and waits for its copies' table. */
async function openCopies(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })
  await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Show details' }))
  await screen.findByRole('region', { name: 'Old copies and their new files' })
}

/** How many times the replacement's details were asked for. */
const detailsAsked = () => vi.mocked(replacementsApi.get).mock.calls.length

describe('JobsPage while a job is followed', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('follows a job from downloading through its progress to downloaded, then asks nothing more', async () => {
    // Arrange
    listing(downloadingJob, downloadedJob)
    renderPage()
    await screen.findByText('45.6%')
    listing({ ...downloadingJob, progress: 0.9 }, downloadedJob)

    // Act
    await nextPoll()
    await within(rowOf(downloadingJob.releaseTitle)).findByText('90%')
    listing({ ...downloadingJob, status: 'Downloaded', progress: 1 }, downloadedJob)
    await nextPoll()
    await within(rowOf(downloadingJob.releaseTitle)).findByText('Downloaded')
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(3)
  })

  it.each<DownloadJobStatus>(['Created', 'Submitted', 'Downloading', 'Paused'])('asks again every 2 s while a %s job is on the page', async (status) => {
    // Arrange
    listing(jobOf({ status }), downloadedJob)
    renderPage()
    await screen.findByText(downloadedJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(3)
  })

  it('offers Cancel on a Created job once a poll finds it 5 minutes old, with no timer of its own', async () => {
    // Arrange
    listing(jobOf({ status: 'Created', clientJobId: null, createdUtc: sentMinutesAgo(4.98) }))
    renderPage()
    await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })
    const before = within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })

    // Act
    await nextPoll()

    // Assert
    expect(before).not.toBeInTheDocument()
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByRole('button', { name: 'Cancel job' })).toBeInTheDocument()
  })

  it.each<DownloadJobStatus>(['Downloaded', 'Failed', 'NeedsLinking', 'Cancelled'])('asks nothing more while every job on the page is %s', async (status) => {
    // Arrange
    listing(jobOf({ status }))
    renderPage()
    await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledOnce()
  })

  it.each<ReplacementJobState>(['Downloaded', 'Inspecting', 'Verified', 'Quarantining', 'Restoring', 'Deleting'])('asks again every 2 s while a downloaded job\'s replacement is %s', async (state) => {
    // Arrange
    listing(replacingJob(state), downloadedJob)
    renderPage()
    await screen.findByText(downloadedJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(3)
  })

  it.each<ReplacementJobState>(['Quarantined', 'NeedsReview', 'Completed', 'Restored', 'Failed', 'Cancelled'])('asks nothing more while a downloaded job\'s replacement is %s', async (state) => {
    // Arrange
    listing(replacingJob(state), downloadedJob)
    renderPage()
    await screen.findByText(downloadedJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledOnce()
  })

  // MAM-26 — a Verified job waits for the switch while it is off (ADR 0033), so asking again would bring
  // the same answer; the check of the new files and a rename under way go on whatever the switch.
  it.each<[ReplacementJobState, number]>([
    ['Verified', 1],
    ['Inspecting', 3],
    ['Quarantining', 3],
  ])('while replacement is switched off, asks for a downloaded job whose replacement is %s %i time(s) over two polls', async (state, asked) => {
    // Arrange
    listing(replacingJob(state, true))
    renderPage()
    await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(asked)
  })

  it('follows a replacement from Inspecting through Verified and the move until its old copy is in the quarantine, then asks nothing more', async () => {
    // Arrange
    listing(replacingJob('Inspecting'))
    renderPage(owner)
    await screen.findByText('Checking the new files')
    listing(replacingJob('Verified'))

    // Act
    await nextPoll()
    await screen.findByText('Verified, moves next')
    listing(replacingJob('Quarantining'))
    await nextPoll()
    await screen.findByText('Moving into the quarantine…')
    listing(jobOf({ status: 'Downloaded', progress: 1, replacement: replacementOf({ state: 'Quarantined', quarantinedCopyCount: 1 }) }))
    await nextPoll()
    await screen.findByText('1 old copy in the quarantine')
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(4)
  })

  // MAM-27 — Keep both ends only the replacement: the download it rode on is still followed.
  it('keeps asking every 2 s after Keep both on a job still downloading, as its download goes on', async () => {
    // Arrange
    const job = jobOf({ status: 'Downloading', progress: 0.3, replacement: replacementOf({ state: 'Downloading', canCancel: true }) })
    const kept = { ...job, replacement: replacementOf({ state: 'Cancelled', reason: 'You cancelled the replacement, so nothing will be replaced.' }) }
    vi.mocked(replacementsApi.cancel).mockResolvedValue(kept)
    listing(job)
    const { user } = renderPage(owner)
    await screen.findByText(job.releaseTitle, { selector: '.settings-name-text' })
    await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Keep both' }))
    const confirmation = await screen.findByRole('dialog', { name: `Keep both copies of ${job.releaseTitle}?` })
    listing(kept)
    await user.click(within(confirmation).getByRole('button', { name: 'Keep both' }))
    await screen.findByText('Cancelled: nothing moved')
    const asked = vi.mocked(downloadsApi.list).mock.calls.length

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(asked + 2)
  })

  // MAM-27 — a replacement the check stopped asks nothing; its Retry sends it back to the check, which
  // goes on by itself.
  it('asks again every 2 s once Retry sends a replacement the check stopped back to the check', async () => {
    // Arrange
    const stopped = ownersJob({ state: 'NeedsReview', stop: 'Check', reason: 'Jellyfin gave “Alien.1979.2160p.Remux.mkv” no TMDb or IMDb id within an hour.', canRetry: true })
    const retried = ownersJob({ state: 'Downloaded' })
    vi.mocked(replacementsApi.retry).mockResolvedValue(retried)
    listing(stopped)
    const { user } = renderPage(owner)
    await screen.findByText(stopped.releaseTitle, { selector: '.settings-name-text' })
    await nextPoll()
    const whileStopped = vi.mocked(downloadsApi.list).mock.calls.length
    listing(retried)
    await user.click(within(rowOf(stopped.releaseTitle)).getByRole('button', { name: 'Retry' }))
    await screen.findByText('Checking the new files')
    const retriedAt = vi.mocked(downloadsApi.list).mock.calls.length

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(whileStopped).toBe(1)
    expect(downloadsApi.list).toHaveBeenCalledTimes(retriedAt + 2)
  })

  it.each<ReplacementJobState>(['Downloaded', 'Inspecting'])('asks for an opened row\'s copies every 2 s while the check runs (%s), and no more once it stopped', async (state) => {
    // Arrange
    vi.mocked(replacementsApi.get).mockResolvedValue(detailsIn(state))
    listing(ownersJob({ state }))
    const { user } = renderPage(owner)
    await openCopies(user)

    // Act
    await nextPoll()
    await nextPoll()
    const whileChecked = detailsAsked()
    vi.mocked(replacementsApi.get).mockResolvedValue(detailsIn('NeedsReview'))
    listing(ownersJob({ state: 'NeedsReview', stop: 'Check', reason: 'Jellyfin gave “Alien.1979.2160p.Remux.mkv” no TMDb or IMDb id within an hour.', canRetry: true }))
    await nextPoll()
    await nextPoll()
    const onceStopped = detailsAsked()
    await nextPoll()
    await nextPoll()

    // Assert
    expect(whileChecked).toBe(3)
    expect(onceStopped).toBeGreaterThan(whileChecked)
    expect(detailsAsked()).toBe(onceStopped)
    expect(await screen.findByText('Not paired.')).toBeInTheDocument()
  })

  it.each<[string, Partial<DownloadJobReplacement>, number]>([
    ['asks for them again when the list brings its replacement changed', { state: 'Quarantining' }, 2],
    ['asks nothing when the list brings it as it was', {}, 1],
    ['asks nothing when the list changes only what the copies don\'t show', { copiesOutsideTickedFolders: 1 }, 1],
  ])('with an opened row whose check is over, %s', async (_, changes, asked) => {
    // Arrange
    vi.mocked(replacementsApi.get).mockResolvedValue(detailsIn('Verified'))
    listing(ownersJob({ state: 'Verified' }))
    const { user } = renderPage(owner)
    await openCopies(user)
    await nextPoll()
    listing(ownersJob({ state: 'Verified', ...changes }))

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(detailsAsked()).toBe(asked)
  })

  it('asks nothing more on the Needs you tab while every job on it waits for the user', async () => {
    // Arrange
    listing(
      ownersJob({ state: 'NeedsReview', stop: 'Move', reason: 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”.', canRetry: true }),
      jobOf({ id: 'd0b10000-0000-4000-8000-000000000030', releaseTitle: 'Arrival (2016) 2160p', status: 'NeedsLinking' }),
      jobOf({ id: 'd0b10000-0000-4000-8000-000000000031', releaseTitle: 'Heat (1995) 2160p', status: 'Downloaded', progress: 1, replacement: replacementOf({ state: 'Quarantined', quarantinedCopyCount: 1, canRestore: true, canDeleteForGood: true }) }),
    )
    renderPage(owner, '/jobs?tab=NeedsYou')
    await screen.findByText('Heat (1995) 2160p', { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledExactlyOnceWith({ view: 'NeedsYou', page: 1, pageSize: 25 })
  })

  it('asks again every 2 s on the In progress tab while a job on it is followed', async () => {
    // Arrange
    listing(downloadingJob)
    renderPage(member, '/jobs?tab=InProgress')
    await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(vi.mocked(downloadsApi.list).mock.calls.map(([query]) => query.view)).toEqual(['InProgress', 'InProgress', 'InProgress'])
  })

  it('asks nothing more while the list is empty', async () => {
    // Arrange
    listing()
    renderPage()
    await screen.findByText(/No download jobs yet\./)

    // Act
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledOnce()
  })
})
