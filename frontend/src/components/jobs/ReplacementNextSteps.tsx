import { CircleCheck, Clock, TriangleAlert, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { formatDateTime, formatNumber } from '@/lib/format'
import { anyCopyDroppable, dropLeavesOthers, workName } from '@/lib/replacement-details'
import type { ReplacementDetails } from '@/lib/types'
import '@/styles/library.css' // .library-source-*, .library-link — the lines, their tones, the link to the Library
import '@/styles/jobs.css' // .jobs-status — the lines as one block

/** A time of day the check waits until or started at: the wait is an hour, so the date goes without saying. */
const TIME_FORMAT: Intl.DateTimeFormatOptions = { timeStyle: 'short' }

/** A how-far line: its icon, its tone and its sentence. */
interface ProgressLine {
  icon: LucideIcon
  tone: 'waiting' | 'done' | 'stopped'
  text: string
}

/**
 * MAM-27 — where a replacement stands and what the owner can do about it, above its copies on the
 * job's page.
 *
 * What you can do, when it waits for him: by who stopped it — the app can't follow the download
 * (Keep both, or Cancel job), the check of the new files (fix it in Jellyfin or the download client,
 * then Retry, with a link to the Library page for a film, whose TMDb link is set there), or the move
 * (remove what holds the copy, then Retry; Restore when part of it is in the quarantine already —
 * MAM-30: a torrent in whichever download client lists it, as the server's reason names it) —,
 * or that a copy only dropping it settles is still in it, which holds Retry back. Drop is named only
 * while the server offers it on some copy: never on the last copy left, where Keep both is the way
 * out; and when every copy left is one only dropping settles, only keeping both does. And while
 * replacement is switched off, that nothing moves until it is on, unless he keeps both.
 *
 * How far it got: waiting for the download, the check about to start, reading the new files (since
 * when), waiting for Jellyfin to identify them (until when), every copy paired, or the check stopped.
 * Times are the browser's local time.
 */
export function ReplacementNextSteps({ details }: { details: ReplacementDetails }) {
  const { t } = useTranslation('jobs')
  const { stop } = details

  const progress = ((): ProgressLine | null => {
    switch (details.state) {
      case 'Scheduled':
      case 'Submitted':
      case 'Downloading':
      case 'Paused':
        return { icon: Clock, tone: 'waiting', text: t('oldCopies.check.waitsDownload') }
      case 'Downloaded':
        return { icon: Clock, tone: 'waiting', text: t('oldCopies.check.starting') }
      case 'Inspecting':
        if (details.checkStage === 'Library' && details.checkWaitsUntilUtc !== null) {
          return { icon: Clock, tone: 'waiting', text: t('oldCopies.check.libraryWait', { time: formatDateTime(details.checkWaitsUntilUtc, TIME_FORMAT) }) }
        }
        return details.checkStartedUtc === null
          ? { icon: Clock, tone: 'waiting', text: t('oldCopies.check.starting') }
          : { icon: Clock, tone: 'waiting', text: t('oldCopies.check.files', { time: formatDateTime(details.checkStartedUtc, TIME_FORMAT) }) }
      case 'NeedsReview':
        // A stop before the move came after a check that paired every copy; a stop of the download
        // came before any check, and what to do says the rest.
        if (stop === 'Move') return { icon: CircleCheck, tone: 'done', text: t('oldCopies.check.verified') }
        return stop === 'Check' ? { icon: TriangleAlert, tone: 'stopped', text: t('oldCopies.check.stopped') } : null
      case 'Failed':
        // Only a check fails a replacement on its new file; one whose download failed was never checked.
        return details.checkStartedUtc === null ? null : { icon: TriangleAlert, tone: 'stopped', text: t('oldCopies.check.stopped') }
      case 'Cancelled':
        return null
      case 'Verified':
      case 'Quarantining':
      case 'Quarantined':
      case 'Restoring':
      case 'Restored':
      case 'Deleting':
      case 'Completed':
        return { icon: CircleCheck, tone: 'done', text: t('oldCopies.check.verified') }
    }
  })()

  const advice = (() => {
    if (details.state !== 'NeedsReview' || stop === null) return null
    if (stop === 'Download') return t('oldCopies.next.Download')
    // Retry can't help while such a copy is in it (the owner's decision of 2026-10-11): drop it first —
    // unless every copy left is one, as the last is never dropped: only keeping both settles it then.
    if (details.dropOnlyCopyCount > 0) {
      const counted = { count: details.dropOnlyCopyCount, value: formatNumber(details.dropOnlyCopyCount) }
      return dropLeavesOthers(details) ? t('oldCopies.next.dropOnly', counted) : t('oldCopies.next.dropOnlyKeepBoth', counted)
    }
    // Drop is offered only while another copy stays in the replacement: a single film's advice leaves
    // it out rather than point at the disabled button below.
    const droppable = anyCopyDroppable(details)
    if (stop === 'Check') return droppable ? t('oldCopies.next.Check') : t('oldCopies.next.CheckNoDrop')
    // Keep both is refused once part of it is in the quarantine; Restore is the way back then.
    if (details.canRestore) return droppable ? t('oldCopies.next.MoveRestore') : t('oldCopies.next.MoveRestoreNoDrop')
    return droppable ? t('oldCopies.next.Move') : t('oldCopies.next.MoveNoDrop')
  })()

  // A film Jellyfin gave no id is fixed by its TMDb link on the Library page, searched by its title:
  // a link to its card could go dead after a library sync.
  const films = stop === 'Check'
    ? details.works.filter((work) => work.kind === 'Movie' && work.title !== null)
    : []
  // Keep both is the only way to end it without switching replacement on, so the line says so only
  // while Keep both is offered.
  const switchedOff = details.switchedOff && details.canCancel

  if (progress === null && advice === null && !switchedOff) return null
  return (
    <div className="jobs-status">
      {advice !== null && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>
            {advice}
            {films.map((work) => (
              <span key={work.workId}>
                {' '}
                <Link className="library-link" to={`/library?${new URLSearchParams({ search: work.title ?? '' })}`}>
                  {t('oldCopies.next.openInLibrary', { title: workName(work) })}
                </Link>
              </span>
            ))}
          </span>
        </p>
      )}
      {switchedOff && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{t('oldCopies.next.switchedOff')}</span>
        </p>
      )}
      {progress !== null && <ProgressLineView line={progress} />}
    </div>
  )
}

/** One how-far line: waiting and done in the status tone (done with the green tick), stopped as a warning. */
function ProgressLineView({ line }: { line: ProgressLine }) {
  const Icon = line.icon
  return (
    <p
      className={line.tone === 'stopped' ? 'library-source-warning' : 'library-source-status'}
      data-status={line.tone === 'done' ? 'Succeeded' : undefined}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{line.text}</span>
    </p>
  )
}
