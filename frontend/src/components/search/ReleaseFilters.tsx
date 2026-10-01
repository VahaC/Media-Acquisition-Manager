import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import { languageName } from '@/lib/media-labels'
import { bytesFromGibibytes } from '@/lib/size-input'
import type { SearchFacets, SearchReleaseSort, SearchSource } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-toolbar, .settings-filter, .settings-error — the row of filters and its hint
import '@/styles/search.css' // .search-filters, .search-size-* — the size pair and the hint under the row

/** What the release filters hold. `''` is "not filtered"; the sizes are the GiB text as typed. */
export interface ReleaseFilterValues {
  /** A search source's id. */
  source: string
  /** A three-letter code, or `unknown`. */
  language: string
  /** A resolution class such as `1080`, or `unknown`. */
  quality: string
  minSize: string
  maxSize: string
  sort: SearchReleaseSort
}

interface ReleaseFiltersProps {
  /** The search's sources; only those that listed something are offered. */
  sources: SearchSource[]
  /** What the stored releases claim, which is all the language and quality filters offer. */
  facets: SearchFacets
  values: ReleaseFilterValues
  onChange: (values: ReleaseFilterValues) => void
}

/** The orders the server sorts by, in the order the select offers them. */
const SORTS: readonly SearchReleaseSort[] = ['seeders', 'largest', 'smallest', 'newest', 'quality']

/**
 * MAM-12 — the filters and the sort of a search's releases. Each change goes to the server (the
 * page sends it as query params); nothing is filtered here. The language and quality filters offer
 * only what the stored releases claim, plus Unknown when a release claims none, so a choice never
 * comes back empty for want of a value no release has.
 *
 * Sizes are typed in GiB, the unit the rows show, and sent as bytes. A bound that isn't a size is
 * left out of the request and said so under the row, rather than turned into a byte count the
 * person never typed.
 */
export function ReleaseFilters({ sources, facets, values, onChange }: ReleaseFiltersProps) {
  const { t } = useTranslation('search')
  const sizeLabelId = useId()
  const sizeHintId = useId()
  const change = (changes: Partial<ReleaseFilterValues>) => onChange({ ...values, ...changes })
  const minInvalid = bytesFromGibibytes(values.minSize) === null
  const maxInvalid = bytesFromGibibytes(values.maxSize) === null

  return (
    <div className="search-filters">
      <div className="settings-toolbar">
        <select
          className="ui-input settings-filter"
          value={values.source}
          aria-label={t('filters.source')}
          onChange={(event) => change({ source: event.target.value })}
        >
          <option value="">{t('filters.allSources')}</option>
          {/* A source that listed nothing would only ever filter to an empty list. */}
          {sources.filter((source) => source.releaseCount > 0).map((source) => (
            <option key={source.id} value={source.id}>{source.name}</option>
          ))}
        </select>
        <select
          className="ui-input settings-filter"
          value={values.language}
          aria-label={t('filters.language')}
          onChange={(event) => change({ language: event.target.value })}
        >
          <option value="">{t('filters.allLanguages')}</option>
          {facets.audioLanguages.map((code) => <option key={code} value={code}>{languageName(code)}</option>)}
          {facets.hasUnknownAudioLanguage && <option value="unknown">{t('filters.unknownLanguage')}</option>}
        </select>
        <select
          className="ui-input settings-filter"
          value={values.quality}
          aria-label={t('filters.quality')}
          onChange={(event) => change({ quality: event.target.value })}
        >
          <option value="">{t('filters.allQualities')}</option>
          {facets.resolutions.map((resolution) => (
            <option key={resolution} value={String(resolution)}>{t('claimed.resolution', { value: resolution })}</option>
          ))}
          {facets.hasUnknownResolution && <option value="unknown">{t('filters.unknownQuality')}</option>}
        </select>
        <div className="search-size-filter" role="group" aria-labelledby={sizeLabelId}>
          <span id={sizeLabelId}>{t('filters.size')}</span>
          {/* Text with a decimal keypad rather than type="number": a number field reports what it
              can't read as empty, so a mistyped bound would vanish without a word. */}
          <Input
            className="search-size-input"
            inputMode="decimal"
            autoComplete="off"
            value={values.minSize}
            placeholder={t('filters.minSizePlaceholder')}
            aria-label={t('filters.minSize')}
            aria-invalid={minInvalid || undefined}
            aria-describedby={minInvalid ? sizeHintId : undefined}
            onChange={(event) => change({ minSize: event.target.value })}
          />
          <Input
            className="search-size-input"
            inputMode="decimal"
            autoComplete="off"
            value={values.maxSize}
            placeholder={t('filters.maxSizePlaceholder')}
            aria-label={t('filters.maxSize')}
            aria-invalid={maxInvalid || undefined}
            aria-describedby={maxInvalid ? sizeHintId : undefined}
            onChange={(event) => change({ maxSize: event.target.value })}
          />
        </div>
        <select
          className="ui-input settings-filter"
          value={values.sort}
          aria-label={t('filters.sort')}
          onChange={(event) => change({ sort: event.target.value as SearchReleaseSort })}
        >
          {SORTS.map((sort) => <option key={sort} value={sort}>{t(`filters.sorts.${sort}`)}</option>)}
        </select>
      </div>
      {(minInvalid || maxInvalid) && (
        <p id={sizeHintId} className="settings-error" role="alert">{t('filters.sizeInvalid')}</p>
      )}
    </div>
  )
}
