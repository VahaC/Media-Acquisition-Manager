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

const DAY_MS = 86_400_000

/**
 * MAM-33 — how many days ago an instant was, in words: "today", "yesterday", "3 days ago". Counted in
 * local calendar days, so last night at 23:50 is "yesterday" at 00:10, not "today". `now` is for tests.
 */
export function formatDaysAgo(value: string | number | Date, now: Date = new Date()): string {
  const then = new Date(value)
  const startOf = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()
  // Rounded: a day with a DST change is 23 or 25 hours long.
  const days = Math.max(0, Math.round((startOf(now) - startOf(then)) / DAY_MS))
  return new Intl.RelativeTimeFormat(i18n.language, { numeric: 'auto' }).format(-days, 'day')
}

/** The units formatTimeAgo counts in, each with how many seconds it holds, largest first. */
const AGO_UNITS: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
]

/**
 * MAM-33 — how long ago an instant was, in its largest whole unit: "now", "12 seconds ago", "2 minutes
 * ago", "1 hour ago". Cut down, never rounded up: 2 minutes 59 seconds is "2 minutes ago". An instant
 * a clock ahead of `now` put in the future reads as now. `now` is when it is measured from: a page
 * that polls passes the time of the last answer, so the words move on with each poll.
 */
export function formatTimeAgo(value: string | number | Date, now: string | number | Date = Date.now()): string {
  const seconds = Math.max(0, Math.floor((new Date(now).getTime() - new Date(value).getTime()) / 1000))
  const format = new Intl.RelativeTimeFormat(i18n.language, { numeric: 'auto' })
  const [unit, size] = AGO_UNITS.find(([, length]) => seconds >= length) ?? ['second', 1]
  return format.format(-Math.floor(seconds / size), unit)
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

/**
 * MAM-9 — how long a file plays, from milliseconds, to the nearest second: `1:26:19` from an hour up,
 * `26:19` under it, the way media players show a length. Plain digits in every locale: a clock time
 * is read the same everywhere, and no locale groups or translates its digits here.
 */
export function formatDuration(milliseconds: number): string {
  const total = Math.max(0, Math.round(milliseconds / 1000))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = String(total % 60).padStart(2, '0')
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`
}

const BIT_RATE_UNITS = ['bps', 'kbps', 'Mbps', 'Gbps'] as const

/**
 * MAM-9 — a bit rate in bits per second, in decimal units (1 kb/s = 1000 b/s) with up to two
 * decimals: `6.01 Mb/s`, `640 kb/s`. Bit rates are counted in steps of 1000 everywhere — ffprobe,
 * MediaInfo and trackers alike — unlike file sizes (formatFileSize).
 *
 * The labels come from `common:bitRate.*`, like the file size units.
 */
export function formatBitRate(bitsPerSecond: number): string {
  let unit = 0
  let value = bitsPerSecond
  // 999.996 kb/s would round to "1,000 kb/s"; it is shown as 1 Mb/s instead.
  while (unit < BIT_RATE_UNITS.length - 1 && Math.abs(value) >= (unit === 0 ? 1000 : 999.995)) {
    value /= 1000
    unit += 1
  }
  const number = formatNumber(value, { maximumFractionDigits: unit === 0 ? 0 : 2 })
  return i18n.t(`bitRate.${BIT_RATE_UNITS[unit]}`, { value: number })
}
