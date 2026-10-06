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
 * MAM-34 — a library work linked to its TMDb film or show by hand. Saving the link makes Jellyfin
 * identify each item as that film or show, as Jellyfin's own web "Identify" does (Vasyl's decision of
 * 2026-10-06, ADR 0026): it replaces the item's title, overview and images, and there is no undo. It
 * happens in the background, never inside a request — a film takes seconds, a series minutes — so the
 * calls here only queue it and the dialog polls how it goes. A link the app mustn't or can't identify
 * (a library that saves beside the media, a locked item, an episode with no IMDb id) stays in the app,
 * with the reason per item. Apart from the Library page's own works, whose cards show a linked id to
 * everyone, every call here is the installation owner's: 403 `{ error }` for anyone else, reads
 * included, as the suggestions name the owner's files.
 *
 * A work with a link being identified answers 409 `{ error }` to a second set, remove or identify ("A
 * write to the library is under way for this work; try again in a moment."); the dialog polls until it
 * ends.
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
   * TMDb film or show, or replaces their link, and queues Jellyfin's identification of each item the
   * server will identify: the answer is at once, 200 with the dialog as it now stands, those items
   * `Queued`, and the dialog's poll follows them to `Written` or `NotWritten` (an item the server keeps
   * in the app comes back `KeptInApp` with its reason). A refusal by the library or by the app is the
   * item's `writeError`, never a failure of the call. 400 `{ error }` for a kind, id, season or episode
   * the rules refuse, an item that isn't the work's, or a film for a series; a bare 404 when the work is
   * gone, and 404 `{ error }` when TMDb has no such film or show; 409 `{ error }` when no catalog is
   * connected, no library reports the work, or a link of the work is being identified; 502 `{ error }`
   * only when TMDb can't be read.
   */
  link: async (workId: string, request: LibraryLinkRequest): Promise<LibraryLinkDialog> =>
    (await api.put<LibraryLinkDialog>(`${BASE_URL}/works/${workId}/tmdb`, request)).data,

  /**
   * `DELETE /api/library/works/{id}/tmdb`. Forgets the work's links in the app only: Jellyfin keeps
   * what Identify gave the item (title, overview, images, ids) and nothing is changed back there. 200
   * with the dialog as the removal left it; 204 (null here) when the work had no link. A bare 404 when
   * the work is gone; 409 `{ error }` only while a link of the work is being identified. Never 502.
   */
  remove: async (workId: string): Promise<LibraryLinkRemoveResult | null> => {
    const response = await api.delete<LibraryLinkRemoveResult>(`${BASE_URL}/works/${workId}/tmdb`)
    return response.status === 204 ? null : response.data
  },

  /**
   * `POST /api/library/tmdb/links/{linkId}/write`. Identify now, Retry or Identify again for one item's
   * link: queues Jellyfin's identification of the item as its link (an item already showing the match
   * is confirmed with no second Apply). The answer's body isn't read: the caller asks for the dialog
   * again, which shows the item `Queued` and follows it. 409 `{ error }` when the link can't be
   * identified (one kept in the app, a switched-off integration, an identification under way, an item
   * the library no longer reports); a bare 404 when the link is gone.
   */
  writeLink: async (linkId: string): Promise<void> => {
    await api.post(`${BASE_URL}/tmdb/links/${linkId}/write`)
  },

  /**
   * `DELETE /api/library/tmdb/links/{linkId}`. Forgets one link in the app only, the way the TMDb page
   * removes a link whose item the library no longer reports. 204; a bare 404 when it is gone already;
   * 409 `{ error }` as `remove`.
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
   * each taken only while that suggestion is still the work's confident one; Jellyfin's identifications
   * happen in the background. 202 with what happened to each work; `batch` follows the identifications.
   * 400 `{ error }` for no items, more than 100, or a malformed one; 409 `{ error }` when no catalog is
   * connected.
   */
  acceptConfident: async (items: LibraryLinkAcceptItem[]): Promise<LibraryLinkBatch> =>
    (await api.post<LibraryLinkBatch>(`${BASE_URL}/tmdb/confident`, { items })).data,

  /**
   * `GET /api/library/tmdb/batches/{batchId}`. An "Accept all confident" click again, its links with
   * their states as they are now. Only the linked works, each under the work its item sits on now: the
   * skipped ones aren't stored, and a link whose item the library no longer reports is left out. A
   * bare 404 for an unknown batch, or one that linked no work. The id of an "Update Jellyfin" batch
   * (`LibraryLinkProgress.batchId`) reads here too.
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
   * `GET /api/library/tmdb/catch-up`. What "Update Jellyfin" would still identify (`toIdentify`: the
   * links whose item Jellyfin can identify and the app hasn't, which a click would queue now) and the
   * batch of identifications under way (`progress`, null when none is). 403 `{ error }` for a member.
   */
  catchUp: async (): Promise<LibraryLinkCatchUp> =>
    (await api.get<LibraryLinkCatchUp>(`${BASE_URL}/tmdb/catch-up`)).data,

  /**
   * `POST /api/library/tmdb/catch-up`. "Update Jellyfin": queues those links for the background worker,
   * which makes Jellyfin identify each as its link (replacing the item's title, overview and images;
   * there is no undo). 202 always, even when it queued nothing, with what it queued (`toIdentify`) and
   * the batch it made (`progress`, `done` 0; null when it queued nothing); the next `catchUp` answers
   * follow the batch.
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
