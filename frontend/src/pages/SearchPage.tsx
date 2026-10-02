import { useEffect, useEffectEvent, useId, useRef, useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { CircleAlert, CircleCheck, Loader2, TriangleAlert } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigationType, useSearchParams } from 'react-router-dom'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import { DownloadDialog } from '@/components/search/DownloadDialog'
import { MatchBadges } from '@/components/search/MatchBadges'
import { ReleaseDetailsDialog } from '@/components/search/ReleaseDetailsDialog'
import { ReleaseFilters, type ReleaseFilterValues } from '@/components/search/ReleaseFilters'
import { ReleaseRow } from '@/components/search/ReleaseRow'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatNumber } from '@/lib/format'
import { useCreateSearch, useSearch, useSearchReleases } from '@/lib/queries'
import { isSearchId, parseSearchQuery } from '@/lib/search-query'
import { bytesFromGibibytes } from '@/lib/size-input'
import type { LibraryMatch, SearchRelease, SearchReleasesQuery, SearchRequest, SearchResponse, SearchSource } from '@/lib/types'
import { useDebounced } from '@/lib/use-debounced'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, toolbar and empty states of this page
import '@/styles/auth.css' // .auth-field — the label over each field of the form, as the dialogs lay them out
import '@/styles/library.css' // .library-works, .library-source-*, .library-link — the cards' column, the source lines, the Library link
import '@/styles/search.css' // .search-* — the form, the sections, the library's notes and the release list

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:search:pageSize'
/** A size bound waits for a pause in typing, as the Library's search does. */
const SIZE_DEBOUNCE_MS = 300

/** The form's fields, as typed. MAM-16 — the title is typed in the header, so it has none. */
interface SearchForm {
  year: string
  imdbId: string
  tmdbId: string
}

const EMPTY_FORM: SearchForm = { year: '', imdbId: '', tmdbId: '' }

/** No filter, the server's default order. */
const NO_FILTERS: ReleaseFilterValues = { source: '', language: '', quality: '', minSize: '', maxSize: '', sort: 'seeders' }

/** The release list's query without its paging: what a change of starts again at page 1. */
type AppliedFilters = Omit<SearchReleasesQuery, 'page' | 'pageSize'>

const FILTER_KEYS = ['source', 'language', 'quality', 'minSizeBytes', 'maxSizeBytes', 'sort'] as const

/**
 * MAM-12 — the Search page: a title (and, to narrow it, a year, an IMDb or a TMDb id) looked for in
 * the library and in every enabled release source at once. "In your library" shows each matching
 * copy with the facts the app verified and why it matched, then the copies that only hold every word
 * of the title, said to be no match; "Available releases" lists every release the sources found —
 * even for a movie already in the library — with what each title claims, and a line per source, so a
 * source that failed says so while the others' releases still show.
 *
 * MAM-16 — the title comes from the header's search field: Enter there opens `/search?q=<the line>`,
 * and the page makes that search at once, a year in brackets ending the line taken as its year (ADR
 * 0015), then puts the search's id in the address in place of the line. The page's own form narrows
 * the search on screen by year, IMDb id and TMDb id: a new search with the same title. Without a
 * search, the page says how to start one.
 *
 * The server stores the search and its releases for a day. The page keeps only its id, in the
 * address (`?id=`), so F5 and Back bring the same search back, and every filter, sort and page reads
 * the stored releases instead of asking the sources again. Every signed-in user searches; someone
 * else's search, like an expired one, is a 404, which the page words as "no longer available".
 */
