import { keepPreviousData, skipToken, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { isBatchUnderWay, isLinkUnderWay, isSuggestionRunUnderWay, LINK_POLL_MS } from '../library-links'
import { libraryLinksApi } from '../library-links-api'
import type {
  ConfidentSuggestionsQuery,
  LibraryLinkAcceptItem,
  LibraryLinkBatch,
  LibraryLinkCatchUp,
  LibraryLinkDialog,
  LibraryLinkList,
  LibraryLinkLookupQuery,
  LibraryLinkRequest,
  LibraryLinkSearchQuery,
  LibraryLinkSuggestionRun,
  LibraryLinksQuery,
} from '../types'
import { qk } from './keys'

/**
 * MAM-34 — a work's TMDb dialog (owner-only), asked for when the dialog opens and again every
 * `LINK_POLL_MS` while an item waits for Jellyfin or is being identified (a film takes seconds, a series
 * minutes) or the suggestions are being prepared, so the state and the suggestions fill in by
 * themselves.
 *
 * When an identification that was under way in the answer the cache held is over in the new one, the
 * works (the cards) and the other link queries (the panel's counts, the lists) are asked for again: the
 * dialog may long since have been closed or never been the one that started it, and nothing else tells
 * them the link has settled. The pattern `useLibraryLinkBatch` uses. Only a link's own identification
 * counts: a preparation of the suggestions ending is no link change and mustn't refetch the works and the
 * Review queue.
 */
export const useLibraryLinkDialog = (workId: string) => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.library.links.work(workId),
    queryFn: async () => {
      const next = await libraryLinksApi.dialog(workId)
      // Read after the answer arrived: a save meanwhile has put its queued items in the cache.
      const previous = queryClient.getQueryData<LibraryLinkDialog>(qk.library.links.work(workId))
      if (linkUnderWay(previous) && !linkUnderWay(next)) invalidateAfterLinkChange(queryClient, workId)
      return next
    },
    refetchInterval: (query) => (underWay(query.state.data) ? LINK_POLL_MS : false),
    // A 403 or a 404 for a work a sync removed won't change on a second try.
    retry: false,
  })
}

/**
 * MAM-34 — a free TMDb search from a work's dialog, asked only once the owner searches (`query` null
 * until then). Each search is cached apart, so going back to an earlier one asks nothing.
 */
export const useLibraryLinkSearch = (workId: string, query: LibraryLinkSearchQuery | null) =>
  useQuery({
    queryKey: qk.library.links.search(workId, query ?? { title: '' }),
    queryFn: query === null ? skipToken : () => libraryLinksApi.search(workId, query),
    // A 400 for a blank title or a bad year, or a 409 without a catalog, won't change on a second try.
    retry: false,
  })

/** MAM-34 — a pasted id looked up from a work's dialog, asked only once the owner looks it up. */
export const useLibraryLinkLookup = (workId: string, query: LibraryLinkLookupQuery | null) =>
  useQuery({
    queryKey: qk.library.links.lookup(workId, query ?? { text: '' }),
    queryFn: query === null ? skipToken : () => libraryLinksApi.lookup(workId, query),
    // A 400 for text that names nothing, or a 404 for an id TMDb doesn't have, is its lasting answer.
    retry: false,
  })

/**
 * MAM-34 — links a work's items to a TMDb film or show, or replaces their link (owner-only). Jellyfin's
 * identification is queued, never waited for: the answer is the dialog as the save left it, the items
 * `Queued`, and it goes into the cache at once, where the dialog's poll takes over; see
 * `invalidateAfterLinkChange` for what else is asked for again.
 */
export const useSetLibraryLink = (workId: string) => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: LibraryLinkRequest) => libraryLinksApi.link(workId, request),
    onSuccess: (dialog) => {
      queryClient.setQueryData<LibraryLinkDialog>(qk.library.links.work(workId), dialog)
      invalidateAfterLinkChange(queryClient, workId)
    },
  })
}

/**
 * MAM-34 — removes a work's links in the app (owner-only); Jellyfin keeps what Identify set. A 200
 * brings the dialog as the removal left it; a 204 (nothing was linked — another tab removed it) brings
 * nothing, so the dialog is asked for again.
 */
export const useRemoveLibraryLink = (workId: string) => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => libraryLinksApi.remove(workId),
    onSuccess: (result) => {
      if (result === null) void queryClient.invalidateQueries({ queryKey: qk.library.links.work(workId), exact: true })
      else queryClient.setQueryData<LibraryLinkDialog>(qk.library.links.work(workId), result.dialog)
      invalidateAfterLinkChange(queryClient, workId)
    },
  })
}

