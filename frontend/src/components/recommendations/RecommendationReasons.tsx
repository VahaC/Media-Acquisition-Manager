import { Clapperboard, Layers, ListPlus, PenLine, Rss, Star, ThumbsUp, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { formatDaysAgo, formatNumber } from '@/lib/format'
import { seasonRanges } from '@/lib/recommendations'
import type { RecommendationReason, RecommendationReasonKind } from '@/lib/types'
import '@/styles/recommendations.css' // .recommendation-reasons, .recommendation-reason — the chips

const REASON_ICONS: Record<RecommendationReasonKind, LucideIcon> = {
  Collection: Layers,
  Director: Clapperboard,
  Creator: PenLine,
  Recommended: ThumbsUp,
  MissingSeasons: ListPlus,
  TopRated: Star,
  NewAtSource: Rss,
}

/**
 * MAM-33 — why a work is recommended (ADR 0023: a recommendation explains every reason), a chip per
 * reason in the server's order: "Guardians of the Galaxy · 2 of 3", "Ridley Scott · 12 in your
 * library", "Because you have Alien, Prometheus and 3 more", "Missing seasons 4–5", "Top rated",
 * "New on Toloka.to · 2 days ago". Each chip's title says what kind of reason it is, which the short
 * text leaves to the icon.
 */
export function RecommendationReasons({ reasons }: { reasons: RecommendationReason[] }) {
  const { t } = useTranslation('recommendations')

  if (reasons.length === 0) return null

  return (
    <ul className="recommendation-reasons" aria-label={t('reasons.label')}>
      {reasons.map((reason, index) => {
        const Icon = REASON_ICONS[reason.kind]
        return (
          // A card can hold two reasons of a kind (two collections it belongs to), so the index keys them.
          <li key={`${reason.kind}:${index}`} className="recommendation-reason" title={t(`reasons.titles.${reason.kind}`)}>
            <Icon className="h-3 w-3" aria-hidden="true" />
            <span>{reasonText(reason, t)}</span>
          </li>
        )
      })}
    </ul>
  )
}

/** A reason's chip text; a reason whose facts are missing falls back to its kind's name. */
function reasonText(reason: RecommendationReason, t: TFunction<'recommendations'>): string {
  const kindName = t(`reasonKinds.${reason.kind}`)
  switch (reason.kind) {
    case 'Collection':
      if (reason.name === null) return kindName
      return reason.held === null || reason.total === null
        ? reason.name
        : t('reasons.collection', { name: reason.name, held: formatNumber(reason.held), total: formatNumber(reason.total) })
    case 'Director':
    case 'Creator':
      if (reason.name === null) return kindName
      return reason.held === null ? reason.name : t('reasons.person', { name: reason.name, held: formatNumber(reason.held) })
    case 'Recommended':
      return recommendedText(reason, t)
    case 'MissingSeasons':
      return reason.seasons.length === 0
        ? kindName
        : t('reasons.missingSeasons', { count: reason.seasons.length, seasons: seasonRanges(reason.seasons) })
    case 'TopRated':
      return t('reasons.topRated')
    case 'NewAtSource':
      // The source's own name, never a fixed one: any release source's feed can bring a work.
      if (reason.name === null) return t('reasons.newAtSourceUnnamed')
      return reason.dateUtc === null
        ? t('reasons.newAtSourceUndated', { name: reason.name })
        : t('reasons.newAtSource', { name: reason.name, ago: formatDaysAgo(reason.dateUtc) })
  }
}

/**
 * "Because you have Alien", "… Alien and Prometheus", "… Alien, Prometheus and Aliens", and from four
 * on two titles and the count of the rest: "… Alien, Prometheus and 3 more". The server keeps at most
 * five titles and counts every recommending work in `held`.
 */
function recommendedText(reason: RecommendationReason, t: TFunction<'recommendations'>): string {
  const { titles } = reason
  const total = Math.max(reason.held ?? 0, titles.length)
  const [first, second, third] = titles
  if (first === undefined) return t('reasons.recommendedCount', { count: total, value: formatNumber(total) })
  if (total === 1) return t('reasons.recommendedOne', { first })
  if (second === undefined) return t('reasons.recommendedOneMore', { first, more: formatNumber(total - 1) })
  if (total === 2) return t('reasons.recommendedTwo', { first, second })
  if (total === 3 && third !== undefined) return t('reasons.recommendedThree', { first, second, third })
  return t('reasons.recommendedMore', { first, second, more: formatNumber(total - 2) })
}
