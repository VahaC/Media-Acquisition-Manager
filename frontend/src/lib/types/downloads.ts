// MAM-13 — sending a release to a download client through `/api/downloads`. Every signed-in user may
// send one (MAM-13's decision 1), so the dialog reads its clients and destinations here, by name
// only, rather than from the owner-only settings.

/**
 * Where a download job stands. `Created`: being sent, or the outcome is unknown. `Submitted`: the
 * client took it (`clientJobId` is null while the client hasn't listed it yet). `Downloading` and
 * `Downloaded` come with MAM-14. `Failed`: `error` says why, and nothing new reached the client.
 * `NeedsLinking`: the client took it, but lists more than one job it could be, or gave no id and the
 * release no info hash, so it has to be linked by hand.
 */
export type DownloadJobStatus = 'Created' | 'Submitted' | 'Downloading' | 'Downloaded' | 'Failed' | 'NeedsLinking'

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
  /** The client's own id for the job once it lists it; null until then. */
  clientJobId: string | null
  /** Why the job failed, every secret masked; null unless it did. */
  error: string | null
  /** UTC with a trailing `Z`. */
  createdUtc: string
  /** UTC with a trailing `Z`. */
  statusChangedUtc: string
}
