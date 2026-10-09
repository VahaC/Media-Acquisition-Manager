import { Clock, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatNumber } from '@/lib/format'
import type { DownloadJobReplacement, ReplacementJobState } from '@/lib/types'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the line and its icon

/** The states a replacement waits in for its download: it is kept until the new copy is verified. */
const SCHEDULED_STATES: ReadonlySet<ReplacementJobState> = new Set<ReplacementJobState>(['Scheduled', 'Submitted', 'Downloading', 'Paused'])

/**
 * MAM-24 — the replacement a download job carries, as one short line (Vasyl's decision of
 * 2026-10-09): scheduled while its download goes on, waiting once it is downloaded — this version
 * verifies and moves nothing (MAM-25 and MAM-26 do) —, or stopped, with the server's reason. While
 * it is scheduled the line also says what won't move: everything, while replacement is switched off
 * (ADR 0033), or the copies whose folder is no longer ticked.
 *
 * On a Failed download it only says it stopped: the job's red line already says why. No live region:
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

  if (SCHEDULED_STATES.has(state)) {
    const scheduled = switchedOff
      ? t('replacement.switchedOff')
      : t('replacement.scheduled', { count: copyCount, value: formatNumber(copyCount) })
    const outside = copiesOutsideTickedFolders === 0
      ? null
      : t('replacement.outsideTickedFolders', { count: copiesOutsideTickedFolders, value: formatNumber(copiesOutsideTickedFolders) })
    return (
      <p className="library-source-status">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{outside === null ? scheduled : `${scheduled} ${outside}`}</span>
      </p>
    )
  }
  if (state === 'Downloaded') {
    return (
      <p className="library-source-status">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{t('replacement.waiting', { count: copyCount, value: formatNumber(copyCount) })}</span>
      </p>
    )
  }
  return (
    <p className="library-source-warning">
      <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{downloadFailed || reason === null ? t('replacement.stoppedNoReason') : t('replacement.stopped', { reason })}</span>
    </p>
  )
}
