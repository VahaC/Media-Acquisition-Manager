import type {
  LibraryMatch,
  SearchRelease,
  SearchReleaseList,
  SearchReleasesQuery,
  SearchResponse,
  SearchSource,
} from '@/lib/types'
import { aliens, continental } from '@/test/library'

// MAM-12 — a stored search as /api/searches describes it: "Aliens" (1986, tt0090605) asked of two
// release sources, Toloka and Other, with the Library's Aliens (test/library.ts) matched by its IMDb
// id. Addresses never appear: a release carries only its public page.

export const searchId = '5ea4c000-0000-4000-8000-000000000001'

/** Toloka as the search's sources list it; `changes` sets the fields a test is about. */
export function searchSourceOf(changes: Partial<SearchSource>): SearchSource {
  return {
    id: '5ea4c000-0000-4000-8000-000000000011',
    integrationId: '5d1c7a90-0000-4000-8000-000000000002',
    name: 'Toloka',
    status: 'Succeeded',
    notSearchedReason: null,
    error: null,
    releaseCount: 2,
    ...changes,
  }
}

export const toloka = searchSourceOf({})

export const other = searchSourceOf({
  id: '5ea4c000-0000-4000-8000-000000000012',
  integrationId: '5d1c7a90-0000-4000-8000-000000000003',
  name: 'Other',
  releaseCount: 1,
})

/** The Library's Aliens, matched by the IMDb id the search asked for, its original title beside it. */
export const aliensMatch: LibraryMatch = {
  work: aliens,
  evidence: 'Identifier',
  matchedIdentifiers: [aliens.identifiers[0]],
  matchedTitle: { text: 'Aliens', kind: 'Original', year: 1986, sourceName: 'Home Jellyfin' },
  conflictingSchemes: [],
}

/** A search for "Aliens" (1986, tt0090605); `changes` sets the fields a test is about. */
export function searchOf(changes: Partial<SearchResponse>): SearchResponse {
  return {
    id: searchId,
    text: 'Aliens',
    year: 1986,
    imdbId: 'tt0090605',
    tmdbId: null,
    createdUtc: '2026-10-01T09:00:00Z',
    library: [aliensMatch],
    sources: [other, toloka],
    facets: { audioLanguages: ['eng', 'ukr'], hasUnknownAudioLanguage: true, resolutions: [2160, 1080], hasUnknownResolution: true },
    ...changes,
  }
}

export const aliensSearch = searchOf({})

/** A release Toloka listed; `changes` sets the fields a test is about. */
export function releaseOf(changes: Partial<SearchRelease>): SearchRelease {
  return {
    id: '5e1e0000-0000-4000-8000-000000000001',
    sourceId: toloka.id,
    sourceName: 'Toloka',
    title: 'Чужі / Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
    sizeBytes: 31_675_383_808,
    seeders: 12,
    peers: 15,
    publishedUtc: '2026-09-28T18:30:00Z',
    sourcePageUrl: 'https://toloka.to/t123456',
    payloadKind: 'TorrentFile',
    claimed: {
      resolution: 1080,
      medium: 'BluRayRemux',
      videoCodec: 'hevc',
      dynamicRanges: [],
      audioLanguages: ['ukr', 'eng'],
      subtitleLanguages: ['eng'],
    },
    claimedWork: { titles: ['Чужі', 'Aliens'], year: 1986 },
    identifiers: [],
    libraryMatches: [{ workId: aliens.id, evidence: 'TitleAndYear', conflictingSchemes: [] }],
    ...changes,
  }
}

export const tolokaRemux = releaseOf({})

/** A 4K release Other listed with the IMDb id it gives, no page at the source and nothing claimed but the resolution. */
export const otherUhd = releaseOf({
  id: '5e1e0000-0000-4000-8000-000000000002',
  sourceId: other.id,
  sourceName: 'Other',
  title: 'Aliens.1986.2160p.UHD.BluRay.x265-GROUP',
  sizeBytes: null,
  seeders: null,
  peers: null,
  publishedUtc: null,
  sourcePageUrl: null,
  payloadKind: null,
  claimed: { resolution: 2160, medium: null, videoCodec: null, dynamicRanges: ['Hdr10', 'DolbyVision'], audioLanguages: [], subtitleLanguages: [] },
  claimedWork: { titles: ['Aliens'], year: 1986 },
  identifiers: [{ scheme: 'Imdb', value: 'tt0090605' }],
  libraryMatches: [{ workId: aliens.id, evidence: 'Identifier', conflictingSchemes: ['Tmdb'] }],
})

/** A release whose title gives no year: nothing read from it, and nothing in the library matched. */
export const untitledRip = releaseOf({
  id: '5e1e0000-0000-4000-8000-000000000003',
  title: 'Aliens BDRip 720p',
  claimed: { resolution: 720, medium: 'BluRayRip', videoCodec: null, dynamicRanges: [], audioLanguages: [], subtitleLanguages: [] },
  claimedWork: { titles: [], year: null },
  libraryMatches: [],
})

/** A library match the Details dialog shows as a second, weaker one: The Continental by title only. */
export const continentalMatch: LibraryMatch = {
  work: continental,
  evidence: 'TitleOnly',
  matchedIdentifiers: [],
  matchedTitle: { text: 'The Continental', kind: 'Display', year: 2023, sourceName: 'Home Jellyfin' },
  conflictingSchemes: [],
}

/** The page the server answers `query` with, out of `total` releases. */
export function releasesPage(query: SearchReleasesQuery, items: SearchRelease[], total = items.length): SearchReleaseList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}
