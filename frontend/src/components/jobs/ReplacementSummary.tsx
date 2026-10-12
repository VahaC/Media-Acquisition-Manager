import type { TFunction } from 'i18next'
import { Archive, CircleCheck, CircleSlash, CircleX, Clock, Loader2, TriangleAlert, Undo2, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatDateTime, formatNumber } from '@/lib/format'
import type { DownloadJobReplacement } from '@/lib/types'
import { cn } from '@/lib/utils'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the one line
import '@/styles/settings-list.css' // .settings-muted — the dash of a job with no replacement

/** Until when the check waits for Jellyfin: the time alone, as ReplacementLine says it. */
const WAIT_FORMAT: Intl.DateTimeFormatOptions = { timeStyle: 'short' }

/** The line's look: an amber warning, or library.css's status tones (green, red, plain). */
type Tone = 'warning' | 'Succeeded' | 'Failed' | undefined

interface Summary {
  icon: LucideIcon
  text: string
  tone: Tone
  /** Work under way, as a download's icon spins. */
  spinning?: boolean
}

/**
 * MAM-41 — the Replacement cell of an owner's Jobs row: where the replacement stands, in a few words on one
 * line — green once verified or replaced, amber while it waits for review, red once it failed, a spinner
 * while it renames. The whole sentence (the server's reason, what to do next, the copies dropped or no
 * longer in a ticked folder) is in the row's details (ReplacementLine), and the copies under it. A job
 * with no replacement shows a dash, which a screen reader reads as "No replacement".
 */
export function ReplacementSummary({ replacement }: { replacement: DownloadJobReplacement | null }) {
  const { t } = useTranslation('jobs')
  if (replacement === null) {
    return (
      <span className="settings-muted">
        <span aria-hidden="true">{t('summary.dash')}</span>
        <span className="sr-only">{t('summary.none')}</span>
      </span>
    )
  }

  const summary = summarize(replacement, t)
  const Icon = summary.icon
  return summary.tone === 'warning' ? (
    <p className="library-source-warning">
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{summary.text}</span>
    </p>
  ) : (
    <p className="library-source-status" data-status={summary.tone}>
      <Icon className={cn('h-3.5 w-3.5', summary.spinning && 'animate-spin')} aria-hidden="true" />
      <span>{summary.text}</span>
    </p>
  )
}

/**
 * The short line of each state. While replacement is switched off a replacement that hasn't moved
 * anything yet says only that, as ReplacementLine does: nothing moves until it is on. Counted by the
 * copies ticked, as ReplacementLine counts, but in the quarantine by the copies it holds there now.
 */
function summarize(replacement: DownloadJobReplacement, t: TFunction<'jobs'>): Summary {
  const { state, switchedOff, copyCount, quarantinedCopyCount, stop, checkStage, checkWaitsUntilUtc } = replacement
  const copies = { count: copyCount, value: formatNumber(copyCount) }
  switch (state) {
    case 'Scheduled':
    case 'Submitted':
    case 'Downloading':
    case 'Paused':
      return { icon: Clock, text: switchedOff ? t('summary.switchedOff') : t('summary.waiting', copies), tone: undefined }
    case 'Downloaded':
    case 'Inspecting':
      if (switchedOff) return { icon: Clock, text: t('summary.switchedOff'), tone: undefined }
      return checkStage === 'Library' && checkWaitsUntilUtc !== null
        ? { icon: Clock, text: t('summary.waitsForJellyfin', { time: formatDateTime(checkWaitsUntilUtc, WAIT_FORMAT) }), tone: undefined }
        : { icon: Clock, text: t('summary.checking'), tone: undefined }
    case 'Verified':
      return switchedOff
        ? { icon: Clock, text: t('summary.switchedOff'), tone: undefined }
        : { icon: CircleCheck, text: t('summary.verified'), tone: 'Succeeded' }
    case 'Quarantining':
      return { icon: Loader2, text: t('summary.quarantining'), tone: undefined, spinning: true }
    case 'Quarantined':
      return {
        icon: Archive,
        text: t('summary.quarantined', { count: quarantinedCopyCount, value: formatNumber(quarantinedCopyCount) }),
        tone: undefined,
      }
    case 'Restoring':
      return { icon: Loader2, text: t('summary.restoring'), tone: undefined, spinning: true }
    case 'Deleting':
      return { icon: Loader2, text: t('summary.deleting'), tone: undefined, spinning: true }
    case 'Restored':
      return { icon: Undo2, text: t('summary.restored', copies), tone: undefined }
    case 'Completed':
      return { icon: CircleCheck, text: t('summary.completed', copies), tone: 'Succeeded' }
    case 'NeedsReview':
      return { icon: TriangleAlert, text: t(`summary.needsReview.${stop ?? 'None'}`), tone: 'warning' }
    case 'Failed':
      return { icon: CircleX, text: t('summary.stopped'), tone: 'Failed' }
    case 'Cancelled':
      return { icon: CircleSlash, text: t('summary.cancelled'), tone: undefined }
  }
}
