import { trackOf } from '@/test/library'
import { i18n } from './i18n'
import {
  audioTrackLabel,
  channelLayoutName,
  codecName,
  languageCode,
  languageName,
  resolutionClass,
  subtitleTrackLabel,
  videoCodecLabel,
} from './media-labels'

describe('media labels', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en-US')
  })

  it.each([
    ['hevc', 'HEVC'],
    ['h264', 'H.264'],
    ['av1', 'AV1'],
    ['vp9', 'VP9'],
    ['mpeg4', 'MPEG-4'],
    ['mpeg2video', 'MPEG-2'],
    ['ac3', 'AC-3'],
    ['eac3', 'E-AC-3'],
    ['dts', 'DTS'],
    ['truehd', 'TrueHD'],
    ['aac', 'AAC'],
    ['flac', 'FLAC'],
    ['opus', 'Opus'],
    ['mp3', 'MP3'],
    ['subrip', 'SRT'],
    ['hdmv_pgs_subtitle', 'PGS'],
    ['ass', 'ASS'],
    ['dvd_subtitle', 'VobSub'],
    ['mov_text', 'MOV text'],
  ])('names the codec %s as %s', (codec, expected) => {
    // Arrange
    const name = codec

    // Act
    const label = codecName(name)

    // Assert
    expect(label).toBe(expected)
  })

  it.each([
    ['DTS-HD MA', 'DTS-HD MA'],
    ['DTS-HD MA + DTS:X', 'DTS-HD MA + DTS:X'],
    ['DTS-ES', 'DTS-ES'],
    [null, 'DTS'],
  ])('names DTS with the profile %s as %s', (profile, expected) => {
    // Arrange
    const codec = 'dts'

    // Act
    const label = codecName(codec, profile)

    // Assert
    expect(label).toBe(expected)
  })

  it('leaves the profile of any other codec out of its name', () => {
    // Arrange — an AAC track's profile is LC; a viewer knows it as AAC.
    const profile = 'LC'

    // Act
    const label = codecName('aac', profile)

    // Assert
    expect(label).toBe('AAC')
  })

  it('shows a codec it has no name for upper-cased, as ffprobe names it', () => {
    // Arrange
    const codec = 'pcm_s16le'

    // Act
    const label = codecName(codec)

    // Assert
    expect(label).toBe('PCM_S16LE')
  })

  it.each([
    ['5.1(side)', '5.1'],
    ['6.1(back)', '6.1'],
    ['7.1', '7.1'],
    ['7.1(wide)', '7.1(wide)'],
    ['stereo', 'stereo'],
  ])('shows the channel layout %s as %s', (layout, expected) => {
    // Arrange
    const reported = layout

    // Act
    const label = channelLayoutName(reported)

    // Assert
    expect(label).toBe(expected)
  })

  it.each([
    ['ukr', 'Ukrainian'],
    ['eng', 'English'],
    ['fre', 'French'],
    ['pt-BR', 'Brazilian Portuguese'],
  ])('names the language tag %s %s', (tag, expected) => {
    // Arrange
    const tagged = tag

    // Act
    const name = languageName(tagged)

    // Assert
    expect(name).toBe(expected)
  })

  it.each([
    ['a tag Intl has no name for', 'qaa', 'QAA'],
    ['a value that is no language tag', 'not a tag', 'NOT A TAG'],
  ])('shows %s upper-cased rather than guessing', (_, tag, expected) => {
    // Arrange
    const tagged = tag

    // Act
    const name = languageName(tagged)

    // Assert
    expect(name).toBe(expected)
  })

  it('names a language in the UI language', async () => {
    // Arrange
    const english = languageName('ukr')
    await i18n.changeLanguage('de-DE')

    // Act
    const german = languageName('ukr')

    // Assert
    expect(english).toBe('Ukrainian')
    expect(german).toBe('Ukrainisch')
  })

  it.each([
    [3840, 2160, 2160],
    [3840, 1600, 2160],
    [2560, 1440, 1440],
    [1920, 1080, 1080],
    [1920, 800, 1080],
    [1916, 796, 1080],
    [1440, 1080, 1080],
    [1280, 720, 720],
    [1280, 536, 720],
    [1024, 576, 576],
    [720, 576, 576],
    [720, 480, 480],
    [640, 480, 480],
    [640, 360, null],
  ])('classes a %s×%s picture as %sp, by its width as much as its height', (width, height, expected) => {
    // Arrange
    const picture = { width, height }

    // Act
    const resolution = resolutionClass(picture.width, picture.height)

    // Assert
    expect(resolution).toBe(expected)
  })

  it.each([
    ['ukr', 'UKR'],
    ['eng', 'ENG'],
    ['en', 'EN'],
    ['en-us', 'EN'],
    ['pt-BR', 'PT'],
  ])('shortens the language tag %s to %s', (tag, expected) => {
    // Arrange
    const tagged = tag

    // Act
    const code = languageCode(tagged)

    // Assert
    expect(code).toBe(expected)
  })

  it.each([
    [trackOf('Video', { codec: 'hevc', profile: 'Main 10' }), 'HEVC Main 10'],
    [trackOf('Video', { codec: 'h264' }), 'H.264'],
    [trackOf('Video'), 'Codec unknown'],
  ])('names a video codec with its profile: %#', (track, expected) => {
    // Arrange
    const video = track

    // Act
    const label = videoCodecLabel(video)

    // Assert
    expect(label).toBe(expected)
  })

  it.each([
    [trackOf('Audio', { codec: 'ac3', channels: 6, channelLayout: '5.1(side)', language: 'ukr', isDefault: true }), 'Ukrainian · AC-3 · 5.1 · default'],
    [trackOf('Audio', { codec: 'dts', profile: 'DTS-HD MA', channels: 6, language: 'eng', isForced: true }), 'English · DTS-HD MA · 6 ch · forced'],
    [trackOf('Audio'), 'Unknown language · Codec unknown'],
  ])('describes an audio track by language, codec, channels and flags: %#', (track, expected) => {
    // Arrange
    const audio = track

    // Act
    const label = audioTrackLabel(audio)

    // Assert
    expect(label).toBe(expected)
  })

  it.each([
    [trackOf('Subtitle', { codec: 'subrip', profile: 'ignored', language: 'eng', isHearingImpaired: true }), 'English · SRT · SDH'],
    [trackOf('Subtitle', { codec: 'hdmv_pgs_subtitle', language: 'ukr', isDefault: true, isForced: true }), 'Ukrainian · PGS · default · forced'],
  ])('describes a subtitle track by language, format and flags: %#', (track, expected) => {
    // Arrange
    const subtitle = track

    // Act
    const label = subtitleTrackLabel(subtitle)

    // Assert
    expect(label).toBe(expected)
  })
})
