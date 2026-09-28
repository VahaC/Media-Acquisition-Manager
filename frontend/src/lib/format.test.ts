import { formatDate, formatDateTime, formatFileSize, formatNumber } from './format'
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
})
