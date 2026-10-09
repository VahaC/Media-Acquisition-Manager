import { useId, useState, type ReactNode } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FileChips } from '@/components/library/FileChips'
import { FileLines } from '@/components/library/FileLines'
import { formatFileSize } from '@/lib/format'
import { versionSize } from '@/lib/library-versions'
import type { ShownVersion } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — "No file reported"
import '@/styles/library.css' // .library-version-rows, -row, -row-inset, -name, -chips, -size, -details

interface VersionRowsProps {
  versions: ShownVersion[]
  /**
   * MAM-21 — more about a version, in its open part after its files: the Duplicates page's facts of a
   * file (its identity, hash, editions and the works holding it). None on the Library and Search pages.
   */
  details?: (version: ShownVersion) => ReactNode
  /** MAM-21 — the list's accessible name: "Versions" unless the rows are something else (the Duplicates page's files). */
  label?: string
}

/**
 * MAM-16 — a work's versions as rows, the approved layout's default view: each row names the version
 * and sums up its file in chips (resolution, codec, HDR, audio languages) beside its size, and opens
 * to the file lines of FileLines below it. One row is open at a time; a single version is shown open,
 * as a plain row, since with nothing to compare there is nothing to collapse. A version without a
 * file is a plain row too: its row already says so, and there is nothing to open.
 *
 * Takes the versions in the card's order, which also numbers the unlabelled ones ("Version 2").
 *
 * MAM-21 — the Duplicates page shows a group's files as these rows too, each file a version named by the
 * end of its path that tells it apart, with its facts under its file lines (`details`).
 */
export function VersionRows({ versions, details, label: listLabel }: VersionRowsProps) {
  const { t } = useTranslation('library')
  const detailsId = useId()
  // The open row by its version's id, which a sync keeps (ADR 0008), so the row stays open while the
  // Library page asks for the works again during an inspection.
  const [openId, setOpenId] = useState<string | null>(null)
  const single = versions.length === 1

  return (
    <ul className="library-version-rows" aria-label={listLabel ?? t('work.versions')}>
      {versions.map((version, index) => {
        const label = version.editionLabel ?? t('versions.unlabelled', { number: index + 1 })
        const hasFile = version.files.length > 0
        if (single || !hasFile) {
          return (
            <li key={version.id}>
              {/* Among rows that open, its label lines up with theirs, past the chevron. */}
              <div className={single ? 'library-version-row' : 'library-version-row library-version-row-inset'}>
                <RowCells version={version} label={label} />
              </div>
              {hasFile && <VersionDetails version={version} details={details} />}
            </li>
          )
        }
        const open = version.id === openId
        const id = `${detailsId}-${index}`
        const Chevron = open ? ChevronDown : ChevronRight
        return (
          <li key={version.id}>
            <button
              type="button"
              className="library-version-row"
              aria-expanded={open}
              aria-controls={open ? id : undefined}
              onClick={() => setOpenId(open ? null : version.id)}
            >
              <Chevron className="library-version-chevron h-4 w-4" aria-hidden="true" />
              <RowCells version={version} label={label} />
            </button>
            {open && <VersionDetails id={id} version={version} details={details} />}
          </li>
        )
      })}
    </ul>
  )
}

// ── a row ──

/**
 * The label, the summary and the size. Spans only: in a version that opens they sit inside a button,
 * which may hold phrasing content alone.
 */
function RowCells({ version, label }: { version: ShownVersion; label: string }) {
  const { t } = useTranslation('library')
  const size = versionSize(version)

  return (
    <>
      <span className="library-version-name">{label}</span>
      <span className="library-version-chips"><Summary version={version} /></span>
      {version.files.length > 0 && (
        <span className="library-version-size">{size === null ? t('file.sizeUnknown') : formatFileSize(size)}</span>
      )}
    </>
  )
}

/**
 * What the version's file holds, in chips (FileChips), or that it has no file. A version of several
 * files is summed up by its first; all of them are listed once it is open.
 */
function Summary({ version }: { version: ShownVersion }) {
  const { t } = useTranslation('library')
  const [file] = version.files

  if (file === undefined) return <span className="settings-muted">{t('version.noFile')}</span>
  return <FileChips file={file} />
}

/** The open part of a row: every file of the version in full, then what the page adds about it (MAM-21). */
function VersionDetails({ id, version, details }: {
  id?: string
  version: ShownVersion
  details: VersionRowsProps['details']
}) {
  return (
    <div id={id} className="library-version-details">
      {version.files.map((file) => <FileLines key={file.id} file={file} />)}
      {details?.(version)}
    </div>
  )
}
