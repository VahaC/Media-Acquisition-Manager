import type {
  ConfidentSuggestion,
  ConfidentSuggestionList,
  ConfidentSuggestionsQuery,
  LibraryIdentifier,
  LibraryLinkBatch,
  LibraryLinkBatchItem,
  LibraryLinkDialog,
  LibraryLinkItem,
  LibraryLinkList,
  LibraryLinkLocationCount,
  LibraryLinkQueue,
  LibraryLinkRow,
  LibraryLinkSuggestionRun,
  LibraryLinksQuery,
  LibraryWork,
  LibraryWorkLink,
  LinkCandidate,
  LinkEvidence,
} from '@/lib/types'

// MAM-34 — the TMDb dialog as GET /api/library/works/{id}/tmdb describes it, after the live checks of
// 2026-10-05: the Tom & Jerry short "120. Landing Stripling (1962)" (6.2 min in Jellyfin, TMDb film
// 223021 with a runtime of 6 and IMDb tt0056164) in «Мультфільми», and "Planet_Earth_E08 - Jungles",
// a film item of an episode of TMDb show 1044 ("Планета Земля").

export const homeJellyfinItemId = 'b81c6f3a2d9e4f0aa1c2d3e4f5a6b7c8'
export const linkId = '11c00000-0000-4000-8000-000000000001'

/** An id as Home Jellyfin reported it; `changes` sets the fields a test is about. */
export function libraryIdentifierOf(scheme: LibraryIdentifier['scheme'], value: string, changes: Partial<LibraryIdentifier> = {}): LibraryIdentifier {
  return { scheme, value, sourceName: 'Home Jellyfin', sourceKind: 'MediaLibrary', setUtc: null, ...changes }
}

/** The TMDb id the owner linked by hand, as the owner reads it (members get no name). */
export function linkedIdentifierOf(scheme: LibraryIdentifier['scheme'], value: string, changes: Partial<LibraryIdentifier> = {}): LibraryIdentifier {
  return { scheme, value, sourceName: 'vasyl', sourceKind: 'Person', setUtc: '2026-10-05T18:40:00Z', ...changes }
}

/** The short as the Library page lists it before it is linked: no id at all. */
export const landingStripling: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000034',
  kind: 'Movie',
  title: '120. Landing Stripling',
  originalTitle: null,
  year: 1962,
  identifiers: [],
  missingIdentifiers: ['Imdb', 'Tmdb'],
  episode: null,
  seasons: null,
  link: null,
  versions: [{
    id: '7a1e0000-0000-4000-8000-000000000035',
    sourceName: 'Home Jellyfin',
    sourceItemId: homeJellyfinItemId,
    editionLabel: null,
    observedUtc: '2026-10-05T08:00:00Z',
    files: [{
      id: '7a1e0000-0000-4000-8000-000000000036',
      servicePath: '/media/cartoons/Tom & Jerry e001-e161 (1940-1967)/120. Landing Stripling (1962).mkv',
      appPath: null,
      pathStatus: 'NotCovered',
      pathProblem: null,
      sizeBytes: 88_604_672,
      locationName: 'Мультфільми',
      inspection: null,
    }],
  }],
}

/** "Planet_Earth_E08 - Jungles": a film item Jellyfin lists as a movie, an episode of a TMDb show. */
export const planetEarthJungles: LibraryWork = {
  ...landingStripling,
  id: '7a1e0000-0000-4000-8000-000000000044',
  title: 'Planet_Earth_E08 - Jungles',
  year: null,
  versions: [{ ...landingStripling.versions[0], id: '7a1e0000-0000-4000-8000-000000000045', sourceItemId: 'c0ffee00c0ffee00c0ffee00c0ffee08' }],
}

/** Evidence all in a suggestion's favour; `changes` sets the pieces a test is about. */
export function evidenceOf(changes: Partial<LinkEvidence> = {}): LinkEvidence {
  return {
    title: { similarity: 1, exact: true, matchedKind: 'OriginalTitle', matchedTitle: 'Landing Stripling' },
    year: { libraryYear: 1962, catalogYear: 1962, difference: 0 },
    runtime: { libraryMinutes: 6, catalogMinutes: 6, agrees: true },
    kindAgrees: true,
    genre: { libraryRole: 'Animation', agreement: 'Agrees' },
    sibling: null,
    ...changes,
  }
}

/** TMDb film 223021 as the live check read it: runtime 6, IMDb tt0056164, the confident suggestion. */
export function candidateOf(changes: Partial<LinkCandidate> = {}): LinkCandidate {
  return {
    catalogKind: 'Movie',
    catalogId: '223021',
    title: 'Посадка Стріплінга',
    originalTitle: 'Landing Stripling',
    year: 1962,
    overview: 'Том намагається впіймати пташку.',
    runtimeMinutes: 6,
    posterUrl: null,
    imdbId: 'tt0056164',
    isConfident: true,
    evidence: evidenceOf(),
    seasonNumber: null,
    episodeNumber: null,
    ...changes,
  }
}

