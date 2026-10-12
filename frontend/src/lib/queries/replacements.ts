import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { replacementsApi } from '../replacements-api'
import type { DownloadJob, ScheduleReplacementRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-24 — what of a release the installation owner may replace through a destination, for the
 * Download dialog. Asked only for the owner with a destination picked (`enabled`): the route is
 * owner-only. Without `holdings` the answer decides whether Replace can be picked and lists the
 * copies; with it, once Replace is picked, the server also reads every download client's torrents
 * for the held marks (ADR 0031) — never for a send that keeps both.
 *
 * The last answer stays on screen while the next one loads (another destination, the held marks):
 * the list doesn't vanish under the person's eyes, and the dialog holds Send while it is only a
 * stand-in. Asked even while the browser says it is offline (`networkMode: 'always'`), and not
 * retried, as the dialog's other reads: a 404 for an expired release won't change on a second try.
 */
export const useReplacementOptions = (releaseId: string, destinationId: string, holdings: boolean, enabled: boolean) =>
  useQuery({
    queryKey: qk.replacements.options(releaseId, destinationId, holdings),
    queryFn: () => replacementsApi.options(releaseId, destinationId, holdings),
    enabled: enabled && destinationId !== '',
    placeholderData: keepPreviousData,
    retry: false,
    networkMode: 'always',
  })

/** The answers that refuse a schedule over the copies or the ticks: the list the owner ticked is out of date. */
const LIST_REFUSALS: ReadonlySet<number> = new Set([400, 409])

/**
 * MAM-24 — sends a release with the replacement of the copies the owner ticked. Never retried on its
 * own, and sent even while the browser says it is offline, as a download is (useSendDownload). Every
 * list of jobs is asked for again once it answers: the Jobs page lists the new job.
 *
 * A 400 or 409 means a ticked copy changed since the list was read (it can't be replaced any more, or
 * isn't the work's): the release's answers for that destination are read again, with the held marks
 * and without, so the dialog shows the copy's reason rather than a list that would be refused again.
 */
export const useScheduleReplacement = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ScheduleReplacementRequest) => replacementsApi.schedule(request),
    networkMode: 'always',
    // Not awaited: the dialog shows what happened at once rather than after a list nobody sees here.
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.lists }) },
    onError: (error, request) => {
      const status = isAxiosError(error) ? error.response?.status : undefined
      if (status === undefined || !LIST_REFUSALS.has(status)) return
      for (const holdings of [false, true]) {
        void queryClient.invalidateQueries({ queryKey: qk.replacements.options(request.releaseId, request.destinationId, holdings) })
      }
    },
  })
}

/**
 * MAM-26 — one of the owner's replacements, for its job's page: every name its move planned and
 * where each is now, the room it takes in the quarantine, and what the owner may do. Asked only for
 * the owner (`enabled`): the route is owner-only. The job's own query asks for it again whenever the
 * replacement it carries comes back changed (useDownloadJob), and each action here asks for it again
 * once it answers. Not retried: a 404 for a replacement gone won't change on a second try.
 *
 * MAM-27 — and its copies, new files and check. While the check runs, what it found of the new files
 * changes on every pass with nothing on the job's line changing, so the caller passes the Jobs
 * cadence (`refetchEveryMs`) for as long as the check runs; otherwise it isn't polled.
 */
export const useReplacement = (id: string, enabled: boolean, refetchEveryMs: number | false = false) =>
  useQuery({
    queryKey: qk.replacements.job(id),
    queryFn: () => replacementsApi.get(id),
    enabled: enabled && id !== '',
    refetchInterval: refetchEveryMs,
    retry: false,
  })

/** The arguments of a Delete for good: the replacement, and the names the owner confirmed. */
interface DeleteForGoodVariables {
  id: string
  names: string[]
}

/** MAM-27 — the arguments of a Drop: the replacement, and the copy the owner takes out of it. */
interface DropCopyVariables {
  id: string
  copyId: string
}

/**
 * Puts the job an owner's action answered with in place at once, as a cancel does. Each action then
 * asks again for every list, job and file page and the replacement's details whatever it answered —
 * a 409 means the replacement changed meanwhile, which the refetch shows —, and stays pending until
 * those are back, so its buttons stay disabled until the page shows the new state. MAM-27 — each
 * carries the replacement's mutation key, so a Jobs row holds its other actions meanwhile
 * (useJobActionsPending).
 */
const settleJob = (queryClient: QueryClient, job: DownloadJob) =>
  queryClient.setQueryData<DownloadJob>(qk.downloads.jobs.detail(job.id), job)

