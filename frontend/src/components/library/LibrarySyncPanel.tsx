import { useId, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { CircleAlert, CircleCheck, FolderSearch, Loader2, RefreshCw, ScanSearch, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime, formatFileSize, formatNumber } from '@/lib/format'
import { isSyncUnderWay } from '@/lib/library-runs'
import { qk, useLibrarySources, useStartFileInspection, useStartFolderScan, useStartLibrarySync } from '@/lib/queries'
import type {
  FileHashing,
  FileInspectionOverview,
  FileInspectionRun,
  FolderScanOverview,
  FolderScanRun,
  LibrarySource,
  LibrarySourceInspection,
  LibrarySourceList,
  LibrarySpace,
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
 *
 * MAM-18 — a Media folders row closes the list of sources: the app's own scan of the allowed folders
 * for the files no library reported, with Scan folders for the owner, polled while a scan is under
 * way. It and the inspection row show without a media library too, as neither needs one.
 *
 * MAM-19 — an inspection ends by hashing the files a byte-identical copy could hide among, so the
 * inspection row follows that step while it runs and tells how it went once the run has finished.
 * Every source, the Media folders row and — for all the files — the inspection row say how much room
 * their files take on disk, each file counted once however many names lead to it (ADR 0018). The
 * polling needs nothing more: the hashing is part of the run under way.
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
  const scanFolders = useStartFolderScan()
  const [startError, setStartError] = useState<StartError | null>(null)
  const [inspectError, setInspectError] = useState<string | null>(null)
  const [scanError, setScanError] = useState<string | null>(null)

  const startScan = async () => {
    setScanError(null)
    try {
      await scanFolders.mutateAsync()
    } catch (e: unknown) {
      // As with Inspect files: mostly a 409 (no media folder is allowed any more), which the refetch
      // the start ends with has already shown on the row; the sentence says why under it.
      setScanError(getApiErrorMessage(e, t('scan.startFailed')))
    }
  }

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
        ) : data ? (
          <>
            {/* MAM-18 — no media library leaves the sentence that says how to connect one; the rows
                under it stay, as the folders are scanned and the files inspected without one. */}
            {data.items.length === 0 && (
              <div className="settings-empty">
                <p>{isOwner ? t('sources.emptyOwner') : t('sources.emptyMember')}</p>
                {isOwner && (
                  <Button asChild variant="outline" size="sm">
                    <Link to="/settings/integrations">{t('sources.openIntegrations')}</Link>
                  </Button>
                )}
              </div>
            )}
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
              {/* MAM-18 — the app's own source of file names, after the libraries: a row of the same
                  list, parted from them by its line. */}
              <ScanRow
                scan={data.scan}
                hasAllowedRoots={data.inspection.hasAllowedRoots}
                isOwner={isOwner}
                starting={scanFolders.isPending}
                startError={scanError}
                onScan={() => void startScan()}
              />
            </ul>
            {/* Under the sources and the Media folders row, as one run reads the files of them all. */}
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
        <InspectionCounts inspection={source.inspection} frame={(counts) => t('inspection.sourceFiles', { counts })} />
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
        {/* MAM-19 — after the warnings, which belong to the files line above them. */}
        <SpaceLine space={source.space} />
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
 * When a run ended; a finished run always has it, the request time stands in otherwise. A sync's,
 * (MAM-9) an inspection's or (MAM-18) a scan's.
 */
function finishedAt(run: Pick<LibrarySyncRun | FileInspectionRun | FolderScanRun, 'finishedUtc' | 'createdUtc'>): string {
  return run.finishedUtc ?? run.createdUtc
}

// ── the latest run ──

/**
 * The source's latest run in one line. `role="status"`, so a screen reader reads a change out —
 * "Syncing… 50 of 120 items", then the outcome — without moving the focus.
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
        // Items read and works stored are counted apart: two copies of one movie are one work.
        // MAM-32 — the items are movies, series and episodes; the works movies and episodes, and the
        // series are counted on their own. A sync from before series were read has no such count.
        const works = run.worksCount ?? 0
        const versions = run.versionsCount ?? 0
        const files = run.filesCount ?? 0
        const counts = {
          time: formatDateTime(finishedAt(run), INSTANT_FORMAT),
          items: t('run.items', { count: run.itemsRead, value: formatNumber(run.itemsRead) }),
          works: t('run.works', { count: works, value: formatNumber(works) }),
          versions: t('run.versions', { count: versions, value: formatNumber(versions) }),
          files: t('run.files', { count: files, value: formatNumber(files) }),
        }
        return run.seriesCount === null
          ? t('run.succeeded', counts)
          : t('run.succeededSeries', {
            ...counts,
            series: t('run.series', { count: run.seriesCount, value: formatNumber(run.seriesCount) }),
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

/** A run's status as its icon. MAM-18 — a scan's statuses are a sync's, and so are its icons. */
function RunIcon({ run }: { run: LibrarySyncRun | FolderScanRun }) {
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
 *
 * MAM-18 — the Media folders row counts the names its scans stored in the same parts; `frame` puts
 * them in each row's own sentence.
 */
function InspectionCounts({ inspection, frame }: {
  inspection: LibrarySourceInspection
  frame: (counts: string) => string
}) {
  const { t } = useTranslation(['library', 'common'])
  const { inspectedCount, problemCount, notInspectedCount } = inspection
  const parts = [
    inspectedCount > 0 && t('inspection.inspected', { count: inspectedCount, value: formatNumber(inspectedCount) }),
    problemCount > 0 && t('inspection.problems', { count: problemCount, value: formatNumber(problemCount) }),
    notInspectedCount > 0
      && t('inspection.notInspected', { count: notInspectedCount, value: formatNumber(notInspectedCount) }),
  ].filter((part) => typeof part === 'string')
  if (parts.length === 0) return null
  return <p className="settings-muted">{frame(parts.join(t('common:separator')))}</p>
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
  // MAM-19 — the run the status line tells of; none while the line says the inspection is off.
  const run = overview.hasAllowedRoots ? overview.activeRun ?? overview.lastFinishedRun : null

  return (
    <div className="library-source library-inspection">
      <div className="library-source-body">
        <InspectionLine overview={overview} isOwner={isOwner} />
        {/* MAM-19 — the hashing's outcome is known once the run has succeeded. A failed run has none:
            what it hashed before a restart stopped it is the next run's cache. */}
        {run?.status === 'Succeeded' && <HashingOutcome hashing={run.hashing} />}
        {overview.hasAllowedRoots && <SpaceLine space={overview.space} total />}
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
        // MAM-19 — hashing is the run's last step: once it has listed files to hash, the line
        // follows them instead of the files checked, which are all done by then.
        if (run.hashing.candidates !== null && run.hashing.candidates > 0) {
          return t('inspection.hashing.running', {
            count: run.hashing.candidates,
            checked: formatNumber(run.hashing.checked),
            total: formatNumber(run.hashing.candidates),
          })
        }
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

/**
 * MAM-19 — how the hashing at the end of a finished inspection went, under its status line: how many
 * of the files that could be copies it hashed and how many it read in full rather than answered from the
 * cache, or that no file needed a hash — as a run from before the hashing reads too. The files it
 * couldn't hash follow as a warning, like the folders a scan didn't go into.
 */
function HashingOutcome({ hashing }: { hashing: FileHashing }) {
  const { t } = useTranslation('library')
  const candidates = hashing.candidates ?? 0
  const problems = hashing.problemCount > 0
    ? { count: hashing.problemCount, value: formatNumber(hashing.problemCount) }
    : null

  return (
    <>
      <p className="settings-muted">
        {candidates === 0
          ? t('inspection.hashing.none')
          : t('inspection.hashing.succeeded', {
            count: candidates,
            // A file it couldn't hash is counted as checked all the same.
            hashed: formatNumber(hashing.checked - hashing.problemCount),
            total: formatNumber(candidates),
            read: formatNumber(hashing.read),
          })}
      </p>
      {problems !== null && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          {/* The first problem's sentence quotes the owner's paths: null for anyone else, who reads
              how many there were. */}
          <span>
            {hashing.problem === null
              ? t('inspection.hashing.problemsMember', problems)
              : t('inspection.hashing.problemsOwner', { ...problems, problem: hashing.problem })}
          </span>
        </p>
      )}
    </>
  )
}

// ── the room the files take ──

/**
 * MAM-19 — how much room a row's files take on disk, each file counted once however many names lead
 * to it (ADR 0018), then the names past one per file and those the app hasn't measured yet, each a
 * sentence of its own. Nothing until the app has measured a file. `total` makes it the sentence of
 * every file, on the inspection row.
 */
function SpaceLine({ space, total = false }: { space: LibrarySpace; total?: boolean }) {
  const { t } = useTranslation('library')
  const { sizeBytes, fileCount, nameCount, unmeasuredCount } = space
  if (fileCount === 0) return null
  const files = { count: fileCount, size: formatFileSize(sizeBytes), value: formatNumber(fileCount) }
  const moreNames = nameCount - fileCount

  return (
    <p className="settings-muted">
      {total ? t('space.total', files) : t('space.row', files)}
      {moreNames > 0 && <>{' '}{t('space.moreNames', { count: moreNames, value: formatNumber(moreNames) })}</>}
      {unmeasuredCount > 0
        && <>{' '}{t('space.unmeasured', { count: unmeasuredCount, value: formatNumber(unmeasuredCount) })}</>}
    </p>
  )
}

// ── the scan of the media folders ──

interface ScanRowProps {
  scan: FolderScanOverview
  /** Whether the owner has allowed any media folder: the inspection's flag, as one list serves both. */
  hasAllowedRoots: boolean
  isOwner: boolean
  /** Its Scan folders request is on its way. */
  starting: boolean
  /** The API's refusal of the last Scan folders. */
  startError: string | null
  onScan: () => void
}

/**
 * MAM-18 — the app's own source of file names, laid out like a library's row: how the scan of the
 * media folders stands, the folders it couldn't go into, the names it stored that no library
 * reported by what the app's look at each found, and Scan folders. A running scan leaves the button
 * on: another one queues behind it.
 */
function ScanRow({ scan, hasAllowedRoots, isOwner, starting, startError, onScan }: ScanRowProps) {
  const { t } = useTranslation('library')
  // The first reason that applies; the API would refuse the start with the same one, except for a
  // queued scan — on its own or behind the running one — which it would answer with that scan again.
  const reason = !isOwner
    ? t('scan.reasons.notOwner')
    : !hasAllowedRoots
      ? t('scan.reasons.noFolders')
      : scan.hasQueuedRun
        ? t('scan.reasons.queued')
        : undefined
  const label = starting ? t('scan.starting') : t('scan.scanFolders')
  // The scan the status line tells of; none while the line says scanning is off. Its problems are
  // known once it has finished, so a scan under way has none yet.
  const run = hasAllowedRoots ? scan.activeRun ?? scan.lastFinishedRun : null
  const problems = run !== null && run.problemCount > 0
    ? { count: run.problemCount, value: formatNumber(run.problemCount) }
    : null

  return (
    <li className="library-source library-scan">
      <div className="library-source-body">
        <p className="library-source-name">
          <span className="settings-name-text">{t('scan.name')}</span>
        </p>
        <ScanLine scan={scan} hasAllowedRoots={hasAllowedRoots} isOwner={isOwner} />
        {run !== null && problems !== null && (
          <p className="library-source-warning">
            <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            {/* The first problem's sentence quotes the owner's paths: null for anyone else, who reads
                how many there were. */}
            <span>
              {run.problem === null
                ? t('scan.problemsMember', problems)
                : t('scan.problemsOwner', { ...problems, problem: run.problem })}
            </span>
          </p>
        )}
        <InspectionCounts inspection={scan.files} frame={(counts) => t('scan.files', { counts })} />
        {/* The owner's sentence for the first of those names that couldn't be read, as a source's row
            shows its own. */}
        {scan.files.problem && (
          <p className="library-source-warning">
            <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{scan.files.problem}</span>
          </p>
        )}
        {/* MAM-19 — after the warning, which belongs to the files line above it. */}
        <SpaceLine space={scan.space} />
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
            onClick={onScan}
          >
            {starting || scan.activeRun !== null
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : <FolderSearch className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className="label-text">{label}</span>
          </Button>
        </DisabledReason>
      </div>
    </li>
  )
}

/**
 * MAM-18 — the scan in one line, `role="status"` like a source's run line: off without a media
 * folder, the scan under way, else the last one that finished.
 */
function ScanLine({ scan, hasAllowedRoots, isOwner }: {
  scan: FolderScanOverview
  hasAllowedRoots: boolean
  isOwner: boolean
}) {
  const { t } = useTranslation('library')

  // Off first, as the inspection's line: without a media folder a scan has nothing to walk.
  if (!hasAllowedRoots) {
    return (
      <p role="status" className="library-source-warning">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        <span>
          {t('scan.off')}
          {isOwner && (
            <>
              {' '}
              <Link to="/settings/media-folders" className="library-link">{t('scan.chooseFolders')}</Link>
            </>
          )}
        </span>
      </p>
    )
  }

  const run = scan.activeRun ?? scan.lastFinishedRun
  const text = (() => {
    if (run === null) return t('scan.never')
    switch (run.status) {
      case 'Queued':
        return t('scan.queued')
      case 'Running':
        return t('scan.running', { count: run.filesFound, found: formatNumber(run.filesFound) })
      case 'Succeeded':
        return t('scan.succeeded', {
          count: run.filesFound,
          time: formatDateTime(finishedAt(run), INSTANT_FORMAT),
          found: formatNumber(run.filesFound),
          // Stored with the success; null only before it.
          unreported: formatNumber(run.filesUnreported ?? 0),
        })
      case 'Failed':
        // Null for anyone but the owner, like a failed sync's.
        return run.error === null
          ? t('scan.failedHidden')
          : t('scan.failed', { time: formatDateTime(finishedAt(run), INSTANT_FORMAT), error: run.error })
    }
  })()

  // A scan queued behind the running one shows only here: the running one is what happens now.
  const shown = run?.status === 'Running' && scan.hasQueuedRun ? t('scan.followedBy', { line: text }) : text

  return (
    <p role="status" className="library-source-status" data-status={run?.status}>
      {run !== null && <RunIcon run={run} />}
      <span>{shown}</span>
    </p>
  )
}
