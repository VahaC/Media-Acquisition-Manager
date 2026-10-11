import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { formatDateTime } from '@/lib/format'
import { qk } from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import type {
  CurrentUser,
  DownloadJob,
  DownloadJobReplacement,
  ReplacementDetails,
  ReplacementJobState,
  ReplacementNameState,
  ReplacementNewFileProgress,
} from '@/lib/types'
import { JobDetailsPage } from '@/pages/JobDetailsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, fileOf, filesPage } from '@/test/downloads'
import {
  ALIEN_NEW_FILE_ID,
  alienHeldSubtitle,
  alienSubtitle,
  alienVideo,
  jobCopyOf,
  nameOf,
  newFileOf,
  REPLACEMENT_ID,
  replacementDetailsOf,
  replacementOf,
  workOf,
} from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-26 — the old copies of the replacement a job carries, on the job's page: every name the move
// planned and where it is now, and the owner's Retry, Restore and Delete for good (ADR 0030).
// MAM-27 — the section is the whole Replacement: what to do and how far the check got, the ticked
// copies with their pairs and Drop, the new files' evidence, Keep both, and Retry after a check.

/** The sentences the section says, spelled out rather than read from the locale. */
const RESTORE_MESSAGE = 'Every file of this replacement in the quarantine goes back where it was, never over a file there now, and no folder is created for it. The new copy stays as it is. Jellyfin lists the old copy again and identifies it afresh, so a TMDb link you set by hand may need Identify again.'
const SWITCHED_OFF = 'Replacement is switched off on the Media folders page: nothing is put back or deleted while it is off.'
const CHANGED = 'What this replacement holds in the quarantine changed since you opened it. Open Delete for good again.'
const HELD = 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.'
const NONE_YET = 'No old copy has moved yet: each moves into the quarantine once the new file is verified.'
const OTHER_WORK = 'Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).'
const NO_ID = 'Jellyfin gave “Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv” no TMDb or IMDb id within the hour, so no new file is Alien (1979).'
const RENUMBERED = 'Jellyfin numbers “Yellowstone S02E05.mkv” as season 2, episode 6 now, not episode 5 as you ticked it.'
const NEXT_DOWNLOAD = 'The app can\'t follow this download, so its new file can\'t be checked. Keep both cancels only the replacement; Cancel job stops following the download. Both copies stay either way.'
const NEXT_CHECK = 'Fix what the reason says — for a film Jellyfin gave no id, set its TMDb link on the Library page, and Jellyfin\'s Identify applies it —, then press Retry: the check starts again, and Jellyfin\'s ids decide. Or drop a copy below: it stays where it is. Or keep both.'
const NEXT_CHECK_NO_DROP = 'Fix what the reason says — for a film Jellyfin gave no id, set its TMDb link on the Library page, and Jellyfin\'s Identify applies it —, then press Retry: the check starts again, and Jellyfin\'s ids decide. Or keep both.'
const NEXT_DROP_ONLY = '1 copy can only be dropped, or both copies kept: a new check can\'t pair it. Drop it below, then press Retry for the others.'
const NEXT_DROP_ONLY_LAST = 'Only keeping both settles this: a new check can\'t pair the replacement\'s last copy, and the last copy can\'t be dropped.'
const NEXT_DROP_ONLY_EVERY = 'Only keeping both settles this: a new check can\'t pair any of the 2 copies left, and the last copy can\'t be dropped.'
const NEXT_MOVE = 'Remove what holds the copy — a torrent goes in qBittorrent, without its files —, or fix what the reason says, then press Retry. Or drop that copy below: it stays where it is, and the others move. Or keep both.'
const NEXT_MOVE_NO_DROP = 'Remove what holds the copy — a torrent goes in qBittorrent, without its files —, or fix what the reason says, then press Retry. Or keep both.'
const NEXT_MOVE_RESTORE = 'Remove what holds the copy — a torrent goes in qBittorrent, without its files —, or fix what the reason says, then press Retry. Or drop that copy below: it stays where it is, and the others move. Restore puts back what is in the quarantine.'
const NEXT_MOVE_RESTORE_NO_DROP = 'Remove what holds the copy — a torrent goes in qBittorrent, without its files —, or fix what the reason says, then press Retry. Restore puts back what is in the quarantine.'
/** Every what-to-do sentence: a page says one of them at most. */
const NEXT_SENTENCES = [
  NEXT_DOWNLOAD, NEXT_CHECK, NEXT_CHECK_NO_DROP, NEXT_DROP_ONLY, NEXT_DROP_ONLY_LAST, NEXT_DROP_ONLY_EVERY,
  NEXT_MOVE, NEXT_MOVE_NO_DROP, NEXT_MOVE_RESTORE, NEXT_MOVE_RESTORE_NO_DROP,
]
const NEXT_SWITCHED_OFF = 'Replacement is switched off on the Media folders page: nothing moves while it is off. Switch it on there, or keep both.'
const BLOCKED_BY_DROP = 'Drop the copies a new check can\'t pair first, or keep both.'
const BLOCKED_KEEP_BOTH = 'A new check can\'t pair the copies left, and the last copy can\'t be dropped: keep both instead.'
const TOO_SHORT = '“Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv” runs 1:20:00, under ¾ of “Alien.mkv”\'s 1:56:00.'
const ONLY_DROP = 'Only dropping it or keeping both settles this.'
const LAST_COPY = 'It is the replacement\'s last copy: keep both instead.'
const DROP_MESSAGE = 'It stays where it is, and nothing of it moves. A new file it was paired with is simply added once Jellyfin has identified it. Press Retry afterwards to go on with the other copies.'
const KEEP_BOTH_MESSAGE = 'The replacement is cancelled: the old copy stays where it is, and nothing of it moves. The download is left as it is, and nothing is sent to Seedbox qBittorrent. This can\'t be undone.'

/** A time of day as the section shows it. */
const timeOf = (instant: string) => formatDateTime(instant, { timeStyle: 'short' })

/** When the last check started, asked Jellyfin to look, and waits for it until. */
const CHECK_STARTED = '2026-10-10T12:00:00Z'
const LIBRARY_ASKED = '2026-10-10T12:05:00Z'
const LIBRARY_WAITS_UNTIL = '2026-10-10T13:05:00Z'

/** Yellowstone's series: the work a season pack's episode copies are of. */
const YELLOWSTONE_WORK_ID = 'e1a50000-0000-4000-8000-000000000402'

/** A ticked episode of Yellowstone's second season, not paired yet; `changes` sets what a test is about. */
const episodeCopyOf = (episode: number, changes: Parameters<typeof jobCopyOf>[0] = {}) => jobCopyOf({
  id: `e1a50000-0000-4000-8000-00000000021${episode}`,
  position: episode - 5,
  appPath: `/media/tv/Yellowstone/Season 2/Yellowstone S02E0${episode}.mkv`,
  sizeBytes: 3_221_225_472,
  workId: YELLOWSTONE_WORK_ID,
  seasonNumber: 2,
  episodeNumber: episode,
  durationMilliseconds: 3_000_000,
  pairedNewFileId: null,
  status: 'Waiting',
  ...changes,
})

/** Episode 5, which Jellyfin now numbers as episode 6: only dropping it settles it. */
const renumberedCopy = episodeCopyOf(5, { problem: RENUMBERED, onlyDropSettles: true, status: 'Stopped', canDrop: true })

/** Episode 6, not paired since the check stopped. */
const unpairedCopy = episodeCopyOf(6, { status: 'NotPaired', canDrop: true })

/** Episodes 6 and 7 of a season pack the check left unpaired: either may be dropped, as the other stays. */
const droppableCopies = () => [unpairedCopy, episodeCopyOf(7, { status: 'NotPaired', canDrop: true })]

/** Alien (1979)'s one copy, which a remux too short to replace it stopped: only a drop settles it, and it is the last. */
const tooShortCopy = jobCopyOf({ pairedNewFileId: null, problem: TOO_SHORT, onlyDropSettles: true, status: 'Stopped' })

/**
 * The film Alien (1979), its check stopped for review: Jellyfin gave its remux no id within the hour.
 * Retry and Keep both are offered; `changes` sets what a test is about.
 */
function checkStopOf(changes: Partial<ReplacementDetails> = {}): ReplacementDetails {
  return replacementDetailsOf({
    state: 'NeedsReview',
    reason: NO_ID,
    stop: 'Check',
    canRetry: true,
    canCancel: true,
    canRestore: false,
    canDeleteForGood: false,
    quarantinedBytes: 0,
    freesBytes: 0,
    names: [],
    checkStartedUtc: CHECK_STARTED,
    libraryAskedUtc: LIBRARY_ASKED,
    copies: [jobCopyOf({ pairedNewFileId: null, problem: NO_ID, status: 'Stopped' })],
    newFiles: [newFileOf({ workId: null, identifiers: [], progress: 'NoId' })],
    works: [workOf()],
    ...changes,
  })
}

