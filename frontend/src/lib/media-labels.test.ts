import { i18n } from './i18n'
import { channelLayoutName, codecName, languageName } from './media-labels'

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
})
