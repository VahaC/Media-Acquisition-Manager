import type { ReactNode } from 'react'
import type { TFunction } from 'i18next'
import { CircleAlert, CircleCheck, CircleDashed, Loader2, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { formatDateTime, formatFileSize, formatNumber } from '@/lib/format'
import { unreportedCountOf } from '@/lib/library-runs'
import type {
  FileInspectionOverview,
  FileInspectionRun,
  FolderScanOverview,
  FolderScanRun,
  LibrarySource,
  LibrarySourceList,
  LibrarySyncRun,
  LibrarySyncStatus,
  UnreportedFileView,
} from '@/lib/types'
import '@/styles/library.css' // .library-bar-* — the items, their icons and the chips

/** The day and the minute, without the year and the seconds: a bar item has room for little. */
const BAR_INSTANT: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }

interface LibrarySourcesBarProps {
  data: LibrarySourceList
  isOwner: boolean
  /** MAM-42 — opens the Files in no library dialog on a view; none for every file the app counts. */
  onShowFiles: (view?: UnreportedFileView) => void
}

/**
 * MAM-36 — what the Sources card says when its details are closed: one item per media library, one
 * for the media folders and one for the file inspection — each with how its last run went and, in
 * amber chips, what needs a look — and what all the files take on disk. The same facts the rows under
 * Details tell in full sentences, which is where the buttons are.
 *
 * MAM-42 — each chip sits on the item whose row under Details holds its amber line, so the two views
 * always show the same warnings: a library's files without an app path and those that couldn't be read
 * on the library, the files in no library, the folders not scanned and the files in no library that
 * couldn't be read on Folders, the files the hashing couldn't read on Inspection. The Folders chips open
 * the Files in no library dialog, the only place that lists those files.
 *
 * Without a media folder there is no scan and no inspection to tell of: one sentence says so, with
 * the way to the settings for the owner.
 */
export function LibrarySourcesBar({ data, isOwner, onShowFiles }: LibrarySourcesBarProps) {
  const { t } = useTranslation(['library', 'common'])
  const { scan, inspection } = data
  const { fileCount, sizeBytes } = inspection.space

  return (
    <ul className="library-bar-items" aria-label={t('bar.label')}>
      {data.items.map((source) => <SourceItem key={source.integrationId} source={source} />)}
      {inspection.hasAllowedRoots ? (
        <>
          <FoldersItem scan={scan} onShowFiles={onShowFiles} />
          <InspectionItem overview={inspection} />
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

// ── the items ──

function SourceItem({ source }: { source: LibrarySource }) {
  const { t } = useTranslation('library')
  const run = source.latestRun
  const usable = source.isEnabled && source.canReadLibrary
  const status = usable ? run?.status ?? null : null
  const missing = source.filesWithoutAppPathCount
  const problems = source.inspection.problemCount

  return (
    <BarItem
      name={source.name}
      detail={usable ? syncDetail(run, t) : source.isEnabled ? t('bar.cannotRead') : t('bar.disabled')}
      status={status}
      chips={(
        <>
          {missing > 0 && (
            <WarningChip>{t('bar.withoutPath', { count: missing, value: formatNumber(missing) })}</WarningChip>
          )}
          {problems > 0 && (
            <WarningChip>{t('inspection.problems', { count: problems, value: formatNumber(problems) })}</WarningChip>
          )}
        </>
      )}
    />
  )
}

function FoldersItem({ scan, onShowFiles }: { scan: FolderScanOverview; onShowFiles: (view?: UnreportedFileView) => void }) {
  const { t } = useTranslation('library')
  const run = scan.activeRun ?? scan.lastFinishedRun
  // MAM-42 — counted now, as the row under Details counts them: the names a scan stored that no library
  // reports and the owner hasn't ignored, not what the last scan found.
  const unreported = unreportedCountOf(scan)
  const skipped = run?.problemCount ?? 0
  const problems = scan.files.problemCount

  return (
    <BarItem
      name={t('bar.folders')}
      detail={scanDetail(run, t)}
      status={run?.status ?? null}
      chips={(
        <>
          {unreported > 0 && (
            <WarningChip onClick={() => onShowFiles()}>
              {t('bar.noLibrary', { count: unreported, value: formatNumber(unreported) })}
            </WarningChip>
          )}
          {skipped > 0 && (
            <WarningChip>{t('bar.skipped', { count: skipped, value: formatNumber(skipped) })}</WarningChip>
          )}
          {problems > 0 && (
            <WarningChip onClick={() => onShowFiles('Problems')}>
              {t('inspection.problems', { count: problems, value: formatNumber(problems) })}
            </WarningChip>
          )}
        </>
      )}
    />
  )
}

function InspectionItem({ overview }: { overview: FileInspectionOverview }) {
  const { t } = useTranslation('library')
  const run = overview.activeRun ?? overview.lastFinishedRun
  // MAM-42 — the files the hashing couldn't read, which the row under Details warns of once the run has
  // succeeded; the files no look could read are their own source's chips.
  const notHashed = run?.status === 'Succeeded' ? run.hashing.problemCount : 0

  return (
    <BarItem
      name={t('bar.inspection')}
      detail={inspectionDetail(run, t)}
      status={run?.status ?? null}
      chips={notHashed > 0 && (
        <WarningChip>{t('bar.notHashed', { count: notHashed, value: formatNumber(notHashed) })}</WarningChip>
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

/** An amber chip; MAM-42 — a button when it opens the files it counts. */
function WarningChip({ children, onClick }: { children: ReactNode; onClick?: () => void }) {
  const { t } = useTranslation('library')
  const content = (
    <>
      <TriangleAlert className="h-3 w-3" aria-hidden="true" />
      {children}
    </>
  )
  return onClick === undefined
    ? <span className="ui-badge library-badge-warning">{content}</span>
    : (
      <button type="button" className="ui-badge library-badge-warning library-badge-button" title={t('bar.showFiles')} onClick={onClick}>
        {content}
      </button>
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