/** The job's line for `details`, as the server words the same replacement. */
function lineOf(details: ReplacementDetails, changes: Partial<DownloadJobReplacement> = {}): DownloadJobReplacement {
  return replacementOf({
    state: details.state,
    reason: details.reason,
    switchedOff: details.switchedOff,
    copyCount: details.copies.filter((copy) => copy.droppedUtc === null).length,
    droppedCopyCount: details.copies.filter((copy) => copy.droppedUtc !== null).length,
    dropOnlyCopyCount: details.dropOnlyCopyCount,
    canRetry: details.canRetry,
    canCancel: details.canCancel,
    stop: details.stop,
    checkStage: details.checkStage,
    checkWaitsUntilUtc: details.checkWaitsUntilUtc,
    ...changes,
  })
}

/** The server answers the downloaded job carrying `details`'s replacement, and `details`. */
function serveDetails(details: ReplacementDetails) {
  serve({ ...downloadedJob, replacement: lineOf(details) }, details)
}

/** The downloaded job carrying the replacement, in `changes`'s state. */
function jobWith(changes: Partial<DownloadJobReplacement> = {}): DownloadJob {
  return { ...downloadedJob, replacement: replacementOf({ state: 'Quarantined', quarantinedCopyCount: 1, ...changes }) }
}

/** The server answers `job`, its one file, and `details` for its replacement. */
function serve(job: DownloadJob, details: ReplacementDetails = replacementDetailsOf()) {
  vi.mocked(downloadsApi.get).mockResolvedValue(job)
  vi.mocked(downloadsApi.files).mockImplementation(async (_, query) => filesPage(query, [fileOf({ progress: 1 })]))
  vi.mocked(replacementsApi.get).mockResolvedValue(details)
}

function renderPage(signedIn: CurrentUser = owner, address = `/jobs/${downloadedJob.id}`) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[address]}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs/:id" element={<JobDetailsPage />} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The Replacement section, once its names have arrived. */
async function replacementSection() {
  const section = await screen.findByRole('region', { name: 'Replacement' })
  await within(section).findByRole('table')
  return section
}

/** The section's row of the name that was at `originalPath`. */
const nameRowOf = (section: HTMLElement, originalPath: string) =>
  within(section).getByText(originalPath, { selector: '.jobs-file-path' }).closest('tr')!

/** A confirmation's buttons, in the order the footer lays them out. */
const footerButtons = (dialog: HTMLElement) =>
  within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')

