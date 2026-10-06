import type { ReactNode } from 'react'
import type { TFunction } from 'i18next'
import { CircleAlert, CircleCheck, CircleDashed, Loader2, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { formatDateTime, formatFileSize, formatNumber } from '@/lib/format'
import type {
  FileInspectionOverview,
  FileInspectionRun,
  FolderScanOverview,
  FolderScanRun,
  LibrarySource,
  LibrarySourceList,
  LibrarySyncRun,
  LibrarySyncStatus,
} from '@/lib/types'
import '@/styles/library.css' // .library-bar-* — the items, their icons and the chips

/** The day and the minute, without the year and the seconds: a bar item has room for little. */
const BAR_INSTANT: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }

interface LibrarySourcesBarProps {
  data: LibrarySourceList
  isOwner: boolean
}

/**
 * MAM-36 — what the Sources card says when its details are closed: one item per media library, one
 * for the media folders and one for the file inspection — each with how its last run went and, in
 * amber chips, what needs a look — and what all the files take on disk. The same facts the rows under
 * Details tell in full sentences, which is where the buttons are.
 *
 * Without a media folder there is no scan and no inspection to tell of: one sentence says so, with
 * the way to the settings for the owner.
 */
export function LibrarySourcesBar({ data, isOwner }: LibrarySourcesBarProps) {
  const { t } = useTranslation(['library', 'common'])
  const { scan, inspection } = data
  const { fileCount, sizeBytes } = inspection.space

  return (
    <ul className="library-bar-items" aria-label={t('bar.label')}>
      {data.items.map((source) => <SourceItem key={source.integrationId} source={source} />)}
      {inspection.hasAllowedRoots ? (
        <>
          <FoldersItem scan={scan} />
          <InspectionItem overview={inspection} problemCount={fileProblemCount(data)} />
        </>
      ) : (
        <li className="library-bar-item">
          <TriangleAlert className="h-3.5 w-3.5 library-bar-warning-icon" aria-hidden="true" />
          <span className="library-bar-detail">
            {t('bar.noFolders')}
            {isOwner && (
              <>
                {' '}
                <Link to="/settings/media-folders" className="library-link">{t('scan.chooseFolders')}</Link>
              </>
            )}
          </span>
        </li>
      )}
      {inspection.hasAllowedRoots && fileCount > 0 && (
        <li className="library-bar-total">
          {t('bar.total', { count: fileCount, value: formatNumber(fileCount), size: formatFileSize(sizeBytes) })}
        </li>
      )}
    </ul>
  )
}

/** The files no run could read: those of the libraries and those the scan stored. */
function fileProblemCount(data: LibrarySourceList): number {
  return data.items.reduce((sum, source) => sum + source.inspection.problemCount, 0) + data.scan.files.problemCount
}

// ── the items ──

function SourceItem({ source }: { source: LibrarySource }) {
  const { t } = useTranslation('library')
  const run = source.latestRun
  const usable = source.isEnabled && source.canReadLibrary
  const status = usable ? run?.status ?? null : null
  const missing = source.filesWithoutAppPathCount

  return (
    <BarItem
      name={source.name}
      detail={usable ? syncDetail(run, t) : source.isEnabled ? t('bar.cannotRead') : t('bar.disabled')}
      status={status}
      chips={missing > 0 && (
        <WarningChip>{t('bar.withoutPath', { count: missing, value: formatNumber(missing) })}</WarningChip>
      )}
    />
  )
}

function FoldersItem({ scan }: { scan: FolderScanOverview }) {
  const { t } = useTranslation('library')
  const run = scan.activeRun ?? scan.lastFinishedRun
  const unreported = run?.status === 'Succeeded' ? run.filesUnreported ?? 0 : 0
  const skipped = run?.problemCount ?? 0

  return (
    <BarItem
      name={t('bar.folders')}
      detail={scanDetail(run, t)}
      status={run?.status ?? null}
      chips={(
        <>
          {unreported > 0 && (
            <WarningChip>{t('bar.noLibrary', { count: unreported, value: formatNumber(unreported) })}</WarningChip>
          )}
          {skipped > 0 && (
            <WarningChip>{t('bar.skipped', { count: skipped, value: formatNumber(skipped) })}</WarningChip>
          )}
        </>
      )}
    />
  )
}

