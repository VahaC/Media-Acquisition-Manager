import { useId, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { CircleAlert, CircleCheck, Loader2, RefreshCw, ScanSearch, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime, formatNumber } from '@/lib/format'
import { isSyncUnderWay } from '@/lib/library-runs'
import { qk, useLibrarySources, useStartFileInspection, useStartLibrarySync } from '@/lib/queries'
import type {
  FileInspectionOverview,
  FileInspectionRun,
  LibrarySource,
  LibrarySourceList,
  LibrarySyncRun,
  LibrarySyncStatus,
} from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-empty, -muted, -name-text — the states every list page shares
import '@/styles/library.css' // .library-source*, .library-inspection, .library-link — the rows, their status lines and actions

/** Date and time without the seconds, which only add noise to when a sync ran. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * A refused start, shown under the source it was for while that source's latest run is still the
 * one it was refused against: a 409 "already being synced" is stale news once that sync has ended.
 */
interface StartError {
  integrationId: string
  message: string
  /** The source's latest run when the start was refused; null when it had none. */
  runId: string | null
  runStatus: LibrarySyncStatus | null
}

/**
 * MAM-8 — the media libraries the Library page is read from: how each one's last sync went, polled
 * while one is under way, and Sync now. Everyone sees the panel; only the installation owner can
 * start a sync, so everyone else gets the button disabled with the reason. A failure's sentence and
 * a path mapping problem can name the service's address, so the API gives them to the owner only
 * and everyone else reads that the owner can see why.
 *
 * MAM-9 — each source also counts how far the app's look at its files has got, and a last row tells
 * how the inspection of all the files stands, with Inspect files for the owner. It is polled, like a
 * sync, while a run is under way.
 */
export function LibrarySyncPanel() {
  const { t } = useTranslation(['library', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const titleId = useId()
  const queryClient = useQueryClient()
  const sources = useLibrarySources()
  const { data, error, isLoading } = sources
  const load = useLoadRetry(sources)
  const start = useStartLibrarySync()
  const inspect = useStartFileInspection()
  const [startError, setStartError] = useState<StartError | null>(null)
  const [inspectError, setInspectError] = useState<string | null>(null)

  const startInspection = async () => {
    setInspectError(null)
    try {
      await inspect.mutateAsync()
    } catch (e: unknown) {
      // Mostly a 409 (no media folder is allowed any more), which the refetch the start ends with
      // has already shown on the row; the sentence says why under it.
      setInspectError(getApiErrorMessage(e, t('inspection.startFailed')))
    }
  }

  const startSync = async (source: LibrarySource) => {
    setStartError(null)
    try {
      await start.mutateAsync(source.integrationId)
    } catch (e: unknown) {
      // MAM-8 — read from the cache, not from `source`: the start refetches the sources before it
      // rejects, so the cache already shows the run that made it refuse (another sync under way).
      const run = queryClient.getQueryData<LibrarySourceList>(qk.library.sources)
        ?.items.find((item) => item.integrationId === source.integrationId)?.latestRun ?? null
      setStartError({
        integrationId: source.integrationId,
        message: getApiErrorMessage(e, t('sources.startFailed')),
        runId: run?.id ?? null,
        runStatus: run?.status ?? null,
      })
    }
  }

  /** The refusal to show under `source`, until its latest run moves on from the one refused against. */
  const startErrorOf = (source: LibrarySource) =>
    startError !== null
      && startError.integrationId === source.integrationId
      && startError.runId === (source.latestRun?.id ?? null)
      && startError.runStatus === (source.latestRun?.status ?? null)
      ? startError.message
      : null

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('sources.title')}</h2>
        <p className="ui-card-description">{t('sources.description')}</p>
      </div>
      <div className="ui-card-content library-sources">
        {/* Outside the ternary below, so a failed poll adds a line above the last known status
            instead of replacing it. */}
        {error && data && (
          <Banner action={<RetryButton onRetry={() => sources.refetch()} />}>
            {t('sources.refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}
        {load.error ? (
          <ErrorState title={t('sources.loadFailed')} onRetry={load.retry} retrying={load.retrying}>
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="settings-muted">{t('common:loading')}</p>
        ) : data && data.items.length === 0 ? (
          <div className="settings-empty">
            <p>{isOwner ? t('sources.emptyOwner') : t('sources.emptyMember')}</p>
            {isOwner && (
              <Button asChild variant="outline" size="sm">
                <Link to="/settings/integrations">{t('sources.openIntegrations')}</Link>
              </Button>
            )}
          </div>
        ) : data ? (
          <>
            <ul className="library-source-list">
              {data.items.map((source) => (
                <SourceRow
                  key={source.integrationId}
                  source={source}
                  isOwner={isOwner}
                  starting={start.isPending && start.variables === source.integrationId}
                  startError={startErrorOf(source)}
                  onSync={() => void startSync(source)}
                />
              ))}
            </ul>
            {/* Under the sources, as one run reads the files of them all. */}
            <InspectionRow
              overview={data.inspection}
              isOwner={isOwner}
              starting={inspect.isPending}
              startError={inspectError}
              onInspect={() => void startInspection()}
            />
          </>
        ) : null}
      </div>
    </section>
  )
}

// ── one source ──

interface SourceRowProps {
  source: LibrarySource
  isOwner: boolean
  /** Its Sync now request is on its way. */
  starting: boolean
  /** The API's refusal of its last Sync now. */
  startError: string | null
  onSync: () => void
}

/** A source's name and badges, its status lines and Sync now — beside them, or under them on a phone. */
function SourceRow({ source, isOwner, starting, startError, onSync }: SourceRowProps) {
  const { t } = useTranslation('library')
  const run = source.latestRun
  const underWay = isSyncUnderWay(run)
  // The first reason that applies; the API would refuse the start with the same one.
  const reason = !isOwner
    ? t('sources.reasons.notOwner')
    : !source.canReadLibrary
      ? t('sources.reasons.cannotRead', { name: source.name })
      : !source.isEnabled
        ? t('sources.reasons.disabled', { name: source.name })
        : underWay
          ? t('sources.reasons.underWay', { name: source.name })
          : undefined
  const label = starting ? t('sources.starting') : underWay ? t('sources.syncing') : t('sources.syncNow')
  const lastSucceeded = run?.status === 'Failed' ? source.lastSucceededRun : null

  return (
    <li className="library-source" data-disabled={!source.isEnabled || undefined}>
      <div className="library-source-body">
        <p className="library-source-name">
          <span className="settings-name-text">{source.name}</span>
          {source.adapterDisplayName && <span className="settings-muted">{source.adapterDisplayName}</span>}
          {!source.isEnabled && <span className="ui-badge ui-badge-outline">{t('sources.disabled')}</span>}
          {!source.canReadLibrary && <span className="ui-badge ui-badge-outline">{t('sources.cannotRead')}</span>}
        </p>
        <RunLine run={run} />
        {lastSucceeded && (
          <p className="settings-muted">
            {t('run.lastSucceeded', { time: formatDateTime(finishedAt(lastSucceeded), INSTANT_FORMAT) })}
          </p>
        )}
        <InspectionCounts source={source} />
        {source.filesWithoutAppPathCount > 0 && (
          <p className="library-source-warning">
            <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            {isOwner ? (
              <span>
                {t('paths.ownerWarning', {
                  count: source.filesWithoutAppPathCount,
                  value: formatNumber(source.filesWithoutAppPathCount),
                })}
                {/* The mapper's own sentence for the first such file, as the API sends it. */}
                {source.pathProblem && <>{' '}{source.pathProblem}</>}{' '}
                <Link to="/settings/integrations" className="library-link">{t('paths.checkMappings')}</Link>
              </span>
            ) : (
              <span>{t('paths.memberWarning')}</span>
            )}
          </p>
        )}
        {/* MAM-9 — the owner's sentence for the first file that couldn't be read, unless it is the
            path problem the warning above already quotes. */}
        {source.inspection.problem && source.inspection.problem !== source.pathProblem && (
          <p className="library-source-warning">
            <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{source.inspection.problem}</span>
          </p>
        )}
        {startError && <StartErrorLine message={startError} />}
      </div>
      <div className="library-source-actions">
        <DisabledReason reason={reason}>
          <Button
            type="button"
            variant="outline"
            size="sm"
            title={label}
            disabled={reason !== undefined || starting}
            onClick={onSync}
          >
            {starting || underWay
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className="label-text">{label}</span>
          </Button>
        </DisabledReason>
      </div>
    </li>
  )
}

/**
 * MAM-16 — a refused Sync now or Inspect files, on the row it was for, as a red status line like a
 * failed run's. `role="alert"`, not the run line's `status`: the refusal answers the click, and is
 * read out at once.
 */
function StartErrorLine({ message }: { message: string }) {
  return (
    <p role="alert" className="library-source-status" data-status="Failed">
      <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{message}</span>
    </p>
  )
}

/**
 * When a run ended; a finished run always has it, the request time stands in otherwise. A sync's or
 * (MAM-9) an inspection's.
 */
function finishedAt(run: Pick<LibrarySyncRun | FileInspectionRun, 'finishedUtc' | 'createdUtc'>): string {
  return run.finishedUtc ?? run.createdUtc
}

// ── the latest run ──

/**
 * The source's latest run in one line. `role="status"`, so a screen reader reads a change out —
 * "Syncing… 50 of 120 movies", then the outcome — without moving the focus.
 */
function RunLine({ run }: { run: LibrarySyncRun | null }) {
  const { t } = useTranslation('library')

  const text = (() => {
    if (run === null) return t('run.never')
    switch (run.status) {
      case 'Queued':
        return t('run.queued')
      case 'Running':
        return run.itemsTotal === null
          ? t('run.running')
          : t('run.runningProgress', {
            count: run.itemsTotal,
            read: formatNumber(run.itemsRead),
            total: formatNumber(run.itemsTotal),
          })
      case 'Succeeded': {
        // Movies read and works stored are counted apart: two copies of one movie are one work.
        const works = run.worksCount ?? 0
        const versions = run.versionsCount ?? 0
        const files = run.filesCount ?? 0
        return t('run.succeeded', {
          time: formatDateTime(finishedAt(run), INSTANT_FORMAT),
          movies: t('run.movies', { count: run.itemsRead, value: formatNumber(run.itemsRead) }),
          works: t('run.works', { count: works, value: formatNumber(works) }),
          versions: t('run.versions', { count: versions, value: formatNumber(versions) }),
          files: t('run.files', { count: files, value: formatNumber(files) }),
        })
      }
      case 'Failed':
        // Null for anyone but the owner: the sentence can name the service's address.
        return run.error === null
          ? t('run.failedHidden')
          : t('run.failed', { time: formatDateTime(finishedAt(run), INSTANT_FORMAT), error: run.error })
    }
  })()

  return (
    <p role="status" className="library-source-status" data-status={run?.status}>
      {run !== null && <RunIcon run={run} />}
      <span>{text}</span>
    </p>
  )
}

function RunIcon({ run }: { run: LibrarySyncRun }) {
  switch (run.status) {
    case 'Queued':
    case 'Running':
      return <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
    case 'Succeeded':
      return <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
    case 'Failed':
      return <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
  }
}

// ── the file inspection ──

/**
 * MAM-9 — a source's files by what the app's last look at each found, the parts that aren't zero
 * only: "Files: 650 inspected · 3 couldn't be read · 217 not inspected yet". Nothing for a source
 * without files.
 */
function InspectionCounts({ source }: { source: LibrarySource }) {
  const { t } = useTranslation(['library', 'common'])
  const { inspectedCount, problemCount, notInspectedCount } = source.inspection
  const parts = [
    inspectedCount > 0 && t('inspection.inspected', { count: inspectedCount, value: formatNumber(inspectedCount) }),
    problemCount > 0 && t('inspection.problems', { count: problemCount, value: formatNumber(problemCount) }),
    notInspectedCount > 0
      && t('inspection.notInspected', { count: notInspectedCount, value: formatNumber(notInspectedCount) }),
  ].filter((part) => typeof part === 'string')
  if (parts.length === 0) return null
  return <p className="settings-muted">{t('inspection.sourceFiles', { counts: parts.join(t('common:separator')) })}</p>
}

interface InspectionRowProps {
  overview: FileInspectionOverview
  isOwner: boolean
  /** Its Inspect files request is on its way. */
  starting: boolean
  /** The API's refusal of the last Inspect files. */
  startError: string | null
  onInspect: () => void
}

/**
 * MAM-9 — how the inspection of the library's files stands, and Inspect files: laid out like a
 * source's row, its line beside the button, or over it on a phone.
 */
function InspectionRow({ overview, isOwner, starting, startError, onInspect }: InspectionRowProps) {
  const { t } = useTranslation('library')
  // The first reason that applies; the API would refuse the start with the same one, except for a
  // queued run — on its own or behind the running one — which it would answer with that run again.
  const reason = !isOwner
    ? t('inspection.reasons.notOwner')
    : !overview.hasAllowedRoots
      ? t('inspection.reasons.noFolders')
      : overview.hasQueuedRun
        ? t('inspection.reasons.queued')
        : undefined
  const label = starting ? t('inspection.starting') : t('inspection.inspect')

  return (
    <div className="library-source library-inspection">
      <div className="library-source-body">
        <InspectionLine overview={overview} isOwner={isOwner} />
        {startError && <StartErrorLine message={startError} />}
      </div>
      <div className="library-source-actions">
        <DisabledReason reason={reason}>
          <Button
            type="button"
            variant="outline"
            size="sm"
            title={label}
            disabled={reason !== undefined || starting}
            onClick={onInspect}
          >
            {starting
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : <ScanSearch className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className="label-text">{label}</span>
          </Button>
        </DisabledReason>
      </div>
    </div>
  )
}

/**
 * The inspection in one line, `role="status"` like a source's run line: off without a media folder,
 * the run under way, else the last one that finished.
 */
function InspectionLine({ overview, isOwner }: { overview: FileInspectionOverview; isOwner: boolean }) {
  const { t } = useTranslation('library')

  // Off first: without a media folder a run reads nothing, whatever is queued.
  if (!overview.hasAllowedRoots) {
    return (
      <p role="status" className="library-source-warning">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        <span>
          {t('inspection.off')}
          {isOwner && (
            <>
              {' '}
              <Link to="/settings/media-folders" className="library-link">{t('inspection.chooseFolders')}</Link>
            </>
          )}
        </span>
      </p>
    )
  }

  const run = overview.activeRun ?? overview.lastFinishedRun
  const text = (() => {
    if (run === null) return t('inspection.never')
    switch (run.status) {
      case 'Queued':
        return t('inspection.queued')
      case 'Running':
        return run.filesTotal === null
          ? t('inspection.running')
          : t('inspection.runningProgress', {
            count: run.filesTotal,
            checked: formatNumber(run.filesChecked),
            total: formatNumber(run.filesTotal),
          })
      case 'Succeeded':
        return t('inspection.succeeded', {
          count: run.filesChecked,
          time: formatDateTime(finishedAt(run), INSTANT_FORMAT),
          checked: formatNumber(run.filesChecked),
          probed: formatNumber(run.filesProbed),
        })
      case 'Failed':
        // Null for anyone but the owner, like a failed sync's.
        return run.error === null
          ? t('inspection.failedHidden')
          : t('inspection.failed', { time: formatDateTime(finishedAt(run), INSTANT_FORMAT), error: run.error })
    }
  })()

  // A run queued behind the running one shows only here: the running one is what happens now.
  const shown = run?.status === 'Running' && overview.hasQueuedRun ? t('inspection.followedBy', { line: text }) : text

  return (
    <p role="status" className="library-source-status" data-status={run?.status}>
      {run !== null && <InspectionIcon run={run} />}
      <span>{shown}</span>
    </p>
  )
}

function InspectionIcon({ run }: { run: FileInspectionRun }) {
  switch (run.status) {
    case 'Queued':
    case 'Running':
      return <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
    case 'Succeeded':
      return <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
    case 'Failed':
      return <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
  }
}
