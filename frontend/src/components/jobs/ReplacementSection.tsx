import { useId } from 'react'
import { Archive, CircleAlert, Clock, Copy, Loader2, RotateCw, Trash2, TriangleAlert, Undo2, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ReplacementCopies } from '@/components/jobs/ReplacementCopies'
import { ReplacementNewFiles } from '@/components/jobs/ReplacementNewFiles'
import { ReplacementNextSteps } from '@/components/jobs/ReplacementNextSteps'
import { useDropCopy } from '@/components/jobs/use-drop-copy'
import { useKeepBoth } from '@/components/jobs/use-keep-both'
import { ActionButton } from '@/components/shared/ActionButton'
import { Banner } from '@/components/shared/Banner'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { useAuthStore } from '@/lib/auth-store'
import { isReplacementBeingChecked } from '@/lib/download-jobs'
import { formatFileSize, formatNumber } from '@/lib/format'
import { dropLeavesOthers } from '@/lib/replacement-details'
import {
  JOBS_POLL_MS,
  useDeleteReplacementForGood,
  useReplacement,
  useRestoreReplacement,
  useRetryReplacement,
} from '@/lib/queries'
import type {
  DownloadJob,
  DownloadJobReplacement,
  ReplacementDetails,
  ReplacementJobState,
  ReplacementName,
  ReplacementNameState,
} from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { cn, getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the heading row, the actions, the table and its states
import '@/styles/library.css' // .library-source-status, .library-source-warning — where each name is, why one stayed
import '@/styles/search.css' // .search-details-heading — the heading of the files the move planned
import '@/styles/jobs.css' // .jobs-* — the heading, the paths that break anywhere, the sizes that don't, the confirmation's list

interface ReplacementSectionProps {
  /** The job the page shows: its release names Keep both's confirmation, its client the message. */
  job: DownloadJob
  /** The replacement that job carries. */
  replacement: DownloadJobReplacement
  /** An action was confirmed and is about to be sent: the page clears its last result and refusal. */
  onStart: () => void
  /** An action went through: the page shows `message` in an ok banner above the job. */
  onDone: (message: string) => void
  /** An action was refused or failed: the page shows `message` in its error banner. */
  onError: (message: string) => void
}

/** Each name state's icon; Moving, Restoring and Deleting spin, as work under way. */
const NAME_ICONS: Record<ReplacementNameState, LucideIcon> = {
  Planned: Clock,
  Moving: Loader2,
  Quarantined: Archive,
  Stayed: TriangleAlert,
  Restoring: Loader2,
  Restored: Undo2,
  Deleting: Loader2,
  Deleted: Trash2,
  Lost: TriangleAlert,
}

/** The name states worded as a warning: the name isn't where the replacement meant it to be. */
const WARNING_STATES: ReadonlySet<ReplacementNameState> = new Set<ReplacementNameState>(['Stayed', 'Lost'])

/** The name states that spin. */
const UNDER_WAY_STATES: ReadonlySet<ReplacementNameState> = new Set<ReplacementNameState>(['Moving', 'Restoring', 'Deleting'])

/**
 * The replacement states that still lead to the move: its download or the check of its new file
 * under way, or verified. Any other with no name stopped or ended before the move.
 */
const BEFORE_MOVE_STATES: ReadonlySet<ReplacementJobState> = new Set<ReplacementJobState>([
  'Scheduled', 'Submitted', 'Downloading', 'Paused', 'Downloaded', 'Inspecting', 'Verified',
])

/**
 * MAM-26 — the old copies of the replacement a job carries, on the job's page between its facts and
 * its new files (ADR 0030): every name the move planned, by position — the ticked video, then its
 * same-name companions —, each with where it was, its place in the quarantine under it, its size and
 * where it is now (the server's sentence for one that stayed, wasn't put back or deleted, or was
 * lost), and the room the replacement takes in the quarantine.
 *
 * The owner's actions sit beside the heading: Retry for one the mover stopped for review; Restore,
 * after a plain confirmation, whenever something of it is in the quarantine — a replacement in review
 * may hold earlier episodes already moved —; Delete for good once it is Quarantined, after a
 * confirmation that lists every name it deletes with its size and the room it frees, then sends
 * exactly those names. Each shows a spinner and "…ing" while its own request runs and holds the
 * others (one shared busy flag). Restore and Delete for good are disabled with the reason while
 * replacement is switched off, as the server refuses them then; Retry isn't, as it writes nothing to
 * the media, and a replacement retried while off waits, verified, until it is on.
 *
 * Only the owner's (ADR 0033): the route is owner-only, so a member is told so and nothing is asked.
 * The details are asked for again whenever the job comes back with its replacement changed
 * (useDownloadJob) and after each action, on no timer of their own but while the check runs (below).
 *
 * MAM-27 — renamed Replacement, as it now holds the whole replacement (the owner's decisions of
 * 2026-10-11): what he can do and how far the check got (ReplacementNextSteps), the ticked copies
 * with their pairs and Drop (ReplacementCopies), what the check found of each new file
 * (ReplacementNewFiles), then MAM-26's names under Files the move planned. The actions row reads Retry
 * · Keep both · Restore · Delete for good, under the one busy flag Drop shares. Retry now also sends a
 * stopped check back to the check, and shows disabled — with the reason as a line too — while a copy
 * only dropping it settles is still in it. Keep both cancels only the replacement after the
 * confirmation the Jobs row asks (useKeepBoth, given the job the page shows), and stays usable while
 * replacement is off, as Drop and Retry do: none of them writes to the media. While the check runs,
 * what it found changes on every pass with nothing on the job's line changing, so the details are
 * asked for again at the Jobs cadence for as long as it runs.
 */
export function ReplacementSection({ job, replacement, onStart, onDone, onError }: ReplacementSectionProps) {
  const { t } = useTranslation(['jobs', 'common'])
  const titleId = useId()
  const confirm = useConfirm()
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const details = useReplacement(replacement.id, isOwner, isReplacementBeingChecked(replacement.state) ? JOBS_POLL_MS : false)
  const { data, error, isLoading } = details
  const load = useLoadRetry(details)
  const retry = useRetryReplacement()
  const keepBoth = useKeepBoth({ onStart, onDone, onError })
  const restore = useRestoreReplacement()
  const remove = useDeleteReplacementForGood()
  const dropCopy = useDropCopy({ onStart, onDone, onError })
  const busy = retry.isPending || keepBoth.pending || restore.isPending || remove.isPending || dropCopy.droppingCopyId !== null
  // Restore and Delete for good only: the server refuses them while replacement is off.
  const switchedOffReason = data?.switchedOff ? t('oldCopies.switchedOff') : undefined
  // A new check can't pair a copy only dropping it settles, so Retry would stop again: shown, but
  // disabled with what to do first (the owner's decision of 2026-10-11) — drop them, or, when every
  // copy left is one and the last is never dropped, keep both.
  const retryReason = data !== undefined && data.stop === 'Check' && data.dropOnlyCopyCount > 0
    ? dropLeavesOthers(data) ? t('oldCopies.retry.blockedByDrop') : t('oldCopies.retry.blockedKeepBoth')
    : undefined

  const sendRetry = async (shown: ReplacementDetails) => {
    // The result says what the retry does, by who stopped the replacement as the owner saw it.
    const done = shown.stop === 'Check' ? t('oldCopies.retry.doneCheck') : t('oldCopies.retry.done')
    onStart()
    try {
      await retry.mutateAsync(shown.id)
      onDone(done)
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.retry.failed')))
    }
  }

  const sendRestore = async (shown: ReplacementDetails) => {
    // What the confirmation is about, as it stands when the owner clicks: the result counts these.
    const quarantined = quarantinedNames(shown)
    const copies = quarantined.filter((name) => name.kind === 'Video').length
    const confirmed = await confirm({
      title: t('oldCopies.restore.confirmTitle', { count: copies }),
      message: t('oldCopies.restore.message'),
      confirmLabel: t('oldCopies.restore.confirm'),
    })
    if (!confirmed) return
    onStart()
    try {
      await restore.mutateAsync(shown.id)
      onDone(t('oldCopies.restore.done', { count: quarantined.length, value: formatNumber(quarantined.length) }))
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.restore.failed')))
    }
  }

  const sendDelete = async (shown: ReplacementDetails) => {
    // The names listed are the names sent: a list that changed meanwhile is the server's 409 to tell,
    // never a reason to delete what the owner wasn't shown.
    const listed = quarantinedNames(shown)
    const copies = listed.filter((name) => name.kind === 'Video').length
    const confirmed = await confirm({
      title: t('oldCopies.deleteForGood.confirmTitle', { count: copies }),
      message: <DeleteForGoodMessage names={listed} freesBytes={shown.freesBytes} />,
      confirmLabel: t('oldCopies.deleteForGood.confirm'),
      destructive: true,
    })
    if (!confirmed) return
    onStart()
    try {
      await remove.mutateAsync({ id: shown.id, names: listed.map((name) => name.id) })
      onDone(t('oldCopies.deleteForGood.done', { count: listed.length, value: formatNumber(listed.length) }))
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.deleteForGood.failed')))
    }
  }

  const body = (() => {
    if (!isOwner) return <p className="settings-muted">{t('oldCopies.notOwner')}</p>
    if (load.error) {
      return (
        <ErrorState
          title={t('oldCopies.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      )
    }
    if (isLoading) return <p className="settings-muted">{t('common:loading')}</p>
    if (!data) return null
    return (
      <>
        <ReplacementNextSteps details={data} />
        <ReplacementCopies
          details={data}
          busy={busy}
          droppingCopyId={dropCopy.droppingCopyId}
          onDrop={(copy) => void dropCopy.drop(data, copy)}
        />
        <ReplacementNewFiles details={data} />
        <OldCopies details={data} />
      </>
    )
  })()

  return (
    <section className="jobs-section" aria-labelledby={titleId}>
      <div className="settings-header">
        <h2 id={titleId} className="jobs-section-title">{t('oldCopies.title')}</h2>
        {isOwner && data && (data.canRetry || retryReason !== undefined || data.canCancel || data.canRestore || data.canDeleteForGood) && (
          <div className="settings-actions">
            {(data.canRetry || retryReason !== undefined) && (
              <ActionButton
                icon={RotateCw}
                label={t('oldCopies.retry.action')}
                pendingLabel={t('oldCopies.retry.pending')}
                title={data.stop === 'Check' ? t('oldCopies.retry.titleCheck') : t('oldCopies.retry.title')}
                pending={retry.isPending}
                disabled={busy}
                reason={retryReason}
                onClick={() => void sendRetry(data)}
              />
            )}
            {/* Not red: nothing is deleted anywhere, both copies stay. */}
            {data.canCancel && (
              <ActionButton
                icon={Copy}
                label={t('oldCopies.keepBoth.action')}
                pendingLabel={t('oldCopies.keepBoth.pending')}
                title={t('oldCopies.keepBoth.title')}
                pending={keepBoth.pending}
                disabled={busy}
                reason={undefined}
                onClick={() => void keepBoth.keepBoth(job, replacement)}
              />
            )}
            {data.canRestore && (
              <ActionButton
                icon={Undo2}
                label={t('oldCopies.restore.action')}
                pendingLabel={t('oldCopies.restore.pending')}
                title={t('oldCopies.restore.title')}
                pending={restore.isPending}
                disabled={busy}
                reason={switchedOffReason}
                onClick={() => void sendRestore(data)}
              />
            )}
            {/* A red outline in the row; the filled red is the confirmation's own button. */}
            {data.canDeleteForGood && (
              <ActionButton
                icon={Trash2}
                label={t('oldCopies.deleteForGood.action')}
                pendingLabel={t('oldCopies.deleteForGood.pending')}
                title={t('oldCopies.deleteForGood.title')}
                pending={remove.isPending}
                disabled={busy}
                reason={switchedOffReason}
                className="ui-button-danger"
                onClick={() => void sendDelete(data)}
              />
            )}
          </div>
        )}
      </div>
      {/* Above the names, which stay on screen as last loaded. */}
      {isOwner && error && data && (
        <Banner action={<RetryButton onRetry={() => details.refetch()} />}>
          {t('oldCopies.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}
      {/* The reasons as lines of their own too, not only on hover: a phone has none. */}
      {switchedOffReason !== undefined && (data?.canRestore || data?.canDeleteForGood) && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{switchedOffReason}</span>
        </p>
      )}
      {isOwner && retryReason !== undefined && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{retryReason}</span>
        </p>
      )}
      {body}
    </section>
  )
}

/** The names the replacement holds in the quarantine now: what Restore puts back and Delete for good deletes. */
function quarantinedNames(details: ReplacementDetails): ReplacementName[] {
  return details.names.filter((name) => name.state === 'Quarantined')
}

// ── the names ──

/**
 * The room the replacement takes in the quarantine, then every name its move planned. MAM-27 — under
 * a heading of their own, below the copies and the new files, once the move planned any.
 */
function OldCopies({ details }: { details: ReplacementDetails }) {
  const { t } = useTranslation('jobs')
  const titleId = useId()
  const quarantined = quarantinedNames(details)

  if (details.names.length === 0) {
    // A move is promised only while one can still come: a replacement cancelled, failed or stopped
    // for review before its move says nothing moved — Retry, when offered, says the rest.
    const sentence = !BEFORE_MOVE_STATES.has(details.state)
      ? t('oldCopies.noneMoved')
      : details.switchedOff ? t('oldCopies.noneYetSwitchedOff') : t('oldCopies.noneYet')
    return <p className="settings-muted">{sentence}</p>
  }
  return (
    <section className="jobs-section" aria-labelledby={titleId}>
      <h3 id={titleId} className="search-details-heading">{t('oldCopies.moved.title')}</h3>
      <p>
        {quarantined.length === 0
          ? t('oldCopies.roomEmpty')
          : t('oldCopies.room', {
            count: quarantined.length,
            value: formatNumber(quarantined.length),
            size: formatFileSize(details.quarantinedBytes),
          })}
      </p>
      <div className="settings-table-wrap">
        <table className="settings-table">
          <thead>
            <tr>
              <th scope="col">{t('oldCopies.columns.path')}</th>
              <th scope="col">{t('oldCopies.columns.size')}</th>
              <th scope="col">{t('oldCopies.columns.where')}</th>
            </tr>
          </thead>
          <tbody>
            {details.names.map((name) => <NameRow key={name.id} name={name} />)}
          </tbody>
        </table>
      </div>
    </section>
  )
}

/**
 * One name: a table row on a wide screen, a card on a phone. Where it was, with its place in the
 * quarantine under it — where it went, or would go —, its size, and where it is now; the server's
 * sentence under that when it stayed, wasn't put back or deleted, or was lost.
 */
function NameRow({ name }: { name: ReplacementName }) {
  const { t } = useTranslation('jobs')
  const Icon = NAME_ICONS[name.state]
  const warning = WARNING_STATES.has(name.state)

  return (
    <tr>
      <td data-label={t('oldCopies.columns.path')} className="jobs-stacked-cell">
        <span className="jobs-name-paths">
          <span className="jobs-file-path">{name.originalPath}</span>
          <span className="ui-hint">
            {t('oldCopies.quarantinePath')}{' '}
            <span className="jobs-file-path">{name.quarantinePath}</span>
          </span>
        </span>
      </td>
      <td data-label={t('oldCopies.columns.size')} className="jobs-size">{formatFileSize(name.sizeBytes)}</td>
      <td data-label={t('oldCopies.columns.where')} className="jobs-stacked-cell">
        <div className="jobs-status">
          <p className={warning ? 'library-source-warning' : 'library-source-status'}>
            <Icon className={cn('h-3.5 w-3.5', UNDER_WAY_STATES.has(name.state) && 'animate-spin')} aria-hidden="true" />
            <span>{t(`oldCopies.state.${name.state}`)}</span>
          </p>
          {name.problem !== null && (
            <p className="library-source-warning">
              <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{name.problem}</span>
            </p>
          )}
          {name.otherNames && <p className="ui-hint">{t('oldCopies.otherNames')}</p>}
        </div>
      </td>
    </tr>
  )
}

// ── Delete for good's confirmation ──

/**
 * Every name Delete for good deletes, where it was and its size, then the room it frees as the
 * server worked it out — a name with another name (a hardlink) frees nothing, and the message says
 * how many have one. The list is left-aligned and its paths break anywhere: the dialog is narrow,
 * and centres its text on a phone.
 */
function DeleteForGoodMessage({ names, freesBytes }: { names: ReplacementName[]; freesBytes: number }) {
  const { t } = useTranslation('jobs')
  const otherNames = names.filter((name) => name.otherNames).length

  return (
    <>
      <p>{t('oldCopies.deleteForGood.lead', { count: names.length, value: formatNumber(names.length) })}</p>
      <ul className="jobs-quarantine-list">
        {names.map((name) => (
          <li key={name.id}>
            <span className="jobs-file-path">{name.originalPath}</span>
            <span className="jobs-quarantine-size">{formatFileSize(name.sizeBytes)}</span>
          </li>
        ))}
      </ul>
      <p>{t('oldCopies.deleteForGood.frees', { size: formatFileSize(freesBytes) })}</p>
      {otherNames > 0 && (
        <p>{t('oldCopies.deleteForGood.otherNames', { count: otherNames, value: formatNumber(otherNames) })}</p>
      )}
    </>
  )
}
