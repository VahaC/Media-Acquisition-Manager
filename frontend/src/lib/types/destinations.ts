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
  /**
   * MAM-32 — the release sources' sections whose releases the Download dialog sends here first; one
   * mapped to another destination moves here. On an update, left out keeps them as they are and
   * empty maps none.
   */
  sections?: DestinationSectionRequest[]
}

/** The body of `PUT /api/settings/destinations/{id}`: the same fields, `sections` left out to keep them. */
export type UpdateDestinationRequest = CreateDestinationRequest

/** MAM-32 — one section of a release source a save maps to the destination, as the sections list gave it. */
export interface DestinationSectionRequest {
  releaseSourceId: string
  categoryIds: string[]
  name: string
  isSourceSpecific: boolean
}

/**
 * MAM-32 — one section (category) of a release source, with the destination it is mapped to. The ids
 * a source files under one name are one section, unless they are mapped apart: then each part is
 * listed, so the list shows exactly what is stored.
 */
export interface DestinationSection {
  /** Without the gateway's subsection mark («Мультсеріали», not «|-Мультсеріали»). */
  name: string
  /** In the source's order; what a save sends back. */
  categoryIds: string[]
  /** The source's own section rather than a standard category ("TV/HD"); mapped, the source's own wins. */
  isSourceSpecific: boolean
  destinationId: string | null
  destinationName: string | null
}

/** MAM-32 — one release source and its sections. */
export interface DestinationSectionSource {
  id: string
  name: string
  /** Its own order, then the mapped ones it no longer lists; only the mapped ones when it couldn't be asked. */
  sections: DestinationSection[]
  /** Why it couldn't be asked (switched off, unreachable, …); null when it answered. */
  error: string | null
}

/**
 * MAM-32 — `GET /api/settings/destinations/sections`: every release source by name, switched off ones
 * too. Whole, never paged: a save replaces the destination's sections, so the dialog must see every
 * one mapped.
 */
export interface DestinationSectionList {
  sources: DestinationSectionSource[]
}

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
