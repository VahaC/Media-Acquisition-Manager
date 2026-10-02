/**
 * MAM-13 — a new id for one submission the browser makes: a random UUID, version 4 (RFC 9562), so
 * the server can tell a resend of the same submission from a new one and never act on it twice.
 *
 * Built from `crypto.getRandomValues`, not `crypto.randomUUID()`: the browser offers randomUUID only
 * in a secure context, and the app is often opened at a LAN address over plain http, which isn't
 * one. getRandomValues is there in every context, and draws from the same generator.
 */
export function newRequestId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  // The version (4: random) in the high nibble of byte 6, the variant (binary 10) in the top bits of
  // byte 8: without them the server's Guid parser still reads it, but it isn't a v4 UUID.
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
