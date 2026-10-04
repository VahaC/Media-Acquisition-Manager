import { i18n } from './i18n'
import { seasonName } from './library-episodes'
import type { ClaimedEpisodes } from './types'

// MAM-32 — what a TV release's title claims, in words: the seasons, and the episodes of one season.
// Claims (ADR 0020), never checked against the files; the Library's names for a season ("Season 1",
// "Specials") are reused, so a claim and a library season read alike.

/** The seasons a release claims: "Season 2", "Specials", or "Seasons 1–5". */
export function claimedSeasons(episodes: ClaimedEpisodes): string {
  return episodes.firstSeason === episodes.lastSeason
    ? seasonName(episodes.firstSeason)
    : i18n.t('search:claimed.seasons', { first: episodes.firstSeason, last: episodes.lastSeason })
}

/** The episodes a release claims of its one season, "Episode 3" or "Episodes 1–9"; null for whole seasons. */
export function claimedEpisodeRange(episodes: ClaimedEpisodes): string | null {
  const { firstEpisode, lastEpisode } = episodes
  if (firstEpisode === null) return null
  return lastEpisode === null || lastEpisode === firstEpisode
    ? i18n.t('library:episode.numbered', { number: firstEpisode })
    : i18n.t('search:claimed.episodes', { first: firstEpisode, last: lastEpisode })
}
