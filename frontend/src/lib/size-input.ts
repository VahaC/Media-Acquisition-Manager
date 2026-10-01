const BYTES_PER_GIBIBYTE = 1024 ** 3

/**
 * MAM-12 — a size typed in GiB as the byte count the API filters by, in digits: `'4.5'` →
 * `'4831838208'`. The page shows sizes in binary units (formatFileSize), so a bound is typed in the
 * same unit the rows read in, and the API takes plain bytes.
 *
 * A comma reads as the decimal point (`4,5`), as many locales write it. Blank is no bound
 * (`undefined`). Anything that isn't a size — not a number, below zero, or more bytes than a number
 * holds exactly — is `null`: the page says so beside the box rather than putting a byte count the
 * person never typed into the server's sentence.
 */
export function bytesFromGibibytes(text: string): string | null | undefined {
  const trimmed = text.trim()
  if (trimmed === '') return undefined
  const gibibytes = Number(trimmed.replace(',', '.'))
  if (!Number.isFinite(gibibytes) || gibibytes < 0) return null
  const bytes = Math.round(gibibytes * BYTES_PER_GIBIBYTE)
  return Number.isSafeInteger(bytes) ? String(bytes) : null
}
