import type { SecretDraft, SecretValueUpsert } from './types'

/**
 * A secret field of the integration dialog as the API takes it: a typed value is stored (`Set`), a
 * ticked removal empties the stored one (`Clear`), and an untouched field is left out, which keeps
 * what is stored (`Keep`) — the dialog never knows the stored value, so it could not send it back.
 *
 * MAM-7 — a typed value goes as typed: a password is never trimmed, and the server's sentence says
 * what is wrong with a blank key.
 */
export function toSecretUpsert(draft: SecretDraft): SecretValueUpsert | undefined {
  if (draft.clear) return { mode: 'Clear' }
  if (draft.value !== '') return { mode: 'Set', value: draft.value }
  return undefined
}
