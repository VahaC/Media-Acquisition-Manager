import type {
  DownloadJobReplacement,
  LibraryFile,
  ReplacementCopy,
  ReplacementEpisode,
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

/** The replacement a job carries; `changes` sets what a test is about. */
export function replacementOf(changes: Partial<DownloadJobReplacement> = {}): DownloadJobReplacement {
  return { state: 'Scheduled', copyCount: 1, reason: null, switchedOff: false, copiesOutsideTickedFolders: 0, ...changes }
}
