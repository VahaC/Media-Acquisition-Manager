import { useId, useState } from 'react'
import { ChevronDown, ChevronRight, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { VersionRows } from '@/components/library/VersionRows'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { formatNumber } from '@/lib/format'
import { seasonName, seasonParam } from '@/lib/library-episodes'
import { compareVersions } from '@/lib/library-versions'
import { useLibraryEpisodes } from '@/lib/queries'
import type { LibraryEpisodeFilters, LibrarySeason, LibraryWork } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — Loading…, and an episode without a version
import '@/styles/library.css' // .library-season-*, .library-episode*, .library-work-ids, .library-badge-warning

/** Per device, one length for every season: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:library:episodes:pageSize'

interface SeriesSeasonsProps {
  seriesId: string
  /** As the works list counted them: regular seasons, then the specials, then the episodes without one. */
  seasons: LibrarySeason[]
  /** The works list's search and filters, which made the counts; none on the Search page. */
  filters?: LibraryEpisodeFilters
}

/**
 * MAM-32 — a series' seasons on its card, in place of a movie's versions (a series has none: its
 * episodes hold them). Each season is a row that says how many episodes it holds and opens to them,
 * a page at a time from the server with the list's own search and filters — a series of 400
 * episodes is never sent whole. An episode shows its number and title, the ids it lacks, and its
 * versions as the Library's rows (VersionRows).
 *
 * Collapsed to begin with, and each opens on its own: two seasons can be compared side by side.
 */
export function SeriesSeasons({ seriesId, seasons, filters = {} }: SeriesSeasonsProps) {
  const { t } = useTranslation('library')
  const episodesId = useId()
  // By the season's param, which a sync keeps, so a season stays open while the works refresh.
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())

  const toggle = (key: string) => {
    const next = new Set(open)
    if (!next.delete(key)) next.add(key)
    setOpen(next)
  }

  return (
    <ul className="library-season-rows" aria-label={t('seasons.label')}>
      {seasons.map((season, index) => {
        const key = seasonParam(season.number)
        const isOpen = open.has(key)
        const id = `${episodesId}-${index}`
        const Chevron = isOpen ? ChevronDown : ChevronRight
        return (
          <li key={key}>
            <button
              type="button"
              className="library-season-row"
              aria-expanded={isOpen}
              aria-controls={isOpen ? id : undefined}
              onClick={() => toggle(key)}
            >
              <Chevron className="library-version-chevron h-4 w-4" aria-hidden="true" />
              <span className="library-season-name">{seasonName(season.number)}</span>
              <span className="library-season-count">
                {t('seasons.episodes', { count: season.episodeCount, value: formatNumber(season.episodeCount) })}
              </span>
            </button>
            {isOpen && <SeasonEpisodes id={id} seriesId={seriesId} season={season.number} filters={filters} />}
          </li>
        )
      })}
    </ul>
  )
}

// ── one opened season ──

/**
 * One page of a season's episodes. The page remembers the filters it belongs to, so new filters
 * start again at page 1 without an effect — the idiom of LibraryPage.
 */
function SeasonEpisodes({ id, seriesId, season, filters }: {
  id: string
  seriesId: string
  season: number | null
  filters: LibraryEpisodeFilters
}) {
  const { t } = useTranslation(['library', 'common'])
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  const filtersKey = [filters.search, filters.locationId, filters.identifiers].join('\n')
  const [paging, setPaging] = useState({ filtersKey, page: 1 })
  if (paging.filtersKey !== filtersKey) setPaging({ filtersKey, page: 1 })
  const page = paging.filtersKey === filtersKey ? paging.page : 1
  const episodes = useLibraryEpisodes(seriesId, { season: seasonParam(season), ...filters, page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = episodes
  const load = useLoadRetry(episodes)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A sync can leave fewer episodes than the page on screen needs: move to the last page there is.
  if (data && !isPlaceholderData && page > pages) setPaging({ filtersKey, page: pages })
  const name = seasonName(season)

  const goToPage = (next: number) => setPaging({ filtersKey, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  return (
    <div id={id} className="library-season-episodes">
      {/* Outside the ternary below, so a failed refresh adds a line above the episodes instead of
          replacing them. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => episodes.refetch()} />}>
          {t('seasons.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}
      {load.error ? (
        // A series a sync removed (a bare 404) won't come back on a second try.
        <ErrorState
          title={t('seasons.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 ? (
        <p className="settings-muted">{t('seasons.noEpisodes')}</p>
      ) : data ? (
        <>
          <ul className="library-episodes" aria-label={t('seasons.episodesOf', { season: name })}>
            {data.items.map((episode) => <li key={episode.id}><EpisodeItem episode={episode} /></li>)}
          </ul>
          {/* Only where a page length can matter: a season within the shortest page needs no pager. */}
          {data.totalCount > PAGE_SIZES[0] && (
            <ListPager
              page={page}
              pages={pages}
              pageSize={pageSize}
              pageSizes={PAGE_SIZES}
              totalCount={data.totalCount}
              onPage={goToPage}
              onPageSize={changePageSize}
            />
          )}
        </>
      ) : null}
    </div>
  )
}

/**
 * One episode: its number and title, the ids of its kind it lacks (IMDb, TVDB), and its versions as
 * the Library's rows. Its ids themselves stay off the row, which is one of many under a season; the
 * missing ones are what the Identifiers filter looks for.
 */
function EpisodeItem({ episode }: { episode: LibraryWork }) {
  const { t } = useTranslation('library')
  const titleId = useId()
  const title = episode.title ?? t('work.unknownTitle')
  const number = episode.episode?.episodeNumber ?? null
  const versions = episode.versions.toSorted(compareVersions)
  const showOriginalTitle = episode.originalTitle !== null && episode.originalTitle !== episode.title

  return (
    <article className="library-episode" aria-labelledby={titleId}>
      <div className="library-episode-head">
        <div className="library-work-heading">
          <p id={titleId} className="library-episode-title">
            {number === null ? title : t('episode.numberedTitle', { number, title })}
          </p>
          {showOriginalTitle && (
            <p className="library-work-meta">{t('work.originalTitle', { title: episode.originalTitle })}</p>
          )}
        </div>
        {episode.missingIdentifiers.length > 0 && (
          <ul className="library-work-ids" aria-label={t('work.identifiers')}>
            {episode.missingIdentifiers.map((scheme) => (
              <li key={scheme}>
                <span className="ui-badge library-badge-warning">
                  <TriangleAlert className="h-3 w-3" aria-hidden="true" />
                  {t('work.missingIdentifier', { scheme: t(`schemes.${scheme}`) })}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
      {versions.length === 0 ? (
        <p className="settings-muted">{t('work.noVersions')}</p>
      ) : (
        <VersionRows versions={versions} />
      )}
    </article>
  )
}
