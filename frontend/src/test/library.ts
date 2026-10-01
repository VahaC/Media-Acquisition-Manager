import type {
  LibraryLocationList,
  LibrarySource,
  LibrarySourceList,
  LibrarySyncRun,
  LibraryWork,
  LibraryWorkList,
  LibraryWorksQuery,
} from '@/lib/types'

// The Library as GET /api/library/* describes it after a sync of the MAM-6 Jellyfin samples: the two
// "Aliens" items of Alien. Collection as one work with two versions, The Continental with an IMDb id
// only, and a work the library knew nothing about. Integration ids match test/integrations.ts.

export const homeJellyfinId = '5d1c7a90-0000-4000-8000-000000000001'

export const aliens: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000001',
  kind: 'Movie',
  title: 'Чужі',
  originalTitle: 'Aliens',
  year: 1986,
  identifiers: [
    { scheme: 'Imdb', value: 'tt0090605', sourceName: 'Home Jellyfin' },
    { scheme: 'Tmdb', value: '679', sourceName: 'Home Jellyfin' },
  ],
  missingIdentifiers: [],
  // Not in edition label order, as a later sync may send them, so the card's own order shows.
  versions: [
    {
      id: '7a1e0000-0000-4000-8000-000000000011',
      sourceName: 'Home Jellyfin',
      sourceItemId: 'a1b2',
      editionLabel: 'Theatrical Cut',
      observedUtc: '2026-09-30T08:00:00Z',
      files: [{
        id: '7a1e0000-0000-4000-8000-000000000021',
        servicePath: '/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv',
        appPath: '/library/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv',
        pathStatus: 'Mapped',
        pathProblem: null,
        sizeBytes: 14_845_708_473,
        locationName: 'Фільми',
      }],
    },
    {
      id: '7a1e0000-0000-4000-8000-000000000012',
      sourceName: 'Home Jellyfin',
      sourceItemId: 'b2c1',
      editionLabel: 'Special Edition',
      observedUtc: '2026-09-30T08:00:00Z',
      files: [{
        id: '7a1e0000-0000-4000-8000-000000000022',
        servicePath: '/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv',
        appPath: '/library/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv',
        pathStatus: 'Mapped',
        pathProblem: null,
        sizeBytes: 19_620_073_340,
        locationName: 'Фільми',
      }],
    },
  ],
}

export const continental: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000002',
  kind: 'Movie',
  title: 'The Continental',
  originalTitle: null,
  year: 2023,
  identifiers: [{ scheme: 'Imdb', value: 'tt29669983', sourceName: 'Home Jellyfin' }],
  missingIdentifiers: ['Tmdb'],
  versions: [{
    id: '7a1e0000-0000-4000-8000-000000000013',
    sourceName: 'Home Jellyfin',
    sourceItemId: 'c3d4',
    editionLabel: null,
    observedUtc: '2026-09-30T08:00:00Z',
    files: [{
      id: '7a1e0000-0000-4000-8000-000000000023',
      servicePath: '/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv',
      // No mappings on the integration: the service's path is the app's path as it is.
      appPath: '/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv',
      pathStatus: 'Identity',
      pathProblem: null,
      sizeBytes: 3_891_719_630,
      locationName: 'Фільми',
    }],
  }],
}

/** Everything the library left out: no title, year, ids, size or library, and a path with no mapping. */
export const unknownWork: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000003',
  kind: 'Movie',
  title: null,
  originalTitle: null,
  year: null,
  identifiers: [],
  missingIdentifiers: ['Imdb', 'Tmdb'],
  versions: [
    {
      id: '7a1e0000-0000-4000-8000-000000000014',
      sourceName: 'Home Jellyfin',
      sourceItemId: 'e5f6',
      editionLabel: null,
      observedUtc: '2026-09-30T08:00:00Z',
      files: [{
        id: '7a1e0000-0000-4000-8000-000000000024',
        servicePath: '/srv/other/untitled.mkv',
        appPath: null,
        pathStatus: 'NotCovered',
        pathProblem: 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.',
        sizeBytes: null,
        locationName: null,
      }],
    },
    {
      id: '7a1e0000-0000-4000-8000-000000000015',
      sourceName: 'Home Jellyfin',
      sourceItemId: 'e5f7',
      editionLabel: 'Disc',
      observedUtc: '2026-09-30T08:00:00Z',
      files: [],
    },
  ],
}

/** The page the server answers `query` with, out of `total` works. */
export function worksPage(query: LibraryWorksQuery, items: LibraryWork[], total = items.length): LibraryWorkList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}

export const locations: LibraryLocationList = {
  items: [
    { id: '10c00000-0000-4000-8000-000000000001', name: 'Мультфільми', sourceName: 'Home Jellyfin' },
    { id: '10c00000-0000-4000-8000-000000000002', name: 'Фільми', sourceName: 'Home Jellyfin' },
  ],
}

/** A run of Home Jellyfin; `changes` sets the fields a test is about. */
export function runOf(changes: Partial<LibrarySyncRun>): LibrarySyncRun {
  return {
    id: '5e000000-0000-4000-8000-000000000001',
    integrationId: homeJellyfinId,
    integrationName: 'Home Jellyfin',
    status: 'Succeeded',
    requestedByUserName: 'vasyl',
    createdUtc: '2026-09-30T07:59:00Z',
    startedUtc: '2026-09-30T07:59:01Z',
    finishedUtc: '2026-09-30T08:00:00Z',
    itemsRead: 870,
    itemsTotal: 870,
    worksCount: 855,
    versionsCount: 870,
    filesCount: 870,
    error: null,
    ...changes,
  }
}

/** Home Jellyfin as the sources list shows it; `changes` sets the fields a test is about. */
export function sourceOf(changes: Partial<LibrarySource>): LibrarySource {
  const lastRun = runOf({})
  return {
    integrationId: homeJellyfinId,
    name: 'Home Jellyfin',
    adapterDisplayName: 'Jellyfin',
    isEnabled: true,
    canReadLibrary: true,
    latestRun: lastRun,
    lastSucceededRun: lastRun,
    filesWithoutAppPathCount: 0,
    pathProblem: null,
    ...changes,
  }
}

export function sourcesOf(...items: LibrarySource[]): LibrarySourceList {
  return { items }
}
