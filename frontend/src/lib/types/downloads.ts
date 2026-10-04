// MAM-13 — sending a release to a download client through `/api/downloads`. Every signed-in user may
// send one (MAM-13's decision 1), so the dialog reads its clients and destinations here, by name
// only, rather than from the owner-only settings.

/**
 * Where a download job stands. `Created`: being sent, or the outcome is unknown. `Submitted`: the
 * client took it (`clientJobId` is null while the client hasn't listed it yet). `Downloading`: the
 * client is downloading it. `Paused`: paused in the client. `Downloaded`: the client has all of it,
 * and the app stops following it. `Failed`: `error` says why, and nothing new reached the client.
 * `NeedsLinking`: the client took it, but lists more than one job it could be, or gave no id and the
 * release no info hash, so it has to be linked by hand. `Cancelled`: the person stopped following it;
 * the torrent and its files stay in the client.
 *
 * MAM-14 — the app follows `Created` (a send interrupted by a restart), `Submitted`, `Downloading`
 * and `Paused` by reading the client; the others have ended.
 */
export type DownloadJobStatus =
  | 'Created'
  | 'Submitted'
  | 'Downloading'
  | 'Paused'
  | 'Downloaded'
  | 'Failed'
  | 'NeedsLinking'
  | 'Cancelled'

/** One destination of a download client: a category of the client, never a folder. */
export interface DownloadDestinationOption {
  id: string
  name: string
  /** The client's own name for the category, exactly as the client spells it. */
  clientDestinationKey: string
  /** The installation's default destination; at most one is. */
  isDefault: boolean
}

/** A download client that can take the release, with its destinations by name. */
export interface DownloadClientOption {
  id: string
  name: string
  /** The adapter's display name ("qBittorrent"). */
  typeName: string
  /** By name; never empty — a client without a destination isn't listed. */
  destinations: DownloadDestinationOption[]
}

/**
 * `GET /api/downloads/options?releaseId=`: every enabled download client this build can send to that
 * takes the release's payload and has a destination, by name.
 */
export interface DownloadOptions {
  clients: DownloadClientOption[]
  /**
   * MAM-32 — the destination the dialog picks first, always one of `clients`': the one the release's
   * tracker section is mapped to, else the installation's default; null when two destinations tie
   * or there is no default (ADR 0020).
   */
  suggestedDestinationId: string | null
}

/**
 * The body of `POST /api/downloads`. `requestId` is the browser's own id for this submission: the
 * same id sent again answers the job it already made instead of sending the release twice.
 */
export interface SendDownloadRequest {
  requestId: string
  releaseId: string
  downloadClientId: string
  destinationId: string
}

/**
 * A download job: `POST /api/downloads` and `GET /api/downloads/{id}`. The names are copied when the
 * job is made, so the job still reads after the client or the destination is removed.
 */
export interface DownloadJob {
  id: string
  releaseId: string
  releaseTitle: string
  sourceName: string
  downloadClientName: string
  destinationName: string | null
  /** The category actually sent. */
  clientDestinationKey: string | null
  status: DownloadJobStatus
  /** MAM-14 — 0 to 1, as the client last reported it; null until it has. */
  progress: number | null
  /** The client's own id for the job once it lists it; null until then. */
  clientJobId: string | null
  /** Why the job failed, every secret masked; null unless it did. */
  error: string | null
  /**
   * MAM-14 — why a followed job isn't moving on (the client can't be asked, reports an error, or no
   * longer lists the torrent); null when nothing blocks it. The job keeps its status meanwhile. A
   * client's own sentence reaches the installation owner only; anyone else reads the app's.
   */
  blockReason: string | null
  /** UTC with a trailing `Z`. */
  createdUtc: string
  /** UTC with a trailing `Z`. */
  statusChangedUtc: string
}

/** MAM-14 — `GET /api/downloads`: one page of the caller's own jobs, newest first. */
export interface DownloadJobList {
  items: DownloadJob[]
  totalCount: number
  page: number
  pageSize: number
}

/** The query of `GET /api/downloads`; a missing status lists every job. */
export interface DownloadJobsQuery {
  status?: DownloadJobStatus
  page: number
  pageSize: number
}

/** MAM-14 — one file of a job, as the client last listed it. */
export interface DownloadJobFile {
  /** From 0, in the client's order. */
  position: number
  /** Relative to the job's folder in the client, `/`-separated, exactly as the client names it. */
  path: string
  sizeBytes: number | null
  /** 0 to 1; null when the client didn't say. */
  progress: number | null
  /** False when the file is skipped in the client; null when the client didn't say. */
  isWanted: boolean | null
}

/** `GET /api/downloads/{id}/files`: one page of a job's files, by position. */
export interface DownloadJobFileList {
  items: DownloadJobFile[]
  totalCount: number
  page: number
  pageSize: number
}

/** The query of `GET /api/downloads/{id}/files`. */
export interface DownloadJobFilesQuery {
  page: number
  pageSize: number
}
