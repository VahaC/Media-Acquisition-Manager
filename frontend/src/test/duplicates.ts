import type {
  LibraryDuplicateFile,
  LibraryDuplicateGroup,
  LibraryDuplicateList,
  LibraryDuplicatesQuery,
  LibraryDuplicateWork,
  LibraryFile,
} from '@/lib/types'
import { aliens, aliensInspection, yellowstone } from '@/test/library'

// MAM-21 — the Duplicates list as GET /api/library/duplicates describes it for the MAM-6 and MAM-32 samples:
// the Theatrical Cut of Aliens hardlinked into the download client's folder (one file, two names), the
// same bytes copied to a backup disk, Yellowstone's first episode in the HD and the 4K library (one
// episode by its series' TVDB id, the same file name in two folders), and Aliens under a name the folder
// scan found beside the library's own (a possible match by title and year).

/** The Theatrical Cut's file as the Library page shows it. */
const aliensFile = aliens.versions[0].files[0]

/** A file as the Library page shows it; `changes` sets the fields a test is about. */
export function libraryFileOf(changes: Partial<LibraryFile>): LibraryFile {
  return { ...aliensFile, ...changes }
}

/** The Theatrical Cut's work as it holds a file of a group. */
export const aliensHolder: LibraryDuplicateWork = {
  id: aliens.id,
  kind: 'Movie',
  title: 'Чужі',
  year: 1986,
  episode: null,
  seriesTitle: null,
  versionId: aliens.versions[0].id,
  editionLabel: 'Theatrical Cut',
  sourceName: 'Home Jellyfin',
  identifiers: aliens.identifiers,
}

/** A file of a group, read and hashed: the Theatrical Cut by default; `changes` sets the fields a test is about. */
export function duplicateFileOf(changes: Partial<LibraryDuplicateFile>): LibraryDuplicateFile {
  return {
    file: aliensFile,
    readSizeBytes: 14_845_708_473,
    deviceId: 2049,
    inode: 1311,
    linkCount: 2,
    sha256: null,
    editions: ['Theatrical'],
    works: [aliensHolder],
    ...changes,
  }
}

/** A name the folder scan found and no library reports, in the download client's folder. */
export const downloadedAliens = duplicateFileOf({
  file: libraryFileOf({
    id: '7a1e0000-0000-4000-8000-000000000521',
    servicePath: '/media/downloads/complete/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv',
    appPath: '/media/downloads/complete/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv',
    pathStatus: 'Identity',
    sizeBytes: 14_845_708_473,
    locationName: null,
  }),
  works: [],
})

/** One file of the Theatrical Cut under the library's name and the download client's, as the app read it. */
export const sameFileGroup: LibraryDuplicateGroup = {
  key: '0a1b2c3d4e5f60718293a4b5c6d7e8f9',
  class: 'SameFile',
  needsReview: false,
  distinctions: [],
  evidence: {
    identity: { deviceId: 2049, inode: 1311, linkCount: 2, nameCount: 2 },
    sha256: null,
    identifiers: [],
    titles: [],
    durationsAgree: null,
  },
  files: [downloadedAliens, duplicateFileOf({})],
  space: { sizeBytes: 14_845_708_473, fileCount: 1, nameCount: 2, unmeasuredCount: 0 },
  recoverable: { sizeBytes: 0, gap: null },
  decision: null,
}

/** The SHA-256 of the Theatrical Cut's bytes. */
export const aliensSha256 = '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08'

/** The Theatrical Cut copied to the backup disk: another file, the same bytes. */
export const backedUpAliens = duplicateFileOf({
  file: libraryFileOf({
    id: '7a1e0000-0000-4000-8000-000000000522',
    servicePath: '/media/backup/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv',
    appPath: '/media/backup/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv',
    pathStatus: 'Identity',
    locationName: null,
  }),
  deviceId: 2065,
  inode: 77,
  linkCount: 1,
  sha256: aliensSha256,
  works: [],
})

/**
 * The Theatrical Cut and its copy on the backup disk; `recoverable` is what removing all but one would
 * free, ext4 on both by default.
 */
export function byteIdenticalGroupOf(changes: Partial<LibraryDuplicateGroup> = {}): LibraryDuplicateGroup {
  return {
    key: '1b2c3d4e5f60718293a4b5c6d7e8f90a',
    class: 'ByteIdentical',
    needsReview: false,
    distinctions: [],
    evidence: { identity: null, sha256: aliensSha256, identifiers: [], titles: [], durationsAgree: null },
    files: [backedUpAliens, duplicateFileOf({ linkCount: 1, sha256: aliensSha256 })],
    space: { sizeBytes: 29_691_416_946, fileCount: 2, nameCount: 2, unmeasuredCount: 0 },
    recoverable: { sizeBytes: 14_845_708_473, gap: null },
    decision: null,
    ...changes,
  }
}

