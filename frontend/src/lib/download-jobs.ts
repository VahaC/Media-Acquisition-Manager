import type { DownloadJob, DownloadJobStatus, ReplacementJobState } from './types'

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
 * job in one of them asks again every 2 s, as the job can move on by itself; one whose jobs have all
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
 * MAM-25 — the replacement states the check of the new files moves on by itself once the download
 * is done: Downloaded until the next check pass takes the job up, then Inspecting while its new files
 * are read, until the check ends it Verified, NeedsReview or Failed.
 */
const CHECKED_REPLACEMENT_STATES: ReadonlySet<ReplacementJobState> = new Set<ReplacementJobState>([
  'Downloaded',
  'Inspecting',
])

/** Whether a replacement in `state` has its new files checked, now or at the next check pass. */
export function isReplacementBeingChecked(state: ReplacementJobState): boolean {
  return CHECKED_REPLACEMENT_STATES.has(state)
}

/**
 * Whether the app is still working on `job` — following its download, or checking the new files of
 * the replacement it carries — so it can change without anyone acting, and a page that shows it asks
 * again. The download stays Downloaded while the check runs, so its status alone would stop the
 * polling before the page could show how the check ended.
 */
export function isJobWorkedOn(job: Pick<DownloadJob, 'status' | 'replacement'>): boolean {
  return isJobFollowed(job.status) || (job.replacement !== null && isReplacementBeingChecked(job.replacement.state))
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
