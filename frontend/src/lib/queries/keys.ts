import type {
  DestinationsQuery,
  DownloadJobFilesQuery,
  DownloadJobsQuery,
  IntegrationsQuery,
  LibraryWorksQuery,
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
  },
  setup: {
    status: ['setup', 'status'] as const,
  },
  library: {
    /** The works, the library filter and the sources: what a change to an integration drops. */
    all: ['library'] as const,
    /** Every page and filter of the works: what a finished sync invalidates. */
    worksAll: ['library', 'works'] as const,
    /** The whole query object is part of the key, so each page and filter is cached apart. */
    works: (query: LibraryWorksQuery) => ['library', 'works', query] as const,
    locations: ['library', 'locations'] as const,
    sources: ['library', 'sources'] as const,
  },
  downloads: {
    /** MAM-13 — the download clients and destinations one release can be sent to. */
    options: (releaseId: string) => ['downloads', 'options', releaseId] as const,
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
    },
  },
}
