import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { codecName, languageName } from '@/lib/media-labels'
import type { ClaimedReleaseQuality } from '@/lib/types'
import '@/styles/library.css' // .library-inspection-* — the labelled row of chips a file's tracks use

/**
 * MAM-12 — what a release's title claims about its quality, as chips under a visible "Claimed"
 * label: the resolution, what it was made from, the video codec, the dynamic ranges, and the audio
 * and subtitle languages. These are claims (MAM-10), never checked against a file, which is why the
 * label is there; a value the title doesn't state is named as unknown, never guessed.
 *
 * Laid out like a file's track line on the Library page (the same chip classes), so a release's
 * claims and a library copy's verified facts read side by side the same way.
 */
export function ClaimedQuality({ claimed }: { claimed: ClaimedReleaseQuality }) {
  const { t } = useTranslation(['search', 'library', 'common'])
  const labelId = useId()
  const languages = (codes: string[]) => codes.map(languageName).join(t('common:separator'))

  const chips = [
    claimed.resolution === null ? t('claimed.resolutionUnknown') : t('claimed.resolution', { value: claimed.resolution }),
    claimed.medium === null ? t('claimed.mediumUnknown') : t(`claimed.medium.${claimed.medium}`),
    claimed.videoCodec === null ? t('claimed.codecUnknown') : codecName(claimed.videoCodec),
    ...(claimed.dynamicRanges.length === 0
      ? [t('claimed.dynamicRangeUnknown')]
      : claimed.dynamicRanges.map((range) => t(`library:media.dynamicRange.${range}`))),
    claimed.audioLanguages.length === 0
      ? t('claimed.audioUnknown')
      : t('claimed.audio', { languages: languages(claimed.audioLanguages) }),
    claimed.subtitleLanguages.length === 0
      ? t('claimed.subtitlesUnknown')
      : t('claimed.subtitles', { languages: languages(claimed.subtitleLanguages) }),
  ]

  return (
    <div className="library-inspection-tracks">
      <span id={labelId} className="library-inspection-kind">{t('claimed.label')}</span>
      <ul className="library-inspection-track-list" aria-labelledby={labelId}>
        {/* Fixed positions: two chips can read the same only if the title says so twice. */}
        {chips.map((chip, index) => <li key={index} className="library-inspection-track">{chip}</li>)}
      </ul>
    </div>
  )
}
