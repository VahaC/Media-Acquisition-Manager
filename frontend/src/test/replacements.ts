import type {
  DownloadJobReplacement,
  LibraryFile,
  ReplacementCopy,
  ReplacementDetails,
  ReplacementEpisode,
  ReplacementJobCopy,
  ReplacementJobNewFile,
  ReplacementJobWork,
  ReplacementName,
  ReplacementOptions,
  ReplacementUnavailableReason,
  ReplacementWork,
} from '@/lib/types'
import { aliens, aliensInspection, yellowstone } from '@/test/library'

// MAM-24 — what GET /api/replacements/options answers for the search's releases: the Library's Aliens
// for the Toloka remux (a title-and-year match, so the owner confirms it), and Yellowstone's held
// episodes for the season pack. Ids match test/library.ts.

/** An answer that lists nothing, for `reason`; `destinationProblem` with DestinationNotUsable. */
export function optionsUnavailable(reason: ReplacementUnavailableReason, destinationProblem: string | null = null): ReplacementOptions {
  return { unavailable: reason, destinationProblem, releaseEditions: [], works: [], clients: [] }
}

/** A copy of `file`, of the version `versionId`, that may be replaced; `changes` sets what a test is about. */
export function copyOf(file: LibraryFile, versionId: string, changes: Partial<ReplacementCopy> = {}): ReplacementCopy {
  return {
    file,
    versionId,
    editionLabel: null,
    editions: [],
    dynamicRanges: [],
    otherNames: false,
    unavailable: null,
    holds: [],
    ...changes,
  }
}

const [theatricalVersion, specialVersion] = aliens.versions

/** Aliens' Theatrical Cut, read by ffprobe (SDR): it may be replaced. */
export const theatricalCopy = copyOf(theatricalVersion.files[0], theatricalVersion.id, {
  editionLabel: 'Theatrical Cut',
  editions: ['Theatrical'],
  dynamicRanges: ['Sdr'],
})

/** Aliens' Special Edition, synced after the last inspection: the app hasn't inspected it. */
export const specialCopy = copyOf(specialVersion.files[0], specialVersion.id, {
  editionLabel: 'Special Edition',
  editions: ['SpecialEdition'],
  unavailable: 'NotInspected',
})

/** Aliens as the Toloka remux matched it — by title and year, so a copy of it needs the owner's tick. */
export function aliensWorkOf(changes: Partial<ReplacementWork> = {}): ReplacementWork {
  return {
    workId: aliens.id,
    kind: 'Movie',
    title: 'Чужі',
    year: 1986,
    evidence: 'TitleAndYear',
    conflictingSchemes: [],
    needsConfirmation: true,
    confirmationText: 'This release is Чужі (1986)',
    libraryNames: ['Фільми'],
    unavailable: null,
    copies: [theatricalCopy, specialCopy],
    episodes: [],
    ...changes,
  }
}

/** What the server answers with `works` listed, the Toloka remux's edition word beside them. */
export function replacementOptionsOf(works: ReplacementWork[], changes: Partial<ReplacementOptions> = {}): ReplacementOptions {
  return { unavailable: null, destinationProblem: null, releaseEditions: ['Theatrical'], works, clients: [], ...changes }
}

/** One file of a Yellowstone episode, read by ffprobe, at `path` under the show's folder. */
function episodeFileOf(id: string, path: string): LibraryFile {
  return {
    id,
    servicePath: `/media/tv/Yellowstone/${path}`,
    appPath: `/library/tv/Yellowstone/${path}`,
    pathStatus: 'Mapped',
    pathProblem: null,
    sizeBytes: 3_221_225_472,
    locationName: 'Серіали',
    inspection: aliensInspection,
  }
}

/** The pack torrent the first season's copies are the files of, in Local qBittorrent. */
export const seasonOneTorrent = { clientName: 'Local qBittorrent', torrentId: 'a1b2c3d4e5f60718293a4b5c6d7e8f9001122334', torrentName: 'Yellowstone.S01.1080p.WEB-DL', secondName: false }

/**
 * The first episode: its 1080p copy, a file of the season's pack torrent, and a second version of the
 * same file name in another folder.
 */
export const daybreak: ReplacementEpisode = {
  workId: '7a1e0000-0000-4000-8000-000000000111',
  seasonNumber: 1,
  episodeNumber: 1,
  title: 'Daybreak',
  copies: [
    copyOf(episodeFileOf('7a1e0000-0000-4000-8000-000000000131', 'Season 1/Yellowstone S01E01.mkv'), '7a1e0000-0000-4000-8000-000000000121', {
      holds: [seasonOneTorrent],
    }),
    copyOf(episodeFileOf('7a1e0000-0000-4000-8000-000000000132', 'Season 1 Remux/Yellowstone S01E01.mkv'), '7a1e0000-0000-4000-8000-000000000122', {
      otherNames: true,
    }),
  ],
}

