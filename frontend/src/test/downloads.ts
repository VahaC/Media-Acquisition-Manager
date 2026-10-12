import type {
  DownloadClientOption,
  DownloadDestinationOption,
  DownloadJob,
  DownloadJobFile,
  DownloadJobFileList,
  DownloadJobFilesQuery,
  DownloadJobList,
  DownloadJobsQuery,
  DownloadOptions,
} from '@/lib/types'
import { cartoons, movies } from '@/test/destinations'
import { localQbittorrent } from '@/test/integrations'
import { tolokaRemux } from '@/test/search'

// MAM-13 — what /api/downloads answers for the search's Toloka release: the download clients and
// destinations it can be sent to (ids match test/destinations.ts and test/integrations.ts), and the
// job a send makes. Addresses never appear: the dialog lists names only.

/** A destination as the dialog lists it, from a destination of the settings fixtures. */
function optionOf(destination: { id: string; name: string; clientDestinationKey: string; isDefault: boolean }): DownloadDestinationOption {
  const { id, name, clientDestinationKey, isDefault } = destination
  return { id, name, clientDestinationKey, isDefault }
}

/** The installation's default destination, of Local qBittorrent. */
export const moviesOption = optionOf(movies)

export const cartoonsOption = optionOf(cartoons)

/** Local qBittorrent with its two destinations, by name; Movies is the default. */
export const localClient: DownloadClientOption = {
  id: localQbittorrent.id,
  name: localQbittorrent.name,
  typeName: 'qBittorrent',
  destinations: [cartoonsOption, moviesOption],
}

/** A second client with two destinations, neither the default. */
export const seedboxClient: DownloadClientOption = {
  id: '5d1c7a90-0000-4000-8000-000000000006',
  name: 'Seedbox qBittorrent',
  typeName: 'qBittorrent',
  destinations: [
    { id: '6a3e9b20-0000-4000-8000-000000000011', name: 'Films', clientDestinationKey: 'films', isDefault: false },
    { id: '6a3e9b20-0000-4000-8000-000000000012', name: 'Series', clientDestinationKey: 'tv', isDefault: false },
  ],
}

/** A third client with a single destination, not the default. */
export const nasClient: DownloadClientOption = {
  id: '5d1c7a90-0000-4000-8000-000000000007',
  name: 'NAS qBittorrent',
  typeName: 'qBittorrent',
  destinations: [{ id: '6a3e9b20-0000-4000-8000-000000000021', name: 'Archive', clientDestinationKey: 'Archive', isDefault: false }],
}

/**
 * The options the server answers with `clients`, in the order given (the server's is by name). MAM-32
 * — with the default destination among them suggested, as the server suggests it when no tracker
 * section of the release is mapped; `suggestedOf` sets another.
 */
export function optionsOf(...clients: DownloadClientOption[]): DownloadOptions {
  const suggested = clients.flatMap((client) => client.destinations).find((destination) => destination.isDefault)
  return { clients, suggestedDestinationId: suggested?.id ?? null }
}

/** MAM-32 — the options of `clients` with `destinationId` suggested: a mapped section's, or null for none. */
export function suggestedOf(destinationId: string | null, ...clients: DownloadClientOption[]): DownloadOptions {
  return { clients, suggestedDestinationId: destinationId }
}

/** The job a send of the Toloka release to Local qBittorrent's Movies makes; `changes` sets the outcome. */
export function jobOf(changes: Partial<DownloadJob>): DownloadJob {
  return {
    id: 'd0b10000-0000-4000-8000-000000000001',
    releaseId: tolokaRemux.id,
    releaseTitle: tolokaRemux.title,
    sourceName: tolokaRemux.sourceName,
    downloadClientName: localClient.name,
    destinationName: moviesOption.name,
    clientDestinationKey: moviesOption.clientDestinationKey,
    status: 'Submitted',
    progress: null,
    clientJobId: '8a2f1c0e9b7d6a5f4e3d2c1b0a9f8e7d6c5b4a39',
    error: null,
    blockReason: null,
    overwriteCheck: 'Checked',
    replacement: null,
    createdUtc: '2026-10-02T09:00:00Z',
    statusChangedUtc: '2026-10-02T09:00:01Z',
    ...changes,
  }
}

// MAM-14 — the Jobs page: the caller's own jobs as GET /api/downloads lists them, and a job's files
// as GET /api/downloads/{id}/files does: paths under the torrent's root folder, as qBittorrent names
// them.

/**
 * When a job sent `minutes` ago was made, by the clock the test runs on (a faked one included): a
 * `Created` job is offered Cancel only once it is 5 minutes old, judged against the time the page's
 * answer came back, so its age can't be pinned to a fixed date.
 */
export function sentMinutesAgo(minutes: number): string {
  return new Date(Date.now() - minutes * 60_000).toISOString()
}

/** The Toloka release downloading in Local qBittorrent, 45.67% through. */
export const downloadingJob = jobOf({ status: 'Downloading', progress: 0.4567 })

/** A second job, downloaded: its client, destination and title differ from the first. */
export const downloadedJob = jobOf({
  id: 'd0b10000-0000-4000-8000-000000000002',
  releaseTitle: 'Alien (1979) Director\'s Cut 1080p BDRip',
  sourceName: 'Toloka',
  downloadClientName: 'Seedbox qBittorrent',
  destinationName: 'Films',
  clientDestinationKey: 'films',
  status: 'Downloaded',
  progress: 1,
  clientJobId: '1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e',
  createdUtc: '2026-10-01T18:30:00Z',
  statusChangedUtc: '2026-10-01T19:42:00Z',
})

/**
 * The page the server answers `query` with, out of `total` jobs. MAM-41 — with the caller's jobs that
 * need them and that are in progress counted as `counts` says: none unless a test is about the tabs.
 */
export function jobsPage(
  query: DownloadJobsQuery,
  items: DownloadJob[],
  total = items.length,
  counts: Partial<Pick<DownloadJobList, 'needsYouCount' | 'inProgressCount'>> = {},
): DownloadJobList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize, needsYouCount: 0, inProgressCount: 0, ...counts }
}

/** One file of a job, wanted and part-way through; `changes` sets what a test is about. */
export function fileOf(changes: Partial<DownloadJobFile>): DownloadJobFile {
  return {
    position: 0,
    path: 'Interstellar.2014.2160p.UHD.BluRay.Remux/Interstellar.2014.2160p.UHD.BluRay.Remux.mkv',
    sizeBytes: 85_899_345_920,
    progress: 0.4567,
    isWanted: true,
    ...changes,
  }
}

/** The page of files the server answers `query` with, out of `total` files. */
export function filesPage(query: DownloadJobFilesQuery, items: DownloadJobFile[], total = items.length): DownloadJobFileList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}
