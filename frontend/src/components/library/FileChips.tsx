import { TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { codecName, languageCode, resolutionClass } from '@/lib/media-labels'
import type { LibraryFile } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — "Not inspected yet" and a file with no chips
import '@/styles/library.css' // .library-version-chip, .library-file-problem — the chips and the warning in their place

/**
 * What a file holds, in chips (resolution, codec, HDR, audio languages), or why it can't say: a file
 * the app hasn't read, or couldn't, says so rather than showing no chips, which would read as a file
 * with nothing in it. Chips and spans only, for the caller's own chip row (`.library-version-chips`):
 * a version's row may sit inside a button, which holds phrasing content alone.
 *
 * MAM-24 — moved out of VersionRows, so the Download dialog sums up each old copy the owner may tick
 * exactly as the Library's row of its version does.
 */
export function FileChips({ file }: { file: LibraryFile }) {
  const { t } = useTranslation(['library', 'common'])

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
