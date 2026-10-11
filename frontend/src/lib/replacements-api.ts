import { api } from './api'
import type {
  DeleteQuarantinedRequest,
  DownloadJob,
  ReplacementDetails,
  ReplacementOptions,
  ScheduleReplacementRequest,
} from './types'

const BASE_URL = '/api/replacements'

/** One replacement's address: its id is the server's, encoded into one path segment all the same. */
const jobUrl = (id: string) => `${BASE_URL}/${encodeURIComponent(id)}`

/**
 * MAM-24 — scheduling the replacement of specific old copies with a download (ADR 0032). Owner-only
 * (ADR 0033): every call answers 403 `{ error }` for anyone else, the read included, so the Download
 * dialog never asks for a member. MAM-26 — and the owner's own replacement afterwards: its names in
 * the quarantine, Retry, Restore and Delete for good (ADR 0030). MAM-27 — its copies, new files and
 * check, Keep both and Drop. Another user's replacement, or none, is a bare 404.
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

  /**
   * MAM-26 — `GET /api/replacements/{id}`. 200 with the replacement: every name its move planned, by
   * position, with where each is now and why one stayed, the room its quarantine holds and frees, and
   * what the owner may do. MAM-27 — with every copy the owner ticked and where it stands, the new files
   * and what the check found of each, the works expected, and how far the check got or who stopped it.
   * 403 `{ error }` for anyone but the owner; a bare 404 for another user's replacement or none.
   */
  get: async (id: string): Promise<ReplacementDetails> =>
    (await api.get<ReplacementDetails>(jobUrl(id))).data,

  /**
   * MAM-26 — `POST /api/replacements/{id}/retry`. 200 with its download job. After a stop of the mover
   * the replacement goes back to `Verified`, so the next pass checks the old copies again and moves
   * them; MAM-27 — after a stop of the check of its new files it goes back to `Downloaded`, the last
   * check's evidence forgotten, so the next pass checks the new files again from the disk and asks
   * Jellyfin to look again, whose ids still decide. Nothing is written to the media or sent to a client
   * by the request itself. 409 `{ error }` when the replacement isn't waiting for review, when the app
   * can't follow its download, when a copy in it can only be dropped (the sentence names it), or when
   * another action on it is under way or it changed meanwhile; 403 `{ error }` for anyone but the
   * owner; a bare 404 for another user's replacement or none.
   *
   * Never retried on its own: a write goes out once, and only the owner's own click repeats it.
   */
  retry: async (id: string): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(`${jobUrl(id)}/retry`)).data,

  /**
   * MAM-27 — `POST /api/replacements/{id}/cancel`, the owner's Keep both. 200 with its download job,
   * the replacement `Cancelled`: both copies stay where they are, and the download is left as it is —
   * nothing written to it, nothing sent to its client. 409 `{ error }` when the replacement has ended
   * already, when part of it has left its folders (Restore puts that back), or when another action on
   * it is under way or it changed meanwhile; 403 `{ error }` for anyone but the owner; a bare 404 for
   * another user's replacement or none.
   *
   * Never retried on its own: a write goes out once, and only the owner's own click repeats it.
   */
  cancel: async (id: string): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(`${jobUrl(id)}/cancel`)).data,

  /**
   * MAM-27 — `POST /api/replacements/{id}/copies/{copyId}/drop`. 200 with its download job: the copy
   * is out of the replacement and stays where it is, and the replacement's state is unchanged — nothing
   * else happens until the owner presses Retry. 409 `{ error }` when the replacement didn't stop for
   * review, when the app can't follow its download, when the copy is dropped already, has left its
   * folder (Restore puts it back) or is the last one left (Keep both instead), or when another action
   * on it is under way or it changed meanwhile; 403 `{ error }` for anyone but the owner; a bare 404
   * for another user's replacement or none, and for a copy that isn't one of this replacement's.
   *
   * Never retried on its own: a write goes out once, and only the owner's own click repeats it.
   */
  dropCopy: async (id: string, copyId: string): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(`${jobUrl(id)}/copies/${encodeURIComponent(copyId)}/drop`)).data,

  /**
   * MAM-26 — `POST /api/replacements/{id}/restore`. 200 with its download job once every name in the
   * quarantine is back where it was, the replacement `Restored`. 409 `{ error }` when nothing of it
   * is in the quarantine, when replacement is switched off, when a name's folder is no longer ticked,
   * changed in the quarantine, would land on a file already there or in a folder that is gone —
   * nothing put back —, when another action on it is under way, or when a rename failed part-way (the
   * sentence says how many came back, and the replacement waits for review). 403 `{ error }` for
   * anyone but the owner; a bare 404 for another user's replacement or none.
   *
   * Never retried on its own: a write goes out once, and only the owner's own click repeats it.
   */
  restore: async (id: string): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(`${jobUrl(id)}/restore`)).data,

  /**
   * MAM-26 — `POST /api/replacements/{id}/delete-for-good` with the ids of the names the owner
   * confirmed. 200 with its download job once every one is deleted, the replacement `Completed`. 400
   * `{ error }` without names; 409 `{ error }` when it isn't Quarantined, when the names aren't
   * exactly what it holds in the quarantine now, when replacement is switched off, when a name
   * changed in the quarantine — nothing deleted —, when another action on it is under way, or when a
   * deletion failed part-way (the sentence says how many are gone, and the replacement waits for
   * review). 403 `{ error }` for anyone but the owner; a bare 404 for another user's replacement or none.
   *
   * Never retried on its own: a deletion goes out once, and only the owner's own click repeats it.
   */
  deleteForGood: async (id: string, names: string[]): Promise<DownloadJob> =>
    (await api.post<DownloadJob>(`${jobUrl(id)}/delete-for-good`, { names } satisfies DeleteQuarantinedRequest)).data,
}
