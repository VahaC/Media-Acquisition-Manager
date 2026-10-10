import { CircleCheck, Clock, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { isReplacementBeingChecked } from '@/lib/download-jobs'
import { formatNumber } from '@/lib/format'
import type { DownloadJobReplacement, ReplacementJobState } from '@/lib/types'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the line and its icon; [data-status='Succeeded'] greens the icon

/** The states a replacement waits in for its download: it is kept until the new copy is verified. */
const SCHEDULED_STATES: ReadonlySet<ReplacementJobState> = new Set<ReplacementJobState>(['Scheduled', 'Submitted', 'Downloading', 'Paused'])

/**
 * MAM-24 — the replacement a download job carries, as one short line (Vasyl's decision of
 * 2026-10-09): scheduled while its download goes on, or stopped, with the server's reason. MAM-25 —
 * once the download is done, its new file being checked, then verified (green, like a downloaded
 * job) — this version moves nothing, MAM-26 does —, or in need of review with the server's reason.
 * While it is scheduled, checked or verified the line also says what won't move: everything, while
 * replacement is switched off (ADR 0033: the checks still run), or the copies whose folder is no
 * longer ticked. A replacement that stopped (needs review, failed or cancelled) goes no further until
 * the owner acts — one in review still holds its copies —, so it says none of that.
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
  const { state, copyCount, reason, switchedOff, copiesOutsideTickedFolders } = replacement
  const outside = copiesOutsideTickedFolders === 0
    ? null
    : t('replacement.outsideTickedFolders', { count: copiesOutsideTickedFolders, value: formatNumber(copiesOutsideTickedFolders) })
  /** A held state's sentence, then the copies that won't move, if any. */
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
    return (
      <p className="library-source-status" data-status="Succeeded">
        <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{held(switchedOff ? t('replacement.verifiedSwitchedOff') : t('replacement.verified', counted))}</span>
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
