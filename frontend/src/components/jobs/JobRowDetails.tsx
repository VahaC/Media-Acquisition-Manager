import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { JobStatus } from '@/components/jobs/JobStatus'
import { ReplacementCopiesPanel } from '@/components/jobs/ReplacementCopiesPanel'
import type { DownloadJob, DownloadJobReplacement } from '@/lib/types'
import '@/styles/library.css' // .library-link — the link to the job's page
import '@/styles/jobs.css' // .jobs-row-details — the details as one column

interface JobRowDetailsProps {
  job: DownloadJob
  /** The replacement the owner sees on the row; null for a member's row or a job without one. */
  replacement: DownloadJobReplacement | null
  /** Another action on the row's job or replacement is on its way (the row's one busy flag): every Drop waits. */
  busy: boolean
  /** A drop was confirmed and is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** A drop was refused or failed: the page shows `message` in the banner above the rows. */
  onError: (message: string) => void
}

/**
 * MAM-41 — what a Jobs row's chevron opens under it: every sentence the row's cells cut short or leave out
 * (why the download isn't moving on or failed, where the replacement stands and what to do, why the
 * release went unchecked — JobStatus, its sentences alone), the replacement's old copies with their new
 * files and Drop (ReplacementCopiesPanel, the owner's only), then where the release came from, the
 * client's id for it (its first 8 characters: the job's page has it whole), and the way to the job's page.
 *
 * Copies was details, not an action (Vasyl, 2026-10-12): MAM-27's Copies button left the row's actions
 * for this. Mounted only while the row is open, so a closed list asks for no replacement's details.
 */
export function JobRowDetails({ job, replacement, busy, onStart, onError }: JobRowDetailsProps) {
  const { t } = useTranslation(['jobs', 'common'])
  const facts = [
    t('row.source', { source: job.sourceName }),
    job.clientJobId === null
      ? t('row.noClientId', { client: job.downloadClientName })
      // A 40- or 64-character hash would take the line: its start tells the torrent apart in the client.
      : t('row.clientId', { client: job.downloadClientName, id: job.clientJobId.slice(0, 8) }),
  ].join(t('common:separator'))

  return (
    <div className="jobs-row-details">
      <JobStatus job={{ ...job, replacement }} sentencesOnly />
      {replacement && <ReplacementCopiesPanel replacement={replacement} busy={busy} onStart={onStart} onError={onError} />}
      <p className="ui-hint">
        {facts}{t('common:separator')}
        <Link className="library-link" to={`/jobs/${job.id}`}>{t('row.openJob')}</Link>
      </p>
    </div>
  )
}
