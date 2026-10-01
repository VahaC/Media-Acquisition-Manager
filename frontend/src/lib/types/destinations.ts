/**
 * MAM-11 — where a destination's folder lands, as the API checks it live (ADR 0003): inside a media
 * library folder, or why automatic replacement stays off for it. Only InLibrary comes without a
 * problem sentence.
 */
export type DestinationPathStatus =
  | 'InLibrary'
  | 'OutsideLibraries'
  | 'PathUnknown'
  | 'LibrariesUnknown'
  | 'CategoryMissing'
  | 'ClientDisabled'
  | 'ClientUnavailable'

/** The live check of one destination, or of one category the client offers. Never stored. */
export interface DestinationCheck {
  status: DestinationPathStatus
  /** The folder as the download client sees it; null when the client doesn't say or wasn't asked. */
  servicePath: string | null
  /** The same folder inside this app, through the client's path mappings; null when they give none. */
  appPath: string | null
  /** The media libraries whose folders hold it, by name; empty unless the status is InLibrary. */
  libraryNames: string[]
  /** One readable sentence for every status but InLibrary; null then. */
  problem: string | null
}

/** One row of the Destinations settings page. */
export interface Destination {
  id: string
  name: string
  /** The installation's default destination; at most one is. */
  isDefault: boolean
  downloadClientId: string
  downloadClientName: string
  /** The client's own category name, exactly as the client spells it; never a folder. */
  clientDestinationKey: string
  check: DestinationCheck
  /** UTC with a trailing `Z`. */
  createdUtc: string
}

/** One page of `GET /api/settings/destinations`. */
export interface DestinationList {
  items: Destination[]
  /** Every destination matching the search, across all pages. */
  totalCount: number
  page: number
  pageSize: number
}

/**
 * The query params of `GET /api/settings/destinations`. Search and paging happen on the server;
 * `search` is left out rather than sent empty.
 */
export interface DestinationsQuery {
  search?: string
  page: number
  pageSize: number
}

/**
 * The body of `POST /api/settings/destinations`. A client left out gets the server's "pick one"
 * sentence, where an empty id would be a body it can't read.
 */
export interface CreateDestinationRequest {
  name: string
  downloadClientId?: string
  clientDestinationKey: string
  isDefault: boolean
}

/** The body of `PUT /api/settings/destinations/{id}`: the same fields, every one sent. */
export type UpdateDestinationRequest = CreateDestinationRequest

/** A category the download client offers, with the check its folder would get. */
export interface ClientDestination {
  /** What a destination stores and a download sends: the client's own name for it. */
  key: string
  /** The name to show; product text, never translated. */
  displayName: string
  check: DestinationCheck
}

/**
 * `GET /api/settings/destinations/client-destinations/{downloadClientId}`: 200 whatever the client
 * said. `error` is the sentence when it couldn't be asked, and `items` is empty then.
 */
export interface ClientDestinationList {
  items: ClientDestination[]
  error: string | null
}