/**
 * MAM-34 — Identify in Jellyfin, Retry or Identify again for one item's link (owner-only). The answer
 * isn't read: the dialog is asked for again whatever it was, as a 409 usually means an identification
 * is already under way, which the refetch then shows (and polls until it ends). The works are asked for
 * again for the card's state.
 */
export const useWriteLibraryLink = (workId: string) => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (linkId: string) => libraryLinksApi.writeLink(linkId),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: qk.library.links.work(workId), exact: true })
      invalidateAfterLinkChange(queryClient, workId)
    },
  })
}

/**
 * MAM-34 — what a link change makes stale besides the work's own dialog: the works (the card's ids and
 * state, the "Missing an id" filter, which a linked work leaves) and every other link query (the
 * review queue, the confident list, the lists of links, another work's dialog whose neighbour this
 * was). The work's own dialog and its searches are left alone: the dialog came with the answer, and a
 * search's results don't depend on the link.
 */
function invalidateAfterLinkChange(queryClient: QueryClient, workId: string) {
  void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
  void queryClient.invalidateQueries({
    queryKey: qk.library.links.all,
    predicate: (query) => query.queryKey[2] !== 'works' || query.queryKey[3] !== workId,
  })
}

/**
 * Every page of every filter's list of links asked for again (the Not identified count and tab, the
 * Conflicts and Unattached tabs), except the list of `except`: a hook that is itself returning that
 * list's answer must not invalidate it, or its own fetch is cancelled and started over without end.
 */
function invalidateLinkLists(queryClient: QueryClient, except?: LibraryLinksQuery) {
  void queryClient.invalidateQueries({
    queryKey: qk.library.links.all,
    predicate: (query) => {
      if (query.queryKey[2] !== 'list') return false
      const listed = query.queryKey[3] as LibraryLinksQuery
      return except === undefined
        || listed.filter !== except.filter || listed.page !== except.page || listed.pageSize !== except.pageSize
    },
  })
}

/** Whether one of an open dialog's items waits for Jellyfin or is being identified: a link change a poll will bring the end of. */
function linkUnderWay(dialog: LibraryLinkDialog | null | undefined): boolean {
  return dialog != null && dialog.items.some((item) => isLinkUnderWay(item.writeState))
}

/** Whether an open dialog shows something under way that a poll will bring the end of: an identification, or suggestions being prepared. */
function underWay(dialog: LibraryLinkDialog | null | undefined): boolean {
  return dialog != null && (dialog.search.pending || linkUnderWay(dialog))
}

// ── the TMDb page ──

/**
 * MAM-34 — the review queue and its work at `index` (owner-only). The work on screen stays while the
 * next index loads, so → doesn't blank the page; asked for again every 2 s while that work's
 * suggestions are being prepared, as its dialog is. A link, "none fits" or a finished sync asks for it
 * again (`invalidateAfterLinkChange`, `useLibrarySources`), so the same index then shows the next work.
 */
export const useLibraryLinkQueue = (index: number) =>
  useQuery({
    queryKey: qk.library.links.queue(index),
    queryFn: () => libraryLinksApi.queue(index),
    placeholderData: keepPreviousData,
    refetchInterval: (query) => (underWay(query.state.data?.current) ? LINK_POLL_MS : false),
    // A 403 for anyone but the owner won't change on a second try.
    retry: false,
  })

/**
 * MAM-34 — "None fits" for a work of the review queue (owner-only): it leaves the queue and the
 * confident list, which are asked for again; the works aren't, as the mark changes no card.
 */
export const useMarkNoneFits = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (workId: string) => libraryLinksApi.markNoneFits(workId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.library.links.all }),
  })
}

/** MAM-34 — takes a work's "none fits" back (owner-only): it is in the queue and the confident list again. */
export const useClearNoneFits = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (workId: string) => libraryLinksApi.clearNoneFits(workId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.library.links.all }),
  })
}

/**
 * MAM-34 — one page of the works whose suggestion is confident, with each library's counts
 * (owner-only). The page on screen stays while the next one loads.
 */
export const useConfidentSuggestions = (query: ConfidentSuggestionsQuery) =>
  useQuery({
    queryKey: qk.library.links.confident(query),
    queryFn: () => libraryLinksApi.confident(query),
    placeholderData: keepPreviousData,
    // A 400 for a page or a library the server refuses won't change on a second try.
    retry: false,
  })

