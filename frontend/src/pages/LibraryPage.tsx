import { useState } from 'react'
import { Link2, ListChecks } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { FilterBar } from '@/components/shared/FilterBar'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { SortSelect } from '@/components/shared/SortSelect'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useLibraryLocations, useLibraryWorks } from '@/lib/queries'
import type { LibraryIdentifierFilter, LibraryKindFilter, LibraryLocation, LibrarySort, LibraryWork } from '@/lib/types'
import { useDebounced } from '@/lib/use-debounced'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, toolbar and empty states of this page
import '@/styles/library.css' // .library-works — the column of work cards

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:library:pageSize'
const SEARCH_DEBOUNCE_MS = 300

/** The orders the server sorts by, in the order the select offers them; Title is the default. */
const SORTS: readonly LibrarySort[] = ['Title', 'TitleDescending', 'Newest', 'Oldest', 'Largest', 'Added']

/**
 * MAM-8 — the Library: every movie the media libraries reported at their last sync, searched,
 * filtered (by Jellyfin library, by missing identifiers) and paged by the server, each with its
 * versions and files. The sources panel above says how the syncs went and starts one (the owner).
 *
 * MAM-32 — and every series, one card among the movies in title order, its seasons opening to their
 * episodes; an episode of no series is a card of its own. A kind filter shows the movies or the
 * series alone, and the search and filters reach into a series: its season counts, and the episodes
 * a season lists, are the ones they keep.
 *
 * Every signed-in user reads it. A finished sync refreshes the list by itself (useLibrarySources).
 *
 * The order stands beside the filters, at the right: by title either way, by year, by the room the files
 * take or by when a work was added. It is no filter and not part of the row they wrap in, so it never
 * comes between the filters and their Clear filters. A new order starts again at page 1, and the order is
 * the page's alone: the episodes a season lists keep their season and number order.
 *
 * MAM-34 — each card has a "TMDb…" action that opens the work's TMDb dialog, where the owner links it
 * to its film or show by hand. A member sees it disabled with the reason, as every owner-only action
 * here; the dialog's routes are the owner's. Nothing is asked for it until the dialog opens.
 *
 * MAM-34 — and with "Missing an id" on, "Review works without a TMDb id" opens the TMDb page (the
 * owner's; a member sees it disabled with the reason). The filters live in the page, not the address,
 * so before it opens the address takes the ones the page reads back — the search and `?identifiers=` —
 * and Back from the TMDb page finds the list as it was.
 */
