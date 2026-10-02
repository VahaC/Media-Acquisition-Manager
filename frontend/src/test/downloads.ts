import type { DownloadClientOption, DownloadDestinationOption, DownloadJob, DownloadOptions } from '@/lib/types'
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

/** The options the server answers with `clients`, in the order given (the server's is by name). */
export function optionsOf(...clients: DownloadClientOption[]): DownloadOptions {
  return { clients }
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
    clientJobId: '8a2f1c0e9b7d6a5f4e3d2c1b0a9f8e7d6c5b4a39',
    error: null,
    createdUtc: '2026-10-02T09:00:00Z',
    statusChangedUtc: '2026-10-02T09:00:01Z',
    ...changes,
  }
}