/**
 * MAM-34 — "Link N works": links the ticked works to their confident suggestion (owner-only), Jellyfin's
 * identifications left to the background. The answer is the batch's first state, put in the cache so
 * its rows show at once and poll from there (`useLibraryLinkBatch`). The works and the other link
 * queries are asked for again — but not the confident list, whose rows the page keeps as they were
 * clicked while their identifications are followed (R24), nor the batches, which poll themselves.
 */
export const useAcceptConfidentSuggestions = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (items: LibraryLinkAcceptItem[]) => libraryLinksApi.acceptConfident(items),
    onSuccess: (batch) => {
      queryClient.setQueryData<LibraryLinkBatch>(qk.library.links.batch(batch.batchId), batch)
      void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
      void queryClient.invalidateQueries({
        queryKey: qk.library.links.all,
        predicate: (query) => query.queryKey[2] !== 'confident' && query.queryKey[2] !== 'batches',
      })
    },
  })
}

/**
 * MAM-34 — an "Accept all confident" click followed while its identifications are under way (owner-only):
 * asked for every 2 s until no link of it waits or is being identified. The answer that brings the last
 * one's end asks for the works again, for the cards' states.
 *
 * Each answer holds only the linked works: it is rebuilt from the stored links, and a skipped work
 * isn't stored, so the caller keeps the click's own answer for those. `enabled` is false for a click
 * that linked no work, whose batch the API answers with a 404: not even a refresh of every link query
 * (a finished sync or preparation) asks for it then.
 */
export const useLibraryLinkBatch = (batchId: string, enabled: boolean) => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.library.links.batch(batchId),
    enabled,
    queryFn: async () => {
      const next = await libraryLinksApi.batch(batchId)
      const previous = queryClient.getQueryData<LibraryLinkBatch>(qk.library.links.batch(batchId))
      if (isBatchUnderWay(previous) && !isBatchUnderWay(next)) void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
      return next
    },
    refetchInterval: (query) => (isBatchUnderWay(query.state.data) ? LINK_POLL_MS : false),
    // The click's own answer, put in the cache, is as fresh as a poll: the first poll comes 2 s after
    // it rather than at once, and a batch already over is never asked for.
    staleTime: LINK_POLL_MS,
    // A 404 for a batch that isn't there won't change on a second try.
    retry: false,
  })
}

/**
 * MAM-34 — one page of the links of a filter: the conflicts, those whose item is gone, or those not
 * identified (owner-only). `enabled` is false for a member, whom the API refuses (the panel's count).
 */
export const useLibraryLinkList = (query: LibraryLinksQuery, enabled = true) =>
  useQuery({
    queryKey: qk.library.links.list(query),
    queryFn: () => libraryLinksApi.links(query),
    enabled,
    placeholderData: keepPreviousData,
    // A 400 for a filter or a page the server refuses won't change on a second try.
    retry: false,
  })

/**
 * MAM-34 — removes one link by its id, the way the TMDb page removes a link whose item the library no
 * longer reports (owner-only). The works and every link query are asked for again.
 */
export const useRemoveLibraryLinkById = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (linkId: string) => libraryLinksApi.removeLink(linkId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
      void queryClient.invalidateQueries({ queryKey: qk.library.links.all })
    },
  })
}

/**
 * MAM-34 — the latest preparation of the suggestions (owner-only), asked for every 2 s while one is
 * queued or running. The answer that brings its end asks for the queue, the lists and every dialog
 * again: their suggestions changed.
 */
export const useLatestLinkSuggestionRun = () => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.library.links.latestRun,
    queryFn: async () => {
      const next = await libraryLinksApi.latestRun()
      // Read after the answer arrived: a start meanwhile has put its queued run in the cache.
      const previous = queryClient.getQueryData<LibraryLinkSuggestionRun | null>(qk.library.links.latestRun)
      if (suggestionsMayHaveChanged(previous, next)) {
        void queryClient.invalidateQueries({
          queryKey: qk.library.links.all,
          predicate: (query) => query.queryKey[2] !== 'runs',
        })
      }
      return next
    },
    refetchInterval: (query) => (isSuggestionRunUnderWay(query.state.data) ? LINK_POLL_MS : false),
    // A 403 for anyone but the owner won't change on a second try.
    retry: false,
  })
}

/**
 * MAM-34 — "Prepare again" (owner-only): searches TMDb anew for every work without an id. The queued
 * run (or the one already waiting) goes into the cache, unless one is running, which the poll follows.
 */
