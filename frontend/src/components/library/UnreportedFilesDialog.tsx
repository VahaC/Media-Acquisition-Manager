import { useId, useState } from 'react'
import { isAxiosError } from 'axios'
import { CircleAlert, Eye, EyeOff, FileQuestion, Loader2, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { InspectionLines } from '@/components/library/FileLines'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { PageTabs } from '@/components/shared/PageTabs'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime, formatFileSize } from '@/lib/format'
import { splitPath } from '@/lib/library-versions'
import { useSetUnreportedFileIgnored, useUnreportedFiles } from '@/lib/queries'
import type { UnreportedFile, UnreportedFileList, UnreportedFileView } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { DEFAULT_PAGE_SIZE, PAGE_SIZES } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-dialog.css' // .settings-dialog, -body — the dialog's scroller and its footer below it
import '@/styles/settings-list.css' // .settings-muted, .settings-empty — the loading line and the empty state
import '@/styles/library.css' // .library-file*, .library-source-status — a file's lines and a refused action
import '@/styles/unreported-files.css' // .unreported-files* — the width, the list and each file's row

/** Date and time without the seconds, as the Sources card says when its runs ended. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/** MAM-42 — the dialog's tabs: every file the app counts, then the API's two views. */
export type UnreportedFilesTab = 'All' | UnreportedFileView

const TABS: readonly { value: UnreportedFilesTab; icon: typeof FileQuestion }[] = [
  { value: 'All', icon: FileQuestion },
  { value: 'Problems', icon: TriangleAlert },
  { value: 'Ignored', icon: EyeOff },
]

/** How many files a tab holds, from the latest answer of any tab: each answer counts all three. */
function countOf(list: UnreportedFileList, tab: UnreportedFilesTab): number {
  return tab === 'All' ? list.countedCount : tab === 'Problems' ? list.problemCount : list.ignoredCount
}

interface UnreportedFilesDialogProps {
  /** The tab it opens on: the bar's chip or the row's Show files decides. */
  initialTab: UnreportedFilesTab
  onClose: () => void
}

/**
 * MAM-42 — the files the scan of the media folders found that no library lists, which the Library page
 * only counted before: each one's name over its folder, its size, why a library most likely doesn't list
 * it, and what the app's look at it found. Three tabs — every file the app counts, those it couldn't read,
 * those the owner ignored — each paged by the server, in path order.
 *
 * The owner's Ignore takes a file that isn't media (an installer's ISO) off every count, and Stop ignoring
 * puts it back; neither changes anything on the disk. A member sees both disabled with the reason. A
 * refusal stays on its file's row while the list has it.
 *
 * The parent mounts it only while it is open, so it starts on its tab's first page each time. Its body is
 * the one scroller; Close sits last.
 */
