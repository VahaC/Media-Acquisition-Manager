import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import type { FileInspectionStatus, LibraryFile, LibraryFileInspection, LibraryWork, MediaFacts } from '@/lib/types'
import { aliens, aliensInspection, continental, trackOf, unknownWork } from '@/test/library'

// MAM-9 — what the app read from each file with ffprobe, under the library's facts of the file.

/** The Continental with its one file changed by `changes`. */
function continentalWith(changes: Partial<LibraryFile>): LibraryWork {
  const [version] = continental.versions
  return { ...continental, versions: [{ ...version, files: [{ ...version.files[0], ...changes }] }] }
}

/** The Continental, inspected, with what ffprobe read as `media`. */
function inspectedAs(media: MediaFacts): LibraryWork {
  return continentalWith({ inspection: { ...aliensInspection, media } })
}

/** `work` with the row of its version labelled `label` opened, as a click opens it. */
async function renderOpened(work: LibraryWork, label: string) {
  const user = userEvent.setup()
  render(<LibraryWorkCard work={work} />)
  await user.click(screen.getByRole('button', { name: new RegExp(`^${label}`) }))
}

/** The entries of the track list named `name` ("Audio", "Subtitles"). */
const tracksOf = (name: string) =>
  within(screen.getByRole('list', { name })).getAllByRole('listitem').map((item) => item.textContent)