/** A weaker suggestion beside it: another short of the year, a title only alike. */
export const otherShort = candidateOf({
  catalogId: '223022',
  title: 'Calypso Cat',
  originalTitle: null,
  imdbId: null,
  overview: null,
  isConfident: false,
  evidence: evidenceOf({ title: { similarity: 0.42, exact: false, matchedKind: 'Title', matchedTitle: 'Calypso Cat' } }),
})

/** TMDb show 1044 suggested for "Planet_Earth_E08 - Jungles", its season and episode prefilled. */
export const planetEarthShow = candidateOf({
  catalogKind: 'Series',
  catalogId: '1044',
  title: 'Планета Земля',
  originalTitle: 'Planet Earth',
  year: 2006,
  runtimeMinutes: 53,
  imdbId: 'tt0795176',
  isConfident: false,
  overview: null,
  evidence: evidenceOf({
    title: { similarity: 0.61, exact: false, matchedKind: 'OriginalTitle', matchedTitle: 'Planet Earth' },
    year: { libraryYear: null, catalogYear: 2006, difference: null },
    runtime: { libraryMinutes: null, catalogMinutes: 53, agrees: null },
    kindAgrees: false,
    genre: { libraryRole: null, agreement: 'Unknown' },
  }),
  seasonNumber: 1,
  episodeNumber: 8,
})

/** The short's one library item, not linked; `changes` sets the fields a test is about. */
export function itemOf(changes: Partial<LibraryLinkItem> = {}): LibraryLinkItem {
  return {
    linkId: null,
    sourceItemId: homeJellyfinItemId,
    sourceName: 'Home Jellyfin',
    itemTitle: '120. Landing Stripling',
    itemKind: 'Movie',
    canWrite: true,
    cannotWriteReason: null,
    target: null,
    catalogKind: null,
    catalogId: null,
    catalogTitle: null,
    seasonNumber: null,
    episodeNumber: null,
    imdbId: null,
    writeState: null,
    writeError: null,
    writeStateUtc: null,
    setUtc: null,
    setByUserName: null,
    libraryIdentifiers: [],
    conflictingValue: null,
    ...changes,
  }
}

/** The same item once linked to film 223021 and written; `changes` sets the fields a test is about. */
export function linkedItemOf(changes: Partial<LibraryLinkItem> = {}): LibraryLinkItem {
  return itemOf({
    linkId,
    target: 'Work',
    catalogKind: 'Movie',
    catalogId: '223021',
    catalogTitle: 'Посадка Стріплінга',
    imdbId: 'tt0056164',
    writeState: 'Written',
    writeStateUtc: '2026-10-05T18:40:05Z',
    setUtc: '2026-10-05T18:40:00Z',
    setByUserName: 'vasyl',
    ...changes,
  })
}

/** A work's link to film 223021, written into Jellyfin; `changes` sets the fields a test is about. */
export function workLinkOf(changes: Partial<LibraryWorkLink> = {}): LibraryWorkLink {
  return {
    target: 'Work',
    catalogKind: 'Movie',
    catalogId: '223021',
    catalogTitle: 'Посадка Стріплінга',
    seasonNumber: null,
    episodeNumber: null,
    imdbId: 'tt0056164',
    setByUserName: 'vasyl',
    setUtc: '2026-10-05T18:40:00Z',
    writeState: 'Written',
    writeError: null,
    conflictingValue: null,
    ...changes,
  }
}

/** The short as the Library page lists it once linked: the linked ids, no missing ones. */
export const linkedLandingStripling: LibraryWork = {
  ...landingStripling,
  identifiers: [
    { scheme: 'Imdb', value: 'tt0056164', sourceName: 'TMDb', sourceKind: 'Metadata', setUtc: null },
    linkedIdentifierOf('Tmdb', '223021'),
  ],
  missingIdentifiers: [],
  link: workLinkOf(),
}

/** The short's dialog before it is linked: two suggestions, the confident one first. */
export function dialogOf(changes: Partial<LibraryLinkDialog> = {}): LibraryLinkDialog {
  return {
    work: landingStripling,
    items: [itemOf()],
    catalogConnected: true,
    suggestions: [candidateOf(), otherShort],
    search: { searchedUtc: '2026-10-05T08:01:00Z', noneFitsUtc: null, pending: false, inPopulation: true },
    // The server's cleaning of "120. Landing Stripling".
    searchTitle: 'Landing Stripling',
    searchYear: 1962,
    ...changes,
  }
}

