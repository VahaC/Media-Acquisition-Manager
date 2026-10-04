import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isJobFollowed } from '../download-jobs'
import { downloadsApi } from '../downloads-api'
import type { DownloadJob, DownloadJobFileList, DownloadJobFilesQuery, DownloadJobsQuery, SendDownloadRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-14 — how often a page that shows a followed job asks again: the server's own pass over the
 * client (it reads qBittorrent every 2 s, `DownloadJobTracker.PollInterval`), so asking more often
 * would only bring the same answer back.
 */
const JOBS_POLL_MS = 2_000

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
 * asks nothing more.
 */
export const useDownloadJobs = (query: DownloadJobsQuery) =>
  useQuery({
    queryKey: qk.downloads.jobs.list(query),
    queryFn: () => downloadsApi.list(query),
    placeholderData: keepPreviousData,
    refetchInterval: (current) => {
      const list = current.state.data
      return list !== undefined && list.items.some((job) => isJobFollowed(job.status)) ? JOBS_POLL_MS : false
    },
    // A 400 for a bad page or status won't change on a second try.
    retry: false,
  })

/**
 * MAM-14 — one of the caller's own jobs, for its details page; nothing is asked without an id (an
 * address that holds none). Asked again every 2 s while the app still follows it.
 *
 * When an answer shows the job moved — another status or progress than the cached one — its files
 * are asked for again, and so they are while a followed job has none listed yet (a magnet still
 * fetching its metadata). That is decided here, on the answer that brings the news, rather than by
 * polling the files on their own: the server reads a job's files again only when it moved or while
 * it has none, so they can't have changed otherwise, and a timer of their own stopped once the job
 * ended — before it could bring the files as the client finished them. The server commits a job's
 * status and progress together with its files, so a job seen moved already has its new files: the
 * files asked for on the answer that turned it Downloaded are its final ones.
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
      return next
    },
    enabled: id !== '',
    refetchInterval: (current) => {
      const job = current.state.data
      return job !== undefined && isJobFollowed(job.status) ? JOBS_POLL_MS : false
    },
    // A 404 for another user's job won't change on a second try.
    retry: false,
  })
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
    mutationFn: (id: string) => downloadsApi.cancel(id),
    networkMode: 'always',
    onSuccess: (job) => queryClient.setQueryData<DownloadJob>(qk.downloads.jobs.detail(job.id), job),
    onSettled: () => queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
  })
}
