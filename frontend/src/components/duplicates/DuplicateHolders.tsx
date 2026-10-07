import { useTranslation } from 'react-i18next'
import { episodePlace } from '@/lib/library-episodes'
import { groupedIdentifierKey, groupIdentifiers } from '@/lib/library-identifiers'
import type { LibraryDuplicateWork } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — a file no library reports
import '@/styles/library.css' // .library-work-meta — a work's facts and its ids, a line each
import '@/styles/duplicates.css' // .duplicates-holder* — the works holding a file, one block each

/**
 * MAM-21 — who holds a file of a duplicate group: each library work through one of its versions, with its
 * title, kind and year, an episode's series and place, the version's own name, the library that reported
 * it and the work's ids — so the files of works whose ids disagree each show their own. A file the folder
 * scan found and no library reports says so instead of an empty line.
 */
export function DuplicateHolders({ works }: { works: LibraryDuplicateWork[] }) {
  const { t } = useTranslation(['duplicates', 'library'])

  if (works.length === 0) return <span className="settings-muted">{t('facts.noLibrary')}</span>
  return (
    <ul className="duplicates-holders">
      {works.map((work) => {
        const place = work.episode === null ? null : episodePlace(work.episode)
        const identifiers = groupIdentifiers(work.identifiers)
        return (
          // A work holds a file through one version, and through two only as two holdings.
          <li key={`${work.id}:${work.versionId}`} className="duplicates-holder">
            <span className="duplicates-holder-title">{work.title ?? t('library:work.unknownTitle')}</span>
            <span className="library-work-meta">
              <span>{t(`holder.kind.${work.kind}`)}</span>
              <span>{work.year ?? t('library:work.unknownYear')}</span>
              {work.seriesTitle !== null && <span>{t('holder.series', { title: work.seriesTitle })}</span>}
              {place !== null && <span>{place}</span>}
              {work.editionLabel !== null && <span>{t('holder.version', { label: work.editionLabel })}</span>}
              {work.sourceName !== null && <span>{t('library:work.from', { source: work.sourceName })}</span>}
            </span>
            <span className="library-work-meta">
              {identifiers.length === 0
                ? <span>{t('holder.noIds')}</span>
                : identifiers.map((identifier) => (
                  <span key={groupedIdentifierKey(identifier)}>
                    {t('library:work.identifier', { scheme: t(`library:schemes.${identifier.scheme}`), value: identifier.value })}
                  </span>
                ))}
            </span>
          </li>
        )
      })}
    </ul>
  )
}
