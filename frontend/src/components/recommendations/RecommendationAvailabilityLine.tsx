import { CircleCheck, Clock, SearchX } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatDaysAgo, formatNumber } from '@/lib/format'
import type { RecommendationAvailability } from '@/lib/types'
import '@/styles/recommendations.css' // .recommendation-availability — the line, green when releases were found

/**
 * MAM-33 — what the release sources had for a work at their last check (ADR 0024), in one line:
 * "4 releases · up to 2160p · Ukr", "Not found (checked 3 days ago)", or "Not checked yet" before
 * the first check. A check is shared by every user and repeated within each user's daily budget.
 */
export function RecommendationAvailabilityLine({ availability }: { availability: RecommendationAvailability | null }) {
  const { t } = useTranslation(['recommendations', 'common'])

  if (availability === null) {
    return (
      <p className="recommendation-availability">
        <Clock className="h-3 w-3" aria-hidden="true" />
        <span>{t('availability.notChecked')}</span>
      </p>
    )
  }

  if (!availability.found) {
    return (
      <p className="recommendation-availability">
        <SearchX className="h-3 w-3" aria-hidden="true" />
        <span>{t('availability.notFound', { ago: formatDaysAgo(availability.checkedUtc) })}</span>
      </p>
    )
  }

  const { releaseCount, bestResolution, hasUkrainianAudio } = availability
  const parts = [
    t('availability.releases', { count: releaseCount, value: formatNumber(releaseCount) }),
    bestResolution !== null && t('availability.upTo', { value: bestResolution }),
  ].filter((part) => typeof part === 'string')

  return (
    <p className="recommendation-availability" data-found="">
      <CircleCheck className="h-3 w-3" aria-hidden="true" />
      <span>
        {parts.join(t('common:separator'))}
        {/* An abbreviation, named in full on hover; the line stays short enough for a phone. */}
        {hasUkrainianAudio && (
          <>
            {t('common:separator')}
            <abbr title={t('availability.ukrainianTitle')}>{t('availability.ukrainian')}</abbr>
          </>
        )}
      </span>
    </p>
  )
}
