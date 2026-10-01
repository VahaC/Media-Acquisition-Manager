import type { IntegrationCallStatus } from './integrations'
import type { DynamicRange, LibraryIdentifier, LibraryWork, MediaIdentifierScheme } from './library'

// MAM-12 — the stored search of `/api/searches`: the library and every enabled release source asked
// at once, the releases kept so filters, sort and paging read them again without asking the sources.

/** Why a work matched what was sought, strongest first: a registry id, the title and year, the title alone. */
export type MatchEvidence = 'Identifier' | 'TitleAndYear' | 'TitleOnly'

/** Which of a work's titles a source gave: the one it shows, or the original-language one. */
export type MediaTitleKind = 'Display' | 'Original'

/** Why a search didn't ask one release source (MAM-10). */
export type ReleaseSourceNotSearchedReason = 'SecretUnreadable' | 'TypeUnavailable' | 'NoUsableSearch'

/** What a release is downloaded from. */
export type ReleasePayloadKind = 'TorrentFile' | 'Magnet' | 'Nzb'

/** What a release's title says it was made from: a claim, never checked against the file. */
export type ReleaseMedium =
  | 'BluRayRemux'
  | 'BluRay'
  | 'BluRayRip'
  | 'WebDownload'
  | 'WebRip'
  | 'Hdtv'
  | 'Dvd'
  | 'DvdRip'
  | 'TvRip'
  | 'Camera'

/** The orders of a search's release list. The server's default is `seeders`. */
export type SearchReleaseSort = 'seeders' | 'largest' | 'smallest' | 'newest' | 'quality'

/**
 * The body of `POST /api/searches`. Every field a string, the year too, so the server's sentence
 * names what is wrong; a field left blank is left out.
 */
export interface SearchRequest {
  text: string
  year?: string
  imdbId?: string
  tmdbId?: string
}

/** A title of a library work, and the integration that reported it. */
export interface LibraryTitle {
  text: string
  kind: MediaTitleKind
  year: number | null
  sourceName: string | null
}

/**
 * One library work the search matched, with why: the ids it matched by (each with the integration
 * that reported it), the title it matched by, and every scheme whose ids disagree — shown, never
 * merged.
 */
export interface LibraryMatch {
  /** The Library page's work: its versions, files and what the app read from them. */
  work: LibraryWork
  evidence: MatchEvidence
  matchedIdentifiers: LibraryIdentifier[]
  matchedTitle: LibraryTitle | null
  conflictingSchemes: MediaIdentifierScheme[]
}

/** How one release source answered the search. */
export interface SearchSource {
  /** The search's own row for the source: the value of the release list's `source` filter. */
  id: string
  /** Null once the integration is removed; `name` keeps its name. */
  integrationId: string | null
  name: string
  /** How the call went (`Succeeded` when it answered, even with nothing); null when it wasn't asked. */
  status: IntegrationCallStatus | null
  /** Why it wasn't asked; null when it was. */
  notSearchedReason: ReleaseSourceNotSearchedReason | null
  /** What went wrong, every secret masked. The installation owner only: null for everyone else. */
  error: string | null
  releaseCount: number
}

/** What the release filters can offer: the values the stored releases' titles claim. */
export interface SearchFacets {
  /** Lower-case ISO 639-2 codes, in code order. */
  audioLanguages: string[]
  /** Whether a release claims no audio language: the filter's Unknown. */
  hasUnknownAudioLanguage: boolean
  /** Resolution classes (2160, 1080…), largest first. */
  resolutions: number[]
  hasUnknownResolution: boolean
}

/** A stored search: `POST /api/searches` and `GET /api/searches/{id}`. */
export interface SearchResponse {
  id: string
  /** The search as asked, trimmed. */
  text: string
  year: number | null
  imdbId: string | null
  tmdbId: string | null
  /** UTC with a trailing `Z`. */
  createdUtc: string
  /** Strongest first; read again from the library on every GET. */
  library: LibraryMatch[]
  /** By name, as they were at search time. */
  sources: SearchSource[]
  facets: SearchFacets
}

/** The quality and languages a release's title claims (MAM-10); a value it doesn't state is null or empty. */
export interface ClaimedReleaseQuality {
  /** A resolution class (2160, 1080, 720…), not the picture's height. */
  resolution: number | null
  medium: ReleaseMedium | null
  /** In the media inspector's names (`hevc`, `h264`), so it reads like a file's track. */
  videoCodec: string | null
  dynamicRanges: DynamicRange[]
  /** Lower-case ISO 639-2 codes (`ukr`, `eng`; `mul` for "Multi"). */
  audioLanguages: string[]
  subtitleLanguages: string[]
}

/** The work a release's title claims to be: every title it gives, and its year. Empty without a year. */
export interface ReleaseWorkClaim {
  titles: string[]
  year: number | null
}

/** A registry id the release's listing gives. */
export interface ReleaseIdentifier {
  scheme: MediaIdentifierScheme
  value: string
}

/** One library work of the search a release matched, with why, and the schemes whose ids disagree. */
export interface ReleaseLibraryMatch {
  workId: string
  evidence: MatchEvidence
  conflictingSchemes: MediaIdentifierScheme[]
}

/**
 * One stored release of a search. Every value is the listing's claim; where to download it from
 * never comes to the browser (ADR 0004).
 */
export interface SearchRelease {
  id: string
  /** The search source that listed it: the `source` filter's value. */
  sourceId: string | null
  sourceName: string
  title: string
  sizeBytes: number | null
  seeders: number | null
  /** Seeders and leechers together, as the source counts them. */
  peers: number | null
  /** UTC with a trailing `Z`. */
  publishedUtc: string | null
  /** The release's page at the source, without any session; null when it gave none. */
  sourcePageUrl: string | null
  payloadKind: ReleasePayloadKind | null
  claimed: ClaimedReleaseQuality
  claimedWork: ReleaseWorkClaim
  identifiers: ReleaseIdentifier[]
  /** Strongest first; empty when it matches nothing in the library. */
  libraryMatches: ReleaseLibraryMatch[]
}

/** One page of `GET /api/searches/{id}/releases`. */
export interface SearchReleaseList {
  items: SearchRelease[]
  /** Every release matching the filters, across all pages. */
  totalCount: number
  page: number
  pageSize: number
}

/**
 * The query params of `GET /api/searches/{id}/releases`. Filters, sort and paging all happen on the
 * server; a filter not set is left out rather than sent empty.
 */
export interface SearchReleasesQuery {
  /** A search source's id. */
  source?: string
  /** A three-letter code, or `unknown`. */
  language?: string
  /** A resolution class such as `1080`, or `unknown`. */
  quality?: string
  /** Bytes, as digits. */
  minSizeBytes?: string
  maxSizeBytes?: string
  sort?: SearchReleaseSort
  page: number
  pageSize: number
}
