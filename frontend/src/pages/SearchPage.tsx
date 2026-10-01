import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { CircleAlert, CircleCheck, Loader2, Search as SearchIcon, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import { MatchBadges } from '@/components/search/MatchBadges'
import { ReleaseDetailsDialog } from '@/components/search/ReleaseDetailsDialog'
import { ReleaseFilters, type ReleaseFilterValues } from '@/components/search/ReleaseFilters'
import { ReleaseRow } from '@/components/search/ReleaseRow'
import { ListPager } from '@/components/shared/ListPager'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatNumber } from '@/lib/format'
import { useCreateSearch, useSearch, useSearchReleases } from '@/lib/queries'
import { bytesFromGibibytes } from '@/lib/size-input'
import type { SearchRelease, SearchReleasesQuery, SearchResponse, SearchSource } from '@/lib/types'
import { useDebounced } from '@/lib/use-debounced'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, toolbar, banners and empty states of this page
import '@/styles/auth.css' // .auth-field — the label over each field of the form, as the dialogs lay them out
import '@/styles/library.css' // .library-works, .library-source-status/-warning — the cards' column and the source lines
import '@/styles/search.css' // .search-* — the form, the sections and the release list

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:search:pageSize'
/** A size bound waits for a pause in typing, as the Library's search does. */
const SIZE_DEBOUNCE_MS = 300

/** The form's fields, as typed. */
interface SearchForm {
  text: string
  year: string
  imdbId: string
  tmdbId: string
}

const EMPTY_FORM: SearchForm = { text: '', year: '', imdbId: '', tmdbId: '' }

/** No filter, the server's default order. */
const NO_FILTERS: ReleaseFilterValues = { source: '', language: '', quality: '', minSize: '', maxSize: '', sort: 'seeders' }

/** The release list's query without its paging: what a change of starts again at page 1. */
type AppliedFilters = Omit<SearchReleasesQuery, 'page' | 'pageSize'>

const FILTER_KEYS = ['source', 'language', 'quality', 'minSizeBytes', 'maxSizeBytes', 'sort'] as const

/**
 * MAM-12 — a search's id as the API makes it (a GUID). `?id=` is the address's, so anyone can put
 * anything there: a link with `../settings/…` in it would otherwise send the reader's token to that
 * API path and read the answer as a search. Encoding the id (search-api.ts) keeps a `/` inside one
 * segment, but not `..` itself, so the page sends no other value at all.
 */
const SEARCH_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * MAM-12 — the Search page: a title (and, to narrow it, a year, an IMDb or a TMDb id) looked for in
 * the library and in every enabled release source at once. "In your library" shows each matching
 * copy with the facts the app verified and why it matched; "Available releases" lists every release
 * the sources found — even for a movie already in the library — with what each title claims, and a
 * line per source, so a source that failed says so while the others' releases still show.
 *
 * The server stores the search and its releases for a day. The page keeps only its id, in the
 * address (`?id=`), so F5 and Back bring the same search back, and every filter, sort and page reads
 * the stored releases instead of asking the sources again. Every signed-in user searches; someone
 * else's search, like an expired one, is a 404, which the page words as "no longer available".
 */
