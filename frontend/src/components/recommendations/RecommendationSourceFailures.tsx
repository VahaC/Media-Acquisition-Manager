import { CircleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '@/lib/auth-store'
import { formatTimeAgo } from '@/lib/format'
import type { RecommendationSourceFailure } from '@/lib/types'
import '@/styles/library.css' // .library-source-status — the line and its icon, as the Search page lays out a failed source
import '@/styles/recommendations.css' // .recommendation-sources — the lines, one under the other

interface RecommendationSourceFailuresProps {
  failures: readonly RecommendationSourceFailure[]
  /** When the list was last asked for, what "12 minutes ago" is measured from. */
  now: number
}

/**
 * MAM-31 — the release sources whose last call for the lists failed, one line each, as the Search
 * page says a failed source: its name, when it was last asked, how the call went, and the owner's
 * sentence with what to do (the API sends it to the owner only); anyone else is told to ask the
 * owner. The live check gave Jackett a wrong key, and this page showed its old counts and "Not
 * checked yet" without a word. Nothing when every source answered.
 */
export function RecommendationSourceFailures({ failures, now }: RecommendationSourceFailuresProps) {
  const { t } = useTranslation(['recommendations', 'search', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  if (failures.length === 0) return null

  return (
    <ul className="recommendation-sources" aria-label={t('sources.label')}>
      {failures.map((failure) => {
        const fix = failure.error ?? (isOwner ? null : t('sources.askOwner', { name: failure.name }))
        return (
          <li key={failure.name} className="library-source-status" data-status="Failed">
            <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            <span>
              <strong>{failure.name}</strong>
              {t('common:separator')}
              {t('sources.lastAsked', { ago: formatTimeAgo(failure.failedUtc, now) })}{' '}
              {t(`search:sources.status.${failure.status}`)}
              {fix !== null && <>{' '}{fix}</>}
            </span>
          </li>
        )
      })}
    </ul>
  )
}
