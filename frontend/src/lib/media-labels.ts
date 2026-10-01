import { i18n } from './i18n'

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
