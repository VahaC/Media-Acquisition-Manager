import type { AccessTokenSummary, CreatedAccessToken } from '@/lib/types'

/** MAM-5 — a token a script used yesterday evening, that expires at the end of the year. */
export const laptopToken: AccessTokenSummary = {
  id: '5d1f7a3e-2c4b-4e8a-9f10-000000000001',
  name: 'Claude on the laptop',
  prefix: 'mam_pat_AbC123',
  access: 'ReadAndDownload',
  createdUtc: '2026-10-01T08:00:00Z',
  lastUsedUtc: '2026-10-15T19:42:00Z',
  expiresUtc: '2026-12-30T08:00:00Z',
  isExpired: false,
}

/** A read-only token that never expires and was never used. */
export const backupToken: AccessTokenSummary = {
  id: '5d1f7a3e-2c4b-4e8a-9f10-000000000002',
  name: 'Backup report',
  prefix: 'mam_pat_Zx9-_q',
  access: 'ReadOnly',
  createdUtc: '2026-09-20T10:30:00Z',
  lastUsedUtc: null,
  expiresUtc: null,
  isExpired: false,
}

/** A token past its expiry: still listed until revoked, but it no longer works. */
export const expiredToken: AccessTokenSummary = {
  id: '5d1f7a3e-2c4b-4e8a-9f10-000000000003',
  name: 'Old cron job',
  prefix: 'mam_pat_Old777',
  access: 'ReadOnly',
  createdUtc: '2026-09-01T06:00:00Z',
  lastUsedUtc: '2026-09-30T06:00:00Z',
  expiresUtc: '2026-10-01T06:00:00Z',
  isExpired: true,
}

/** What `POST /api/profile/tokens` answers: a new token and its secret, 51 characters. */
export const createdToken: CreatedAccessToken = {
  token: {
    id: '5d1f7a3e-2c4b-4e8a-9f10-000000000004',
    name: 'Claude on the NAS',
    prefix: 'mam_pat_Nas4Kq',
    access: 'ReadOnly',
    createdUtc: '2026-10-16T09:00:00Z',
    lastUsedUtc: null,
    expiresUtc: '2027-01-14T09:00:00Z',
    isExpired: false,
  },
  secret: 'mam_pat_Nas4Kq7Rt2Vw9Yz1Bc3Df5Gh7Jk9Lm1Np3Qr5St7Uvw',
}
