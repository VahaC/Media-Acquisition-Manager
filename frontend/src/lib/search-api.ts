import { api } from './api'
import type { SearchReleaseList, SearchReleasesQuery, SearchRequest, SearchResponse } from './types'

const BASE_URL = '/api/searches'

/**
 * MAM-12 — the stored search. Every signed-in user searches; a search is its creator's, so another
 * user's search — like one that has expired, a day after it was made — answers a bare 404, the same
 * as an id that never existed. A source's `error` can name the service's address, so it comes back
 * to the installation owner only and is null for everyone else (the MAM-10 rule).
 *
 * No release ever carries where it is downloaded from (ADR 0004): only its id and what it claims.
 */
export const searchApi = {
  /**
   * `POST /api/searches`. Asks the library and every enabled release source at once and stores what
   * they answered: 201 with the search (Location `/api/searches/{id}`), whatever the sources said —
   * a source that failed says so in its own entry. 400 `{ error }` for a missing or too long title,
   * control characters, or a year, IMDb or TMDb id that isn't one; nothing is stored then and no
   * source is asked. MAM-32 — a season (0 to 9999, 0 for the specials) is searched for in TV alone,
   * and a season that isn't one is a 400 too.
   */
  create: async (request: SearchRequest): Promise<SearchResponse> =>
    (await api.post<SearchResponse>(BASE_URL, request)).data,

  /**
   * `GET /api/searches/{id}`. 200 with the search: its library matches read again now, its sources as
   * they answered. A bare 404 when it is missing, expired or someone else's.
   *
   * The id comes from the address (`?id=`), so it is encoded into one path segment: a `/` in it must
   * not reach another API path with the caller's token. The page also sends only an id shaped like
   * one, since encoding leaves `..` as it is.
   */
  get: async (id: string): Promise<SearchResponse> =>
    (await api.get<SearchResponse>(`${BASE_URL}/${encodeURIComponent(id)}`)).data,

  /**
   * `GET /api/searches/{id}/releases?source=&kind=&language=&quality=&minSizeBytes=&maxSizeBytes=&sort=&page=&pageSize=`.
   * One page of the search's stored releases, filtered, sorted and paged on the server — no source is
   * asked again. 400 `{ error }` for a source that isn't a source id at all (not a GUID), a kind other
   * than `Movie` or `Tv` (MAM-32), a language or quality the filter can't read, a size that isn't a
   * byte count or a smallest size above the largest, a sort it doesn't know, a page below 1 or a page
   * size outside 1–100. A well-formed source id that isn't one of this search's (one a page kept from
   * an earlier search, say) is not an error: it lists nothing, 200 with no items and a total of 0. A
   * bare 404 like `get`, and the id encoded as `get` encodes it.
   */
  releases: async (id: string, query: SearchReleasesQuery): Promise<SearchReleaseList> =>
    (await api.get<SearchReleaseList>(`${BASE_URL}/${encodeURIComponent(id)}/releases`, { params: query })).data,
}
