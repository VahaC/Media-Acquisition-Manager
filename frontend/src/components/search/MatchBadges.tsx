import { CircleCheck, CircleHelp, Library, TextSearch, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatNumber } from '@/lib/format'
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
  /**
   * The work's title that matched, with its source; none when only an id matched. For `Words`, the
   * title holding the words, none when only a file path holds them.
   */
  matchedTitle?: LibraryTitle | null
  /** MAM-12 — the title searched for, as the search keeps it: what a `Words` badge quotes. */
  searchText?: string
  /**
   * The matched work's name and year, on a release row: the badge then says the release is in the
   * library and which copy it is ("In your library: Чужі (1986) — matched by id"). A release's
   * evidence, so never `Words`.
   */
  inLibraryOf?: string
  /**
   * MAM-32 — on a TV release's match, how many episodes of the series the library holds among what
   * the release claims; null or left out when there is no count to show.
   */
  heldEpisodeCount?: number | null
}

/**
 * MAM-12 — why a library work matched what was sought, as badges: the evidence (an id, the title and
 * year, or the title alone — a possible match to check), the matched title with the integration that
 * reported it on hover (as the card's identifier badges do), and a warning per scheme whose ids
 * disagree. A conflict is shown, never resolved: the page picks and merges nothing.
 *
 * A work listed by words (ADR 0012) is no match, and its badge says so in its text — a phone shows
 * no tooltip — in the outline tone of a plain fact rather than the tick of a match or the amber of
 * one to check: the title searched for is in one of its titles, or in a file path only. Its list is
 * named "Why it is listed", so a screen reader doesn't call a work that isn't a match a match.
 *
 * MAM-32 — a TV release's match also says how many episodes of what it claims the library already
 * holds ("9 of its episodes in your library"), a plain fact in the outline tone: a season pack of
 * episodes all held is likely a second copy, one of none a gap to fill.
 */
export function MatchBadges({ evidence, conflictingSchemes, matchedIdentifiers = [], matchedTitle = null, searchText = '', inLibraryOf, heldEpisodeCount = null }: MatchBadgesProps) {
  const { t } = useTranslation(['search', 'library'])
  const scheme = (value: MediaIdentifierScheme) => t(`library:schemes.${value}`)

  const evidenceBadges = (() => {
    // A release is never matched by words (ADR 0012), so `match.inLibrary` has no `Words` sentence;
    // the second check only tells the typed key so.
    if (inLibraryOf !== undefined && evidence !== 'Words') {
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
    if (evidence === 'Words') {
      const text = matchedTitle
        ? t('match.words', { text: searchText })
        : t('match.wordsInPath', { text: searchText })
      return [{ key: evidence, text, title: t('match.wordsHint') }]
    }
    const text = evidence === 'Identifier'
      ? t('match.identifierAny')
      : evidence === 'TitleAndYear' ? t('match.titleAndYear') : t('match.titleOnly')
    return [{ key: evidence, text, title: undefined }]
  })()

  const titleKind = matchedTitle ? t(`match.titleKind.${matchedTitle.kind}`) : undefined

  return (
    <ul className="search-badges" aria-label={evidence === 'Words' ? t('match.wordsLabel') : t('match.label')}>
      {evidenceBadges.map((badge) => (
        <li key={badge.key}>
          {/* A title-only match is a candidate to check, so it takes the warning tone; a work listed
              by words is no match at all, so it takes neither a match's tone nor its tick. */}
          <span
            className={evidence === 'TitleOnly'
              ? 'ui-badge library-badge-warning'
              : evidence === 'Words' ? 'ui-badge ui-badge-outline' : 'ui-badge ui-badge-secondary'}
            title={badge.title}
          >
            {evidence === 'TitleOnly'
              ? <CircleHelp className="h-3 w-3" aria-hidden="true" />
              : evidence === 'Words'
                ? <TextSearch className="h-3 w-3" aria-hidden="true" />
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
      {heldEpisodeCount !== null && (
        <li>
          <span className="ui-badge ui-badge-outline" title={t('match.heldHint')}>
            <Library className="h-3 w-3" aria-hidden="true" />
            {heldEpisodeCount === 0
              ? t('match.heldNone')
              : t('match.held', { count: heldEpisodeCount, value: formatNumber(heldEpisodeCount) })}
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
