import { CircleAlert, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { STATUS_ICONS, statusTone } from '@/components/jobs/job-status-look'
import { ReplacementLine } from '@/components/jobs/ReplacementLine'
import type { DownloadJob } from '@/lib/types'
import { cn } from '@/lib/utils'
import '@/styles/library.css' // .library-source-status, .library-source-warning — the status, reason and error lines
import '@/styles/jobs.css' // .jobs-status — the lines as one block

/**
 * MAM-14 — where a job stands, as the Jobs list and the details page show it: the status in words,
 * then why a followed job isn't moving on (its block reason, an amber warning line: the job keeps
 * its status meanwhile, MAM-14's decisions 2 and 3), then why it failed (a red line). Both sentences
 * are the server's, shown as they are. MAM-38 — then, for a job sent without the check for files it
 * would write over, why it went unchecked (an amber line, from the locale) — never on a Failed job,
 * though one whose submission failed after the check still carries the reason. MAM-24 — after the
 * error, the replacement the job carries, if any (ReplacementLine).
 *
 * The colours follow the Library's status lines: a green icon for Downloaded, a red line for
 * Failed, an amber icon for a job that needs linking; amber never colours text (contrast).
 */
export function JobStatus({ job, onJobPage = false, sentencesOnly = false }: {
  job: DownloadJob
  /** MAM-27 — it is the job's own page, whose Replacement section says what to do (ReplacementLine). */
  onJobPage?: boolean
  /**
   * MAM-41 — it is a Jobs row's details (JobRowDetails), under cells that already show the status: the
   * sentences alone, in full, as the cells cut them short.
   */
  sentencesOnly?: boolean
}) {
  const { t } = useTranslation('jobs')
  const Icon = STATUS_ICONS[job.status]
  const label = t(`status.${job.status}`)
  const spinning = job.status === 'Downloading' && job.blockReason === null
  // A Failed job's red line is the whole story: "not checked" beside it would only be noise.
  const notChecked = job.status !== 'Failed' && job.overwriteCheck !== null && job.overwriteCheck !== 'Checked'
    ? job.overwriteCheck
    : null
  // No empty block in the row's details, which would leave a gap above what follows it.
  if (sentencesOnly && !job.blockReason && !job.error && !job.replacement && !notChecked) return null

  return (
    <div className="jobs-status">
      {sentencesOnly ? null : job.status === 'NeedsLinking' ? (
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
      {job.blockReason && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{job.blockReason}</span>
        </p>
      )}
      {job.error && (
        <p className="library-source-status" data-status="Failed">
          <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{job.error}</span>
        </p>
      )}
      {job.replacement && (
        <ReplacementLine
          replacement={job.replacement}
          client={job.downloadClientName}
          downloadFailed={job.status === 'Failed'}
          onJobPage={onJobPage}
        />
      )}
      {notChecked && (
        <p className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{t(`overwriteCheck.${notChecked}`)}</span>
        </p>
      )}
    </div>
  )
}
