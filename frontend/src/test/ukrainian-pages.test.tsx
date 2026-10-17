import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { Mock } from 'vitest'
import App from '@/App'
import appSource from '@/App.tsx?raw'
import { allowedRootsApi } from '@/lib/allowed-roots-api'
import { authApi } from '@/lib/auth-api'
import { copyRemovalsApi } from '@/lib/copy-removals-api'
import { destinationsApi } from '@/lib/destinations-api'
import { downloadsApi } from '@/lib/downloads-api'
import { duplicatesApi } from '@/lib/duplicates-api'
import { healthApi } from '@/lib/health-api'
import { i18n, resources } from '@/lib/i18n'
import { integrationsApi } from '@/lib/integrations-api'
import { libraryApi } from '@/lib/library-api'
import { libraryLinksApi } from '@/lib/library-links-api'
import { profileApi } from '@/lib/profile-api'
import { queryClient } from '@/lib/query-client'
import { recommendationsApi } from '@/lib/recommendations-api'
import { replacementsApi } from '@/lib/replacements-api'
import { searchApi } from '@/lib/search-api'
import { setupApi } from '@/lib/setup-api'
import type {
  CurrentUser,
  CurrentUserProfile,
  DownloadJob,
  HealthReport,
  IntegrationSummary,
  LibraryLinksQuery,
  ReplacementDetails,
  UserSummary,
} from '@/lib/types'
import { usersApi } from '@/lib/users-api'
import { backupToken, expiredToken, laptopToken } from '@/test/access-tokens'
import { allowedRootsOf, cartoonsFolder, collectionsFolder, moviesFolder, quarantineOf, tvShowsFolder } from '@/test/allowed-roots'
import { apiError } from '@/test/api-error'
import { owner, resetAuthStore, sessionFor } from '@/test/auth-session'
import { resetConnectionStore } from '@/test/connection'
import { removalOf, removalsPage } from '@/test/copy-removals'
import { cartoons, destinationsPage, incoming, movies, sampleCategories } from '@/test/destinations'
import { downloadedJob, downloadingJob, fileOf, filesPage, jobsPage } from '@/test/downloads'
import { byteIdenticalGroupOf, duplicatesPage, possibleGroupOf, sameFileGroup, sameWorkGroup } from '@/test/duplicates'
import { adapterTypes, homeJellyfin, localQbittorrent, tmdbCatalog, toloka as tolokaIndexer } from '@/test/integrations'
import { aliens, continental, locations, sourceOf, sourcesOf, tomAndJerryKids, unknownWork, worksPage, yellowstone } from '@/test/library'
import {
  catchUpOf,
  confidentCalypso,
  confidentPage,
  confidentStripling,
  conflictSentence,
  dialogOf,
  linkRowOf,
  linksPage,
  queueOf,
  suggestionRunOf,
} from '@/test/library-links'
import {
  allTabsOn,
  covenant,
  guardians,
  listOf,
  martian,
  runOf as recommendationRunOf,
  yellowstone as yellowstoneRecommendation,
} from '@/test/recommendations'
import { jobCopyOf, newFileOf, replacementDetailsOf, replacementOf, workOf } from '@/test/replacements'
import {
  aliensSearch,
  otherUhd,
  releasesPage,
  searchId,
  tolokaRemux,
  yellowstoneEpisodes,
  yellowstonePack,
  yellowstoneSearch,
} from '@/test/search'
import { collectStrings, englishIn, rawKeysIn, screenTexts, unresolvedIn } from '@/test/ui-text-scan'

vi.mock('@/lib/allowed-roots-api')
vi.mock('@/lib/auth-api')
vi.mock('@/lib/copy-removals-api')
vi.mock('@/lib/destinations-api')
vi.mock('@/lib/downloads-api')
vi.mock('@/lib/duplicates-api')
vi.mock('@/lib/health-api')
vi.mock('@/lib/integrations-api')
vi.mock('@/lib/library-api')
vi.mock('@/lib/library-links-api')
vi.mock('@/lib/profile-api')
vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/replacements-api')
vi.mock('@/lib/search-api')
vi.mock('@/lib/setup-api')
vi.mock('@/lib/users-api')

