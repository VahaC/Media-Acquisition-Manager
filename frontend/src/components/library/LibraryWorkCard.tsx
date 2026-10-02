import { useId, type ReactNode } from 'react'
import { Rows3, Table2, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { VersionRows } from '@/components/library/VersionRows'
import { VersionTable } from '@/components/library/VersionTable'
import { SegmentedControl } from '@/components/shared/SegmentedControl'
import { compareVersions } from '@/lib/library-versions'
import type { LibraryWork } from '@/lib/types'
import { useNarrowScreen } from '@/lib/use-narrow-screen'
import { useVersionsView } from '@/lib/use-versions-view'
import '@/styles/settings-list.css' // .settings-muted — the "nothing reported" line
import '@/styles/library.css' // .library-work*, .library-badge-warning

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
 * under the title (`evidence`). Its Details dialog lists the versions through VersionItem, with the
 * same file and fact lines.
 *
 * MAM-16 — the approved layout: the versions as rows (VersionRows) or, from two versions on, as a
 * table that compares them (VersionTable), switched under the badges. One choice for every card on
 * the Library and Search pages, kept in this browser (useVersionsView).
 */
export function LibraryWorkCard({ work, evidence, headingLevel = 2 }: LibraryWorkCardProps) {
  const { t } = useTranslation('library')
  const titleId = useId()
  const [view, setView] = useVersionsView()
  // Owner's choice: on a phone the versions are always rows, as a table of several leaves a few
  // characters a column at 375px. library.css hides the switch at the same width, but which view
  // renders is behaviour rather than looks, so the card asks the window too. The stored choice stays
  // for a wider screen.
  const narrow = useNarrowScreen()
  // ADR 0008 — a work holds the facts of one source only, so the first fact that names a source
  // names it; shown once on the card instead of on every version.
  const source = work.versions.find((version) => version.sourceName)?.sourceName
    ?? work.identifiers.find((identifier) => identifier.sourceName)?.sourceName
    ?? null
  const versions = work.versions.toSorted(compareVersions)
  const showOriginalTitle = work.originalTitle !== null && work.originalTitle !== work.title
  const hasBadges = work.identifiers.length > 0 || work.missingIdentifiers.length > 0
  // One version has nothing to compare it with, so no table and no switch.
  const comparable = versions.length > 1
  const Heading = headingLevel === 3 ? 'h3' : 'h2'

  return (
    <article className="ui-card library-work" aria-labelledby={titleId}>
      <div className="library-work-header">
        <div className="library-work-heading">
          <Heading id={titleId} className="ui-card-title library-work-title">{work.title ?? t('work.unknownTitle')}</Heading>
          <p className="library-work-meta">
            {showOriginalTitle && <span>{t('work.originalTitle', { title: work.originalTitle })}</span>}
            <span>{work.year ?? t('work.unknownYear')}</span>
            {source && <span>{t('work.from', { source })}</span>}
          </p>
          {evidence}
        </div>
        {(hasBadges || comparable) && (
          <div className="library-work-side">
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
            {comparable && (
              <div className="library-work-view">
                <SegmentedControl
                  label={t('versions.view.label')}
                  options={[
                    { value: 'rows', label: t('versions.view.rows'), title: t('versions.view.rowsTitle'), icon: Rows3 },
                    { value: 'table', label: t('versions.view.table'), title: t('versions.view.tableTitle'), icon: Table2 },
                  ]}
                  value={view}
                  onChange={setView}
                  showLabels
                />
              </div>
            )}
          </div>
        )}
      </div>
      {versions.length === 0 ? (
        <p className="settings-muted">{t('work.noVersions')}</p>
      ) : comparable && view === 'table' && !narrow ? (
        <VersionTable versions={versions} />
      ) : (
        <VersionRows versions={versions} />
      )}
    </article>
  )
}
