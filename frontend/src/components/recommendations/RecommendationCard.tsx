import { useId, useState } from 'react'
import { Download, EyeOff, Loader2, Search, Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { RecommendationAvailabilityLine } from '@/components/recommendations/RecommendationAvailabilityLine'
import { RecommendationReasons } from '@/components/recommendations/RecommendationReasons'
import { CatalogPoster } from '@/components/shared/CatalogPoster'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { Button } from '@/components/ui/button'
import { formatNumber } from '@/lib/format'
import { useDismissRecommendation, useUndismissRecommendation } from '@/lib/queries'
import type { Recommendation } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/recommendations.css' // .recommendation-* — the card, its head, meta, actions and the hidden row

interface RecommendationCardProps {
  item: Recommendation
  /** Whether "Not interested" hid it: the card is then a slim row with Undo. The page keeps it, so it lasts across tabs. */
  hidden: boolean
  /** Its "Not interested" went through. */
  onHidden: () => void
  /** Its Undo went through. */
  onRestored: () => void
}

/**
 * MAM-33 — one recommended work: the poster beside its title (the catalog's Ukrainian one, the
 * original under it when it differs), year, genres and rating, why it is recommended, what the release
 * sources had at their last check, and a Downloading badge while a download job someone follows
 * matches it. "Releases" opens the Search page searched for it — with its TMDb id when it is a film,
 * which narrows the search to it. "Not interested" hides it for this user only.
 *
 * Once hidden, the card stays in its place as one line with Undo, rather than the grid closing up
 * under the pointer; the page asks for its lists again when the person leaves (RecommendationsPage).
 * A refused request says why inside the card or the line.
 */
export function RecommendationCard({ item, hidden, onHidden, onRestored }: RecommendationCardProps) {
  const { t } = useTranslation(['recommendations', 'common'])
  const titleId = useId()
  const hiddenId = useId()
  const dismiss = useDismissRecommendation()
  const undismiss = useUndismissRecommendation()
  const [error, setError] = useState<string | null>(null)
  // The button the person pressed goes away with the card or the line, so the focus moves to the one
  // that takes its place. Not on the first render: a card that simply loads takes no focus.
  const [focusAction, setFocusAction] = useState(false)
  const target = { kind: item.kind, catalogId: item.catalogId }

  const hide = async () => {
    setError(null)
    try {
      await dismiss.mutateAsync(target)
      setFocusAction(true)
      onHidden()
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('card.dismissFailed')))
    }
  }

  const undo = async () => {
    setError(null)
    try {
      await undismiss.mutateAsync(target)
      setFocusAction(true)
      onRestored()
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('card.undoFailed')))
    }
  }

  if (hidden) {
    return (
      <div className="recommendation-hidden">
        <span id={hiddenId}>{t('card.hidden', { title: item.title })}</span>
        {/* Described by the line, so the focus landing on it after Not interested reads which work. */}
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-describedby={hiddenId}
          autoFocus={focusAction}
          disabled={undismiss.isPending}
          onClick={() => void undo()}
        >
          {undismiss.isPending
            ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            : <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />}
          {undismiss.isPending ? t('card.undoing') : t('card.undo')}
        </Button>
        {error && <FieldErrorLine>{error}</FieldErrorLine>}
      </div>
    )
  }

  const showOriginalTitle = item.originalTitle !== null && item.originalTitle !== item.title
  // Encoded, so a `&` or a `#` in a title stays part of it. The Search page makes the search from
  // `q` (a bracketed year ending it is the year) and narrows a film by its TMDb id.
  const releasesUrl = `/search?q=${encodeURIComponent(item.searchText)}`
    + (item.searchTmdbId === null ? '' : `&tmdbId=${encodeURIComponent(item.searchTmdbId)}`)

  return (
    <article className="ui-card recommendation-card" aria-labelledby={titleId}>
      <CatalogPoster url={item.posterUrl} title={item.title} kind={item.kind} />
      <div className="recommendation-body">
        <div className="recommendation-head">
          <h2 id={titleId} className="recommendation-title">{item.title}</h2>
          {item.downloading && (
            <span className="ui-badge ui-badge-secondary">
              <Download className="h-3 w-3" aria-hidden="true" />
              {t('card.downloading')}
            </span>
          )}
        </div>
        <p className="recommendation-meta">
          {showOriginalTitle && <span>{t('card.originalTitle', { title: item.originalTitle })}</span>}
          {item.year !== null && <span>{item.year}</span>}
          {item.genres.length > 0 && <span>{item.genres.join(t('card.genreSeparator'))}</span>}
          {item.rating !== null && (
            <span>
              {t('card.rating', {
                count: item.voteCount,
                rating: formatNumber(item.rating, { minimumFractionDigits: 1, maximumFractionDigits: 1 }),
                votes: formatNumber(item.voteCount),
              })}
            </span>
          )}
        </p>
        <RecommendationReasons reasons={item.reasons} />
        <RecommendationAvailabilityLine availability={item.availability} />
        <div className="recommendation-actions" role="group" aria-label={t('card.actions')}>
          <Button asChild variant="outline" size="sm">
            <Link to={releasesUrl} title={t('card.releases')}>
              <Search className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="label-text">{t('card.releases')}</span>
            </Link>
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            title={t('card.notInterested')}
            autoFocus={focusAction}
            disabled={dismiss.isPending}
            onClick={() => void hide()}
          >
            {dismiss.isPending
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className="label-text">{dismiss.isPending ? t('card.hiding') : t('card.notInterested')}</span>
          </Button>
        </div>
        {error && <FieldErrorLine>{error}</FieldErrorLine>}
      </div>
    </article>
  )
}
