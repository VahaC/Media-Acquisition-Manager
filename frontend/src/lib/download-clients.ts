import type { DownloadDestinationKind, IntegrationsQuery, IntegrationSummary } from './types'

// MAM-31 — the installation's download clients, as the Destinations page and the destination dialog
// read them. One query for both: the dialog opens on the list the page already holds, in the words of
// what the clients save to, instead of in neutral words for a moment while it asks again.

/**
 * The download clients to pick from: the server filters them by kind and orders them by name. The
 * API's largest page — an installation has a handful of download clients, not a hundred.
 */
export const DOWNLOAD_CLIENTS: IntegrationsQuery = { kind: 'DownloadClient', page: 1, pageSize: 100 }

/**
 * MAM-30 — the kind of destination every listed client has; null when they differ, when none is
 * listed, or when their type isn't registered. MAM-31 — null is spoken in neutral words, naming
 * neither a category nor a folder: the live check found "category" said where the client was
 * Transmission.
 */
export function sharedKindOf(clients: readonly IntegrationSummary[]): DownloadDestinationKind | null {
  const kinds = new Set(clients.map((client) => client.destinationKind))
  return kinds.size === 1 ? [...kinds][0] : null
}
