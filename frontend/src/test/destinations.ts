import type {
  ClientDestination,
  ClientDestinationList,
  Destination,
  DestinationCheck,
  DestinationList,
  DestinationSection,
  DestinationSectionList,
  DestinationSectionSource,
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

/** MAM-32 — a section of a release source; `changes` sets what a test is about. */
export function sectionOf(name: string, categoryIds: string[], changes: Partial<DestinationSection> = {}): DestinationSection {
  return { name, categoryIds, isSourceSpecific: true, destinationId: null, destinationName: null, ...changes }
}

/**
 * MAM-32 — Toloka's sections as GET /api/settings/destinations/sections lists them from its real caps:
 * «Мультсеріали» (three ids under one name) mapped to Cartoons, «Українські фільми» to Movies,
 * «Телесеріали» to none, then the standard categories, Movies/HD mapped to Movies.
 */
export const tolokaSections: DestinationSectionSource = {
  id: '5d1c7a90-0000-4000-8000-000000000002',
  name: 'Toloka',
  sections: [
    sectionOf('Українські фільми', ['100016'], { destinationId: movies.id, destinationName: movies.name }),
    sectionOf('Мультсеріали', ['100044', '100125', '100195'], { destinationId: cartoons.id, destinationName: cartoons.name }),
    sectionOf('Телесеріали', ['100137']),
    sectionOf('Movies/HD', ['2040'], { isSourceSpecific: false, destinationId: movies.id, destinationName: movies.name }),
    sectionOf('TV', ['5000'], { isSourceSpecific: false }),
  ],
  error: null,
}

/** The sections list of `sources`, in the order given (the server's is by name). */
export function sectionsOf(...sources: DestinationSectionSource[]): DestinationSectionList {
  return { sources }
}