export const useStartLinkSuggestionRun = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => libraryLinksApi.startRun(),
    onSuccess: (run) => {
      queryClient.setQueryData<LibraryLinkSuggestionRun | null>(
        qk.library.links.latestRun,
        (latest) => (latest?.status === 'Running' ? latest : run),
      )
    },
    // A 409 (no catalog) changes nothing, but the refetch shows the run as it is.
    onSettled: () => queryClient.invalidateQueries({ queryKey: qk.library.links.latestRun }),
  })
}

/**
 * Whether a preparation has ended between the run the cache held (`previous`; undefined before the
 * first answer, when nothing was on screen yet) and `next`: another run, or the same one now final.
 */
function suggestionsMayHaveChanged(
  previous: LibraryLinkSuggestionRun | null | undefined,
  next: LibraryLinkSuggestionRun | null,
): boolean {
  // The status, not isSuggestionRunUnderWay: its type guard would leave `next` as never below.
  if (previous === undefined || next === null || next.status === 'Queued' || next.status === 'Running') return false
  return previous === null || previous.id !== next.id || previous.status !== next.status
}

// ── Update Jellyfin ──

/** The Pending list one link at a time: its count is how many links wait for Jellyfin or are being identified. */
const PENDING_LINKS: LibraryLinksQuery = { filter: 'Pending', page: 1, pageSize: 1 }

/**
 * MAM-34 — what "Update Jellyfin" would still identify, and the batch under way (owner-only, Vasyl's
 * decision of 2026-10-06). `enabled` is false for a member, whom the API refuses.
 *
 * Asked for again every `LINK_POLL_MS` while a batch is under way (`progress` not null), so its "done of
 * total" moves and a reload in the middle finds it still going. The answer that brings its end (the
 * cache held a batch, the new one holds none) asks for the works and every list of links again: the
 * cards, the Not identified count and its tab changed.
 */
export const useLibraryLinkCatchUp = (enabled: boolean) => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.library.links.catchUp,
    queryFn: async () => {
      const next = await libraryLinksApi.catchUp()
      // Read after the answer arrived: a start meanwhile has put its batch in the cache.
      const previous = queryClient.getQueryData<LibraryLinkCatchUp>(qk.library.links.catchUp)
      if (previous?.progress != null && next.progress === null) {
        void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
        invalidateLinkLists(queryClient)
      }
      return next
    },
    enabled,
    refetchInterval: (query) => (query.state.data?.progress != null ? LINK_POLL_MS : false),
    // A 403 for anyone but the owner won't change on a second try.
    retry: false,
  })
}

/**
 * MAM-34 — how many links wait for the background worker or are being identified (owner-only), read
 * from the Pending list a link at a time and asked for every `LINK_POLL_MS` while there are any. A link
 * queued from the dialog or the Review tab carries no batch id, so the catch-up's `progress` never
 * mentions it and the panel would never learn it failed; this list does. The answer that brings the
 * last one's end asks for the counts, every other list of links (the Not identified count and tab) and
 * the works again. The Pending list itself is left out of that: invalidating the query whose answer is
 * being returned would cancel it and ask again, with the same cache, forever.
 */
export const usePendingLibraryLinks = (enabled: boolean) => {
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: qk.library.links.list(PENDING_LINKS),
    enabled,
    queryFn: async () => {
      const next = await libraryLinksApi.links(PENDING_LINKS)
      const previous = queryClient.getQueryData<LibraryLinkList>(qk.library.links.list(PENDING_LINKS))
      if (previous !== undefined && previous.totalCount > 0 && next.totalCount === 0) {
        void queryClient.invalidateQueries({ queryKey: qk.library.links.catchUp })
        void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
        invalidateLinkLists(queryClient, PENDING_LINKS)
      }
      return next
    },
    refetchInterval: (query) => ((query.state.data?.totalCount ?? 0) > 0 ? LINK_POLL_MS : false),
    // A 403 for anyone but the owner won't change on a second try.
    retry: false,
  })
}

/**
 * MAM-34 — "Update Jellyfin" (owner-only): queues the links the count names for the background worker.
 * Its answer isn't kept: every link query is asked for again — the counts, now without the queued
 * links and with the batch's progress, which starts the progress poll — and so are the works, whose
 * cards show their links waiting. The click ends once the link queries have answered, so the button
 * doesn't come back on between the start and the first progress.
 */
export const useStartLibraryLinkCatchUp = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => libraryLinksApi.startCatchUp(),
    onSuccess: async () => {
      void queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
      await queryClient.invalidateQueries({ queryKey: qk.library.links.all })
    },
  })
}
