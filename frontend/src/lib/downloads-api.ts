import { api } from './api'
import type {
  DownloadJob,
  DownloadJobFileList,
  DownloadJobFilesQuery,
  DownloadJobList,
  DownloadJobsQuery,
  DownloadOptions,
  SendDownloadRequest,
} from './types'

const BASE_URL = '/api/downloads'

/**
 * MAM-13 — sending a release a search found to a download client. Every signed-in user may send one,
 * like searching (MAM-13's decision 1); a release is only the caller's own search's, and a job only
 * its sender's.
 */
export const downloadsApi = {
  /**
   * `GET /api/downloads/options?releaseId=`. 200 with every enabled download client this build can
   * send to that takes the release's payload (one whose kind is still unknown needs a client that
   * takes a .torrent file or a magnet link) and has a destination: clients and destinations by name.
   * 400 `{ error }` without a release; 404 `{ error }` when the release isn't one of the caller's
   * searches' — another user's, more than a day old, or none.
   */
  options: async (releaseId: string): Promise<DownloadOptions> =>
    (await api.get<DownloadOptions>(`${BASE_URL}/options`, { params: { releaseId } })).data,

  /**
   * `POST /api/downloads`. 201 with the new job (Location `/api/downloads/{id}`) whatever its outcome:
   * `Submitted`, `NeedsLinking`, or `Failed` with its `error` — the job is written before anything is
   * fetched or sent. 200 with the existing job when this `requestId` was already sent with the same
   * release, client and destination: a repeated click or a retry, so nothing is fetched or sent
   * again. 400 `{ error }` for a missing id, a destination of another client, or a client that can't
   * take the release's payload; 404 `{ error }` when the release, the client or the destination is
   * gone; 409 `{ error }` when the request id was used for a different download, the client or the
   * release's source is switched off or can't be used by this build, the source was removed, or the
   * release was already sent to this client.
   *
   * Never retried on its own: a write goes out once, and only the person's own Send repeats it.
   */
  send: async (request: SendDownloadRequest): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(BASE_URL, request)).data,

  /**
   * `GET /api/downloads/{id}`. 200 with the caller's own job; a bare 404 for another user's or none.
   * The id is encoded into one path segment, so it can't reach another API path.
   */
  get: async (id: string): Promise<DownloadJob> =>
    (await api.get<DownloadJob>(`${BASE_URL}/${encodeURIComponent(id)}`)).data,

  /**
   * MAM-14 — `GET /api/downloads?status=&page=&pageSize=`. 200 with one page of the caller's own
   * jobs, newest first — the installation owner's too (MAM-14's decision 1). 400 `{ error }` for a
   * page or page size out of range, or a status that isn't one.
   */
  list: async (query: DownloadJobsQuery): Promise<DownloadJobList> =>
    (await api.get<DownloadJobList>(BASE_URL, { params: query })).data,

  /**
   * MAM-14 — `GET /api/downloads/{id}/files?page=&pageSize=`. 200 with one page of the job's files as
   * the client last listed them, by position (none until the client knows them: a magnet still
   * fetching its metadata). A bare 404 for another user's job or none; 400 `{ error }` for a page or
   * page size out of range.
   */
  files: async (id: string, query: DownloadJobFilesQuery): Promise<DownloadJobFileList> =>
    (await api.get<DownloadJobFileList>(`${BASE_URL}/${encodeURIComponent(id)}/files`, { params: query })).data,

  /**
   * MAM-14 — `POST /api/downloads/{id}/cancel`. 200 with the job, now `Cancelled`: the app stops
   * following it and sends nothing to the client, so the torrent and its files stay there. A bare 404
   * for another user's job or none; 409 `{ error }` when the job has already ended (downloaded,
   * failed or cancelled), so there is nothing to stop following.
   */
  cancel: async (id: string): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(`${BASE_URL}/${encodeURIComponent(id)}/cancel`)).data,
}
