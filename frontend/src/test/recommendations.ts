import type {
  Recommendation,
  RecommendationFacets,
  RecommendationList,
  RecommendationReason,
  RecommendationReasonKind,
  RecommendationRun,
  RecommendationSettings,
  RecommendationsQuery,
} from '@/lib/types'

// MAM-33 — the Recommendations page as GET /api/recommendations describes it after a computation over
// the MAM-6 Jellyfin samples with TMDb connected: films, a held series with seasons missing and a
// documentary, with every kind of reason. Catalog ids are TMDb's own.

/** Local noon `days` calendar days before today, as UTC with a trailing `Z`: "2 days ago" whatever the hour the test runs at. */
export function daysAgo(days: number): string {
  const now = new Date()
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() - days, 12).toISOString()
}

/** The user's settings as a user who never saved has them: every tab on, 60 searches a day. */
export const allTabsOn: RecommendationSettings = {
  movies: true,
  series: true,
  cartoons: true,
  documentaries: true,
  dailySearchLimit: 60,
  searchesLast24Hours: 12,
}

/** Every tab off: the header hides Recommendations, and the page points to the Profile. */
export const allTabsOff: RecommendationSettings = { ...allTabsOn, movies: false, series: false, cartoons: false, documentaries: false }

/** A reason of `kind`; `changes` sets the facts a test is about. */
export function reasonOf(kind: RecommendationReasonKind, changes: Partial<RecommendationReason> = {}): RecommendationReason {
  return { kind, name: null, held: null, total: null, titles: [], seasons: [], dateUtc: null, ...changes }
}

/** A recommended film; `changes` sets the fields a test is about. */
export function recommendationOf(changes: Partial<Recommendation> = {}): Recommendation {
  return {
    id: 'c0000000-0000-4000-8000-000000000001',
    kind: 'Movie',
    catalogId: '447365',
    title: 'Вартові Галактики 3',
    originalTitle: 'Guardians of the Galaxy Vol. 3',
    year: 2023,
    genres: ['Фантастика', 'Пригоди', 'Комедія'],
    rating: 7.9,
    voteCount: 8905,
    posterUrl: '/api/recommendations/posters/r2J02Z2OpNTctfOSN1Ydgii51I3.jpg',
    reasons: [reasonOf('Collection', { name: 'Guardians of the Galaxy', held: 2, total: 3 })],
    availability: {
      checkedUtc: daysAgo(0),
      found: true,
      releaseCount: 4,
      bestResolution: 2160,
      hasUkrainianAudio: true,
      maxSeeders: 24,
    },
    downloading: false,
    searchText: 'Guardians of the Galaxy Vol. 3 (2023)',
    searchTmdbId: '447365',
    ...changes,
  }
}

/** A film from a collection the library holds two parts of, found at a release source. */
export const guardians = recommendationOf()

/** A film by a director the library holds 12 films of; the sources found nothing three days ago. */
export const martian = recommendationOf({
  id: 'c0000000-0000-4000-8000-000000000002',
  catalogId: '286217',
  title: 'Марсіянин',
  originalTitle: 'The Martian',
  year: 2015,
  genres: ['Драма', 'Фантастика'],
  rating: 7.7,
  voteCount: 21_034,
  posterUrl: null,
  reasons: [reasonOf('Director', { name: 'Ridley Scott', held: 12 })],
  availability: { checkedUtc: daysAgo(3), found: false, releaseCount: 0, bestResolution: null, hasUkrainianAudio: false, maxSeeders: null },
  searchText: 'The Martian (2015)',
  searchTmdbId: '286217',
})

