import type { IntegrationAdapterType, IntegrationAdapterTypeList, IntegrationSummary } from '@/lib/types'

// The three types MAM-7 ships, MAM-33's TMDb and MAM-30's Transmission, as GET
// /api/settings/integrations/adapter-types describes them, and a few saved integrations. Addresses
// are http://127.0.0.1:9, never a host on the LAN.

export const jellyfinType: IntegrationAdapterType = {
  typeId: 'jellyfin',
  displayName: 'Jellyfin',
  kind: 'MediaLibrary',
  userName: 'NotUsed',
  password: 'NotUsed',
  apiKey: 'Required',
  reportsFilePaths: true,
  destinationKind: null,
}

export const torznabType: IntegrationAdapterType = {
  typeId: 'torznab',
  displayName: 'Torznab',
  kind: 'ReleaseSearch',
  userName: 'NotUsed',
  password: 'NotUsed',
  apiKey: 'Required',
  reportsFilePaths: false,
  destinationKind: null,
}

export const qbittorrentType: IntegrationAdapterType = {
  typeId: 'qbittorrent',
  displayName: 'qBittorrent',
  kind: 'DownloadClient',
  userName: 'Optional',
  password: 'Optional',
  apiKey: 'NotUsed',
  reportsFilePaths: true,
  destinationKind: 'Category',
}

/**
 * MAM-30 — the folder client: Basic auth like qBittorrent's, and its destinations are folders (the
 * media library folders mapped back through its path mappings, and its own download folder).
 */
export const transmissionType: IntegrationAdapterType = {
  typeId: 'transmission',
  displayName: 'Transmission',
  kind: 'DownloadClient',
  userName: 'Optional',
  password: 'Optional',
  apiKey: 'NotUsed',
  reportsFilePaths: true,
  destinationKind: 'Folder',
}

/** MAM-33 — the metadata catalog: its API Read Access Token goes in the API key field. */
export const tmdbType: IntegrationAdapterType = {
  typeId: 'tmdb',
  displayName: 'TMDb',
  kind: 'Metadata',
  userName: 'NotUsed',
  password: 'NotUsed',
  apiKey: 'Required',
  reportsFilePaths: false,
  destinationKind: null,
}

/** In the server's order: by kind, then name. */
export const adapterTypes: IntegrationAdapterTypeList = { items: [jellyfinType, torznabType, qbittorrentType, transmissionType, tmdbType] }

export const homeJellyfin: IntegrationSummary = {
  id: '5d1c7a90-0000-4000-8000-000000000001',
  name: 'Home Jellyfin',
  adapterType: 'jellyfin',
  adapterDisplayName: 'Jellyfin',
  kind: 'MediaLibrary',
  isEnabled: true,
  baseUrl: 'http://127.0.0.1:9',
  userName: null,
  hasPassword: false,
  hasApiKey: true,
  pathMappings: [{ servicePrefix: '/data/movies', appPrefix: '/media/movies' }],
  createdUtc: '2026-09-20T08:00:00Z',
  destinationKind: null,
}

export const toloka: IntegrationSummary = {
  id: '5d1c7a90-0000-4000-8000-000000000002',
  name: 'Toloka',
  adapterType: 'torznab',
  adapterDisplayName: 'Torznab',
  kind: 'ReleaseSearch',
  isEnabled: false,
  baseUrl: 'http://127.0.0.1:9/1/api',
  userName: null,
  hasPassword: false,
  hasApiKey: true,
  pathMappings: [],
  createdUtc: '2026-09-21T08:00:00Z',
  destinationKind: null,
}

export const localQbittorrent: IntegrationSummary = {
  id: '5d1c7a90-0000-4000-8000-000000000003',
  name: 'Local qBittorrent',
  adapterType: 'qbittorrent',
  adapterDisplayName: 'qBittorrent',
  kind: 'DownloadClient',
  isEnabled: true,
  baseUrl: 'http://127.0.0.1:9/qbt',
  userName: 'admin',
  hasPassword: true,
  hasApiKey: false,
  pathMappings: [{ servicePrefix: '/downloads', appPrefix: '/media' }],
  createdUtc: '2026-09-22T08:00:00Z',
  destinationKind: 'Category',
}

/** MAM-30 — a Transmission whose destinations are folders, with the same mapping as localQbittorrent. */
export const localTransmission: IntegrationSummary = {
  id: '5d1c7a90-0000-4000-8000-000000000008',
  name: 'Local Transmission',
  adapterType: 'transmission',
  adapterDisplayName: 'Transmission',
  kind: 'DownloadClient',
  isEnabled: true,
  baseUrl: 'http://127.0.0.1:9',
  userName: 'admin',
  hasPassword: true,
  hasApiKey: false,
  pathMappings: [{ servicePrefix: '/downloads', appPrefix: '/media' }],
  createdUtc: '2026-09-24T08:00:00Z',
  destinationKind: 'Folder',
}

/**
 * Saved by an older build with a type this one no longer registers. MAM-30 — no longer
 * "transmission", which this build registers.
 */
export const retiredClient: IntegrationSummary = {
  id: '5d1c7a90-0000-4000-8000-000000000004',
  name: 'Old client',
  adapterType: 'retired-client',
  adapterDisplayName: null,
  kind: 'DownloadClient',
  isEnabled: true,
  baseUrl: 'http://127.0.0.1:9/retired',
  userName: null,
  hasPassword: false,
  hasApiKey: false,
  pathMappings: [],
  createdUtc: '2026-09-23T08:00:00Z',
  // A type this build doesn't register has no kind the app can tell.
  destinationKind: null,
}
