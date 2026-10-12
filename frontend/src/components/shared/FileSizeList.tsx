import { useTranslation } from 'react-i18next'
import { formatFileSize } from '@/lib/format'
import '@/styles/jobs.css' // .jobs-quarantine-list, -size, .jobs-file-path — the list, its sizes, the paths that break anywhere

/** One file of the list: a key of its own, its path and its size (null when nothing measured it). */
interface ListedFile {
  id: string
  path: string
  sizeBytes: number | null
}

/**
 * A confirmation's list of files, each its path with its size under it: what Delete for good deletes, what
 * Restore puts back, what Remove this copy moves and what stays. Left-aligned with paths that break anywhere,
 * as the dialog is narrow and centres its text on a phone.
 *
 * MAM-40 — one list for the four confirmations that had each written it out (DeleteForGoodMessage, Restore's
 * and Remove this copy's on the Duplicates page), so they read alike.
 */
export function FileSizeList({ files }: { files: ListedFile[] }) {
  const { t } = useTranslation('library')

  return (
    <ul className="jobs-quarantine-list">
      {files.map((file) => (
        <li key={file.id}>
          <span className="jobs-file-path">{file.path}</span>
          <span className="jobs-quarantine-size">{file.sizeBytes === null ? t('file.sizeUnknown') : formatFileSize(file.sizeBytes)}</span>
        </li>
      ))}
    </ul>
  )
}
