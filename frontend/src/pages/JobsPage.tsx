import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { CancelJobButton } from '@/components/jobs/CancelJobButton'
import { JobProgress } from '@/components/jobs/JobProgress'
import { JobStatus } from '@/components/jobs/JobStatus'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { canCancelJob, DOWNLOAD_JOB_STATUSES } from '@/lib/download-jobs'
import { formatDateTime } from '@/lib/format'
import { useDownloadJobs } from '@/lib/queries'
import type { DownloadJob, DownloadJobStatus } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, toolbar, table and row actions of this page
import '@/styles/library.css' // .library-link — the release title that opens its job, the link to Search
import '@/styles/jobs.css' // .jobs-stacked-cell — the Status cell that stacks its sentences on a phone

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:jobs:pageSize'

/** When a job was sent, as the Library's sync lines show an instant. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * MAM-14 — Jobs: the releases the signed-in user sent to a download client, newest first, filtered
 * by status and paged by the server. Every user sees their own jobs only, the installation owner
 * included (MAM-14's decision 1). Each row says where the job stands, why a followed job isn't moving
 * on, how far it has got, and where it went; its title opens the job's details. A job the app still
 * follows, or one that needs linking, can be cancelled: the app stops following it, and the torrent
 * stays in the client.
 *
 * While a job on the page is still followed, the list is asked for again every 10 s
 * (useDownloadJobs), so statuses and progress move on by themselves.
 */
export function JobsPage() {
  const { t } = useTranslation(['jobs', 'common'])
  const [status, setStatus] = useState<DownloadJobStatus | ''>('')
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  // The page remembers the filter it belongs to, so another status starts again at page 1 without
  // an effect — the idiom of IntegrationsPage.
  const [paging, setPaging] = useState({ status: '', page: 1 })
  const [actionError, setActionError] = useState<string | null>(null)

  const onFilter = paging.status === status
  if (!onFilter) setPaging({ status, page: 1 })
  const page = onFilter ? paging.page : 1
  const filtered = status !== ''
  const jobs = useDownloadJobs({ status: status || undefined, page, pageSize })
  const { data, dataUpdatedAt, error, isLoading, isPlaceholderData } = jobs
  const load = useLoadRetry(jobs)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A job that ends or is cancelled can leave a filtered list with fewer pages than the one on
  // screen: the refetched page is empty, so move to the last one there is. Only on this page's own
  // answer, not the one kept on screen while it loads.
  if (onFilter && data && !isPlaceholderData && page > pages) setPaging({ status, page: pages })

  const goToPage = (next: number) => setPaging({ status, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      <div className="settings-toolbar">
        <select
          className="ui-input settings-filter"
          value={status}
          aria-label={t('statusFilter.label')}
          onChange={(event) => setStatus(event.target.value as DownloadJobStatus | '')}
        >
          <option value="">{t('statusFilter.all')}</option>
          {DOWNLOAD_JOB_STATUSES.map((option) => (
            <option key={option} value={option}>{t(`status.${option}`)}</option>
          ))}
        </select>
      </div>

      {/* Outside the ternary below, so a failed refresh or a refused cancel adds a line above the
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
      ) : data && data.totalCount === 0 && filtered ? (
        <div className="settings-empty">
          <p>{t('noMatch')}</p>
          <Button type="button" variant="outline" size="sm" onClick={() => setStatus('')}>
            {t('clearFilters')}
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
                  <th scope="col">{t('columns.status')}</th>
                  <th scope="col">{t('columns.progress')}</th>
                  <th scope="col">{t('columns.sentTo')}</th>
                  <th scope="col">{t('columns.sent')}</th>
                  <th scope="col">{t('columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((job) => (
                  <JobRow
                    key={job.id}
                    job={job}
                    answeredAt={dataUpdatedAt}
                    onCancelStart={() => setActionError(null)}
                    onCancelError={setActionError}
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
  )
}

// ── one row ──

interface JobRowProps {
  job: DownloadJob
  /**
   * When the list on screen came back (ms since the epoch): the instant a `Created` job's age is
   * judged at for Cancel (canCancelJob). Not the clock read during render, which a render must not
   * do; the list is asked for again every 10 s while it shows a `Created` job, so Cancel still
   * appears within one poll of the job counting as interrupted. 0 while another page's list stands
   * in for this one's, which hides Cancel on a `Created` row until this page's answer is back.
   */
  answeredAt: number
  onCancelStart: () => void
  onCancelError: (message: string) => void
}

/**
 * A table row on a wide screen, a card on a phone: every cell carries its column name in
 * `data-label` for the stacked layout. Sent to is the client and the destination, by the names they
 * had when the job was made; a destination removed since is named by the category actually sent.
 */
function JobRow({ job, answeredAt, onCancelStart, onCancelError }: JobRowProps) {
  const { t } = useTranslation(['jobs', 'common'])
  const destination = job.destinationName ?? job.clientDestinationKey ?? t('noDestination')

  return (
    <tr>
      <td data-label={t('columns.release')}>
        <span className="settings-name">
          <Link className="settings-name-text library-link" to={`/jobs/${job.id}`}>{job.releaseTitle}</Link>
        </span>
      </td>
      <td data-label={t('columns.status')} className="jobs-stacked-cell">
        <JobStatus job={job} />
      </td>
      <td data-label={t('columns.progress')}>
        <JobProgress progress={job.progress} />
      </td>
      <td data-label={t('columns.sentTo')}>
        {[job.downloadClientName, destination].join(t('common:separator'))}
      </td>
      <td data-label={t('columns.sent')}>{formatDateTime(job.createdUtc, INSTANT_FORMAT)}</td>
      <td className="settings-actions-cell">
        <div className="settings-actions">
          {canCancelJob(job, answeredAt) && (
            <CancelJobButton job={job} onStart={onCancelStart} onError={onCancelError} />
          )}
        </div>
      </td>
    </tr>
  )
}
