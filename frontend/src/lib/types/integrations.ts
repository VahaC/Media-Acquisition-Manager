/** What the app uses an integration for. */
export type IntegrationKind = 'MediaLibrary' | 'ReleaseSearch' | 'DownloadClient'

/** Whether a service type takes a credential. */
export type CredentialRequirement = 'NotUsed' | 'Optional' | 'Required'

/**
 * One outcome for every call to any integration, so the UI never depends on one product's fields.
 * The sentence that comes with it is what a person reads; the status only picks the colour.
 */
export type IntegrationCallStatus =
  | 'Succeeded'
  | 'Unreachable'
  | 'TimedOut'
  | 'AuthenticationFailed'
  | 'Refused'
  | 'ServiceError'
  | 'UnexpectedResponse'
  | 'NotFound'

/** What a save or a draft test does with a stored secret. */
export type SecretUpsertMode = 'Keep' | 'Set' | 'Clear'

/** A secret on the wire. Left out means Keep; `value` only with Set. */
export interface SecretValueUpsert {
  mode: SecretUpsertMode
  value?: string
}

/** A folder as the service sees it, and the same folder inside this app's container. */
export interface PathMapping {
  servicePrefix: string
  appPrefix: string
}

/** One row of the Integrations page; secrets never come back, only has* flags. */
export interface IntegrationSummary {
  id: string
  name: string
  /** The registered type id (`jellyfin`, `torznab`, `qbittorrent`); never changes. */
  adapterType: string
  /** Null when this version of the app no longer registers the type: it can only be removed. */
  adapterDisplayName: string | null
  kind: IntegrationKind
  isEnabled: boolean
  baseUrl: string
  userName: string | null
  hasPassword: boolean
  hasApiKey: boolean
  /** In the order they were entered. */
  pathMappings: PathMapping[]
  /** UTC with a trailing `Z`. */
  createdUtc: string
}

/** One page of `GET /api/settings/integrations`. */
export interface IntegrationList {
  items: IntegrationSummary[]
  /** Every integration matching the filters, across all pages. */
  totalCount: number
  page: number
  pageSize: number
}

/**
 * The query params of `GET /api/settings/integrations`. Search, filter and paging all happen on the
 * server; an empty search or kind is left out rather than sent empty.
 */
export interface IntegrationsQuery {
  search?: string
  kind?: IntegrationKind
  page: number
  pageSize: number
}

/** A type of service this build can connect to, and the credentials it takes. */
export interface IntegrationAdapterType {
  typeId: string
  /** A product name; shown as is, never translated. */
  displayName: string
  kind: IntegrationKind
  userName: CredentialRequirement
  password: CredentialRequirement
  apiKey: CredentialRequirement
  /** Whether the service reports file paths, so path mappings apply to it. */
  reportsFilePaths: boolean
}

/** `GET /api/settings/integrations/adapter-types`, ordered by kind, then name. */
export interface IntegrationAdapterTypeList {
  items: IntegrationAdapterType[]
}

/**
 * The body of `POST /api/settings/integrations`. A credential or the mappings the type doesn't use
 * are left out; a secret left out stores none.
 */
export interface CreateIntegrationRequest {
  adapterType: string
  name: string
  baseUrl: string
  userName?: string
  password?: SecretValueUpsert
  apiKey?: SecretValueUpsert
  isEnabled: boolean
  pathMappings?: PathMapping[]
}

/**
 * The body of `PUT /api/settings/integrations/{id}`. A secret left out keeps the stored one; the
 * mappings left out stay as they are, `[]` removes them all. The type never changes.
 */
export type UpdateIntegrationRequest = Omit<CreateIntegrationRequest, 'adapterType'>

/**
 * The body of `POST /api/settings/integrations/test`: an unsaved draft. Without `integrationId` it
 * is a new integration of `adapterType`; with it, the edit of that integration, whose stored type is
 * used and whose stored secret stands in for one left out.
 */
export interface TestIntegrationRequest {
  integrationId?: string
  adapterType?: string
  baseUrl: string
  userName?: string
  password?: SecretValueUpsert
  apiKey?: SecretValueUpsert
}

/** What a connection test answers, whatever the service said: 200 with a sentence. */
export interface ConnectionTestResult {
  status: IntegrationCallStatus
  message: string
}

/** What a secret field holds in the dialog; never the stored value. */
export interface SecretDraft {
  value: string
  /** The owner ticked "Remove the stored …". */
  clear: boolean
}

/** A path mapping row being edited; `key` keeps each row's inputs when another row is removed. */
export interface PathMappingDraft extends PathMapping {
  key: number
}
