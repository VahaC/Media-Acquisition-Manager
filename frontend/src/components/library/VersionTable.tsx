import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { formatBitRate, formatDuration, formatFileSize } from '@/lib/format'
import { splitPath, versionSize } from '@/lib/library-versions'
import { audioTrackLabel, subtitleTrackLabel, videoCodecLabel } from '@/lib/media-labels'
import type { MediaFacts, ShownVersion } from '@/lib/types'
import '@/styles/library.css' // .library-version-table* — the frame, the table, a line, the file row and the caption

interface VersionTableProps {
  versions: ShownVersion[]
  /**
   * MAM-21 — more facts, a row each after File: the Duplicates page's folder, identity, hash, editions and
   * the works holding each file. None on the Library and Search pages.
   */
  rows?: readonly { label: string; value: (version: ShownVersion) => ReactNode }[]
  /** MAM-21 — the table's accessible name: "Versions" unless the columns are something else (the Duplicates page's files). */
  label?: string
  /** MAM-21 — where the values come from, when the added rows would make the Library's own line untrue. */
  caption?: string
}

/**
 * MAM-16 — a work's versions side by side, the approved layout's comparison view: a column per
 * version, a row per fact. A real table with row and column headers, so a screen reader names the
 * version and the fact of every cell.
 *
 * The media rows say why a file shows nothing ffprobe read — not inspected yet, no path in this app,
 * couldn't be read — rather than "Unknown" or "No audio tracks", which are facts about a file the
 * app read. A version of several files is described by its first, with its size the sum and every
 * file named; one without a file names none.
 *
 * Takes the versions in the card's order, which also numbers the unlabelled ones ("Version 2").
 *
 * MAM-21 — the Duplicates page compares a group's files in this table too, each file a column named by
 * the end of its path that tells it apart, with rows of its own after File (`rows`).
 */
export function VersionTable({ versions, rows, label, caption }: VersionTableProps) {
  const { t } = useTranslation('library')
  const unknown = t('versions.unknown')
  const columns = versions.map((version, index) => {
    const [file] = version.files
    return {
      version,
      label: version.editionLabel ?? t('versions.unlabelled', { number: index + 1 }),
      media: file?.inspection?.media ?? null,
      // What the media rows say when there are no facts. Every status but Inspected comes without them.
      unread: file === undefined
        ? unknown
        : file.appPath === null
          ? t('versions.noAppPath')
          : file.inspection === null
            ? t('media.notInspected')
            : t('versions.unreadable'),
    }
  })

  const video = (media: MediaFacts) => media.tracks.find((track) => track.kind === 'Video')
  /**
   * One track or file a line, each a block of its own, so a screen reader reads them apart too. The
   * lines have no id, two tracks can read the same, and their order is fixed.
   */
  const lines = (entries: string[], none: string) => (entries.length === 0
    ? none
    : entries.map((entry, index) => <span key={index} className="library-version-table-line">{entry}</span>))
  const mediaRows: [string, (media: MediaFacts) => ReactNode][] = [
    [t('versions.table.resolution'), (media) => {
      const track = video(media)
      if (track === undefined) return t('media.noVideo')
      return track.width === null || track.height === null
        ? unknown
        : t('media.resolution', { width: track.width, height: track.height })
    }],
    [t('versions.table.video'), (media) => {
      const track = video(media)
      if (track === undefined) return t('media.noVideo')
      return track.codec === null ? unknown : videoCodecLabel(track)
    }],
    [t('versions.table.hdr'), (media) => {
      const track = video(media)
      if (track === undefined) return t('media.noVideo')
      return track.dynamicRange === null ? unknown : t(`media.dynamicRange.${track.dynamicRange}`)
    }],
    [t('versions.table.bitRate'), (media) => (media.bitRate === null ? unknown : formatBitRate(media.bitRate))],
    [t('versions.table.duration'), (media) =>
      media.durationMilliseconds === null ? unknown : formatDuration(media.durationMilliseconds)],
    [t('media.audio'), (media) =>
      lines(media.tracks.filter((track) => track.kind === 'Audio').map(audioTrackLabel), t('media.noAudio'))],
    [t('media.subtitles'), (media) =>
      lines(media.tracks.filter((track) => track.kind === 'Subtitle').map(subtitleTrackLabel), t('media.noSubtitles'))],
  ]

  return (
    <>
      {/* The frame rounds and borders the table; a table's own border can't take a radius. */}
      <div className="library-version-table-frame">
        <table className="library-version-table" aria-label={label ?? t('work.versions')}>
          <thead>
            <tr>
              <td />
              {columns.map(({ version, label }) => <th key={version.id} scope="col">{label}</th>)}
            </tr>
          </thead>
          <tbody>
            {mediaRows.map(([label, value]) => (
              <tr key={label}>
                <th scope="row">{label}</th>
                {columns.map(({ version, media, unread }) => (
                  <td key={version.id}>{media === null ? unread : value(media)}</td>
                ))}
              </tr>
            ))}
            <tr>
              <th scope="row">{t('versions.table.size')}</th>
              {columns.map(({ version }) => {
                const size = versionSize(version)
                return <td key={version.id}>{size === null ? unknown : formatFileSize(size)}</td>
              })}
            </tr>
            <tr>
              <th scope="row">{t('versions.table.file')}</th>
              {columns.map(({ version }) => (
                <td
                  key={version.id}
                  className="library-version-table-file"
                  title={version.files.map((file) => file.servicePath).join('\n') || undefined}
                >
                  {lines(version.files.map((file) => splitPath(file.servicePath).name), t('version.noFile'))}
                </td>
              ))}
            </tr>
            {rows?.map((row) => (
              <tr key={row.label}>
                <th scope="row">{row.label}</th>
                {columns.map(({ version }) => <td key={version.id}>{row.value(version)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="library-version-table-caption">{caption ?? t('versions.table.caption')}</p>
    </>
  )
}
