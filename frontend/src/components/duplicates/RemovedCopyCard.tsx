import { useId } from 'react'
import { isAxiosError } from 'axios'
import { Archive, CircleAlert, Loader2, Trash2, TriangleAlert, Undo2, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActionButton } from '@/components/shared/ActionButton'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { DeleteForGoodMessage } from '@/components/shared/DeleteForGoodMessage'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { FileSizeList } from '@/components/shared/FileSizeList'
import { QuarantineNameRow } from '@/components/shared/QuarantineNameRow'
import { formatDateTime, formatFileSize, formatNumber } from '@/lib/format'
import { splitPath } from '@/lib/library-versions'
import { useDeleteCopyRemovalForGood, useRestoreCopyRemoval } from '@/lib/queries'
import type { CopyRemoval, CopyRemovalState, ReplacementName } from '@/lib/types'
import { cn, getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-table, -wrap — the table of its files
import '@/styles/library.css' // .library-work-meta, .library-source-status, -warning — the file's name, its state, the reason
import '@/styles/duplicates.css' // .duplicates-* — the card, its head, actions and facts, the paths that break anywhere

interface RemovedCopyCardProps {
  removal: CopyRemoval
  /**
   * Replacement is switched off on the Media folders page: the server refuses Restore and Delete for good then,
   * so both are held with the reason. The tab says it once, with the way to that page.
   */
  switchedOff: boolean
  /** Why this removal's last Restore or Delete for good failed, kept by the tab; null when it didn't. */
  error: string | null
  /** Restore or Delete for good was confirmed and is about to be sent: the tab clears its last result. */
  onStart: () => void
  /** It went through: the tab shows `message` above the list, which no longer has this removal. */
  onDone: (message: string) => void
  /**
   * It failed: the tab keeps `message`, shown on this card while the list has it and above the list once the
   * list, asked for again, no longer does — a removal that ended meanwhile would otherwise leave with no word.
   */
  onFailed: (message: string) => void
}

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/** Each state's icon; Moving, Restoring and Deleting spin, as work under way. */
const STATE_ICONS: Record<CopyRemovalState, LucideIcon> = {
  Moving: Loader2,
  Quarantined: Archive,
  NeedsReview: TriangleAlert,
  Restoring: Loader2,
  Restored: Undo2,
  Deleting: Loader2,
  Deleted: Trash2,
  NotMoved: TriangleAlert,
}

/** The states worded as a warning: something of it isn't where the removal meant it to be. */
const WARNING_STATES: ReadonlySet<CopyRemovalState> = new Set<CopyRemovalState>(['NeedsReview', 'NotMoved'])

/** The states that spin. */
const UNDER_WAY_STATES: ReadonlySet<CopyRemovalState> = new Set<CopyRemovalState>(['Moving', 'Restoring', 'Deleting'])

/**
 * MAM-40 — one copy the owner removed on the Duplicates page, on its Removed copies tab: headed by its
 * group's title as it was when removed, with the file's name under it; where it was, where it is in the
 * quarantine, the room it takes, when and by whom it was removed, its state in plain words and the server's
 * sentence when it needs review; then every name that went with it (the video and its same-name companions),
 * each with where it is now, as a replacement's old copies on its job's page (QuarantineNameRow).
 *
 * Restore puts it back after a plain confirmation listing what goes back; Delete for good asks once more,
 * naming every path with its size and the room it frees, as for a replacement's old copies
 * (DeleteForGoodMessage). Each is disabled with the reason when the server says it can't be done now, shows a
 * spinner and "…ing" while its own request runs, and holds the other meanwhile. A refusal is the server's
 * sentence, inline on the card — kept by the tab, which shows it above the list instead once the card is gone.
 * While replacement is switched off both are held with that reason, as the server refuses them then (Phase C's
 * old copies do the same).
 */
export function RemovedCopyCard({ removal, switchedOff, error, onStart, onDone, onFailed }: RemovedCopyCardProps) {
  const { t } = useTranslation(['duplicates', 'jobs'])
  const titleId = useId()
  const confirm = useConfirm()
  const restore = useRestoreCopyRemoval()
  const deleteForGood = useDeleteCopyRemovalForGood()
  const busy = restore.isPending || deleteForGood.isPending
  const name = splitPath(removal.originalPath).name
  const StateIcon = STATE_ICONS[removal.state]
  // What the removal's own state rules out first: switching replacement on wouldn't change that.
  const switchedOffReason = switchedOff ? t('removed.switchedOff') : undefined
  const restoreReason = removal.canRestore ? switchedOffReason : t('removed.unavailable')
  const deleteReason = removal.canDeleteForGood ? switchedOffReason : t('removed.unavailable')

  /** A bare 404 is a removal that ended meanwhile: the list, asked for again, no longer has it. */
  const failure = (e: unknown, fallback: string) =>
    isAxiosError(e) && e.response?.status === 404 ? t('removed.gone') : getApiErrorMessage(e, fallback)

  const sendRestore = async () => {
    // What the confirmation lists, as it stands when the owner clicks.
    const names = quarantinedNames(removal)
    const confirmed = await confirm({
      title: t('removed.restore.confirmTitle', { name }),
      message: <RestoreMessage names={names} />,
      confirmLabel: t('removed.restore.confirm'),
    })
    if (!confirmed) return
    onStart()
    try {
      await restore.mutateAsync(removal.id)
      onDone(t('removed.restore.done', { name }))
    } catch (e: unknown) {
      onFailed(failure(e, t('removed.restore.failed')))
    }
  }

  const sendDelete = async () => {
    const confirmed = await confirm({
      title: t('removed.deleteForGood.confirmTitle', { name }),
      message: <DeleteForGoodMessage names={quarantinedNames(removal)} freesBytes={removal.freesBytes} />,
      confirmLabel: t('removed.deleteForGood.confirm'),
      destructive: true,
      // MAM-41 — every Delete for good asks in the wide confirmation: its message lists paths.
      wide: true,
    })
    if (!confirmed) return
    onStart()
    try {
      await deleteForGood.mutateAsync(removal.id)
      onDone(t('removed.deleteForGood.done', { name }))
    } catch (e: unknown) {
      onFailed(failure(e, t('removed.deleteForGood.failed')))
    }
  }

  return (
    <article className="ui-card duplicates-group" aria-labelledby={titleId}>
      <div className="duplicates-group-header">
        <div className="duplicates-group-heading">
          <h2 id={titleId} className="ui-card-title duplicates-group-title">{removal.title}</h2>
          <p className="library-work-meta"><span className="duplicates-path">{name}</span></p>
        </div>
        <div className="duplicates-group-side">
          <div className="duplicates-actions" role="group" aria-label={t('removed.actionsLabel')}>
            <ActionButton
              icon={Undo2}
              label={t('removed.restore.action')}
              pendingLabel={t('removed.restore.pending')}
              title={t('removed.restore.title')}
              pending={restore.isPending}
              disabled={busy}
              reason={restoreReason}
              onClick={() => void sendRestore()}
            />
            {/* A red outline in the row; the filled red is the confirmation's own button. */}
            <ActionButton
              icon={Trash2}
              label={t('removed.deleteForGood.action')}
              pendingLabel={t('removed.deleteForGood.pending')}
              title={t('removed.deleteForGood.title')}
              pending={deleteForGood.isPending}
              disabled={busy}
              reason={deleteReason}
              className="ui-button-danger"
              onClick={() => void sendDelete()}
            />
          </div>
        </div>
      </div>

      <dl className="duplicates-facts">
        <div>
          <dt>{t('removed.facts.originalPath')}</dt>
          <dd><span className="duplicates-path">{removal.originalPath}</span></dd>
        </div>
        <div>
          <dt>{t('removed.facts.quarantinePath')}</dt>
          <dd><span className="duplicates-path">{removal.quarantinePath}</span></dd>
        </div>
        <div>
          <dt>{t('removed.facts.size')}</dt>
          <dd>{formatFileSize(removal.sizeBytes)}</dd>
        </div>
        <div>
          <dt>{t('removed.facts.removed')}</dt>
          <dd>
            {t('removed.removedBy', { date: formatDateTime(removal.createdUtc, INSTANT_FORMAT), name: removal.requestedByUserName })}
          </dd>
        </div>
        <div>
          <dt>{t('removed.facts.state')}</dt>
          <dd>
            <span className={WARNING_STATES.has(removal.state) ? 'library-source-warning' : 'library-source-status'}>
              <StateIcon className={cn('h-3.5 w-3.5', UNDER_WAY_STATES.has(removal.state) && 'animate-spin')} aria-hidden="true" />
              <span>{t(`removed.state.${removal.state}`)}</span>
            </span>
          </dd>
        </div>
      </dl>

      {removal.reason !== null && (
        <p className="library-source-warning">
          <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{removal.reason}</span>
        </p>
      )}

      {/* The reasons as a line too, not only on hover: a phone has none. */}
      {(!removal.canRestore || !removal.canDeleteForGood) && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{t('removed.unavailable')}</span>
        </p>
      )}

      {error && <FieldErrorLine>{error}</FieldErrorLine>}

      <div className="settings-table-wrap">
        <table className="settings-table" aria-label={t('removed.files')}>
          <thead>
            <tr>
              <th scope="col">{t('removed.columns.path')}</th>
              <th scope="col">{t('jobs:oldCopies.columns.size')}</th>
              <th scope="col">{t('jobs:oldCopies.columns.where')}</th>
            </tr>
          </thead>
          <tbody>
            {removal.names.map((entry) => <QuarantineNameRow key={entry.id} name={entry} pathLabel={t('removed.columns.path')} />)}
          </tbody>
        </table>
      </div>
    </article>
  )
}

/** The names the removal holds in the quarantine now: what Restore puts back and Delete for good deletes. */
function quarantinedNames(removal: CopyRemoval): ReplacementName[] {
  return removal.names.filter((name) => name.state === 'Quarantined')
}

// ── Restore's confirmation ──

/** Every name Restore puts back, where it goes and its size, listed as Delete for good's are. */
function RestoreMessage({ names }: { names: ReplacementName[] }) {
  const { t } = useTranslation('duplicates')

  return (
    <>
      <p>{t('removed.restore.message', { count: names.length, value: formatNumber(names.length) })}</p>
      <FileSizeList files={names.map((name) => ({ id: name.id, path: name.originalPath, sizeBytes: name.sizeBytes }))} />
    </>
  )
}