// MAM-46 — every page of App.tsx's route table, opened at its address in Ukrainian by the installation's
// owner, with the API answering every request a page makes with the fixtures the page's own tests use.
// The session is restored as on F5 (a refresh answering with the owner, whose account says Ukrainian),
// so the app applies the account's language itself. A page passes when Ukrainian is all there is
// (ADR 0001): no key i18next can't find in the Ukrainian resources (English is no fallback here, so an
// untranslated key counts), no key, placeholder or nesting left on screen, no English phrase that came
// from neither the server's data nor a name the Ukrainian resources keep, and <html lang="uk">. Its own
// heading, read from the Ukrainian resources and different from the English one, proves it rendered in
// Ukrainian at all.

/** Every API module, by the name a failure message gives it. */
const apis = {
  allowedRootsApi,
  authApi,
  copyRemovalsApi,
  destinationsApi,
  downloadsApi,
  duplicatesApi,
  healthApi,
  integrationsApi,
  libraryApi,
  libraryLinksApi,
  profileApi,
  recommendationsApi,
  replacementsApi,
  searchApi,
  setupApi,
  usersApi,
}

/** The owner, who chose Ukrainian in their Profile. */
const ukrainianOwner: CurrentUser = { ...owner, language: 'uk' }

const ownerProfile: CurrentUserProfile = {
  ...ukrainianOwner,
  createdUtc: '2026-09-28T09:15:00Z',
  lastSignInUtc: '2026-10-01T18:42:00Z',
  sessionExpiresUtc: '2026-10-31T18:42:00Z',
}

/** Ready but for one check without a sentence of its own, so the page names it in its own words. */
const degradedReport: HealthReport = {
  status: 'Degraded',
  checks: [
    { name: 'database', status: 'Healthy', description: 'The database is reachable and up to date.' },
    { name: 'jellyfin', status: 'Degraded', description: null },
  ],
}

const vasyl: UserSummary = {
  id: owner.id,
  userName: 'vasyl',
  isOwner: true,
  isDisabled: false,
  createdUtc: '2026-09-01T08:00:00Z',
  lastSignInUtc: '2026-09-28T09:30:00Z',
  activeTokenCount: 2,
}
const olena: UserSummary = {
  id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000002',
  userName: 'olena',
  isOwner: false,
  isDisabled: false,
  createdUtc: '2026-09-10T08:00:00Z',
  lastSignInUtc: null,
  activeTokenCount: 1,
}
const taras: UserSummary = {
  id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000003',
  userName: 'taras',
  isOwner: false,
  isDisabled: true,
  createdUtc: '2026-09-12T08:00:00Z',
  lastSignInUtc: '2026-09-20T18:45:00Z',
  activeTokenCount: 0,
}

// The jobs: one downloading, one whose replacement holds the old copy in the quarantine (Restore and
// Delete for good offered), and one whose replacement stopped for the owner at the check, with its
// copies, new files and works listed.

const quarantinedReplacement = replacementDetailsOf()

const noIdReason = 'Jellyfin gave “Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC.mkv” no TMDb or IMDb id within the hour, so no new file is Alien (1979).'

const stoppedReplacement: ReplacementDetails = replacementDetailsOf({
  id: 'e1a50000-0000-4000-8000-000000000002',
  downloadJobId: 'd0b10000-0000-4000-8000-000000000003',
  state: 'NeedsReview',
  reason: noIdReason,
  stop: 'Check',
  canRetry: true,
  canCancel: true,
  canRestore: false,
  canDeleteForGood: false,
  quarantinedBytes: 0,
  freesBytes: 0,
  names: [],
  checkStartedUtc: '2026-10-10T12:00:00Z',
  libraryAskedUtc: '2026-10-10T12:05:00Z',
  copies: [jobCopyOf({ pairedNewFileId: null, problem: noIdReason, status: 'Stopped', canDrop: true })],
  newFiles: [newFileOf({ workId: null, identifiers: [], progress: 'NoId' })],
  works: [workOf()],
})

const quarantinedJob: DownloadJob = {
  ...downloadedJob,
  replacement: replacementOf({ state: 'Quarantined', quarantinedCopyCount: 1, canRestore: true, canDeleteForGood: true }),
}

