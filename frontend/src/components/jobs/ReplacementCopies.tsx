import { useId } from 'react'
import {
  Archive,
  CircleAlert,
  CircleCheck,
  Clock,
  ListMinus,
  Loader2,
  MapPin,
  Trash2,
  TriangleAlert,
  Undo2,
  type LucideIcon,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { NewFileFacts } from '@/components/jobs/NewFileFacts'
import { ActionButton } from '@/components/shared/ActionButton'
import { formatNumber } from '@/lib/format'
import { copyFacts } from '@/lib/replacement-details'
import type { ReplacementCopyStatus, ReplacementDetails, ReplacementJobCopy, ReplacementJobNewFile } from '@/lib/types'
import { cn } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the table that turns into cards, the Drop cell, the muted lines
import '@/styles/library.css' // .library-source-* — where each copy stands
import '@/styles/search.css' // .search-details-heading — the table's heading inside the Replacement section
import '@/styles/jobs.css' // .jobs-* — the paths that break anywhere, the stacked cells, the greyed dropped row

interface ReplacementCopiesProps {
  details: ReplacementDetails
  /** Another action of the replacement is under way: every Drop waits (one shared busy flag). */
  busy: boolean
  /** The copy whose drop is on its way, which shows the spinner; null while none is. */
  droppingCopyId: string | null
  /** The owner pressed Drop on `copy`: the caller confirms and sends it (useDropCopy). */
  onDrop: (copy: ReplacementJobCopy) => void
}

/** Each status's icon; Moving spins, as work under way. */
const STATUS_ICONS: Record<ReplacementCopyStatus, LucideIcon> = {
  Dropped: ListMinus,
  Waiting: Clock,
  Paired: CircleCheck,
  Stopped: TriangleAlert,
  NotPaired: TriangleAlert,
  Moving: Loader2,
  InQuarantine: Archive,
  Restored: Undo2,
  Deleted: Trash2,
  Lost: TriangleAlert,
  Stays: MapPin,
}

/** The statuses worded as a warning: the copy holds the replacement up, or isn't where it should be. */
const WARNING_STATUSES: ReadonlySet<ReplacementCopyStatus> = new Set<ReplacementCopyStatus>(['Stopped', 'NotPaired', 'Lost'])

/**
 * The statuses of a copy whose video's name left its folder (moving, in the quarantine, or deleted
 * from it): the server never lets one be dropped — Restore is the way back —, so its Drop isn't
 * explained as the last copy's.
 */
const LEFT_FOLDER_STATUSES: ReadonlySet<ReplacementCopyStatus> = new Set<ReplacementCopyStatus>(['Moving', 'InQuarantine', 'Deleted'])

/**
 * MAM-27 — every old copy the owner ticked for a replacement, by position, with the new file each is
 * paired with and where it stands: what the check found of it, the server's sentence when it stopped
 * the replacement, and whether only dropping it (or Keep both) settles that. A dropped copy stays in
 * the list, greyed, with its last problem as history. Shared by the job's page and the Jobs list's
 * Copies panel, which bring their own confirmation and banners (useDropCopy).
 *
 * Drop is the owner's per copy, offered as the server says (`canDrop`: the replacement stopped for
 * review, the copy is in it and none of its names left its folder). The last copy left is never
 * dropped: its Drop shows disabled, with the reason on hover and as a line of its own — a phone has no
 * hover —, as Keep both is the way to keep it. While the check decides the pairs, a line says nothing
 * moves until every copy is paired or dropped.
 *
 * The page never works a pair out itself: the pair, the status and the problem are the server's.
 * Nothing at all for a replacement that lists no copy.
 */
export function ReplacementCopies({ details, busy, droppingCopyId, onDrop }: ReplacementCopiesProps) {
  const { t } = useTranslation('jobs')
  const titleId = useId()
  if (details.copies.length === 0) return null

  const undropped = details.copies.filter((copy) => copy.droppedUtc === null)
  const paired = undropped.filter((copy) => copy.pairedNewFileId !== null).length
  const dropped = details.copies.length - undropped.length
  // The one copy whose Drop the server refuses only because it is the last one left in a stopped
  // replacement: Drop applies there and nowhere else (the owner's decision of 2026-10-11).
  const stopped = details.state === 'NeedsReview' && (details.stop === 'Check' || details.stop === 'Move')
  const lastCopy = stopped && undropped.length === 1 ? undropped[0] : undefined
  const isLastCopy = (copy: ReplacementJobCopy) =>
    copy === lastCopy && !copy.canDrop && !LEFT_FOLDER_STATUSES.has(copy.status)
  const dropShown = details.copies.some((copy) => copy.canDrop || isLastCopy(copy))
  // While the check runs, and once it stopped: what holds the move back is a copy not paired yet.
  const summaryShown = details.state === 'Inspecting' || details.stop === 'Check'

  return (
    <section className="jobs-section" aria-labelledby={titleId}>
      <h3 id={titleId} className="search-details-heading">{t('oldCopies.copies.title')}</h3>
      {summaryShown && (
        <p>
          {t('oldCopies.copies.summary', {
            count: undropped.length,
            value: formatNumber(undropped.length),
            paired: formatNumber(paired),
            dropped: formatNumber(dropped),
          })}
        </p>
      )}
      <div className="settings-table-wrap">
        <table className="settings-table">
          <thead>
            <tr>
              <th scope="col">{t('oldCopies.copies.columns.copy')}</th>
              <th scope="col">{t('oldCopies.copies.columns.newFile')}</th>
              <th scope="col">{t('oldCopies.copies.columns.where')}</th>
              {dropShown && <th scope="col">{t('oldCopies.copies.columns.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {details.copies.map((copy) => (
              <CopyRow
                key={copy.id}
                copy={copy}
                pair={copy.pairedNewFileId === null ? undefined : details.newFiles.find((file) => file.id === copy.pairedNewFileId)}
                dropShown={dropShown}
                lastCopy={isLastCopy(copy)}
                busy={busy}
                dropping={droppingCopyId === copy.id}
                onDrop={onDrop}
              />
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

// ── one copy ──

interface CopyRowProps {
  copy: ReplacementJobCopy
  /** The new file it is paired with; undefined while none is. */
  pair: ReplacementJobNewFile | undefined
  /** The table has a Drop column: some copy can be dropped, or is the last one left. */
  dropShown: boolean
  /** It is the stopped replacement's last copy: its Drop shows disabled, with the reason. */
  lastCopy: boolean
  busy: boolean
  /** Its own drop is on its way. */
  dropping: boolean
  onDrop: (copy: ReplacementJobCopy) => void
}

/**
 * One ticked copy: a table row on a wide screen, a card on a phone. Its path with what it was when
 * ticked under it; the new file it is paired with, or why there is none; and where it stands — the
 * status, the server's sentence when it stopped the replacement, and the lines that say what settles
 * it.
 */
function CopyRow({ copy, pair, dropShown, lastCopy, busy, dropping, onDrop }: CopyRowProps) {
  const { t } = useTranslation('jobs')
  const dropped = copy.droppedUtc !== null
  const Icon = STATUS_ICONS[copy.status]
  const warning = WARNING_STATUSES.has(copy.status)
  const lastCopyReason = lastCopy ? t('oldCopies.drop.lastCopy') : undefined

  return (
    <tr className={dropped ? 'jobs-copy-dropped' : undefined}>
      <td data-label={t('oldCopies.copies.columns.copy')} className="jobs-stacked-cell">
        <span className="jobs-name-paths">
          <span className="jobs-file-path">{copy.appPath}</span>
          <span className="ui-hint">{copyFacts(copy)}</span>
        </span>
      </td>
      <td data-label={t('oldCopies.copies.columns.newFile')} className="jobs-stacked-cell">
        {pair !== undefined ? (
          <NewFileFacts file={pair} withPath />
        ) : (
          // Before the check is over no pair is shown yet (it pairs once Jellyfin identified every
          // new file); after it, a copy without one is what the status line explains.
          <span className="settings-muted">
            {copy.status === 'Waiting' ? t('oldCopies.copies.pairedLater') : t('oldCopies.copies.notPaired')}
          </span>
        )}
      </td>
      <td data-label={t('oldCopies.copies.columns.where')} className="jobs-stacked-cell">
        <div className="jobs-status">
          <p
            className={warning ? 'library-source-warning' : 'library-source-status'}
            data-status={copy.status === 'Paired' ? 'Succeeded' : undefined}
          >
            <Icon className={cn('h-3.5 w-3.5', copy.status === 'Moving' && 'animate-spin')} aria-hidden="true" />
            <span>
              {t(`oldCopies.copies.status.${copy.status}`)}
              {/* "Stopped the replacement:" introduces the server's own sentence. */}
              {copy.status === 'Stopped' && copy.problem !== null && <>{' '}{copy.problem}</>}
            </span>
          </p>
          {copy.status !== 'Stopped' && copy.problem !== null && (dropped ? (
            // A dropped copy keeps its last problem as history: greyed, not a warning to act on.
            <p className="ui-hint">{copy.problem}</p>
          ) : (
            <p className="library-source-warning">
              <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{copy.problem}</span>
            </p>
          ))}
          {/* The last copy left is never dropped: its own line says keep both instead. */}
          {copy.onlyDropSettles && !dropped && !lastCopy && <p className="ui-hint">{t('oldCopies.copies.onlyDrop')}</p>}
          {/* The reason as a line of its own too, not only on hover: a phone has none. */}
          {lastCopyReason !== undefined && <p className="ui-hint">{lastCopyReason}</p>}
        </div>
      </td>
      {dropShown && (
        <td className="settings-actions-cell">
          {(copy.canDrop || lastCopy) && (
            <div className="settings-actions">
              <ActionButton
                icon={ListMinus}
                label={t('oldCopies.drop.action')}
                pendingLabel={t('oldCopies.drop.pending')}
                title={t('oldCopies.drop.title')}
                pending={dropping}
                disabled={busy}
                reason={lastCopyReason}
                onClick={() => onDrop(copy)}
              />
            </div>
          )}
        </td>
      )}
    </tr>
  )
}
