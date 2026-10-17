import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { ReplacementSummary } from '@/components/jobs/ReplacementSummary'
import { formatDateTime } from '@/lib/format'
import type { DownloadJobReplacement, ReplacementJobState } from '@/lib/types'
import { pluralFormProbe } from '@/test/plural-probe'
import { replacementOf } from '@/test/replacements'

// MAM-41 — the Replacement cell of an owner's Jobs row, alone: where the replacement stands in a few words
// on one line, for every state; the whole sentence is in the row's details (ReplacementLine).

const WAITS_UNTIL_UTC = '2026-10-11T14:05:00Z'
const waitsUntil = formatDateTime(WAITS_UNTIL_UTC, { timeStyle: 'short' })

/** How a line looks: its class, its library.css tone, and whether its icon spins. */
interface Look {
  line: 'library-source-status' | 'library-source-warning'
  tone: string | null
  spinning: boolean
}

const plain: Look = { line: 'library-source-status', tone: null, spinning: false }
const green: Look = { line: 'library-source-status', tone: 'Succeeded', spinning: false }
const red: Look = { line: 'library-source-status', tone: 'Failed', spinning: false }
const amber: Look = { line: 'library-source-warning', tone: null, spinning: false }
const underWay: Look = { line: 'library-source-status', tone: null, spinning: true }

describe('ReplacementSummary', () => {
  it.each<[string, Partial<DownloadJobReplacement>, string, Look]>([
    ['scheduled, of one copy', { state: 'Scheduled', copyCount: 1 }, 'Waits for the download: 1 copy', plain],
    ['submitted', { state: 'Submitted', copyCount: 2 }, 'Waits for the download: 2 copies', plain],
    ['downloading, of several copies', { state: 'Downloading', copyCount: 18 }, 'Waits for the download: 18 copies', plain],
    ['paused', { state: 'Paused', copyCount: 3 }, 'Waits for the download: 3 copies', plain],
    ['waiting for its download while replacement is switched off', { state: 'Downloading', copyCount: 2, switchedOff: true }, 'Switched off: nothing moves', plain],
    ['downloaded, until the check takes it up', { state: 'Downloaded' }, 'Checking the new files', plain],
    ['inspecting the new files on the disk', { state: 'Inspecting', checkStage: 'Files', checkWaitsUntilUtc: '2026-10-11T13:10:00Z' }, 'Checking the new files', plain],
    ['waiting for Jellyfin, until when', { state: 'Inspecting', checkStage: 'Library', checkWaitsUntilUtc: WAITS_UNTIL_UTC }, `Waits for Jellyfin until ${waitsUntil}`, plain],
    ['checked while replacement is switched off', { state: 'Inspecting', switchedOff: true, checkStage: 'Library', checkWaitsUntilUtc: WAITS_UNTIL_UTC }, 'Switched off: nothing moves', plain],
    ['verified', { state: 'Verified' }, 'Verified, moves next', green],
    ['verified while replacement is switched off', { state: 'Verified', switchedOff: true }, 'Switched off: nothing moves', plain],
    ['moving into the quarantine', { state: 'Quarantining' }, 'Moving into the quarantine…', underWay],
    ['in the quarantine, one copy', { state: 'Quarantined', copyCount: 1, quarantinedCopyCount: 1 }, '1 old copy in the quarantine', plain],
    ['in the quarantine, counted by the copies there now', { state: 'Quarantined', copyCount: 5, quarantinedCopyCount: 3 }, '3 old copies in the quarantine', plain],
    ['being put back', { state: 'Restoring' }, 'Putting back…', underWay],
    ['being deleted permanently', { state: 'Deleting' }, 'Deleting permanently…', underWay],
    ['undone by a restore', { state: 'Restored', copyCount: 1 }, 'Undone: the old copy is back', plain],
    ['undone by a restore, of several copies', { state: 'Restored', copyCount: 2 }, 'Undone: the old copies are back', plain],
    ['completed', { state: 'Completed', copyCount: 1 }, 'Replaced: old copy deleted', green],
    ['completed, of several copies', { state: 'Completed', copyCount: 18 }, 'Replaced: old copies deleted', green],
    ['stopped by the check', { state: 'NeedsReview', stop: 'Check', reason: 'Jellyfin gave no id.' }, 'Check stopped', amber],
    ['stopped by the mover', { state: 'NeedsReview', stop: 'Move', reason: 'Still held by a torrent.' }, 'Move stopped', amber],
    ['stopped as its download can\'t be followed', { state: 'NeedsReview', stop: 'Download', reason: 'Downloads can\'t be told apart.' }, 'Can\'t follow the download', amber],
    ['in review, with no stop said', { state: 'NeedsReview' }, 'Needs review', amber],
    ['failed', { state: 'Failed', reason: 'The new file has no video stream.' }, 'Stopped', red],
    ['cancelled', { state: 'Cancelled', reason: 'You cancelled the replacement, so nothing will be replaced.' }, 'Cancelled: nothing moved', plain],
  ])('says in a few words where a replacement stands: %s', (_, changes, text, look) => {
    // Arrange
    const replacement = replacementOf(changes)

    // Act
    render(<ReplacementSummary replacement={replacement} />)

    // Assert
    const line = screen.getByText(text).closest('p')!
    expect(line).toHaveClass(look.line)
    expect(line.getAttribute('data-status')).toBe(look.tone)
    const icon = line.querySelector('svg')!
    expect(icon).toHaveAttribute('aria-hidden', 'true')
    expect(icon.classList.contains('animate-spin')).toBe(look.spinning)
    // The few words alone: the reason and what to do are the details' to say.
    expect(line.textContent).toBe(text)
  })

  it('shows a dash for a job with no replacement, read out as No replacement', () => {
    // Arrange
    const replacement = null

    // Act
    const { container } = render(<ReplacementSummary replacement={replacement} />)

    // Assert
    expect(screen.getByText('—')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByText('No replacement')).toHaveClass('sr-only')
    expect(container.firstElementChild).toHaveClass('settings-muted')
    expect(container.querySelector('svg')).toBeNull()
  })

  // MAM-46 — Ukrainian's singular form also counts 21, 31 and 101, so a line that says "the old copy"
  // without its number has a key of its own for exactly one copy. The probe holds only the counted key's
  // Ukrainian forms, so one copy reads the exactly-one key in English.
  it.each<[ReplacementJobState, string, string]>([
    ['Restored', 'summary.restored', 'Undone: the old copy is back'],
    ['Completed', 'summary.completed', 'Replaced: old copy deleted'],
  ])('reads exactly one copy\'s own line, and a plural form for 21, 3 and 5 copies: %s', (state, key, oneCopy) => {
    // Arrange
    const probe = pluralFormProbe('jobs', key, (form) => `form: ${form}`)

    // Act
    const lines = [1, 21, 3, 5].map((copyCount) => {
      const { container, unmount } = render(
        <I18nextProvider i18n={probe}>
          <ReplacementSummary replacement={replacementOf({ state, copyCount })} />
        </I18nextProvider>,
      )
      const text = container.textContent
      unmount()
      return text
    })

    // Assert
    expect(lines).toEqual([oneCopy, 'form: one', 'form: few', 'form: many'])
  })
})
