import { useId } from 'react'
import { TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatFileSize } from '@/lib/format'
import type { LibraryFile, LibraryVersion, LibraryWork } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — the "nothing reported" lines
import '@/styles/library.css' // .library-work-*, .library-version*, .library-file*, .library-badge-warning

/**
 * MAM-8 — one work of the Library page: its title, original title and year, the identifiers it is
 * matched by (and a warning badge per missing one), and every version with its files. A value the
 * library didn't report says so ("Year unknown", "Size unknown") rather than leaving a gap.
 *
 * A file shows its name as the line and its folder under it: the name is what tells two versions
 * apart, and a long path would otherwise bury it. The whole path is in the name's tooltip.
 */
export function LibraryWorkCard({ work }: { work: LibraryWork }) {
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

  return (
    <article className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header library-work-header">
        <div className="library-work-heading">
          <h2 id={titleId} className="ui-card-title library-work-title">{work.title ?? t('work.unknownTitle')}</h2>
          <p className="library-work-meta">
            {showOriginalTitle && <span>{t('work.originalTitle', { title: work.originalTitle })}</span>}
            <span>{work.year ?? t('work.unknownYear')}</span>
            {source && <span>{t('work.from', { source })}</span>}
          </p>
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

/**
 * MAM-8 — versions by edition label (unlabelled first), then by their first file's path, so the
 * two copies of one movie keep their places from one sync to the next.
 */
function compareVersions(a: LibraryVersion, b: LibraryVersion): number {
  return (a.editionLabel ?? '').localeCompare(b.editionLabel ?? '')
    || (a.files[0]?.servicePath ?? '').localeCompare(b.files[0]?.servicePath ?? '')
}

// ── one version ──

function VersionItem({ version }: { version: LibraryVersion }) {
  const { t } = useTranslation('library')

  return (
    <li className="library-version">
      {/* The service's own label, when it offers a choice of versions of one movie. */}
      {version.editionLabel && <p className="library-version-label">{version.editionLabel}</p>}
      {version.files.length === 0
        ? <p className="settings-muted">{t('version.noFile')}</p>
        : version.files.map((file) => <FileLines key={file.id} file={file} />)}
    </li>
  )
}

// ── one file ──

function FileLines({ file }: { file: LibraryFile }) {
  const { t } = useTranslation('library')
  const { name, folder } = splitPath(file.servicePath)

  return (
    <div className="library-file">
      <p className="library-file-name" title={file.servicePath}>{name}</p>
      {folder !== null && <p className="library-file-folder">{folder}</p>}
      {/* The app's path only when the mappings changed it: the same path twice is noise. */}
      {file.appPath !== null && file.appPath !== file.servicePath && (
        <p className="library-file-app-path">{t('file.appPath', { path: file.appPath })}</p>
      )}
      {file.appPath === null && (
        <p className="library-file-problem">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{file.pathProblem ?? t('file.noAppPath')}</span>
        </p>
      )}
      <p className="library-file-facts">
        <span>{file.sizeBytes === null ? t('file.sizeUnknown') : formatFileSize(file.sizeBytes)}</span>
        <span>{file.locationName === null ? t('file.locationUnknown') : t('file.location', { name: file.locationName })}</span>
      </p>
    </div>
  )
}

/**
 * A path's last segment and the folder holding it, split at the last `/` or `\` (a Windows path).
 * A path without a folder part is all name.
 */
function splitPath(path: string): { name: string; folder: string | null } {
  const cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'))
  if (cut < 0 || cut === path.length - 1) return { name: path, folder: null }
  // A file right under the root keeps the root as its folder rather than an empty line.
  return { name: path.slice(cut + 1), folder: cut === 0 ? path.slice(0, 1) : path.slice(0, cut) }
}
