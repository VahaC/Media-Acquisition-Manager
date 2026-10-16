/**
 * MAM-5 — what a personal access token may do. Read only reads what its user reads; Read and download
 * also sends downloads (Keep both), cancels following and marks Not interested (ADR 0040).
 */
export type AccessTokenAccess = 'ReadOnly' | 'ReadAndDownload'

/** MAM-5 — how long a new token works: 30 days, 90 days, a year, or until it is revoked. */
export type AccessTokenLifetime = 'Days30' | 'Days90' | 'OneYear' | 'Never'

/** One of the signed-in user's tokens, as the Profile page lists it. Never carries the secret. */
export interface AccessTokenSummary {
  id: string
  name: string
  /** The secret's first characters (`mam_pat_` and six more), to tell tokens apart. */
  prefix: string
  access: AccessTokenAccess
  /** UTC with a trailing `Z`. */
  createdUtc: string
  /** UTC with a trailing `Z`; null until a script first uses it. Written at most once a minute. */
  lastUsedUtc: string | null
  /** UTC with a trailing `Z`; null for a token that never expires. */
  expiresUtc: string | null
  /** Past its expiry: listed until revoked, but it no longer works. */
  isExpired: boolean
}

/** `GET /api/profile/tokens`: the user's tokens that aren't revoked, newest first, unpaged. */
export interface AccessTokenList {
  items: AccessTokenSummary[]
}

/** The body of `POST /api/profile/tokens`. */
export interface CreateAccessTokenRequest {
  name: string
  access: AccessTokenAccess
  lifetime: AccessTokenLifetime
}

/** The answer to `POST /api/profile/tokens`: the token, and its secret — the only time it is sent. */
export interface CreatedAccessToken {
  token: AccessTokenSummary
  secret: string
}
