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
 * a removal; nothing here removes, moves or renames a file.
 */
export const duplicatesApi = {
  /**
   * `GET /api/library/duplicates?class=&decision=&page=&pageSize=`. One page of groups, by class in the
   * specification's order, then by their first work's title. `class` narrows them to one class;
   * `decision` to the possible matches the owner confirmed or dismissed — without it the dismissed ones
   * are left out. 400 `{ error }` for a page below 1, a page size outside 1–100, a class or a decision
   * that isn't one.
   */
  list: async (query: LibraryDuplicatesQuery): Promise<LibraryDuplicateList> =>
    (await api.get<LibraryDuplicateList>(BASE_URL, { params: query })).data,

  /**
   * MAM-21 — `PUT /api/library/duplicates/{key}/decision`, owner-only: confirms or dismisses the possible
   * match the key names now, with an audit row. 204, also when the same decision was already stored (then
   * nothing is written). A bare 404 for a key no group has now — its members changed, and it asks again
   * under a new key; 400 `{ error }` for a group that isn't a possible match; 403 `{ error }` for anyone
   * but the owner.
   */
  decide: async (key: string, decision: DuplicateMatchDecision): Promise<void> => {
    await api.put(`${BASE_URL}/${encodeURIComponent(key)}/decision`, { decision })
  },

  /**
   * MAM-21 — `DELETE /api/library/duplicates/{key}/decision`, owner-only: forgets the decision, with an
   * audit row, so the group is undecided again and back on the list. 204, also when none was stored; a
   * bare 404, a 400 `{ error }` and a 403 `{ error }` as `decide`.
   */
  undo: async (key: string): Promise<void> => {
    await api.delete(`${BASE_URL}/${encodeURIComponent(key)}/decision`)
  },
}
