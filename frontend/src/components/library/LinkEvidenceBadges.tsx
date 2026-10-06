import { CircleCheck, CircleHelp, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatNumber } from '@/lib/format'
import type { CatalogWorkKind, LinkEvidence } from '@/lib/types'
import '@/styles/library.css' // .library-badge-warning — the amber of evidence that speaks against it
import '@/styles/search.css' // .search-badges — the badge row and its icons

/** How a piece of evidence speaks: for the suggestion, against it, or not at all (unknown). */
type Tone = 'agrees' | 'disagrees' | 'unknown'

interface Chip {
  key: string
  tone: Tone
  text: string
  title?: string
}

const TONE_CLASSES: Record<Tone, string> = {
  agrees: 'ui-badge ui-badge-outline',
  disagrees: 'ui-badge library-badge-warning',
  unknown: 'ui-badge ui-badge-outline',
}

/**
 * MAM-34 — what speaks for and against a TMDb suggestion, as badges: the title (the same, or how
 * alike), the year (the same, or how many years apart), the runtime against the work's measured length
 * ("6 min ≈ 6 min"), the kind (a show suggested for a film links the film as its episode), the genre
 * against what the work's library mostly holds, and a linked neighbour it looks related to. A tick for
 * what agrees, the amber of a possible match for what disagrees, a question mark for what isn't known —
 * the tones of the Search page's match badges. The server computed every piece; nothing is judged here.
 */
export function LinkEvidenceBadges({ evidence, catalogKind }: { evidence: LinkEvidence; catalogKind: CatalogWorkKind }) {
  const { t } = useTranslation('libraryLinks')
  const minutes = (value: number) => t('candidate.minutes', { count: value, value: formatNumber(value) })
  const { title, year, runtime, genre, sibling } = evidence

  const chips: Chip[] = [
    {
      key: 'title',
      tone: title.exact ? 'agrees' : 'unknown',
      text: title.exact
        ? t('evidence.titleExact', { title: title.matchedTitle })
        : t('evidence.titleSimilar', { percent: formatNumber(Math.round(title.similarity * 100)), title: title.matchedTitle }),
      title: t('evidence.titleHint', { kind: t(`evidence.titleKind.${title.matchedKind}`) }),
    },
    year.difference === null
      ? { key: 'year', tone: 'unknown', text: t('evidence.yearUnknown') }
      : year.difference === 0
        ? { key: 'year', tone: 'agrees', text: t('evidence.yearSame') }
        : {
          key: 'year',
          // A year either way is the tolerance of every title-and-year match here (ADR 0012).
          tone: year.difference === 1 ? 'unknown' : 'disagrees',
          text: t('evidence.yearApart', { count: year.difference, value: formatNumber(year.difference) }),
        },
    runtime.agrees === null || runtime.libraryMinutes === null || runtime.catalogMinutes === null
      ? { key: 'runtime', tone: 'unknown', text: t('evidence.runtimeUnknown'), title: t('evidence.runtimeHint') }
      : {
        key: 'runtime',
        tone: runtime.agrees ? 'agrees' : 'disagrees',
        text: t(runtime.agrees ? 'evidence.runtimeAgrees' : 'evidence.runtimeDisagrees', {
          library: minutes(runtime.libraryMinutes),
          catalog: minutes(runtime.catalogMinutes),
        }),
        title: t('evidence.runtimeHint'),
      },
    evidence.kindAgrees
      ? { key: 'kind', tone: 'agrees', text: t(`evidence.kindAgrees.${catalogKind}`) }
      : { key: 'kind', tone: 'disagrees', text: t(`evidence.kindDisagrees.${catalogKind}`) },
  ]
  // A library without a genre of its own says nothing for or against, so no badge.
  if (genre.libraryRole !== null && genre.agreement !== 'Unknown') {
    chips.push(genre.agreement === 'Agrees'
      ? { key: 'genre', tone: 'agrees', text: t(`evidence.genreAgrees.${genre.libraryRole}`) }
      : { key: 'genre', tone: 'disagrees', text: t(`evidence.genreDisagrees.${genre.libraryRole}`) })
  }
  if (sibling !== null) {
    chips.push({ key: 'sibling', tone: 'agrees', text: t(`evidence.sibling.${sibling.reason}`, { title: sibling.siblingTitle }) })
  }

  return (
    <ul className="search-badges" aria-label={t('candidate.evidence')}>
      {chips.map((chip) => (
        <li key={chip.key}>
          <span className={TONE_CLASSES[chip.tone]} title={chip.title}>
            {chip.tone === 'agrees'
              ? <CircleCheck className="h-3 w-3" aria-hidden="true" />
              : chip.tone === 'disagrees'
                ? <TriangleAlert className="h-3 w-3" aria-hidden="true" />
                : <CircleHelp className="h-3 w-3" aria-hidden="true" />}
            {chip.text}
          </span>
        </li>
      ))}
    </ul>
  )
}
