import { keepPreviousData, useIsMutating, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { isJobFollowed, isJobWorkedOn } from '../download-jobs'
import { downloadsApi } from '../downloads-api'
import type {
  DownloadJob,
  DownloadJobFileList,
  DownloadJobFilesQuery,
  DownloadJobList,
  DownloadJobReplacement,
  DownloadJobsQuery,
  SendDownloadRequest,
} from '../types'
import { qk } from './keys'

/**
 * MAM-14 — how often a page that shows a followed job asks again: the server's own pass over the
 * client (it reads qBittorrent every 2 s, `DownloadJobTracker.PollInterval`), so asking more often
 * would only bring the same answer back. MAM-25 — the same while only a replacement's check runs:
 * its passes are further apart (`ReplacementVerificationTracker.PassInterval`, 15 s), but one cadence
 * keeps a page simple, and the line shows how the check ended within 2 s of the pass that ended it.
 * MAM-26 — the same while the mover's pass (also 15 s) takes a verified replacement up and moves it.
 * MAM-27 — and for a replacement's details while its check runs (useReplacement's callers), which is
 * why it is exported.
 */
export const JOBS_POLL_MS = 2_000

/**
 * MAM-13 — the download clients and destinations a release can be sent to, for the Download dialog.
 * Asked again each time the dialog opens: the owner may have just added a client or a destination.
 *
 * Asked even while the browser says it is offline (`networkMode: 'always'`): a query TanStack held
 * back would leave the dialog with no data and no error, and it would tell the person no client can
 * take the release, when only the network is down. Asked, it fails into the dialog's error line.
 */
export const useDownloadOptions = (releaseId: string) =>
  useQuery({
    queryKey: qk.downloads.options(releaseId),
    queryFn: () => downloadsApi.options(releaseId),
    // A 404 for an expired release won't change on a second try.
    retry: false,
    networkMode: 'always',
  })

/**
 * MAM-38 — whether a release sent to the destination is checked for files it would write over, for the
 * Download dialog's warning before Send. Asked once a destination is picked, and not retried: the
 * warning is a hint, the send checks again, and a failed question shows nothing rather than a wrong
 * warning.
 *
 * Asked even while the browser says it is offline (`networkMode: 'always'`), as the options and the
 * send are: a question TanStack held back would leave the dialog with no data and no error, so a
 * folder that can't be checked would go without its warning while Send still goes out. Asked, it
 * either answers or fails into warning of nothing.
 */
export const useDownloadOverwriteCheck = (destinationId: string) =>
  useQuery({
    queryKey: qk.downloads.overwriteCheck(destinationId),
    queryFn: () => downloadsApi.overwriteCheck(destinationId),
    enabled: destinationId !== '',
    retry: false,
    networkMode: 'always',
  })

/**
 * Sends a release to a download client. Never retried on its own (mutations aren't by default): the
 * request id keeps a resend from becoming a second download, but only the person's own Send resends.
 * MAM-14 — the Jobs page lists the new job, so every list is asked for again once a send answers.
 *
 * Sent even while the browser says it is offline (`networkMode: 'always'`): TanStack would otherwise
 * hold the send back and send it on its own once the browser is back online — after the person may
 * have closed the dialog, sending a download nobody sees. Sent, it fails at once into the dialog's
 * error line, and the person's own Send repeats it under the same request id.
 */
export const useSendDownload = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SendDownloadRequest) => downloadsApi.send(request),
    networkMode: 'always',
    // Not awaited: the dialog shows what happened at once rather than after a list nobody sees here.
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.lists }) },
  })
}

/**
 * MAM-14 — one page of the caller's own jobs, filtered and paged by the server. The previous page
 * stays on screen while the next one loads. Asked again every 2 s while the page shows a job the
 * app still follows, as its status and progress move without anyone acting; a page of ended jobs
 * asks nothing more. MAM-25 — and while it shows a downloaded job whose replacement's new files are
 * still being checked, as the check ends on its own; MAM-26 — or whose old copies are about to move
 * or moving (isReplacementWorkedOn).
 *
 * MAM-27 — an owner's row can open its replacement's copies (useReplacement) under it. As on the job's
 * page (useDownloadJob), those details are asked for again on the answer that brings a replacement
 * changed, rather than on a timer of their own: a check that ends, or a mover that takes the copies up,
 * would otherwise leave an opened row showing them as they were. Only opened rows ask; another row's
 * details, if cached, are only marked stale.
 */
