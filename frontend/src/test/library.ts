import type {
  FileInspectionOverview,
  FileInspectionRun,
  FolderScanOverview,
  FolderScanRun,
  LibraryFileInspection,
  LibraryLocationList,
  LibrarySource,
  LibrarySourceList,
  LibrarySpace,
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
    { scheme: 'Imdb', value: 'tt0090605', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null },
    { scheme: 'Tmdb', value: '679', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null },
  ],
  missingIdentifiers: [],
  episode: null,
  seasons: null,
  link: null,
  posterUrl: null,
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
  identifiers: [{ scheme: 'Imdb', value: 'tt29669983', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null }],
  missingIdentifiers: ['Tmdb'],
  episode: null,
  seasons: null,
  link: null,
  posterUrl: null,
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
  episode: null,
  seasons: null,
  link: null,
  posterUrl: null,
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

/**
 * MAM-32 — Yellowstone as the MAM-32 Jellyfin samples give it: a series with a show's ids and its
 * seasons counted from its episodes, the specials after the regular seasons. No versions: its
 * episodes hold them.
 */
export const yellowstone: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000401',
  kind: 'Series',
  title: 'Єллоустоун',
  originalTitle: 'Yellowstone',
  year: 2018,
  identifiers: [
    { scheme: 'Imdb', value: 'tt4236770', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null },
    { scheme: 'Tvdb', value: '341164', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null },
  ],
  missingIdentifiers: ['TmdbTv'],
  versions: [],
  episode: null,
  seasons: [
    { number: 1, episodeCount: 9 },
    { number: 2, episodeCount: 10 },
    { number: 0, episodeCount: 1 },
    { number: null, episodeCount: 2 },
  ],
  link: null,
  posterUrl: null,
}

/** MAM-32 — an episode of Yellowstone's first season as the episodes endpoint lists it; `changes` sets the fields a test is about. */
export function yellowstoneEpisodeOf(changes: Partial<LibraryWork>): LibraryWork {
  return {
    id: '7a1e0000-0000-4000-8000-000000000111',
    kind: 'Episode',
    title: 'Daybreak',
    originalTitle: null,
    year: 2018,
    identifiers: [{ scheme: 'Imdb', value: 'tt7608156', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null }],
    missingIdentifiers: ['Tvdb'],
    versions: [{
      id: '7a1e0000-0000-4000-8000-000000000121',
      sourceName: 'Home Jellyfin',
      sourceItemId: 'y101',
      editionLabel: null,
      observedUtc: '2026-10-04T08:00:00Z',
      files: [{
        id: '7a1e0000-0000-4000-8000-000000000131',
        servicePath: '/media/tv/Yellowstone/Season 1/Yellowstone S01E01 Daybreak WEB-DL 1080p.mkv',
        appPath: '/library/tv/Yellowstone/Season 1/Yellowstone S01E01 Daybreak WEB-DL 1080p.mkv',
        pathStatus: 'Mapped',
        pathProblem: null,
        sizeBytes: 3_221_225_472,
        locationName: 'Серіали',
        inspection: null,
      }],
    }],
    episode: { seriesId: yellowstone.id, seasonNumber: 1, episodeNumber: 1 },
    seasons: null,
    link: null,
    posterUrl: null,
    ...changes,
  }
}

/**
 * MAM-32 — an episode of no series, a card of its own: Tom & Jerry Kids of the samples, its sixty-five
 * copies cut to seven here, more than the versions table compares.
 */
export const tomAndJerryKids: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000201',
  kind: 'Episode',
  title: 'Tom & Jerry Kids',
  originalTitle: null,
  year: 1990,
  identifiers: [],
  missingIdentifiers: ['Imdb', 'Tvdb'],
  versions: Array.from({ length: 7 }, (_, index) => ({
    id: `7a1e0000-0000-4000-8000-0000000002${String(index + 10)}`,
    sourceName: 'Home Jellyfin',
    sourceItemId: `tj${index}`,
    editionLabel: null,
    observedUtc: '2026-10-04T08:00:00Z',
    files: [{
      id: `7a1e0000-0000-4000-8000-0000000003${String(index + 10)}`,
      servicePath: `/media/cartoons/Tom & Jerry Kids/Tom & Jerry Kids ${String(index + 1).padStart(2, '0')}.avi`,
      appPath: `/library/cartoons/Tom & Jerry Kids/Tom & Jerry Kids ${String(index + 1).padStart(2, '0')}.avi`,
      pathStatus: 'Mapped' as const,
      pathProblem: null,
      sizeBytes: 183_500_800,
      locationName: 'Мультфільми',
      inspection: null,
    }],
  })),
  episode: { seriesId: null, seasonNumber: 1, episodeNumber: 4 },
  seasons: null,
  link: null,
  posterUrl: null,
}

