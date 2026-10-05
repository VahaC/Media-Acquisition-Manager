import { api } from './api'
import type {
  FileInspectionRun,
  FolderScanRun,
  LibraryEpisodesQuery,
  LibraryLocationList,
  LibrarySourceList,
  LibrarySyncRun,
  LibraryWorkList,
  LibraryWorksQuery,
} from './types'

const BASE_URL = '/api/library'

/**
 * MAM-8 — the imported library. Every signed-in user reads it; only the installation owner starts a
 * sync, which sends the owner's stored key to the service and replaces the snapshot everyone reads.
 * A run's `error` can name the service's address, and a path problem names the owner's mappings, so
 * they — with a run's `requestedByUserName` and every `pathProblem`, a source's or a file's — come
 * back to the owner only and are null for everyone else.
 *
 * MAM-9 — the same holds for the file inspection: a run's `error` and `requestedByUserName`, and
 * every inspection `problem` (they quote paths inside the app), are the owner's only.
 *
 * MAM-18 — and for the scan of the media folders: a scan's `error`, `problem` and
 * `requestedByUserName`, and the `problem` of the names it stored.
 *
 * MAM-19 — and for the hashing at the end of an inspection: a run's `hashing.problem`. The room the
 * files take (`space`) is everyone's.
 */
export const libraryApi = {
  /**
   * `GET /api/library/works?search=&locationId=&identifiers=&kind=&page=&pageSize=`. One page of
   * works, searched (titles, registry ids, year, file paths; any letter case), filtered by library,
   * by missing identifiers and — MAM-32 — by kind, and ordered by title on the server. 400 `{ error }`
   * for a page below 1, a page size outside 1–100, a location that isn't an id, an identifiers filter
   * other than `Missing` or a kind other than `Movie` or `Series`.
   *
   * MAM-32 — a series is one work, listed when one of its episodes passes the search and filters,
   * with its seasons counted by them; an episode of no series is a work of its own.
   */
  works: async (query: LibraryWorksQuery): Promise<LibraryWorkList> =>
    (await api.get<LibraryWorkList>(`${BASE_URL}/works`, { params: query })).data,

  /**
   * MAM-32 — `GET /api/library/works/{seriesId}/episodes?season=&search=&locationId=&identifiers=&page=&pageSize=`.
   * One page of a series' episodes, each a work with its versions, in season, number and title order:
   * one season (`None` for the episodes without one), searched and filtered as the works are. A bare
   * 404 when the id isn't a series'; 400 `{ error }` for a season that isn't one, and for the works'
   * own refusals.
   */
  episodes: async (seriesId: string, query: LibraryEpisodesQuery): Promise<LibraryWorkList> =>
    (await api.get<LibraryWorkList>(`${BASE_URL}/works/${seriesId}/episodes`, { params: query })).data,

  /** `GET /api/library/locations`. 200 with the libraries that hold at least one file. */
  locations: async (): Promise<LibraryLocationList> =>
    (await api.get<LibraryLocationList>(`${BASE_URL}/locations`)).data,

  /**
   * `GET /api/library/sources`. 200 with every media library integration, its latest run, its last
   * successful one and how many of its files the current path mappings give no path in this app.
   * `signal` cancels the request: React Query's, so a poll it cancels rejects instead of answering.
   */
  sources: async (signal?: AbortSignal): Promise<LibrarySourceList> =>
    (await api.get<LibrarySourceList>(`${BASE_URL}/sources`, { signal })).data,

  /**
   * `POST /api/library/sources/{integrationId}/syncs`, owner-only. 202 with the queued run; the sync
   * itself runs in the background, and `sources` shows how it goes. 403 `{ error }` for anyone but
   * the owner; a bare 404 when the integration is gone or isn't a media library; 409 `{ error }`
   * when it is switched off, this version can't read its type, or a sync of it is already under
   * way; 503 `{ error }` when too many syncs are waiting.
   */
  startSync: async (integrationId: string): Promise<LibrarySyncRun> =>
    (await api.post<LibrarySyncRun>(`${BASE_URL}/sources/${integrationId}/syncs`)).data,

  /**
   * `POST /api/library/inspections`, owner-only. 202 with the queued run (Location
   * `/api/library/inspections/{id}`); the run reads the files in the background, and `sources` shows
   * how it goes. Idempotent: when a run is already queued it answers 202 with that one. 409
   * `{ error }` when no media folder is allowed yet; 403 `{ error }` for anyone but the owner.
   */
  startInspection: async (): Promise<FileInspectionRun> =>
    (await api.post<FileInspectionRun>(`${BASE_URL}/inspections`)).data,

  /**
   * MAM-18 — `POST /api/library/scans`, owner-only. 202 with the queued scan (Location
   * `/api/library/scans/{id}`); the scan walks the media folders in the background, and `sources`
   * shows how it goes. When a scan is already queued it answers 202 with that one; a scan that is
   * running gets a new one queued behind it. 409 `{ error }` when no media folder is allowed yet;
   * 403 `{ error }` for anyone but the owner; 503 `{ error }` when too many scans are waiting.
   */
  startScan: async (): Promise<FolderScanRun> =>
    (await api.post<FolderScanRun>(`${BASE_URL}/scans`)).data,
}
