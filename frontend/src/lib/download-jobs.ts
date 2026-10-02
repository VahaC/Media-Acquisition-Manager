import type { DownloadJob, DownloadJobStatus } from './types'

/**
 * MAM-14 — every job status, in the order the API lists them: the Jobs page's status filter offers
 * them in this order.
 */
export const DOWNLOAD_JOB_STATUSES: readonly DownloadJobStatus[] = [
  'Created',
  'Submitted',
  'Downloading',
  'Paused',
  'Downloaded',
  'Failed',
  'NeedsLinking',
  'Cancelled',
]

/**
 * MAM-14 — the statuses the app still follows by reading the client (ADR 0014). A page that shows a
 * job in one of them asks again every 10 s, as the job can move on by itself; one whose jobs have all
 * ended asks nothing more. `Created` is in: the server follows one left by an interrupted send.
 */
const FOLLOWED_STATUSES: ReadonlySet<DownloadJobStatus> = new Set<DownloadJobStatus>([
  'Created',
  'Submitted',
  'Downloading',
  'Paused',
])

/** Whether the app still follows a job in `status`, so it can change without anyone acting. */
export function isJobFollowed(status: DownloadJobStatus): boolean {
  return FOLLOWED_STATUSES.has(status)
}

/**
 * MAM-14 — how old a `Created` job must be before Cancel is offered on it: the server's
 * `DownloadJobPoller.InterruptedSendAge`. A younger one may still be being sent, and the server
 * refuses to cancel it (409) so the cancel can't race the send. The server stays the authority: a
 * browser clock that runs ahead at worst shows a button the server answers with its sentence.
 */
const INTERRUPTED_SEND_AGE_MS = 5 * 60_000

/**
 * Whether `job` can be cancelled at `now` (ms since the epoch): a followed one, or one that needs
 * linking — cancelling is the only way to dismiss it, so its release can be sent again. A `Created`
 * one only once its send counts as interrupted (INTERRUPTED_SEND_AGE_MS): before that it is still
 * being sent. The server decides (a job that ended meanwhile answers 409); this only decides whether
 * the row offers Cancel.
 */
export function canCancelJob(job: Pick<DownloadJob, 'status' | 'createdUtc'>, now: number): boolean {
  if (job.status === 'Created') return now - Date.parse(job.createdUtc) >= INTERRUPTED_SEND_AGE_MS
  return isJobFollowed(job.status) || job.status === 'NeedsLinking'
}