const stoppedJob: DownloadJob = {
  ...downloadedJob,
  id: stoppedReplacement.downloadJobId,
  releaseTitle: 'Alien.1979.2160p.UHD.BluRay.REMUX.HDR.HEVC',
  replacement: replacementOf({
    id: stoppedReplacement.id,
    state: 'NeedsReview',
    reason: noIdReason,
    stop: 'Check',
    canRetry: true,
    canCancel: true,
  }),
}

const jobs = [downloadingJob, quarantinedJob, stoppedJob]

const replacements = [quarantinedReplacement, stoppedReplacement]

// The TMDb page's lists, as its Not identified, Conflicts and Unattached tabs show them
// (LibraryTmdbPage.lists.test.tsx).
const refusedRow = linkRowOf({ writeState: 'NotWritten', writeError: conflictSentence, identifiedUtc: null })
const conflictRow = linkRowOf({ conflictingValue: '999', writeState: 'Lost' })
const goneRow = linkRowOf({ workId: null, workTitle: null, itemTitle: '120. Landing Stripling (1962) [old cut]', writeState: 'InLibrary' })

/** The panels' own asks (the pending identifications, whether any was refused), apart from the tabs' lists. */
const isPanelAsk = (query: LibraryLinksQuery) => query.filter === 'Pending' || (query.filter === 'NotWritten' && query.pageSize === 1)

function linkRowsFor(query: LibraryLinksQuery) {
  if (isPanelAsk(query)) return []
  if (query.filter === 'Conflict') return [conflictRow]
  if (query.filter === 'NotWritten') return [refusedRow]
  return [goneRow]
}

function integrationsPage(query: { page: number; pageSize: number }, items: IntegrationSummary[]) {
  return { items, totalCount: items.length, page: query.page, pageSize: query.pageSize }
}

/** Every string the API answered with: the server's data, which a page shows as it came. */
const servedText = new Set<string>()

/** `value`, its strings remembered as the server's data. */
function served<T>(value: T): T {
  collectStrings(value, servedText)
  return value
}

/** The API calls a page made that this test serves no answer to: its fixtures are incomplete. */
const unserved = new Set<string>()

/**
 * The API of an installation with something on every page. A call nothing here answers never settles,
 * so a page waits rather than showing an error, and is named in `unserved`.
 */