/** The second episode, a file of the same pack torrent. */
export const killTheMessenger: ReplacementEpisode = {
  workId: '7a1e0000-0000-4000-8000-000000000112',
  seasonNumber: 1,
  episodeNumber: 2,
  title: 'Kill the Messenger',
  copies: [
    copyOf(episodeFileOf('7a1e0000-0000-4000-8000-000000000133', 'Season 1/Yellowstone S01E02.mkv'), '7a1e0000-0000-4000-8000-000000000123', {
      holds: [seasonOneTorrent],
    }),
  ],
}

/** The second season's first episode, in a folder not ticked for replacement. */
export const aThundering: ReplacementEpisode = {
  workId: '7a1e0000-0000-4000-8000-000000000113',
  seasonNumber: 2,
  episodeNumber: 1,
  title: 'A Thundering',
  copies: [
    copyOf(episodeFileOf('7a1e0000-0000-4000-8000-000000000134', 'Season 2/Yellowstone S02E01.mkv'), '7a1e0000-0000-4000-8000-000000000124', {
      unavailable: 'FolderNotTicked',
    }),
  ],
}

/** Yellowstone as the season pack matched it, by an id: no confirmation, its held episodes by season. */
export function yellowstoneWorkOf(changes: Partial<ReplacementWork> = {}): ReplacementWork {
  return {
    workId: yellowstone.id,
    kind: 'Series',
    title: 'Єллоустоун',
    year: 2018,
    evidence: 'Identifier',
    conflictingSchemes: [],
    needsConfirmation: false,
    confirmationText: 'This release is Єллоустоун (2018), seasons 1–5',
    libraryNames: ['Серіали'],
    unavailable: null,
    copies: [],
    episodes: [daybreak, killTheMessenger, aThundering],
    ...changes,
  }
}

/** MAM-26 — the replacement's own id, which its details and the owner's actions ask by. */
export const REPLACEMENT_ID = 'e1a50000-0000-4000-8000-000000000001'

/**
 * The replacement a job carries; `changes` sets what a test is about. It offers nothing — no Retry,
 * MAM-27: no Keep both — unless a test says so, so a page's buttons are only the ones a test asks for.
 */
export function replacementOf(changes: Partial<DownloadJobReplacement> = {}): DownloadJobReplacement {
  return {
    id: REPLACEMENT_ID,
    state: 'Scheduled',
    copyCount: 1,
    reason: null,
    switchedOff: false,
    copiesOutsideTickedFolders: 0,
    quarantinedCopyCount: 0,
    stayedFileCount: 0,
    canRetry: false,
    droppedCopyCount: 0,
    dropOnlyCopyCount: 0,
    canCancel: false,
    stop: null,
    checkStage: null,
    checkWaitsUntilUtc: null,
    ...changes,
  }
}

// MAM-26 — what GET /api/replacements/{id} answers for a film moved into /media's quarantine: Alien
// (1979)'s video with its two subtitles, one of which a torrent holds, so it stayed.

/** Where `relative` lands in the quarantine of /media for REPLACEMENT_ID. */
const quarantined = (relative: string) => `/media/.mam-quarantine/${REPLACEMENT_ID}/${relative}`

/** One name of the replacement; `changes` sets what a test is about. */
export function nameOf(changes: Partial<ReplacementName>): ReplacementName {
  return {
    id: 'e1a50000-0000-4000-8000-000000000101',
    kind: 'Video',
    originalPath: '/media/movies/Alien (1979)/Alien.mkv',
    quarantinePath: quarantined('movies/Alien (1979)/Alien.mkv'),
    sizeBytes: 42_949_672_960,
    otherNames: false,
    state: 'Quarantined',
    problem: null,
    ...changes,
  }
}

/** Alien (1979)'s video, moved into the quarantine: 40 GiB. */
export const alienVideo = nameOf({})

/** Its English subtitle, moved with it: 50 KiB. */
export const alienSubtitle = nameOf({
  id: 'e1a50000-0000-4000-8000-000000000102',
  kind: 'Companion',
  originalPath: '/media/movies/Alien (1979)/Alien.en.srt',
  quarantinePath: quarantined('movies/Alien (1979)/Alien.en.srt'),
  sizeBytes: 51_200,
})

/** Its Ukrainian subtitle, held by a torrent, so it stayed in its folder. */
export const alienHeldSubtitle = nameOf({
  id: 'e1a50000-0000-4000-8000-000000000103',
  kind: 'Companion',
  originalPath: '/media/movies/Alien (1979)/Alien.uk.srt',
  quarantinePath: quarantined('movies/Alien (1979)/Alien.uk.srt'),
  sizeBytes: 48_128,
  state: 'Stayed',
  problem: 'Held by the torrent “Alien.1979.Subs” in “Local qBittorrent”, so it stays where it is.',
})

