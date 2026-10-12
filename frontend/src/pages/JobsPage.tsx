import { useId, useState } from 'react'
import { BellRing, ChevronRight, CircleCheck, List, Loader2, type LucideIcon } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { CancelJobButton } from '@/components/jobs/CancelJobButton'
import { DeleteForGoodButton } from '@/components/jobs/DeleteForGoodButton'
import { JobDownloadSummary } from '@/components/jobs/JobDownloadSummary'
import { JobRowDetails } from '@/components/jobs/JobRowDetails'
import { KeepBothButton } from '@/components/jobs/KeepBothButton'
import { ReplacementSummary } from '@/components/jobs/ReplacementSummary'
import { RestoreButton } from '@/components/jobs/RestoreButton'
import { RetryReplacementButton } from '@/components/jobs/RetryReplacementButton'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { PageTabs } from '@/components/shared/PageTabs'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import { canCancelJob, JOB_TABS, jobTabFrom, jobTabView, type JobTab } from '@/lib/download-jobs'
import { formatDateTime, formatNumber } from '@/lib/format'
import { useDownloadJobs, useJobActionsPending } from '@/lib/queries'
import type { DownloadJob } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, table and row actions of this page
import '@/styles/library.css' // .library-link — the release title that opens its job, the link to Search
import '@/styles/jobs.css' // .jobs-* — the tab panel, the row's chevron and title, its cells' blocks, the details row

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:jobs:pageSize'

/** When a job was sent, as the Library's sync lines show an instant. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * The table's columns for the owner: Release, Download, Replacement, Actions. A member's rows have no
 * Replacement; a row's details open in one cell that spans them all.
 */
const OWNER_COLUMN_COUNT = 4

/** Each tab's icon. */
const TAB_ICONS: Record<JobTab, LucideIcon> = {
  NeedsYou: BellRing,
  InProgress: Loader2,
  Finished: CircleCheck,
  All: List,
}

/**
 * MAM-14 — Jobs: the releases the signed-in user sent to a download client, newest first, paged by the
 * server. Every user sees their own jobs only, the installation owner included (MAM-14's decision 1).
 *
 * MAM-41 — underline tabs in place of the status filter: Needs you — the jobs that wait for the user's
 * decision: a replacement stopped for review or holding old copies in the quarantine, a job that needs
 * linking —, In progress, Finished, and All. The server sorts the jobs into them (DownloadJobView) and
 * counts the first two whatever tab is open, so their names say how many they hold; one that holds none
 * shows no number. The tab is in the address (`?tab=NeedsYou`, left out for All; one that isn't a tab reads as All), so F5
 * keeps it, and another tab starts at page 1, as on the Duplicates page. An empty tab says so, with the
 * way to every job.
 *
 * A row is short: the release, with its client, destination and when it was sent under it; where the
 * download stands (JobDownloadSummary); the owner's — where the replacement stands (ReplacementSummary);
 * and the actions, verbs only: Retry, Keep both, Restore, Delete for good, Cancel job. Copies was details,
 * not an action (Vasyl, 2026-10-12): the chevron before the title opens the row's details under it —
 * every sentence the cells cut short, and the replacement's copies with Drop (JobRowDetails).
 *
 * While a job on the page is still worked on, the list is asked for again every 2 s
 * (useDownloadJobs), so statuses and progress move on by themselves.
 */