export const useDownloadJobs = (query: DownloadJobsQuery) => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.downloads.jobs.list(query),
    queryFn: async ({ signal }) => {
      const next = await downloadsApi.list(query)
      // As useDownloadJob: a poll cancelled by a newer request never commits its answer.
      if (signal.aborted) return next
      const previous = queryClient.getQueryData<DownloadJobList>(qk.downloads.jobs.list(query))
      if (previous !== undefined) askAgainForChangedReplacements(queryClient, previous.items, next.items)
      return next
    },
    placeholderData: keepPreviousData,
    refetchInterval: (current) => {
      const list = current.state.data
      return list !== undefined && list.items.some(isJobWorkedOn) ? JOBS_POLL_MS : false
    },
    // A 400 for a bad page or status won't change on a second try.
    retry: false,
  })
}

/** Asks again for the details of each replacement on `next` that came back other than it was on `previous`. */
function askAgainForChangedReplacements(queryClient: QueryClient, previous: DownloadJob[], next: DownloadJob[]) {
  const before = new Map(previous.map((job) => [job.id, job.replacement]))
  for (const job of next) {
    if (job.replacement === null || !before.has(job.id)) continue
    if (replacementChanged(before.get(job.id) ?? null, job.replacement)) {
      void queryClient.invalidateQueries({ queryKey: qk.replacements.job(job.replacement.id) })
    }
  }
}

/**
 * MAM-14 — one of the caller's own jobs, for its details page; nothing is asked without an id (an
 * address that holds none). Asked again every 2 s while the app still follows it, or (MAM-25) still
 * checks the new files of the replacement it carries, or (MAM-26) moves its old copies.
 *
 * When an answer shows the job moved — another status or progress than the cached one — its files
 * are asked for again, and so they are while a followed job has none listed yet (a magnet still
 * fetching its metadata). That is decided here, on the answer that brings the news, rather than by
 * polling the files on their own: the server reads a job's files again only when it moved or while
 * it has none, so they can't have changed otherwise, and a timer of their own stopped once the job
 * ended — before it could bring the files as the client finished them. The server commits a job's
 * status and progress together with its files, so a job seen moved already has its new files: the
 * files asked for on the answer that turned it Downloaded are its final ones.
 *
 * MAM-26 — the same holds for the replacement's details on the job's page (useReplacement): they are
 * asked for again on the answer whose replacement changed — another state, copies moved, files that
 * stayed, Retry offered or the switch turned —, never on a timer of their own. MAM-27 — except while
 * its check runs, when the page itself asks every 2 s: what the check finds changes on every pass with
 * nothing on the job's line changing.
 */
export const useDownloadJob = (id: string) => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.downloads.jobs.detail(id),
    queryFn: async ({ signal }) => {
      const next = await downloadsApi.get(id)
      // A poll cancelled by a newer request (a cancel invalidates the job) never commits its answer;
      // comparing it with the cache would ask for the files here and again in the newer request.
      if (signal.aborted) return next
      const previous = queryClient.getQueryData<DownloadJob>(qk.downloads.jobs.detail(id))
      const moved = previous !== undefined && (previous.status !== next.status || previous.progress !== next.progress)
      const awaitingFiles = isJobFollowed(next.status) && queryClient
        .getQueriesData<DownloadJobFileList>({ queryKey: qk.downloads.jobs.allFiles(id) })
        .some(([, files]) => files?.totalCount === 0)
      if (moved || awaitingFiles) void queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.allFiles(id) })
      if (previous !== undefined && next.replacement !== null && replacementChanged(previous.replacement, next.replacement)) {
        void queryClient.invalidateQueries({ queryKey: qk.replacements.job(next.replacement.id) })
      }
      return next
    },
    enabled: id !== '',
    refetchInterval: (current) => {
      const job = current.state.data
      return job !== undefined && isJobWorkedOn(job) ? JOBS_POLL_MS : false
    },
    // A 404 for another user's job won't change on a second try.
    retry: false,
  })
}

