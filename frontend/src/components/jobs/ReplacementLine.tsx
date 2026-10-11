import { Archive, CircleCheck, Clock, Loader2, TriangleAlert, Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { isReplacementBeingChecked } from '@/lib/download-jobs'
import { formatDateTime, formatNumber } from '@/lib/format'
import type { DownloadJobReplacement, ReplacementJobState } from '@/lib/types'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the line and its icon; [data-status='Succeeded'] greens the icon

/** The states a replacement waits in for its download: it is kept until the new copy is verified. */
const SCHEDULED_STATES: ReadonlySet<ReplacementJobState> = new Set<ReplacementJobState>(['Scheduled', 'Submitted', 'Downloading', 'Paused'])

/**
 * MAM-27 — until when the check waits for Jellyfin: the time alone, as the job's page says it. The wait
 * is an hour at most, so the day goes without saying.
 */
const WAIT_FORMAT: Intl.DateTimeFormatOptions = { timeStyle: 'short' }

/**
 * MAM-26 — the sentence of each state the app leaves by itself once it is done renaming. Counted by
 * the copies ticked rather than the ones in the quarantine, which change under way.
 */
const UNDER_WAY_KEYS = {
  Quarantining: 'replacement.quarantining',
  Restoring: 'replacement.restoring',
  Deleting: 'replacement.deleting',
} as const

/**
 * MAM-24 — the replacement a download job carries, as one short line (Vasyl's decision of
 * 2026-10-09): scheduled while its download goes on, or stopped, with the server's reason. MAM-25 —
 * once the download is done, its new file being checked, then verified (green, like a downloaded
 * job), or in need of review with the server's reason. MAM-26 — verified, its old copies move into
 * the quarantine next; then moving (a spinner, as a download under way), moved and kept until the
 * owner decides — with the files that stayed in their folder, if any —, being put back or deleted,
 * and at the end undone (both copies back) or replaced (green: the old copy deleted for good).
 * While it is scheduled, checked or verified the line also says what won't move: everything, while
 * replacement is switched off (ADR 0033: the checks still run), or — the copies still to move whose
 * folder is no longer ticked — everything too unless they are ticked again, as the check right
 * before the move stops the whole replacement for review over one such copy (MAM-26); verified with
 * replacement on, that is all it says. Once the mover has taken it up, a change of the switch or the
 * ticks stops it for review rather than holding it, and a replacement that stopped (needs review,
 * failed or cancelled) goes no further until the owner acts — one in review still holds its copies
 * —, so it says none of that.
 *
 * MAM-27 — while the check waits for Jellyfin to identify the new files, until what time it waits. In
 * review, after the server's reason, what to do next where the reason alone doesn't say it: keep both
 * or cancel the job when the app can't follow the download; fix the item and press Retry when the
 * check stopped — or, when a copy only a drop settles is in it, drop it on the job's page, or keep
 * both when every copy left is one, as the last is never dropped. The mover's own reasons already end
 * with what to do ("…then press Retry."), so nothing is added to them. Its counts leave out the copies
 * the owner dropped, so in every state the line then says how many were dropped: each stays where it
 * is. In the job's own facts it adds neither what to do nor "see the job": the Replacement section
 * under them says it (`onJobPage`).
 *
 * On a Failed download it gives no reason: the job's red line already says why. No live region:
 * the Download dialog's outcome line before it is the one that speaks, and a list's rows never do.
 * Shared by the Jobs page's status block (JobStatus) and the Download dialog's sent view.
 */
export function ReplacementLine({ replacement, downloadFailed, onJobPage = false }: {
  replacement: DownloadJobReplacement
  /** The download job failed: its own error line says why the replacement stopped. */
  downloadFailed: boolean
  /**
   * MAM-27 — the line sits in the job's own facts, above its Replacement section: it says neither what
   * to do nor "see the job", as the section right under it says that at length.
   */
  onJobPage?: boolean
}) {
  const { t } = useTranslation('jobs')
  const {
    state, copyCount, reason, switchedOff, copiesOutsideTickedFolders, quarantinedCopyCount, stayedFileCount,
    droppedCopyCount, dropOnlyCopyCount, stop, checkStage, checkWaitsUntilUtc,
  } = replacement
  const outside = copiesOutsideTickedFolders === 0
    ? null
    : t('replacement.outsideTickedFolders', { count: copiesOutsideTickedFolders, value: formatNumber(copiesOutsideTickedFolders) })
  // MAM-27 — the clause starts with its own space (jobs.json), so it is joined without one.
  const dropped = droppedCopyCount === 0
    ? ''
    : t('replacement.dropped', { count: droppedCopyCount, value: formatNumber(droppedCopyCount) })
  /** A state's sentence, then the copies dropped from the replacement, if any: its counts leave them out. */
  const told = (sentence: string) => `${sentence}${dropped}`
  /** A held state's sentence, then the copies whose folder would stop the move, if any. */
  const held = (sentence: string) => (outside === null ? told(sentence) : `${told(sentence)} ${outside}`)
  const counted = { count: copyCount, value: formatNumber(copyCount) }

  if (SCHEDULED_STATES.has(state)) {
    return (
      <p className="library-source-status">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{held(switchedOff ? t('replacement.switchedOff') : t('replacement.scheduled', counted))}</span>
      </p>
    )
  }
  // MAM-25 — Downloaded lasts only until the next check pass takes the job up, so it reads as the
  // check itself rather than as a wait of its own. MAM-27 — once Jellyfin was asked to look, the wait
  // has an end, so the line names it; while replacement is switched off, that nothing will move is
  // still what the line says, as in every state before the move.
  if (isReplacementBeingChecked(state)) {
    const checking = switchedOff
      ? t('replacement.checkingSwitchedOff')
      : checkStage === 'Library' && checkWaitsUntilUtc !== null
        ? t('replacement.checkingLibrary', { ...counted, time: formatDateTime(checkWaitsUntilUtc, WAIT_FORMAT) })
        : t('replacement.checking', counted)
    return (
      <p className="library-source-status">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{held(checking)}</span>
      </p>
    )
  }
  if (state === 'Verified') {
    // MAM-26 — with replacement on, a copy outside a ticked folder stops the move on the mover's next
    // pass: nothing "moves next", so the line says only that.
    const verified = switchedOff
      ? held(t('replacement.verifiedSwitchedOff'))
      : outside === null
        ? told(t('replacement.verified', counted))
        : told(t('replacement.verifiedOutside', { count: copiesOutsideTickedFolders, value: formatNumber(copiesOutsideTickedFolders) }))
    return (
      <p className="library-source-status" data-status="Succeeded">
        <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{verified}</span>
      </p>
    )
  }
  // MAM-26 — work under way inside one pass or one request: it spins, as a download under way does.
  if (state === 'Quarantining' || state === 'Restoring' || state === 'Deleting') {
    return (
      <p className="library-source-status">
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        <span>{told(t(UNDER_WAY_KEYS[state], { count: copyCount }))}</span>
      </p>
    )
  }
  if (state === 'Quarantined') {
    const moved = told(t('replacement.quarantined', { count: quarantinedCopyCount, value: formatNumber(quarantinedCopyCount) }))
    // The job's own page lists those files right under its facts.
    const stayedCounted = { count: stayedFileCount, value: formatNumber(stayedFileCount) }
    const stayed = onJobPage ? t('replacement.stayedHere', stayedCounted) : t('replacement.stayed', stayedCounted)
    return (
      <p className="library-source-status">
        <Archive className="h-3.5 w-3.5" aria-hidden="true" />
        <span>
          {stayedFileCount === 0 ? moved : `${moved} ${stayed}`}
        </span>
      </p>
    )
  }
  if (state === 'Restored') {
    return (
      <p className="library-source-status">
        <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{told(t('replacement.restored', { count: copyCount }))}</span>
      </p>
    )
  }
  if (state === 'Completed') {
    return (
      <p className="library-source-status" data-status="Succeeded">
        <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{told(t('replacement.completed', { count: copyCount }))}</span>
      </p>
    )
  }
  const unexplained = downloadFailed || reason === null
  if (state === 'NeedsReview') {
    // MAM-27 — the clause starts with its own space, as the dropped one does. A copy only a drop
    // settles comes before the check's own advice: Retry isn't offered while it is in the replacement.
    // When every copy left is one only a drop settles, the last can't be dropped: keeping both is all
    // that settles it (`copyCount` leaves the dropped copies out).
    const dropOnly = { count: dropOnlyCopyCount, value: formatNumber(dropOnlyCopyCount) }
    const next = onJobPage
      ? ''
      : stop === 'Download'
        ? t('replacement.next.Download')
        : dropOnlyCopyCount > 0
          ? dropOnlyCopyCount < copyCount ? t('replacement.next.dropOnly', dropOnly) : t('replacement.next.dropOnlyKeepBoth', dropOnly)
          : stop === 'Check'
            ? t('replacement.next.Check')
            : ''
    const review = told(unexplained ? t('replacement.needsReviewNoReason') : t('replacement.needsReview', { reason }))
    return (
      <p className="library-source-warning">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{`${review}${next}`}</span>
      </p>
    )
  }
  return (
    <p className="library-source-warning">
      <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{told(unexplained ? t('replacement.stoppedNoReason') : t('replacement.stopped', { reason }))}</span>
    </p>
  )
}
