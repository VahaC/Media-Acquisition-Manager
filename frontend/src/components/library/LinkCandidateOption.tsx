import { BadgeCheck } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LinkEvidenceBadges } from '@/components/library/LinkEvidenceBadges'
import { CatalogPoster } from '@/components/shared/CatalogPoster'
import { formatNumber } from '@/lib/format'
import type { LinkCandidate } from '@/lib/types'
import '@/styles/library.css' // .library-work-meta — the muted line of facts under a title
import '@/styles/library-links.css' // .tmdb-link-candidate* — the poster beside the facts, the picked frame

interface LinkCandidateOptionProps {
  candidate: LinkCandidate
  /** The radio group it belongs to: the suggestions, the lookup's answer or a search's results. */
  name: string
  checked: boolean
  onPick: (candidate: LinkCandidate) => void
  /** The form the radio belongs to when it sits outside it (a search's results sit in the search form). */
  form?: string
  /** MAM-34 — the key that picks it in the TMDb page's Review tab ("1" to "5"), shown beside its title. */
  shortcut?: string
}

/**
 * MAM-34 — one TMDb film or show to pick for a library work: its poster, title and year, kind, TMDb
 * and IMDb ids, runtime and what it is about, and the evidence for and against it. The whole card is
 * the radio's label, so a tap anywhere on a phone picks it; the radio is named by the title alone, as
 * the card's text would make a sentence of a name. The suggestions, a pasted id's answer and a search's
 * results all show their candidates this way.
 */
export function LinkCandidateOption({ candidate, name, checked, onPick, form, shortcut }: LinkCandidateOptionProps) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const heading = candidate.year === null
    ? candidate.title
    : t('candidate.titleYear', { title: candidate.title, year: candidate.year })
  const showOriginalTitle = candidate.originalTitle !== null && candidate.originalTitle !== candidate.title

  return (
    <label className="tmdb-link-candidate" data-checked={checked ? '' : undefined}>
      <input
        type="radio"
        className="tmdb-link-candidate-radio"
        name={name}
        form={form}
        checked={checked}
        onChange={() => onPick(candidate)}
        aria-label={t('candidate.pick', { title: heading })}
        aria-keyshortcuts={shortcut}
      />
      <CatalogPoster url={candidate.posterUrl} title={candidate.title} kind={candidate.catalogKind} />
      <span className="tmdb-link-candidate-body">
        <span className="tmdb-link-candidate-title">
          {heading}
          {/* aria-hidden: aria-keyshortcuts on the radio tells a screen reader about the key. */}
          {shortcut !== undefined && <>{' '}<kbd className="tmdb-link-key" aria-hidden="true">{shortcut}</kbd></>}
        </span>
        <span className="library-work-meta">
          <span>{t(`candidate.kind.${candidate.catalogKind}`)}</span>
          {showOriginalTitle && <span>{candidate.originalTitle}</span>}
          {candidate.year === null && <span>{t('candidate.unknownYear')}</span>}
          {candidate.runtimeMinutes !== null && (
            <span>{t('candidate.minutes', { count: candidate.runtimeMinutes, value: formatNumber(candidate.runtimeMinutes) })}</span>
          )}
          <span>{t('candidate.id', { id: candidate.catalogId })}</span>
          {candidate.imdbId !== null && <span>{candidate.imdbId}</span>}
        </span>
        {candidate.isConfident && (
          <span className="ui-badge ui-badge-secondary tmdb-link-confident" title={t('candidate.confidentHint')}>
            <BadgeCheck className="h-3 w-3" aria-hidden="true" />
            {t('candidate.confident')}
          </span>
        )}
        {candidate.overview !== null && <span className="tmdb-link-candidate-overview">{candidate.overview}</span>}
        <LinkEvidenceBadges evidence={candidate.evidence} catalogKind={candidate.catalogKind} />
      </span>
    </label>
  )
}