function InspectionItem({ overview, problemCount }: { overview: FileInspectionOverview; problemCount: number }) {
  const { t } = useTranslation('library')
  const run = overview.activeRun ?? overview.lastFinishedRun

  return (
    <BarItem
      name={t('bar.inspection')}
      detail={inspectionDetail(run, t)}
      status={run?.status ?? null}
      chips={problemCount > 0 && (
        <WarningChip>{t('inspection.problems', { count: problemCount, value: formatNumber(problemCount) })}</WarningChip>
      )}
    />
  )
}

// ── an item's parts ──

interface BarItemProps {
  name: string
  detail: string
  status: LibrarySyncStatus | null
  chips: ReactNode
}

function BarItem({ name, detail, status, chips }: BarItemProps) {
  return (
    <li className="library-bar-item" data-status={status ?? undefined}>
      <BarIcon status={status} />
      <span className="library-bar-name">{name}</span>
      <span className="library-bar-detail">{detail}</span>
      {chips}
    </li>
  )
}

/** A run's status as its icon; a dashed one for what never ran or can't. */
function BarIcon({ status }: { status: LibrarySyncStatus | null }) {
  switch (status) {
    case 'Queued':
    case 'Running':
      return <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
    case 'Succeeded':
      return <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
    case 'Failed':
      return <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
    case null:
      return <CircleDashed className="h-3.5 w-3.5" aria-hidden="true" />
  }
}

function WarningChip({ children }: { children: ReactNode }) {
  return (
    <span className="ui-badge library-badge-warning">
      <TriangleAlert className="h-3 w-3" aria-hidden="true" />
      {children}
    </span>
  )
}

// ── what each item says of its run ──

type Translate = TFunction<'library'>

/** When a run ended; a finished run always has it, the request time stands in otherwise. */
const endedAt = (run: Pick<LibrarySyncRun | FileInspectionRun | FolderScanRun, 'finishedUtc' | 'createdUtc'>) =>
  formatDateTime(run.finishedUtc ?? run.createdUtc, BAR_INSTANT)

function syncDetail(run: LibrarySyncRun | null, t: Translate): string {
  if (run === null) return t('bar.neverSynced')
  switch (run.status) {
    case 'Queued':
      return t('bar.queued')
    case 'Running':
      return run.itemsTotal === null
        ? t('bar.syncing')
        : t('bar.syncingProgress', { read: formatNumber(run.itemsRead), total: formatNumber(run.itemsTotal) })
    case 'Succeeded':
      return endedAt(run)
    case 'Failed':
      return t('bar.syncFailed')
  }
}

function scanDetail(run: FolderScanRun | null, t: Translate): string {
  if (run === null) return t('bar.neverScanned')
  switch (run.status) {
    case 'Queued':
      return t('bar.queued')
    case 'Running':
      return t('bar.scanning', { count: run.filesFound, found: formatNumber(run.filesFound) })
    case 'Succeeded':
      return endedAt(run)
    case 'Failed':
      return t('bar.scanFailed')
  }
}

function inspectionDetail(run: FileInspectionRun | null, t: Translate): string {
  if (run === null) return t('bar.neverInspected')
  switch (run.status) {
    case 'Queued':
      return t('bar.queued')
    case 'Running':
      // Hashing is the run's last step: once it has listed files to hash, the item follows them.
      if (run.hashing.candidates !== null && run.hashing.candidates > 0) {
        return t('bar.hashing', { checked: formatNumber(run.hashing.checked), total: formatNumber(run.hashing.candidates) })
      }
      return run.filesTotal === null
        ? t('bar.inspecting')
        : t('bar.inspectingProgress', { checked: formatNumber(run.filesChecked), total: formatNumber(run.filesTotal) })
    case 'Succeeded':
      return endedAt(run)
    case 'Failed':
      return t('bar.inspectionFailed')
  }
}