/**
 * The replacement as its job's page asks for it: Quarantined, the video and one subtitle in the
 * quarantine and the held one in its folder, Restore and Delete for good offered. MAM-27 — with no
 * copy, new file or work listed unless a test says so (jobCopyOf, newFileOf, workOf), so a page shows
 * only the rows a test is about.
 */
export function replacementDetailsOf(changes: Partial<ReplacementDetails> = {}): ReplacementDetails {
  return {
    id: REPLACEMENT_ID,
    downloadJobId: 'd0b10000-0000-4000-8000-000000000002',
    state: 'Quarantined',
    reason: null,
    switchedOff: false,
    canRetry: false,
    canRestore: true,
    canDeleteForGood: true,
    quarantinedBytes: alienVideo.sizeBytes + alienSubtitle.sizeBytes,
    freesBytes: alienVideo.sizeBytes + alienSubtitle.sizeBytes,
    names: [alienVideo, alienSubtitle, alienHeldSubtitle],
    canCancel: false,
    stop: null,
    checkStage: null,
    checkStartedUtc: null,
    libraryAskedUtc: null,
    checkWaitsUntilUtc: null,
    dropOnlyCopyCount: 0,
    copies: [],
    newFiles: [],
    works: [],
    ...changes,
  }
}

// MAM-27 — the rows GET /api/replacements/{id} lists for the same film: the ticked copy of Alien
// (1979), the 2160p remux its download brought, and the work Jellyfin's ids are compared with. By
// default the pair is verified and the copy is in the quarantine, as in replacementDetailsOf;
// `changes` sets what a test is about.

/** Alien (1979)'s work: the one the replacement expects, and the one Jellyfin's ids identified the remux as. */
export const ALIEN_WORK_ID = 'e1a50000-0000-4000-8000-000000000401'

/** The remux's own row id, which the copy's pair points at. */
export const ALIEN_NEW_FILE_ID = 'e1a50000-0000-4000-8000-000000000301'

/** One old copy the owner ticked: Alien (1979)'s 40 GiB video, paired with the remux and in the quarantine. */
export function jobCopyOf(changes: Partial<ReplacementJobCopy> = {}): ReplacementJobCopy {
  return {
    id: 'e1a50000-0000-4000-8000-000000000201',
    position: 0,
    appPath: alienVideo.originalPath,
    sizeBytes: alienVideo.sizeBytes,
    workId: ALIEN_WORK_ID,
    seasonNumber: null,
    episodeNumber: null,
    episodeNumberEnd: null,
    editionLabel: null,
    durationMilliseconds: 6_960_000,
    pairedNewFileId: ALIEN_NEW_FILE_ID,
    problem: null,
    onlyDropSettles: false,
    droppedUtc: null,
    status: 'InQuarantine',
    canDrop: false,
    ...changes,
  }
}

/** One new file the download brought: the 2160p HEVC remux, read on the disk and identified by Jellyfin by its ids. */
export function newFileOf(changes: Partial<ReplacementJobNewFile> = {}): ReplacementJobNewFile {
  return {
    id: ALIEN_NEW_FILE_ID,
    position: 0,
    relativePath: 'Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv',
    appPath: '/media/movies/Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv',
    clientSizeBytes: 64_424_509_440,
    clientProgress: 1,
    sizeBytes: 64_424_509_440,
    durationMilliseconds: 6_990_000,
    videoCodec: 'hevc',
    width: 3840,
    height: 2160,
    checkedUtc: '2026-10-10T12:00:00Z',
    kind: 'Movie',
    seasonNumber: null,
    episodeNumber: null,
    episodeNumberEnd: null,
    readUtc: '2026-10-10T12:05:00Z',
    workId: ALIEN_WORK_ID,
    problem: null,
    progress: 'Identified',
    identifiers: [
      { scheme: 'Imdb', value: 'tt0078748', ofSeries: false, setByOwner: false },
      { scheme: 'Tmdb', value: '348', ofSeries: false, setByOwner: false },
    ],
    ...changes,
  }
}

/** The work the replacement expects: Alien (1979), matched by its ids, so no confirmation was needed. */
export function workOf(changes: Partial<ReplacementJobWork> = {}): ReplacementJobWork {
  return {
    workId: ALIEN_WORK_ID,
    kind: 'Movie',
    title: 'Alien',
    year: 1979,
    confirmedByOwner: false,
    identifiers: [
      { scheme: 'Imdb', value: 'tt0078748', ofSeries: false, setByOwner: false },
      { scheme: 'Tmdb', value: '348', ofSeries: false, setByOwner: false },
    ],
    ...changes,
  }
}