function serveTheInstallation() {
  for (const [apiName, api] of Object.entries(apis)) {
    for (const [name, fn] of Object.entries(api)) {
      (fn as Mock).mockImplementation(() => {
        unserved.add(`${apiName}.${name}`)
        return new Promise(() => {})
      })
    }
  }

  vi.mocked(setupApi.status).mockImplementation(async () => served({ isRequired: false }))
  vi.mocked(authApi.refresh).mockImplementation(async () => served(sessionFor(ukrainianOwner)))
  vi.mocked(authApi.me).mockImplementation(async () => served(ownerProfile))
  vi.mocked(healthApi.ready).mockImplementation(async () => served(degradedReport))

  vi.mocked(profileApi.recommendationSettings).mockImplementation(async () => served(allTabsOn))
  vi.mocked(profileApi.accessTokens).mockImplementation(async () => served({ items: [laptopToken, backupToken, expiredToken] }))

  vi.mocked(libraryApi.works).mockImplementation(async (query) =>
    served(worksPage(query, [aliens, continental, unknownWork, yellowstone, tomAndJerryKids])))
  vi.mocked(libraryApi.locations).mockImplementation(async () => served(locations))
  vi.mocked(libraryApi.sources).mockImplementation(async () => served(sourcesOf(sourceOf({}))))
  vi.mocked(libraryApi.poster).mockRejectedValue(apiError(404))

  vi.mocked(libraryLinksApi.latestRun).mockImplementation(async () => served(suggestionRunOf()))
  vi.mocked(libraryLinksApi.queue).mockImplementation(async (index) => served(queueOf(index, 2, dialogOf())))
  vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) =>
    served(confidentPage(query, [confidentStripling, confidentCalypso])))
  vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => served(linksPage(query, linkRowsFor(query))))
  vi.mocked(libraryLinksApi.catchUp).mockImplementation(async () => served(catchUpOf()))

  vi.mocked(duplicatesApi.list).mockImplementation(async (query) =>
    served(duplicatesPage(query, [sameFileGroup, byteIdenticalGroupOf(), sameWorkGroup, possibleGroupOf()])))
  vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => served(removalsPage(query, [removalOf()])))

  vi.mocked(recommendationsApi.list).mockImplementation(async (query) =>
    served(listOf(query, query.tab === 'Series' ? [yellowstoneRecommendation] : [guardians, martian, covenant])))
  vi.mocked(recommendationsApi.latestRun).mockImplementation(async () => served(recommendationRunOf()))
  vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))

  vi.mocked(downloadsApi.list).mockImplementation(async (query) =>
    served(jobsPage(query, jobs, jobs.length, { needsYouCount: 1, inProgressCount: 1 })))
  vi.mocked(downloadsApi.get).mockImplementation(async (id) => served(jobs.find((job) => job.id === id) ?? downloadingJob))
  vi.mocked(downloadsApi.files).mockImplementation(async (_, query) =>
    served(filesPage(query, [fileOf({}), fileOf({ position: 1, path: 'Sample/sample.mkv', sizeBytes: 52_428_800, progress: 0, isWanted: false })])))
  vi.mocked(replacementsApi.get).mockImplementation(async (id) =>
    served(replacements.find((replacement) => replacement.id === id) ?? quarantinedReplacement))

  vi.mocked(searchApi.get).mockImplementation(async () => served(aliensSearch))
  vi.mocked(searchApi.releases).mockImplementation(async (_, query) => served(releasesPage(query, [tolokaRemux, otherUhd])))

  vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
    served(integrationsPage(query, [homeJellyfin, localQbittorrent, tolokaIndexer, tmdbCatalog])))
  vi.mocked(integrationsApi.adapterTypes).mockImplementation(async () => served(adapterTypes))
  vi.mocked(destinationsApi.list).mockImplementation(async (query) => served(destinationsPage(query, [cartoons, incoming, movies])))
  vi.mocked(destinationsApi.clientDestinations).mockImplementation(async () => served(sampleCategories))
  vi.mocked(allowedRootsApi.get).mockImplementation(async () => served(allowedRootsOf(['/media', '/data/films'], {
    replacementEnabled: true,
    libraryFolders: [{ ...moviesFolder, ticked: true }, tvShowsFolder, cartoonsFolder, collectionsFolder],
    quarantines: [quarantineOf()],
  })))
  vi.mocked(usersApi.list).mockImplementation(async (query) =>
    served({ items: [vasyl, olena, taras], totalCount: 3, page: query.page, pageSize: query.pageSize }))
}

/** A key of the resources, `[namespace, dotted.key]`. */
type ResourceKey = readonly [namespace: string, key: string]

interface PageCase {
  /** What the case is, as the test's name says it. */
  page: string
  path: string
  /** Whether the owner is signed in; a page before sign-in has no session to restore. */
  signedIn: boolean
  /** A heading the page shows, by its resource key, and its level. */
  heading: readonly [level: number, key: ResourceKey]
  /** Waits for the page's main content: the fixtures' rows, cards or fields, not only its frame. */
  content?: () => Promise<unknown>
  /** Changes what the API answers for this page. */
  serve?: () => void
  /** Opens what only a click shows, so it is read in Ukrainian too. */
  then?: (user: ReturnType<typeof userEvent.setup>) => Promise<void>
}

const findText = (text: string | RegExp) => () => screen.findAllByText(text)

const findRowTitle = () =>
  waitFor(() => expect(document.querySelector('.tmdb-link-row-title')).not.toBeNull())

