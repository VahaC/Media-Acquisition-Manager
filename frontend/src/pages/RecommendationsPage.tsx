import { useEffect, useId, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { RecommendationCard } from '@/components/recommendations/RecommendationCard'
import { RecommendationFilters, type RecommendationFilterValues } from '@/components/recommendations/RecommendationFilters'
import { RecommendationRunPanel } from '@/components/recommendations/RecommendationRunPanel'
import { RecommendationTabs } from '@/components/recommendations/RecommendationTabs'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import { qk, useRecommendationSettings, useRecommendations } from '@/lib/queries'
import { enabledTabs, sortFrom, tabMixesKinds } from '@/lib/recommendations'
import type { RecommendationFacets, RecommendationSort, RecommendationTab } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, the toolbar and the empty states of this page
import '@/styles/recommendations.css' // .recommendation-panel, -grid, -attribution — the tab's column, the cards' grid and the catalog's notice

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:recommendations:pageSize'

/** No filter: every work of the tab. */
const NO_FILTERS: RecommendationFilterValues = { reason: '', genreId: '', decade: '', kind: '', available: false }

/**
 * MAM-33 — Recommendations: the movies, series, cartoons and documentaries the library doesn't have
 * yet (Vasyl's decision 2), each with why it is here and what the release sources had for it. The
 * lists are computed for the installation after every library sync; each user reads the tabs they
 * switched on in their Profile (decision 5), without the works they marked "Not interested" (decision
 * 3). A recommendation only proposes (ADR 0023): "Releases" opens the Search page, and nothing is
 * ever sent from here.
 *
 * The tab is in the address (`?tab=Movies`), so F5 and a link keep it; a tab that isn't one, or that
 * the user has off, reads as the first one they have on. So is the order of the works (`&sort=Rating`,
 * left out for Score, the server's default); one that isn't an order reads as Score. With every tab off the page says where to
 * turn one on. The settings must load first: they decide which tabs there are; a failed refresh of
 * them keeps the tabs they last gave under a banner.
 */
export function RecommendationsPage() {
  const { t } = useTranslation(['recommendations', 'common'])
  const settings = useRecommendationSettings()
  const load = useLoadRetry(settings)
  const tabs = settings.data ? enabledTabs(settings.data) : []

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      {/* Outside the ternary below, so a failed refresh of the settings adds a line above the tabs
          instead of replacing them. */}
      {settings.error && settings.data && (
        <Banner action={<RetryButton onRetry={() => settings.refetch()} />}>
          {t('settingsRefreshFailed')}{' '}
          {getApiErrorMessage(settings.error)}
        </Banner>
      )}

      {load.error ? (
        <ErrorState title={t('settingsLoadFailed')} onRetry={load.retry} retrying={load.retrying}>
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : settings.isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : settings.data && tabs.length === 0 ? (
        <div className="settings-empty">
          <p>{t('allTabsOff')}</p>
          <Button asChild variant="outline" size="sm">
            <Link to="/profile">{t('openProfile')}</Link>
          </Button>
        </div>
      ) : settings.data ? (
        <RecommendationLists tabs={tabs} />
      ) : null}
    </div>
  )
}

// ── the lists ──

/**
 * The run panel, the tabs and the current tab's toolbar, cards and pager. The filters and the page
 * belong to the tab they were set on, so another tab starts unfiltered at page 1 without an effect —
 * the idiom of LibraryPage. The order is in the address and stays from tab to tab; a new order starts
 * at page 1 like a new filter, and keeps the filters.
 *
 * Hidden cards ("Not interested") are kept here rather than in each card, so a tab the person comes
 * back to shows them as hidden rows until its list, asked for again on the way back, drops them.
 * Leaving the page with any hidden drops the cached lists, so the next visit doesn't show them as
 * cards while it asks again.
 */
function RecommendationLists({ tabs }: { tabs: readonly RecommendationTab[] }) {
  const { t } = useTranslation(['recommendations', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const asked = searchParams.get('tab')
  const tab = tabs.find((candidate) => candidate === asked) ?? tabs[0]
  const sort = sortFrom(searchParams.get('sort'))
  const idPrefix = useId()
  const panelId = `${idPrefix}-panel`
  const tabId = (of: RecommendationTab) => `${idPrefix}-tab-${of}`
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  const [view, setView] = useState({ tab, sort, filters: NO_FILTERS, page: 1 })
  const onTab = view.tab === tab
  // The page belongs to the tab and the order it was reached in; the filters to the tab alone.
  const onView = onTab && view.sort === sort
  if (!onView) setView({ tab, sort, filters: onTab ? view.filters : NO_FILTERS, page: 1 })
  const filters = onTab ? view.filters : NO_FILTERS
  const page = onView ? view.page : 1
  const [hiddenIds, setHiddenIds] = useState<ReadonlySet<string>>(() => new Set())
  const hidAny = useRef(false)

  useEffect(() => () => {
    if (hidAny.current) queryClient.removeQueries({ queryKey: qk.recommendations.lists })
  }, [queryClient])

  const mixesKinds = tabMixesKinds(tab)
  const filtered = filters.reason !== '' || filters.genreId !== '' || filters.decade !== ''
    || (mixesKinds && filters.kind !== '') || filters.available
  const list = useRecommendations({
    tab,
    reason: filters.reason || undefined,
    available: filters.available || undefined,
    genreId: filters.genreId === '' ? undefined : Number(filters.genreId),
    decade: filters.decade === '' ? undefined : Number(filters.decade),
    kind: mixesKinds && filters.kind !== '' ? filters.kind : undefined,
    sort: sort === 'Score' ? undefined : sort,
    page,
    pageSize,
  })
  const { data, error, isLoading, isPlaceholderData } = list
  const load = useLoadRetry(list)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A computation can leave fewer works than the page on screen needs: move to the last page there
  // is. Only on this page's own answer, not the one kept on screen while it loads.
  if (onView && data && !isPlaceholderData && page > pages) setView({ tab, sort, filters, page: pages })
  // The selects offer what the tab's last answer did: a filter the server refuses (400) leaves no
  // answer, and the toolbar must stay to change it.
  const [lastFacets, setLastFacets] = useState<{ tab: RecommendationTab; facets: RecommendationFacets } | null>(null)
  if (data && !isPlaceholderData && (lastFacets?.tab !== tab || lastFacets.facets !== data.facets)) {
    setLastFacets({ tab, facets: data.facets })
  }
  const facets = data && !isPlaceholderData ? data.facets : lastFacets?.tab === tab ? lastFacets.facets : data?.facets
  // Nothing to filter in a tab without works; a filtered one keeps its toolbar, to change or clear
  // the filters.
  const showFilters = facets !== undefined && (filtered || (data !== undefined && data.totalCount > 0))

  /** The address of a tab in an order; Score, the server's default, is left out. */
  const showIn = (nextTab: RecommendationTab, nextSort: RecommendationSort) =>
    setSearchParams(nextSort === 'Score' ? { tab: nextTab } : { tab: nextTab, sort: nextSort }, { replace: true })

  const selectTab = (next: RecommendationTab) => {
    if (next !== tab) showIn(next, sort)
  }

  // The page goes back to 1 as the address changes (onView above).
  const changeSort = (next: RecommendationSort) => showIn(tab, next)

  const changeFilters = (next: RecommendationFilterValues) => setView({ tab, sort, filters: next, page: 1 })

  const goToPage = (next: number) => setView({ tab, sort, filters, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  const setHidden = (id: string, hidden: boolean) => {
    if (hidden) hidAny.current = true
    setHiddenIds((current) => {
      const next = new Set(current)
      if (hidden) next.add(id)
      else next.delete(id)
      return next
    })
  }

  return (
    <>
      <RecommendationRunPanel catalogConnected={data?.catalogConnected} />

      <RecommendationTabs tabs={tabs} current={tab} panelId={panelId} tabId={tabId} onSelect={selectTab} />

      <div id={panelId} role="tabpanel" aria-labelledby={tabId(tab)} className="recommendation-panel">
        {showFilters && (
          <RecommendationFilters
            facets={facets}
            showKind={mixesKinds}
            values={filters}
            onChange={changeFilters}
            sort={sort}
            onSort={changeSort}
          />
        )}

        {/* Outside the ternary below, so a failed refresh adds a line above the cards instead of
            replacing them. */}
        {error && data && !isPlaceholderData && (
          <Banner action={<RetryButton onRetry={() => list.refetch()} />}>
            {t('refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}

        {load.error ? (
          // A filter the server refuses, or a tab switched off meanwhile (400), is its lasting answer.
          <ErrorState
            title={t('loadFailed')}
            onRetry={retryCanHelp(load.error) ? load.retry : undefined}
            retrying={load.retrying}
          >
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="settings-muted">{t('common:loading')}</p>
        ) : data && data.totalCount === 0 && !filtered && !data.catalogConnected ? (
          <div className="settings-empty">
            <p>{isOwner ? t('notConnected.owner') : t('notConnected.member')}</p>
            {isOwner && (
              <Button asChild variant="outline" size="sm">
                <Link to="/settings/integrations">{t('notConnected.openIntegrations')}</Link>
              </Button>
            )}
          </div>
        ) : data && data.totalCount === 0 && !filtered ? (
          <div className="settings-empty">
            <p>{t('empty')}</p>
          </div>
        ) : data && data.totalCount === 0 ? (
          <div className="settings-empty">
            <p>{t('noMatch')}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => changeFilters(NO_FILTERS)}>
              {t('clearFilters')}
            </Button>
          </div>
        ) : data ? (
          <>
            <ul className="recommendation-grid">
              {data.items.map((item) => (
                <li key={item.id}>
                  <RecommendationCard
                    item={item}
                    hidden={hiddenIds.has(item.id)}
                    onHidden={() => setHidden(item.id, true)}
                    onRestored={() => setHidden(item.id, false)}
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
      </div>

      <p className="recommendation-attribution">{t('attribution')}</p>
    </>
  )
}
