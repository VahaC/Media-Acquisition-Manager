import { i18n } from './i18n'
import type { RecommendationRun, RecommendationSettings, RecommendationSort, RecommendationTab } from './types'

/** MAM-33 — the tabs in the order the page shows them (Vasyl's decision 2). */
export const RECOMMENDATION_TABS: readonly RecommendationTab[] = ['Movies', 'Series', 'Cartoons', 'Documentaries']

/** MAM-33 — the orders the toolbar offers, in its order; Score, the server's default, first. */
export const RECOMMENDATION_SORTS: readonly RecommendationSort[] = ['Score', 'Rating', 'Votes', 'Newest', 'Oldest', 'Title', 'Releases']

/**
 * MAM-33 — the order the address names (`?sort=Rating`); Score when it names none, or one that isn't
 * an order in this exact spelling, as the server would refuse it.
 */
export function sortFrom(value: string | null): RecommendationSort {
  return RECOMMENDATION_SORTS.find((sort) => sort === value) ?? 'Score'
}

/** MAM-33 — the settings field that switches each tab on or off. */
export const TAB_SETTINGS = {
  Movies: 'movies',
  Series: 'series',
  Cartoons: 'cartoons',
  Documentaries: 'documentaries',
} as const satisfies Record<RecommendationTab, keyof RecommendationSettings>

/** MAM-33 — the tabs `settings` has on, in the page's order. */
export function enabledTabs(settings: RecommendationSettings): RecommendationTab[] {
  return RECOMMENDATION_TABS.filter((tab) => settings[TAB_SETTINGS[tab]])
}

/** MAM-33 — Cartoons and Documentaries hold films and shows alike, so they offer the films/series filter. */
export function tabMixesKinds(tab: RecommendationTab): boolean {
  return tab === 'Cartoons' || tab === 'Documentaries'
}

/** MAM-33 — whether a computation is still under way: the latest one is polled while it is. */
export function isRunUnderWay(run: RecommendationRun | null | undefined): run is RecommendationRun {
  return run?.status === 'Queued' || run?.status === 'Running'
}

/**
 * MAM-33 — season numbers with every run of two or more in a row as a range: [4, 5] → "4–5",
 * [3 … 11] → "3–11", [2] → "2", [5, 7] → "5, 7". Sorted first and duplicates dropped, so the
 * server's order doesn't matter.
 */
export function seasonRanges(seasons: readonly number[]): string {
  const sorted = [...new Set(seasons)].toSorted((a, b) => a - b)
  const parts: string[] = []
  let first = 0
  while (first < sorted.length) {
    let last = first
    while (last + 1 < sorted.length && sorted[last + 1] === sorted[last] + 1) last += 1
    parts.push(last === first
      ? String(sorted[first])
      : i18n.t('recommendations:reasons.seasonRange', { first: sorted[first], last: sorted[last] }))
    first = last + 1
  }
  return parts.join(i18n.t('recommendations:reasons.seasonSeparator'))
}
