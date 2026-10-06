import { api } from './api'
import type {
  ConfidentSuggestionList,
  ConfidentSuggestionsQuery,
  LibraryLinkAcceptItem,
  LibraryLinkBatch,
  LibraryLinkCatchUp,
  LibraryLinkDialog,
  LibraryLinkList,
  LibraryLinkLookupQuery,
  LibraryLinkQueue,
  LibraryLinkRemoveResult,
  LibraryLinkRequest,
  LibraryLinkSearchQuery,
  LibraryLinkSuggestionRun,
  LibraryLinksQuery,
  LinkCandidateList,
} from './types'

const BASE_URL = '/api/library'

/**
 * MAM-34 — a library work linked to its TMDb film or show by hand, and its ids written into the
 * library's ProviderIds — a film's, a series', or an episode's own IMDb id for an episode Jellyfin lists
 * as a film (no images, no titles, the cast left as it is — ADR 0026) —, after which Jellyfin is asked
 * to look up the item's missing metadata, as its own "Search for missing metadata" does (Vasyl's
 * decision of 2026-10-06). An episode's link the app has no id to write for is kept in the app, as the
 * server says per item. Apart from the Library page's own works,
 * whose cards show a linked id to everyone, every call here is the installation owner's: 403
 * `{ error }` for anyone else, reads included, as the suggestions name the owner's files.
 *
 * A link whose write is under way answers 409 `{ error }` to a second set, remove or write ("A write to
 * the library is under way for this work; try again in a moment."); the dialog polls until it ends.
 */