export function LibraryPage() {
  const { t } = useTranslation(['library', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  // The parent mounts the dialog only while it is open, so every opening starts fresh.
  const [linking, setLinking] = useState<LibraryWork | null>(null)
  const locations = useLibraryLocations()
  const [searchParams, setSearchParams] = useSearchParams()
  // MAM-12 — the Search page links here with its title in `?search=` when more movies hold its
  // words than it lists, so the page opens already searched for it. Read once, as the box's first
  // value: what the person types after that is theirs.
  const [search, setSearch] = useState(() => searchParams.get('search') ?? '')
  const [locationId, setLocationId] = useState('')
  // MAM-34 — read once like `?search=`: the TMDb page's way back to this list (and Back) carries it.
  const [identifiers, setIdentifiers] = useState<LibraryIdentifierFilter | ''>(
    () => (searchParams.get('identifiers') === 'Missing' ? 'Missing' : ''),
  )
  const [kind, setKind] = useState<LibraryKindFilter | ''>('')
  const [sort, setSort] = useState<LibrarySort>('Title')
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  // The page remembers the filters it belongs to, so different filters start again at page 1
  // without an effect — the idiom of IntegrationsPage.
  const [paging, setPaging] = useState({ search: '', locationId: '', identifiers: '', kind: '', sort: 'Title', page: 1 })

  // Typing waits for a pause; emptying the box shows every work at once.
  const debouncedSearch = useDebounced(search.trim(), SEARCH_DEBOUNCE_MS)
  const appliedSearch = search.trim() === '' ? '' : debouncedSearch
  // MAM-8 — a sync can take away the library the filter names (emptied or removed in Jellyfin, or
  // its integration removed), and the refreshed list no longer offers it. The select would then
  // read All libraries while every request kept the old id and found nothing, so the filter lets go
  // of it during render, the same way the paging below starts again.
  const locationGone = locationId !== '' && locations.data !== undefined
    && !locations.data.items.some((location) => location.id === locationId)
  if (locationGone) setLocationId('')
  const appliedLocationId = locationGone ? '' : locationId
  const onFilters = paging.search === appliedSearch
    && paging.locationId === appliedLocationId
    && paging.identifiers === identifiers
    && paging.kind === kind
    && paging.sort === sort
  if (!onFilters) setPaging({ search: appliedSearch, locationId: appliedLocationId, identifiers, kind, sort, page: 1 })
  const page = onFilters ? paging.page : 1
  const filtered = appliedSearch !== '' || appliedLocationId !== '' || identifiers !== '' || kind !== ''
  // MAM-32 — what a series card's seasons were counted by, so an opened season lists those episodes.
  // The kind filter isn't one of them: it picks works, never episodes.
  const episodeFilters = {
    search: appliedSearch || undefined,
    locationId: appliedLocationId || undefined,
    identifiers: identifiers || undefined,
  }
  const works = useLibraryWorks({ ...episodeFilters, kind: kind || undefined, sort: sort === 'Title' ? undefined : sort, page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = works
  const load = useLoadRetry(works)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A sync can leave fewer works than the page on screen needs: the refetched page is empty, so
  // move to the last one there is. Only on this page's own answer, not the one kept on screen while
  // it loads.
  if (onFilters && data && !isPlaceholderData && page > pages) {
    setPaging({ search: appliedSearch, locationId: appliedLocationId, identifiers, kind, sort, page: pages })
  }

  const goToPage = (next: number) =>
    setPaging({ search: appliedSearch, locationId: appliedLocationId, identifiers, kind, sort, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  // MAM-34 — the address takes the filters the page reads back before the TMDb page opens on top of
  // it (replacing this entry, so Back lands on it), as the page keeps them in its state alone.
  const keepFiltersForBack = () => {
    const next = new URLSearchParams()
    if (search.trim() !== '') next.set('search', search.trim())
    next.set('identifiers', 'Missing')
    setSearchParams(next, { replace: true })
  }

  const clearFilters = () => {
    setSearch('')
    setLocationId('')
    setIdentifiers('')
    setKind('')
  }

  /**
   * A library's name in the filter, with its integration's name only when another library has the
   * same name — two Jellyfin servers can both have a "Фільми".
   */
  const locationLabel = (location: LibraryLocation, all: LibraryLocation[]) => {
    const shared = all.some((other) => other.id !== location.id && other.name === location.name)
    return shared && location.sourceName
      ? t('locationFilter.option', { name: location.name, source: location.sourceName })
      : location.name
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      <LibrarySyncPanel />

      <FilterBar
        sort={(
          <SortSelect
            label={t('sort.label')}
            value={sort}
            options={SORTS.map((option) => ({ value: option, label: t(`sort.${option}`) }))}
            onChange={setSort}
          />
        )}
      >
        <Input
          type="search"
          className="settings-search"
          value={search}
          placeholder={t('search.placeholder')}
          aria-label={t('search.label')}
          onChange={(event) => setSearch(event.target.value)}
        />
        <select
          className="ui-input settings-filter"
          value={kind}
          aria-label={t('kindFilter.label')}
          onChange={(event) => setKind(event.target.value as LibraryKindFilter | '')}
        >
          <option value="">{t('kindFilter.all')}</option>
          <option value="Movie">{t('kindFilter.Movie')}</option>
          <option value="Series">{t('kindFilter.Series')}</option>
        </select>
        <select
          className="ui-input settings-filter"
          value={appliedLocationId}
          aria-label={t('locationFilter.label')}
          onChange={(event) => setLocationId(event.target.value)}
        >
          <option value="">{t('locationFilter.all')}</option>
          {locations.data?.items.map((location, _, all) => (
            <option key={location.id} value={location.id}>{locationLabel(location, all)}</option>
          ))}
        </select>
        <select
          className="ui-input settings-filter"
          value={identifiers}
          aria-label={t('identifierFilter.label')}
          onChange={(event) => setIdentifiers(event.target.value as LibraryIdentifierFilter | '')}
        >
          <option value="">{t('identifierFilter.all')}</option>
          <option value="Missing">{t('identifierFilter.missing')}</option>
        </select>
        {identifiers === 'Missing' && <ReviewEntry isOwner={isOwner} onOpen={keepFiltersForBack} />}
      </FilterBar>

      {/* MAM-8 — without the libraries the filter offers All libraries alone, which looks like there
          is nothing to choose; this says why instead. A refresh that fails keeps the last list.
          MAM-16 — a banner, not an error state: the works below still load without it. */}
      {locations.error && !locations.data && (
        <Banner action={<RetryButton onRetry={() => locations.refetch()} />}>
          {t('locationFilter.loadFailed')}{' '}
          {getApiErrorMessage(locations.error)}
        </Banner>
      )}

      {/* Outside the ternary below, so a failed refresh adds a line above the cards instead of
          replacing them. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => works.refetch()} />}>
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {load.error ? (
        // A filter the server refuses (400) is its lasting answer: no Try again for it.
        <ErrorState
          title={t('loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 && filtered ? (
        <div className="settings-empty">
          <p>{t('noMatch')}</p>
          <Button type="button" variant="outline" size="sm" onClick={clearFilters}>
            {t('clearFilters')}
          </Button>
        </div>
      ) : data && data.totalCount === 0 ? (
        <div className="settings-empty">
          {/* Worded to hold before the first sync as well as after one that found nothing, or after
              a library was removed. */}
          <p>{t('empty')}</p>
        </div>
      ) : data ? (
        <>
          <ul className="library-works">
            {data.items.map((work) => (
              <li key={work.id}>
                <LibraryWorkCard
                  work={work}
                  episodeFilters={episodeFilters}
                  actions={<TmdbAction isOwner={isOwner} onOpen={() => setLinking(work)} />}
                />
              </li>
            ))}
          </ul>

          <ListPager
            page={page}
            pages={pages}
            pageSize={pageSize}
            pageSizes={PAGE_SIZES}
            totalCount={data.totalCount}
            onPage={goToPage}
            onPageSize={changePageSize}
          />
        </>
      ) : null}

      {linking && <TmdbLinkDialog work={linking} onClose={() => setLinking(null)} />}
    </div>
  )
}

// ── a card's action ──

/**
 * MAM-34 — "TMDb…" on a card: opens the work's TMDb dialog. Disabled with the reason for anyone but the
 * owner — the API would refuse the dialog with the same one.
 */
function TmdbAction({ isOwner, onOpen }: { isOwner: boolean; onOpen: () => void }) {
  const { t } = useTranslation('libraryLinks')
  const reason = isOwner ? undefined : t('card.notOwner')

  return (
    <DisabledReason reason={reason}>
      <Button type="button" variant="outline" size="sm" title={t('card.actionTitle')} disabled={reason !== undefined} onClick={onOpen}>
        <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
        <span className="label-text">{t('card.action')}</span>
      </Button>
    </DisabledReason>
  )
}

/**
 * MAM-34 — "Review works without a TMDb id", beside the "Missing an id" filter: opens the TMDb page.
 * Disabled with the reason for anyone but the owner — the page's routes would refuse them with it.
 */
function ReviewEntry({ isOwner, onOpen }: { isOwner: boolean; onOpen: () => void }) {
  const { t } = useTranslation('libraryLinks')

  if (!isOwner) {
    return (
      <DisabledReason reason={t('card.notOwner')}>
        <Button type="button" variant="outline" size="sm" disabled>
          <ListChecks className="h-3.5 w-3.5" aria-hidden="true" />
          {t('entry.review')}
        </Button>
      </DisabledReason>
    )
  }
  return (
    <Button asChild variant="outline" size="sm">
      <Link to="/library/tmdb" onClick={onOpen}>
        <ListChecks className="h-3.5 w-3.5" aria-hidden="true" />
        {t('entry.review')}
      </Link>
    </Button>
  )
}