describe('LibraryWorkCard file inspection', () => {
  it('shows the length, the picture, the video codec, the dynamic range and the bit rate', async () => {
    // Arrange
    const work = aliens

    // Act
    await renderOpened(work, 'Theatrical Cut')

    // Assert
    const summary = screen.getByText('2:17:17').closest('p')!
    expect([...summary.children].map((part) => part.textContent))
      .toEqual(['2:17:17', '1920×1040', 'HEVC Main 10', 'SDR', '14.42 Mb/s'])
  })

  it('lists the audio tracks by language, codec, channels and flags, in the file order', async () => {
    // Arrange
    const work = aliens

    // Act
    await renderOpened(work, 'Theatrical Cut')

    // Assert
    expect(tracksOf('Audio')).toEqual([
      'Ukrainian · AC-3 · 5.1 · default',
      'Ukrainian · AC-3 · 5.1',
      'English · DTS-HD MA · 5.1',
    ])
  })

  it('lists the subtitles by language, format and flags', async () => {
    // Arrange
    const work = aliens

    // Act
    await renderOpened(work, 'Theatrical Cut')

    // Assert
    expect(tracksOf('Subtitles')).toEqual(['Ukrainian · SRT · forced', 'English · SRT · SDH', 'English · PGS'])
  })

  it('says when the app has not looked at a file yet, in its row and once it is opened', async () => {
    // Arrange — the Special Edition was synced after the last inspection.
    const work = aliens

    // Act
    await renderOpened(work, 'Special Edition')

    // Assert
    expect(screen.getAllByText('Not inspected yet')).toHaveLength(2)
    // Never a fact about tracks of a file the app hasn't read.
    expect(screen.queryByText('No audio tracks')).not.toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Audio' })).not.toBeInTheDocument()
  })

  it('names every value ffprobe did not give as unknown instead of guessing', () => {
    // Arrange
    const work = inspectedAs({
      durationMilliseconds: null,
      bitRate: null,
      tracks: [trackOf('Video'), trackOf('Audio'), trackOf('Subtitle')],
    })

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    const summary = screen.getByText('Duration unknown').closest('p')!
    expect([...summary.children].map((part) => part.textContent))
      .toEqual(['Duration unknown', 'Resolution unknown', 'Codec unknown', 'HDR unknown', 'Bit rate unknown'])
    expect(tracksOf('Audio')).toEqual(['Unknown language · Codec unknown'])
    expect(tracksOf('Subtitles')).toEqual(['Unknown language · Codec unknown'])
  })

  it('counts the channels when the layout is unknown, and names a codec it has no name for as ffprobe does', () => {
    // Arrange
    const work = inspectedAs({
      durationMilliseconds: 1_579_000,
      bitRate: 640_000,
      tracks: [
        trackOf('Video', { codec: 'h264', width: 1280, height: 720 }),
        trackOf('Audio', { codec: 'pcm_s16le', channels: 2, language: 'qaa', isForced: true }),
        trackOf('Audio', { codec: 'aac', profile: 'LC', channels: 2, channelLayout: 'stereo', language: 'eng' }),
      ],
    })

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    const summary = screen.getByText('26:19').closest('p')!
    expect([...summary.children].map((part) => part.textContent))
      .toEqual(['26:19', '1280×720', 'H.264', 'HDR unknown', '640 kb/s'])
    expect(tracksOf('Audio')).toEqual(['QAA · PCM_S16LE · 2 ch · forced', 'English · AAC · stereo'])
  })

  it.each([
    ['Hdr10', 'HDR10'],
    ['Hlg', 'HLG'],
    ['DolbyVision', 'Dolby Vision'],
  ] as const)('names the dynamic range %s as %s', (dynamicRange, expected) => {
    // Arrange
    const work = inspectedAs({
      durationMilliseconds: 8_237_456,
      bitRate: 24_570_000,
      tracks: [trackOf('Video', { codec: 'hevc', profile: 'Main 10', width: 3840, height: 2160, dynamicRange })],
    })

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    const summary = screen.getByText('3840×2160').closest('p')!
    expect(within(summary).getByText(expected)).toBeInTheDocument()
  })

  it('says when a file has no video track, no audio and no subtitles', () => {
    // Arrange
    const work = inspectedAs({ durationMilliseconds: 1_000, bitRate: 1_000, tracks: [] })

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert — the row says it as well, in place of chips.
    const summary = screen.getByText('0:01').closest('p')!
    expect(within(summary).getByText('No video track')).toBeInTheDocument()
    expect(screen.getByText('No audio tracks')).toBeInTheDocument()
    expect(screen.getByText('No subtitles')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Audio' })).not.toBeInTheDocument()
  })

  it.each([
    ['MediaUnreadable', 'ffprobe couldn\'t read this file as a video.'],
    ['OutsideAllowedRoots', 'This file is outside the media folders the app may read.'],
    ['NotFound', 'Nothing was found at this path.'],
    ['NotARegularFile', 'This path isn\'t a regular file.'],
    ['Unreadable', 'The app can\'t read this file.'],
    ['IdentityUnknown', 'This system can\'t tell files apart, so the app keeps nothing it read.'],
    ['InspectorUnavailable', 'ffprobe isn\'t available to the app.'],
  ] satisfies [FileInspectionStatus, string][])('tells everyone but the owner in words why a file is %s', (status, sentence) => {
    // Arrange — the API leaves the problem out for anyone but the owner.
    const inspection: LibraryFileInspection = { status, problem: null, inspectedUtc: '2026-09-30T08:05:00Z', media: null }
    const work = continentalWith({ inspection })

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    const line = screen.getByText(sentence).closest('p')!
    expect(line).toHaveClass('library-file-problem')
    expect(line.querySelector('svg')).not.toBeNull()
    expect(screen.queryByText('Not inspected yet')).not.toBeInTheDocument()
  })

  it('shows the owner the sentence of the API for a file it could not read', () => {
    // Arrange
    const problem = 'ffprobe could not read /media/movies/x.mkv: Invalid data found when processing input.'
    const work = continentalWith({ inspection: { status: 'MediaUnreadable', problem, inspectedUtc: '2026-09-30T08:05:00Z', media: null } })

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(screen.getByText(problem)).toBeInTheDocument()
    expect(screen.queryByText('ffprobe couldn\'t read this file as a video.')).not.toBeInTheDocument()
  })

  it('does not repeat why a file without an app path could not be read', async () => {
    // Arrange — the inspection's sentence is the mapper's, which the path line already shows.
    const work = unknownWork

    // Act
    await renderOpened(work, 'Version 1')

    // Assert
    expect(screen.getAllByText('No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.')).toHaveLength(1)
    expect(screen.queryByText('This file had no path inside the app when it was last inspected.')).not.toBeInTheDocument()
  })
})
