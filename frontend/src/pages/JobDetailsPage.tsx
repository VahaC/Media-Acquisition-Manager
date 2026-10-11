import { useId, useState } from 'react'
import { isAxiosError } from 'axios'
import { ArrowLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router-dom'
import { CancelJobButton } from '@/components/jobs/CancelJobButton'
import { JobProgress } from '@/components/jobs/JobProgress'
import { JobStatus } from '@/components/jobs/JobStatus'
import { ReplacementSection } from '@/components/jobs/ReplacementSection'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { canCancelJob } from '@/lib/download-jobs'
import { formatDateTime, formatFileSize } from '@/lib/format'
import { useDownloadJob, useDownloadJobFiles } from '@/lib/queries'
import type { DownloadJob, DownloadJobFile } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, files table and its states
import '@/styles/search.css' // .search-details-facts — the job's facts, a name and its value per line
import '@/styles/jobs.css' // .jobs-* — the title and paths that break anywhere, the files heading

/** Per device: a phone and a desktop want different page lengths. */
const FILES_PAGE_SIZE_STORAGE_KEY = 'mam:jobFiles:pageSize'

/** An instant, as the Jobs list shows when a job was sent. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * MAM-14 — a job's id as the API makes it (a GUID). `:id` is the address's, so anyone can put
 * anything there; the id is encoded into one path segment (downloads-api.ts), but `..` itself would
 * still reach another API path, so the page sends no other value at all — SearchPage's rule for its
 * `?id=`. To the server an id that isn't one is the same as a missing job, so the page words it the
 * same way without asking.
 */
const JOB_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * MAM-14 — one of the signed-in user's own jobs: what was sent where (the release and its source,
 * the client, the destination and the category actually sent), where it stands and why it isn't
 * moving on, how far it has got, when it was sent and last changed status, the client's own id for
 * it, and its files as the client last listed them, paged. Cancel when the app still follows it, or
 * when it needs linking. MAM-26 — between the facts and the files, the old copies of the replacement
 * the job carries, with the owner's Retry, Restore and Delete for good (ReplacementSection): their
 * results in a green banner above the facts, their refusals in the red one Cancel uses.
 *
 * Another user's job, the installation owner's view of it included, is a bare 404 (MAM-14's
 * decision 1), worded like an address that holds no job. While the app follows the job, it is asked
 * for again every 2 s, and its files whenever it comes back moved.
 */