const pages: PageCase[] = [
  {
    page: 'Home, with the user menu open',
    path: '/',
    signedIn: true,
    heading: [2, ['home', 'status.title']],
    content: findText(/jellyfin/),
    then: async (user) => {
      const menuButton = screen.getByRole('button', { name: new RegExp(ukrainianOwner.userName) })
      await user.click(menuButton)
      expect(menuButton).toHaveAttribute('aria-expanded', 'true')
    },
  },
  { page: 'Sign in', path: '/login', signedIn: false, heading: [1, ['auth', 'login.title']], content: () => screen.findAllByRole('textbox') },
  {
    page: 'Setup',
    path: '/setup',
    signedIn: false,
    heading: [1, ['auth', 'setup.title']],
    content: () => screen.findAllByRole('textbox'),
    serve: () => vi.mocked(setupApi.status).mockImplementation(async () => served({ isRequired: true })),
  },
  { page: 'Not found', path: '/no-such-page', signedIn: true, heading: [1, ['notFound', 'title']] },
  { page: 'Search, before a search', path: '/search', signedIn: true, heading: [1, ['search', 'title']] },
  {
    page: 'Search, with a film\'s results',
    path: `/search?id=${searchId}`,
    signedIn: true,
    heading: [1, ['search', 'title']],
    content: () => screen.findByRole('heading', { level: 3, name: tolokaRemux.title }),
  },
  {
    page: 'Search, with a series\' results',
    path: `/search?id=${yellowstoneSearch.id}`,
    signedIn: true,
    heading: [1, ['search', 'title']],
    content: () => screen.findByRole('heading', { level: 3, name: yellowstonePack.title }),
    serve: () => {
      vi.mocked(searchApi.get).mockImplementation(async () => served(yellowstoneSearch))
      vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
        served(releasesPage(query, [yellowstonePack, yellowstoneEpisodes])))
    },
  },
  {
    page: 'Library',
    path: '/library',
    signedIn: true,
    heading: [1, ['library', 'title']],
    content: () => screen.findByRole('heading', { level: 2, name: aliens.title! }),
  },
  {
    page: 'TMDb links, Review',
    path: '/library/tmdb',
    signedIn: true,
    heading: [1, ['libraryLinks', 'page.title']],
    content: findText(new RegExp(dialogOf().work.title!)),
  },
  {
    page: 'TMDb links, Confident',
    path: '/library/tmdb?tab=Confident',
    signedIn: true,
    heading: [1, ['libraryLinks', 'page.title']],
    content: findText(new RegExp(confidentStripling.work.title!)),
  },
  {
    page: 'TMDb links, Not identified',
    path: '/library/tmdb?tab=NotIdentified',
    signedIn: true,
    heading: [1, ['libraryLinks', 'page.title']],
    content: findRowTitle,
  },
  {
    page: 'TMDb links, Conflicts',
    path: '/library/tmdb?tab=Conflicts',
    signedIn: true,
    heading: [1, ['libraryLinks', 'page.title']],
    content: findRowTitle,
  },
  {
    page: 'TMDb links, Unattached',
    path: '/library/tmdb?tab=Unattached',
    signedIn: true,
    heading: [1, ['libraryLinks', 'page.title']],
    content: findRowTitle,
  },
  {
    page: 'Duplicates, All',
    path: '/library/duplicates',
    signedIn: true,
    heading: [1, ['duplicates', 'title']],
    content: () => screen.findAllByRole('article'),
  },
  {
    page: 'Duplicates, Removed copies',
    path: '/library/duplicates?tab=Removed',
    signedIn: true,
    heading: [1, ['duplicates', 'title']],
    content: () => screen.findByRole('article', { name: removalOf().title }),
  },
  {
    page: 'Recommendations, Movies',
    path: '/recommendations',
    signedIn: true,
    heading: [1, ['recommendations', 'title']],
    content: () => screen.findByRole('heading', { level: 2, name: guardians.title }),
  },
  {
    page: 'Recommendations, Series',
    path: '/recommendations?tab=Series',
    signedIn: true,
    heading: [1, ['recommendations', 'title']],
    content: () => screen.findByRole('heading', { level: 2, name: yellowstoneRecommendation.title }),
  },
  {
    page: 'Jobs',
    path: '/jobs',
    signedIn: true,
    heading: [1, ['jobs', 'title']],
    content: findText(stoppedJob.releaseTitle),
  },
  {
    page: 'A job\'s details, downloading',
    path: `/jobs/${downloadingJob.id}`,
    signedIn: true,
    heading: [2, ['jobs', 'details.factsTitle']],
    content: findText('Sample/sample.mkv'),
  },
  {
    page: 'A job\'s details, its old copy in the quarantine',
    path: `/jobs/${quarantinedJob.id}`,
    signedIn: true,
    heading: [2, ['jobs', 'oldCopies.title']],
    content: findText(quarantinedReplacement.names[0].originalPath),
  },
  {
    page: 'A job\'s details, its replacement stopped for the owner',
    path: `/jobs/${stoppedJob.id}`,
    signedIn: true,
    heading: [2, ['jobs', 'oldCopies.title']],
    content: () => screen.findByRole('heading', { level: 3, name: resourceValue('uk', ['jobs', 'oldCopies.newFiles.title']) as string }),
  },
  {
    page: 'Profile',
    path: '/profile',
    signedIn: true,
    heading: [2, ['profile', 'language.title']],
    content: findText(laptopToken.name),
  },
  {
    page: 'Integrations',
    path: '/settings/integrations',
    signedIn: true,
    heading: [1, ['integrations', 'title']],
    content: findText(tmdbCatalog.name),
  },
  {
    // MAM-46 — the dialog's Catalog language select, its names in the UI's language (Intl.DisplayNames).
    page: 'Integrations, with TMDb open for editing',
    path: '/settings/integrations',
    signedIn: true,
    heading: [1, ['integrations', 'title']],
    content: findText(tmdbCatalog.baseUrl),
    then: async (user) => {
      const row = screen.getByText(tmdbCatalog.baseUrl).closest('tr')!
      await user.click(within(row).getByRole('button', { name: resourceValue('uk', ['integrations', 'actions.edit']) as string }))
      await screen.findByRole('dialog')
    },
  },
  {
    page: 'Destinations',
    path: '/settings/destinations',
    signedIn: true,
    heading: [1, ['destinations', 'title']],
    content: findText(incoming.name),
  },
  {
    page: 'Media folders',
    path: '/settings/media-folders',
    signedIn: true,
    heading: [1, ['mediaFolders', 'title']],
    content: () => screen.findByDisplayValue('/data/films'),
  },
  {
    page: 'Users',
    path: '/settings/users',
    signedIn: true,
    heading: [1, ['users', 'title']],
    content: findText(taras.userName),
  },
]