/**
 * MAM-35 — a work's `posterUrl` as the API names it, `/api/library/posters/{itemId}-{tag}.jpg`, from the
 * item ids and Primary image tags the MAM-35 Jellyfin samples recorded: the Theatrical Cut of Aliens,
 * the series Yellowstone, its episode Daybreak, whose own tag isn't the series', and the Tom & Jerry
 * Kids episode of no series.
 */
export const aliensPosterUrl = '/api/library/posters/00a49b2db2200514168431dc028f0a0b-a608b4104259bc0bb01c6ad262c53aed.jpg'
export const yellowstonePosterUrl = '/api/library/posters/bad3e58c7b81156c7bccd6235599accd-7060953c5bc967a2e947ecc514b77a37.jpg'
export const daybreakPosterUrl = '/api/library/posters/9dce0fa40765e5104b1c0a123a281280-c46fbb41d55088253b976fab0894d6a8.jpg'
export const tomAndJerryKidsPosterUrl = '/api/library/posters/4f28ab262a69cb4a9eb85ea904a6dc5d-4a58e8e95f7f6ad8307f541d94e23800.jpg'

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
    // A sync from before series were read (MAM-32) has no count of them.
    seriesCount: null,
    versionsCount: 870,
    filesCount: 870,
    error: null,
    ...changes,
  }
}

/**
 * MAM-19 — the room Home Jellyfin's 870 files take, 6.5 TiB: one name each, as MAM-17 found no file
 * on the media disk with a second name.
 */
const homeJellyfinSpace: LibrarySpace = { sizeBytes: 7_146_825_580_544, fileCount: 870, nameCount: 870, unmeasuredCount: 0 }

/** MAM-19 — the room the twelve files in no library take, 45 GiB, one name each. */
const scannedSpace: LibrarySpace = { sizeBytes: 48_318_382_080, fileCount: 12, nameCount: 12, unmeasuredCount: 0 }

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
    space: homeJellyfinSpace,
    ...changes,
  }
}

/**
 * An inspection of the library's files that read them all; `changes` sets the fields a test is about.
 * MAM-19 — its hashing found no file to hash, as on the media host MAM-17 counted, where no two
 * library files have one size; a run that hasn't listed the candidates yet needs `candidates: null`.
 */
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
    hashing: { candidates: 0, checked: 0, read: 0, problemCount: 0, problem: null },
    error: null,
    ...changes,
  }
}

/**
 * Where the inspection stands; by default a media folder is allowed and the inspection after the
 * last sync has finished. MAM-19 — the space is the total of `sourceOf({})` and `scanOf({})`, none
 * of whose files has a name in the other. `changes` sets the fields a test is about.
 */
export function inspectionOf(changes: Partial<FileInspectionOverview>): FileInspectionOverview {
  return {
    hasAllowedRoots: true,
    activeRun: null,
    lastFinishedRun: inspectionRunOf({}),
    hasQueuedRun: false,
    space: {
      sizeBytes: homeJellyfinSpace.sizeBytes + scannedSpace.sizeBytes,
      fileCount: homeJellyfinSpace.fileCount + scannedSpace.fileCount,
      nameCount: homeJellyfinSpace.nameCount + scannedSpace.nameCount,
      unmeasuredCount: 0,
    },
    ...changes,
  }
}

/**
 * MAM-18 — a scan of the media folders that stored what it found: as many files as MAM-17 counted on
 * the media host, twelve of them in no library. `changes` sets the fields a test is about.
 */
export function scanRunOf(changes: Partial<FolderScanRun>): FolderScanRun {
  return {
    id: '5ca00000-0000-4000-8000-000000000001',
    status: 'Succeeded',
    requestedByUserName: 'vasyl',
    createdUtc: '2026-09-30T08:10:00Z',
    startedUtc: '2026-09-30T08:10:01Z',
    finishedUtc: '2026-09-30T08:12:00Z',
    filesFound: 1_394,
    filesUnreported: 12,
    problemCount: 0,
    problem: null,
    error: null,
    ...changes,
  }
}

/**
 * MAM-18 — where the scan of the media folders stands; by default the last scan has finished and the
 * inspection after it read the twelve names it stored. `changes` sets the fields a test is about.
 */
export function scanOf(changes: Partial<FolderScanOverview>): FolderScanOverview {
  return {
    activeRun: null,
    lastFinishedRun: scanRunOf({}),
    hasQueuedRun: false,
    files: { inspectedCount: 12, problemCount: 0, notInspectedCount: 0, problem: null },
    space: scannedSpace,
    ignoredCount: 0,
    extrasCount: 0,
    ...changes,
  }
}

/**
 * The sources list of `items`, with the inspection as `inspectionOf({})` and the scan as
 * `scanOf({})` describe them.
 */
export function sourcesOf(...items: LibrarySource[]): LibrarySourceList {
  return { items, inspection: inspectionOf({}), scan: scanOf({}) }
}
