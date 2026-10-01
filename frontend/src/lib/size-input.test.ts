import { bytesFromGibibytes } from '@/lib/size-input'

describe('bytesFromGibibytes', () => {
  it.each([
    ['4.5', '4831838208'],
    ['1', '1073741824'],
    ['0', '0'],
    [' 2 ', '2147483648'],
    ['0.000000001', '1'],
  ])('turns %j GiB into its byte count in digits', (text, bytes) => {
    // Arrange
    const typed = text

    // Act
    const result = bytesFromGibibytes(typed)

    // Assert
    expect(result).toBe(bytes)
  })

  it('reads a comma as the decimal point', () => {
    // Arrange
    const typed = '4,5'

    // Act
    const result = bytesFromGibibytes(typed)

    // Assert
    expect(result).toBe('4831838208')
  })

  it.each(['', '   '])('reads %j as no bound', (text) => {
    // Arrange
    const typed = text

    // Act
    const result = bytesFromGibibytes(typed)

    // Assert
    expect(result).toBeUndefined()
  })

  it.each(['abc', '-1', '4.5 GiB', 'Infinity', '1e400', '9000000000'])('refuses %j, which is not a size', (text) => {
    // Arrange
    const typed = text

    // Act
    const result = bytesFromGibibytes(typed)

    // Assert
    expect(result).toBeNull()
  })
})