export function UnreportedFilesDialog({ initialTab, onClose }: UnreportedFilesDialogProps) {
  const { t } = useTranslation(['library', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const panelId = useId()
  const tabIdBase = useId()
  const [tab, setTab] = useState<UnreportedFilesTab>(initialTab)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  // A refused Ignore or Stop ignoring, on the row of the file it was for.
  const [failure, setFailure] = useState<{ id: string; message: string } | null>(null)
  const files = useUnreportedFiles({ view: tab === 'All' ? undefined : tab, page, pageSize }, true)
  const { data, error, isLoading, isPlaceholderData } = files
  const load = useLoadRetry(files)
  const setIgnored = useSetUnreportedFileIgnored()
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // Only on this page's own answer, not the one kept on screen while it loads: an Ignore can empty the
  // last page, which then steps back to the one before.
  if (data && !isPlaceholderData && page > pages) setPage(pages)

  const selectTab = (next: UnreportedFilesTab) => {
    setTab(next)
    setPage(1)
    setFailure(null)
  }

  const toggle = async (file: UnreportedFile) => {
    const ignored = file.ignoredUtc === null
    setFailure(null)
    try {
      await setIgnored.mutateAsync({ id: file.id, ignored })
    } catch (e: unknown) {
      // A bare 404: a scan or a sync took the file out of no library meanwhile; the lists, asked for
      // again, show what there is now.
      const gone = isAxiosError(e) && e.response?.status === 404
      setFailure({
        id: file.id,
        message: gone ? t('unreported.gone') : getApiErrorMessage(e, ignored ? t('unreported.ignoreFailed') : t('unreported.undoIgnoreFailed')),
      })
    }
  }

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog unreported-files-dialog">
        <DialogHeader>
          <DialogTitle>{t('unreported.title')}</DialogTitle>
          <DialogDescription>{t('unreported.description')}</DialogDescription>
        </DialogHeader>
        <PageTabs
          label={t('unreported.tabs.label')}
          tabs={TABS.map(({ value, icon }) => ({
            value,
            icon,
            label: data
              ? t('unreported.tabs.withCount', { name: t(`unreported.tabs.${value}`), count: countOf(data, value) })
              : t(`unreported.tabs.${value}`),
          }))}
          current={tab}
          panelId={panelId}
          tabId={(value) => `${tabIdBase}-${value}`}
          onSelect={selectTab}
        />
        <div id={panelId} role="tabpanel" aria-labelledby={`${tabIdBase}-${tab}`} className="settings-dialog-body unreported-files-body">
          {/* Outside the ternary below, so a failed refresh adds a line above the files instead of replacing them. */}
          {error && data && !isPlaceholderData && (
            <Banner action={<RetryButton onRetry={() => files.refetch()} />}>
              {t('unreported.refreshFailed')}{' '}
              {getApiErrorMessage(error)}
            </Banner>
          )}
          {load.error ? (
            <ErrorState
              title={t('unreported.loadFailed')}
              onRetry={retryCanHelp(load.error) ? load.retry : undefined}
              retrying={load.retrying}
            >
              {getApiErrorMessage(load.error)}
            </ErrorState>
          ) : isLoading ? (
            <p className="settings-muted">{t('common:loading')}</p>
          ) : data && data.totalCount === 0 ? (
            <div className="settings-empty">
              <p>{t(`unreported.empty.${tab}`)}</p>
            </div>
          ) : data ? (
            <>
              <ul className="unreported-files">
                {data.items.map((file) => (
                  <UnreportedFileRow
                    key={file.id}
                    file={file}
                    isOwner={isOwner}
                    pending={setIgnored.isPending && setIgnored.variables?.id === file.id}
                    error={failure?.id === file.id ? failure.message : null}
                    onToggle={() => void toggle(file)}
                  />
                ))}
              </ul>
              <ListPager
                page={page}
                pages={pages}
                pageSize={pageSize}
                pageSizes={PAGE_SIZES}
                totalCount={data.totalCount}
                onPage={(next) => {
                  setPage(next)
                  setFailure(null)
                }}
                onPageSize={(next) => {
                  setPageSize(next)
                  setPage(1)
                  setFailure(null)
                }}
              />
            </>
          ) : null}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>{t('common:close')}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

interface UnreportedFileRowProps {
  file: UnreportedFile
  isOwner: boolean
  /** Its Ignore or Stop ignoring is on its way. */
  pending: boolean
  /** The refusal of its last Ignore or Stop ignoring. */
  error: string | null
  onToggle: () => void
}

/**
 * One file: its name over its folder, its size and why a library most likely doesn't list it, what the
 * app's look found — ffprobe's reading, or why it couldn't read it, the Library page's lines — and, on the
 * Ignored tab, since when it is ignored. Ignore or Stop ignoring beside it, or under it on a phone.
 */
function UnreportedFileRow({ file, isOwner, pending, error, onToggle }: UnreportedFileRowProps) {
  const { t } = useTranslation('library')
  const { name, folder } = splitPath(file.appPath)
  const ignored = file.ignoredUtc !== null
  const label = pending
    ? ignored ? t('unreported.undoing') : t('unreported.ignoring')
    : ignored ? t('unreported.undoIgnore') : t('unreported.ignore')
  const place = t(`unreported.place.${file.place.kind}`, { library: file.place.libraryName, name: file.place.extraName })

  return (
    <li className="unreported-file">
      <div className="library-file unreported-file-body">
        <p className="library-file-name" title={file.appPath}>{name}</p>
        {folder !== null && <p className="library-file-folder">{folder}</p>}
        <p className="library-file-facts">
          <span>{file.sizeBytes === null ? t('file.sizeUnknown') : formatFileSize(file.sizeBytes)}</span>
        </p>
        <p className="settings-muted">{place}</p>
        <InspectionLines inspection={file.inspection} />
        {file.ignoredUtc !== null && (
          <p className="settings-muted">{t('unreported.ignoredAt', { time: formatDateTime(file.ignoredUtc, INSTANT_FORMAT) })}</p>
        )}
        {error !== null && (
          <p role="alert" className="library-source-status" data-status="Failed">
            <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{error}</span>
          </p>
        )}
      </div>
      <div className="unreported-file-actions">
        <DisabledReason reason={isOwner ? undefined : t('unreported.notOwner')}>
          <Button
            type="button"
            variant="outline"
            size="sm"
            title={ignored ? t('unreported.undoIgnoreTitle') : t('unreported.ignoreTitle')}
            disabled={!isOwner || pending}
            onClick={onToggle}
          >
            {pending
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : ignored ? <Eye className="h-3.5 w-3.5" aria-hidden="true" /> : <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className="label-text">{label}</span>
          </Button>
        </DisabledReason>
      </div>
    </li>
  )
}
