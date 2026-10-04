import { i18n } from './i18n'
import type { LibraryEpisode } from './types'

// MAM-32 — how the Library page names seasons and episodes. Seasons come from the episodes' own
// numbers (ADR 0019): 0 is the specials, and an episode the library gives no season to sits in a
// group of its own.

/** A season's name: "Season 2", "Specials" for season 0, "No season" for the episodes without one. */
export function seasonName(season: number | null): string {
  if (season === null) return i18n.t('library:season.none')
  return season === 0 ? i18n.t('library:season.specials') : i18n.t('library:season.numbered', { number: season })
}

/**
 * The `season` the episodes endpoint takes for a season of the list: its number, or `None` for the
 * episodes without one (a blank `season` would list every season).
 */
export function seasonParam(season: number | null): string {
  return season === null ? 'None' : String(season)
}

/**
 * Where an episode of no series sits, as its card says it: "Season 1 · Episode 4", "Specials ·
 * Episode 2", or the part the library gave; null when it gave neither number.
 */
export function episodePlace(episode: LibraryEpisode): string | null {
  const parts = [
    episode.seasonNumber === null ? null : seasonName(episode.seasonNumber),
    episode.episodeNumber === null ? null : i18n.t('library:episode.numbered', { number: episode.episodeNumber }),
  ].filter((part) => part !== null)
  return parts.length === 0 ? null : parts.join(i18n.t('common:separator'))
}
