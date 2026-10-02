import { useId, useState } from 'react'
import { ChevronDown, ChevronRight, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FileLines } from '@/components/library/FileLines'
import { formatFileSize } from '@/lib/format'
import { versionSize } from '@/lib/library-versions'
import { codecName, languageCode, resolutionClass } from '@/lib/media-labels'
import type { LibraryVersion } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — "Not inspected yet", "No file reported" and a file with no chips
import '@/styles/library.css' // .library-version-rows, -row, -row-inset, -name, -chips, -chip, -size, -details, .library-file-problem

/**
 * MAM-16 — a work's versions as rows, the approved layout's default view: each row names the version
 * and sums up its file in chips (resolution, codec, HDR, audio languages) beside its size, and opens
 * to the file lines of FileLines below it. One row is open at a time; a single version is shown open,
 * as a plain row, since with nothing to compare there is nothing to collapse. A version without a
 * file is a plain row too: its row already says so, and there is nothing to open.
 *
 * Takes the versions in the card's order, which also numbers the unlabelled ones ("Version 2").
 */
export function VersionRows({ versions }: { versions: LibraryVersion[] }) {
  const { t } = useTranslation('library')
  const detailsId = useId()
  // The open row by its version's id, which a sync keeps (ADR 0008), so the row stays open while the
  // Library page asks for the works again during an inspection.
  const [openId, setOpenId] = useState<string | null>(null)
  const single = versions.length === 1

  return (
    <ul className="library-version-rows" aria-label={t('work.versions')}>
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
              {hasFile && <VersionDetails version={version} />}
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
            {open && <VersionDetails id={id} version={version} />}
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
function RowCells({ version, label }: { version: LibraryVersion; label: string }) {
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
 * What the version's file holds, in chips, or why it can't say: a file the app hasn't read, or
 * couldn't, says so rather than showing no chips, which would read as a file with nothing in it. A
 * version of several files is summed up by its first; all of them are listed once it is open.
 */
function Summary({ version }: { version: LibraryVersion }) {
  const { t } = useTranslation(['library', 'common'])
  const [file] = version.files

  if (file === undefined) return <span className="settings-muted">{t('version.noFile')}</span>
  if (file.appPath === null) return <Problem text={t('versions.noAppPath')} />
  if (file.inspection === null) return <span className="settings-muted">{t('media.notInspected')}</span>
  // Every status but Inspected comes without media facts: the app looked and couldn't read them.
  if (file.inspection.media === null) return <Problem text={t('versions.unreadable')} />

  const { tracks } = file.inspection.media
  const video = tracks.find((track) => track.kind === 'Video')
  const width = video?.width ?? null
  const height = video?.height ?? null
  const codec = video?.codec ?? null
  const dynamicRange = video?.dynamicRange ?? null
  const resolution = width === null || height === null ? null : resolutionClass(width, height)
  // Every audio track's language, repeats included: two Ukrainian dubs are two tracks to choose from.
  const languages = tracks.flatMap((track) =>
    track.kind === 'Audio' && track.language !== null ? [languageCode(track.language)] : [])
  // A value ffprobe didn't give is left out here; the open row names it as unknown. Below 480p the
  // picture has no class, and its size is the chip.
  const chips = [
    width === null || height === null
      ? null
      : resolution === null
        ? t('media.resolution', { width, height })
        : t('versions.resolutionClass', { value: resolution }),
    codec === null ? null : codecName(codec),
    dynamicRange === null ? null : t(`media.dynamicRange.${dynamicRange}`),
    languages.length === 0 ? null : languages.join(t('common:separator')),
  ].filter((chip) => chip !== null)

  // A file read without any of them still says what it holds: no picture at all, or a picture
  // ffprobe told nothing about.
  if (chips.length === 0) {
    return <span className="settings-muted">{t(video === undefined ? 'media.noVideo' : 'versions.unknown')}</span>
  }
  // The chips have no id, and their order is fixed.
  return chips.map((chip, index) => <span key={index} className="library-version-chip">{chip}</span>)
}

/** A warning in place of the chips, with the icon of FileLines' path problem. */
function Problem({ text }: { text: string }) {
  return (
    <span className="library-file-problem">
      <TriangleAlert className="h-3 w-3" aria-hidden="true" />
      <span>{text}</span>
    </span>
  )
}

/** The open part of a row: every file of the version in full. */
function VersionDetails({ id, version }: { id?: string; version: LibraryVersion }) {
  return (
    <div id={id} className="library-version-details">
      {version.files.map((file) => <FileLines key={file.id} file={file} />)}
    </div>
  )
}
