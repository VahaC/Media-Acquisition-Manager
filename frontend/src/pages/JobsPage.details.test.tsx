import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { formatDateTime } from '@/lib/format'
import { replacementsApi } from '@/lib/replacements-api'
import type { CurrentUser, DownloadJob, DownloadJobReplacement, DownloadJobStatus, DownloadOverwriteCheck } from '@/lib/types'
import { JobsPage } from '@/pages/JobsPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, jobOf, jobsPage } from '@/test/downloads'
import { jobCopyOf, REPLACEMENT_ID, replacementDetailsOf, replacementOf } from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-41 — the chevron before a Jobs row's title opens the row's details in a row of their own under it:
// every sentence the cells cut short or leave out (MAM-14's block reason and error, MAM-38's unchecked
// send, MAM-24…27's replacement line), the replacement's old copies with Drop (MAM-27's Copies, which
// left the row's actions: details, not an action), where the release came from, the client's id, and
// the way to the job's page.

/** Matches a job's release title only. */
const asTitle = { selector: '.settings-name-text' }

/** The table row (or phone card) of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, asTitle).closest('tr')!

/** The row right under the job's own: its details while they are open. */
const detailsOf = (title: string) => rowOf(title).nextElementSibling as HTMLElement | null

/** The server lists `items` for every page and view asked for. */
function listing(...items: DownloadJob[]) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items))
}

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/jobs']}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs" element={<JobsPage />} />
            <Route path="jobs/:id" element={<p>Job details</p>} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** Opens the details of the job's row and returns the row they open in. */
async function openDetails(user: ReturnType<typeof userEvent.setup>, title: string) {
  await screen.findByText(title, asTitle)
  await user.click(within(rowOf(title)).getByRole('button', { name: 'Show details' }))
  return detailsOf(title)!
}

// The server's sentences.
const BLOCK_REASON = '“Local qBittorrent” couldn\'t be asked about this download. Ask the owner to check “Local qBittorrent” under Integrations.'
const WAITS_UNTIL_UTC = '2026-10-11T14:05:00Z'
const waitsUntil = formatDateTime(WAITS_UNTIL_UTC, { timeStyle: 'short' })
const HELD_REASON = 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.'
const NO_ID_REASON = 'Jellyfin gave “Alien.1979.2160p.Remux.mkv” no TMDb or IMDb id within an hour, so it can\'t be compared with Alien (1979).'
const RENUMBERED_REASON = 'Jellyfin numbers “Yellowstone.S02E05.2160p.mkv” as season 2, episode 6, not the episode “Yellowstone S02E05.mkv” is.'
const DOWNLOAD_REASON = 'qBittorrent\'s downloads couldn\'t be told apart for this release, so the app can\'t follow it to verify the new file.'
const KEPT_BOTH_REASON = 'You cancelled the replacement, so nothing will be replaced.'

const blockedJob = jobOf({
  id: 'd0b10000-0000-4000-8000-000000000003',
  releaseTitle: 'Arrival (2016) 1080p WEB-DL',
  status: 'Downloading',
  progress: 0.2,
  blockReason: BLOCK_REASON,
})

/** The owner's job carrying a replacement with `changes`; `status` sets the download's. */
const replacingJob = (changes: Partial<DownloadJobReplacement>, status: DownloadJobStatus = 'Downloaded') => jobOf({
  id: 'd0b10000-0000-4000-8000-000000000008',
  releaseTitle: 'Alien (1979) 2160p Remux',
  status,
  progress: status === 'Downloaded' ? 1 : 0.3,
  replacement: replacementOf(changes),
})