export function JobDetailsPage() {
  const { t } = useTranslation(['jobs', 'common'])
  const { id: addressId = '' } = useParams()
  const id = JOB_ID_PATTERN.test(addressId) ? addressId : ''
  const jobQuery = useDownloadJob(id)
  const { data: job, dataUpdatedAt, error, isLoading } = jobQuery
  const load = useLoadRetry(jobQuery)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionResult, setActionResult] = useState<string | null>(null)

  /** An action is about to be sent: the last one's news goes. */
  const startAction = () => {
    setActionError(null)
    setActionResult(null)
  }

  const body = (() => {
    // A bare 404 won't change on a second try: no Try again, and no server sentence to add.
    if (id === '' || (load.error && isGone(load.error))) return <ErrorState title={t('details.notFound')} />
    if (load.error) {
      return (
        <ErrorState title={t('details.loadFailed')} onRetry={load.retry} retrying={load.retrying}>
          {getApiErrorMessage(load.error)}
        </ErrorState>
      )
    }
    if (isLoading) return <p className="settings-muted">{t('common:loading')}</p>
    if (!job) return null
    return (
      <>
        <div className="settings-header">
          <div className="settings-intro">
            <h1 className="settings-title jobs-title">{job.releaseTitle}</h1>
          </div>
          {/* A Created job's age is judged at its answer's arrival, not by reading the clock in
              render: it is asked for again every 2 s, so Cancel appears within one poll of the
              job counting as interrupted. */}
          {canCancelJob(job, dataUpdatedAt) && (
            <div className="settings-actions">
              <CancelJobButton job={job} onStart={startAction} onError={setActionError} />
            </div>
          )}
        </div>

        {/* Outside the facts, so a failed refresh or a refused cancel adds a line above them
            instead of replacing them. A job removed meanwhile is a 404 like any other. MAM-26 — the
            old copies' actions answer here too. */}
        {error && (isGone(error) ? (
          <Banner>{t('details.notFound')}</Banner>
        ) : (
          <Banner action={<RetryButton onRetry={() => jobQuery.refetch()} />}>
            {t('details.refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        ))}
        {actionError && <Banner onDismiss={() => setActionError(null)}>{actionError}</Banner>}
        {actionResult && <Banner tone="ok" onDismiss={() => setActionResult(null)}>{actionResult}</Banner>}

        <JobFacts job={job} />
        {job.replacement && (
          <ReplacementSection
            replacement={job.replacement}
            onStart={startAction}
            onDone={setActionResult}
            onError={setActionError}
          />
        )}
        <JobFiles job={job} />
      </>
    )
  })()

  return (
    <div className="settings-page">
      <div>
        <Button asChild variant="outline" size="sm">
          <Link to="/jobs">
            <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
            {t('details.back')}
          </Link>
        </Button>
      </div>
      {body}
    </div>
  )
}

/** A bare 404: another user's job, or none — the same answer. */
function isGone(error: unknown): boolean {
  return isAxiosError(error) && error.response?.status === 404
}

// ── the facts ──

/**
 * The job's facts, as the Download dialog's summary lists what it sends. The names are the ones the
 * job copied when it was made, so they still read after the client or the destination is removed.
 */
function JobFacts({ job }: { job: DownloadJob }) {
  const { t } = useTranslation('jobs')
  const titleId = useId()
  const destination = job.destinationName ?? t('noDestination')

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('details.factsTitle')}</h2>
      </div>
      <div className="ui-card-content">
        <dl className="search-details-facts">
          <dt>{t('details.source')}</dt>
          <dd>{job.sourceName}</dd>
          <dt>{t('details.client')}</dt>
          <dd>{job.downloadClientName}</dd>
          <dt>{t('details.destination')}</dt>
          <dd>
            {job.clientDestinationKey === null
              ? destination
              : t('details.destinationValue', { name: destination, category: job.clientDestinationKey })}
          </dd>
          <dt>{t('details.status')}</dt>
          <dd><JobStatus job={job} /></dd>
          <dt>{t('details.progress')}</dt>
          <dd><JobProgress progress={job.progress} /></dd>
          <dt>{t('details.sent')}</dt>
          <dd>{formatDateTime(job.createdUtc, INSTANT_FORMAT)}</dd>
          <dt>{t('details.statusChanged')}</dt>
          <dd>{formatDateTime(job.statusChangedUtc, INSTANT_FORMAT)}</dd>
          <dt>{t('details.clientJobId')}</dt>
          <dd>
            {job.clientJobId === null
              ? t('details.clientJobIdUnknown')
              : <span className="jobs-client-id">{job.clientJobId}</span>}
          </dd>
        </dl>
      </div>
    </section>
  )
}

// ── the files ──

/**
 * The job's files as the client last listed them, by position and paged by the server: the path in
 * the job's folder exactly as the client names it, the size, and how far each has got — or Skipped,
 * for a file the client was told not to download. A magnet still fetching its metadata lists none
 * yet. Asked again whenever the job comes back moved (useDownloadJob), as their progress moves with it,
 * and every 2 s while their last load failed (useDownloadJobFiles), so a passing error clears itself.
 */
function JobFiles({ job }: { job: DownloadJob }) {
  const { t } = useTranslation(['jobs', 'common'])
  const titleId = useId()
  const [pageSize, setPageSize] = usePageSize(FILES_PAGE_SIZE_STORAGE_KEY)
  const [page, setPage] = useState(1)
  const files = useDownloadJobFiles(job.id, { page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = files
  // MAM-16 — files that failed are asked for again every 2 s: the failure stays on screen through
  // those polls rather than blinking to "Loading…" each time.
  const load = useLoadRetry(files)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A client that lists fewer files than before (a torrent re-added) leaves the page past the last.
  if (data && !isPlaceholderData && page > pages) setPage(pages)

  const changePageSize = (next: number) => {
    setPageSize(next)
    setPage(1)
  }

  return (
    <section className="jobs-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="jobs-section-title">{t('files.title')}</h2>

      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => files.refetch()} />}>
          {t('files.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {load.error ? (
        <ErrorState
          title={t('files.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 ? (
        <div className="settings-empty">
          <p>{t('files.empty')}</p>
        </div>
      ) : data ? (
        <>
          <div className="settings-table-wrap">
            <table className="settings-table">
              <thead>
                <tr>
                  <th scope="col">{t('files.columns.path')}</th>
                  <th scope="col">{t('files.columns.size')}</th>
                  <th scope="col">{t('files.columns.progress')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((file) => <FileRow key={file.position} file={file} />)}
              </tbody>
            </table>
          </div>

          <ListPager
            page={page}
            pages={pages}
            pageSize={pageSize}
            pageSizes={PAGE_SIZES}
            totalCount={data.totalCount}
            onPage={setPage}
            onPageSize={changePageSize}
          />
        </>
      ) : null}
    </section>
  )
}

/** One file: a table row on a wide screen, a card on a phone, the path above its values there. */
function FileRow({ file }: { file: DownloadJobFile }) {
  const { t } = useTranslation('jobs')

  return (
    <tr>
      <td data-label={t('files.columns.path')} className="jobs-stacked-cell">
        <span className="jobs-file-path">{file.path}</span>
      </td>
      <td data-label={t('files.columns.size')}>
        {file.sizeBytes === null ? t('files.sizeUnknown') : formatFileSize(file.sizeBytes)}
      </td>
      <td data-label={t('files.columns.progress')}>
        {file.isWanted === false
          ? <span className="ui-badge ui-badge-secondary" title={t('files.skippedTitle')}>{t('files.skipped')}</span>
          : <JobProgress progress={file.progress} />}
      </td>
    </tr>
  )
}
