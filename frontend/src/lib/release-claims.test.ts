import { claimedEpisodeRange, claimedSeasons } from './release-claims'
import type { ClaimedEpisodes } from './types'

/** What a TV release's title claims; `changes` sets the fields a test is about. */
const claimOf = (changes: Partial<ClaimedEpisodes>): ClaimedEpisodes =>
  ({ firstSeason: 1, lastSeason: 1, firstEpisode: null, lastEpisode: null, ...changes })

describe('release claims', () => {
  it.each([
    ['one season', claimOf({}), 'Season 1'],
    ['a run of seasons', claimOf({ firstSeason: 1, lastSeason: 5 }), 'Seasons 1–5'],
    ['the specials', claimOf({ firstSeason: 0, lastSeason: 0 }), 'Specials'],
    ['one episode of a season', claimOf({ firstSeason: 2, lastSeason: 2, firstEpisode: 3, lastEpisode: 3 }), 'Season 2'],
  ])('names the seasons of %s', (_, claim, expected) => {
    // Arrange
    const episodes = claim

    // Act
    const seasons = claimedSeasons(episodes)

    // Assert
    expect(seasons).toBe(expected)
  })

  it.each([
    ['whole seasons', claimOf({ lastSeason: 5 }), null],
    ['a run of episodes', claimOf({ firstEpisode: 1, lastEpisode: 9 }), 'Episodes 1–9'],
    ['one episode', claimOf({ firstEpisode: 3, lastEpisode: 3 }), 'Episode 3'],
    ['an episode without a last one', claimOf({ firstEpisode: 4, lastEpisode: null }), 'Episode 4'],
  ])('names the episodes of %s', (_, claim, expected) => {
    // Arrange
    const episodes = claim

    // Act
    const range = claimedEpisodeRange(episodes)

    // Assert
    expect(range).toBe(expected)
  })
})