/** The value of `[namespace, key]` in `language`, as the resources hold it. */
function resourceValue(language: 'en' | 'uk', [namespace, key]: ResourceKey): unknown {
  return i18n.getResource(language, namespace, key)
}

/** Every value of the Ukrainian resources: the product names among them stay in Latin letters. */
const ukrainianValues: string[] = (() => {
  const values = new Set<string>()
  collectStrings(resources.uk, values)
  return [...values]
})()

/** The paths App.tsx routes, as its <Route> elements spell them: `''` for the index route, `*` for the rest. */
function routedPaths(): string[] {
  return [...appSource.matchAll(/<Route\s+(?:(index)\s|path="([^"]*)")/g)].map(([, index, path]) => (index ? '' : path))
}

/** Whether `pathname` is an address of `route` (`jobs/:id` takes any one segment for `:id`). */
function isAddressOf(route: string, pathname: string): boolean {
  return new RegExp(`^/${route.replace(/:[^/]+/g, '[^/]+')}$`).test(pathname)
}

/** The locale tests run in (jsdom's navigator says en-US), which every test file expects to find. */
const testLanguage = i18n.language

/** The keys i18next could not find in the Ukrainian resources, as `namespace:key`. */
const missingKeys = new Set<string>()

const { saveMissing, missingKeyHandler, fallbackLng } = i18n.options

describe('every page in Ukrainian', () => {
  beforeAll(async () => {
    // Each page's code loads on its first visit; loaded here, no case waits on a cold transform.
    await Promise.all([
      import('@/pages/DuplicatesPage'),
      import('@/pages/HomePage'),
      import('@/pages/JobDetailsPage'),
      import('@/pages/JobsPage'),
      import('@/pages/LibraryPage'),
      import('@/pages/LibraryTmdbPage'),
      import('@/pages/LoginPage'),
      import('@/pages/NotFoundPage'),
      import('@/pages/ProfilePage'),
      import('@/pages/RecommendationsPage'),
      import('@/pages/SearchPage'),
      import('@/pages/SetupPage'),
      import('@/pages/settings/DestinationsPage'),
      import('@/pages/settings/IntegrationsPage'),
      import('@/pages/settings/MediaFoldersPage'),
      import('@/pages/settings/UsersPage'),
    ])
  }, 60_000)

  beforeEach(async () => {
    vi.resetAllMocks()
    localStorage.clear()
    resetAuthStore()
    resetConnectionStore()
    servedText.clear()
    unserved.clear()
    missingKeys.clear()
    serveTheInstallation()
    // No English fallback: a key the Ukrainian resources lack is reported, not shown in English.
    i18n.options.fallbackLng = false
    i18n.options.saveMissing = true
    i18n.options.missingKeyHandler = (_languages, namespace, key) => {
      missingKeys.add(`${namespace}:${key}`)
    }
    await i18n.changeLanguage('uk')
  })

  afterEach(async () => {
    // Unmounted before the cache is emptied and the language changes back: a page still on screen
    // would mount again in English and ask again, and its late answer would land in the next case.
    cleanup()
    queryClient.clear()
    window.history.pushState({}, '', '/')
    i18n.options.fallbackLng = fallbackLng
    i18n.options.saveMissing = saveMissing
    i18n.options.missingKeyHandler = missingKeyHandler
    await i18n.changeLanguage(testLanguage)
  })

  it('opens every route of App.tsx', () => {
    // Arrange
    const routes = routedPaths()
    const opened = pages.map(({ path }) => new URL(path, window.location.origin).pathname)
    const named = routes.filter((route) => route !== '*')

    // Act
    const unopened = routes.filter((route) => route === '*'
      ? !opened.some((pathname) => !named.some((other) => isAddressOf(other, pathname)))
      : !opened.some((pathname) => isAddressOf(route, pathname)))

    // Assert
    expect(routes).toEqual(expect.arrayContaining(['', 'login', 'setup', 'jobs/:id', 'settings/users', '*']))
    expect(unopened).toEqual([])
  })

  it('reports a key the Ukrainian resources lack instead of showing its English text', () => {
    // Arrange
    i18n.addResourceBundle('en', 'probe', { onlyInEnglish: 'Only in English' })
    const translate = i18n.t as (key: string, options: { ns: string }) => string

    // Act
    const shown = translate('onlyInEnglish', { ns: 'probe' })
    i18n.removeResourceBundle('en', 'probe')

    // Assert
    expect(shown).toBe('onlyInEnglish')
    expect([...missingKeys]).toEqual(['probe:onlyInEnglish'])
  })

  it.each(pages)('shows $page ($path) in Ukrainian only', async ({ path, signedIn, heading: [level, key], content, serve, then }) => {
    // Arrange
    const ukrainianHeading = resourceValue('uk', key)
    expect(ukrainianHeading, `uk ${key.join(':')}`).toEqual(expect.any(String))
    expect(ukrainianHeading, `uk ${key.join(':')} is the English text`).not.toBe(resourceValue('en', key))
    if (!signedIn) vi.mocked(authApi.refresh).mockRejectedValue(apiError(401, 'Your session has ended. Sign in again.'))
    serve?.()
    window.history.pushState({}, '', path)

    // Act
    render(<App />)
    const user = userEvent.setup()
    expect(await screen.findByRole('heading', { level, name: ukrainianHeading as string }, { timeout: 10_000 })).toBeInTheDocument()
    await content?.()
    await then?.(user)
    expect([...unserved], 'API calls this test serves no answer to').toEqual([])
    await waitFor(() => expect(queryClient.isFetching()).toBe(0))

    // Assert
    const texts = screenTexts(document.body)
    const allowed = [...servedText, ...ukrainianValues]
    expect({
      missingKeys: [...missingKeys].sort(),
      rawKeys: rawKeysIn(texts),
      unresolved: unresolvedIn(texts),
      english: englishIn(texts, allowed),
    }).toEqual({ missingKeys: [], rawKeys: [], unresolved: [], english: [] })
    expect(document.documentElement.lang).toBe('uk')
  }, 30_000)
})
