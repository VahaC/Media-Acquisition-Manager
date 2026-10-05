import { useTranslation } from 'react-i18next'
import { formatNumber } from '@/lib/format'
import { RECOMMENDATION_SORTS } from '@/lib/recommendations'
import type { CatalogWorkKind, RecommendationFacets, RecommendationReasonKind, RecommendationSort } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-toolbar, .settings-filter — the row of filters
import '@/styles/settings-dialog.css' // .settings-check — the Available only box before its label

/** What the toolbar holds. `''` is "not filtered"; the genre and the decade as their option values. */
export interface RecommendationFilterValues {
  reason: RecommendationReasonKind | ''
  /** A catalog genre id. */
  genreId: string
  /** The first year of a decade, "1990". */
  decade: string
  kind: CatalogWorkKind | ''
  /** Keeps only the works a release source carries. */
  available: boolean
}

interface RecommendationFiltersProps {
  /** What the tab's works offer; each option carries its count. */
  facets: RecommendationFacets
  /** Cartoons and Documentaries hold films and shows alike; only they offer the films/series select. */
  showKind: boolean
  values: RecommendationFilterValues
  onChange: (values: RecommendationFilterValues) => void
  /** The order of the works; not a filter, so Clear filters leaves it. */
  sort: RecommendationSort
  onSort: (sort: RecommendationSort) => void
}

/**
 * MAM-33 — the filters of one tab: the reason, the genre, the decade, films or series (Cartoons and
 * Documentaries), and Available only; then the order of the works. Each change goes to the server as a
 * query param (the page sends it); nothing is filtered or sorted here. The filter selects offer only
 * what the tab's works have, each with how many.
 */
export function RecommendationFilters({ facets, showKind, values, onChange, sort, onSort }: RecommendationFiltersProps) {
  const { t } = useTranslation('recommendations')
  const change = (changes: Partial<RecommendationFilterValues>) => onChange({ ...values, ...changes })
  const option = (label: string, count: number) => t('filters.option', { label, value: formatNumber(count) })

  return (
    <div className="settings-toolbar">
      <select
        className="ui-input settings-filter"
        value={values.reason}
        aria-label={t('filters.reason')}
        onChange={(event) => change({ reason: event.target.value as RecommendationReasonKind | '' })}
      >
        <option value="">{t('filters.allReasons')}</option>
        {facets.reasons.map(({ kind, count }) => (
          <option key={kind} value={kind}>{option(t(`reasonKinds.${kind}`), count)}</option>
        ))}
      </select>
      <select
        className="ui-input settings-filter"
        value={values.genreId}
        aria-label={t('filters.genre')}
        onChange={(event) => change({ genreId: event.target.value })}
      >
        <option value="">{t('filters.allGenres')}</option>
        {facets.genres.map(({ id, name, count }) => (
          <option key={id} value={String(id)}>{option(name, count)}</option>
        ))}
      </select>
      <select
        className="ui-input settings-filter"
        value={values.decade}
        aria-label={t('filters.decade')}
        onChange={(event) => change({ decade: event.target.value })}
      >
        <option value="">{t('filters.allDecades')}</option>
        {facets.decades.map(({ decade, count }) => (
          <option key={decade} value={String(decade)}>{option(t('filters.decadeName', { decade }), count)}</option>
        ))}
      </select>
      {showKind && (
        <select
          className="ui-input settings-filter"
          value={values.kind}
          aria-label={t('filters.kind')}
          onChange={(event) => change({ kind: event.target.value as CatalogWorkKind | '' })}
        >
          <option value="">{t('filters.allKinds')}</option>
          {facets.kinds.map(({ kind, count }) => (
            <option key={kind} value={kind}>{option(t(`filters.kinds.${kind}`), count)}</option>
          ))}
        </select>
      )}
      <label className="settings-check">
        <input
          type="checkbox"
          checked={values.available}
          onChange={(event) => change({ available: event.target.checked })}
        />
        {t('filters.availableOnly')}
      </label>
      {/* Last, after the filters: it orders what they keep. */}
      <select
        className="ui-input settings-filter"
        value={sort}
        aria-label={t('filters.sort')}
        onChange={(event) => onSort(event.target.value as RecommendationSort)}
      >
        {RECOMMENDATION_SORTS.map((option) => (
          <option key={option} value={option}>{t(`filters.sorts.${option}`)}</option>
        ))}
      </select>
    </div>
  )
}