/** A film five library works recommend; the sources were never asked about it. */
export const covenant = recommendationOf({
  id: 'c0000000-0000-4000-8000-000000000003',
  catalogId: '126889',
  title: 'Чужий: Заповіт',
  originalTitle: 'Alien: Covenant',
  year: 2017,
  genres: ['Жахи', 'Фантастика'],
  rating: 6.1,
  voteCount: 1,
  posterUrl: null,
  reasons: [
    reasonOf('Recommended', { held: 5, titles: ['Alien', 'Prometheus', 'Aliens', 'Alien³', 'Alien Resurrection'] }),
    reasonOf('TopRated'),
    reasonOf('NewAtSource', { name: 'Toloka.to', dateUtc: daysAgo(2) }),
  ],
  availability: null,
  searchText: 'Alien: Covenant (2017)',
  searchTmdbId: '126889',
})

/** A series the library holds three seasons of, with two aired since; a download of it is followed. */
export const yellowstone = recommendationOf({
  id: 'c0000000-0000-4000-8000-000000000004',
  kind: 'Series',
  catalogId: '73586',
  title: 'Єллоустоун',
  originalTitle: 'Yellowstone',
  year: 2018,
  genres: ['Драма', 'Вестерн'],
  rating: 8.2,
  voteCount: 3_120,
  posterUrl: null,
  reasons: [
    reasonOf('MissingSeasons', { seasons: [4, 5] }),
    reasonOf('Creator', { name: 'Taylor Sheridan', held: 2 }),
  ],
  availability: { checkedUtc: daysAgo(0), found: true, releaseCount: 1, bestResolution: 1080, hasUkrainianAudio: false, maxSeeders: 3 },
  downloading: true,
  searchText: 'Yellowstone (2018)',
  searchTmdbId: null,
})

/** What the Movies tab's selects offer with the films above. */
export const movieFacets: RecommendationFacets = {
  reasons: [
    { kind: 'Collection', count: 1 },
    { kind: 'Director', count: 1 },
    { kind: 'Recommended', count: 1 },
  ],
  genres: [
    { id: 878, name: 'Фантастика', count: 3 },
    { id: 18, name: 'Драма', count: 1 },
  ],
  decades: [
    { decade: 2010, count: 2 },
    { decade: 2020, count: 1 },
  ],
  kinds: [{ kind: 'Movie', count: 3 }],
}

/** A documentary tab holds films and shows alike. */
export const documentaryFacets: RecommendationFacets = {
  reasons: [{ kind: 'TopRated', count: 3 }],
  genres: [{ id: 99, name: 'Документальний', count: 3 }],
  decades: [{ decade: 2010, count: 3 }],
  kinds: [
    { kind: 'Movie', count: 2 },
    { kind: 'Series', count: 1 },
  ],
}

/** A computation; `changes` sets the fields a test is about. By default it succeeded this morning. */
export function runOf(changes: Partial<RecommendationRun> = {}): RecommendationRun {
  return {
    id: '7e000000-0000-4000-8000-000000000001',
    status: 'Succeeded',
    trigger: 'LibrarySynced',
    requestedByUserName: null,
    startedUtc: '2026-10-05T07:00:01Z',
    finishedUtc: '2026-10-05T07:00:09Z',
    seedCount: 118,
    candidateCount: 240,
    error: null,
    createdUtc: '2026-10-05T07:00:00Z',
    stage: null,
    stageDone: null,
    stageTotal: null,
    ...changes,
  }
}

/** One page of `query`'s tab holding `items`; `changes` sets the rest (the total, the facets, the catalog). */
export function listOf(
  query: RecommendationsQuery,
  items: Recommendation[],
  changes: Partial<RecommendationList> = {},
): RecommendationList {
  return {
    items,
    totalCount: items.length,
    page: query.page,
    pageSize: query.pageSize,
    facets: movieFacets,
    catalogConnected: true,
    lastRun: runOf(),
    failedSources: [],
    ...changes,
  }
}

/** The first request of a tab: no filter, the server's order, page 1 of 25. */
export function firstQueryOf(tab: RecommendationsQuery['tab']): RecommendationsQuery {
  return { tab, reason: undefined, available: undefined, genreId: undefined, decade: undefined, kind: undefined, sort: undefined, page: 1, pageSize: 25 }
}
