import { useId, type ReactNode } from 'react'
import { TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { VersionItem } from '@/components/library/VersionItem'
import { compareVersions } from '@/lib/library-versions'
import type { LibraryWork } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — the "nothing reported" lines
import '@/styles/library.css' // .library-work-*, .library-versions, .library-badge-warning

interface LibraryWorkCardProps {
  work: LibraryWork
  /** MAM-12 — what the search page says about why the work matched, under the title and year. */
  evidence?: ReactNode
  /**
   * MAM-12 — the title's heading level: 2 on the Library page, 3 under a section's own h2 (the
   * search page's "In your library"), so the page's outline stays one level per step.
   */
  headingLevel?: 2 | 3
}

/**
 * MAM-8 — one work of the Library page: its title, original title and year, the identifiers it is
 * matched by (and a warning badge per missing one), and every version with its files. A value the
 * library didn't report says so ("Year unknown", "Size unknown") rather than leaving a gap.
 *
 * A file shows its name as the line and its folder under it: the name is what tells two versions
 * apart, and a long path would otherwise bury it. The whole path is in the name's tooltip.
 *
 * MAM-9 — under the library's facts come the ones the app read from the file itself with ffprobe:
 * length, picture, bit rate, and the audio and subtitle tracks, or why they couldn't be read.
 *
 * MAM-12 — the search page shows its library matches as these same cards, with why each matched
 * under the title (`evidence`); its versions, files and facts live in VersionItem and below, which
 * its Details dialog reuses.
 */
export function LibraryWorkCard({ work, evidence, headingLevel = 2 }: LibraryWorkCardProps) {
  const { t } = useTranslation('library')
  const titleId = useId()
  // ADR 0008 — a work holds the facts of one source only, so the first fact that names a source
  // names it; shown once on the card instead of on every version.
  const source = work.versions.find((version) => version.sourceName)?.sourceName
    ?? work.identifiers.find((identifier) => identifier.sourceName)?.sourceName
    ?? null
  const versions = work.versions.toSorted(compareVersions)
  const showOriginalTitle = work.originalTitle !== null && work.originalTitle !== work.title
  const hasBadges = work.identifiers.length > 0 || work.missingIdentifiers.length > 0
  const Heading = headingLevel === 3 ? 'h3' : 'h2'

  return (
    <article className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header library-work-header">
        <div className="library-work-heading">
          <Heading id={titleId} className="ui-card-title library-work-title">{work.title ?? t('work.unknownTitle')}</Heading>
          <p className="library-work-meta">
            {showOriginalTitle && <span>{t('work.originalTitle', { title: work.originalTitle })}</span>}
            <span>{work.year ?? t('work.unknownYear')}</span>
            {source && <span>{t('work.from', { source })}</span>}
          </p>
          {evidence}
        </div>
        {hasBadges && (
          <ul className="library-work-ids" aria-label={t('work.identifiers')}>
            {work.identifiers.map((identifier) => (
              <li key={`${identifier.scheme}:${identifier.value}`}>
                <span
                  className="ui-badge ui-badge-outline"
                  title={identifier.sourceName ? t('work.identifierSource', { source: identifier.sourceName }) : undefined}
                >
                  {t('work.identifier', { scheme: t(`schemes.${identifier.scheme}`), value: identifier.value })}
                </span>
              </li>
            ))}
            {work.missingIdentifiers.map((scheme) => (
              <li key={scheme}>
                <span className="ui-badge library-badge-warning">
                  <TriangleAlert className="h-3 w-3" aria-hidden="true" />
                  {t('work.missingIdentifier', { scheme: t(`schemes.${scheme}`) })}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className="ui-card-content">
        {versions.length === 0 ? (
          <p className="settings-muted">{t('work.noVersions')}</p>
        ) : (
          <ul className="library-versions" aria-label={t('work.versions')}>
            {versions.map((version) => <VersionItem key={version.id} version={version} />)}
          </ul>
        )}
      </div>
    </article>
  )
}
