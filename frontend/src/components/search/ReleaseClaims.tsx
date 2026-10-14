import { Captions, Film, Volume2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { codecName, languageName } from '@/lib/media-labels'
import { claimedEpisodeRange, claimedSeasons } from '@/lib/release-claims'
import type { ClaimedEpisodes, ClaimedReleaseQuality, ReleaseKind } from '@/lib/types'
import '@/styles/search.css' // .release-claims* — the line and its groups

/**
 * What a release's title claims about its quality, on its row on the Search page, as up to three
 * groups of plain text, each after its icon: the picture (a TV release's "TV" and seasons first, then
 * the resolution, what it was made from, the codec and the dynamic ranges), the audio languages and
 * the subtitle languages. Vasyl's choice of 2026-10-14, over chips and over a picked-out resolution.
 *
 * Only what the title states: a value it doesn't state is left out rather than named as unknown, and
 * a title that states nothing leaves no line. The Details dialog still lists every one, unknowns
 * included (ClaimedQuality). The line's hover says these are the title's claims, never checked against
 * a file (MAM-10), which the "Claimed" label before the chips used to say; the list keeps that name.
 */
export function ReleaseClaims({ claimed, kind, episodes }: {
  claimed: ClaimedReleaseQuality
  kind: ReleaseKind
  /** MAM-32 — the seasons or episodes a TV release's title claims. */
  episodes: ClaimedEpisodes | null
}) {
  const { t } = useTranslation(['search', 'library', 'common'])

  const episodeRange = episodes === null ? null : claimedEpisodeRange(episodes)
  const picture = [
    ...(kind !== 'Tv'
      ? []
      : [t('claimed.tv'), ...(episodes === null ? [] : [claimedSeasons(episodes)]), ...(episodeRange === null ? [] : [episodeRange])]),
    ...(claimed.resolution === null ? [] : [t('claimed.resolution', { value: claimed.resolution })]),
    ...(claimed.medium === null ? [] : [t(`claimed.medium.${claimed.medium}`)]),
    ...(claimed.videoCodec === null ? [] : [codecName(claimed.videoCodec)]),
    ...claimed.dynamicRanges.map((range) => t(`library:media.dynamicRange.${range}`)),
  ]
  const audio = claimed.audioLanguages
  const subtitles = claimed.subtitleLanguages

  if (picture.length === 0 && audio.length === 0 && subtitles.length === 0) return null

  const names = (codes: string[]) => codes.map(languageName).join(t('claimed.languageSeparator'))

  return (
    <ul className="release-claims" aria-label={t('claimed.label')} title={t('claimed.hint')}>
      {picture.length > 0 && (
        <li>
          <Film className="h-3.5 w-3.5" aria-hidden="true" />
          {picture.join(t('common:separator'))}
        </li>
      )}
      {audio.length > 0 && (
        <li>
          <Volume2 className="h-3.5 w-3.5" aria-label={t('library:media.audio')} />
          {names(audio)}
        </li>
      )}
      {subtitles.length > 0 && (
        <li>
          <Captions className="h-3.5 w-3.5" aria-label={t('library:media.subtitles')} />
          {names(subtitles)}
        </li>
      )}
    </ul>
  )
}