/** The short's dialog once linked and written. */
export function linkedDialogOf(changes: Partial<LibraryLinkDialog> = {}): LibraryLinkDialog {
  return dialogOf({
    work: linkedLandingStripling,
    items: [linkedItemOf()],
    search: { searchedUtc: '2026-10-05T08:01:00Z', noneFitsUtc: null, pending: false, inPopulation: false },
    ...changes,
  })
}

// ── a series, whose link stays in the app ──

/**
 * The API's sentence for a series item (the Jellyfin adapter's, Vasyl's decision of 2026-10-06):
 * saving a series makes Jellyfin save each of its seasons and episodes again, so a series' link is
 * never written.
 */
export const seriesKeptInAppReason = 'Jellyfin rewrites every season and episode of a series whenever it saves the series, so a series link stays in the app.'

export const penguinsItemId = 'e7c4a91d3b6f4e2a9c8d7b6a5f4e3d21'
export const penguinsLinkId = '11c00000-0000-4000-8000-000000000064'

/**
 * A series named after its folder as Jellyfin names it, with no id at all: three seasons, 146
 * episodes — the fixture's own counts, not those of the live check's series.
 */
export const penguins: LibraryWork = {
  id: '7a1e0000-0000-4000-8000-000000000064',
  kind: 'Series',
  title: 'The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ] by Jatx 11',
  originalTitle: null,
  year: 2008,
  identifiers: [],
  missingIdentifiers: ['Imdb', 'TmdbTv', 'Tvdb'],
  episode: null,
  seasons: [{ number: 1, episodeCount: 49 }, { number: 2, episodeCount: 51 }, { number: 3, episodeCount: 46 }],
  link: null,
  versions: [],
}

/** TMDb show 7869 suggested for the series. */
export const penguinsShow = candidateOf({
  catalogKind: 'Series',
  catalogId: '7869',
  title: 'Пінгвіни з Мадагаскару',
  originalTitle: 'The Penguins of Madagascar',
  year: 2008,
  runtimeMinutes: 23,
  imdbId: null,
  isConfident: false,
  overview: null,
  evidence: evidenceOf({
    title: { similarity: 1, exact: true, matchedKind: 'OriginalTitle', matchedTitle: 'The Penguins of Madagascar' },
    year: { libraryYear: 2008, catalogYear: 2008, difference: 0 },
    runtime: { libraryMinutes: null, catalogMinutes: 23, agrees: null },
  }),
})

/** The series' one library item, which the API says can't be written; `changes` sets the fields a test is about. */
export function penguinsItemOf(changes: Partial<LibraryLinkItem> = {}): LibraryLinkItem {
  return itemOf({
    sourceItemId: penguinsItemId,
    itemTitle: penguins.title,
    itemKind: 'Series',
    canWrite: false,
    cannotWriteReason: seriesKeptInAppReason,
    ...changes,
  })
}

/** The series as the Library page lists it once linked to show 7869: kept in the app, never missing an id. */
export const linkedPenguins: LibraryWork = {
  ...penguins,
  identifiers: [linkedIdentifierOf('TmdbTv', '7869', { setUtc: '2026-10-06T09:00:00Z' })],
  missingIdentifiers: [],
  link: workLinkOf({
    catalogKind: 'Series',
    catalogId: '7869',
    catalogTitle: 'Пінгвіни з Мадагаскару',
    imdbId: null,
    setUtc: '2026-10-06T09:00:00Z',
    writeState: 'KeptInApp',
  }),
}

/** The series' dialog before it is linked: show 7869 suggested, the free search from its cleaned title. */
export function penguinsDialogOf(changes: Partial<LibraryLinkDialog> = {}): LibraryLinkDialog {
  return dialogOf({
    work: penguins,
    items: [penguinsItemOf()],
    suggestions: [penguinsShow],
    searchTitle: 'The Penguins Of Madagascar',
    searchYear: 2008,
    ...changes,
  })
}

/** The series' dialog once linked to show 7869: its item's link kept in the app. */
export function linkedPenguinsDialogOf(changes: Partial<LibraryLinkDialog> = {}): LibraryLinkDialog {
  return penguinsDialogOf({
    work: linkedPenguins,
    items: [penguinsItemOf({
      linkId: penguinsLinkId,
      target: 'Work',
      catalogKind: 'Series',
      catalogId: '7869',
      catalogTitle: 'Пінгвіни з Мадагаскару',
      writeState: 'KeptInApp',
      writeStateUtc: '2026-10-06T09:00:00Z',
      setUtc: '2026-10-06T09:00:00Z',
      setByUserName: 'vasyl',
    })],
    search: { searchedUtc: '2026-10-05T08:01:00Z', noneFitsUtc: null, pending: false, inPopulation: false },
    ...changes,
  })
}

// ── the TMDb page ──

