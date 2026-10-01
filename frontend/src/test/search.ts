import type {
  LibraryMatch,
  LibraryWork,
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
    libraryUnlistedCount: 0,
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

// MAM-12 — a search for "rambo" (no year, no id), as a library of the Rambo films answers it: Рембо
// IV, whose original title is the word itself, matches by title alone; Рембо III holds the word in
// its original title, and Перша кров only in its folder, so the two are listed by words — no match.

/** A Rambo film of the library with one file in the collection's folder; `changes` sets what tells it apart. */
function ramboOf(id: number, fileName: string, changes: Partial<LibraryWork>): LibraryWork {
  const [version] = continental.versions
  const servicePath = `/media/movies/Rambo. Collection/${fileName}`
  return {
    ...continental,
    id: `7a1e0000-0000-4000-8000-0000000002${id}0`,
    identifiers: [],
    missingIdentifiers: ['Imdb', 'Tmdb'],
    versions: [{
      ...version,
      id: `7a1e0000-0000-4000-8000-0000000002${id}1`,
      files: [{ ...version.files[0], id: `7a1e0000-0000-4000-8000-0000000002${id}2`, servicePath, appPath: servicePath }],
    }],
    ...changes,
  }
}

/** Рембо IV (2008): its original title is the title asked for, so it matches by title alone. */
export const ramboIVMatch: LibraryMatch = {
  work: ramboOf(4, '[04] Rambo (2008) BDRip 1080p [Hurtom].mkv', { title: 'Рембо IV', originalTitle: 'Rambo', year: 2008 }),
  evidence: 'TitleOnly',
  matchedIdentifiers: [],
  matchedTitle: { text: 'Rambo', kind: 'Original', year: 2008, sourceName: 'Home Jellyfin' },
  conflictingSchemes: [],
}

/** Рембо III (1988): its original title holds the word, so it is listed by words with that title. */
export const ramboIIIByWords: LibraryMatch = {
  work: ramboOf(3, '[03] Rambo III (1988) BDRip 1080p [Hurtom].mkv', { title: 'Рембо III', originalTitle: 'Rambo III', year: 1988 }),
  evidence: 'Words',
  matchedIdentifiers: [],
  matchedTitle: { text: 'Rambo III', kind: 'Original', year: 1988, sourceName: 'Home Jellyfin' },
  conflictingSchemes: [],
}

/** Рембо. Перша кров (1982): only its file's folder holds the word, so no title is named. */
export const firstBloodByWords: LibraryMatch = {
  work: ramboOf(1, '[01] Rambo. First Blood (1982) [GER Transfer] BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv', {
    title: 'Рембо. Перша кров',
    originalTitle: 'First Blood',
    year: 1982,
  }),
  evidence: 'Words',
  matchedIdentifiers: [],
  matchedTitle: null,
  conflictingSchemes: [],
}

/** The search for "rambo": the match first, then the two works listed by words. */
export const ramboSearch = searchOf({
  text: 'rambo',
  year: null,
  imdbId: null,
  library: [ramboIVMatch, ramboIIIByWords, firstBloodByWords],
})

/** A release of Rambo III, matched to the work the search lists by words on its own title and year. */
export const ramboIIIRip = releaseOf({
  id: '5e1e0000-0000-4000-8000-000000000004',
  title: 'Rambo III (1988) BDRip 1080p',
  claimed: { resolution: 1080, medium: 'BluRayRip', videoCodec: null, dynamicRanges: [], audioLanguages: [], subtitleLanguages: [] },
  claimedWork: { titles: ['Rambo III'], year: 1988 },
  libraryMatches: [{ workId: ramboIIIByWords.work.id, evidence: 'TitleAndYear', conflictingSchemes: [] }],
})
