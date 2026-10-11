import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { formatDateTime } from '@/lib/format'
import type { CurrentUser, DownloadJob, DownloadJobReplacement, DownloadJobStatus, DownloadOverwriteCheck } from '@/lib/types'
import { replacementsApi } from '@/lib/replacements-api'
import { JobsPage } from '@/pages/JobsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, jobOf, jobsPage, sentMinutesAgo } from '@/test/downloads'
import { REPLACEMENT_ID, replacementOf } from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

/** What the last request for the list asked for. */
const lastQuery = () => vi.mocked(downloadsApi.list).mock.lastCall?.[0]

/** Matches a job's release title only. */
const asTitle = { selector: '.settings-name-text' }

/** The table row (or phone card) of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, asTitle).closest('tr')!

/** The server lists `items`, out of `total`, whatever page and status are asked for. */
function listing(items: DownloadJob[], total = items.length) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items, total))
}

function renderPage(signedIn: CurrentUser = member) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/jobs']}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs" element={<JobsPage />} />
            <Route path="jobs/:id" element={<p>Job details</p>} />
            <Route path="search" element={<p>Search page</p>} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const blockedJob = jobOf({
  id: 'd0b10000-0000-4000-8000-000000000003',
  releaseTitle: 'Arrival (2016) 1080p WEB-DL',
  status: 'Downloading',
  progress: 0.2,
  blockReason: '“Local qBittorrent” couldn\'t be asked about this download. Ask the owner to check “Local qBittorrent” under Integrations.',
})

const failedJob = jobOf({
  id: 'd0b10000-0000-4000-8000-000000000004',
  releaseTitle: 'Dune (2021) 2160p',
  status: 'Failed',
  clientJobId: null,
  error: '“Local qBittorrent” already has this torrent, so nothing was sent.',
})

