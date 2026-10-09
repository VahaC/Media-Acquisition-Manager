import { api } from './api'
import type { DownloadJob, ReplacementOptions, ScheduleReplacementRequest } from './types'

const BASE_URL = '/api/replacements'

/**
 * MAM-24 — scheduling the replacement of specific old copies with a download (ADR 0032). Owner-only
 * (ADR 0033): every call answers 403 `{ error }` for anyone else, the read included, so the Download
 * dialog never asks for a member.
 */
export const replacementsApi = {
  /**
   * `GET /api/replacements/options?releaseId=&destinationId=&holdings=`. 200 with what of the release
   * the owner may replace through the destination: the release-level reason, or the matched works
   * with their copies, each copy's reason and marks. With `holdings`, and only while something can be
   * replaced, every download client's list of torrents is read for the held marks (ADR 0031); without,
   * none is. 400 `{ error }` without a release or a destination; 404 `{ error }` when the release isn't
   * one of the caller's searches' or the destination is gone.
   */
  options: async (releaseId: string, destinationId: string, holdings: boolean): Promise<ReplacementOptions> =>
    (await api.get<ReplacementOptions>(`${BASE_URL}/options`, { params: { releaseId, destinationId, holdings } })).data,

  /**
   * `POST /api/replacements`. Answers as `POST /api/downloads`: 201 with the new job, its replacement
   * on it, whatever its outcome — `Failed` with its `error` when the send was refused, nothing sent
   * and nothing to replace; 200 with the job this `requestId` already made with the same ticks. 400
   * `{ error }` for a missing id, no copy ticked, a copy that isn't the matched work's, or a match by
   * title and year not confirmed; 404 `{ error }` when the release, the client or the destination is
   * gone; 409 `{ error }` when a ticked copy can't be replaced any more (its sentence names the file),
   * or for the reasons a download is refused.
   *
   * Never retried on its own: a write goes out once, and only the owner's own Send repeats it.
   */
  schedule: async (request: ScheduleReplacementRequest): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(BASE_URL, request)).data,
}