describe('JobDetailsPage — the old copies of a replacement', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    serve(jobWith())
  })

  it('lists the old copies between the facts and the files: each name where it was, its place in the quarantine, its size and where it is now', async () => {
    // Arrange
    const details = replacementDetailsOf()

    // Act
    renderPage()

    // Assert
    const section = await replacementSection()
    expect(replacementsApi.get).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    const facts = screen.getByRole('region', { name: 'Details' })
    const files = screen.getByRole('region', { name: 'Files' })
    expect(facts.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(section.compareDocumentPosition(files) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(within(section).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Old copy', 'Size', 'Where it is now'])
    expect(within(section).getByRole('region', { name: 'Files the move planned' })).toContainElement(within(section).getByRole('table'))
    expect(within(section).getByText('The quarantine holds 40 GiB of this replacement, in 2 files.')).toBeInTheDocument()
    expect(details.names.map((name) => within(nameRowOf(section, name.originalPath)).getByText(name.quarantinePath, { selector: '.jobs-file-path' })))
      .toHaveLength(3)
    const video = within(nameRowOf(section, alienVideo.originalPath))
    expect(video.getByText('40 GiB')).toBeInTheDocument()
    expect(video.getByText('In the quarantine.').closest('p')).toHaveClass('library-source-status')
    expect(within(nameRowOf(section, alienSubtitle.originalPath)).getByText('50 KiB')).toBeInTheDocument()
    const held = within(nameRowOf(section, alienHeldSubtitle.originalPath))
    expect(held.getByText('Stayed in its folder.').closest('p')).toHaveClass('library-source-warning')
    expect(held.getByText(alienHeldSubtitle.problem!).closest('p')).toHaveClass('library-source-warning')
  })

  it('names every cell for a phone, and stacks the paths and where each name is', async () => {
    // Arrange
    renderPage()

    // Act
    const section = await replacementSection()

    // Assert
    const cells = [...nameRowOf(section, alienVideo.originalPath).querySelectorAll('td')]
    expect(cells.map((cell) => cell.getAttribute('data-label'))).toEqual(['Old copy', 'Size', 'Where it is now'])
    expect(cells.map((cell) => cell.classList.contains('jobs-stacked-cell'))).toEqual([true, false, true])
    expect(within(section).getByText(alienVideo.quarantinePath).closest('.ui-hint')).toHaveTextContent(`Quarantine: ${alienVideo.quarantinePath}`)
  })

  it.each<[ReplacementNameState, string, string]>([
    ['Planned', 'In its folder, not moved.', 'library-source-status'],
    ['Moving', 'Being moved into the quarantine…', 'library-source-status'],
    ['Quarantined', 'In the quarantine.', 'library-source-status'],
    ['Stayed', 'Stayed in its folder.', 'library-source-warning'],
    ['Restoring', 'Being put back…', 'library-source-status'],
    ['Restored', 'Back where it was.', 'library-source-status'],
    ['Deleting', 'Being deleted for good…', 'library-source-status'],
    ['Deleted', 'Deleted for good.', 'library-source-status'],
    ['Lost', 'Found neither where it was nor in the quarantine.', 'library-source-warning'],
  ])('says where a name is now: %s', async (state, sentence, tone) => {
    // Arrange
    serve(jobWith(), replacementDetailsOf({ names: [nameOf({ state })] }))

    // Act
    renderPage()

    // Assert
    const section = await replacementSection()
    const line = within(nameRowOf(section, alienVideo.originalPath)).getByText(sentence).closest('p')
    expect(line).toHaveClass(tone)
    expect(line?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('says a name with another name frees nothing, and shows why one wasn\'t put back', async () => {
    // Arrange
    const problem = 'The app couldn\'t rename “Alien.mkv” back: “/media/movies/Alien (1979)/Alien.mkv” already holds a file.'
    serve(jobWith(), replacementDetailsOf({ names: [nameOf({ otherNames: true, problem })] }))

    // Act
    renderPage()

    // Assert
    const row = within(nameRowOf(await replacementSection(), alienVideo.originalPath))
    expect(row.getByText('In the quarantine.')).toBeInTheDocument()
    expect(row.getByText(problem).closest('p')).toHaveClass('library-source-warning')
    expect(row.getByText('It has another name (a hardlink), so deleting it frees no space.')).toBeInTheDocument()
  })

  it('says no old copy has moved yet, with only Keep both offered, while the replacement holds no names', async () => {
    // Arrange
    serve(jobWith({ state: 'Verified', quarantinedCopyCount: 0, canCancel: true }), replacementDetailsOf({
      state: 'Verified', canCancel: true, canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await screen.findByRole('region', { name: 'Replacement' })
    expect(await within(section).findByText(NONE_YET)).toBeInTheDocument()
    expect(within(section).queryByRole('table')).not.toBeInTheDocument()
    expect(within(section).queryByRole('region', { name: 'Files the move planned' })).not.toBeInTheDocument()
    expect(within(section).getAllByRole('button').map((button) => button.textContent)).toEqual(['Keep both'])
  })

  it('says no old copy moves while replacement is switched off, when the replacement holds no names yet, and that keeping both ends it', async () => {
    // Arrange
    serve(jobWith({ state: 'Verified', quarantinedCopyCount: 0, switchedOff: true, canCancel: true }), replacementDetailsOf({
      state: 'Verified', switchedOff: true, canCancel: true, canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await screen.findByRole('region', { name: 'Replacement' })
    expect(await within(section).findByText('No old copy has moved yet, and none moves while replacement is switched off.')).toBeInTheDocument()
    expect(within(section).queryByText(NONE_YET)).not.toBeInTheDocument()
    expect(within(section).getByText(NEXT_SWITCHED_OFF).closest('p')).toHaveClass('library-source-warning')
    expect(within(section).getByRole('button', { name: 'Keep both' })).toBeEnabled()
  })

  it.each<[string, Partial<ReplacementDetails> & { state: ReplacementJobState }, string[]]>([
    ['cancelled with its download', { state: 'Cancelled', reason: 'You cancelled the download, so nothing will be replaced.' }, []],
    ['failed on its new file', { state: 'Failed', reason: '“Alien.1979.Directors.Cut.2160p.mkv” has no video stream.' }, []],
    [
      'stopped for review by the check of its new files, with Retry and Keep both',
      { state: 'NeedsReview', reason: OTHER_WORK, stop: 'Check', canRetry: true, canCancel: true },
      ['Retry', 'Keep both'],
    ],
    [
      'stopped for review by the move\'s checks, with Retry and Keep both',
      { state: 'NeedsReview', reason: HELD, stop: 'Move', canRetry: true, canCancel: true },
      ['Retry', 'Keep both'],
    ],
  ])('says nothing was moved, never that a move comes, for a replacement %s before anything moved', async (_, changes, buttons) => {
    // Arrange
    const { state, reason = null, stop = null, canRetry = false, canCancel = false } = changes
    serve(jobWith({ state, reason, stop, canRetry, canCancel, quarantinedCopyCount: 0 }), replacementDetailsOf({
      canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [], ...changes,
    }))

    // Act
    renderPage()

    // Assert
    const section = await screen.findByRole('region', { name: 'Replacement' })
    expect(await within(section).findByText('Nothing of this replacement was moved.')).toBeInTheDocument()
    expect(within(section).queryByText(NONE_YET)).not.toBeInTheDocument()
    expect(within(section).queryAllByRole('button').map((button) => button.textContent)).toEqual(buttons)
  })

  it('says nothing of it is in the quarantine once every name is back', async () => {
    // Arrange
    serve(jobWith({ state: 'Restored', quarantinedCopyCount: 0 }), replacementDetailsOf({
      state: 'Restored',
      canRestore: false,
      canDeleteForGood: false,
      quarantinedBytes: 0,
      freesBytes: 0,
      names: [nameOf({ state: 'Restored' }), { ...alienSubtitle, state: 'Restored' }],
    }))

    // Act
    renderPage()

    // Assert
    const section = await replacementSection()
    expect(within(section).getByText('Nothing of this replacement is in the quarantine.')).toBeInTheDocument()
    expect(within(section).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows a member that the replacement is the owner\'s, with no action and nothing asked', async () => {
    // Arrange
    const signedIn = member
    serve(jobWith({ state: 'NeedsReview', reason: NO_ID, stop: 'Check', canRetry: true, canCancel: true, quarantinedCopyCount: 0 }))

    // Act
    renderPage(signedIn)

    // Assert
    const section = await screen.findByRole('region', { name: 'Replacement' })
    expect(within(section).getByText('Only the installation owner sees a replacement\'s copies and checks, and retries, cancels or drops them.')).toBeInTheDocument()
    expect(within(section).queryByRole('button')).not.toBeInTheDocument()
    expect(within(section).queryByRole('table')).not.toBeInTheDocument()
    expect(replacementsApi.get).not.toHaveBeenCalled()
  })

  it('shows no Replacement section for a job without a replacement', async () => {
    // Arrange
    serve(downloadedJob)

    // Act
    renderPage()

    // Assert
    await screen.findByRole('region', { name: 'Files' })
    expect(screen.queryByRole('region', { name: 'Replacement' })).not.toBeInTheDocument()
    expect(replacementsApi.get).not.toHaveBeenCalled()
  })

  it('says the old copies could not be loaded, with the server sentence, while the facts stay, and Try again shows them', async () => {
    // Arrange
    vi.mocked(replacementsApi.get).mockRejectedValueOnce(networkError())
    const { user } = renderPage()
    const section = await screen.findByRole('region', { name: 'Replacement' })
    const state = await within(section).findByRole('alert')

    // Act
    await user.click(within(section).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the old copies.')
    expect(state).toHaveTextContent('The server could not be reached.')
    expect(await within(section).findByRole('table')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Details' })).toBeInTheDocument()
  })

  it('offers Restore and Delete for good for a quarantined replacement, the deletion in red outline, and no Retry', async () => {
    // Arrange
    renderPage()

    // Act
    const section = await replacementSection()

    // Assert
    const actions = section.querySelector<HTMLElement>('.settings-actions')!
    expect(within(actions).getAllByRole('button').map((button) => button.textContent)).toEqual(['Restore', 'Delete for good'])
    const remove = within(actions).getByRole('button', { name: 'Delete for good' })
    expect(remove).toHaveClass('ui-button-outline', 'ui-button-danger')
    expect(remove).toHaveAttribute('title', 'Delete the old copies in the quarantine for good')
    expect(remove.querySelector('.label-text')).toHaveTextContent('Delete for good')
    expect(within(actions).getByRole('button', { name: 'Restore' })).toHaveAttribute('title', 'Put the old copies in the quarantine back where they were')
  })

  // ── Retry ──

  it('sends a replacement the mover stopped back to be moved, without a confirmation, and says so', async () => {
    // Arrange
    const stopped = jobWith({ state: 'NeedsReview', reason: HELD, stop: 'Move', canRetry: true, quarantinedCopyCount: 0 })
    serve(stopped, replacementDetailsOf({
      state: 'NeedsReview', reason: HELD, stop: 'Move', canRetry: true, canRestore: false, canDeleteForGood: false,
      quarantinedBytes: 0, freesBytes: 0, names: [nameOf({ state: 'Planned' })],
    }))
    const retried = jobWith({ state: 'Verified', quarantinedCopyCount: 0 })
    vi.mocked(replacementsApi.retry).mockResolvedValue(retried)
    const { user } = renderPage()
    const section = await replacementSection()
    vi.mocked(downloadsApi.get).mockResolvedValue(retried)
    const retry = within(section).getByRole('button', { name: 'Retry' })

    // Act
    await user.click(retry)

    // Assert
    expect(retry).toHaveAttribute('title', 'Check the old copies again, and move them once nothing stands in the way')
    expect(replacementsApi.retry).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await screen.findByRole('status')).toHaveTextContent('Retried: the app checks the old copies again and moves them once nothing stands in the way.')
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  // ── Restore ──

  it('puts the old copies back after a plain confirmation that says the new copy stays and Jellyfin identifies the old one afresh', async () => {
    // Arrange
    const restored = jobWith({ state: 'Restored', quarantinedCopyCount: 0 })
    vi.mocked(replacementsApi.restore).mockResolvedValue(restored)
    const { user } = renderPage()
    const section = await replacementSection()
    vi.mocked(downloadsApi.get).mockResolvedValue(restored)
    await user.click(within(section).getByRole('button', { name: 'Restore' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Put the old copy back?' })
    const buttons = footerButtons(confirmation).map((button) => button.textContent)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Restore' }))

    // Assert
    expect(confirmation).toHaveTextContent(RESTORE_MESSAGE)
    expect(buttons).toEqual(['Restore', 'Cancel'])
    expect(replacementsApi.restore).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    const result = await screen.findByRole('status')
    expect(result).toHaveTextContent('Restored: 2 files are back where they were.')
    expect(result).toHaveClass('ui-banner')
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
    expect(downloadsApi.get).toHaveBeenCalledTimes(2)
  })

  it('puts nothing back on Cancel', async () => {
    // Arrange
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Restore' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Put the old copy back?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.restore).not.toHaveBeenCalled()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('offers Restore, and Retry, for a replacement in review that holds earlier copies in the quarantine, but not Delete for good', async () => {
    // Arrange
    const reason = '“Alien.mkv” couldn\'t be moved into the quarantine: the folder isn\'t on the quarantine\'s mount. 1 of 3 names is in the quarantine.'
    serve(jobWith({ state: 'NeedsReview', reason, stop: 'Move', canRetry: true }), replacementDetailsOf({
      state: 'NeedsReview', reason, stop: 'Move', canRetry: true, canRestore: true, canDeleteForGood: false,
      names: [alienVideo, { ...alienSubtitle, state: 'Planned' }],
    }))
    vi.mocked(replacementsApi.restore).mockResolvedValue(jobWith({ state: 'Restored', quarantinedCopyCount: 0 }))
    const { user } = renderPage()
    const section = await replacementSection()
    const actions = within(section).getAllByRole('button').map((button) => button.textContent)

    // Act
    await user.click(within(section).getByRole('button', { name: 'Restore' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Put the old copy back?' })).getByRole('button', { name: 'Restore' }))

    // Assert
    expect(actions).toEqual(['Retry', 'Restore'])
    expect(within(section).getByText(NEXT_MOVE_RESTORE_NO_DROP).closest('p')).toHaveClass('library-source-warning')
    expect(replacementsApi.restore).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    expect(await screen.findByRole('status')).toHaveTextContent('Restored: 1 file is back where it was.')
  })

  it('shows the server sentence in the page\'s error banner when a restore stops part-way', async () => {
    // Arrange
    const sentence = '“Alien.en.srt” couldn\'t be put back: “/media/movies/Alien (1979)/Alien.en.srt” already holds a file. 1 of 2 names is back where it was.'
    vi.mocked(replacementsApi.restore).mockRejectedValue(apiError(409, sentence))
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Put the old copy back?' })).getByRole('button', { name: 'Restore' }))

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    expect(section).not.toContainElement(banner)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
  })

  it('says Restoring… with a spinner while the restore is on its way, and holds Delete for good meanwhile', async () => {
    // Arrange
    vi.mocked(replacementsApi.restore).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Put the old copy back?' })).getByRole('button', { name: 'Restore' }))

    // Assert
    const restoring = await within(section).findByRole('button', { name: 'Restoring…' })
    expect(restoring).toBeDisabled()
    expect(restoring.querySelector('svg')).toHaveClass('animate-spin')
    expect(within(section).getByRole('button', { name: 'Delete for good' })).toBeDisabled()
  })

  // ── Delete for good ──

  it('asks once more before deleting for good, listing every path in the quarantine with its size and the room it frees, Delete for good first', async () => {
    // Arrange
    const completed = jobWith({ state: 'Completed', quarantinedCopyCount: 0 })
    vi.mocked(replacementsApi.deleteForGood).mockResolvedValue(completed)
    const { user } = renderPage()
    const section = await replacementSection()
    vi.mocked(downloadsApi.get).mockResolvedValue(completed)
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    const listed = within(confirmation).getAllByRole('listitem').map((item) => [
      item.querySelector('.jobs-file-path')?.textContent,
      item.querySelector('.jobs-quarantine-size')?.textContent,
    ])
    const buttons = footerButtons(confirmation)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

    // Assert
    expect(confirmation).toHaveTextContent('These 2 files are deleted from the quarantine, and can\'t be restored afterwards:')
    expect(listed).toEqual([[alienVideo.originalPath, '40 GiB'], [alienSubtitle.originalPath, '50 KiB']])
    expect(confirmation).not.toHaveTextContent(alienHeldSubtitle.originalPath)
    expect(confirmation).toHaveTextContent('Frees 40 GiB.')
    expect(confirmation).not.toHaveTextContent(/another name/)
    expect(buttons.map((button) => button.textContent)).toEqual(['Delete for good', 'Cancel'])
    expect(buttons[0]).toHaveClass('ui-button-destructive')
    expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])
    expect(await screen.findByRole('status')).toHaveTextContent('Deleted for good: 2 files.')
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
  })

  it('says how many names have another name and free nothing, with the room the server says the rest frees', async () => {
    // Arrange
    serve(jobWith(), replacementDetailsOf({ names: [{ ...alienVideo, otherNames: true }, alienSubtitle], freesBytes: alienSubtitle.sizeBytes }))
    const { user } = renderPage()
    const section = await replacementSection()

    // Act
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    expect(confirmation).toHaveTextContent('Frees 50 KiB.')
    expect(confirmation).toHaveTextContent('1 file has another name (a hardlink), so deleting it frees no space.')
  })

  it('deletes nothing on Cancel', async () => {
    // Arrange
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.deleteForGood).not.toHaveBeenCalled()
  })

  it('sends exactly the names the confirmation listed, though the replacement changed while it was open', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(409, CHANGED))
    const { client, user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    const another = nameOf({ id: 'e1a50000-0000-4000-8000-000000000104', originalPath: '/media/movies/Alien (1979)/Alien.nfo' })
    act(() => client.setQueryData(qk.replacements.job(REPLACEMENT_ID), replacementDetailsOf({ names: [alienVideo, alienSubtitle, another] })))

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

    // Assert
    expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(CHANGED)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('takes a refusal\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(409, CHANGED))
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })).getByRole('button', { name: 'Delete for good' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says its own sentence when a deletion fails and the API gives none', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })).getByRole('button', { name: 'Delete for good' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not delete the old copies. Reload the page and try again.')
  })

  it('says Deleting… with a spinner while the deletion is on its way, and holds Restore meanwhile', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const section = await replacementSection()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })).getByRole('button', { name: 'Delete for good' }))

    // Assert
    const deleting = await within(section).findByRole('button', { name: 'Deleting…' })
    expect(deleting).toBeDisabled()
    expect(deleting.querySelector('svg')).toHaveClass('animate-spin')
    expect(within(section).getByRole('button', { name: 'Restore' })).toBeDisabled()
  })

  // ── switched off ──

  it('disables Restore and Delete for good, saying why on hover and in a line of its own, while replacement is switched off; Retry stays', async () => {
    // Arrange
    const reason = '“Alien.mkv” couldn\'t be moved into the quarantine: the folder is read-only. 1 of 3 names is in the quarantine.'
    serve(jobWith({ state: 'NeedsReview', reason, canRetry: true, switchedOff: true }), replacementDetailsOf({
      state: 'NeedsReview', reason, switchedOff: true, canRetry: true, canRestore: true, canDeleteForGood: true,
    }))
    const { user } = renderPage()
    const section = await replacementSection()

    // Act
    await user.click(within(section).getByRole('button', { name: 'Restore' }))

    // Assert
    for (const name of ['Restore', 'Delete for good']) {
      const button = within(section).getByRole('button', { name })
      expect(button).toBeDisabled()
      expect(button.closest('.settings-action-reason')).toHaveAttribute('title', SWITCHED_OFF)
    }
    expect(within(section).getByRole('button', { name: 'Retry' })).toBeEnabled()
    expect(within(section).getByText(SWITCHED_OFF).closest('p')).toHaveClass('library-source-warning')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.restore).not.toHaveBeenCalled()
  })

  // ── asked again as the job moves ──

  describe('while the job moves', () => {
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    /** Lets the polling interval pass once, with the answers it brings. */
    const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

    it('asks for the old copies again when the job comes back with its replacement changed, and only then', async () => {
      // Arrange
      serve(jobWith({ state: 'Quarantining', quarantinedCopyCount: 0 }), replacementDetailsOf({
        state: 'Quarantining', canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0,
        names: [nameOf({ state: 'Moving' }), { ...alienSubtitle, state: 'Planned' }],
      }))
      renderPage()
      const section = await replacementSection()
      await within(section).findByText('Being moved into the quarantine…')

      // Act
      await nextPoll()
      const unchanged = vi.mocked(replacementsApi.get).mock.calls.length
      serve(jobWith(), replacementDetailsOf())
      await nextPoll()

      // Assert
      expect(unchanged).toBe(1)
      expect(await within(section).findByText('The quarantine holds 40 GiB of this replacement, in 2 files.')).toBeInTheDocument()
      expect(replacementsApi.get).toHaveBeenCalledTimes(2)
      expect(within(section).getByRole('button', { name: 'Delete for good' })).toBeEnabled()
    })

    // MAM-27 — what the check found changes on every pass, with nothing on the job's line changing.
    it('asks for the replacement every 2 s while the check of its new files runs, and shows what each pass found', async () => {
      // Arrange
      const asked = replacementDetailsOf({
        state: 'Inspecting', checkStage: 'Library', checkStartedUtc: CHECK_STARTED, libraryAskedUtc: LIBRARY_ASKED,
        checkWaitsUntilUtc: LIBRARY_WAITS_UNTIL, canRestore: false, canDeleteForGood: false, canCancel: true,
        quarantinedBytes: 0, freesBytes: 0, names: [],
        copies: [jobCopyOf({ pairedNewFileId: null, status: 'Waiting' })],
        newFiles: [newFileOf({ readUtc: null, workId: null, identifiers: [], kind: null, progress: 'AskedToLook' })],
        works: [workOf()],
      })
      serveDetails(asked)
      renderPage()
      const section = await screen.findByRole('region', { name: 'Replacement' })
      await within(section).findByText('Jellyfin asked to look at it.')
      vi.mocked(replacementsApi.get).mockResolvedValue({ ...asked, newFiles: [newFileOf({ workId: null, identifiers: [], progress: 'NoId' })] })

      // Act
      await nextPoll()

      // Assert
      expect(await within(section).findByText('No TMDb or IMDb id from Jellyfin yet.')).toBeInTheDocument()
      expect(replacementsApi.get).toHaveBeenCalledTimes(2)
    })

    it('asks nothing more for a replacement whose check stopped for review', async () => {
      // Arrange
      serveDetails(checkStopOf())
      renderPage()
      const section = await screen.findByRole('region', { name: 'Replacement' })
      await within(section).findByText('The check stopped.')

      // Act
      await nextPoll()
      await nextPoll()

      // Assert
      expect(replacementsApi.get).toHaveBeenCalledOnce()
    })
  })
})

// ── MAM-27: managing a replacement ──

describe('JobDetailsPage — managing a replacement', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
  })

  /** The Replacement section, once its details have arrived. */
  async function loadedSection() {
    const section = await screen.findByRole('region', { name: 'Replacement' })
    await within(section).findByRole('region', { name: 'Old copies and their new files' })
    return section
  }

  /** The copies table's row of the copy at `appPath`. */
  const copyRowOf = (section: HTMLElement, appPath: string) =>
    within(within(section).getByRole('region', { name: 'Old copies and their new files' }))
      .getByText(appPath, { selector: '.jobs-file-path' }).closest('tr')!

  /** The new files table's row of the file at `appPath`. */
  const newFileRowOf = (section: HTMLElement, appPath: string) =>
    within(within(section).getByRole('region', { name: 'New files' }))
      .getByText(appPath, { selector: '.jobs-file-path' }).closest('tr')!

  // ── what to do and how far it got ──

  it.each<[string, Partial<ReplacementDetails>, string]>([
    ['the app can\'t follow its download', { stop: 'Download', canRetry: false }, NEXT_DOWNLOAD],
    ['the check of its new files', { stop: 'Check', copies: droppableCopies(), newFiles: [] }, NEXT_CHECK],
    // MAM-27 review — a film's one copy is never dropped: the advice names no Drop the page refuses.
    ['the check of a film\'s new file, its one copy the last', { stop: 'Check' }, NEXT_CHECK_NO_DROP],
    [
      'the check, with a copy only dropping it settles',
      { stop: 'Check', reason: RENUMBERED, canRetry: false, dropOnlyCopyCount: 1, copies: [renumberedCopy, unpairedCopy], newFiles: [] },
      NEXT_DROP_ONLY,
    ],
    [
      'the check, with a film\'s one copy only dropping it settles',
      { stop: 'Check', reason: TOO_SHORT, canRetry: false, dropOnlyCopyCount: 1, copies: [tooShortCopy] },
      NEXT_DROP_ONLY_LAST,
    ],
    [
      'the check, with every copy left one only dropping it settles',
      {
        stop: 'Check', reason: RENUMBERED, canRetry: false, dropOnlyCopyCount: 2,
        copies: [renumberedCopy, episodeCopyOf(6, { problem: RENUMBERED, onlyDropSettles: true, status: 'Stopped', canDrop: true })],
        newFiles: [],
      },
      NEXT_DROP_ONLY_EVERY,
    ],
    ['the move', { stop: 'Move', reason: HELD, copies: droppableCopies(), newFiles: [] }, NEXT_MOVE],
    ['the move of a film\'s one copy', { stop: 'Move', reason: HELD }, NEXT_MOVE_NO_DROP],
    [
      'the move, part of it in the quarantine already',
      { stop: 'Move', reason: HELD, canCancel: false, canRestore: true, copies: droppableCopies(), newFiles: [] },
      NEXT_MOVE_RESTORE,
    ],
    ['the move of a film\'s one copy, part of it in the quarantine already', { stop: 'Move', reason: HELD, canCancel: false, canRestore: true }, NEXT_MOVE_RESTORE_NO_DROP],
  ])('says what the owner can do about a replacement stopped by %s', async (_, changes, sentence) => {
    // Arrange
    serveDetails(checkStopOf(changes))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const line = within(section).getByText(sentence).closest('p')
    expect(line).toHaveClass('library-source-warning')
    expect(line?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    for (const other of NEXT_SENTENCES.filter((text) => text !== sentence)) {
      expect(within(section).queryByText(other)).not.toBeInTheDocument()
    }
  })

  it('links a film the check stopped on to the Library page, searched by its title, where its TMDb link is set', async () => {
    // Arrange
    serveDetails(checkStopOf())

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const link = within(section).getByRole('link', { name: 'Open Alien (1979) in Library' })
    expect(link).toHaveAttribute('href', '/library?search=Alien')
    expect(link.closest('p')).toHaveTextContent(NEXT_CHECK_NO_DROP)
  })

  it('links nothing to the Library page for a replacement the move stopped', async () => {
    // Arrange
    serveDetails(checkStopOf({ stop: 'Move', reason: HELD }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    expect(within(section).queryByRole('link')).not.toBeInTheDocument()
  })

  it.each<[string, Partial<ReplacementDetails>, string, string | null]>([
    ['while the download goes on', { state: 'Downloading' }, 'Waits for the download.', null],
    ['once the download is done', { state: 'Downloaded' }, 'The check of the new files starts within a few seconds.', null],
    [
      'while the new files are read',
      { state: 'Inspecting', checkStage: 'Files', checkStartedUtc: CHECK_STARTED },
      `Reading the new files on the disk (started ${timeOf(CHECK_STARTED)}).`,
      null,
    ],
    [
      'while it waits for Jellyfin, until when',
      { state: 'Inspecting', checkStage: 'Library', checkStartedUtc: CHECK_STARTED, libraryAskedUtc: LIBRARY_ASKED, checkWaitsUntilUtc: LIBRARY_WAITS_UNTIL },
      `Waiting for Jellyfin to identify the new files until ${timeOf(LIBRARY_WAITS_UNTIL)}.`,
      null,
    ],
    [
      'once verified',
      { state: 'Verified', copies: [jobCopyOf({ status: 'Paired' })], newFiles: [newFileOf()] },
      'Every old copy is paired with a new file Jellyfin identified.',
      'Succeeded',
    ],
  ])('says how far the replacement got %s', async (_, changes, sentence, status) => {
    // Arrange
    serveDetails(checkStopOf({
      reason: null, stop: null, canRetry: false, copies: [jobCopyOf({ pairedNewFileId: null, status: 'Waiting' })], newFiles: [], ...changes,
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const line = within(section).getByText(sentence).closest('p')
    expect(line).toHaveClass('library-source-status')
    expect(line?.getAttribute('data-status')).toBe(status)
  })

  it.each<[string, ReplacementDetails, Partial<DownloadJobReplacement>, string]>([
    ['the check stopped', checkStopOf(), {}, `Replacement needs review: ${NO_ID}`],
    [
      'a copy only dropping it settles',
      checkStopOf({ reason: RENUMBERED, canRetry: false, dropOnlyCopyCount: 1, copies: [renumberedCopy, unpairedCopy], newFiles: [] }),
      {},
      `Replacement needs review: ${RENUMBERED}`,
    ],
    [
      'a file stayed in its folder',
      replacementDetailsOf(),
      { quarantinedCopyCount: 1, stayedFileCount: 1 },
      'Old copy moved into the quarantine: 1 copy, kept until you restore it or delete it for good. 1 file stayed in its folder.',
    ],
  ])('says in the job\'s own Status fact where the replacement stands when %s, without sending the owner to the job he is on', async (_, details, changes, sentence) => {
    // Arrange
    serve({ ...downloadedJob, replacement: lineOf(details, changes) }, details)

    // Act
    renderPage()

    // Assert
    await within(await screen.findByRole('region', { name: 'Replacement' })).findAllByRole('table')
    const status = screen.getByText('Status', { selector: 'dt' }).nextElementSibling as HTMLElement
    expect(within(status).getByText(sentence)).toBeInTheDocument()
    expect(status).not.toHaveTextContent(/see the job/)
  })

  it('says the check stopped, as a warning', async () => {
    // Arrange
    serveDetails(checkStopOf())

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    expect(within(section).getByText('The check stopped.').closest('p')).toHaveClass('library-source-warning')
  })

  // ── the copies ──

  it('lists each ticked copy with what it was, the new file it is paired with and where it stands, and offers no Drop where none applies', async () => {
    // Arrange
    serveDetails(replacementDetailsOf({
      state: 'Verified', canRestore: false, canDeleteForGood: false, canCancel: true, quarantinedBytes: 0, freesBytes: 0, names: [],
      copies: [jobCopyOf({ status: 'Paired', editionLabel: 'Theatrical Cut' })],
      newFiles: [newFileOf()],
      works: [workOf()],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const copies = within(section).getByRole('region', { name: 'Old copies and their new files' })
    expect(within(copies).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Old copy', 'New file', 'Where it stands'])
    const row = copyRowOf(section, alienVideo.originalPath)
    const cells = [...row.querySelectorAll('td')]
    expect(cells.map((cell) => cell.getAttribute('data-label'))).toEqual(['Old copy', 'New file', 'Where it stands'])
    expect(cells.every((cell) => cell.classList.contains('jobs-stacked-cell'))).toBe(true)
    expect(within(cells[0]).getByText('40 GiB · 1:56:00 · Theatrical Cut')).toHaveClass('ui-hint')
    expect(within(cells[1]).getByText(newFileOf().appPath!, { selector: '.jobs-file-path' })).toBeInTheDocument()
    expect(within(cells[1]).getByText('60 GiB · 1:56:30')).toHaveClass('ui-hint')
    expect([...cells[1].querySelectorAll('.library-version-chip')].map((chip) => chip.textContent)).toEqual(['2160p', 'HEVC'])
    const status = within(cells[2]).getByText('Paired with its new file: it moves into the quarantine when the replacement goes ahead.').closest('p')
    expect(status).toHaveClass('library-source-status')
    expect(status).toHaveAttribute('data-status', 'Succeeded')
    expect(within(copies).queryByRole('button')).not.toBeInTheDocument()
    expect(within(copies).queryByText(/Nothing moves until/)).not.toBeInTheDocument()
  })

  it('says a copy stopped the replacement with the server\'s sentence, and that nothing moves until every copy is paired or dropped', async () => {
    // Arrange
    serveDetails(checkStopOf())

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const row = within(copyRowOf(section, alienVideo.originalPath))
    expect(row.getByText(`Stopped the replacement: ${NO_ID}`).closest('p')).toHaveClass('library-source-warning')
    expect(row.getByText('No new file paired.')).toHaveClass('settings-muted')
    expect(within(section).getByText('Nothing moves until every copy is paired or dropped: 0 of 1 paired, 0 dropped.')).toBeInTheDocument()
  })

  it('says a copy waiting for the check is paired once Jellyfin has identified every new file', async () => {
    // Arrange
    serveDetails(checkStopOf({
      state: 'Inspecting', reason: null, stop: null, canRetry: false, checkStage: 'Library', checkWaitsUntilUtc: LIBRARY_WAITS_UNTIL,
      copies: [jobCopyOf({ pairedNewFileId: null, status: 'Waiting' })],
    }))

    // Act
    renderPage()

    // Assert
    const row = within(copyRowOf(await loadedSection(), alienVideo.originalPath))
    expect(row.getByText('Paired once Jellyfin has identified every new file.')).toBeInTheDocument()
    expect(row.getByText('Kept until the new file is verified.').closest('p')).toHaveClass('library-source-status')
  })

  it('greys a dropped copy, keeps its last problem as history, and offers no Drop for it', async () => {
    // Arrange
    serveDetails(checkStopOf({
      copies: [
        { ...renumberedCopy, droppedUtc: '2026-10-10T14:00:00Z', status: 'Dropped', canDrop: false },
        episodeCopyOf(6, { problem: NO_ID, status: 'Stopped', canDrop: true }),
        episodeCopyOf(7, { problem: NO_ID, status: 'Stopped', canDrop: true }),
      ],
      newFiles: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const dropped = copyRowOf(section, renumberedCopy.appPath)
    expect(dropped).toHaveClass('jobs-copy-dropped')
    expect(within(dropped).getByText('Dropped from the replacement: it stays where it is.')).toBeInTheDocument()
    expect(within(dropped).getByText(RENUMBERED)).toHaveClass('ui-hint')
    expect(within(dropped).queryByText(ONLY_DROP)).not.toBeInTheDocument()
    expect(within(dropped).queryByRole('button')).not.toBeInTheDocument()
    expect(within(copyRowOf(section, episodeCopyOf(6).appPath)).getByRole('button', { name: 'Drop' })).toBeEnabled()
    expect(within(section).getByText('Nothing moves until every copy is paired or dropped: 0 of 2 paired, 1 dropped.')).toBeInTheDocument()
  })

  // ── Drop ──

  it('drops a copy after a confirmation that says it stays where it is, Drop it first and Keep it in last, and says so above the job', async () => {
    // Arrange
    const details = checkStopOf({ copies: [unpairedCopy, episodeCopyOf(7, { status: 'NotPaired', canDrop: true })], newFiles: [] })
    serveDetails(details)
    vi.mocked(replacementsApi.dropCopy).mockResolvedValue({ ...downloadedJob, replacement: lineOf(details) })
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(copyRowOf(section, unpairedCopy.appPath)).getByRole('button', { name: 'Drop' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E06.mkv” from the replacement?' })
    const buttons = footerButtons(confirmation)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Drop it' }))

    // Assert
    expect(confirmation).toHaveTextContent(DROP_MESSAGE)
    expect(buttons.map((button) => button.textContent)).toEqual(['Drop it', 'Keep it in'])
    expect(buttons[0]).not.toHaveClass('ui-button-destructive')
    expect(replacementsApi.dropCopy).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, unpairedCopy.id)
    const result = await screen.findByRole('status')
    expect(result).toHaveTextContent('Dropped “Yellowstone S02E06.mkv”: it stays where it is.')
    expect(section).not.toContainElement(result)
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
    expect(replacementsApi.retry).not.toHaveBeenCalled()
  })

  it('drops nothing on Keep it in', async () => {
    // Arrange
    serveDetails(checkStopOf({ copies: [unpairedCopy, episodeCopyOf(7, { status: 'NotPaired', canDrop: true })], newFiles: [] }))
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(copyRowOf(section, unpairedCopy.appPath)).getByRole('button', { name: 'Drop' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Drop “Yellowstone S02E06.mkv” from the replacement?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Keep it in' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.dropCopy).not.toHaveBeenCalled()
  })

  it('says Dropping… with a spinner on that copy\'s row only, and holds every other action of the replacement meanwhile', async () => {
    // Arrange
    const other = episodeCopyOf(7, { status: 'NotPaired', canDrop: true })
    serveDetails(checkStopOf({ copies: [unpairedCopy, other], newFiles: [] }))
    vi.mocked(replacementsApi.dropCopy).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(copyRowOf(section, unpairedCopy.appPath)).getByRole('button', { name: 'Drop' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Drop it' }))

    // Assert
    const dropping = await within(copyRowOf(section, unpairedCopy.appPath)).findByRole('button', { name: 'Dropping…' })
    expect(dropping).toBeDisabled()
    expect(dropping.querySelector('svg')).toHaveClass('animate-spin')
    const otherDrop = within(copyRowOf(section, other.appPath)).getByRole('button', { name: 'Drop' })
    expect(otherDrop).toBeDisabled()
    expect(otherDrop.querySelector('svg')).not.toHaveClass('animate-spin')
    expect(within(section).getByRole('button', { name: 'Retry' })).toBeDisabled()
    expect(within(section).getByRole('button', { name: 'Keep both' })).toBeDisabled()
  })

  it('shows the server\'s sentence in the page\'s error banner when a drop is refused', async () => {
    // Arrange
    const sentence = '“Yellowstone S02E06.mkv” is the last copy this replacement would replace, so it can\'t be dropped: keep both copies instead.'
    serveDetails(checkStopOf({ copies: [unpairedCopy, episodeCopyOf(7, { status: 'NotPaired', canDrop: true })], newFiles: [] }))
    vi.mocked(replacementsApi.dropCopy).mockRejectedValue(apiError(409, sentence))
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(copyRowOf(section, unpairedCopy.appPath)).getByRole('button', { name: 'Drop' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Drop it' }))

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    expect(section).not.toContainElement(banner)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('disables the last copy\'s Drop, saying why on hover and in a line of its own', async () => {
    // Arrange
    serveDetails(checkStopOf({
      copies: [
        { ...unpairedCopy, canDrop: false },
        episodeCopyOf(7, { droppedUtc: '2026-10-10T14:00:00Z', status: 'Dropped' }),
      ],
      newFiles: [],
    }))
    const { user } = renderPage()
    const section = await loadedSection()
    const row = copyRowOf(section, unpairedCopy.appPath)
    const drop = within(row).getByRole('button', { name: 'Drop' })

    // Act
    await user.click(drop)

    // Assert
    expect(drop).toBeDisabled()
    expect(drop.closest('.settings-action-reason')).toHaveAttribute('title', LAST_COPY)
    expect(within(row).getByText(LAST_COPY)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(within(section).getByRole('button', { name: 'Keep both' })).toBeEnabled()
  })

  // ── Retry ──

  it('shows Retry disabled while a copy only dropping it settles is in the replacement, saying why on hover and in a line of its own', async () => {
    // Arrange
    serveDetails(checkStopOf({
      reason: RENUMBERED, canRetry: false, dropOnlyCopyCount: 1, copies: [renumberedCopy, unpairedCopy], newFiles: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const retry = within(section).getByRole('button', { name: 'Retry' })
    expect(retry).toBeDisabled()
    expect(retry.closest('.settings-action-reason')).toHaveAttribute('title', BLOCKED_BY_DROP)
    expect(within(section).getByText(BLOCKED_BY_DROP).closest('p')).toHaveClass('library-source-warning')
    const row = within(copyRowOf(section, renumberedCopy.appPath))
    expect(row.getByText(`Stopped the replacement: ${RENUMBERED}`)).toBeInTheDocument()
    expect(row.getByText(ONLY_DROP)).toBeInTheDocument()
    expect(row.getByRole('button', { name: 'Drop' })).toBeEnabled()
    expect(within(copyRowOf(section, unpairedCopy.appPath)).queryByText(ONLY_DROP)).not.toBeInTheDocument()
  })

  it('shows Retry disabled for a film whose one copy only dropping it settles, pointing to Keep both, as the last copy is never dropped', async () => {
    // Arrange
    serveDetails(checkStopOf({ reason: TOO_SHORT, canRetry: false, dropOnlyCopyCount: 1, copies: [tooShortCopy] }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const retry = within(section).getByRole('button', { name: 'Retry' })
    expect(retry).toBeDisabled()
    expect(retry.closest('.settings-action-reason')).toHaveAttribute('title', BLOCKED_KEEP_BOTH)
    expect(within(section).getByText(BLOCKED_KEEP_BOTH).closest('p')).toHaveClass('library-source-warning')
    expect(within(section).queryByText(BLOCKED_BY_DROP)).not.toBeInTheDocument()
    expect(within(section).getByText(NEXT_DROP_ONLY_LAST)).toBeInTheDocument()
    const row = within(copyRowOf(section, alienVideo.originalPath))
    expect(row.getByRole('button', { name: 'Drop' })).toBeDisabled()
    expect(row.getByText(LAST_COPY)).toBeInTheDocument()
    expect(row.queryByText(ONLY_DROP)).not.toBeInTheDocument()
    expect(within(section).getByRole('button', { name: 'Keep both' })).toBeEnabled()
  })

  it('sends a replacement whose check stopped back to the check, without a confirmation, and says Jellyfin is asked again', async () => {
    // Arrange
    serveDetails(checkStopOf())
    const retried = { ...downloadedJob, replacement: replacementOf({ state: 'Downloaded' }) }
    vi.mocked(replacementsApi.retry).mockResolvedValue(retried)
    const { user } = renderPage()
    const section = await loadedSection()
    const retry = within(section).getByRole('button', { name: 'Retry' })

    // Act
    await user.click(retry)

    // Assert
    expect(retry).toHaveAttribute('title', 'Check the new files again: Jellyfin\'s ids decide whether the old copies move')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.retry).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    expect(await screen.findByRole('status')).toHaveTextContent('Retried: the app checks the new files again and asks Jellyfin to look at them again.')
  })

  // The server never offers Keep both and Restore together (a name in the quarantine refuses Keep
  // both); the row's order holds whichever it offers.
  it('keeps its actions in one row, in one order: Retry, Keep both, Restore, Delete for good', async () => {
    // Arrange
    serveDetails(checkStopOf({ stop: 'Move', reason: HELD, canRestore: true, canDeleteForGood: true }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const actions = section.querySelector<HTMLElement>('.settings-header .settings-actions')!
    expect(within(actions).getAllByRole('button').map((button) => button.textContent)).toEqual(['Retry', 'Keep both', 'Restore', 'Delete for good'])
  })

  // ── Keep both ──

  it('cancels only the replacement after a confirmation naming the release and its client, Keep both first and Keep replacing last, and says so', async () => {
    // Arrange
    const details = checkStopOf()
    serveDetails(details)
    const cancelled = { ...downloadedJob, replacement: replacementOf({ state: 'Cancelled', reason: 'You cancelled the replacement, so nothing will be replaced.' }) }
    vi.mocked(replacementsApi.cancel).mockResolvedValue(cancelled)
    const { user } = renderPage()
    const section = await loadedSection()
    vi.mocked(downloadsApi.get).mockResolvedValue(cancelled)
    const keepBoth = within(section).getByRole('button', { name: 'Keep both' })
    await user.click(keepBoth)
    const confirmation = await screen.findByRole('dialog', { name: `Keep both copies of ${downloadedJob.releaseTitle}?` })
    const buttons = footerButtons(confirmation)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Keep both' }))

    // Assert
    expect(keepBoth).toHaveAttribute('title', 'Cancel only the replacement: both copies stay, and the download goes on')
    expect(keepBoth).not.toHaveClass('ui-button-danger')
    expect(confirmation).toHaveTextContent(KEEP_BOTH_MESSAGE)
    expect(buttons.map((button) => button.textContent)).toEqual(['Keep both', 'Keep replacing'])
    expect(buttons[0]).not.toHaveClass('ui-button-destructive')
    expect(replacementsApi.cancel).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    expect(downloadsApi.cancel).not.toHaveBeenCalled()
    const result = await screen.findByRole('status')
    expect(result).toHaveTextContent('Replacement cancelled: both copies stay.')
    expect(section).not.toContainElement(result)
  })

  it('names the release and its client in Keep both\'s confirmation though the address spells the job\'s id in capitals', async () => {
    // Arrange
    serveDetails(checkStopOf())
    const { user } = renderPage(owner, `/jobs/${downloadedJob.id.toUpperCase()}`)
    const section = await loadedSection()

    // Act
    await user.click(within(section).getByRole('button', { name: 'Keep both' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: `Keep both copies of ${downloadedJob.releaseTitle}?` })
    expect(confirmation).toHaveTextContent(KEEP_BOTH_MESSAGE)
  })

  it('cancels nothing on Keep replacing', async () => {
    // Arrange
    serveDetails(checkStopOf())
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(section).getByRole('button', { name: 'Keep both' }))
    const confirmation = await screen.findByRole('dialog', { name: `Keep both copies of ${downloadedJob.releaseTitle}?` })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Keep replacing' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.cancel).not.toHaveBeenCalled()
  })

  it('shows the server\'s sentence in the page\'s error banner when Keep both is refused', async () => {
    // Arrange
    const sentence = 'Part of this replacement has left its folders already, so it can\'t be cancelled: Restore puts back what is in the quarantine.'
    serveDetails(checkStopOf())
    vi.mocked(replacementsApi.cancel).mockRejectedValue(apiError(409, sentence))
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(section).getByRole('button', { name: 'Keep both' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Keep both' }))

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    expect(section).not.toContainElement(banner)
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
  })

  it('says Keeping both… with a spinner while Keep both is on its way, and holds Retry and every Drop meanwhile', async () => {
    // Arrange
    serveDetails(checkStopOf({ copies: [unpairedCopy, episodeCopyOf(7, { status: 'NotPaired', canDrop: true })], newFiles: [] }))
    vi.mocked(replacementsApi.cancel).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const section = await loadedSection()
    await user.click(within(section).getByRole('button', { name: 'Keep both' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Keep both' }))

    // Assert
    const keeping = await within(section).findByRole('button', { name: 'Keeping both…' })
    expect(keeping).toBeDisabled()
    expect(keeping.querySelector('svg')).toHaveClass('animate-spin')
    expect(within(section).getByRole('button', { name: 'Retry' })).toBeDisabled()
    expect(within(section).getAllByRole('button', { name: 'Drop' }).every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
  })

  // ── the new files ──

  it('lists each new file with what the disk and Jellyfin said of it, and the ids its item carries against the expected ones', async () => {
    // Arrange
    serveDetails(checkStopOf({ newFiles: [newFileOf({ identifiers: [{ scheme: 'Imdb', value: 'tt0090605', ofSeries: false, setByOwner: false }], workId: null, progress: 'NoId' })] }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const files = within(section).getByRole('region', { name: 'New files' })
    expect(within(files).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['New file', 'On the disk', 'Jellyfin'])
    const row = newFileRowOf(section, newFileOf().appPath!)
    const cells = [...row.querySelectorAll('td')]
    expect(cells.map((cell) => cell.getAttribute('data-label'))).toEqual(['New file', 'On the disk', 'Jellyfin'])
    expect(within(cells[1]).getByText('60 GiB · 1:56:30')).toHaveClass('ui-hint')
    expect([...cells[1].querySelectorAll('.library-version-chip')].map((chip) => chip.textContent)).toEqual(['2160p', 'HEVC'])
    // The check is over, so a step it never reached is a warning, not a wait.
    expect(within(cells[2]).getByText('No TMDb or IMDb id from Jellyfin yet.').closest('p')).toHaveClass('library-source-warning')
    expect([...cells[2].querySelectorAll('.library-version-chip')].map((chip) => chip.textContent)).toEqual(['IMDb tt0090605'])
    expect(within(cells[2]).getByText('Expected: IMDb tt0078748 · TMDb 348')).toHaveClass('ui-hint')
  })

  it('names the work Jellyfin identified a new file as, with its ids, and expects nothing more of it', async () => {
    // Arrange
    serveDetails(replacementDetailsOf({
      state: 'Verified', canRestore: false, canDeleteForGood: false, canCancel: true, quarantinedBytes: 0, freesBytes: 0, names: [],
      copies: [jobCopyOf({ status: 'Paired' })],
      newFiles: [newFileOf()],
      works: [workOf()],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const cell = within(newFileRowOf(section, newFileOf().appPath!).querySelectorAll('td')[2])
    expect(cell.getByText('Identified by Jellyfin.').closest('p')).toHaveAttribute('data-status', 'Succeeded')
    expect(cell.getByText('Identified: Alien (1979)')).toBeInTheDocument()
    expect(cell.getByText('IMDb tt0078748')).toHaveClass('library-version-chip')
    expect(cell.queryByText(/^Expected:/)).not.toBeInTheDocument()
  })

  it('says a new file stopped the replacement with the server\'s sentence', async () => {
    // Arrange
    const problem = '“Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv” runs 1:20:00, under ¾ of “Alien.mkv”\'s 1:56:00.'
    serveDetails(checkStopOf({ reason: problem, newFiles: [newFileOf({ problem, progress: 'Stopped' })] }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const line = within(newFileRowOf(section, newFileOf().appPath!)).getByText(`Stopped the replacement: ${problem}`).closest('p')
    expect(line).toHaveClass('library-source-warning')
  })

  it('warns of what a pass found of a new file while the check still waits on another, without saying it stopped', async () => {
    // Arrange
    const problem = '“Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv” runs 1:20:00, under ¾ of “Alien.mkv”\'s 1:56:00.'
    serveDetails(checkStopOf({
      state: 'Inspecting', reason: null, stop: null, canRetry: false, checkStage: 'Library', checkWaitsUntilUtc: LIBRARY_WAITS_UNTIL,
      copies: [jobCopyOf({ pairedNewFileId: null, status: 'Waiting' })],
      newFiles: [newFileOf({ problem, progress: 'AskedToLook' })],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const row = within(newFileRowOf(section, newFileOf().appPath!))
    expect(row.getByText('Jellyfin asked to look at it.').closest('p')).toHaveClass('library-source-status')
    expect(row.getByText(problem).closest('p')).toHaveClass('library-source-warning')
    expect(row.queryByText(/^Stopped the replacement:/)).not.toBeInTheDocument()
  })

  it('shows how far the client got on a new file not downloaded yet', async () => {
    // Arrange
    serveDetails(checkStopOf({
      state: 'Inspecting', reason: null, stop: null, canRetry: false, checkStage: 'Files',
      newFiles: [newFileOf({ clientProgress: 0.5, checkedUtc: null, sizeBytes: null, durationMilliseconds: null, videoCodec: null, width: null, height: null, progress: 'Downloading' })],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const row = within(newFileRowOf(section, newFileOf().appPath!))
    expect(row.getByRole('progressbar', { name: 'Download progress' })).toHaveAttribute('value', '0.5')
    expect(row.getByText('Not downloaded yet.').closest('p')).toHaveClass('library-source-status')
  })

  it.each<[ReplacementNewFileProgress, string]>([
    ['Downloading', 'Not downloaded yet.'],
    ['NotChecked', 'Not read on the disk yet.'],
    ['Checked', 'Read on the disk.'],
    ['AskedToLook', 'Jellyfin asked to look at it.'],
    ['NotListed', 'Not listed by Jellyfin yet.'],
    ['NotRead', 'Jellyfin hasn\'t finished reading it.'],
    ['Extra', 'An extra: simply added.'],
    ['NoId', 'No TMDb or IMDb id from Jellyfin yet.'],
    ['NotNumbered', 'No season and episode number from Jellyfin yet.'],
    ['Identified', 'Identified by Jellyfin.'],
  ])('says how far a new file got while the check runs: %s', async (progress, sentence) => {
    // Arrange
    serveDetails(checkStopOf({
      state: 'Inspecting', reason: null, stop: null, canRetry: false, checkStage: 'Library', checkWaitsUntilUtc: LIBRARY_WAITS_UNTIL,
      copies: [jobCopyOf({ pairedNewFileId: null, status: 'Waiting' })],
      newFiles: [newFileOf({ progress })],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const line = within(newFileRowOf(section, newFileOf().appPath!)).getByText(sentence).closest('p')
    expect(line).toHaveClass('library-source-status')
  })

  it('shows no New files table before the check listed any', async () => {
    // Arrange
    serveDetails(checkStopOf({ state: 'Downloading', reason: null, stop: null, canRetry: false, newFiles: [], copies: [jobCopyOf({ pairedNewFileId: null, status: 'Waiting' })] }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    expect(within(section).queryByRole('region', { name: 'New files' })).not.toBeInTheDocument()
    expect(within(section).getByText('Waits for the download.')).toBeInTheDocument()
  })

  it('names a copy and a new file of several episodes by their range in both tables, as the check\'s sentence does', async () => {
    // Arrange
    const covers = 'Jellyfin numbers “Yellowstone S02E05-E06.mkv” as season 2, episodes 5–6, so it can\'t replace “Yellowstone S02E05.mkv” alone.'
    const pack = newFileOf({
      appPath: '/media/tv/Yellowstone/Season 2/Yellowstone S02E05-E06.mkv', kind: 'Episode', seasonNumber: 2, episodeNumber: 5, episodeNumberEnd: 6,
    })
    serveDetails(checkStopOf({
      reason: covers, canRetry: false, dropOnlyCopyCount: 1,
      copies: [
        episodeCopyOf(5, { problem: covers, onlyDropSettles: true, status: 'Stopped', canDrop: true }),
        episodeCopyOf(7, { episodeNumberEnd: 8, status: 'NotPaired', canDrop: true }),
      ],
      newFiles: [pack],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    expect(within(newFileRowOf(section, pack.appPath!)).getByText('Season 2 · Episodes 5–6')).toHaveClass('ui-hint')
    expect(within(copyRowOf(section, episodeCopyOf(7).appPath)).getByText('3 GiB · 50:00 · Season 2 · Episodes 7–8')).toHaveClass('ui-hint')
    expect(within(copyRowOf(section, episodeCopyOf(5).appPath)).getByText('3 GiB · 50:00 · Season 2 · Episode 5')).toHaveClass('ui-hint')
  })

  it('draws a paired new file alike in the copies table and the New files table: its facts, then its chips', async () => {
    // Arrange
    serveDetails(replacementDetailsOf({
      state: 'Verified', canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
      copies: [jobCopyOf({ status: 'Paired' })],
      newFiles: [newFileOf()],
      works: [workOf()],
    }))

    // Act
    renderPage()

    // Assert
    const section = await loadedSection()
    const paired = copyRowOf(section, alienVideo.originalPath).querySelectorAll('td')[1].querySelector('.jobs-name-paths')!
    const disk = newFileRowOf(section, newFileOf().appPath!).querySelectorAll('td')[1].querySelector('.jobs-name-paths')!
    // The copies table leads with the path, as the New files table has a column of its own for it.
    expect(paired.firstElementChild).toHaveTextContent(newFileOf().appPath!)
    expect([...paired.children].slice(1).map((child) => child.outerHTML)).toEqual([...disk.children].map((child) => child.outerHTML))
    expect([...disk.children].map((child) => child.className)).toEqual(['ui-hint', 'library-version-chips'])
  })

  it('pairs a copy with the new file by the server\'s id, never by its own reading', async () => {
    // Arrange
    const other = newFileOf({ id: 'e1a50000-0000-4000-8000-000000000302', position: 1, appPath: '/media/movies/Alien.1979.Sample.mkv', relativePath: 'Sample.mkv' })
    serveDetails(replacementDetailsOf({
      state: 'Verified', canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
      copies: [jobCopyOf({ status: 'Paired', pairedNewFileId: ALIEN_NEW_FILE_ID })],
      newFiles: [other, newFileOf()],
      works: [workOf()],
    }))

    // Act
    renderPage()

    // Assert
    const row = within(copyRowOf(await loadedSection(), alienVideo.originalPath))
    expect(row.getByText(newFileOf().appPath!, { selector: '.jobs-file-path' })).toBeInTheDocument()
    expect(row.queryByText(other.appPath!)).not.toBeInTheDocument()
  })
})