describe('JobsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    listing([downloadingJob, downloadedJob])
  })

  it('lists the caller\'s jobs, newest first as the server sends them, with their status, progress, client and destination, and when they were sent', async () => {
    // Arrange
    const query = { status: undefined, page: 1, pageSize: 25 }

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(lastQuery()).toEqual(query)
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent))
      .toEqual(['Release', 'Status', 'Progress', 'Sent to', 'Sent', 'Actions'])
    expect(screen.getAllByText(/./, asTitle).map((title) => title.textContent))
      .toEqual([downloadingJob.releaseTitle, downloadedJob.releaseTitle])
    const downloading = within(rowOf(downloadingJob.releaseTitle))
    expect(downloading.getByText('Downloading')).toBeInTheDocument()
    expect(downloading.getByRole('progressbar', { name: 'Download progress' })).toHaveAttribute('value', '0.4567')
    expect(downloading.getByText('45.6%')).toBeInTheDocument()
    expect(downloading.getByText('Local qBittorrent · Movies')).toBeInTheDocument()
    expect(downloading.getByText(formatDateTime(downloadingJob.createdUtc, { dateStyle: 'medium', timeStyle: 'short' }))).toBeInTheDocument()
    const downloaded = within(rowOf(downloadedJob.releaseTitle))
    expect(downloaded.getByText('Downloaded').closest('p')).toHaveAttribute('data-status', 'Succeeded')
    expect(downloaded.getByText('100%')).toBeInTheDocument()
    expect(downloaded.getByText('Seedbox qBittorrent · Films')).toBeInTheDocument()
    expect(screen.getByText('1–2 of 2')).toBeInTheDocument()
  })

  it('links each release title to its job\'s details', async () => {
    // Arrange
    const { user } = renderPage()
    const title = await screen.findByText(downloadingJob.releaseTitle, asTitle)

    // Act
    await user.click(title)

    // Assert
    expect(title).toHaveAttribute('href', `/jobs/${downloadingJob.id}`)
    expect(await screen.findByText('Job details')).toBeInTheDocument()
  })

  it('shows a block reason as a warning line under the status, which the job keeps', async () => {
    // Arrange
    listing([blockedJob])

    // Act
    renderPage()

    // Assert
    const row = within(await screen.findByText(blockedJob.releaseTitle, asTitle).then((title) => title.closest('tr')!))
    expect(row.getByText('Downloading')).toBeInTheDocument()
    const reason = row.getByText(blockedJob.blockReason!).closest('p')
    expect(reason).toHaveClass('library-source-warning')
    expect(rowOf(blockedJob.releaseTitle).querySelector('td[data-label="Status"]')).toHaveClass('jobs-stacked-cell')
  })

  it.each<[Exclude<DownloadOverwriteCheck, 'Checked'>, string]>([
    ['NotCheckedMagnet', 'Not checked for files it would write over: a magnet link names its files only once the client has fetched them.'],
    ['NotCheckedFileList', 'Not checked for files it would write over: the app couldn\'t read the .torrent\'s list of files.'],
    ['NotCheckedFolderUnknown', 'Not checked for files it would write over: the destination\'s folder had no path in the app.'],
    ['NotCheckedFolderOutsideMediaFolders', 'Not checked for files it would write over: the destination\'s folder wasn\'t under Media folders, or a link in it led out of them.'],
    ['NotCheckedClientJobs', 'Not checked for files it would write over: the download client couldn\'t list the torrents it already had there, or their files.'],
    ['NotCheckedFileName', 'Not checked for files it would write over: a file\'s name held characters the app couldn\'t look up.'],
  ])('says, as a warning line under the status, why a job was sent without the check for files it would write over: %s', async (overwriteCheck, sentence) => {
    // Arrange
    const uncheckedJob = jobOf({ id: 'd0b10000-0000-4000-8000-000000000005', releaseTitle: 'Heat (1995) WEBRip', overwriteCheck })
    listing([uncheckedJob, downloadingJob])

    // Act
    renderPage()

    // Assert
    await screen.findByText(uncheckedJob.releaseTitle, asTitle)
    const note = within(rowOf(uncheckedJob.releaseTitle)).getByText(sentence)
    expect(note.closest('p')).toHaveClass('library-source-warning')
    expect(within(rowOf(downloadingJob.releaseTitle)).queryByText(/Not checked for files/)).not.toBeInTheDocument()
  })

  it('says nothing of the check for a job sent before it existed', async () => {
    // Arrange
    const olderJob = jobOf({ id: 'd0b10000-0000-4000-8000-000000000006', releaseTitle: 'Heat (1995) WEBRip', overwriteCheck: null })
    listing([olderJob])

    // Act
    renderPage()

    // Assert
    await screen.findByText(olderJob.releaseTitle, asTitle)
    expect(within(rowOf(olderJob.releaseTitle)).queryByText(/Not checked for files/)).not.toBeInTheDocument()
    expect(within(rowOf(olderJob.releaseTitle)).queryByText(/^overwriteCheck\./)).not.toBeInTheDocument()
  })

  it('says nothing of the check for a failed job, though its submission failed after it', async () => {
    // Arrange
    const refusedJob = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000007',
      releaseTitle: 'Heat (1995) WEBRip',
      status: 'Failed',
      clientJobId: null,
      error: 'qBittorrent answered 500 Internal Server Error. Its own log says why.',
      overwriteCheck: 'NotCheckedMagnet',
    })
    listing([refusedJob])

    // Act
    renderPage()

    // Assert
    await screen.findByText(refusedJob.releaseTitle, asTitle)
    const row = within(rowOf(refusedJob.releaseTitle))
    expect(row.getByText(refusedJob.error!).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(row.queryByText(/Not checked for files/)).not.toBeInTheDocument()
  })

  it.each<[string, Partial<DownloadJobReplacement>, DownloadJobStatus, string, string]>([
    ['scheduled', { state: 'Scheduled', copyCount: 1 }, 'Created', 'Replacement scheduled: 1 copy, kept until the new one is verified.', 'library-source-status'],
    ['downloading, of several copies', { state: 'Downloading', copyCount: 18 }, 'Downloading', 'Replacement scheduled: 18 copies, kept until the new one is verified.', 'library-source-status'],
    ['scheduled while replacement is switched off', { state: 'Submitted', copyCount: 2, switchedOff: true }, 'Submitted', 'Replacement scheduled, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['paused, with copies no longer in a ticked folder', { state: 'Paused', copyCount: 3, copiesOutsideTickedFolders: 2 }, 'Paused', 'Replacement scheduled: 3 copies, kept until the new one is verified. 2 copies are no longer in a ticked folder: unless they are ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['downloaded, until the check takes it up', { state: 'Downloaded', copyCount: 1 }, 'Downloaded', 'Replacement checks the new file: 1 copy, kept until it is verified.', 'library-source-status'],
    ['inspecting the new files, of several copies', { state: 'Inspecting', copyCount: 18 }, 'Downloaded', 'Replacement checks the new file: 18 copies, kept until it is verified.', 'library-source-status'],
    ['inspecting while replacement is switched off', { state: 'Inspecting', copyCount: 2, switchedOff: true }, 'Downloaded', 'Replacement checks the new file, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['downloaded, with a copy no longer in a ticked folder', { state: 'Downloaded', copyCount: 2, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Replacement checks the new file: 2 copies, kept until it is verified. 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified', { state: 'Verified', copyCount: 1 }, 'Downloaded', 'New file verified: 1 old copy moves into the quarantine next.', 'library-source-status'],
    // MAM-26 — the mover's check stops the whole replacement over one such copy: nothing moves next.
    ['verified, of several copies, some no longer in a ticked folder', { state: 'Verified', copyCount: 5, copiesOutsideTickedFolders: 2 }, 'Downloaded', 'New file verified, but 2 copies are no longer in a ticked folder: unless they are ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified, with one copy no longer in a ticked folder', { state: 'Verified', copyCount: 1, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'New file verified, but 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified while replacement is switched off', { state: 'Verified', copyCount: 3, switchedOff: true }, 'Downloaded', 'New file verified, but replacement is switched off: nothing will move while it is off.', 'library-source-status'],
    ['verified while replacement is switched off, with a copy no longer in a ticked folder', { state: 'Verified', copyCount: 3, switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'New file verified, but replacement is switched off: nothing will move while it is off. 1 copy is no longer in a ticked folder: unless it is ticked again, the replacement stops for review and nothing moves.', 'library-source-status'],
    ['verified while replacement is on, with nothing in the quarantine yet', { state: 'Verified', copyCount: 2, quarantinedCopyCount: 0 }, 'Downloaded', 'New file verified: 2 old copies move into the quarantine next.', 'library-source-status'],
    ['moving one copy into the quarantine', { state: 'Quarantining', copyCount: 1 }, 'Downloaded', 'Moving the old copy into the quarantine…', 'library-source-status'],
    ['moving several copies, with nothing said of what won\'t move', { state: 'Quarantining', copyCount: 4, switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Moving the old copies into the quarantine…', 'library-source-status'],
    ['in the quarantine', { state: 'Quarantined', copyCount: 1, quarantinedCopyCount: 1 }, 'Downloaded', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good.', 'library-source-status'],
    ['in the quarantine, of several copies, with nothing said of the switch', { state: 'Quarantined', copyCount: 3, quarantinedCopyCount: 3, switchedOff: true }, 'Downloaded', 'Old copies moved into the quarantine: 3 copies, kept until you restore them or delete them for good.', 'library-source-status'],
    ['in the quarantine, with a file that stayed in its folder', { state: 'Quarantined', copyCount: 1, quarantinedCopyCount: 1, stayedFileCount: 1 }, 'Downloaded', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good. 1 file stayed in its folder: see the job.', 'library-source-status'],
    ['in the quarantine, with files that stayed in their folders', { state: 'Quarantined', copyCount: 2, quarantinedCopyCount: 2, stayedFileCount: 3 }, 'Downloaded', 'Old copies moved into the quarantine: 2 copies, kept until you restore them or delete them for good. 3 files stayed in their folders: see the job.', 'library-source-status'],
    ['being put back', { state: 'Restoring', copyCount: 1, quarantinedCopyCount: 1 }, 'Downloaded', 'Putting the old copy back…', 'library-source-status'],
    ['being put back, of several copies', { state: 'Restoring', copyCount: 2, quarantinedCopyCount: 0 }, 'Downloaded', 'Putting the old copies back…', 'library-source-status'],
    ['being deleted for good', { state: 'Deleting', copyCount: 1, quarantinedCopyCount: 1 }, 'Downloaded', 'Deleting the old copy for good…', 'library-source-status'],
    ['being deleted for good, of several copies', { state: 'Deleting', copyCount: 5, quarantinedCopyCount: 5 }, 'Downloaded', 'Deleting the old copies for good…', 'library-source-status'],
    ['undone by a restore', { state: 'Restored', copyCount: 1 }, 'Downloaded', 'Replacement undone: the old copy is back where it was.', 'library-source-status'],
    ['undone by a restore, of several copies', { state: 'Restored', copyCount: 2 }, 'Downloaded', 'Replacement undone: the old copies are back where they were.', 'library-source-status'],
    ['completed', { state: 'Completed', copyCount: 1 }, 'Downloaded', 'Replaced: the old copy was deleted for good.', 'library-source-status'],
    ['completed, of several copies', { state: 'Completed', copyCount: 18 }, 'Downloaded', 'Replaced: the old copies were deleted for good.', 'library-source-status'],
    ['in need of review after the move, with the server\'s sentence', { state: 'NeedsReview', reason: 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.', canRetry: true }, 'Downloaded', 'Replacement needs review: Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.', 'library-source-warning'],
    ['in need of review after the check, with nothing said of what won\'t move', { state: 'NeedsReview', reason: 'Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).', switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Replacement needs review: Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).', 'library-source-warning'],
    ['in need of review, with no reason given', { state: 'NeedsReview' }, 'Downloaded', 'Replacement needs review.', 'library-source-warning'],
    ['in need of review while its download needs linking', { state: 'NeedsReview', reason: 'qBittorrent\'s downloads couldn\'t be told apart for this release, so the app can\'t follow it to verify the new file.' }, 'NeedsLinking', 'Replacement needs review: qBittorrent\'s downloads couldn\'t be told apart for this release, so the app can\'t follow it to verify the new file.', 'library-source-warning'],
    ['failed on a new file that isn\'t readable media, with nothing said of what won\'t move', { state: 'Failed', reason: '“Alien.1979.Directors.Cut.2160p.mkv” has no video stream.', switchedOff: true, copiesOutsideTickedFolders: 1 }, 'Downloaded', 'Replacement stopped: “Alien.1979.Directors.Cut.2160p.mkv” has no video stream.', 'library-source-warning'],
    ['cancelled', { state: 'Cancelled', reason: 'You cancelled the download, so nothing will be replaced.' }, 'Cancelled', 'Replacement stopped: You cancelled the download, so nothing will be replaced.', 'library-source-warning'],
  ])('says under the status where the replacement a job carries stands: %s', async (_, replacement, status, sentence, tone) => {
    // Arrange
    const replacing = jobOf({ id: 'd0b10000-0000-4000-8000-000000000008', releaseTitle: 'Alien (1979) 2160p Remux', status, replacement: replacementOf(replacement) })
    listing([replacing, downloadingJob])

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText(replacing.releaseTitle, asTitle)
    const line = within(rowOf(replacing.releaseTitle)).getByText(sentence).closest('p')
    expect(line).toHaveClass(tone)
    expect(line).not.toHaveAttribute('role')
    expect(line?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    expect(within(rowOf(downloadingJob.releaseTitle)).queryByText(/Replacement/)).not.toBeInTheDocument()
  })

  it('greens a verified replacement\'s line, as a downloaded job\'s, and not one still being checked', async () => {
    // Arrange
    const verified = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000010',
      releaseTitle: 'Alien (1979) 2160p Remux',
      status: 'Downloaded',
      progress: 1,
      replacement: replacementOf({ state: 'Verified' }),
    })
    const inspecting = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000011',
      releaseTitle: 'Aliens (1986) 2160p Remux',
      status: 'Downloaded',
      progress: 1,
      replacement: replacementOf({ state: 'Inspecting' }),
    })
    listing([verified, inspecting])

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText(verified.releaseTitle, asTitle)
    expect(within(rowOf(verified.releaseTitle)).getByText(/^New file verified:/).closest('p')).toHaveAttribute('data-status', 'Succeeded')
    expect(within(rowOf(inspecting.releaseTitle)).getByText(/^Replacement checks the new file:/).closest('p')).not.toHaveAttribute('data-status')
  })

  // MAM-26 — a replacement only spins while the app renames, and only its end by deletion is green.
  it.each<[DownloadJobReplacement['state'], string, boolean, boolean]>([
    ['Quarantining', 'Moving the old copy into the quarantine…', true, false],
    ['Restoring', 'Putting the old copy back…', true, false],
    ['Deleting', 'Deleting the old copy for good…', true, false],
    ['Quarantined', 'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good.', false, false],
    ['Restored', 'Replacement undone: the old copy is back where it was.', false, false],
    ['Completed', 'Replaced: the old copy was deleted for good.', false, true],
  ])('marks a %s replacement\'s line as work under way or as a success only where it is one', async (state, sentence, spinning, green) => {
    // Arrange
    const replacing = jobOf({
      id: 'd0b10000-0000-4000-8000-000000000012',
      releaseTitle: 'Alien (1979) 2160p Remux',
      status: 'Downloaded',
      progress: 1,
      replacement: replacementOf({ state, quarantinedCopyCount: 1 }),
    })
    listing([replacing])

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText(replacing.releaseTitle, asTitle)
    const line = within(rowOf(replacing.releaseTitle)).getByText(sentence).closest('p')!
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
    listing([refused])

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText(refused.releaseTitle, asTitle)
    const row = within(rowOf(refused.releaseTitle))
    expect(row.getByText(refused.error!).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(row.getByText('Replacement stopped.').closest('p')).toHaveClass('library-source-warning')
    expect(row.queryByText(/nothing will be replaced\.$/)).not.toBeInTheDocument()
  })

  it('shows a failed job\'s sentence as a red line under its status, with no Cancel', async () => {
    // Arrange
    listing([failedJob])

    // Act
    renderPage()

    // Assert
    await screen.findByText(failedJob.releaseTitle, asTitle)
    const row = within(rowOf(failedJob.releaseTitle))
    expect(row.getByText('Failed').closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(row.getByText(failedJob.error!).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(row.getByText('Not known yet')).toBeInTheDocument()
    expect(row.queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
  })

  it('names the category sent when the job\'s destination has no name', async () => {
    // Arrange
    listing([jobOf({ destinationName: null, clientDestinationKey: 'Movies/4K' })])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    expect(within(rowOf(downloadingJob.releaseTitle)).getByText('Local qBittorrent · Movies/4K')).toBeInTheDocument()
  })

  it.each<[DownloadJobStatus, boolean]>([
    ['Created', true],
    ['Submitted', true],
    ['Downloading', true],
    ['Paused', true],
    ['NeedsLinking', true],
    ['Downloaded', false],
    ['Failed', false],
    ['Cancelled', false],
  ])('offers Cancel on a %s job sent 6 minutes ago: %s', async (status, offered) => {
    // Arrange
    listing([jobOf({ status, createdUtc: sentMinutesAgo(6) })])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    const button = within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })
    expect(button !== null).toBe(offered)
  })

  it('offers no Cancel on a Created job sent under 5 minutes ago, as it may still be being sent', async () => {
    // Arrange
    listing([jobOf({ status: 'Created', clientJobId: null, createdUtc: sentMinutesAgo(1) })])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    expect(within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
  })

  it('offers every status in the filter, in the API\'s order', async () => {
    // Arrange
    renderPage()

    // Act
    const select = await screen.findByRole('combobox', { name: 'Status' })

    // Assert
    expect([...select.querySelectorAll('option')].map((option) => option.textContent)).toEqual([
      'All statuses', 'Sending', 'Submitted', 'Downloading', 'Paused', 'Downloaded', 'Failed', 'Needs linking', 'Cancelled',
    ])
  })

  it('asks the server for the picked status, from page 1 again', async () => {
    // Arrange
    listing([downloadingJob, downloadedJob], 30)
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Downloading')

    // Assert
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ status: 'Downloading', page: 1, pageSize: 25 })
  })

  it('pages through the list and keeps the page size per device', async () => {
    // Arrange
    listing([downloadingJob], 60)
    const { user } = renderPage()
    await screen.findByText('1–25 of 60')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ status: undefined, page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:jobs:pageSize')).toBe('50')
  })

  it('moves to the last page there is when the list shrank under the page on screen', async () => {
    // Arrange
    listing([downloadingJob], 30)
    const { user, client } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')
    listing([downloadingJob], 20)

    // Act
    await client.refetchQueries()

    // Assert
    expect(await screen.findByText('1–20 of 20')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ status: undefined, page: 1, pageSize: 25 })
  })

  it('says how to send a release from Search when there are no jobs yet', async () => {
    // Arrange
    listing([])

    // Act
    const { user } = renderPage()

    // Assert
    const empty = await screen.findByText(/No download jobs yet\./)
    expect(empty).toHaveTextContent('No download jobs yet. Send a release from Search, and it is followed here until it finishes.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    await user.click(within(empty).getByRole('link', { name: 'Search' }))
    expect(await screen.findByText('Search page')).toBeInTheDocument()
  })

  it('says nothing matches the status, and clears the filter on request', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockImplementation(async (query) =>
      jobsPage(query, query.status === undefined ? [downloadingJob] : [], query.status === undefined ? 1 : 0))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Paused')
    await screen.findByText('None of your download jobs has this status.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('')
    expect(lastQuery()).toEqual({ status: undefined, page: 1, pageSize: 25 })
  })

  it('shows the server sentence in place of the list when the first load is refused, with no Try again', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockRejectedValue(apiError(400, 'The page size must be between 1 and 100.'))

    // Act
    renderPage()

    // Assert
    const state = await screen.findByRole('alert')
    expect(state).toHaveTextContent('Could not load your download jobs.')
    expect(state).toHaveTextContent('The page size must be between 1 and 100.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('says what failed when the first load fails and the API gives no sentence, and Try again loads the list', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockRejectedValueOnce(apiError(500))
    const { user } = renderPage()
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load your download jobs.')
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the same rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    vi.mocked(downloadsApi.list).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refresh failed, showing the last loaded jobs.')
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it('asks again from Retry on the refresh banner, and drops the banner once the list is back', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    vi.mocked(downloadsApi.list).mockRejectedValueOnce(networkError())
    await client.refetchQueries()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('cancels a job after a confirmation that says the torrent stays in the client, for %s', async (_, signedIn) => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockResolvedValue({ ...downloadingJob, status: 'Cancelled' })
    const { user } = renderPage(signedIn)
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    listing([{ ...downloadingJob, status: 'Cancelled' }, downloadedJob])

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(confirmation).toHaveTextContent(
      'The torrent and its files stay in Local qBittorrent: nothing is paused, stopped or removed there.')
    expect(downloadsApi.cancel).toHaveBeenCalledExactlyOnceWith(downloadingJob.id)
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByText('Cancelled')).toBeInTheDocument()
    expect(within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('puts Cancel job first and Keep following last in the confirmation, and cancels nothing on Keep following', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })

    // Act
    const buttons = within(confirmation.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')
    await user.click(within(confirmation).getByRole('button', { name: 'Keep following' }))

    // Assert
    expect(buttons.map((button) => button.textContent)).toEqual(['Cancel job', 'Keep following'])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(downloadsApi.cancel).not.toHaveBeenCalled()
  })

  it('shows the server sentence above the rows when a cancel is refused, and the job as it is now', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(
      apiError(409, 'This download is already downloaded, so there\'s nothing to stop following.'))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    listing([{ ...downloadingJob, status: 'Downloaded', progress: 1 }, downloadedJob])

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(await screen.findByRole('alert'))
      .toHaveTextContent('This download is already downloaded, so there\'s nothing to stop following.')
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByText('Downloaded')).toBeInTheDocument()
    expect(screen.getByText(downloadedJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it('takes a refused cancel\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it('shows its own sentence when a cancel fails and the API gives none', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not cancel this job. Reload the page and try again.')
  })

  // MAM-26 — the server's sentence on the row ends "…then press Retry.", so the owner's Retry sits on the row.
  describe('Retry of a replacement the mover stopped', () => {
    const heldReason = 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.'

    /** The owner's downloaded job whose replacement the mover stopped for review; `changes` sets the replacement. */
    const stoppedJob = (changes: Partial<DownloadJobReplacement> = {}) => jobOf({
      id: 'd0b10000-0000-4000-8000-000000000013',
      releaseTitle: 'Alien (1979) 2160p Remux',
      status: 'Downloaded',
      progress: 1,
      replacement: replacementOf({ state: 'NeedsReview', reason: heldReason, canRetry: true, ...changes }),
    })

    it.each<[string, Partial<DownloadJobReplacement>, CurrentUser, boolean]>([
      ['offers Retry to the owner on a replacement the mover stopped', {}, owner, true],
      ['offers no Retry to anyone else', {}, member, false],
      ['offers no Retry on a replacement stopped by the check of its new files', { canRetry: false }, owner, false],
      ['offers no Retry on a replacement in the quarantine', { state: 'Quarantined', reason: null, canRetry: false, quarantinedCopyCount: 1 }, owner, false],
    ])('%s, on its row', async (_, changes, signedIn, offered) => {
      // Arrange
      const job = stoppedJob(changes)
      listing([job])

      // Act
      renderPage(signedIn)

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Retry' }) !== null).toBe(offered)
    })

    it('sends the replacement back to be moved without a confirmation, and the row shows it verified', async () => {
      // Arrange
      const job = stoppedJob()
      const retried = { ...job, replacement: replacementOf({ state: 'Verified' }) }
      vi.mocked(replacementsApi.retry).mockResolvedValue(retried)
      listing([job])
      const { user } = renderPage(owner)
      await screen.findByText(job.releaseTitle, asTitle)
      listing([retried])

      // Act
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' }))

      // Assert
      expect(replacementsApi.retry).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(await within(rowOf(job.releaseTitle)).findByText('New file verified: 1 old copy moves into the quarantine next.')).toBeInTheDocument()
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('shows the server sentence above the rows when a retry is refused', async () => {
      // Arrange
      const sentence = 'This replacement isn\'t waiting for review after a move, so there\'s nothing to retry here.'
      vi.mocked(replacementsApi.retry).mockRejectedValue(apiError(409, sentence))
      const job = stoppedJob()
      listing([job])
      const { user } = renderPage(owner)
      await screen.findByText(job.releaseTitle, asTitle)

      // Act
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' }))

      // Assert
      expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    })

    it('says Retrying… with a spinner while its retry is on its way, and names the button in its title for a phone', async () => {
      // Arrange
      vi.mocked(replacementsApi.retry).mockReturnValue(new Promise(() => {}))
      const job = stoppedJob()
      listing([job])
      const { user } = renderPage(owner)
      await screen.findByText(job.releaseTitle, asTitle)
      const button = within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' })

      // Act
      await user.click(button)

      // Assert
      expect(button).toHaveTextContent('Retrying…')
      expect(button).toBeDisabled()
      expect(button.querySelector('svg')).toHaveClass('animate-spin')
      expect(button).toHaveAttribute('title', 'Check the old copies again, and move them once nothing stands in the way')
      expect(button.querySelector('.label-text')).toHaveTextContent('Retrying…')
      expect(button.closest('.settings-actions')).not.toBeNull()
    })
  })

  it('names the Cancel button in its title, for the icon alone on a phone', async () => {
    // Arrange
    renderPage()

    // Act
    const button = await screen.findByRole('button', { name: 'Cancel job' })

    // Assert
    expect(button).toHaveAttribute('title', 'Cancel job')
    expect(button.querySelector('.label-text')).toHaveTextContent('Cancel job')
    expect(button.closest('.settings-actions')).not.toBeNull()
  })
})
