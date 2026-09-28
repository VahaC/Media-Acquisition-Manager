import { i18n } from './i18n'

// Dates, numbers and file sizes on screen, in the UI locale (`i18n.language`, e.g. `en-GB`) instead
// of a format written into a component. Every page formats through here, so a second language
// brings its own date order, separators and unit labels without touching a page. ADR 0001.

/** An instant as local date and time. The API sends UTC with a trailing `Z`. */
export function formatDateTime(value: string | number | Date, options?: Intl.DateTimeFormatOptions): string {
  return new Date(value).toLocaleString(i18n.language, options)
}

/**
 * The local calendar date of an instant. Never the first ten characters of the ISO string: those
 * are the UTC date, a day off near midnight.
 */
export function formatDate(value: string | number | Date, options?: Intl.DateTimeFormatOptions): string {
  return new Date(value).toLocaleDateString(i18n.language, options)
}

export function formatNumber(value: number, options?: Intl.NumberFormatOptions): string {
  return value.toLocaleString(i18n.language, options)
}

const FILE_SIZE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB'] as const

/**
 * A byte count in binary units (1 KiB = 1024 B) with up to two decimals: `7.94 GiB`, the way
 * qBittorrent shows it. Trackers such as Toloka count in the same 1024 steps (they label them GB),
 * so a release shows the same number here as on its source page. Decimal units would put 8.53 GB
 * next to the tracker's 7.94 GB for the same file.
 *
 * The labels come from `common:fileSize.*`, so a language can use its own unit symbols.
 */
export function formatFileSize(bytes: number): string {
  let unit = 0
  let value = bytes
  // 1023.996 KiB would round to "1,024 KiB"; it is shown as 1 MiB instead.
  while (unit < FILE_SIZE_UNITS.length - 1 && Math.abs(value) >= (unit === 0 ? 1024 : 1023.995)) {
    value /= 1024
    unit += 1
  }
  const number = formatNumber(value, { maximumFractionDigits: unit === 0 ? 0 : 2 })
  return i18n.t(`fileSize.${FILE_SIZE_UNITS[unit]}`, { value: number })
}
