import { i18n } from './i18n'
import type { MediaTrack } from './types'

// MAM-9 — the names a file's tracks are shown by on the Library page. ffprobe reports codecs and
// languages as identifiers (`hevc`, `ukr`); people know them as HEVC and Ukrainian.

/**
 * Codec names as they are written on release pages and players. These are names, the same in every
 * language, so they stay out of the locale resources; the words around them ("Codec unknown") don't.
 */
const CODEC_NAMES: Readonly<Record<string, string>> = {
  hevc: 'HEVC',
  h264: 'H.264',
  av1: 'AV1',
  vp9: 'VP9',
  mpeg4: 'MPEG-4',
  mpeg2video: 'MPEG-2',
  ac3: 'AC-3',
  eac3: 'E-AC-3',
  dts: 'DTS',
  truehd: 'TrueHD',
  aac: 'AAC',
  flac: 'FLAC',
  opus: 'Opus',
  mp3: 'MP3',
  subrip: 'SRT',
  hdmv_pgs_subtitle: 'PGS',
  ass: 'ASS',
  dvd_subtitle: 'VobSub',
  mov_text: 'MOV text',
}

/**
 * A codec's name for ffprobe's `codec_name`; a codec this list doesn't know is shown by its own name,
 * upper-cased. ffprobe names every DTS flavour `dts` and tells them apart by the profile, so a DTS
 * profile (`DTS-HD MA`, `DTS:X`) is the name.
 */
export function codecName(codec: string, profile: string | null = null): string {
  if (codec === 'dts' && profile?.startsWith('DTS')) return profile
  return CODEC_NAMES[codec] ?? codec.toUpperCase()
}

/** The channel layout without ffprobe's speaker-placement suffix: `5.1(side)` is 5.1 to a viewer. */
export function channelLayoutName(layout: string): string {
  return layout.replace(/\((side|back)\)$/, '')
}

let languageNames: { locale: string; names: Intl.DisplayNames } | null = null

/**
 * A language tag's name in the UI language: `ukr` → Ukrainian, `eng` → English. A tag Intl has no
 * name for (or can't parse) is shown as it is, upper-cased, rather than guessed at.
 */
export function languageName(tag: string): string {
  if (languageNames?.locale !== i18n.language) {
    languageNames = {
      locale: i18n.language,
      names: new Intl.DisplayNames(i18n.language, { type: 'language', fallback: 'none' }),
    }
  }
  try {
    return languageNames.names.of(tag) ?? tag.toUpperCase()
  } catch {
    return tag.toUpperCase() /* not a valid language tag */
  }
}

/**
 * MAM-16 — a language tag as the short code a version's row lists its audio by: the language part,
 * upper-cased (`ukr` → UKR, `en-us` → EN). Files are tagged with three letters or two, so the code
 * is whichever the file has rather than a guess at the other.
 */
export function languageCode(tag: string): string {
  return tag.split(/[-_]/)[0].toUpperCase()
}

/** The classes a picture is named by, largest first: the frame's height and its 16:9 width. */
const RESOLUTION_CLASSES = [
  [2160, 3840],
  [1440, 2560],
  [1080, 1920],
  [720, 1280],
  [576, 1024],
  [480, 854],
] as const

/**
 * MAM-16 — the class a picture is known by (1080 for 1080p), or null below 480p. The width counts as
 * much as the height: a film cropped to 2.40:1 is 1920×800 and a 4:3 one 1440×1080, both 1080p. A
 * picture within a tenth of the class's frame reaches it, as encoders crop a few pixels (1916×796);
 * a DVD's 720×576 is 576p by its height, while its 720 pixels of width reach no class of their own.
 */
export function resolutionClass(width: number, height: number): number | null {
  const reached = RESOLUTION_CLASSES.find(
    ([classHeight, classWidth]) => width >= classWidth * 0.9 || height >= classHeight * 0.9,
  )
  return reached?.[0] ?? null
}

// ── a track in words ──
// MAM-16 — moved out of MediaLines, so a version's table names the tracks exactly as its expanded
// row does.

/** A track's codec, or that ffprobe didn't give one. */
function codecOf(track: MediaTrack): string {
  return track.codec === null ? i18n.t('library:media.codecUnknown') : codecName(track.codec, track.profile)
}

/** The parts that apply, joined by the UI's separator: `Ukrainian · AC-3 · 5.1 · default`. */
function describe(parts: (string | false | null)[]): string {
  return parts.filter((part) => typeof part === 'string').join(i18n.t('common:separator'))
}

function languageOf(track: MediaTrack): string {
  return track.language === null ? i18n.t('library:media.unknownLanguage') : languageName(track.language)
}

function flagsOf(track: MediaTrack): (string | false)[] {
  return [track.isDefault && i18n.t('library:media.default'), track.isForced && i18n.t('library:media.forced')]
}

/** A video track's codec with its profile, as release pages write it (`HEVC Main 10`, `H.264 High`). */
export function videoCodecLabel(track: MediaTrack): string {
  return track.codec === null || track.profile === null
    ? codecOf(track)
    : i18n.t('library:media.codecProfile', { codec: codecName(track.codec), profile: track.profile })
}

/** An audio track by language, codec, channels and flags: `Ukrainian · AC-3 · 5.1 · default`. */
export function audioTrackLabel(track: MediaTrack): string {
  return describe([
    languageOf(track),
    codecOf(track),
    track.channelLayout !== null
      ? channelLayoutName(track.channelLayout)
      : track.channels !== null && i18n.t('library:media.channels', { count: track.channels }),
    ...flagsOf(track),
  ])
}

/** A subtitle track by language, format and flags: `English · SRT · SDH`. */
export function subtitleTrackLabel(track: MediaTrack): string {
  // A subtitle's profile says nothing to a viewer; its format alone does (SRT, PGS).
  return describe([
    languageOf(track),
    track.codec === null ? i18n.t('library:media.codecUnknown') : codecName(track.codec),
    ...flagsOf(track),
    track.isHearingImpaired && i18n.t('library:media.hearingImpaired'),
  ])
}
