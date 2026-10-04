import { episodePlace, seasonName, seasonParam } from './library-episodes'

describe('library episodes', () => {
  it.each([
    [1, 'Season 1'],
    [10, 'Season 10'],
    [0, 'Specials'],
    [null, 'No season'],
  ])('names season %s “%s”', (season, expected) => {
    // Arrange
    const number = season

    // Act
    const name = seasonName(number)

    // Assert
    expect(name).toBe(expected)
  })

  it.each([
    [2, '2'],
    [0, '0'],
    [null, 'None'],
  ])('asks the episodes endpoint for season %s as “%s”', (season, expected) => {
    // Arrange
    const number = season

    // Act
    const param = seasonParam(number)

    // Assert
    expect(param).toBe(expected)
  })

  it.each([
    ['a season and a number', { seasonNumber: 1, episodeNumber: 4 }, 'Season 1 · Episode 4'],
    ['a special', { seasonNumber: 0, episodeNumber: 2 }, 'Specials · Episode 2'],
    ['a number alone', { seasonNumber: null, episodeNumber: 7 }, 'Episode 7'],
    ['a season alone', { seasonNumber: 3, episodeNumber: null }, 'Season 3'],
    ['neither', { seasonNumber: null, episodeNumber: null }, null],
  ])('says where an episode of %s sits', (_, numbers, expected) => {
    // Arrange
    const episode = { seriesId: null, ...numbers }

    // Act
    const place = episodePlace(episode)

    // Assert
    expect(place).toBe(expected)
  })
})
