import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { replacementsApi } from '../replacements-api'
import type { ScheduleReplacementRequest } from '../types'
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