export const libraryLinksApi = {
  /**
   * `GET /api/library/works/{id}/tmdb`. 200 with the work's dialog: the work with its link, each of its
   * library items with its link and where it stands, whether a catalog is connected, the prepared
   * suggestions (best first, at most five) and where their preparation stands. A bare 404 when the work
   * is gone.
   */
  dialog: async (workId: string): Promise<LibraryLinkDialog> =>
    (await api.get<LibraryLinkDialog>(`${BASE_URL}/works/${workId}/tmdb`)).data,

  /**
   * `PUT /api/library/works/{id}/tmdb`. Links the work's items (`sourceItemIds`, or all of them) to a
   * TMDb film or show, or replaces their link, then writes their ids into the library at once, item by
   * item, and asks the library to look up each written item's missing metadata (an episode's link
   * without an id to write is kept in the app). 200 with the dialog as it now stands — each item's write
   * state says how the write went, its lookup fields how the request for the lookup went (a failed write
   * or lookup is a state with its sentence, not a failure of the call). 400 `{ error }` for a kind, id, season or episode the rules refuse, an item
   * that isn't the work's, or a film for a series; a bare 404 when the work is gone, and 404
   * `{ error }` when TMDb has no such film or show; 409
   * `{ error }` when no catalog is connected, no library reports the work, or a write is under way;
   * 502 `{ error }` when TMDb can't be read.
   */
  link: async (workId: string, request: LibraryLinkRequest): Promise<LibraryLinkDialog> =>
    (await api.put<LibraryLinkDialog>(`${BASE_URL}/works/${workId}/tmdb`, request)).data,

  /**
   * `DELETE /api/library/works/{id}/tmdb`. Changes back in the library the ids the app wrote, where it
   * still holds them, then forgets the work's links; a link never written (kept in the app) has nothing
   * to change back. 200 with the dialog as the removal left it; 204 (null here) when the
   * work had no link. A bare 404 when the work is gone; 409 `{ error }` when the library integration
   * holding ids the app wrote is switched off, or a write is under way; 502 `{ error }` when the
   * library couldn't be changed back — the link then stays, and Remove can be tried again.
   */
  remove: async (workId: string): Promise<LibraryLinkRemoveResult | null> => {
    const response = await api.delete<LibraryLinkRemoveResult>(`${BASE_URL}/works/${workId}/tmdb`)
    return response.status === 204 ? null : response.data
  },

  /**
   * `POST /api/library/tmdb/links/{linkId}/write`. Retry, or Write again, for one item's link: writes
   * its ids again (nothing changes where they are in place) and asks the library for the metadata
   * lookup again. The answer's body isn't read: the caller asks for the dialog again, which shows how
   * the write went. 409 `{ error }` when the link can't be written (one kept in the app, a switched-off
   * integration, a write under way, an item the library no longer reports); a bare 404 when the link is
   * gone.
   */
  writeLink: async (linkId: string): Promise<void> => {
    await api.post(`${BASE_URL}/tmdb/links/${linkId}/write`)
  },

  /**
   * `DELETE /api/library/tmdb/links/{linkId}`. Forgets one link, the way the TMDb page removes a link
   * whose item the library no longer reports. 204; a bare 404 when it is gone already; 409 and 502
   * `{ error }` as `remove`.
   */
  removeLink: async (linkId: string): Promise<void> => {
    await api.delete(`${BASE_URL}/tmdb/links/${linkId}`)
  },

  /**
   * `GET /api/library/works/{id}/tmdb/search?title=&year=`. TMDb's first page of films and shows (by the
   * work's kind) for a title, the year narrowing it, each with its evidence against the work. 400
   * `{ error }` for a blank title or a year that isn't one; a bare 404 when the work is gone; 409
   * `{ error }` when no catalog is connected; 502 `{ error }` when TMDb can't be read.
   */
  search: async (workId: string, query: LibraryLinkSearchQuery): Promise<LinkCandidateList> =>
    (await api.get<LinkCandidateList>(`${BASE_URL}/works/${workId}/tmdb/search`, { params: query })).data,

  /**
   * `GET /api/library/works/{id}/tmdb/lookup?text=&kind=`. The film or show a pasted TMDb or IMDb link,
   * IMDb id or bare TMDb number names, with its evidence; several when an IMDb id names a film and a
   * show. `kind` (`Movie` or `Series`) says which numbering a bare number is in. 400 `{ error }` for
   * text that is none of those, or a bare number without a kind; 404 `{ error }` when TMDb has no such
   * film or show (or the IMDb id is an episode's); 409 `{ error }` when no catalog is connected; 502
   * `{ error }` when TMDb can't be read.
   */
  lookup: async (workId: string, query: LibraryLinkLookupQuery): Promise<LinkCandidateList> =>
    (await api.get<LinkCandidateList>(`${BASE_URL}/works/${workId}/tmdb/lookup`, { params: query })).data,

  /**
   * `GET /api/library/tmdb/queue?index=`. The review queue — the works without a TMDb id, without a
   * link and not marked "none fits", in title order — and the dialog of the one at `index`. 400
   * `{ error }` for an index below 0.
   */
  queue: async (index: number): Promise<LibraryLinkQueue> =>
    (await api.get<LibraryLinkQueue>(`${BASE_URL}/tmdb/queue`, { params: { index } })).data,

  /**
   * `PUT /api/library/works/{id}/tmdb/none-fits`. Marks that none of the work's suggestions fits, which
   * takes it out of the review queue and the confident list. 204; a bare 404 when the work is gone.
   */
  markNoneFits: async (workId: string): Promise<void> => {
    await api.put(`${BASE_URL}/works/${workId}/tmdb/none-fits`)
  },

  /** `DELETE /api/library/works/{id}/tmdb/none-fits`. Takes the mark back. 204; a bare 404 when the work is gone. */
  clearNoneFits: async (workId: string): Promise<void> => {
    await api.delete(`${BASE_URL}/works/${workId}/tmdb/none-fits`)
  },

  /**
   * `GET /api/library/tmdb/confident?locationId=&page=&pageSize=`. One page of the works whose
   * suggestion is confident, by title, with each library's counts. 400 `{ error }` for a page below 1,
   * a page size outside 1–100 or a library that isn't an id.
   */
  confident: async (query: ConfidentSuggestionsQuery): Promise<ConfidentSuggestionList> =>
    (await api.get<ConfidentSuggestionList>(`${BASE_URL}/tmdb/confident`, { params: query })).data,

  /**
   * `POST /api/library/tmdb/confident`. Links the given works to their confident suggestion (1 to 100),
   * each taken only while that suggestion is still the work's confident one; the library writes happen
   * in the background, one link at a time. 202 with what happened to each work; `batch` follows the
   * writes. 400 `{ error }` for no items, more than 100, or a malformed one; 409 `{ error }` when no
   * catalog is connected.
   */
  acceptConfident: async (items: LibraryLinkAcceptItem[]): Promise<LibraryLinkBatch> =>
    (await api.post<LibraryLinkBatch>(`${BASE_URL}/tmdb/confident`, { items })).data,

  /**
   * `GET /api/library/tmdb/batches/{batchId}`. An "Accept all confident" click again, its links with
   * their states as they are now. Only the linked works, each under the work its item sits on now: the
   * skipped ones aren't stored, and a link whose item the library no longer reports is left out. A
   * bare 404 for an unknown batch, or one that linked no work.
   */
  batch: async (batchId: string): Promise<LibraryLinkBatch> =>
    (await api.get<LibraryLinkBatch>(`${BASE_URL}/tmdb/batches/${batchId}`)).data,

  /**
   * `GET /api/library/tmdb/links?filter=&page=&pageSize=`. One page of the links of a filter. 400
   * `{ error }` for a missing or unknown filter, a page below 1 or a page size outside 1–100.
   */
  links: async (query: LibraryLinksQuery): Promise<LibraryLinkList> =>
    (await api.get<LibraryLinkList>(`${BASE_URL}/tmdb/links`, { params: query })).data,

  /**
   * `GET /api/library/tmdb/catch-up`. What the links set before the app wrote series' and episodes'
   * links, and asked Jellyfin for the metadata lookup, still lack: the links kept in the app that can be
   * written now, and the written ones whose item Jellyfin wasn't asked to look up.
   */
  catchUp: async (): Promise<LibraryLinkCatchUp> =>
    (await api.get<LibraryLinkCatchUp>(`${BASE_URL}/tmdb/catch-up`)).data,

  /**
   * `POST /api/library/tmdb/catch-up`. "Update Jellyfin": queues those links for the background writer,
   * which writes them one at a time (nothing changes where the ids are in place) and asks the library to
   * look up each one's metadata. 202 with what it queued; the Pending list of `links` follows the writes.
   */
  startCatchUp: async (): Promise<LibraryLinkCatchUp> =>
    (await api.post<LibraryLinkCatchUp>(`${BASE_URL}/tmdb/catch-up`)).data,

  /**
   * `GET /api/library/tmdb/runs/latest`. The latest preparation of the suggestions; 204 (null here)
   * before the first.
   */
  latestRun: async (): Promise<LibraryLinkSuggestionRun | null> => {
    const response = await api.get<LibraryLinkSuggestionRun>(`${BASE_URL}/tmdb/runs/latest`)
    return response.status === 204 ? null : response.data
  },

  /**
   * `POST /api/library/tmdb/runs`. Prepares the suggestions again, searching TMDb for every work anew.
   * 202 with the queued run (or the one already waiting); 409 `{ error }` when no catalog is connected.
   */
  startRun: async (): Promise<LibraryLinkSuggestionRun> =>
    (await api.post<LibraryLinkSuggestionRun>(`${BASE_URL}/tmdb/runs`)).data,
}