export function SearchPage() {
  const { t } = useTranslation(['search', 'common'])
  const [searchParams, setSearchParams] = useSearchParams()
  const addressId = searchParams.get('id') ?? ''
  // To the server an id that isn't one is the same as a missing search, so the page words it the
  // same way ("no longer available") without asking.
  const id = SEARCH_ID_PATTERN.test(addressId) ? addressId : ''
  const malformedId = addressId !== '' && id === ''
  const { data, error, isLoading } = useSearch(id)
  const create = useCreateSearch()
  const [form, setForm] = useState(EMPTY_FORM)
  const [createError, setCreateError] = useState<string | null>(null)
  // The search the form was last filled from, and whether the person has typed since. A search that
  // loads — the one in the address on F5, one Back returns to, the one just made — fills a form
  // nobody is typing in, so the fields always say what the results below were searched for.
  const [filledFrom, setFilledFrom] = useState<string | null>(null)
  const [edited, setEdited] = useState(false)
  if (data && !edited && filledFrom !== data.id) {
    setFilledFrom(data.id)
    setForm(formOf(data))
  }
  // A search waits for every source, up to about 20 s for a slow one, so the person may have gone to
  // another page by the time it answers. Once they have, its answer must not pull them back: in
  // declarative mode setSearchParams still navigates after an unmount, to this page's path. An
  // effect, as the page's own mount is the external thing tracked; set on every mount, so
  // StrictMode's mount, unmount and mount again in development doesn't leave it false.
  const mounted = useRef(false)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])

  const edit = (changes: Partial<SearchForm>) => {
    setForm({ ...form, ...changes })
    setEdited(true)
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setCreateError(null)
    try {
      const created = await create.mutateAsync({
        text: form.text.trim(),
        // Left out rather than sent empty; the server reads what is there and words what is wrong.
        year: form.year.trim() || undefined,
        imdbId: form.imdbId.trim() || undefined,
        tmdbId: form.tmdbId.trim() || undefined,
      })
      // useCreateSearch has cached the answer under its id either way; only the move to it is skipped.
      if (!mounted.current) return
      setEdited(false)
      // A new entry, not a replacement: Back returns to the search before this one.
      setSearchParams({ id: created.id })
    } catch (e: unknown) {
      setCreateError(getApiErrorMessage(e, t('form.failed')))
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
      <form className="search-form" aria-label={t('form.label')} noValidate onSubmit={(event) => void submit(event)}>
        <div className="search-form-fields">
          <label className="auth-field search-form-title">
            <span className="ui-label">{t('form.title')}</span>
            <Input
              value={form.text}
              placeholder={t('form.titlePlaceholder')}
              autoComplete="off"
              required
              onChange={(event) => edit({ text: event.target.value })}
            />
          </label>
          <label className="auth-field">
            <span className="ui-label">{t('form.year')}{' '}{t('form.optional')}</span>
            <Input
              value={form.year}
              placeholder={t('form.yearPlaceholder')}
              inputMode="numeric"
              autoComplete="off"
              onChange={(event) => edit({ year: event.target.value })}
            />
          </label>
          <label className="auth-field">
            <span className="ui-label">{t('form.imdbId')}{' '}{t('form.optional')}</span>
            <Input
              value={form.imdbId}
              placeholder={t('form.imdbIdPlaceholder')}
              autoComplete="off"
              onChange={(event) => edit({ imdbId: event.target.value })}
            />
          </label>
          <label className="auth-field">
            <span className="ui-label">{t('form.tmdbId')}{' '}{t('form.optional')}</span>
            <Input
              value={form.tmdbId}
              placeholder={t('form.tmdbIdPlaceholder')}
              inputMode="numeric"
              autoComplete="off"
              onChange={(event) => edit({ tmdbId: event.target.value })}
            />
          </label>
          <Button type="submit" disabled={create.isPending}>
            {create.isPending
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : <SearchIcon className="h-3.5 w-3.5" aria-hidden="true" />}
            {create.isPending ? t('form.submitting') : t('form.submit')}
          </Button>
        </div>
        {createError && <p className="settings-error" role="alert">{createError}</p>}
      </form>

      {/* Outside the ternary below, so a failed refresh adds a line above the results instead of
          replacing them. */}
      {error && data && (
        <p className="settings-banner" role="alert">
          <CircleAlert className="h-3.5 w-3.5 inline" aria-hidden="true" />{' '}
          {isGone(error) ? t('expired') : <>{t('refreshFailed')}{' '}{getApiErrorMessage(error)}</>}
        </p>
      )}

      {malformedId ? (
        <p className="settings-error" role="alert">{t('expired')}</p>
      ) : id === '' ? null : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : error && !data ? (
        <p className="settings-error" role="alert">
          {isGone(error) ? t('expired') : getApiErrorMessage(error, t('loadFailed'))}
        </p>
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
    text: search.text,
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
 * matched. All of them, unpaged: one title matches a handful of copies, and the server lists them
 * all (it picks none).
 */
function LibrarySection({ search }: { search: SearchResponse }) {
  const { t } = useTranslation('search')
  const titleId = useId()

  return (
    <section className="search-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="search-section-title">{t('library.title')}</h2>
      {search.library.length === 0 ? (
        <p className="settings-muted">{t('library.empty')}</p>
      ) : (
        <ul className="library-works">
          {search.library.map((match) => (
            <li key={match.work.id}>
              <LibraryWorkCard
                work={match.work}
                headingLevel={3}
                evidence={(
                  <MatchBadges
                    evidence={match.evidence}
                    conflictingSchemes={match.conflictingSchemes}
                    matchedIdentifiers={match.matchedIdentifiers}
                    matchedTitle={match.matchedTitle}
                  />
                )}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
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
  const { data, error, isLoading } = useSearchReleases(search.id, { ...applied, page, pageSize })
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

      {error && data && (
        <p className="settings-banner" role="alert">
          <CircleAlert className="h-3.5 w-3.5 inline" aria-hidden="true" />{' '}
          {t('releases.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </p>
      )}

      {isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : error && !data ? (
        <p className="settings-error" role="alert">
          {isGone(error) ? t('expired') : getApiErrorMessage(error, t('releases.loadFailed'))}
        </p>
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
                <ReleaseRow release={release} library={search.library} onDetails={() => setDetails(release)} />
              </li>
            ))}
          </ul>

          <ListPager
            page={page}
            pages={pages}
            pageSize={pageSize}
            pageSizes={PAGE_SIZES}
            onPage={goToPage}
            onPageSize={changePageSize}
          />
        </>
      ) : null}

      {details && (
        <ReleaseDetailsDialog release={details} library={search.library} onClose={() => setDetails(null)} />
      )}
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