/**
 * Whether a job's replacement came back other than the cached one in what its details show. MAM-27 —
 * also a copy dropped (the counts), Keep both offered or no more, and who stopped it or where its check
 * is: a drop, or a check that moved on to Jellyfin, leaves the state as it was.
 */
function replacementChanged(previous: DownloadJobReplacement | null, next: DownloadJobReplacement): boolean {
  return previous === null
    || previous.state !== next.state
    || previous.switchedOff !== next.switchedOff
    || previous.quarantinedCopyCount !== next.quarantinedCopyCount
    || previous.stayedFileCount !== next.stayedFileCount
    || previous.canRetry !== next.canRetry
    || previous.copyCount !== next.copyCount
    || previous.droppedCopyCount !== next.droppedCopyCount
    || previous.dropOnlyCopyCount !== next.dropOnlyCopyCount
    || previous.canCancel !== next.canCancel
    || previous.stop !== next.stop
    || previous.checkStage !== next.checkStage
}

/**
 * MAM-14 — one page of a job's files, by position, as the client last listed them. Not polled while
 * they load: the job's own query asks for them again whenever the job moves (useDownloadJob).
 *
 * Asked again every 2 s only while this query's last fetch failed, and no more once one succeeds:
 * the job's query asks for them only on a move, so a failed load on a job that stays put (Paused, or
 * held by a block reason) or on one that just ended would otherwise stay failed until the page is
 * opened again — the answer is never retried (`retry: false`), and nothing refetches on focus.
 */
export const useDownloadJobFiles = (id: string, query: DownloadJobFilesQuery) =>
  useQuery({
    queryKey: qk.downloads.jobs.files(id, query),
    queryFn: () => downloadsApi.files(id, query),
    enabled: id !== '',
    placeholderData: keepPreviousData,
    refetchInterval: (current) => (current.state.status === 'error' ? JOBS_POLL_MS : false),
    // Not retried at once: a 400 for a bad page or a 404 for a job gone won't change on a second
    // try. Any failure is asked for again on the poll above instead, at the jobs' own cadence: a
    // passing one then clears, and a lasting one costs one request per 2 s while the page is open.
    retry: false,
  })

/**
 * MAM-14 — stops following one of the caller's own jobs. Nothing is sent to the client: the torrent
 * and its files stay there. The answer replaces the cached job at once; then every list, job and
 * file page is asked for again whatever the answer — a 409 means the job ended meanwhile, which the
 * refetch then shows. The mutation stays pending until that refetch is back, so the row's button
 * stays disabled until the row shows the new status.
 *
 * Sent even while the browser says it is offline (`networkMode: 'always'`), like a send: held back,
 * it would leave the button waiting with no error, and go out on its own later, after the person
 * may have left the page.
 */
export const useCancelDownload = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: qk.actions.cancelJob,
    mutationFn: (id: string) => downloadsApi.cancel(id),
    networkMode: 'always',
    onSuccess: (job) => queryClient.setQueryData<DownloadJob>(qk.downloads.jobs.detail(job.id), job),
    onSettled: () => queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
  })
}

/** The replacement an owner's action was sent for: its id is the variables, or their `id`. */
function replacementIdOf(variables: unknown): string | null {
  if (typeof variables === 'string') return variables
  return typeof variables === 'object' && variables !== null && 'id' in variables && typeof variables.id === 'string'
    ? variables.id
    : null
}

/**
 * MAM-27 — whether an action on the job `jobId`, or on the replacement `replacementId` it carries, is
 * on its way, whichever button sent it: a Jobs row's Cancel job, Retry and Keep both, or a Drop from
 * the Copies opened under it. The row holds every other one meanwhile — one busy flag, as the job's
 * page's Replacement section has —, as a second would be refused, or act on a replacement the first
 * just changed (a Retry that goes first sends a copy the owner was dropping back to the check).
 */
export const useJobActionsPending = (jobId: string, replacementId: string | null) => {
  const cancelling = useIsMutating({ mutationKey: qk.actions.cancelJob, predicate: (mutation) => mutation.state.variables === jobId })
  const acting = useIsMutating({
    mutationKey: qk.actions.replacement,
    predicate: (mutation) => replacementId !== null && replacementIdOf(mutation.state.variables) === replacementId,
  })
  return cancelling + acting > 0
}
