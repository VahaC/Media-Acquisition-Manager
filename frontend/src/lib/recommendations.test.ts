import { enabledTabs, isRunUnderWay, seasonRanges, sortFrom, tabMixesKinds } from '@/lib/recommendations'
import { allTabsOff, allTabsOn, runOf } from '@/test/recommendations'

describe('seasonRanges', () => {
  it.each([
    [[4, 5], '4–5'],
    [[3, 4, 5, 6, 7, 8, 9, 10, 11], '3–11'],
    [[2], '2'],
    [[5, 7], '5, 7'],
    [[1, 2, 3, 5, 8, 9], '1–3, 5, 8–9'],
    [[9, 8, 2, 2], '2, 8–9'],
  ])('writes the seasons %j as “%s”', (seasons, expected) => {
    // Arrange
    const numbers = seasons

    // Act
    const text = seasonRanges(numbers)

    // Assert
    expect(text).toBe(expected)
  })
})

describe('enabledTabs', () => {
  it('lists the tabs a user has on in the page\'s order', () => {
    // Arrange
    const settings = { ...allTabsOn, series: false, cartoons: false }

    // Act
    const tabs = enabledTabs(settings)

    // Assert
    expect(tabs).toEqual(['Movies', 'Documentaries'])
    expect(enabledTabs(allTabsOn)).toEqual(['Movies', 'Series', 'Cartoons', 'Documentaries'])
    expect(enabledTabs(allTabsOff)).toEqual([])
  })
})

describe('tabMixesKinds', () => {
  it('says only Cartoons and Documentaries hold films and shows alike', () => {
    // Arrange
    const tabs = ['Movies', 'Series', 'Cartoons', 'Documentaries'] as const

    // Act
    const mixed = tabs.filter(tabMixesKinds)

    // Assert
    expect(mixed).toEqual(['Cartoons', 'Documentaries'])
  })
})

describe('isRunUnderWay', () => {
  it.each([
    ['Queued', true],
    ['Running', true],
    ['Succeeded', false],
    ['Failed', false],
  ] as const)('says a %s computation is under way: %s', (status, expected) => {
    // Arrange
    const run = runOf({ status })

    // Act
    const underWay = isRunUnderWay(run)

    // Assert
    expect(underWay).toBe(expected)
    expect(isRunUnderWay(null)).toBe(false)
  })
})

describe('sortFrom', () => {
  it.each([
    ['Rating', 'Rating'],
    ['Releases', 'Releases'],
    ['Score', 'Score'],
    [null, 'Score'],
    ['', 'Score'],
    ['rating', 'Score'],
    ['Popularity', 'Score'],
  ] as const)('reads the order %j in the address as %s', (value, expected) => {
    // Arrange
    const asked = value

    // Act
    const sort = sortFrom(asked)

    // Assert
    expect(sort).toBe(expected)
  })
})
