import { CircleAlert, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { JobProgress } from '@/components/jobs/JobProgress'
import { STATUS_ICONS, statusTone } from '@/components/jobs/job-status-look'
import { isJobFollowed } from '@/lib/download-jobs'
import { formatProgress } from '@/lib/format'
import type { DownloadJob } from '@/lib/types'
import { cn } from '@/lib/utils'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the status and its one line
import '@/styles/jobs.css' // .jobs-status, .jobs-clamp — the lines as one block, cut at two lines

/**
 * MAM-41 — the Download cell of a Jobs row: where the download stands, in a word and at most one short
 * line, so a row stays a row. The bar only while the app still follows the download — a downloaded one
 * needs none, and a cancelled one says in its word how far it got ("Cancelled at 45.6%", cut down as the
 * bar's percentage is). Then why a followed job isn't moving on, or why it failed, cut at two lines with
 * the whole sentence on hover; the row's details (JobRowDetails) and the job's page show it in full.
 *
 * The colours are JobStatus's (job-status-look): a green icon for Downloaded, a red line for Failed, an
 * amber icon for a job that needs linking.
 */
export function JobDownloadSummary({ job }: { job: DownloadJob }) {
  const { t } = useTranslation('jobs')
  const Icon = STATUS_ICONS[job.status]
  const spinning = job.status === 'Downloading' && job.blockReason === null
  // A job cancelled once it was done says nothing more than Cancelled; nor does one the client never reported on.
  const label = job.status === 'Cancelled' && job.progress !== null && job.progress < 1
    ? t('row.cancelledAt', { progress: formatProgress(job.progress) })
    : t(`status.${job.status}`)

  return (
    <div className="jobs-status">
      {job.status === 'NeedsLinking' ? (
        <p className="library-source-warning">
          <Icon className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{label}</span>
        </p>
      ) : (
        <p className="library-source-status" data-status={statusTone(job.status)}>
          <Icon className={cn('h-3.5 w-3.5', spinning && 'animate-spin')} aria-hidden="true" />
          <span>{label}</span>
        </p>
      )}
      {isJobFollowed(job.status) && <JobProgress progress={job.progress} />}
      {job.blockReason && (
        <p className="library-source-warning jobs-clamp" title={job.blockReason}>
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{job.blockReason}</span>
        </p>
      )}
      {job.error && (
        <p className="library-source-status jobs-clamp" data-status="Failed" title={job.error}>
          <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{job.error}</span>
        </p>
      )}
    </div>
  )
}