export function JobsPage() {
  const { t } = useTranslation(['jobs', 'common'])
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = jobTabFrom(searchParams.get('tab'))
  const idPrefix = useId()
  const panelId = `${idPrefix}-panel`
  const tabId = (of: JobTab) => `${idPrefix}-tab-${of}`
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  // The page belongs to the tab it was reached on, so another tab starts at page 1 without an effect.
  const [paging, setPaging] = useState<{ tab: JobTab; page: number }>({ tab, page: 1 })
  const [actionError, setActionError] = useState<string | null>(null)

  const onTab = paging.tab === tab
  if (!onTab) setPaging({ tab, page: 1 })
  const page = onTab ? paging.page : 1
  const jobs = useDownloadJobs({ view: jobTabView(tab), page, pageSize })
  const { data, dataUpdatedAt, error, isLoading, isPlaceholderData } = jobs
  const load = useLoadRetry(jobs)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A job that leaves the tab (decided, ended or cancelled) can leave it with fewer pages than the one
  // on screen: move to the last one there is, on this tab's own answer only.
  if (onTab && data && !isPlaceholderData && page > pages) setPaging({ tab, page: pages })

  const select = (next: JobTab) => {
    if (next !== tab) setSearchParams(next === 'All' ? {} : { tab: next }, { replace: true })
  }

  const goToPage = (next: number) => setPaging({ tab, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  /** A tab's name, with how many jobs it holds where the server counts them. */
  const tabLabel = (of: JobTab) => {
    const count = of === 'NeedsYou' ? data?.needsYouCount : of === 'InProgress' ? data?.inProgressCount : undefined
    return count === undefined || count === 0 ? t(`tabs.${of}`) : t('tabs.counted', { name: t(`tabs.${of}`), value: formatNumber(count) })
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      <PageTabs
        label={t('tabs.label')}
        tabs={JOB_TABS.map((value) => ({ value, label: tabLabel(value), icon: TAB_ICONS[value] }))}
        current={tab}
        panelId={panelId}
        tabId={tabId}
        onSelect={select}
      />

      <div id={panelId} role="tabpanel" aria-labelledby={tabId(tab)} className="jobs-panel">
        {/* Outside the ternary below, so a failed refresh or a refused action adds a line above the
            rows instead of replacing them. */}
        {error && data && !isPlaceholderData && (
          <Banner action={<RetryButton onRetry={() => jobs.refetch()} />}>
            {t('refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}
        {actionError && <Banner onDismiss={() => setActionError(null)}>{actionError}</Banner>}

        {load.error ? (
          <ErrorState
            title={t('loadFailed')}
            onRetry={retryCanHelp(load.error) ? load.retry : undefined}
            retrying={load.retrying}
          >
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="settings-muted">{t('common:loading')}</p>
        ) : data && data.totalCount === 0 && tab !== 'All' ? (
          <div className="settings-empty">
            <p>{t(`emptyTab.${tab}`)}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => select('All')}>
              {t('showAll')}
            </Button>
          </div>
        ) : data && data.totalCount === 0 ? (
          <div className="settings-empty">
            <p>
              <Trans t={t} i18nKey="empty" components={{ search: <Link className="library-link" to="/search" /> }} />
            </p>
          </div>
        ) : data ? (
          <>
            <div className="settings-table-wrap">
              <table className="settings-table">
                <thead>
                  <tr>
                    <th scope="col">{t('columns.release')}</th>
                    <th scope="col">{t('columns.download')}</th>
                    {isOwner && <th scope="col">{t('columns.replacement')}</th>}
                    <th scope="col">{t('columns.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((job) => (
                    <JobRow
                      key={job.id}
                      job={job}
                      isOwner={isOwner}
                      answeredAt={dataUpdatedAt}
                      onActionStart={() => setActionError(null)}
                      onActionError={setActionError}
                    />
                  ))}
                </tbody>
              </table>
            </div>

            <ListPager
              page={page}
              pages={pages}
              pageSize={pageSize}
              pageSizes={PAGE_SIZES}
              totalCount={data.totalCount}
              onPage={goToPage}
              onPageSize={changePageSize}
            />
          </>
        ) : null}
      </div>
    </div>
  )
}

// ── one row ──

interface JobRowProps {
  job: DownloadJob
  /** The owner's rows carry the Replacement cell and its actions (ADR 0033). */
  isOwner: boolean
  /**
   * When the list on screen came back (ms since the epoch): the instant a `Created` job's age is
   * judged at for Cancel (canCancelJob). Not the clock read during render, which a render must not
   * do; the list is asked for again every 2 s while it shows a `Created` job, so Cancel still
   * appears within one poll of the job counting as interrupted. 0 while another page's list stands
   * in for this one's, which hides Cancel on a `Created` row until this page's answer is back.
   */
  answeredAt: number
  /**
   * Cancel, or an action on the job's replacement (Retry, Keep both, Restore, Delete for good, a Drop
   * from the details), is about to be sent: the page clears its last refusal.
   */
  onActionStart: () => void
  /** One of them was refused: the page shows the sentence above the rows. */
  onActionError: (message: string) => void
}

/**
 * A table row on a wide screen, a card on a phone: the title heads the card, and the Download and
 * Replacement cells carry their column names in `data-label` for the stacked layout. The client and
 * the destination are named as they were when the job was made; a destination removed since is named
 * by the category actually sent.
 *
 * MAM-41 — the chevron before the title opens the row's details in a row of their own under it, as
 * wide as the table (aria-expanded, and aria-controls while they are open); the replacement's details
 * are asked for only while they are. The owner's actions on the replacement come first, the step
 * forward first: Retry and Keep both while it waits for review, Restore and Delete for good while it
 * holds old copies in the quarantine — each shown when the server says it can be done (MAM-27's Keep
 * both ends only the replacement). Cancel job comes last: it stops following the download too.
 */
function JobRow({ job, isOwner, answeredAt, onActionStart, onActionError }: JobRowProps) {
  const { t } = useTranslation(['jobs', 'common'])
  const replacement = isOwner ? job.replacement : null
  // One busy flag for the row: while any of its actions is on its way — a Drop from its details
  // included —, the others wait, as on the job's page.
  const busy = useJobActionsPending(job.id, replacement?.id ?? null)
  const detailsId = useId()
  const [open, setOpen] = useState(false)
  const destination = job.destinationName ?? job.clientDestinationKey ?? t('noDestination')
  const sent = [job.downloadClientName, destination, formatDateTime(job.createdUtc, INSTANT_FORMAT)].join(t('common:separator'))
  const cancellable = canCancelJob(job, answeredAt)
  const anyAction = replacement?.canRetry || replacement?.canCancel || replacement?.canRestore
    || replacement?.canDeleteForGood || cancellable

  return (
    <>
      <tr className={open ? 'jobs-row-open' : undefined}>
        <td className="jobs-release-cell">
          <div className="jobs-release">
            <button
              type="button"
              className="jobs-expand"
              aria-expanded={open}
              aria-controls={open ? detailsId : undefined}
              aria-label={open ? t('row.hideDetails') : t('row.showDetails')}
              title={open ? t('row.hideDetails') : t('row.showDetails')}
              onClick={() => setOpen(!open)}
            >
              <ChevronRight className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="jobs-release-text">
              <Link className="settings-name-text library-link" to={`/jobs/${job.id}`}>{job.releaseTitle}</Link>
              <span className="ui-hint">{sent}</span>
            </div>
          </div>
        </td>
        <td data-label={t('columns.download')} className="jobs-stacked-cell">
          <JobDownloadSummary job={job} />
        </td>
        {isOwner && (
          <td data-label={t('columns.replacement')} className="jobs-stacked-cell jobs-replacement-cell">
            <ReplacementSummary replacement={replacement} />
          </td>
        )}
        <td className="settings-actions-cell jobs-actions-cell">
          {anyAction && (
            <div className="settings-actions">
              {replacement?.canRetry && (
                <RetryReplacementButton replacement={replacement} busy={busy} onStart={onActionStart} onError={onActionError} />
              )}
              {replacement?.canCancel && (
                <KeepBothButton job={job} replacement={replacement} busy={busy} onStart={onActionStart} onError={onActionError} />
              )}
              {replacement?.canRestore && (
                <RestoreButton replacement={replacement} busy={busy} onStart={onActionStart} onError={onActionError} />
              )}
              {replacement?.canDeleteForGood && (
                <DeleteForGoodButton replacement={replacement} busy={busy} onStart={onActionStart} onError={onActionError} />
              )}
              {cancellable && (
                <CancelJobButton job={job} busy={busy} onStart={onActionStart} onError={onActionError} />
              )}
            </div>
          )}
        </td>
      </tr>
      {open && (
        <tr id={detailsId} className="jobs-details-row">
          <td colSpan={isOwner ? OWNER_COLUMN_COUNT : OWNER_COLUMN_COUNT - 1}>
            <JobRowDetails job={job} replacement={replacement} busy={busy} onStart={onActionStart} onError={onActionError} />
          </td>
        </tr>
      )}
    </>
  )
}
