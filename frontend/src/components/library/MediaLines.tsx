import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { formatBitRate, formatDuration } from '@/lib/format'
import { audioTrackLabel, subtitleTrackLabel, videoCodecLabel } from '@/lib/media-labels'
import type { MediaFacts } from '@/lib/types'
import '@/styles/library.css' // .library-file-facts, .library-inspection-* — the summary line and the track chips

/**
 * One line for the whole file — its length, the first video track's picture, codec and dynamic
 * range, and the overall bit rate — then a line of audio tracks and one of subtitles. A value
 * ffprobe didn't give is named as unknown, never guessed. Track titles are left out on purpose: they
 * are free text a release group wrote ("MVO | DniproFilm"), not something the app verified.
 *
 * MAM-12 — moved out of LibraryWorkCard with the file and version blocks around it, so the search
 * page's Details dialog shows a library copy's verified facts exactly as the Library page does.
 *
 * MAM-16 — the tracks are put into words by lib/media-labels.ts, which the versions table shares.
 */
export function MediaLines({ media }: { media: MediaFacts }) {
  const { t } = useTranslation('library')
  const video = media.tracks.find((track) => track.kind === 'Video')

  return (
    <>
      <p className="library-file-facts library-inspection-summary">
        <span>
          {media.durationMilliseconds === null ? t('media.durationUnknown') : formatDuration(media.durationMilliseconds)}
        </span>
        {video === undefined ? (
          <span>{t('media.noVideo')}</span>
        ) : (
          <>
            <span>
              {video.width === null || video.height === null
                ? t('media.resolutionUnknown')
                : t('media.resolution', { width: video.width, height: video.height })}
            </span>
            <span>{videoCodecLabel(video)}</span>
            <span>
              {video.dynamicRange === null
                ? t('media.dynamicRangeUnknown')
                : t(`media.dynamicRange.${video.dynamicRange}`)}
            </span>
          </>
        )}
        <span>{media.bitRate === null ? t('media.bitRateUnknown') : formatBitRate(media.bitRate)}</span>
      </p>
      <TrackLine
        label={t('media.audio')}
        empty={t('media.noAudio')}
        entries={media.tracks.filter((track) => track.kind === 'Audio').map(audioTrackLabel)}
      />
      <TrackLine
        label={t('media.subtitles')}
        empty={t('media.noSubtitles')}
        entries={media.tracks.filter((track) => track.kind === 'Subtitle').map(subtitleTrackLabel)}
      />
    </>
  )
}

/**
 * The tracks of one kind as a labelled list whose entries wrap as a whole, or a line saying the file
 * has none.
 */
function TrackLine({ label, empty, entries }: { label: string; empty: string; entries: string[] }) {
  const labelId = useId()
  if (entries.length === 0) return <p className="library-file-facts"><span>{empty}</span></p>
  return (
    <div className="library-inspection-tracks">
      <span id={labelId} className="library-inspection-kind">{label}</span>
      <ul className="library-inspection-track-list" aria-labelledby={labelId}>
        {/* Tracks have no id; the file's own order is fixed, and two tracks can read the same. */}
        {entries.map((entry, index) => <li key={index} className="library-inspection-track">{entry}</li>)}
      </ul>
    </div>
  )
}
