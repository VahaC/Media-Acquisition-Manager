import type {
  FileInspectionOverview,
  FileInspectionRun,
  LibraryFileInspection,
  LibraryLocationList,
  LibrarySource,
  LibrarySourceList,
  LibrarySyncRun,
  LibraryWork,
  LibraryWorkList,
  LibraryWorksQuery,
  MediaTrack,
  MediaTrackKind,
} from '@/lib/types'

// The Library as GET /api/library/* describes it after a sync of the MAM-6 Jellyfin samples: the two
// "Aliens" items of Alien. Collection as one work with two versions, The Continental with an IMDb id
// only, and a work the library knew nothing about. Integration ids match test/integrations.ts.

export const homeJellyfinId = '5d1c7a90-0000-4000-8000-000000000001'

/** A track as ffprobe reads it; `changes` sets the fields a test is about. */
export function trackOf(kind: MediaTrackKind, changes: Partial<MediaTrack> = {}): MediaTrack {
  return {
    kind,
    codec: null,
    profile: null,
    width: null,
    height: null,
    dynamicRange: null,
    channels: null,
    channelLayout: null,
    language: null,
    isDefault: false,
    isForced: false,
    isHearingImpaired: false,
    ...changes,
  }
}

/**
 * MAM-9 — what ffprobe read from the Theatrical Cut of the MAM-6 samples: HEVC Main 10, two Ukrainian
 * dubs and the English DTS-HD MA original, forced Ukrainian subtitles and English ones for the hearing
 * impaired. The tracks' titles ("MVO | DniproFilm") are not part of the API.
 */
export const aliensInspection: LibraryFileInspection = {
  status: 'Inspected',
  problem: null,
  inspectedUtc: '2026-09-30T08:05:00Z',
  media: {
    durationMilliseconds: 8_237_456,
    bitRate: 14_418_920,
    tracks: [
      trackOf('Video', { codec: 'hevc', profile: 'Main 10', width: 1920, height: 1040, dynamicRange: 'Sdr', isDefault: true }),
      trackOf('Audio', { codec: 'ac3', channels: 6, channelLayout: '5.1(side)', language: 'ukr', isDefault: true }),
      trackOf('Audio', { codec: 'ac3', channels: 6, channelLayout: '5.1(side)', language: 'ukr' }),
      trackOf('Audio', { codec: 'dts', profile: 'DTS-HD MA', channels: 6, channelLayout: '5.1(side)', language: 'eng' }),
      trackOf('Subtitle', { codec: 'subrip', language: 'ukr', isForced: true }),
      trackOf('Subtitle', { codec: 'subrip', language: 'eng', isHearingImpaired: true }),
      trackOf('Subtitle', { codec: 'hdmv_pgs_subtitle', language: 'eng' }),
    ],
  },
}

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
        inspection: aliensInspection,
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
        // Synced after the last inspection: the app hasn't looked at it yet.
        inspection: null,
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
      inspection: null,
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
        // The inspection's sentence for a file without an app path is the mapper's own.
        inspection: {
          status: 'NoAppPath',
          problem: 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.',
          inspectedUtc: '2026-09-30T08:05:00Z',
          media: null,
        },
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
    inspection: { inspectedCount: 870, problemCount: 0, notInspectedCount: 0, problem: null },
    ...changes,
  }
}

/** An inspection of the library's files that read them all; `changes` sets the fields a test is about. */
export function inspectionRunOf(changes: Partial<FileInspectionRun>): FileInspectionRun {
  return {
    id: '1a000000-0000-4000-8000-000000000001',
    trigger: 'LibrarySynced',
    status: 'Succeeded',
    requestedByUserName: null,
    createdUtc: '2026-09-30T08:00:00Z',
    startedUtc: '2026-09-30T08:00:01Z',
    finishedUtc: '2026-09-30T08:05:00Z',
    filesTotal: 870,
    filesChecked: 870,
    filesProbed: 870,
    error: null,
    ...changes,
  }
}

/**
 * Where the inspection stands; by default a media folder is allowed and the inspection after the
 * last sync has finished. `changes` sets the fields a test is about.
 */
export function inspectionOf(changes: Partial<FileInspectionOverview>): FileInspectionOverview {
  return { hasAllowedRoots: true, activeRun: null, lastFinishedRun: inspectionRunOf({}), hasQueuedRun: false, ...changes }
}

/** The sources list of `items`, with the inspection as `inspectionOf({})` describes it. */
export function sourcesOf(...items: LibrarySource[]): LibrarySourceList {
  return { items, inspection: inspectionOf({}) }
}
