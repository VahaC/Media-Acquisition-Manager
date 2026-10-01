import type {
  ClientDestination,
  ClientDestinationList,
  Destination,
  DestinationCheck,
  DestinationList,
  DestinationsQuery,
  IntegrationSummary,
} from '@/lib/types'
import { localQbittorrent } from '@/test/integrations'

// MAM-11 — destinations as GET /api/settings/destinations describes them: the categories of the
// qBittorrent sample (VendorSamples/QBittorrent/categories.json) through the path mapping
// /downloads → /media, landing in the libraries of the Jellyfin sample (virtual-folders.json).
// Client ids match test/integrations.ts; addresses are http://127.0.0.1:9, never a host on the LAN.

/** A check that found the folder inside a media library; `changes` sets what a test is about. */
export function checkOf(changes: Partial<DestinationCheck> = {}): DestinationCheck {
  return {
    status: 'InLibrary',
    servicePath: '/downloads/movies',
    appPath: '/media/movies',
    libraryNames: ['Фільми'],
    problem: null,
    ...changes,
  }
}

/** A second download client, switched off. */
export const spareQbittorrent: IntegrationSummary = {
  ...localQbittorrent,
  id: '5d1c7a90-0000-4000-8000-000000000005',
  name: 'Spare qBittorrent',
  isEnabled: false,
  baseUrl: 'http://127.0.0.1:9/spare',
}

/** The installation's default destination. */
export const movies: Destination = {
  id: '6a3e9b20-0000-4000-8000-000000000001',
  name: 'Movies',
  isDefault: true,
  downloadClientId: localQbittorrent.id,
  downloadClientName: localQbittorrent.name,
  clientDestinationKey: 'Movies',
  check: checkOf(),
  createdUtc: '2026-10-01T09:00:00Z',
}

export const cartoons: Destination = {
  id: '6a3e9b20-0000-4000-8000-000000000002',
  name: 'Cartoons',
  isDefault: false,
  downloadClientId: localQbittorrent.id,
  downloadClientName: localQbittorrent.name,
  clientDestinationKey: 'Cartoons',
  check: checkOf({ servicePath: '/downloads/cartoons', appPath: '/media/cartoons', libraryNames: ['Мульфільми'] }),
  createdUtc: '2026-10-01T09:05:00Z',
}

/** A category whose folder lies outside every library folder. */
export const incoming: Destination = {
  id: '6a3e9b20-0000-4000-8000-000000000003',
  name: 'Incoming',
  isDefault: false,
  downloadClientId: localQbittorrent.id,
  downloadClientName: localQbittorrent.name,
  clientDestinationKey: 'Incoming',
  check: checkOf({
    status: 'OutsideLibraries',
    servicePath: '/downloads/incoming',
    appPath: '/media/incoming',
    libraryNames: [],
    problem: '“/media/incoming” is outside every media library folder, so automatic replacement stays off for this destination.',
  }),
  createdUtc: '2026-10-01T09:10:00Z',
}

/** The page the server answers `query` with, out of `total` destinations. */
export function destinationsPage(query: DestinationsQuery, items: Destination[], total = items.length): DestinationList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}

function categoryOf(name: string, folder: string, library: string): ClientDestination {
  return {
    key: name,
    displayName: name,
    check: checkOf({ servicePath: `/downloads/${folder}`, appPath: `/media/${folder}`, libraryNames: [library] }),
  }
}

/** The four sample categories, as the client lists them. */
export const sampleCategories: ClientDestinationList = {
  items: [
    categoryOf('Cartoons', 'cartoons', 'Мульфільми'),
    categoryOf('Documentary', 'documentary', 'Документалки'),
    categoryOf('Movies', 'movies', 'Фільми'),
    categoryOf('TV Shows', 'tv-shows', 'Серіали'),
  ],
  error: null,
}
