import { formatBitRate, formatDate, formatDateTime, formatDaysAgo, formatDuration, formatFileSize, formatNumber, formatTimeAgo } from './format'
import { i18n } from './i18n'

// A fixed zone, so the expected strings don't depend on the machine running the tests.
const utc = { timeZone: 'UTC' } as const
const instant = '2026-09-28T11:03:00Z'

describe('format', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en-US')
  })

  it('writes a date in the order of the UI locale', async () => {
    // Arrange
    await i18n.changeLanguage('en-US')
    const american = formatDate(instant, utc)

    // Act
    await i18n.changeLanguage('en-GB')
    const british = formatDate(instant, utc)

    // Assert
    expect(american).toBe('9/28/2026')
    expect(british).toBe('28/09/2026')
  })

  it('writes an instant as date and time in the UI locale', async () => {
    // Arrange
    await i18n.changeLanguage('en-GB')

    // Act
    const text = formatDateTime(instant, utc)

    // Assert
    expect(text).toBe('28/09/2026, 11:03:00')
  })

  it('shows the local calendar date, not the UTC one', async () => {
    // Arrange — the tests run in Europe/Kyiv (vitest.config.ts), where 23:30 UTC is the next day.
    await i18n.changeLanguage('en-GB')

    // Act
    const text = formatDate('2026-09-28T23:30:00Z')

    // Assert
    expect(text).toBe('29/09/2026')
  })

  it('shows an instant in local time', async () => {
    // Arrange — Kyiv is UTC+3 in summer time.
    await i18n.changeLanguage('en-GB')

    // Act
    const text = formatDateTime('2026-09-28T23:30:00Z')

    // Assert
    expect(text).toBe('29/09/2026, 02:30:00')
  })

  it('groups digits the way the UI locale does', async () => {
    // Arrange
    await i18n.changeLanguage('en-US')
    const american = formatNumber(1_234_567.5)

    // Act
    await i18n.changeLanguage('en-IN')
    const indian = formatNumber(1_234_567.5)

    // Assert
    expect(american).toBe('1,234,567.5')
    expect(indian).toBe('12,34,567.5')
  })

  it.each([
    [0, '0 B'],
    [1023, '1,023 B'],
    [1024, '1 KiB'],
    [1536, '1.5 KiB'],
    [8_525_510_083, '7.94 GiB'],
    [5 * 1024 ** 4, '5 TiB'],
    [3 * 1024 ** 5, '3,072 TiB'],
  ])('writes %i bytes as %s', (bytes, expected) => {
    // Arrange
    const size = bytes

    // Act
    const text = formatFileSize(size)

    // Assert
    expect(text).toBe(expected)
  })

  it('moves to the next unit when rounding would show 1,024 of the smaller one', () => {
    // Arrange — one byte short of a mebibyte is 1023.999 KiB.
    const bytes = 1024 ** 2 - 1

    // Act
    const text = formatFileSize(bytes)

    // Assert
    expect(text).toBe('1 MiB')
  })

  it('writes a file size with the decimal separator of the UI locale', async () => {
    // Arrange
    await i18n.changeLanguage('de-DE')

    // Act
    const text = formatFileSize(8_525_510_083)

    // Assert
    expect(text).toBe('7,94 GiB')
  })

  it.each([
    [0, '0:00'],
    [999, '0:01'],
    [59_499, '0:59'],
    [59_500, '1:00'],
    [1_579_000, '26:19'],
    [3_599_499, '59:59'],
    [3_599_500, '1:00:00'],
    [5_179_000, '1:26:19'],
    [8_237_456, '2:17:17'],
    [36_061_000, '10:01:01'],
  ])('writes a length of %i ms as %s', (milliseconds, expected) => {
    // Arrange
    const length = milliseconds

    // Act
    const text = formatDuration(length)

    // Assert
    expect(text).toBe(expected)
  })

  it('writes a length with plain digits whatever the UI locale', async () => {
    // Arrange — Arabic would write its own digits through Intl; a clock time stays as it is.
    await i18n.changeLanguage('ar-EG')

    // Act
    const text = formatDuration(5_179_000)

    // Assert
    expect(text).toBe('1:26:19')
  })

  it.each([
    [0, '0 b/s'],
    [999, '999 b/s'],
    [1000, '1 kb/s'],
    [640_000, '640 kb/s'],
    [1_536_000, '1.54 Mb/s'],
    [6_012_345, '6.01 Mb/s'],
    [14_418_920, '14.42 Mb/s'],
    [1_500_000_000, '1.5 Gb/s'],
    [2_000_000_000_000, '2,000 Gb/s'],
  ])('writes %i bits per second as %s', (bitsPerSecond, expected) => {
    // Arrange
    const rate = bitsPerSecond

    // Act
    const text = formatBitRate(rate)

    // Assert
    expect(text).toBe(expected)
  })

  it('moves to the next unit when rounding would show 1,000 of the smaller one', () => {
    // Arrange — 999.996 kb/s.
    const bitsPerSecond = 999_996

    // Act
    const text = formatBitRate(bitsPerSecond)

    // Assert
    expect(text).toBe('1 Mb/s')
  })

  it('writes a bit rate with the decimal separator of the UI locale', async () => {
    // Arrange
    await i18n.changeLanguage('de-DE')

    // Act
    const text = formatBitRate(6_012_345)

    // Assert
    expect(text).toBe('6,01 Mb/s')
  })

  it.each([
    ['the same day', '2026-10-05T05:00:00Z', 'today'],
    ['the day before', '2026-10-04T05:00:00Z', 'yesterday'],
    ['three days before', '2026-10-02T15:00:00Z', '3 days ago'],
  ])('says how many days ago an instant was on %s, in words', (_, instant, expected) => {
    // Arrange — noon in Kyiv.
    const now = new Date('2026-10-05T09:00:00Z')

    // Act
    const text = formatDaysAgo(instant, now)

    // Assert
    expect(text).toBe(expected)
  })

  it('counts local calendar days: 23:50 last night is yesterday at 00:10', () => {
    // Arrange — 00:10 and 23:50 the night before, in Kyiv (UTC+3 in October).
    const now = new Date('2026-10-04T21:10:00Z')
    const instant = '2026-10-04T20:50:00Z'

    // Act
    const text = formatDaysAgo(instant, now)

    // Assert
    expect(text).toBe('yesterday')
  })

  it('counts a day with the clocks put back as one day', () => {
    // Arrange — Kyiv leaves summer time on 25 October 2026, a day 25 hours long.
    const now = new Date('2026-10-26T10:00:00Z')
    const instant = '2026-10-24T09:00:00Z'

    // Act
    const text = formatDaysAgo(instant, now)

    // Assert
    expect(text).toBe('2 days ago')
  })

  it.each([
    ['the same second', '2026-10-05T09:00:00Z', 'now'],
    ['12 seconds before', '2026-10-05T08:59:48Z', '12 seconds ago'],
    ['2 minutes 59 seconds before', '2026-10-05T08:57:01Z', '2 minutes ago'],
    ['an hour and a half before', '2026-10-05T07:30:00Z', '1 hour ago'],
  ])('says how long ago an instant was on %s, in its largest whole unit', (_, instant, expected) => {
    // Arrange
    const now = '2026-10-05T09:00:00Z'

    // Act
    const text = formatTimeAgo(instant, now)

    // Assert
    expect(text).toBe(expected)
  })

  it('says an instant a clock ahead of this one put a few seconds in the future is now', () => {
    // Arrange — the server's clock a little ahead of the browser's.
    const now = '2026-10-05T09:00:00Z'

    // Act
    const text = formatTimeAgo('2026-10-05T09:00:03Z', now)

    // Assert
    expect(text).toBe('now')
  })

  it('says an instant a clock ahead of this one put in the future is today', () => {
    // Arrange
    const now = new Date('2026-10-05T09:00:00Z')

    // Act
    const text = formatDaysAgo('2026-10-06T09:00:00Z', now)

    // Assert
    expect(text).toBe('today')
  })
})
