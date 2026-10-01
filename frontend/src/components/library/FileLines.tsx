import { TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { MediaLines } from '@/components/library/MediaLines'
import { formatFileSize } from '@/lib/format'
import type { LibraryFile, LibraryFileInspection } from '@/lib/types'
import '@/styles/library.css' // .library-file* — the name, folder, app path, problem and facts lines

/**
 * MAM-8 — one file of a version: its name over its folder, the app's path when the mappings changed
 * it (or why it has none), its size and library, then (MAM-9) what the app read from it.
 *
 * MAM-12 — moved out of LibraryWorkCard, so the search page's Details dialog shows a library copy
 * exactly as the Library page does.
 */
export function FileLines({ file }: { file: LibraryFile }) {
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
      <InspectionLines inspection={file.inspection} />
    </div>
  )
}

// ── what the app read from the file ──

/**
 * MAM-9 — the file's inspection under its facts. A file the app couldn't read gets the reason as a
 * warning, like a missing app path: the owner reads the API's sentence (it can quote paths inside
 * the app), everyone else a sentence for the status.
 */
function InspectionLines({ inspection }: { inspection: LibraryFileInspection | null }) {
  const { t } = useTranslation('library')

  if (inspection === null) return <p className="library-file-facts"><span>{t('media.notInspected')}</span></p>
  if (inspection.status === 'Inspected') return inspection.media && <MediaLines media={inspection.media} />
  // The path warning above already says why nothing could be read; a second line would repeat it.
  // Once the mappings give the file a path, the API reports it as not inspected yet instead.
  if (inspection.status === 'NoAppPath') return null
  return (
    <p className="library-file-problem">
      <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{inspection.problem ?? t(`media.status.${inspection.status}`)}</span>
    </p>
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
