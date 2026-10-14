import { keepPreviousData, skipToken, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { dataUrlOf } from '../data-url'
import { recommendationsApi } from '../recommendations-api'
import { isRunUnderWay } from '../recommendations'
import type { RecommendationDismissal, RecommendationRun, RecommendationsQuery } from '../types'
import { qk } from './keys'

/**
 * MAM-33 — how often the latest computation is asked for while one is under way, the cadence of a
 * library sync's polling: a computation from cached catalog rows takes seconds.
 */
const RUN_POLL_MS = 2_000

/**
 * MAM-33 — one page of one tab, filtered and paged by the server. The page on screen stays while the
 * next page or filter loads, so a change doesn't blank the grid — but another tab's works never stand
 * in for this tab's: they would read as this tab's until its answer came.
 */
export const useRecommendations = (query: RecommendationsQuery) =>
  useQuery({
    queryKey: qk.recommendations.list(query),
    queryFn: () => recommendationsApi.list(query),
    placeholderData: (previous, previousQuery) =>
      previousQuery?.queryKey[2].tab === query.tab ? keepPreviousData(previous) : undefined,
    // A 400 for a bad filter or a tab switched off won't change on a second try.
    retry: false,
  })

/**
 * MAM-33 — the latest computation of the lists, polled every 2 s while one is under way.
 *
 * When an answer says a computation may have replaced the candidates since the run the cache held
 * (listsMayHaveChanged), every list is asked for again — decided here, on the answer that brings the
 * news, as `useLibrarySources` does for a sync: nothing is refetched on the first load, after a failed
 * computation the page followed (the previous candidates stay), or twice when two components show
 * the run.
 */
export const useLatestRecommendationRun = () => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.recommendations.latestRun,
    queryFn: async ({ signal }) => {
      const next = await recommendationsApi.latestRun(signal)
      // A poll cancelled by a newer request (Recompute now invalidates the run) never commits its
      // answer, so the cache below would still hold what it saw before, and the lists would be asked
      // for here and again in the newer request.
      if (signal.aborted) return next
      // Read after the answer arrived: a start meanwhile has put its queued run in the cache.
      const previous = queryClient.getQueryData<RecommendationRun | null>(qk.recommendations.latestRun)
      if (listsMayHaveChanged(previous, next)) {
        void queryClient.invalidateQueries({ queryKey: qk.recommendations.lists })
      }
      return next
    },
    refetchInterval: (query) => (isRunUnderWay(query.state.data) ? RUN_POLL_MS : false),
    retry: false,
  })
}

/**
 * Whether a computation may have replaced the candidates between the run the cache held (`previous`,
 * undefined before the first answer) and the latest one the server answered with.
 *
 * The server answers with one run only, the running one or else the newest, so a poll can miss a
 * success: the computation the page followed finishes, and by the next poll the one queued behind it
 * has already started, or failed. Its Succeeded state is never seen, so the followed run going away
 * counts as well as a success the cache didn't hold.
 */
function listsMayHaveChanged(previous: RecommendationRun | null | undefined, next: RecommendationRun | null): boolean {
  // The first answer: the lists on screen were asked for alongside it.
  if (previous === undefined) return false
  // A success the cache didn't hold yet: the run it followed, or one it never saw under way.
  if (next?.status === 'Succeeded') return previous === null || previous.id !== next.id || previous.status !== 'Succeeded'
  // The run it followed is no longer the latest: it ended between two polls, maybe in success.
  return isRunUnderWay(previous) && next?.id !== previous.id
}

/**
 * MAM-33 — Recompute now (owner-only). The queued run goes into the cache at once unless one is
 * running there (it stays on screen; the new one waits behind it): that starts the polling, and is
 * what the next answer is compared with, so a computation that ends before that answer still brings
 * the lists again. The run is asked for again whatever the answer.
 */
export const useStartRecommendationRun = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => recommendationsApi.startRun(),
    onSuccess: (run) => {
      queryClient.setQueryData<RecommendationRun | null>(
        qk.recommendations.latestRun,
        (latest) => (latest?.status === 'Running' ? latest : run),
      )
    },
    // A 409 (no catalog any more) or a 503 changes nothing, but the refetch shows the run as it is.
    onSettled: () => queryClient.invalidateQueries({ queryKey: qk.recommendations.latestRun }),
  })
}

/**
 * MAM-33 — "Not interested". Invalidates nothing: the card stays where it was, as the row that offers
 * Undo, rather than the grid closing up under the pointer. The page asks for its lists again when the
 * person leaves the tab or the page.
 */
export const useDismissRecommendation = () =>
  useMutation({
    mutationFn: ({ kind, catalogId }: RecommendationDismissal) => recommendationsApi.dismiss(kind, catalogId),
  })

/** MAM-33 — Undo of "Not interested": the card comes back where it was, so nothing is asked for again. */
export const useUndismissRecommendation = () =>
  useMutation({
    mutationFn: ({ kind, catalogId }: RecommendationDismissal) => recommendationsApi.undismiss(kind, catalogId),
  })

/**
 * What a recommended work is about, in `language` (else English), for its card's Description dialog.
 * Asked for only while the dialog is open — the dialog mounts it — and kept for the page's life: the
 * server keeps it for a month. A failure isn't retried by itself; the dialog offers Try again.
 */
export const useRecommendationOverview = ({ kind, catalogId }: RecommendationDismissal, language: string) =>
  useQuery({
    queryKey: qk.recommendations.overview(kind, catalogId, language),
    queryFn: () => recommendationsApi.overview(kind, catalogId, language),
    staleTime: Infinity,
    retry: false,
  })

/**
 * MAM-33 — a poster as a `data:` URL, the only image source besides the app's own the CSP allows
 * (`img-src 'self' data:`): an `<img src="/api/…">` couldn't carry the bearer token, and a `blob:` URL
 * is blocked. A poster file never changes, so it is never asked for twice while cached; nothing is
 * asked without a URL, and a failure isn't retried — the card shows its placeholder instead.
 *
 * `wanted` is false while the card is far from the screen: nothing is asked for then, so a page of
 * cards doesn't fetch every poster at once. A poster already cached still shows.
 */
export const usePoster = (url: string | null, wanted = true) =>
  useQuery({
    queryKey: qk.recommendations.poster(url ?? ''),
    queryFn: url === null || !wanted ? skipToken : async () => dataUrlOf(await recommendationsApi.poster(url)),
    staleTime: Infinity,
    retry: false,
  })