/** The jungles episode's dialog: the show 1044 suggested, its season and episode prefilled. */
export function junglesDialogOf(changes: Partial<LibraryLinkDialog> = {}): LibraryLinkDialog {
  return dialogOf({
    work: planetEarthJungles,
    items: [itemOf({ sourceItemId: planetEarthJungles.versions[0].sourceItemId!, itemTitle: planetEarthJungles.title })],
    suggestions: [planetEarthShow],
    searchTitle: 'Planet Earth',
    searchYear: null,
    ...changes,
  })
}

/** The review queue at `index`, of `totalCount` works, showing `current`. */
export function queueOf(index: number, totalCount: number, current: LibraryLinkDialog | null): LibraryLinkQueue {
  return { totalCount, index, current }
}

/** A preparation of the suggestions that went well; `changes` sets the fields a test is about. */
export function suggestionRunOf(changes: Partial<LibraryLinkSuggestionRun> = {}): LibraryLinkSuggestionRun {
  return {
    id: '5a990000-0000-4000-8000-000000000001',
    status: 'Succeeded',
    trigger: 'LibrarySynced',
    requestedByUserName: null,
    createdUtc: '2026-10-05T08:00:00Z',
    startedUtc: '2026-10-05T08:00:01Z',
    finishedUtc: '2026-10-05T08:01:00Z',
    worksTotal: 240,
    worksDone: 240,
    error: null,
    ...changes,
  }
}

/** «Мультфільми» with its works without a TMDb id, 168 of them with a confident suggestion. */
export const cartoonsCount: LibraryLinkLocationCount = {
  locationId: 'c0000000-0000-4000-8000-00000000000a',
  locationName: 'Мультфільми',
  populationCount: 240,
  confidentCount: 168,
}

/** «Фільми», with a few. */
export const filmsCount: LibraryLinkLocationCount = {
  locationId: 'c0000000-0000-4000-8000-00000000000b',
  locationName: 'Фільми',
  populationCount: 12,
  confidentCount: 3,
}

/** The short and its confident suggestion, film 223021. */
export const confidentStripling: ConfidentSuggestion = { work: landingStripling, candidate: candidateOf() }

/** Another short, «Calypso Cat» (1962), and its own confident suggestion. */
export const confidentCalypso: ConfidentSuggestion = {
  work: { ...landingStripling, id: '7a1e0000-0000-4000-8000-000000000054', title: '121. Calypso Cat' },
  candidate: candidateOf({ catalogId: '223022', title: 'Calypso Cat', originalTitle: null, imdbId: null }),
}

/** One page of confident suggestions answering `query`, with the libraries' counts. */
export function confidentPage(
  query: ConfidentSuggestionsQuery,
  items: ConfidentSuggestion[],
  totalCount = items.length,
  locations: LibraryLinkLocationCount[] = [cartoonsCount, filmsCount],
): ConfidentSuggestionList {
  return { items, totalCount, page: query.page, pageSize: query.pageSize, locations }
}

/** A link as the TMDb page's lists show it: the short's, written; `changes` sets the fields a test is about. */
export function linkRowOf(changes: Partial<LibraryLinkRow> = {}): LibraryLinkRow {
  return {
    linkId,
    workId: landingStripling.id,
    workTitle: landingStripling.title,
    sourceName: 'Home Jellyfin',
    sourceItemId: homeJellyfinItemId,
    itemTitle: '120. Landing Stripling',
    target: 'Work',
    catalogKind: 'Movie',
    catalogId: '223021',
    catalogTitle: 'Посадка Стріплінга',
    seasonNumber: null,
    episodeNumber: null,
    writeState: 'Written',
    writeError: null,
    setUtc: '2026-10-05T18:40:00Z',
    setByUserName: 'vasyl',
    conflictingValue: null,
    ...changes,
  }
}

/** One page of links answering `query`. */
export function linksPage(query: LibraryLinksQuery, items: LibraryLinkRow[], totalCount = items.length): LibraryLinkList {
  return { items, totalCount, page: query.page, pageSize: query.pageSize }
}

/**
 * A work of an "Accept all confident" click, linked, its one link in `writeState`. The link's id is
 * the work's with a 1 in its last four digits ("…0034" → "…1034"), so each work's link has its own.
 */
export function batchItemOf(work: LibraryWork, writeState: LibraryLinkRow['writeState'], changes: Partial<LibraryLinkRow> = {}): LibraryLinkBatchItem {
  return {
    workId: work.id,
    outcome: 'Linked',
    reason: null,
    links: [linkRowOf({ linkId: `${work.id.slice(0, -4)}1${work.id.slice(-3)}`, workId: work.id, workTitle: work.title, writeState, ...changes })],
  }
}

export const batchId = 'ba7c0000-0000-4000-8000-000000000001'

/** An "Accept all confident" click of `items`. */
export function batchOf(items: LibraryLinkBatchItem[]): LibraryLinkBatch {
  return { batchId, items }
}
