import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { libraryApi } from '../library-api'
import { isSyncUnderWay } from '../library-runs'
import type { LibrarySourceList, LibraryWorksQuery } from '../types'
import { qk } from './keys'

/**
 * MAM-8 — how often the sources are asked for while a sync is under way. A sync of 870 movies takes
 * seconds, and its "Syncing… 50 of 120" line would be useless at the 10 s of other live views.
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
 * The media library integrations and how their syncs went, polled every 2 s while one is under way.
 *
 * MAM-8 — when a run it last saw queued or running comes back Succeeded, the stored library has just
 * been replaced, so the works and the library filter are asked for again. That is decided here, on
 * the answer that brings the news, rather than in a component effect: nothing is refetched on the
 * first load, after a failed sync, or twice when two components show the sources.
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
      if (finishedSyncing(previous, next)) {
        void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
        void queryClient.invalidateQueries({ queryKey: qk.library.locations })
      }
      return next
    },
    refetchInterval: (query) =>
      query.state.data?.items.some((source) => isSyncUnderWay(source.latestRun)) ? SYNC_POLL_MS : false,
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
