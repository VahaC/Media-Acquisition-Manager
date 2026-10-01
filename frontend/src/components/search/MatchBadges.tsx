import { CircleCheck, CircleHelp, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { LibraryIdentifier, LibraryTitle, MatchEvidence, MediaIdentifierScheme } from '@/lib/types'
import '@/styles/library.css' // .library-badge-warning — the amber of a possible match and of a conflict
import '@/styles/search.css' // .search-badges — the badge row and its icons

interface MatchBadgesProps {
  evidence: MatchEvidence
  /** Every scheme whose ids disagree; one warning badge each. */
  conflictingSchemes: MediaIdentifierScheme[]
  /**
   * The work's ids that matched, each with the integration that reported it. Without them (a
   * release's match, whose ids aren't listed) an id match is named without its scheme.
   */
  matchedIdentifiers?: LibraryIdentifier[]
  /** The work's title that matched, with its source; none when only an id matched. */
  matchedTitle?: LibraryTitle | null
  /**
   * The matched work's name and year, on a release row: the badge then says the release is in the
   * library and which copy it is ("In your library: Чужі (1986) — matched by id").
   */
  inLibraryOf?: string
}

/**
 * MAM-12 — why a library work matched what was sought, as badges: the evidence (an id, the title and
 * year, or the title alone — a possible match to check), the matched title with the integration that
 * reported it on hover (as the card's identifier badges do), and a warning per scheme whose ids
 * disagree. A conflict is shown, never resolved: the page picks and merges nothing.
 */
export function MatchBadges({ evidence, conflictingSchemes, matchedIdentifiers = [], matchedTitle = null, inLibraryOf }: MatchBadgesProps) {
  const { t } = useTranslation(['search', 'library'])
  const scheme = (value: MediaIdentifierScheme) => t(`library:schemes.${value}`)

  const evidenceBadges = (() => {
    if (inLibraryOf !== undefined) {
      // The copy is named in the text, as a phone shows no tooltip. No hover either: a release's match
      // carries no matched title whose source it could add, and repeating the text adds nothing.
      return [{ key: evidence, text: t(`match.inLibrary.${evidence}`, { work: inLibraryOf }), title: undefined }]
    }
    if (evidence === 'Identifier' && matchedIdentifiers.length > 0) {
      return matchedIdentifiers.map((identifier) => ({
        key: `${identifier.scheme}:${identifier.value}`,
        text: t('match.identifier', { scheme: scheme(identifier.scheme) }),
        title: identifier.sourceName
          ? t('match.identifierSource', { scheme: scheme(identifier.scheme), value: identifier.value, source: identifier.sourceName })
          : t('match.identifierValue', { scheme: scheme(identifier.scheme), value: identifier.value }),
      }))
    }
    const text = evidence === 'Identifier'
      ? t('match.identifierAny')
      : evidence === 'TitleAndYear' ? t('match.titleAndYear') : t('match.titleOnly')
    return [{ key: evidence, text, title: undefined }]
  })()

  const titleKind = matchedTitle ? t(`match.titleKind.${matchedTitle.kind}`) : undefined

  return (
    <ul className="search-badges" aria-label={t('match.label')}>
      {evidenceBadges.map((badge) => (
        <li key={badge.key}>
          {/* A title-only match is a candidate to check, so it takes the warning tone. */}
          <span
            className={evidence === 'TitleOnly' ? 'ui-badge library-badge-warning' : 'ui-badge ui-badge-secondary'}
            title={badge.title}
          >
            {evidence === 'TitleOnly'
              ? <CircleHelp className="h-3 w-3" aria-hidden="true" />
              : <CircleCheck className="h-3 w-3" aria-hidden="true" />}
            {badge.text}
          </span>
        </li>
      ))}
      {matchedTitle && (
        <li>
          <span
            className="ui-badge ui-badge-outline"
            title={matchedTitle.sourceName ? t('match.titleSource', { kind: titleKind, source: matchedTitle.sourceName }) : titleKind}
          >
            {matchedTitle.year === null
              ? t('match.matchedTitleNoYear', { title: matchedTitle.text })
              : t('match.matchedTitle', { title: matchedTitle.text, year: matchedTitle.year })}
          </span>
        </li>
      )}
      {conflictingSchemes.map((conflict) => (
        <li key={conflict}>
          <span className="ui-badge library-badge-warning" title={t('match.conflictHint')}>
            <TriangleAlert className="h-3 w-3" aria-hidden="true" />
            {t('match.conflict', { scheme: scheme(conflict) })}
          </span>
        </li>
      ))}
    </ul>
  )
}