/** Yellowstone's first episode as it holds a file: the HD library's by default. */
export function daybreakHolderOf(changes: Partial<LibraryDuplicateWork> = {}): LibraryDuplicateWork {
  return {
    id: '7a1e0000-0000-4000-8000-000000000111',
    kind: 'Episode',
    title: 'Daybreak',
    year: 2018,
    episode: { seriesId: yellowstone.id, seasonNumber: 1, episodeNumber: 1 },
    seriesTitle: 'Єллоустоун',
    versionId: '7a1e0000-0000-4000-8000-000000000121',
    editionLabel: null,
    sourceName: 'Home Jellyfin',
    identifiers: [{ scheme: 'Imdb', value: 'tt7608156', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null }],
    ...changes,
  }
}

/** The file name both of Yellowstone's first episodes have, each in its library's folder. */
export const daybreakFileName = 'Yellowstone S01E01 Daybreak WEB-DL.mkv'

/**
 * Yellowstone's first episode in the HD and the 4K library, one episode by its series' TVDB id with its
 * season and episode: the same file name in two folders, an SDR and an HDR10 copy.
 */
export const sameWorkGroup: LibraryDuplicateGroup = {
  key: '2c3d4e5f60718293a4b5c6d7e8f90a1b',
  class: 'SameWork',
  needsReview: true,
  distinctions: [{ kind: 'DynamicRange', scheme: null, ofSeries: false }],
  evidence: {
    identity: null,
    sha256: null,
    identifiers: [
      { scheme: 'Imdb', value: 'tt7608156', seasonNumber: null, episodeNumber: null },
      { scheme: 'Tvdb', value: '341164', seasonNumber: 1, episodeNumber: 1 },
    ],
    titles: [],
    durationsAgree: null,
  },
  files: [
    duplicateFileOf({
      file: libraryFileOf({
        id: '7a1e0000-0000-4000-8000-000000000131',
        servicePath: `/media/tv/Yellowstone/Season 1/${daybreakFileName}`,
        appPath: `/library/tv/Yellowstone/Season 1/${daybreakFileName}`,
        sizeBytes: 3_221_225_472,
        locationName: 'Серіали',
        inspection: null,
      }),
      readSizeBytes: null,
      deviceId: null,
      inode: null,
      linkCount: null,
      editions: [],
      works: [daybreakHolderOf()],
    }),
    duplicateFileOf({
      file: libraryFileOf({
        id: '7a1e0000-0000-4000-8000-000000000132',
        servicePath: `/media/tv-4k/Yellowstone/Season 1/${daybreakFileName}`,
        appPath: `/library/tv-4k/Yellowstone/Season 1/${daybreakFileName}`,
        sizeBytes: 9_663_676_416,
        locationName: 'Серіали 4K',
        inspection: aliensInspection,
      }),
      readSizeBytes: 9_663_676_416,
      deviceId: 2049,
      inode: 4242,
      linkCount: 1,
      editions: [],
      works: [daybreakHolderOf({
        id: '7a1e0000-0000-4000-8000-000000000112',
        versionId: '7a1e0000-0000-4000-8000-000000000122',
        sourceName: 'Home Jellyfin',
        identifiers: [
          { scheme: 'Imdb', value: 'tt7608156', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null },
          { scheme: 'Tvdb', value: '6468512', sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null },
        ],
      })],
    }),
  ],
  space: { sizeBytes: 9_663_676_416, fileCount: 1, nameCount: 2, unmeasuredCount: 1 },
  recoverable: null,
  decision: null,
}

/**
 * The library's Aliens and a name the folder scan found with the same title and year: a possible match,
 * undecided by default; `changes` sets the fields a test is about.
 */
export function possibleGroupOf(changes: Partial<LibraryDuplicateGroup> = {}): LibraryDuplicateGroup {
  return {
    key: '3d4e5f60718293a4b5c6d7e8f90a1b2c',
    class: 'Possible',
    needsReview: true,
    distinctions: [],
    evidence: {
      identity: null,
      sha256: null,
      identifiers: [],
      titles: [{ text: 'Aliens', year: 1986 }],
      durationsAgree: true,
    },
    files: [
      duplicateFileOf({
        file: libraryFileOf({
          id: '7a1e0000-0000-4000-8000-000000000523',
          servicePath: '/media/downloads/complete/Aliens.1986.Special.Edition.1080p.mkv',
          appPath: '/media/downloads/complete/Aliens.1986.Special.Edition.1080p.mkv',
          pathStatus: 'Identity',
          locationName: null,
        }),
        deviceId: 2049,
        inode: 9001,
        linkCount: 1,
        editions: ['SpecialEdition'],
        works: [],
      }),
      duplicateFileOf({ linkCount: 1 }),
    ],
    space: { sizeBytes: 34_465_781_813, fileCount: 2, nameCount: 2, unmeasuredCount: 0 },
    recoverable: null,
    decision: null,
    ...changes,
  }
}

/** The page the server answers `query` with, out of `total` groups. */
export function duplicatesPage(query: LibraryDuplicatesQuery, items: LibraryDuplicateGroup[], total = items.length): LibraryDuplicateList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}