describe('JobsPage row details', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    // A replacement that lists no copy: its Copies show nothing unless a test is about them.
    vi.mocked(replacementsApi.get).mockResolvedValue(replacementDetailsOf({ copies: [] }))
  })

  // ── the chevron ──

  it('puts a chevron before each title, closed: named Show details, in its title too, pointing at nothing yet', async () => {
    // Arrange
    listing(downloadingJob)

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    const chevron = within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Show details' })
    expect(chevron).toHaveAttribute('aria-expanded', 'false')
    expect(chevron).not.toHaveAttribute('aria-controls')
    expect(chevron).toHaveAttribute('title', 'Show details')
    expect(chevron).toHaveClass('jobs-expand')
    expect(chevron.closest('td')).toHaveClass('jobs-release-cell')
    expect(detailsOf(downloadingJob.releaseTitle)).toBeNull()
  })

  it.each<[string, CurrentUser, string]>([
    ['the owner\'s four columns', owner, '4'],
    ['a member\'s three', member, '3'],
  ])('opens the details in a row of their own under the job\'s, one cell across %s, and points the chevron at them', async (_, signedIn, columns) => {
    // Arrange
    listing(downloadingJob, downloadedJob)
    const { user } = renderPage(signedIn)
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    const chevron = within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Show details' })

    // Act
    await user.click(chevron)

    // Assert
    const details = detailsOf(downloadingJob.releaseTitle)!
    expect(details).toHaveClass('jobs-details-row')
    expect(details.querySelectorAll('td')).toHaveLength(1)
    expect(details.querySelector('td')).toHaveAttribute('colspan', columns)
    expect(details.querySelector('td')).not.toHaveAttribute('data-label')
    expect(chevron).toHaveAttribute('aria-expanded', 'true')
    expect(chevron).toHaveAttribute('aria-controls', details.id)
    expect(chevron).toHaveAccessibleName('Hide details')
    expect(chevron).toHaveAttribute('title', 'Hide details')
    expect(rowOf(downloadingJob.releaseTitle)).toHaveClass('jobs-row-open')
    // The other row stays closed.
    expect(document.querySelectorAll('tr.jobs-details-row')).toHaveLength(1)
  })

  it('closes the details again from Hide details', async () => {
    // Arrange
    listing(downloadingJob, downloadedJob)
    const { user } = renderPage()
    await openDetails(user, downloadingJob.releaseTitle)
    const chevron = within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Hide details' })

    // Act
    await user.click(chevron)

    // Assert
    expect(chevron).toHaveAttribute('aria-expanded', 'false')
    expect(chevron).not.toHaveAttribute('aria-controls')
    expect(chevron).toHaveAccessibleName('Show details')
    expect(rowOf(downloadingJob.releaseTitle)).not.toHaveClass('jobs-row-open')
    expect(detailsOf(downloadingJob.releaseTitle)).toBe(rowOf(downloadedJob.releaseTitle))
  })

  // ── what they say ──

  it('says where the release came from, the client\'s id for it cut short, and leads to the job\'s page', async () => {
    // Arrange
    listing(downloadingJob)
    const { user } = renderPage()
    const details = await openDetails(user, downloadingJob.releaseTitle)

    // Act
    await user.click(within(details).getByRole('link', { name: 'Open the job page' }))

    // Assert
    expect(within(details).getByText(/^From Toloka · Local qBittorrent id 8a2f1c0e… ·/)).toHaveClass('ui-hint')
    expect(await screen.findByText('Job details')).toBeInTheDocument()
  })

  it('says the client doesn\'t list a job it has no id for', async () => {
    // Arrange
    const job = jobOf({ status: 'Created', clientJobId: null })
    listing(job)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, job.releaseTitle)

    // Assert
    expect(within(details).getByText(/^From Toloka · Local qBittorrent doesn't list it ·/)).toBeInTheDocument()
    expect(within(details).getByRole('link', { name: 'Open the job page' })).toHaveAttribute('href', `/jobs/${job.id}`)
  })

  it('holds only the facts line for a job with no sentence to say', async () => {
    // Arrange
    listing(downloadingJob)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, downloadingJob.releaseTitle)

    // Assert
    expect(details.querySelector('.jobs-status')).toBeNull()
    expect(details.querySelector('.jobs-row-details')?.children).toHaveLength(1)
  })

  it('shows a block reason in full, under the status the row\'s cell already says, which cuts it at two lines', async () => {
    // Arrange
    listing(blockedJob)
    const { user } = renderPage()
    await screen.findByText(blockedJob.releaseTitle, asTitle)
    const cut = within(rowOf(blockedJob.releaseTitle)).getByText(BLOCK_REASON).closest('p')

    // Act
    const details = await openDetails(user, blockedJob.releaseTitle)

    // Assert
    expect(cut).toHaveClass('library-source-warning', 'jobs-clamp')
    expect(cut).toHaveAttribute('title', BLOCK_REASON)
    const whole = within(details).getByText(BLOCK_REASON).closest('p')
    expect(whole).toHaveClass('library-source-warning')
    expect(whole).not.toHaveClass('jobs-clamp')
    expect(within(details).queryByText('Downloading')).not.toBeInTheDocument()
  })

  it('shows a failed job\'s sentence in full as a red line', async () => {
    // Arrange
    const failedJob = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000004',
      releaseTitle: 'Dune (2021) 2160p',
      status: 'Failed',
      clientJobId: null,
      error: '“Local qBittorrent” already has this torrent, so nothing was sent.',
    })
    listing(failedJob)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, failedJob.releaseTitle)

    // Assert
    const line = within(details).getByText(failedJob.error!).closest('p')
    expect(line).toHaveAttribute('data-status', 'Failed')
    expect(line).not.toHaveClass('jobs-clamp')
    expect(within(details).queryByText('Failed')).not.toBeInTheDocument()
  })

  it.each<[Exclude<DownloadOverwriteCheck, 'Checked'>, string]>([
    ['NotCheckedMagnet', 'Not checked for files it would write over: a magnet link names its files only once the client has fetched them.'],
    ['NotCheckedFileList', 'Not checked for files it would write over: the app couldn\'t read the .torrent\'s list of files.'],
    ['NotCheckedFolderUnknown', 'Not checked for files it would write over: the destination\'s folder had no path in the app.'],
    ['NotCheckedFolderOutsideMediaFolders', 'Not checked for files it would write over: the destination\'s folder wasn\'t under Media folders, or a link in it led out of them.'],
    ['NotCheckedClientJobs', 'Not checked for files it would write over: the download client couldn\'t list the torrents it already had there, or their files.'],
    ['NotCheckedFileName', 'Not checked for files it would write over: a file\'s name held characters the app couldn\'t look up.'],
  ])('says in the details, as a warning line, why a job was sent without the check for files it would write over: %s', async (overwriteCheck, sentence) => {
    // Arrange
    const uncheckedJob = jobOf({ id: 'd0b10000-0000-4000-8000-000000000005', releaseTitle: 'Heat (1995) WEBRip', overwriteCheck })
    listing(uncheckedJob)
    const { user } = renderPage()
    await screen.findByText(uncheckedJob.releaseTitle, asTitle)
    const onTheRow = within(rowOf(uncheckedJob.releaseTitle)).queryByText(sentence)

    // Act
    const details = await openDetails(user, uncheckedJob.releaseTitle)

    // Assert
    expect(onTheRow).not.toBeInTheDocument()
    expect(within(details).getByText(sentence).closest('p')).toHaveClass('library-source-warning')
  })

  it.each<[string, Partial<DownloadJob>]>([
    ['a job sent before the check existed', { overwriteCheck: null }],
    ['a failed job, though its submission failed after the check', { status: 'Failed', clientJobId: null, error: 'qBittorrent answered 500 Internal Server Error. Its own log says why.', overwriteCheck: 'NotCheckedMagnet' }],
  ])('says nothing of the check for %s', async (_, changes) => {
    // Arrange
    const job = jobOf({ id: 'd0b10000-0000-4000-8000-000000000006', releaseTitle: 'Heat (1995) WEBRip', ...changes })
    listing(job)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, job.releaseTitle)

    // Assert
    expect(within(details).queryByText(/Not checked for files/)).not.toBeInTheDocument()
    expect(within(details).queryByText(/^overwriteCheck\./)).not.toBeInTheDocument()
  })

  // MAM-24…27 — the replacement's whole sentence, which the row's Replacement cell says in a few words.
  it.each<[string, Partial<DownloadJobReplacement>, DownloadJobStatus, string, string]>([
    ['scheduled', { state: 'Scheduled', copyCount: 1 }, 'Created', 'Replacement scheduled: 1 copy, kept until the new one is verified.', 'library-source-status'],
    ['downloading, of several copies', { state: 'Downloading', copyCount: 18 }, 'Downloading', 'Replacement scheduled: 18 copies, kept until the new one is verified.', 'library-source-status'],
    ['scheduled while replacement is switched off', { state: 'Submitted', copyCount: 2, switchedOff: true }, 'Submitted', 'Replacement scheduled, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['paused, with copies no longer in a ticked folder', { state: 'Paused', copyCount: 3, copiesOutsideTickedFolders: 2 }, 'Paused', 'Replacement scheduled: 3 copies, kept until the new one is verified. 2 copies are no longer in a ticked folder: unless they are ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['downloaded, until the check takes it up', { state: 'Downloaded', copyCount: 1 }, 'Downloaded', 'Replacement checks the new file: 1 copy, kept until it is verified.', 'library-source-status'],
    ['inspecting the new files, of several copies', { state: 'Inspecting', copyCount: 18 }, 'Downloaded', 'Replacement checks the new file: 18 copies, kept until it is verified.', 'library-source-status'],
    ['inspecting while replacement is switched off', { state: 'Inspecting', copyCount: 2, switchedOff: true }, 'Downloaded', 'Replacement checks the new file, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['reading the new files on the disk, with no time said', { state: 'Inspecting', copyCount: 2, checkStage: 'Files', checkWaitsUntilUtc: '2026-10-11T13:10:00Z' }, 'Downloaded', 'Replacement checks the new file: 2 copies, kept until it is verified.', 'library-source-status'],
    ['waiting for Jellyfin to identify the new file, until when', { state: 'Inspecting', copyCount: 1, checkStage: 'Library', checkWaitsUntilUtc: WAITS_UNTIL_UTC }, 'Downloaded', `Replacement waits for Jellyfin to identify the new file until ${waitsUntil}: 1 copy, kept until it is verified.`, 'library-source-status'],
    ['waiting for Jellyfin, of several copies', { state: 'Inspecting', copyCount: 18, checkStage: 'Library', checkWaitsUntilUtc: WAITS_UNTIL_UTC }, 'Downloaded', `Replacement waits for Jellyfin to identify the new files until ${waitsUntil}: 18 copies, kept until they are verified.`, 'library-source-status'],
    ['waiting for Jellyfin while replacement is switched off, which is what it says', { state: 'Inspecting', copyCount: 2, switchedOff: true, checkStage: 'Library', checkWaitsUntilUtc: WAITS_UNTIL_UTC }, 'Downloaded', 'Replacement checks the new file, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['checked again after a copy was dropped', { state: 'Downloaded', copyCount: 1, droppedCopyCount: 1 }, 'Downloaded', 'Replacement checks the new file: 1 copy, kept until it is verified. 1 copy dropped.', 'library-source-status'],
    ['waiting for Jellyfin, with copies dropped and one no longer in a ticked folder', { state: 'Inspecting', copyCount: 2, droppedCopyCount: 2, copiesOutsideTickedFolders: 1, checkStage: 'Library', checkWaitsUntilUtc: WAITS_UNTIL_UTC }, 'Downloaded', `Replacement waits for Jellyfin to identify the new files until ${waitsUntil}: 2 copies, kept until they are verified. 2 copies dropped. 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.`, 'library-source-status'],
    ['downloaded, with a copy no longer in a ticked folder', { state: 'Downloaded', copyCount: 2, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Replacement checks the new file: 2 copies, kept until it is verified. 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified', { state: 'Verified', copyCount: 1 }, 'Downloaded', 'New file verified: 1 old copy moves into the quarantine next.', 'library-source-status'],
    ['verified, of several copies, some no longer in a ticked folder', { state: 'Verified', copyCount: 5, copiesOutsideTickedFolders: 2 }, 'Downloaded', 'New file verified, but 2 copies are no longer in a ticked folder: unless they are ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified, with one copy no longer in a ticked folder', { state: 'Verified', copyCount: 1, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'New file verified, but 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified while replacement is switched off', { state: 'Verified', copyCount: 3, switchedOff: true }, 'Downloaded', 'New file verified, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['verified while replacement is switched off, with a copy no longer in a ticked folder', { state: 'Verified', copyCount: 3, switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'New file verified, but replacement is switched off: nothing will move while it is off. 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified while replacement is on, with nothing in the quarantine yet', { state: 'Verified', copyCount: 2, quarantinedCopyCount: 0 }, 'Downloaded', 'New file verified: 2 old copies move into the quarantine next.', 'library-source-status'],
    ['verified, with copies dropped', { state: 'Verified', copyCount: 2, droppedCopyCount: 3 }, 'Downloaded', 'New file verified: 2 old copies move into the quarantine next. 3 copies dropped.', 'library-source-status'],
    ['moving one copy into the quarantine', { state: 'Quarantining', copyCount: 1 }, 'Downloaded', 'Moving the old copy into the quarantine…', 'library-source-status'],
    ['moving several copies, with nothing said of what won\'t move', { state: 'Quarantining', copyCount: 4, switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Moving the old copies into the quarantine…', 'library-source-status'],
    ['in the quarantine', { state: 'Quarantined', copyCount: 1, quarantinedCopyCount: 1 }, 'Downloaded', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good.', 'library-source-status'],
    ['in the quarantine, of several copies, with nothing said of the switch', { state: 'Quarantined', copyCount: 3, quarantinedCopyCount: 3, switchedOff: true }, 'Downloaded', 'Old copies moved into the quarantine: 3 copies, kept until you restore them or delete them for good.', 'library-source-status'],
    ['in the quarantine, with a file that stayed in its folder', { state: 'Quarantined', copyCount: 1, quarantinedCopyCount: 1, stayedFileCount: 1 }, 'Downloaded', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good. 1 file stayed in its folder: see the job.', 'library-source-status'],
    ['in the quarantine, with files that stayed in their folders', { state: 'Quarantined', copyCount: 2, quarantinedCopyCount: 2, stayedFileCount: 3 }, 'Downloaded', 'Old copies moved into the quarantine: 2 copies, kept until you restore them or delete them for good. 3 files stayed in their folders: see the job.', 'library-source-status'],
    ['in the quarantine, with a copy dropped and a file that stayed in its folder', { state: 'Quarantined', copyCount: 1, quarantinedCopyCount: 1, stayedFileCount: 1, droppedCopyCount: 1 }, 'Downloaded', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good. 1 copy dropped. 1 file stayed in its folder: see the job.', 'library-source-status'],
    ['being put back', { state: 'Restoring', copyCount: 1, quarantinedCopyCount: 1 }, 'Downloaded', 'Putting the old copy back…', 'library-source-status'],
    ['being put back, of several copies', { state: 'Restoring', copyCount: 2, quarantinedCopyCount: 0 }, 'Downloaded', 'Putting the old copies back…', 'library-source-status'],
    ['being deleted for good', { state: 'Deleting', copyCount: 1, quarantinedCopyCount: 1 }, 'Downloaded', 'Deleting the old copy for good…', 'library-source-status'],
    ['being deleted for good, of several copies', { state: 'Deleting', copyCount: 5, quarantinedCopyCount: 5 }, 'Downloaded', 'Deleting the old copies for good…', 'library-source-status'],
    ['undone by a restore', { state: 'Restored', copyCount: 1 }, 'Downloaded', 'Replacement undone: the old copy is back where it was.', 'library-source-status'],
    ['undone by a restore, of several copies', { state: 'Restored', copyCount: 2 }, 'Downloaded', 'Replacement undone: the old copies are back where they were.', 'library-source-status'],
    ['completed', { state: 'Completed', copyCount: 1 }, 'Downloaded', 'Replaced: the old copy was deleted for good.', 'library-source-status'],
    ['completed, of several copies', { state: 'Completed', copyCount: 18 }, 'Downloaded', 'Replaced: the old copies were deleted for good.', 'library-source-status'],
    ['in need of review after the move, with the server\'s sentence', { state: 'NeedsReview', stop: 'Move', reason: HELD_REASON, canRetry: true }, 'Downloaded', `Replacement needs review: ${HELD_REASON}`, 'library-source-warning'],
    ['in need of review after the move, with copies dropped', { state: 'NeedsReview', stop: 'Move', reason: HELD_REASON, canRetry: true, droppedCopyCount: 2 }, 'Downloaded', `Replacement needs review: ${HELD_REASON} 2 copies dropped.`, 'library-source-warning'],
    ['in need of review after the check, with what to do and nothing said of what won\'t move', { state: 'NeedsReview', stop: 'Check', reason: 'Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).', switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Replacement needs review: Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979). Fix it in Jellyfin or qBittorrent, then press Retry — or see the job.', 'library-source-warning'],
    ['in need of review after the check, with a copy dropped', { state: 'NeedsReview', stop: 'Check', reason: NO_ID_REASON, droppedCopyCount: 1 }, 'Downloaded', `Replacement needs review: ${NO_ID_REASON} 1 copy dropped. Fix it in Jellyfin or qBittorrent, then press Retry — or see the job.`, 'library-source-warning'],
    ['in need of review after the check, with a copy only a drop settles', { state: 'NeedsReview', stop: 'Check', reason: RENUMBERED_REASON, copyCount: 2, dropOnlyCopyCount: 1 }, 'Downloaded', `Replacement needs review: ${RENUMBERED_REASON} 1 copy can only be dropped, or both kept: see the job.`, 'library-source-warning'],
    ['in need of review after the check, with copies only a drop settles and one dropped', { state: 'NeedsReview', stop: 'Check', reason: RENUMBERED_REASON, copyCount: 3, dropOnlyCopyCount: 2, droppedCopyCount: 1 }, 'Downloaded', `Replacement needs review: ${RENUMBERED_REASON} 1 copy dropped. 2 copies can only be dropped, or both kept: see the job.`, 'library-source-warning'],
    ['in need of review after the check, its one copy one only a drop settles', { state: 'NeedsReview', stop: 'Check', reason: RENUMBERED_REASON, copyCount: 1, dropOnlyCopyCount: 1 }, 'Downloaded', `Replacement needs review: ${RENUMBERED_REASON} A new check can't pair its last copy: keep both.`, 'library-source-warning'],
    ['in need of review after the check, every copy left one only a drop settles', { state: 'NeedsReview', stop: 'Check', reason: RENUMBERED_REASON, copyCount: 2, dropOnlyCopyCount: 2, droppedCopyCount: 1 }, 'Downloaded', `Replacement needs review: ${RENUMBERED_REASON} 1 copy dropped. A new check can't pair any of its 2 copies left: keep both.`, 'library-source-warning'],
    ['in need of review, with no reason given', { state: 'NeedsReview' }, 'Downloaded', 'Replacement needs review.', 'library-source-warning'],
    ['in need of review while its download needs linking', { state: 'NeedsReview', stop: 'Download', reason: DOWNLOAD_REASON }, 'NeedsLinking', `Replacement needs review: ${DOWNLOAD_REASON} Keep both, or cancel the job.`, 'library-source-warning'],
    ['failed on a new file that isn\'t readable media, with nothing said of what won\'t move', { state: 'Failed', reason: '“Alien.1979.Directors.Cut.2160p.mkv” has no video stream.', switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Replacement stopped: “Alien.1979.Directors.Cut.2160p.mkv” has no video stream.', 'library-source-warning'],
    ['cancelled', { state: 'Cancelled', reason: 'You cancelled the download, so nothing will be replaced.' }, 'Cancelled', 'Replacement stopped: You cancelled the download, so nothing will be replaced.', 'library-source-warning'],
    ['cancelled alone by the owner\'s Keep both, its download going on', { state: 'Cancelled', reason: KEPT_BOTH_REASON }, 'Downloading', `Replacement stopped: ${KEPT_BOTH_REASON}`, 'library-source-warning'],
    ['cancelled after a copy was dropped', { state: 'Cancelled', reason: KEPT_BOTH_REASON, droppedCopyCount: 1 }, 'Downloaded', `Replacement stopped: ${KEPT_BOTH_REASON} 1 copy dropped.`, 'library-source-warning'],
  ])('says in the details where the replacement a job carries stands: %s', async (_, replacement, status, sentence, tone) => {
    // Arrange
    const replacing = replacingJob(replacement, status)
    listing(replacing, downloadingJob)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, replacing.releaseTitle)

    // Assert
    const line = within(details).getByText(sentence).closest('p')
    expect(line).toHaveClass(tone)
    expect(line).not.toHaveAttribute('role')
    expect(line?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    expect(within(rowOf(replacing.releaseTitle)).queryByText(sentence)).not.toBeInTheDocument()
  })

  it('greens a verified replacement\'s line, as a downloaded job\'s, and not one still being checked', async () => {
    // Arrange
    const verified = replacingJob({ state: 'Verified' })
    const inspecting = { ...replacingJob({ state: 'Inspecting' }), id: 'd0b10000-0000-4000-8000-000000000011', releaseTitle: 'Aliens (1986) 2160p Remux' }
    listing(verified, inspecting)
    const { user } = renderPage()

    // Act
    const verifiedDetails = await openDetails(user, verified.releaseTitle)
    const inspectingDetails = await openDetails(user, inspecting.releaseTitle)

    // Assert
    expect(within(verifiedDetails).getByText(/^New file verified:/).closest('p')).toHaveAttribute('data-status', 'Succeeded')
    expect(within(inspectingDetails).getByText(/^Replacement checks the new file:/).closest('p')).not.toHaveAttribute('data-status')
  })

  // MAM-26 — a replacement only spins while the app renames, and only its end by deletion is green.
  it.each<[DownloadJobReplacement['state'], string, boolean, boolean]>([
    ['Quarantining', 'Moving the old copy into the quarantine…', true, false],
    ['Restoring', 'Putting the old copy back…', true, false],
    ['Deleting', 'Deleting the old copy for good…', true, false],
    ['Quarantined', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good.', false, false],
    ['Restored', 'Replacement undone: the old copy is back where it was.', false, false],
    ['Completed', 'Replaced: the old copy was deleted for good.', false, true],
  ])('marks a %s replacement\'s line in the details as work under way or as a success only where it is one', async (state, sentence, spinning, green) => {
    // Arrange
    const replacing = replacingJob({ state, quarantinedCopyCount: 1 })
    listing(replacing)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, replacing.releaseTitle)

    // Assert
    const line = within(details).getByText(sentence).closest('p')!
    expect(line.querySelector('svg')?.classList.contains('animate-spin')).toBe(spinning)
    expect(line.getAttribute('data-status') === 'Succeeded').toBe(green)
  })

  it('says only that the replacement stopped on a failed job, whose red line says why', async () => {
    // Arrange
    const refused = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000009',
      releaseTitle: 'Alien (1979) 2160p Remux',
      status: 'Failed',
      clientJobId: null,
      error: 'This release turned out to be a magnet link, whose files the app can\'t check before sending, so nothing was sent and no copy will be replaced. Pick Keep both to download it.',
      replacement: replacementOf({ state: 'Failed', reason: 'The download was refused before it was sent, so nothing will be replaced.' }),
    })
    listing(refused)
    const { user } = renderPage()

    // Act
    const details = await openDetails(user, refused.releaseTitle)

    // Assert
    expect(within(details).getByText(refused.error!).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(within(details).getByText('Replacement stopped.').closest('p')).toHaveClass('library-source-warning')
    expect(within(details).queryByText(/nothing will be replaced\.$/)).not.toBeInTheDocument()
  })

  it('shows a member no replacement in the details, nor asks for one, though the job carried one', async () => {
    // Arrange
    const replacing = replacingJob({ state: 'NeedsReview', stop: 'Move', reason: HELD_REASON, canRetry: true })
    listing(replacing)
    const { user } = renderPage(member)

    // Act
    const details = await openDetails(user, replacing.releaseTitle)

    // Assert
    expect(within(details).queryByText(/Replacement/)).not.toBeInTheDocument()
    expect(within(details).getByRole('link', { name: 'Open the job page' })).toBeInTheDocument()
    expect(replacementsApi.get).not.toHaveBeenCalled()
  })

  // ── the replacement's copies, with Drop ──

  // MAM-27 — the owner's replacement's copies, the job's page's table with Drop, and its refusals in the
  // page's banner. MAM-41 — in the details, as the copies are details, not an action.
  describe('the replacement\'s copies', () => {
    const e05Path = '/media/shows/Yellowstone (2018)/Season 02/Yellowstone S02E05.mkv'
    const e06Path = '/media/shows/Yellowstone (2018)/Season 02/Yellowstone S02E06.mkv'

    /** The episode Jellyfin numbers elsewhere: only dropping it settles it. */
    const renumbered = jobCopyOf({
      id: 'e1a50000-0000-4000-8000-000000000211',
      appPath: e05Path,
      seasonNumber: 2,
      episodeNumber: 5,
      pairedNewFileId: null,
      problem: RENUMBERED_REASON,
      onlyDropSettles: true,
      status: 'Stopped',
      canDrop: true,
    })

    /** The other episode, left without a pair as the check stopped. */
    const unpaired = jobCopyOf({
      id: 'e1a50000-0000-4000-8000-000000000212',
      position: 1,
      appPath: e06Path,
      seasonNumber: 2,
      episodeNumber: 6,
      pairedNewFileId: null,
      status: 'NotPaired',
      canDrop: true,
    })

    /** The owner's downloaded season whose check stopped over the renumbered episode. */
    const stoppedJob = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000015',
      releaseTitle: 'Yellowstone S02 2160p WEB-DL',
      status: 'Downloaded',
      progress: 1,
      replacement: replacementOf({ state: 'NeedsReview', stop: 'Check', reason: RENUMBERED_REASON, copyCount: 2, dropOnlyCopyCount: 1, canCancel: true }),
    })

    /** The same replacement's details, as its copies show them. */
    const stoppedDetails = replacementDetailsOf({
      state: 'NeedsReview',
      stop: 'Check',
      reason: RENUMBERED_REASON,
      canRestore: false,
      canDeleteForGood: false,
      canCancel: true,
      quarantinedBytes: 0,
      freesBytes: 0,
      names: [],
      dropOnlyCopyCount: 1,
      copies: [renumbered, unpaired],
    })

    /** The same job once the renumbered episode was dropped: Retry is offered again. */
    const droppedJob = {
      ...stoppedJob,
      replacement: replacementOf({ state: 'NeedsReview', stop: 'Check', reason: RENUMBERED_REASON, copyCount: 1, droppedCopyCount: 1, canRetry: true, canCancel: true }),
    }

    /** Its details then: the episode greyed as dropped, the other its last copy. */
    const droppedDetails = {
      ...stoppedDetails,
      canRetry: true,
      dropOnlyCopyCount: 0,
      copies: [
        { ...renumbered, droppedUtc: '2026-10-11T12:00:00Z', status: 'Dropped' as const, canDrop: false },
        { ...unpaired, canDrop: false },
      ],
    }

    /** Opens the details of the job's row and waits for its copies' table. */
    async function openCopies(user: ReturnType<typeof userEvent.setup>, title: string) {
      const details = await openDetails(user, title)
      return within(details).findByRole('region', { name: 'Old copies and their new files' })
    }

    beforeEach(() => {
      vi.mocked(replacementsApi.get).mockResolvedValue(stoppedDetails)
    })

    it('asks for the replacement\'s copies only once the details are open, and shows them there with Drop, under the sentences', async () => {
      // Arrange
      listing(stoppedJob, downloadingJob)
      const { user } = renderPage()
      await screen.findByText(stoppedJob.releaseTitle, asTitle)
      const askedClosed = vi.mocked(replacementsApi.get).mock.calls.length

      // Act
      const copies = await openCopies(user, stoppedJob.releaseTitle)

      // Assert
      expect(askedClosed).toBe(0)
      expect(replacementsApi.get).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
      const details = detailsOf(stoppedJob.releaseTitle)!
      expect(details).toContainElement(copies)
      const sentence = within(details).getByText(/^Replacement needs review:/)
      expect(sentence.compareDocumentPosition(copies) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(within(copies).getByText(e05Path)).toBeInTheDocument()
      expect(within(copies).getByText(e06Path)).toBeInTheDocument()
      expect(within(copies).getByText(`Stopped the replacement: ${RENUMBERED_REASON}`)).toBeInTheDocument()
      expect(within(copies).getByText('Only dropping it or keeping both settles this.')).toBeInTheDocument()
      expect(within(copies).getAllByRole('button', { name: 'Drop' })).toHaveLength(2)
    })

    it('offers no Copies button among the row\'s actions: the copies are details, not an action', async () => {
      // Arrange
      listing(stoppedJob)

      // Act
      renderPage()

      // Assert
      await screen.findByText(stoppedJob.releaseTitle, asTitle)
      const actions = [...rowOf(stoppedJob.releaseTitle).querySelectorAll('td.settings-actions-cell button')].map((button) => button.textContent)
      expect(actions).toEqual(['Keep both'])
      expect(within(rowOf(stoppedJob.releaseTitle)).queryByRole('button', { name: /Copies/ })).not.toBeInTheDocument()
    })

    it('asks nothing for a job with no replacement', async () => {
      // Arrange
      listing(downloadingJob)
      const { user } = renderPage()

      // Act
      const details = await openDetails(user, downloadingJob.releaseTitle)

      // Assert
      expect(within(details).queryByRole('region', { name: 'Old copies and their new files' })).not.toBeInTheDocument()
      expect(replacementsApi.get).not.toHaveBeenCalled()
    })

    it('drops a copy from the details after its confirmation, and the row offers Retry again', async () => {
      // Arrange
      vi.mocked(replacementsApi.dropCopy).mockResolvedValue(droppedJob)
      listing(stoppedJob)
      const { user } = renderPage()
      const copies = await openCopies(user, stoppedJob.releaseTitle)
      await user.click(within(within(copies).getByText(e05Path).closest('tr')!).getByRole('button', { name: 'Drop' }))
      const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E05.mkv” from the replacement?' })
      listing(droppedJob)
      vi.mocked(replacementsApi.get).mockResolvedValue(droppedDetails)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Drop it' }))

      // Assert
      expect(replacementsApi.dropCopy).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, renumbered.id)
      expect(await within(rowOf(stoppedJob.releaseTitle)).findByRole('button', { name: 'Retry' })).toBeInTheDocument()
      const details = within(detailsOf(stoppedJob.releaseTitle)!)
      expect(await details.findByText(`Replacement needs review: ${RENUMBERED_REASON} 1 copy dropped. Fix it in Jellyfin or qBittorrent, then press Retry — or see the job.`)).toBeInTheDocument()
      expect(await within(copies).findByText('Dropped from the replacement: it stays where it is.')).toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('asks before dropping that the copy stays and Retry comes after, Drop it first and Keep it in last, not in red nor wide, and drops nothing on Keep it in', async () => {
      // Arrange
      const dropMessage = 'It stays where it is, and nothing of it moves. A new file it was paired with is simply added once Jellyfin has identified it. Press Retry afterwards to go on with the other copies.'
      listing(stoppedJob)
      const { user } = renderPage()
      const copies = await openCopies(user, stoppedJob.releaseTitle)
      await user.click(within(within(copies).getByText(e05Path).closest('tr')!).getByRole('button', { name: 'Drop' }))
      const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E05.mkv” from the replacement?' })
      const buttons = within(confirmation.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Keep it in' }))

      // Assert
      expect(confirmation).toHaveTextContent(dropMessage)
      expect(confirmation).not.toHaveClass('confirm-dialog-wide')
      expect(buttons.map((button) => button.textContent)).toEqual(['Drop it', 'Keep it in'])
      expect(buttons[0]).not.toHaveClass('ui-button-destructive')
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(replacementsApi.dropCopy).not.toHaveBeenCalled()
    })

    it('shows the spinner on the dropped copy alone while its drop is on its way, and holds the other copy\'s Drop', async () => {
      // Arrange
      vi.mocked(replacementsApi.dropCopy).mockReturnValue(new Promise(() => {}))
      listing(stoppedJob)
      const { user } = renderPage()
      const copies = await openCopies(user, stoppedJob.releaseTitle)
      const e05 = within(copies).getByText(e05Path).closest('tr')!
      const e06 = within(copies).getByText(e06Path).closest('tr')!
      await user.click(within(e05).getByRole('button', { name: 'Drop' }))
      const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E05.mkv” from the replacement?' })

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Drop it' }))

      // Assert
      const dropping = await within(e05).findByRole('button', { name: 'Dropping…' })
      expect(dropping).toBeDisabled()
      expect(dropping.querySelector('svg')).toHaveClass('animate-spin')
      expect(within(e06).getByRole('button', { name: 'Drop' })).toBeDisabled()
    })

    describe('one busy flag for the row and its details', () => {
      /** The owner's season whose move a torrent stopped: Retry, Keep both and every Drop are offered together. */
      const heldJob = {
        ...stoppedJob,
        replacement: replacementOf({ state: 'NeedsReview', stop: 'Move', reason: HELD_REASON, copyCount: 2, canRetry: true, canCancel: true }),
      }

      /** Its details: both episodes paired, and either may be dropped. */
      const heldDetails = {
        ...stoppedDetails,
        stop: 'Move' as const,
        reason: HELD_REASON,
        canRetry: true,
        dropOnlyCopyCount: 0,
        copies: [
          { ...renumbered, problem: null, onlyDropSettles: false, status: 'Paired' as const },
          { ...unpaired, status: 'Paired' as const },
        ],
      }

      beforeEach(() => {
        vi.mocked(replacementsApi.get).mockResolvedValue(heldDetails)
      })

      it('holds the row\'s Retry and Keep both while a drop from its details is on its way', async () => {
        // Arrange
        vi.mocked(replacementsApi.dropCopy).mockReturnValue(new Promise(() => {}))
        listing(heldJob)
        const { user } = renderPage()
        const copies = await openCopies(user, heldJob.releaseTitle)
        await user.click(within(within(copies).getByText(e05Path).closest('tr')!).getByRole('button', { name: 'Drop' }))
        const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E05.mkv” from the replacement?' })

        // Act
        await user.click(within(confirmation).getByRole('button', { name: 'Drop it' }))

        // Assert
        await within(copies).findByRole('button', { name: 'Dropping…' })
        const row = within(rowOf(heldJob.releaseTitle))
        expect(row.getByRole('button', { name: 'Retry' })).toBeDisabled()
        expect(row.getByRole('button', { name: 'Keep both' })).toBeDisabled()
        expect(replacementsApi.retry).not.toHaveBeenCalled()
      })

      it('holds every Drop in the details, and Keep both, while the row\'s Retry is on its way', async () => {
        // Arrange
        vi.mocked(replacementsApi.retry).mockReturnValue(new Promise(() => {}))
        listing(heldJob)
        const { user } = renderPage()
        const copies = await openCopies(user, heldJob.releaseTitle)
        const usableBefore = within(copies).getAllByRole('button', { name: 'Drop' }).every((button) => !(button as HTMLButtonElement).disabled)

        // Act
        await user.click(within(rowOf(heldJob.releaseTitle)).getByRole('button', { name: 'Retry' }))

        // Assert
        expect(usableBefore).toBe(true)
        expect(await within(rowOf(heldJob.releaseTitle)).findByRole('button', { name: 'Retrying…' })).toBeDisabled()
        expect(within(copies).getAllByRole('button', { name: 'Drop' }).every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
        expect(within(rowOf(heldJob.releaseTitle)).getByRole('button', { name: 'Keep both' })).toBeDisabled()
      })
    })

    it('shows a refused drop\'s sentence in the banner above the rows, not in the details', async () => {
      // Arrange
      const sentence = '“Yellowstone S02E05.mkv” is the last copy this replacement would replace, so it can\'t be dropped: keep both copies instead.'
      vi.mocked(replacementsApi.dropCopy).mockRejectedValue(apiError(409, sentence))
      listing(stoppedJob)
      const { user } = renderPage()
      const copies = await openCopies(user, stoppedJob.releaseTitle)
      await user.click(within(within(copies).getByText(e05Path).closest('tr')!).getByRole('button', { name: 'Drop' }))
      const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E05.mkv” from the replacement?' })

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Drop it' }))

      // Assert
      const banner = await screen.findByRole('alert')
      expect(banner).toHaveTextContent(sentence)
      expect(detailsOf(stoppedJob.releaseTitle)).not.toContainElement(banner)
    })

    it('says so in the details when the copies can\'t be loaded, and keeps the sentences', async () => {
      // Arrange
      vi.mocked(replacementsApi.get).mockRejectedValue(apiError(404))
      listing(stoppedJob)
      const { user } = renderPage()

      // Act
      const details = await openDetails(user, stoppedJob.releaseTitle)

      // Assert
      expect(await within(details).findByRole('alert')).toHaveTextContent('Could not load the old copies.')
      expect(within(details).getByText(/^Replacement needs review:/)).toBeInTheDocument()
    })
  })
})