export function SearchPage() {
  const { t } = useTranslation(['search', 'common'])
  const [searchParams, setSearchParams] = useSearchParams()
  const { key: entryKey } = useLocation()
  const navigationType = useNavigationType()
  const addressId = searchParams.get('id') ?? ''
  // To the server an id that isn't one is the same as a missing search, so the page words it the
  // same way ("no longer available") without asking.
  const id = isSearchId(addressId) ? addressId : ''
  const malformedId = addressId !== '' && id === ''
  // MAM-16 — the line typed in the header. An address with an id is that search, whatever else it
  // holds.
  const line = addressId === '' ? (searchParams.get('q') ?? '').trim() : ''
  const asked = line === '' ? null : parseSearchQuery(line)
  const search = useSearch(id)
  const { data, error, isLoading } = search
  const load = useLoadRetry(search)
  const create = useCreateSearch()
  const [form, setForm] = useState(EMPTY_FORM)
  const [edited, setEdited] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  // The history entry a search is being made from, while it is: Back to another entry mid-way shows
  // that one as it is, not as searching.
  const [creatingFrom, setCreatingFrom] = useState<string | null>(null)
  // Every move — a search made, Back, the header's Enter — lands on another history entry, and each
  // starts afresh: the fields follow the search it shows, and the last search's refusal is gone. The
  // page's own move to the search it made (the only replace here) keeps what the person typed while
  // the sources answered, as the header's field does: that can take up to 20 s.
  const [seenEntry, setSeenEntry] = useState(entryKey)
  if (entryKey !== seenEntry) {
    setSeenEntry(entryKey)
    if (navigationType !== 'REPLACE') setEdited(false)
    setCreateError(null)
  }
  // Until the person types, the fields say what the search on screen was narrowed by — the one in
  // the address on F5, one Back returns to, the one just made — or the year the line asks for.
  const shown = edited ? form : data ? formOf(data) : { ...EMPTY_FORM, year: asked?.year ?? '' }
  // The title "Results for" names and the form narrows: the search on screen's, else the line's.
  const text = data?.text ?? asked?.text ?? null
  const searching = creatingFrom === entryKey

  // A search waits for every source, up to about 20 s for a slow one, so the person may have moved
  // on by the time it answers: to another page, Back to an earlier search, or to a newer `?q=`. Its
  // answer then must not move them: in declarative mode setSearchParams still navigates after an
  // unmount, to this page's path, and an older `?q=` answering last would replace the newer one. So
  // an answer moves the page only while it is still on the history entry the search was made from.
  // An effect, as the entry on screen is the external thing tracked; cleared on unmount, and set
  // again on StrictMode's second mount in development.
  const entryOnScreen = useRef<string | null>(null)
  useEffect(() => {
    entryOnScreen.current = entryKey
    return () => { entryOnScreen.current = null }
  }, [entryKey])

  // ADR 0011 — the `?q=` entries this page made a search from, each with the id its search answered
  // with, or null while it is being made. A search whose answer replaces its entry is that entry's:
  // the line's own, or Apply's after the server refused the line.
  const madeFromLine = useRef(new Map<string, string | null>())

  const start = async (request: SearchRequest, replace: boolean) => {
    const madeFrom = entryKey
    setCreatingFrom(madeFrom)
    if (replace) madeFromLine.current.set(madeFrom, null)
    try {
      const created = await create.mutateAsync(request)
      if (replace) madeFromLine.current.set(madeFrom, created.id)
      // useCreateSearch has cached the answer under its id either way; only the move to it is skipped.
      if (entryOnScreen.current === madeFrom) setSearchParams({ id: created.id }, { replace })
    } catch (e: unknown) {
      // Refused: coming back to the line asks again, as opening it afresh would.
      if (replace) madeFromLine.current.delete(madeFrom)
      if (entryOnScreen.current === madeFrom) setCreateError(getApiErrorMessage(e, t('form.failed')))
    } finally {
      setCreatingFrom((current) => (current === madeFrom ? null : current))
    }
  }

  // ADR 0011 — `?q=` makes its search once per history entry: StrictMode runs this effect twice in
  // development, and every run would ask every source again. The answer replaces the `?q=` entry,
  // so Back skips it to the page before, and neither Back nor F5 asks again. Coming back to the entry
  // by Back and Forward, while the page stays open, waits for a search still being made, or moves to
  // the answer that came meanwhile (it is in the cache); a page opened afresh knows of no search.
  const searchFromAddress = useEffectEvent((typed: string) => {
    const made = madeFromLine.current.get(entryKey)
    if (made === undefined) void start(parseSearchQuery(typed), true)
    else if (made !== null) setSearchParams({ id: made }, { replace: true })
  })
  useEffect(() => {
    if (line !== '') searchFromAddress(line)
  }, [line, entryKey])

  const edit = (changes: Partial<SearchForm>) => {
    setForm({ ...shown, ...changes })
    setEdited(true)
  }

  const narrow = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (text === null) return
    setCreateError(null)
    void start(
      {
        text,
        // Left out rather than sent empty; the server reads what is there and words what is wrong.
        year: shown.year.trim() || undefined,
        imdbId: shown.imdbId.trim() || undefined,
        tmdbId: shown.tmdbId.trim() || undefined,
      },
      // A new entry, so Back returns to the search before this one; but a `?q=` the server refused
      // is replaced, so Back never returns to a line that has no search.
      asked !== null,
    )
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      {text !== null && (
        <div className="search-narrow">
          <p className="search-results-head">
            <span className="search-results-for">
              {/* The title is a child of the markup, never a value Trans fills in: with escaping off
                  (i18n.ts), a title holding "<strong>" or "{{text}}" would become markup or a
                  placeholder instead of showing those characters. */}
              <Trans
                t={t}
                i18nKey="form.resultsFor"
                components={{ strong: <strong />, text: <span>{text}</span> }}
              />
            </span>
            <span className="search-results-note" role="status" data-running={searching || undefined}>
              {searching && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
              {searching ? t('form.submitting') : t('form.narrow')}
            </span>
          </p>
          {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
          <form className="search-form" noValidate onSubmit={narrow}>
            <div className="search-form-fields">
              <label className="auth-field">
                <span className="ui-label">{t('form.year')}{' '}{t('form.optional')}</span>
                <Input
                  value={shown.year}
                  placeholder={t('form.yearPlaceholder')}
                  inputMode="numeric"
                  autoComplete="off"
                  onChange={(event) => edit({ year: event.target.value })}
                />
              </label>
              <label className="auth-field">
                <span className="ui-label">{t('form.imdbId')}{' '}{t('form.optional')}</span>
                <Input
                  value={shown.imdbId}
                  placeholder={t('form.imdbIdPlaceholder')}
                  autoComplete="off"
                  onChange={(event) => edit({ imdbId: event.target.value })}
                />
              </label>
              <label className="auth-field">
                <span className="ui-label">{t('form.tmdbId')}{' '}{t('form.optional')}</span>
                <Input
                  value={shown.tmdbId}
                  placeholder={t('form.tmdbIdPlaceholder')}
                  inputMode="numeric"
                  autoComplete="off"
                  onChange={(event) => edit({ tmdbId: event.target.value })}
                />
              </label>
              <Button type="submit" variant="outline" disabled={searching}>
                {searching && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
                {t('form.apply')}
              </Button>
            </div>
          </form>
        </div>
      )}
      {createError && <Banner onDismiss={() => setCreateError(null)}>{createError}</Banner>}

      {/* Outside the ternary below, so a failed refresh adds a line above the results instead of
          replacing them. */}
      {error && data && (isGone(error) ? (
        <Banner>{t('expired')}</Banner>
      ) : (
        <Banner action={<RetryButton onRetry={() => search.refetch()} />}>
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      ))}

      {/* A bare 404 (expired, or someone else's) won't change on a second try: no Try again, and no
          server sentence to add. */}
      {malformedId || (load.error && isGone(load.error)) ? (
        <ErrorState title={t('expired')} />
      ) : load.error ? (
        <ErrorState title={t('loadFailed')} onRetry={load.retry} retrying={load.retrying}>
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : id === '' ? (
        // A `?q=` search being made, or one the server refused, has nothing to show here yet.
        asked === null && (
          <div className="search-hint">
            <p className="search-hint-title">{t('empty.title')}</p>
            <p className="settings-muted">{t('empty.hint')}</p>
          </div>
        )
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data ? (
        // A new search is a new set of sources and facets: its own filters, page and dialog, and no
        // releases of the last search kept on screen while its first page loads.
        <SearchResults key={data.id} search={data} />
      ) : null}
    </div>
  )
}

/** The search as the form shows it: every field as text, one the search didn't give left blank. */
function formOf(search: SearchResponse): SearchForm {
  return {
    year: search.year === null ? '' : String(search.year),
    imdbId: search.imdbId ?? '',
    tmdbId: search.tmdbId ?? '',
  }
}

/** A bare 404: the search expired (a day after it was made) or isn't this user's — the same answer. */
function isGone(error: unknown): boolean {
  return isAxiosError(error) && error.response?.status === 404
}

// ── the results ──

function SearchResults({ search }: { search: SearchResponse }) {
  return (
    <>
      <LibrarySection search={search} />
      <ReleasesSection search={search} />
    </>
  )
}

/**
 * Every library work the search matched, strongest first, as the Library page's cards with why each
 * matched. Every match, unpaged: one title matches a handful of copies, and the server lists them
 * all (it picks none).
 *
 * MAM-12 — then, under a line of their own that says they are no match, the works that matched in
 * no other way but hold every word of the title, whole, in one of their titles or file paths
 * ("rambo" lists "Rambo III", and "First Blood" by its folder; ADR 0012) — a release still matches
 * them only on its own evidence. A common word could list hundreds, so the server lists at most 20
 * and counts the rest; the line under them takes the person to the Library page searched for the
 * same text.
 */
function LibrarySection({ search }: { search: SearchResponse }) {
  const { t } = useTranslation('search')
  const titleId = useId()
  const wordsId = useId()
  const matches = search.library.filter((match) => match.evidence !== 'Words')
  const byWords = search.library.filter((match) => match.evidence === 'Words')
  const unlisted = search.libraryUnlistedCount

  return (
    <section className="search-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="search-section-title">{t('library.title')}</h2>
      {matches.length === 0 ? (
        <p className="settings-muted">{t('library.empty')}</p>
      ) : (
        <ul className="library-works">
          {matches.map((match) => <LibraryMatchItem key={match.work.id} match={match} searchText={search.text} />)}
        </ul>
      )}
      {byWords.length > 0 && (
        <>
          <p id={wordsId} className="settings-muted search-library-note">{t('library.wordsLead', { text: search.text })}</p>
          <ul className="library-works" aria-labelledby={wordsId}>
            {byWords.map((match) => <LibraryMatchItem key={match.work.id} match={match} searchText={search.text} />)}
          </ul>
        </>
      )}
      {unlisted > 0 && (
        <p className="settings-muted search-library-note">
          {t('library.unlisted', { count: unlisted, value: formatNumber(unlisted) })}{' '}
          {/* URLSearchParams encodes the text, so a `&` or a `#` in a title stays part of it. */}
          <Link to={`/library?${new URLSearchParams({ search: search.text })}`} className="library-link">
            {t('library.unlistedLink', { text: search.text })}
          </Link>
        </p>
      )}
    </section>
  )
}

/** One work of "In your library": the Library page's card, with why it is listed under its title. */
function LibraryMatchItem({ match, searchText }: { match: LibraryMatch; searchText: string }) {
  return (
    <li>
      <LibraryWorkCard
        work={match.work}
        headingLevel={3}
        evidence={(
          <MatchBadges
            evidence={match.evidence}
            conflictingSchemes={match.conflictingSchemes}
            matchedIdentifiers={match.matchedIdentifiers}
            matchedTitle={match.matchedTitle}
            searchText={searchText}
          />
        )}
      />
    </li>
  )
}

/**
 * How each source answered, then the stored releases filtered, sorted and paged by the server. The
 * page remembers the filters it belongs to, so a change starts again at page 1 without an effect —
 * the idiom of LibraryPage.
 */
function ReleasesSection({ search }: { search: SearchResponse }) {
  const { t } = useTranslation(['search', 'common'])
  const titleId = useId()
  const [filters, setFilters] = useState(NO_FILTERS)
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  const [details, setDetails] = useState<SearchRelease | null>(null)
  // MAM-13 — the release being sent to a download client; its dialog is mounted only while it is set.
  const [downloading, setDownloading] = useState<SearchRelease | null>(null)

  // Typing a bound waits for a pause; emptying the box applies at once. A bound that isn't a size
  // is left out (ReleaseFilters says so under the row).
  const debouncedMinSize = useDebounced(filters.minSize.trim(), SIZE_DEBOUNCE_MS)
  const debouncedMaxSize = useDebounced(filters.maxSize.trim(), SIZE_DEBOUNCE_MS)
  const sizeBound = (typed: string, debounced: string) =>
    typed.trim() === '' ? undefined : bytesFromGibibytes(debounced) ?? undefined
  const applied: AppliedFilters = {
    source: filters.source || undefined,
    language: filters.language || undefined,
    quality: filters.quality || undefined,
    minSizeBytes: sizeBound(filters.minSize, debouncedMinSize),
    maxSizeBytes: sizeBound(filters.maxSize, debouncedMaxSize),
    sort: filters.sort,
  }
  const [paging, setPaging] = useState({ filters: applied, page: 1 })
  const onFilters = FILTER_KEYS.every((key) => paging.filters[key] === applied[key])
  if (!onFilters) setPaging({ filters: applied, page: 1 })
  const page = onFilters ? paging.page : 1
  const filtered = FILTER_KEYS.some((key) => key !== 'sort' && applied[key] !== undefined)
  const releases = useSearchReleases(search.id, { ...applied, page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = releases
  const load = useLoadRetry(releases)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  const hasReleases = search.sources.some((source) => source.releaseCount > 0)

  const goToPage = (next: number) => setPaging({ filters: applied, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  // The order stays: it is not a filter, and the list it sorts is the one coming back.
  const clearFilters = () => setFilters({ ...NO_FILTERS, sort: filters.sort })

  return (
    <section className="search-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="search-section-title">{t('releases.title')}</h2>

      {search.sources.length === 0 ? (
        <p className="settings-muted">{t('releases.noSources')}</p>
      ) : (
        <ul className="search-sources" aria-label={t('sources.label')}>
          {search.sources.map((source) => <SourceLine key={source.id} source={source} />)}
        </ul>
      )}

      {/* Nothing to filter when no source listed anything. */}
      {hasReleases && (
        <ReleaseFilters sources={search.sources} facets={search.facets} values={filters} onChange={setFilters} />
      )}

      {/* The search expiring under a kept page is the same 404 as on its first load, with no Retry. */}
      {error && data && !isPlaceholderData && (isGone(error) ? (
        <Banner>{t('expired')}</Banner>
      ) : (
        <Banner action={<RetryButton onRetry={() => releases.refetch()} />}>
          {t('releases.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      ))}

      {load.error && isGone(load.error) ? (
        <ErrorState title={t('expired')} />
      ) : load.error ? (
        // Bounds the server refuses (400) are its lasting answer: no Try again, the filters are above.
        <ErrorState
          title={t('releases.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 && filtered ? (
        <div className="settings-empty">
          <p>{t('releases.noMatch')}</p>
          <Button type="button" variant="outline" size="sm" onClick={clearFilters}>
            {t('releases.clearFilters')}
          </Button>
        </div>
      ) : data && data.totalCount === 0 ? (
        // Without a source the line above already says why there is nothing.
        search.sources.length > 0 && <p className="settings-muted">{t('releases.none')}</p>
      ) : data ? (
        <>
          <ul className="search-releases">
            {data.items.map((release) => (
              <li key={release.id}>
                <ReleaseRow
                  release={release}
                  library={search.library}
                  onDetails={() => setDetails(release)}
                  onDownload={() => setDownloading(release)}
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

      {details && (
        <ReleaseDetailsDialog release={details} library={search.library} onClose={() => setDetails(null)} />
      )}
      {downloading && <DownloadDialog release={downloading} onClose={() => setDownloading(null)} />}
    </section>
  )
}

/**
 * How one source answered: how many releases it listed, why it wasn't asked, or how the call failed
 * — with the owner's sentence after it (the API sends it to the owner only). A failed source is one
 * line here and never hides the others' releases.
 */
function SourceLine({ source }: { source: SearchSource }) {
  const { t } = useTranslation(['search', 'common'])
  const line = source.status === 'Succeeded'
    ? source.releaseCount === 0
      ? t('sources.noReleases')
      : t('sources.releases', { count: source.releaseCount, value: formatNumber(source.releaseCount) })
    : source.status !== null
      ? t(`sources.status.${source.status}`)
      : source.notSearchedReason !== null
        ? t('sources.notSearched', { reason: t(`sources.reasons.${source.notSearchedReason}`) })
        : null
  const text = (
    <span>
      <strong>{source.name}</strong>
      {line !== null && <>{t('common:separator')}{line}</>}
      {source.error && <>{' '}{source.error}</>}
    </span>
  )

  // Not asked: a warning, as the owner can usually put it right; a failed call: an error.
  if (source.status === null) {
    return (
      <li className="library-source-warning">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        {text}
      </li>
    )
  }
  const succeeded = source.status === 'Succeeded'
  return (
    <li className="library-source-status" data-status={succeeded ? 'Succeeded' : 'Failed'}>
      {succeeded
        ? <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
        : <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />}
      {text}
    </li>
  )
}