/**
 * MAM-26 — sends a replacement the mover stopped for review back to be checked and moved (ADR 0031:
 * "remove that torrent there without its files, then press Retry"). MAM-27 — or one the check of its
 * new files stopped back to that check. The request itself writes nothing to the media, so the
 * quarantine's room is left alone. Never retried on its own, and sent even while the browser says it
 * is offline (`networkMode: 'always'`), as a cancel is: held back, it would go out on its own later,
 * after the owner may have left the page.
 */
export const useRetryReplacement = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: qk.actions.replacement,
    mutationFn: (id: string) => replacementsApi.retry(id),
    networkMode: 'always',
    onSuccess: (job) => settleJob(queryClient, job),
    onSettled: (_, __, id) => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
      queryClient.invalidateQueries({ queryKey: qk.replacements.job(id) }),
    ]),
  })
}

/**
 * MAM-27 — the owner's Keep both: cancels only the replacement, so both copies stay and its download
 * goes on as it is. Nothing of it is in the quarantine (the server refuses otherwise), so its room is
 * left alone. Never retried on its own, and sent even while the browser says it is offline
 * (useRetryReplacement's reason).
 */
export const useCancelReplacement = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: qk.actions.replacement,
    mutationFn: (id: string) => replacementsApi.cancel(id),
    networkMode: 'always',
    onSuccess: (job) => settleJob(queryClient, job),
    onSettled: (_, __, id) => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
      queryClient.invalidateQueries({ queryKey: qk.replacements.job(id) }),
    ]),
  })
}

/**
 * MAM-27 — takes one ticked copy out of the owner's replacement: it stays where it is, and nothing
 * else happens until the owner presses Retry. A copy that left its folder can't be dropped, so the
 * quarantine's room is left alone. Never retried on its own, and sent even while the browser says it
 * is offline (useRetryReplacement's reason); `variables.copyId` tells which row waits.
 */
export const useDropReplacementCopy = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: qk.actions.replacement,
    mutationFn: ({ id, copyId }: DropCopyVariables) => replacementsApi.dropCopy(id, copyId),
    networkMode: 'always',
    onSuccess: (job) => settleJob(queryClient, job),
    onSettled: (_, __, { id }) => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
      queryClient.invalidateQueries({ queryKey: qk.replacements.job(id) }),
    ]),
  })
}

/**
 * MAM-26 — puts every name of the owner's replacement in the quarantine back where it was (ADR 0030).
 * Once it answers, the job, its details and the Media folders' quarantine room are asked for again,
 * whatever the answer: a refusal part-way still brought some names back. Never retried on its own,
 * and sent even while the browser says it is offline (useRetryReplacement's reason). MAM-40 — and the
 * Duplicates page: a copy back in its folder counts in its group again, and a replacement that ended no
 * longer holds its files from Remove this copy.
 */
export const useRestoreReplacement = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: qk.actions.replacement,
    mutationFn: (id: string) => replacementsApi.restore(id),
    networkMode: 'always',
    onSuccess: (job) => settleJob(queryClient, job),
    onSettled: (_, __, id) => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
      queryClient.invalidateQueries({ queryKey: qk.replacements.job(id) }),
      queryClient.invalidateQueries({ queryKey: qk.settings.allowedRoots }),
      queryClient.invalidateQueries({ queryKey: qk.library.duplicatesAll }),
    ]),
  })
}

/**
 * MAM-26 — deletes for good exactly the names the owner confirmed from the replacement's quarantine
 * (ADR 0030); the server refuses the list if it isn't what the quarantine holds now. Then as Restore:
 * the job, its details, the quarantine room and (MAM-40) the Duplicates page are asked for again
 * whatever the answer — a replacement that ended no longer holds its files from Remove this copy.
 * Never retried on its own, and sent even while the browser says it is offline
 * (useRetryReplacement's reason).
 */
export const useDeleteReplacementForGood = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: qk.actions.replacement,
    mutationFn: ({ id, names }: DeleteForGoodVariables) => replacementsApi.deleteForGood(id, names),
    networkMode: 'always',
    onSuccess: (job) => settleJob(queryClient, job),
    onSettled: (_, __, { id }) => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.downloads.jobs.all }),
      queryClient.invalidateQueries({ queryKey: qk.replacements.job(id) }),
      queryClient.invalidateQueries({ queryKey: qk.settings.allowedRoots }),
      queryClient.invalidateQueries({ queryKey: qk.library.duplicatesAll }),
    ]),
  })
}
