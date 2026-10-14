import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReplacementCopies } from '@/components/jobs/ReplacementCopies'
import type { ReplacementCopyStatus, ReplacementDetails, ReplacementJobCopy } from '@/lib/types'
import { alienVideo, jobCopyOf, newFileOf, replacementDetailsOf, workOf } from '@/test/replacements'

// MAM-27 — the ticked copies of a replacement, alone: what the job's page and the Jobs list's Copies
// panel both show. The confirmation and the request are the caller's (useDropCopy); here Drop only
// says which copy.

/** The sentences the table says, spelled out rather than read from the locale. */
const RENUMBERED = 'Jellyfin numbers “Yellowstone S02E05.mkv” as season 2, episode 6 now, not episode 5 as you ticked it.'
const ONLY_DROP = 'Only dropping it or keeping both settles this.'
const LAST_COPY = 'It is the replacement\'s last copy: keep both instead.'

/** A ticked episode of Yellowstone's second season, not paired; `changes` sets what a test is about. */
const episodeCopyOf = (episode: number, changes: Partial<ReplacementJobCopy> = {}) => jobCopyOf({
  id: `e1a50000-0000-4000-8000-00000000021${episode}`,
  position: episode - 5,
  appPath: `/media/tv/Yellowstone/Season 2/Yellowstone S02E0${episode}.mkv`,
  sizeBytes: 3_221_225_472,
  seasonNumber: 2,
  episodeNumber: episode,
  durationMilliseconds: 3_000_000,
  pairedNewFileId: null,
  status: 'NotPaired',
  ...changes,
})

/** A replacement whose check stopped for review over `copies`, as its job's page asks for it. */
const stoppedWith = (copies: ReplacementJobCopy[], changes: Partial<ReplacementDetails> = {}) => replacementDetailsOf({
  state: 'NeedsReview',
  stop: 'Check',
  canRestore: false,
  canDeleteForGood: false,
  names: [],
  copies,
  newFiles: [],
  works: [workOf()],
  ...changes,
})

/** Renders the table with a Drop the test watches; `droppingCopyId` is the copy whose drop is on its way. */
function renderCopies(details: ReplacementDetails, busy = false, droppingCopyId: string | null = null) {
  const onDrop = vi.fn<(copy: ReplacementJobCopy) => void>()
  render(<ReplacementCopies details={details} busy={busy} droppingCopyId={droppingCopyId} onDrop={onDrop} />)
  return { onDrop, user: userEvent.setup() }
}

/** The row of the copy at `appPath`. */
const rowOf = (appPath: string) => screen.getByText(appPath, { selector: '.jobs-file-path' }).closest('tr')!

