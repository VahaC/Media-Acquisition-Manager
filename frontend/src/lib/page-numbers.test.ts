import { pageNumbers } from '@/lib/page-numbers'

describe('pageNumbers', () => {
  it.each([
    [5, 33, [1, 'gap', 4, 5, 6, 'gap', 33]],
    [1, 33, [1, 2, 'gap', 33]],
    [2, 33, [1, 2, 3, 'gap', 33]],
    [32, 33, [1, 'gap', 31, 32, 33]],
    [33, 33, [1, 'gap', 32, 33]],
  ])('shows the first and last page and page %i ±1 of %i, with gaps between', (page, pages, expected) => {
    // Arrange
    const current = page

    // Act
    const result = pageNumbers(current, pages)

    // Assert
    expect(result).toEqual(expected)
  })

  it.each([
    [4, 33, [1, 2, 3, 4, 5, 'gap', 33]],
    [30, 33, [1, 'gap', 29, 30, 31, 32, 33]],
    [4, 7, [1, 2, 3, 4, 5, 6, 7]],
  ])('shows the number rather than a gap that would hide one page (page %i of %i)', (page, pages, expected) => {
    // Arrange
    const current = page

    // Act
    const result = pageNumbers(current, pages)

    // Assert
    expect(result).toEqual(expected)
  })

  it.each([
    [1, 1, [1]],
    [1, 2, [1, 2]],
    [2, 2, [1, 2]],
    [2, 3, [1, 2, 3]],
  ])('lists every page of a short list without gaps (page %i of %i)', (page, pages, expected) => {
    // Arrange
    const current = page

    // Act
    const result = pageNumbers(current, pages)

    // Assert
    expect(result).toEqual(expected)
  })

  it('lists only the pages that exist when the page is past the end', () => {
    // Arrange
    const page = 4

    // Act
    const result = pageNumbers(page, 2)

    // Assert
    expect(result).toEqual([1, 2])
  })
})
