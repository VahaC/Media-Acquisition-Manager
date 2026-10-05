import { api } from './api'
import type { CatalogWorkKind, RecommendationList, RecommendationRun, RecommendationsQuery } from './types'

const BASE_URL = '/api/recommendations'

/**
 * MAM-33 — where every poster URL the list gives points. The poster is fetched with the user's token,
 * so only a path under it is ever asked for: a URL from anywhere else would carry the token there.
 */
const POSTER_PATH = `${BASE_URL}/posters/`

/**
 * MAM-33 — the recommendations: works the library doesn't have yet, computed for the installation
 * from its library and the metadata catalog (TMDb), then read per user — their tabs, without the
 * works they marked "Not interested". Every signed-in user reads them; only the installation owner
 * starts a computation, and only the owner reads why one failed and who asked for it.
 */
export const recommendationsApi = {
  /**
   * `GET /api/recommendations?tab=&reason=&available=&genreId=&decade=&kind=&sort=&page=&pageSize=`.
   * One page of one tab, filtered, sorted and paged on the server (no `sort` is Score), with what its
   * filters offer, whether a catalog is connected and the latest computation. 400 `{ error }` for a
   * tab, reason, genre, decade, kind, sort or page it can't read, and "This tab is switched off in your
   * profile." for a tab the user has off.
   */
  list: async (query: RecommendationsQuery): Promise<RecommendationList> =>
    (await api.get<RecommendationList>(BASE_URL, { params: query })).data,

  /** `GET /api/recommendations/runs/latest`. 200 with the latest computation; 204 (null here) before the first. */
  latestRun: async (signal?: AbortSignal): Promise<RecommendationRun | null> => {
    const response = await api.get<RecommendationRun | ''>(`${BASE_URL}/runs/latest`, { signal })
    return response.status === 204 || response.data === '' ? null : response.data
  },

  /**
   * `POST /api/recommendations/runs`, owner-only. 202 with the queued computation, or the one already
   * waiting, which it joins. 403 `{ error }` for anyone but the owner; 409 `{ error }` "Connect a
   * metadata catalog under Integrations first." without one; 503 `{ error }` when too many are waiting.
   */
  startRun: async (): Promise<RecommendationRun> =>
    (await api.post<RecommendationRun>(`${BASE_URL}/runs`)).data,

  /**
   * `PUT /api/recommendations/dismissals/{kind}/{catalogId}` — "Not interested", kept for this user
   * alone. 204, also when it was already dismissed; 400 `{ error }` for a kind or id it can't read.
   */
  dismiss: async (kind: CatalogWorkKind, catalogId: string): Promise<void> => {
    await api.put(`${BASE_URL}/dismissals/${kind}/${encodeURIComponent(catalogId)}`)
  },

  /** `DELETE /api/recommendations/dismissals/{kind}/{catalogId}` — Undo of "Not interested". 204; 400 as `dismiss`. */
  undismiss: async (kind: CatalogWorkKind, catalogId: string): Promise<void> => {
    await api.delete(`${BASE_URL}/dismissals/${kind}/${encodeURIComponent(catalogId)}`)
  },

  /**
   * `GET /api/recommendations/posters/{fileName}` — the poster bytes (JPEG or PNG), as the list's
   * `posterUrl` names them. Through the API client rather than an `<img src>`, which couldn't send the
   * bearer token; the caller shows it as a `data:` URL, the only image source besides the app's own
   * the CSP allows. A bare 404 for a file no recommended work has; 502 `{ error }` when the catalog
   * couldn't be reached. A URL outside the posters path is refused here, before any request.
   */
  poster: async (url: string): Promise<Blob> => {
    if (!url.startsWith(POSTER_PATH) || url.includes('..')) throw new Error(`Not a poster URL: ${url}`)
    return (await api.get<Blob>(url, { responseType: 'blob' })).data
  },
}
