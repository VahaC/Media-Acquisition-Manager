import type {
  ConfidentSuggestionsQuery,
  DestinationsQuery,
  DownloadJobFilesQuery,
  DownloadJobsQuery,
  IntegrationsQuery,
  LibraryDuplicatesQuery,
  LibraryEpisodesQuery,
  LibraryLinkLookupQuery,
  LibraryLinkSearchQuery,
  LibraryLinksQuery,
  LibraryWorksQuery,
  RecommendationsQuery,
  SearchReleasesQuery,
  UsersQuery,
} from '../types'

/**
 * Every query key in the app. Hierarchical arrays that mirror the URL, so a prefix invalidates a
 * whole family. Components never write a literal key.
 */
export const qk = {
  health: {
    ready: ['health', 'ready'] as const,
    /** MAM-16 — the offline strip's probe. */
    live: ['health', 'live'] as const,
  },
  setup: {
    status: ['setup', 'status'] as const,
  },
  auth: {
    /** MAM-16 — the signed-in user's profile and this session's end, for the Profile page. */
    me: ['auth', 'me'] as const,
  },
  profile: {
    /** MAM-33 — the user's recommendation tabs and daily search limit: the header, the page and the Profile card. */
    recommendations: ['profile', 'recommendations'] as const,
  },
  recommendations: {
    /** MAM-33 — every page and filter of every tab: what a finished computation invalidates. */
    lists: ['recommendations', 'list'] as const,
    /** The whole query object is part of the key, so each tab, page and filter is cached apart. */
    list: (query: RecommendationsQuery) => ['recommendations', 'list', query] as const,
    /** The latest computation, polled while one is under way. */
    latestRun: ['recommendations', 'runs', 'latest'] as const,
    /** One poster as a `data:` URL, by the URL the list gave; a poster file never changes. */
    poster: (url: string) => ['recommendations', 'posters', url] as const,
  },
  library: {
    /** The works, the library filter and the sources: what a change to an integration drops. */
    all: ['library'] as const,
    /** Every page and filter of the works: what a finished sync invalidates. */
    worksAll: ['library', 'works'] as const,
    /** The whole query object is part of the key, so each page and filter is cached apart. */
    works: (query: LibraryWorksQuery) => ['library', 'works', query] as const,
    /**
     * MAM-32 — one page of one season of a series, under `works` so what refreshes the works (a
     * finished sync, an inspection's facts) refreshes the open seasons too.
     */
    episodes: (seriesId: string, query: LibraryEpisodesQuery) => ['library', 'works', seriesId, 'episodes', query] as const,
    locations: ['library', 'locations'] as const,
    sources: ['library', 'sources'] as const,
    /**
     * MAM-35 — one work's library poster as a `data:` URL, by the URL the works gave. Beside `works`
     * rather than under it, as the links are: a finished sync, an inspection under way, a link change,
     * the works' poll while a link is under way and Refresh all invalidate the works, which would ask
     * for every poster on screen again. The URL names one version of the image (its tag), so nothing
     * needs to invalidate it.
     */
    poster: (url: string) => ['library', 'posters', url] as const,
    /**
     * MAM-21 — every page of every tab of the Duplicates page: what a decision invalidates, as a dismissed
     * group leaves every tab but Dismissed and a confirmed one changes its card on each. Under `library`, so a
     * change to an integration drops them with the works; beside `works` rather than under it, as an inspection
     * under way invalidates the works every 2 s, and every answer here is worked out from the whole library.
     */
    duplicatesAll: ['library', 'duplicates'] as const,
    /** MAM-21 — one page of one tab; the whole query object is part of the key, so each is cached apart. */
    duplicates: (query: LibraryDuplicatesQuery) => ['library', 'duplicates', query] as const,
    /**
     * MAM-34 — the TMDb links: under `library`, so a change to an integration drops them with the works
     * (removing an integration removes its links), but beside `works` rather than under it — an
     * inspection under way refreshes the works every 2 s, which would ask for an open dialog's
     * suggestions as often.
     */
    links: {
      /** Every dialog, queue, list and search: what a link change, a finished sync and a finished inspection invalidate. */
      all: ['library', 'links'] as const,
      /** One work's dialog; its searches and lookups sit under it. */
      work: (workId: string) => ['library', 'links', 'works', workId] as const,
      /** A free TMDb search from one work's dialog; the whole query is part of the key. */
      search: (workId: string, query: LibraryLinkSearchQuery) => ['library', 'links', 'works', workId, 'search', query] as const,
      /** A pasted id looked up from one work's dialog. */
      lookup: (workId: string, query: LibraryLinkLookupQuery) => ['library', 'links', 'works', workId, 'lookup', query] as const,
      /** The review queue at one index. */
      queue: (index: number) => ['library', 'links', 'queue', index] as const,
      /** Every page of the confident suggestions: what "Accept all confident" leaves alone while its rows show. */
      confidentAll: ['library', 'links', 'confident'] as const,
      /** One page of the confident suggestions. */
      confident: (query: ConfidentSuggestionsQuery) => ['library', 'links', 'confident', query] as const,
      /** One "Accept all confident" click, polled while its identifications are under way. */
      batch: (batchId: string) => ['library', 'links', 'batches', batchId] as const,
      /** One page of the links of a filter. */
      list: (query: LibraryLinksQuery) => ['library', 'links', 'list', query] as const,
      /** The latest preparation of the suggestions. */
      latestRun: ['library', 'links', 'runs', 'latest'] as const,
      /** What "Update Jellyfin" would still identify, and the batch under way. */
      catchUp: ['library', 'links', 'catch-up'] as const,
    },
  },
  downloads: {
    /** MAM-13 — the download clients and destinations one release can be sent to. */
    options: (releaseId: string) => ['downloads', 'options', releaseId] as const,
    /** MAM-38 — whether a destination's folder is checked for files a download would write over. */
    overwriteCheck: (destinationId: string) => ['downloads', 'overwrite-check', destinationId] as const,
    /**
     * MAM-14 — the caller's own jobs, under `jobs` rather than beside `options`: a job's id would
     * otherwise sit where `options` does, and invalidating jobs would reach the options too.
     */
    jobs: {
      /** Every list, job and job's files: what a cancel invalidates, whatever it answered. */
      all: ['downloads', 'jobs'] as const,
      /** Every page and filter of the list: what a send invalidates. */
      lists: ['downloads', 'jobs', 'list'] as const,
      /** The whole query object is part of the key, so each page and filter is cached apart. */
      list: (query: DownloadJobsQuery) => ['downloads', 'jobs', 'list', query] as const,
      /** One job; its files sit under it. */
      detail: (id: string) => ['downloads', 'jobs', id] as const,
      /** Every page of one job's files: what a move of the job invalidates. */
      allFiles: (id: string) => ['downloads', 'jobs', id, 'files'] as const,
      /** One page of one job's files. */
      files: (id: string, query: DownloadJobFilesQuery) => ['downloads', 'jobs', id, 'files', query] as const,
    },
  },
  replacements: {
    /**
     * MAM-24 — every answer about what a release may replace: what saving the Media folders (the
     * switch and the ticked folders decide every reason) invalidates.
     */
    all: ['replacements'] as const,
    /**
     * What one release may replace through one destination; with the held marks (`holdings`, every
     * download client's torrents read) or without, each cached apart.
     */
    options: (releaseId: string, destinationId: string, holdings: boolean) =>
      ['replacements', 'options', releaseId, destinationId, holdings] as const,
  },
  search: {
    /** MAM-12 — every stored search and its releases: the prefix of the keys below. */
    all: ['searches'] as const,
    /** One stored search: its query, library matches and sources. */
    detail: (id: string) => ['searches', id] as const,
    /** The whole query object is part of the key, so each page, filter and sort is cached apart. */
    releases: (id: string, query: SearchReleasesQuery) => ['searches', id, 'releases', query] as const,
  },
  settings: {
    /** MAM-9 — the media folders: what saving them invalidates, with the library sources. */
    allowedRoots: ['settings', 'allowed-roots'] as const,
    users: {
      /** Every page of every search: what a change to any user invalidates. */
      all: ['settings', 'users'] as const,
      /** The whole query object is part of the key, so each page and search is cached apart. */
      list: (query: UsersQuery) => ['settings', 'users', 'list', query] as const,
    },
    integrations: {
      /**
       * Every page and filter: what a change to any integration invalidates. `lists`, not the
       * `['settings', 'integrations']` family, which would refetch the adapter types too.
       */
      lists: ['settings', 'integrations', 'list'] as const,
      /** The whole query object is part of the key, so each page and filter is cached apart. */
      list: (query: IntegrationsQuery) => ['settings', 'integrations', 'list', query] as const,
      /** Fixed by the build; never invalidated by a change. */
      adapterTypes: ['settings', 'integrations', 'adapter-types'] as const,
    },
    destinations: {
      /**
       * MAM-11 — the lists and the clients' categories: what a change to an integration invalidates,
       * as a client's name, switch, credentials and path mappings are part of every check.
       */
      all: ['settings', 'destinations'] as const,
      /** Every page and search: what a change to any destination invalidates. */
      lists: ['settings', 'destinations', 'list'] as const,
      /** The whole query object is part of the key, so each page and search is cached apart. */
      list: (query: DestinationsQuery) => ['settings', 'destinations', 'list', query] as const,
      /** The categories one download client offers, asked live; a destination change leaves them be. */
      clientDestinations: (downloadClientId: string) =>
        ['settings', 'destinations', 'client-destinations', downloadClientId] as const,
      /**
       * MAM-32 — the release sources' sections and the destinations they are mapped to: what a change
       * to a destination invalidates, as its save moves sections and its removal frees them.
       */
      sections: ['settings', 'destinations', 'sections'] as const,
    },
  },
}
