import { Archive, CircleCheck, Clock, Loader2, TriangleAlert, Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { isReplacementBeingChecked } from '@/lib/download-jobs'
import { formatNumber } from '@/lib/format'
import type { DownloadJobReplacement, ReplacementJobState } from '@/lib/types'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the line and its icon; [data-status='Succeeded'] greens the icon

/** The states a replacement waits in for its download: it is kept until the new copy is verified. */
const SCHEDULED_STATES: ReadonlySet<ReplacementJobState> = new Set<ReplacementJobState>(['Scheduled', 'Submitted', 'Downloading', 'Paused'])

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
 * On a Failed download it gives no reason: the job's red line already says why. No live region:
 * the Download dialog's outcome line before it is the one that speaks, and a list's rows never do.
 * Shared by the Jobs page's status block (JobStatus) and the Download dialog's sent view.
 */
export function ReplacementLine({ replacement, downloadFailed }: {
  replacement: DownloadJobReplacement
  /** The download job failed: its own error line says why the replacement stopped. */
  downloadFailed: boolean
}) {
  const { t } = useTranslation('jobs')
  const { state, copyCount, reason, switchedOff, copiesOutsideTickedFolders, quarantinedCopyCount, stayedFileCount } = replacement
  const outside = copiesOutsideTickedFolders === 0
    ? null
    : t('replacement.outsideTickedFolders', { count: copiesOutsideTickedFolders, value: formatNumber(copiesOutsideTickedFolders) })
  /** A held state's sentence, then the copies whose folder would stop the move, if any. */
  const held = (sentence: string) => (outside === null ? sentence : `${sentence} ${outside}`)
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
  // check itself rather than as a wait of its own.
  if (isReplacementBeingChecked(state)) {
    return (
      <p className="library-source-status">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{held(switchedOff ? t('replacement.checkingSwitchedOff') : t('replacement.checking', counted))}</span>
      </p>
    )
  }
  if (state === 'Verified') {
    // MAM-26 — with replacement on, a copy outside a ticked folder stops the move on the mover's next
    // pass: nothing "moves next", so the line says only that.
    const verified = switchedOff
      ? held(t('replacement.verifiedSwitchedOff'))
      : outside === null
        ? t('replacement.verified', counted)
        : t('replacement.verifiedOutside', { count: copiesOutsideTickedFolders, value: formatNumber(copiesOutsideTickedFolders) })
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
        <span>{t(UNDER_WAY_KEYS[state], { count: copyCount })}</span>
      </p>
    )
  }
  if (state === 'Quarantined') {
    const moved = t('replacement.quarantined', { count: quarantinedCopyCount, value: formatNumber(quarantinedCopyCount) })
    return (
      <p className="library-source-status">
        <Archive className="h-3.5 w-3.5" aria-hidden="true" />
        <span>
          {stayedFileCount === 0
            ? moved
            : `${moved} ${t('replacement.stayed', { count: stayedFileCount, value: formatNumber(stayedFileCount) })}`}
        </span>
      </p>
    )
  }
  if (state === 'Restored') {
    return (
      <p className="library-source-status">
        <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{t('replacement.restored', { count: copyCount })}</span>
      </p>
    )
  }
  if (state === 'Completed') {
    return (
      <p className="library-source-status" data-status="Succeeded">
        <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{t('replacement.completed', { count: copyCount })}</span>
      </p>
    )
  }
  const unexplained = downloadFailed || reason === null
  if (state === 'NeedsReview') {
    return (
      <p className="library-source-warning">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{unexplained ? t('replacement.needsReviewNoReason') : t('replacement.needsReview', { reason })}</span>
      </p>
    )
  }
  return (
    <p className="library-source-warning">
      <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{unexplained ? t('replacement.stoppedNoReason') : t('replacement.stopped', { reason })}</span>
    </p>
  )
}