describe('ReplacementCopies', () => {
  it('lists nothing at all for a replacement that lists no copy', () => {
    // Arrange
    const details = replacementDetailsOf({ copies: [] })

    // Act
    renderCopies(details)

    // Assert
    expect(screen.queryByRole('region')).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it.each<[ReplacementCopyStatus, string, string, string | null]>([
    ['Dropped', 'Dropped from the replacement: it stays where it is.', 'library-source-status', null],
    ['Waiting', 'Kept until the new file is verified.', 'library-source-status', null],
    ['Paired', 'Paired with its new file: it moves into the quarantine when the replacement goes ahead.', 'library-source-status', 'Succeeded'],
    ['NotPaired', 'Not paired.', 'library-source-warning', null],
    ['Moving', 'Moving…', 'library-source-status', null],
    ['InQuarantine', 'In the quarantine.', 'library-source-status', null],
    ['Restored', 'Back where it was.', 'library-source-status', null],
    ['Deleted', 'Deleted permanently.', 'library-source-status', null],
    ['Lost', 'Found neither where it was nor in the quarantine.', 'library-source-warning', null],
    ['Stays', 'Stays where it is.', 'library-source-status', null],
  ])('says where a copy stands: %s', (status, sentence, tone, dataStatus) => {
    // Arrange
    const details = replacementDetailsOf({ copies: [jobCopyOf({ status, droppedUtc: status === 'Dropped' ? '2026-10-10T14:00:00Z' : null })], newFiles: [newFileOf()] })

    // Act
    renderCopies(details)

    // Assert
    const line = within(rowOf(alienVideo.originalPath)).getByText(sentence).closest('p')
    expect(line).toHaveClass(tone)
    expect(line?.getAttribute('data-status')).toBe(dataStatus)
    const icon = line?.querySelector('svg')
    expect(icon).toHaveAttribute('aria-hidden', 'true')
    expect(icon?.classList.contains('animate-spin')).toBe(status === 'Moving')
  })

  it('says a copy stopped the replacement in one line with the server\'s sentence, and that only dropping it settles it', () => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5, { problem: RENUMBERED, onlyDropSettles: true, status: 'Stopped', canDrop: true }), episodeCopyOf(6, { canDrop: true })])

    // Act
    renderCopies(details)

    // Assert
    const row = within(rowOf(episodeCopyOf(5).appPath))
    expect(row.getByText(`Stopped the replacement: ${RENUMBERED}`).closest('p')).toHaveClass('library-source-warning')
    expect(row.getByText(ONLY_DROP)).toHaveClass('ui-hint')
    expect(within(rowOf(episodeCopyOf(6).appPath)).queryByText(ONLY_DROP)).not.toBeInTheDocument()
  })

  it('says why a copy that moved part-way isn\'t where it should be, as a warning under where it stands', () => {
    // Arrange
    const problem = '“Alien.en.srt” couldn\'t be moved into the quarantine: the folder is read-only.'
    const details = replacementDetailsOf({ state: 'NeedsReview', stop: 'Move', copies: [jobCopyOf({ problem })], newFiles: [newFileOf()] })

    // Act
    renderCopies(details)

    // Assert
    const row = within(rowOf(alienVideo.originalPath))
    expect(row.getByText('In the quarantine.')).toBeInTheDocument()
    expect(row.getByText(problem).closest('p')).toHaveClass('library-source-warning')
  })

  it('names an episode copy by its size, running time and place in its series', () => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5, { canDrop: true }), episodeCopyOf(6, { canDrop: true })])

    // Act
    renderCopies(details)

    // Assert
    expect(within(rowOf(episodeCopyOf(5).appPath)).getByText('3 GiB · 50:00 · Season 2 · Episode 5')).toHaveClass('ui-hint')
  })

  it('shows the paired new file\'s picture by its frame below 480p, and its codec by name', () => {
    // Arrange
    const details = replacementDetailsOf({
      state: 'Verified',
      copies: [jobCopyOf({ status: 'Paired' })],
      newFiles: [newFileOf({ width: 640, height: 352, videoCodec: 'h264', durationMilliseconds: null, sizeBytes: 734_003_200 })],
    })

    // Act
    renderCopies(details)

    // Assert
    const cell = rowOf(alienVideo.originalPath).querySelectorAll('td')[1]
    expect([...cell.querySelectorAll('.library-version-chip')].map((chip) => chip.textContent)).toEqual(['640×352', 'H.264'])
    expect(within(cell).getByText('700 MiB')).toHaveClass('ui-hint')
  })

  it('asks the caller to drop the copy whose Drop was pressed, and offers it only where the server does', async () => {
    // Arrange
    const dropped = episodeCopyOf(7, { droppedUtc: '2026-10-10T14:00:00Z', status: 'Dropped' })
    const details = stoppedWith([episodeCopyOf(5, { canDrop: true }), episodeCopyOf(6, { canDrop: true }), dropped])
    const { onDrop, user } = renderCopies(details)

    // Act
    await user.click(within(rowOf(episodeCopyOf(6).appPath)).getByRole('button', { name: 'Drop' }))

    // Assert
    expect(onDrop).toHaveBeenCalledExactlyOnceWith(details.copies[1])
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Old copy', 'New file', 'Where it stands', 'Actions'])
    expect(within(rowOf(dropped.appPath)).queryByRole('button')).not.toBeInTheDocument()
    expect(rowOf(dropped.appPath)).toHaveClass('jobs-copy-dropped')
    const drop = within(rowOf(episodeCopyOf(5).appPath)).getByRole('button', { name: 'Drop' })
    expect(drop).toHaveAttribute('title', 'Take this copy out of the replacement: it stays where it is')
    expect(drop.querySelector('.label-text')).toHaveTextContent('Drop')
    expect(drop.closest('td')).toHaveClass('settings-actions-cell')
  })

  it('says Dropping… with a spinner on the copy being dropped only, and holds every Drop while the replacement is busy', () => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5, { canDrop: true }), episodeCopyOf(6, { canDrop: true })])

    // Act
    renderCopies(details, true, episodeCopyOf(5).id)

    // Assert
    const dropping = within(rowOf(episodeCopyOf(5).appPath)).getByRole('button', { name: 'Dropping…' })
    expect(dropping).toBeDisabled()
    expect(dropping.querySelector('svg')).toHaveClass('animate-spin')
    const other = within(rowOf(episodeCopyOf(6).appPath)).getByRole('button', { name: 'Drop' })
    expect(other).toBeDisabled()
    expect(other.querySelector('svg')).not.toHaveClass('animate-spin')
  })

  it('disables the last copy\'s Drop, saying why on hover and in a line of its own', async () => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5), episodeCopyOf(6, { droppedUtc: '2026-10-10T14:00:00Z', status: 'Dropped' })])
    const { onDrop, user } = renderCopies(details)
    const drop = within(rowOf(episodeCopyOf(5).appPath)).getByRole('button', { name: 'Drop' })

    // Act
    await user.click(drop)

    // Assert
    expect(drop).toBeDisabled()
    expect(drop.closest('.settings-action-reason')).toHaveAttribute('title', LAST_COPY)
    expect(within(rowOf(episodeCopyOf(5).appPath)).getByText(LAST_COPY)).toHaveClass('ui-hint')
    expect(onDrop).not.toHaveBeenCalled()
  })

  it('says only that the last copy is kept by keeping both, not that dropping it settles it, when only a drop would', () => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5, { problem: RENUMBERED, onlyDropSettles: true, status: 'Stopped' })], { dropOnlyCopyCount: 1 })

    // Act
    renderCopies(details)

    // Assert
    const row = within(rowOf(episodeCopyOf(5).appPath))
    expect(row.getByText(LAST_COPY)).toHaveClass('ui-hint')
    expect(row.queryByText(ONLY_DROP)).not.toBeInTheDocument()
    expect(row.getByRole('button', { name: 'Drop' })).toBeDisabled()
  })

  it.each<[string, Partial<ReplacementDetails>, ReplacementCopyStatus]>([
    ['while the check runs', { state: 'Inspecting', stop: null }, 'Waiting'],
    ['once the app can\'t follow the download', { stop: 'Download' }, 'NotPaired'],
    ['once its last copy is in the quarantine', { stop: 'Move' }, 'InQuarantine'],
  ])('offers no Drop at all, and doesn\'t call a copy the last one, %s', (_, changes, status) => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5, { status })], changes)

    // Act
    renderCopies(details)

    // Assert
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByText(LAST_COPY)).not.toBeInTheDocument()
    expect(screen.getAllByRole('columnheader')).toHaveLength(3)
  })

  it.each<[string, Partial<ReplacementDetails>, boolean]>([
    ['while the check runs', { state: 'Inspecting', stop: null }, true],
    ['once the check stopped', {}, true],
    ['once the move stopped', { stop: 'Move' }, false],
    ['once verified', { state: 'Verified', stop: null }, false],
  ])('says nothing moves until every copy is paired or dropped, or not, %s', (_, changes, shown) => {
    // Arrange
    const details = stoppedWith([
      episodeCopyOf(5, { pairedNewFileId: newFileOf().id, status: 'Paired' }),
      episodeCopyOf(6),
      episodeCopyOf(7),
      episodeCopyOf(8, { droppedUtc: '2026-10-10T14:00:00Z', status: 'Dropped' }),
    ], { newFiles: [newFileOf()], ...changes })

    // Act
    renderCopies(details)

    // Assert
    const summary = screen.queryByText('Nothing moves until every copy is paired or dropped: 1 of 3 paired, 1 dropped.')
    expect(summary !== null).toBe(shown)
  })

  it('heads the table with its own title, as a region a page can find', () => {
    // Arrange
    const details = stoppedWith([episodeCopyOf(5, { canDrop: true }), episodeCopyOf(6, { canDrop: true })])

    // Act
    renderCopies(details)

    // Assert
    const region = screen.getByRole('region', { name: 'Old copies and their new files' })
    expect(within(region).getByRole('heading', { level: 3 })).toHaveClass('search-details-heading')
    expect([...rowOf(episodeCopyOf(5).appPath).querySelectorAll('td')].map((cell) => cell.getAttribute('data-label')))
      .toEqual(['Old copy', 'New file', 'Where it stands', null])
  })
})
