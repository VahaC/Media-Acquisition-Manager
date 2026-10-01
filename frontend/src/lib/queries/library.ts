import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { libraryApi } from '../library-api'
import { isSyncUnderWay } from '../library-runs'
import type { LibrarySourceList, LibraryWorksQuery } from '../types'
import { qk } from './keys'

/**
 * MAM-8 — how often the sources are asked for while a sync is under way. A sync of 870 movies takes
 * seconds, and its "Syncing… 50 of 120" line would be useless at the 10 s of other live views.
 * MAM-9 — the same cadence while files are being inspected: one cheap request for the counts, and
 * one refresh of the works page at most when the counts moved (see `inspectionMoved`).
 */
const SYNC_POLL_MS = 2_000

/**
 * One page of the Library page's works, searched, filtered and paged by the server. The previous
 * page stays on screen while the next one loads, so typing a search doesn't blank the list.
 */
export const useLibraryWorks = (query: LibraryWorksQuery) =>
  useQuery({
    queryKey: qk.library.works(query),
    queryFn: () => libraryApi.works(query),
    placeholderData: keepPreviousData,
    // A 400 for a bad page or filter won't change on a second try.
    retry: false,
  })

/** The libraries that hold files, for the Library page's library filter. */
export const useLibraryLocations = () =>
  useQuery({
    queryKey: qk.library.locations,
    queryFn: () => libraryApi.locations(),
    retry: false,
  })

/**
 * The media library integrations, how their syncs went and how far the file inspection has got,
 * polled every 2 s while a sync or an inspection is under way.
 *
 * MAM-8 — when a run it last saw queued or running comes back Succeeded, the stored library has just
 * been replaced, so the works and the library filter are asked for again. That is decided here, on
 * the answer that brings the news, rather than in a component effect: nothing is refetched on the
 * first load, after a failed sync, or twice when two components show the sources.
 *
 * MAM-9 — an inspection writes each file's facts as it reads them, so the works are asked for again
 * whenever an answer shows it moved: more files checked by the run under way, or a run that has
 * finished since the last answer. While files are being read that is at most once per poll, and the
 * facts fill in on the cards as they come; once the run ends, the last of them arrive with it.
 */
export const useLibrarySources = () => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.library.sources,
    queryFn: async ({ signal }) => {
      const next = await libraryApi.sources(signal)
      // A poll cancelled by a newer request (Sync now invalidates the sources) never commits its
      // answer, so the cache below would still hold what it saw before: comparing with it would
      // refetch the works here and again in the newer request. The signal cancels the request
      // itself; this catches an answer that was already on its way.
      if (signal.aborted) return next
      // Read after the answer arrived: a sync started meanwhile has put its queued run in the cache.
      const previous = queryClient.getQueryData<LibrarySourceList>(qk.library.sources)
      const synced = finishedSyncing(previous, next)
      // One invalidation for both reasons: a second one in the same tick would cancel the first
      // request and send another.
      if (synced || inspectionMoved(previous, next)) {
        void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
      }
      if (synced) void queryClient.invalidateQueries({ queryKey: qk.library.locations })
      return next
    },
    refetchInterval: (query) => {
      const list = query.state.data
      const underWay = list !== undefined
        && (list.items.some((source) => isSyncUnderWay(source.latestRun)) || list.inspection.activeRun !== null)
      return underWay ? SYNC_POLL_MS : false
    },
    retry: false,
  })
}

/**
 * Starts a sync of one media library (owner-only).
 *
 * The queued run goes into the cached sources at once. Besides showing it, that is what the sources
 * query compares its next answer with: a small library can be synced before that answer arrives,
 * which would otherwise bring a Succeeded run the page never saw queued and leave the works as they
 * were. The sources are asked for again whatever the answer — a 409 usually means another sync of
 * it is under way, which the refetch then shows.
 */
export const useStartLibrarySync = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (integrationId: string) => libraryApi.startSync(integrationId),
    onSuccess: (run, integrationId) => {
      queryClient.setQueryData<LibrarySourceList>(qk.library.sources, (list) => list && {
        ...list,
        items: list.items.map((source) =>
          source.integrationId === integrationId ? { ...source, latestRun: run } : source),
      })
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: qk.library.sources }),
  })
}

/**
 * MAM-9 — starts an inspection of the library's files (owner-only).
 *
 * The queued run goes into the cached sources at once unless one is already under way there (a
 * running one stays on screen; the new one waits behind it). As with a sync, that is also what the
 * next answer is compared with, so a run that finishes before it arrives still brings the works
 * again. The sources are asked for again whatever the answer: a 409 means no media folder is
 * allowed any more, which the refetch then shows.
 */
export const useStartFileInspection = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => libraryApi.startInspection(),
    onSuccess: (run) => {
      queryClient.setQueryData<LibrarySourceList>(qk.library.sources, (list) => list && {
        ...list,
        // The run it queued (or joined) waits, behind the running one when there is one.
        inspection: { ...list.inspection, activeRun: list.inspection.activeRun ?? run, hasQueuedRun: true },
      })
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: qk.library.sources }),
  })
}

/** Whether a run `previous` showed queued or running is Succeeded in `next`. */
function finishedSyncing(previous: LibrarySourceList | undefined, next: LibrarySourceList): boolean {
  if (previous === undefined) return false
  return next.items.some((source) => {
    const run = source.latestRun
    if (run?.status !== 'Succeeded') return false
    const before = previous.items.find((item) => item.integrationId === source.integrationId)?.latestRun
    return before?.id === run.id && isSyncUnderWay(before)
  })
}

/**
 * MAM-9 — whether the inspection wrote file facts between `previous` and `next`: a run has finished
 * since (its last files), or the run under way in both has checked more files. A run that only
 * appeared, or moved from queued to running, has written nothing yet.
 */
function inspectionMoved(previous: LibrarySourceList | undefined, next: LibrarySourceList): boolean {
  if (previous === undefined) return false
  const before = previous.inspection
  const after = next.inspection
  if (before.lastFinishedRun?.id !== after.lastFinishedRun?.id) return true
  return before.activeRun !== null && after.activeRun !== null
    && before.activeRun.id === after.activeRun.id
    && before.activeRun.filesChecked !== after.activeRun.filesChecked
}
