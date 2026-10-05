import type { IntegrationAdapterType, IntegrationAdapterTypeList, IntegrationSummary } from '@/lib/types'

// The three types MAM-7 ships and MAM-33's TMDb, as GET /api/settings/integrations/adapter-types
// describes them, and a few saved integrations. Addresses are http://127.0.0.1:9, never a host on the LAN.

export const jellyfinType: IntegrationAdapterType = {
  typeId: 'jellyfin',
  displayName: 'Jellyfin',
  kind: 'MediaLibrary',
  userName: 'NotUsed',
  password: 'NotUsed',
  apiKey: 'Required',
  reportsFilePaths: true,
}

export const torznabType: IntegrationAdapterType = {
  typeId: 'torznab',
  displayName: 'Torznab',
  kind: 'ReleaseSearch',
  userName: 'NotUsed',
  password: 'NotUsed',
  apiKey: 'Required',
  reportsFilePaths: false,
}

export const qbittorrentType: IntegrationAdapterType = {
  typeId: 'qbittorrent',
  displayName: 'qBittorrent',
  kind: 'DownloadClient',
  userName: 'Optional',
  password: 'Optional',
  apiKey: 'NotUsed',
  reportsFilePaths: true,
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
}

/** In the server's order: by kind, then name. */
export const adapterTypes: IntegrationAdapterTypeList = { items: [jellyfinType, torznabType, qbittorrentType, tmdbType] }

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
}

/** Saved by an older build with a type this one no longer registers. */
export const retiredClient: IntegrationSummary = {
  id: '5d1c7a90-0000-4000-8000-000000000004',
  name: 'Old Transmission',
  adapterType: 'transmission',
  adapterDisplayName: null,
  kind: 'DownloadClient',
  isEnabled: true,
  baseUrl: 'http://127.0.0.1:9/transmission',
  userName: null,
  hasPassword: false,
  hasApiKey: false,
  pathMappings: [],
  createdUtc: '2026-09-23T08:00:00Z',
}
