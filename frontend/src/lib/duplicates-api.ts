import { api } from './api'
import type { DuplicateMatchDecision, LibraryDuplicateList, LibraryDuplicatesQuery } from './types'

const BASE_URL = '/api/library/duplicates'

/**
 * MAM-20 — the library's duplicates, grouped into the four classes of specification §3.3 and worked out
 * from what the app holds on every read (ADR 0028). Every signed-in user reads them, as they read the
 * Library; the sentences that quote the owner's paths, and who set a link, are the owner's only.
 *
 * MAM-21 — and the owner's decision about a possible match, kept by the group's key: confirmed (looked at,
 * still one group) or dismissed (off the list). A decision never makes the works one and never authorises
 * a removal.
 *
 * MAM-40 — the same decision is the owner's Keep both on copies of a work and exact copies: Dismissed alone,
 * which keeps every copy as it is (listed under Kept as they are) until a new copy joins the group. Nothing
 * here removes, moves or renames a file: Remove this copy is `copyRemovalsApi.remove`, under the owner-only
 * replacement routes.
 */
export const duplicatesApi = {
  /**
   * `GET /api/library/duplicates?class=&decision=&page=&pageSize=`. One page of groups, by class in the
   * specification's order, then by their first work's title. `class` narrows them to one class;
   * `decision` to the possible matches the owner confirmed or dismissed, Dismissed with the copies he kept
   * as they are (MAM-40) — without it the dismissed and kept ones are left out. A copy in the quarantine
   * counts in no group. 400 `{ error }` for a page below 1, a page size outside 1–100, a class or a decision
   * that isn't one.
   */
  list: async (query: LibraryDuplicatesQuery): Promise<LibraryDuplicateList> =>
    (await api.get<LibraryDuplicateList>(BASE_URL, { params: query })).data,

  /**
   * MAM-21 — `PUT /api/library/duplicates/{key}/decision`, owner-only: confirms or dismisses the possible
   * match the key names now, with an audit row. 204, also when the same decision was already stored (then
   * nothing is written). A bare 404 for a key no group has now — its members changed, and it asks again
   * under a new key; 403 `{ error }` for anyone but the owner.
   *
   * MAM-40 — on copies of a work or exact copies it takes Dismissed alone, the owner's Keep both. 400
   * `{ error }` for Confirmed on those, and for any decision on one file's names, which have nothing to
   * keep or remove.
   */
  decide: async (key: string, decision: DuplicateMatchDecision): Promise<void> => {
    await api.put(`${BASE_URL}/${encodeURIComponent(key)}/decision`, { decision })
  },

  /**
   * MAM-21 — `DELETE /api/library/duplicates/{key}/decision`, owner-only: forgets the decision — a possible
   * match's, or (MAM-40) a Keep both —, with an audit row, so the group is undecided again and back on the
   * list. 204, also when none was stored; a bare 404, a 400 `{ error }` (one file's names) and a 403
   * `{ error }` as `decide`.
   */
  undo: async (key: string): Promise<void> => {
    await api.delete(`${BASE_URL}/${encodeURIComponent(key)}/decision`)
  },
}
