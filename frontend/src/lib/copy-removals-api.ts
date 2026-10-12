import { api } from './api'
import type { CopyRemoval, CopyRemovalListResponse, CopyRemovalsQuery, RemoveCopyRequest } from './types'

const BASE_URL = '/api/replacements/removals'

/** One removal's address: its id is the server's, encoded into one path segment all the same. */
const removalUrl = (id: string) => `${BASE_URL}/${encodeURIComponent(id)}`

/**
 * MAM-40 — the owner's removals of a copy from the Duplicates page: one file of a same-work or
 * exact-copies group moved, with its same-name companions, into the quarantine of its media folder, and
 * put back or deleted for good from there (ADR 0030). Behind the same switch and ticked folders as a
 * replacement, and owner-only like every replacement route (ADR 0033): anyone else gets 403 `{ error }`,
 * the read included. A removal, group or file the server doesn't know is a bare 404.
 */
export const copyRemovalsApi = {
  /**
   * `GET /api/replacements/removals?page=&pageSize=`. 200 with one page of the removals still open
   * (Moving, Quarantined, NeedsReview, Restoring, Deleting), newest first, and the room all of them
   * take in the quarantine. 400 `{ error }` for a page below 1 or a page size outside 1–100.
   */
  list: async (query: CopyRemovalsQuery): Promise<CopyRemovalListResponse> =>
    (await api.get<CopyRemovalListResponse>(BASE_URL, { params: query })).data,

  /**
   * `POST /api/replacements/removals`. 200 with the removal once the file and its companions are in the
   * quarantine and Jellyfin has been told. A bare 404 when the group's key names no group now or the file
   * isn't one of it; 409 `{ error }` when a gate or a check refuses it — nothing moved — or when the
   * video's rename failed (the removal ended NotMoved, its sentence in the error); 400 `{ error }` for a
   * request it can't take.
   *
   * Never retried on its own: a move goes out once, and only the owner's own click repeats it.
   */
  remove: async (request: RemoveCopyRequest): Promise<CopyRemoval> =>
    (await api.post<CopyRemoval>(BASE_URL, request)).data,

  /**
   * `POST /api/replacements/removals/{id}/restore`. 200 with the removal once every name in the
   * quarantine is back where it was. 409 `{ error }` when nothing of it is in the quarantine, when
   * replacement is switched off or a folder is no longer ticked, or when a name can't go back — the
   * sentence says which; a bare 404 for a removal the server doesn't know.
   *
   * Never retried on its own: a write goes out once, and only the owner's own click repeats it.
   */
  restore: async (id: string): Promise<CopyRemoval> =>
    (await api.post<CopyRemoval>(`${removalUrl(id)}/restore`)).data,

  /**
   * `POST /api/replacements/removals/{id}/delete-for-good`, with no body: the whole removal. 200 with the
   * removal once every name of it in the quarantine is deleted. 409 `{ error }` when it can't be deleted
   * now — the sentence says why; a bare 404 for a removal the server doesn't know.
   *
   * Never retried on its own: a deletion goes out once, and only the owner's own click repeats it.
   */
  deleteForGood: async (id: string): Promise<CopyRemoval> =>
    (await api.post<CopyRemoval>(`${removalUrl(id)}/delete-for-good`)).data,
}
