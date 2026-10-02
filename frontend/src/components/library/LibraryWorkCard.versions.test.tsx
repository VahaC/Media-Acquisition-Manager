import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import type { LibraryFile, LibraryVersion, LibraryWork } from '@/lib/types'
import { aliens, aliensInspection, continental, trackOf, unknownWork } from '@/test/library'
import { windowIs } from '@/test/match-media'

// MAM-16 — a card's versions as the approved layout shows them: rows that open one at a time, or a
// table that compares them, picked with a switch shared by every card and kept in this browser.

const [theatricalCut, specialEdition] = aliens.versions

/** A version of Aliens with its one file changed by `changes`. */
function withFile(version: LibraryVersion, changes: Partial<LibraryFile>): LibraryVersion {
  return { ...version, files: [{ ...version.files[0], ...changes }] }
}

/** The row of the version labelled `label`, which opens it. */
const rowOf = (label: string) => screen.getByRole('button', { name: new RegExp(`^${label}`) })

/** The chips a row sums its file up in. */
const chipsOf = (label: string) =>
  [...rowOf(label).querySelectorAll('.library-version-chip')].map((chip) => chip.textContent)

/** The table's row headed `header`, as the text of each version's cell, a line of it per line. */
function tableRow(header: string) {
  const row = screen.getByRole('rowheader', { name: header }).closest('tr')!
  return within(row).getAllByRole('cell').map((cell) => {
    const lines = [...cell.querySelectorAll('.library-version-table-line')]
    return lines.length === 0 ? cell.textContent : lines.map((line) => line.textContent).join('\n')
  })
}

describe('LibraryWorkCard versions', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('as rows', () => {
    it('shows each version as a closed row with its label, what its file holds in chips, and its size', () => {
      // Arrange
      const work = aliens

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      expect(rowOf('Special Edition')).toHaveAttribute('aria-expanded', 'false')
      expect(rowOf('Special Edition')).toHaveTextContent(/^Special EditionNot inspected yet18\.27 GiB$/)
      expect(rowOf('Theatrical Cut')).toHaveAttribute('aria-expanded', 'false')
      expect(chipsOf('Theatrical Cut')).toEqual(['1080p', 'HEVC', 'SDR', 'UKR · UKR · ENG'])
      expect(rowOf('Theatrical Cut')).toHaveTextContent(/13\.83 GiB$/)
      expect(screen.queryByText(/^\[02\.\d\] Aliens/)).not.toBeInTheDocument()
    })

    it('names a version without a label by its place in the card', () => {
      // Arrange — by path, the Theatrical Cut's file comes first.
      const work: LibraryWork = { ...aliens, versions: aliens.versions.map((version) => ({ ...version, editionLabel: null })) }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      expect(chipsOf('Version 1')).toEqual(['1080p', 'HEVC', 'SDR', 'UKR · UKR · ENG'])
      expect(rowOf('Version 2')).toHaveTextContent('Not inspected yet')
    })

    it('says why a row has no chips: no path in this app, or a file the app could not read', () => {
      // Arrange
      const work: LibraryWork = {
        ...aliens,
        versions: [
          withFile(theatricalCut, { inspection: { status: 'NotFound', problem: null, inspectedUtc: '2026-09-30T08:05:00Z', media: null } }),
          withFile(specialEdition, { appPath: null, pathStatus: 'NotCovered' }),
        ],
      }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      expect(rowOf('Special Edition')).toHaveTextContent('No path in this app')
      expect(rowOf('Theatrical Cut')).toHaveTextContent('Couldn\'t be read')
      expect(within(rowOf('Theatrical Cut')).getByText('Couldn\'t be read').closest('.library-file-problem')!.querySelector('svg')).not.toBeNull()
      expect(screen.queryByText('Not inspected yet')).not.toBeInTheDocument()
    })

    it('says what a file read without a value for any chip holds: no video track, or unknown', () => {
      // Arrange — no track at all, and a video track ffprobe told nothing about beside untagged audio.
      const empty = { ...aliensInspection, media: { durationMilliseconds: 1_000, bitRate: 1_000, tracks: [] } }
      const unknowns = { ...aliensInspection, media: { durationMilliseconds: null, bitRate: null, tracks: [trackOf('Video'), trackOf('Audio')] } }
      const work: LibraryWork = {
        ...aliens,
        versions: [withFile(theatricalCut, { inspection: unknowns }), withFile(specialEdition, { inspection: empty })],
      }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      expect(rowOf('Special Edition')).toHaveTextContent(/^Special EditionNo video track18\.27 GiB$/)
      expect(rowOf('Theatrical Cut')).toHaveTextContent(/^Theatrical CutUnknown13\.83 GiB$/)
      expect(chipsOf('Special Edition')).toEqual([])
      expect(chipsOf('Theatrical Cut')).toEqual([])
    })

    it('opens a row to its file: name, folder, app path, facts, and the tracks ffprobe read', async () => {
      // Arrange
      const user = userEvent.setup()
      render(<LibraryWorkCard work={aliens} />)

      // Act
      await user.click(rowOf('Theatrical Cut'))

      // Assert
      const row = rowOf('Theatrical Cut')
      expect(row).toHaveAttribute('aria-expanded', 'true')
      const details = document.getElementById(row.getAttribute('aria-controls')!)!
      expect(within(details).getByText(/^\[02\.1\] Aliens \(1986\)/)).toBeInTheDocument()
      expect(within(details).getByText('/media/movies/Alien. Collection')).toBeInTheDocument()
      expect(within(details).getByText(/^In this app: /)).toBeInTheDocument()
      expect(within(details).getByText('Library: Фільми')).toBeInTheDocument()
      expect(within(details).getByText('HEVC Main 10')).toBeInTheDocument()
      expect(within(details).getByRole('list', { name: 'Audio' })).toBeInTheDocument()
      expect(within(details).getByRole('list', { name: 'Subtitles' })).toBeInTheDocument()
    })

    it('keeps one row open at a time, and closes an open row on a second click', async () => {
      // Arrange
      const user = userEvent.setup()
      render(<LibraryWorkCard work={aliens} />)
      await user.click(rowOf('Theatrical Cut'))

      // Act
      await user.click(rowOf('Special Edition'))
      const afterSecond = [rowOf('Special Edition').getAttribute('aria-expanded'), rowOf('Theatrical Cut').getAttribute('aria-expanded')]
      const openFiles = screen.getAllByText(/^\[02\.\d\] Aliens/).map((name) => name.textContent)
      await user.click(rowOf('Special Edition'))

      // Assert
      expect(afterSecond).toEqual(['true', 'false'])
      expect(openFiles).toEqual(['[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv'])
      expect(rowOf('Special Edition')).toHaveAttribute('aria-expanded', 'false')
      expect(screen.queryByText(/^\[02\.\d\] Aliens/)).not.toBeInTheDocument()
    })

    it('shows a single version open, as a row that is no button', () => {
      // Arrange — The Continental has one version, without a label.
      const work = continental

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      const versions = screen.getByRole('list', { name: 'Versions' })
      expect(within(versions).queryByRole('button')).not.toBeInTheDocument()
      expect(versions).toHaveTextContent(/^Version 1Not inspected yet3\.62 GiB/)
      expect(within(versions).getByText(/S01E01 - BDRip 1080p x265/)).toBeInTheDocument()
    })

    it('sums up a version of several files by its first, adds their sizes, and lists them all once open', async () => {
      // Arrange — Jellyfin stacks a movie in two parts as one version.
      const [part1] = theatricalCut.files
      const part2: LibraryFile = { ...part1, id: '7a1e0000-0000-4000-8000-000000000031', servicePath: '/media/movies/Alien. Collection/Aliens (1986) - part2.mkv', sizeBytes: 1_073_741_824, inspection: null }
      const work: LibraryWork = { ...aliens, versions: [{ ...theatricalCut, files: [part1, part2] }, specialEdition] }
      const user = userEvent.setup()
      render(<LibraryWorkCard work={work} />)

      // Act
      await user.click(rowOf('Theatrical Cut'))

      // Assert
      expect(chipsOf('Theatrical Cut')).toEqual(['1080p', 'HEVC', 'SDR', 'UKR · UKR · ENG'])
      expect(rowOf('Theatrical Cut')).toHaveTextContent(/14\.83 GiB$/)
      expect(screen.getByText(/^\[02\.1\] Aliens \(1986\)/)).toBeInTheDocument()
      expect(screen.getByText('Aliens (1986) - part2.mkv')).toBeInTheDocument()
    })

    it('says in its row when a version has no file, with no size, and gives it nothing to open', () => {
      // Arrange
      const work = unknownWork

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      const disc = screen.getByText('Disc').closest('li')!
      expect(disc).toHaveTextContent(/^DiscNo file reported for this version\.$/)
      expect(within(disc).queryByRole('button')).not.toBeInTheDocument()
      expect(disc.firstElementChild).toHaveClass('library-version-row-inset')
      expect(rowOf('Version 1')).toHaveTextContent(/^Version 1No path in this appSize unknown$/)
    })

    it('says once that the single version of a work has no file', () => {
      // Arrange
      const work: LibraryWork = { ...unknownWork, versions: [unknownWork.versions[1]] }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      const versions = screen.getByRole('list', { name: 'Versions' })
      expect(versions).toHaveTextContent(/^DiscNo file reported for this version\.$/)
      expect(within(versions).getAllByText('No file reported for this version.')).toHaveLength(1)
      expect(within(versions).queryByRole('button')).not.toBeInTheDocument()
    })
  })

  describe('the switch', () => {
    it('is not offered for a work of one version', () => {
      // Arrange
      const work = continental

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      expect(screen.queryByRole('radiogroup', { name: 'Show versions as' })).not.toBeInTheDocument()
    })

    it('offers Rows and Table under the badges, with Rows picked at first', () => {
      // Arrange
      const work = aliens

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      const group = screen.getByRole('radiogroup', { name: 'Show versions as' })
      expect(within(group).getByRole('radio', { name: 'Rows' })).toBeChecked()
      expect(within(group).getByRole('radio', { name: 'Table' })).not.toBeChecked()
      expect(within(group).getByTitle('Versions as rows')).toHaveTextContent('Rows')
      expect(within(group).getByTitle('Versions as table')).toHaveTextContent('Table')
      expect(screen.getByRole('list', { name: 'Identifiers' }).nextElementSibling).toContainElement(group)
    })

    it('shows the table on Table and remembers it in this browser', async () => {
      // Arrange
      const user = userEvent.setup()
      render(<LibraryWorkCard work={aliens} />)

      // Act
      await user.click(screen.getByRole('radio', { name: 'Table' }))

      // Assert
      expect(screen.getByRole('table', { name: 'Versions' })).toBeInTheDocument()
      expect(screen.queryByRole('list', { name: 'Versions' })).not.toBeInTheDocument()
      expect(localStorage.getItem('mam:versionsView')).toBe('table')
    })

    it.each([
      ['table', 'table'],
      ['rows', 'list'],
      ['grid', 'list'],
    ])('opens with %s stored as a %s', (stored, role) => {
      // Arrange
      localStorage.setItem('mam:versionsView', stored)

      // Act
      render(<LibraryWorkCard work={aliens} />)

      // Assert
      expect(screen.getByRole(role, { name: 'Versions' })).toBeInTheDocument()
    })

    it('switches every card on the page at once', async () => {
      // Arrange
      const user = userEvent.setup()
      const second: LibraryWork = { ...aliens, id: '7a1e0000-0000-4000-8000-000000000099', title: 'Чужі 2' }
      render(
        <>
          <LibraryWorkCard work={aliens} />
          <LibraryWorkCard work={second} />
        </>,
      )

      // Act
      await user.click(screen.getAllByRole('radio', { name: 'Table' })[0])

      // Assert
      expect(screen.getAllByRole('table', { name: 'Versions' })).toHaveLength(2)
      expect(screen.getAllByRole('radio', { name: 'Table' }).map((radio) => (radio as HTMLInputElement).checked)).toEqual([true, true])
    })

    it('shows the rows on a phone whatever is stored, and the table again once the window is wide', () => {
      // Arrange
      localStorage.setItem('mam:versionsView', 'table')
      const display = windowIs(true)
      render(<LibraryWorkCard work={aliens} />)
      const onPhone = screen.queryByRole('table', { name: 'Versions' })

      // Act
      act(() => display.resize(false))

      // Assert
      expect(onPhone).not.toBeInTheDocument()
      expect(screen.getByRole('table', { name: 'Versions' })).toBeInTheDocument()
      expect(localStorage.getItem('mam:versionsView')).toBe('table')
    })

    it('shows the rows once the window narrows to a phone', () => {
      // Arrange
      localStorage.setItem('mam:versionsView', 'table')
      const display = windowIs(false)
      render(<LibraryWorkCard work={aliens} />)

      // Act
      act(() => display.resize(true))

      // Assert
      expect(screen.getByRole('list', { name: 'Versions' })).toBeInTheDocument()
      expect(screen.queryByRole('table', { name: 'Versions' })).not.toBeInTheDocument()
    })
  })

  describe('as a table', () => {
    beforeEach(() => {
      localStorage.setItem('mam:versionsView', 'table')
    })

    it('has a column per version and a row per fact, in the order of the approved layout', () => {
      // Arrange
      const work = aliens

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      const table = screen.getByRole('table', { name: 'Versions' })
      expect(within(table).getAllByRole('columnheader').map((header) => header.textContent))
        .toEqual(['Special Edition', 'Theatrical Cut'])
      expect(within(table).getAllByRole('rowheader').map((header) => header.textContent))
        .toEqual(['Resolution', 'Video', 'HDR', 'Bit rate', 'Duration', 'Audio', 'Subtitles', 'Size', 'File'])
    })

    it('fills each column from what ffprobe read and what Jellyfin reported, one track or file a line', () => {
      // Arrange
      const work = aliens

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert — the Theatrical Cut is the second column.
      expect(tableRow('Resolution')[1]).toBe('1920×1040')
      expect(tableRow('Video')[1]).toBe('HEVC Main 10')
      expect(tableRow('HDR')[1]).toBe('SDR')
      expect(tableRow('Bit rate')[1]).toBe('14.42 Mb/s')
      expect(tableRow('Duration')[1]).toBe('2:17:17')
      expect(tableRow('Audio')[1]).toBe('Ukrainian · AC-3 · 5.1 · default\nUkrainian · AC-3 · 5.1\nEnglish · DTS-HD MA · 5.1')
      expect(tableRow('Subtitles')[1]).toBe('Ukrainian · SRT · forced\nEnglish · SRT · SDH\nEnglish · PGS')
      expect(tableRow('Size')).toEqual(['18.27 GiB', '13.83 GiB'])
      expect(tableRow('File')[1]).toBe('[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv')
      const fileCell = screen.getByRole('rowheader', { name: 'File' }).closest('tr')!.cells[2]
      expect(fileCell).toHaveAttribute('title', theatricalCut.files[0].servicePath)
    })

    it('says why the media rows of a file are empty rather than "No audio tracks"', () => {
      // Arrange — the Special Edition is not inspected yet; the run found no app path for one copy
      // and nothing at the path of another.
      const inspectedUtc = '2026-09-30T08:05:00Z'
      const noAppPath = { status: 'NoAppPath', problem: null, inspectedUtc, media: null } as const
      const notFound = { status: 'NotFound', problem: null, inspectedUtc, media: null } as const
      const work: LibraryWork = {
        ...aliens,
        versions: [
          specialEdition,
          { ...withFile(theatricalCut, { appPath: null, pathStatus: 'NotCovered', inspection: noAppPath }), id: 'no-path', editionLabel: 'No path' },
          { ...withFile(theatricalCut, { inspection: notFound }), id: 'unreadable', editionLabel: 'Unreadable' },
        ],
      }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert — columns by label: No path, Special Edition, Unreadable.
      for (const header of ['Resolution', 'Video', 'HDR', 'Bit rate', 'Duration', 'Audio', 'Subtitles']) {
        expect(tableRow(header)).toEqual(['No path in this app', 'Not inspected yet', 'Couldn\'t be read'])
      }
      expect(screen.queryByText('No audio tracks')).not.toBeInTheDocument()
      expect(screen.queryByText('No subtitles')).not.toBeInTheDocument()
    })

    it('shows the facts an earlier reading left for a file with no path in this app now, which its row only flags', async () => {
      // Arrange — the API keeps an Inspected reading until the next run; the open row shows it too.
      const user = userEvent.setup()
      const work: LibraryWork = { ...aliens, versions: [withFile(theatricalCut, { appPath: null, pathStatus: 'NotCovered' }), specialEdition] }
      render(<LibraryWorkCard work={work} />)
      const table = [tableRow('Resolution')[1], tableRow('Video')[1], tableRow('Audio')[1]]

      // Act
      await user.click(screen.getByRole('radio', { name: 'Rows' }))

      // Assert — the Theatrical Cut is the second column.
      expect(table).toEqual(['1920×1040', 'HEVC Main 10', 'Ukrainian · AC-3 · 5.1 · default\nUkrainian · AC-3 · 5.1\nEnglish · DTS-HD MA · 5.1'])
      expect(rowOf('Theatrical Cut')).toHaveTextContent(/^Theatrical CutNo path in this app13\.83 GiB$/)
    })

    it('says Unknown for what ffprobe did not give, and no tracks for a file read without any', () => {
      // Arrange
      const unknowns = { ...aliensInspection, media: { durationMilliseconds: null, bitRate: null, tracks: [trackOf('Video')] } }
      const empty = { ...aliensInspection, media: { durationMilliseconds: 1_000, bitRate: 1_000, tracks: [] } }
      const work: LibraryWork = {
        ...aliens,
        versions: [
          withFile(theatricalCut, { inspection: unknowns, sizeBytes: null }),
          withFile(specialEdition, { inspection: empty }),
        ],
      }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert — Special Edition first.
      expect(tableRow('Resolution')).toEqual(['No video track', 'Unknown'])
      expect(tableRow('Video')).toEqual(['No video track', 'Unknown'])
      expect(tableRow('HDR')).toEqual(['No video track', 'Unknown'])
      expect(tableRow('Bit rate')).toEqual(['1 kb/s', 'Unknown'])
      expect(tableRow('Duration')).toEqual(['0:01', 'Unknown'])
      expect(tableRow('Audio')).toEqual(['No audio tracks', 'No audio tracks'])
      expect(tableRow('Subtitles')).toEqual(['No subtitles', 'No subtitles'])
      expect(tableRow('Size')).toEqual(['18.27 GiB', 'Unknown'])
    })

    it('adds up the sizes of a version of several files and names every file, and says when one has none', () => {
      // Arrange
      const [part1] = theatricalCut.files
      const part2: LibraryFile = { ...part1, id: '7a1e0000-0000-4000-8000-000000000031', servicePath: '/media/movies/Alien. Collection/Aliens (1986) - part2.mkv', sizeBytes: 1_073_741_824 }
      const work: LibraryWork = { ...aliens, versions: [{ ...theatricalCut, files: [part1, part2] }, { ...specialEdition, files: [] }] }

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert — Special Edition, without a file, first.
      expect(tableRow('Size')).toEqual(['Unknown', '14.83 GiB'])
      expect(tableRow('File')).toEqual([
        'No file reported for this version.',
        '[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv\nAliens (1986) - part2.mkv',
      ])
      expect(tableRow('Duration')).toEqual(['Unknown', '2:17:17'])
    })

    it('says under the table where its values come from', () => {
      // Arrange
      const work = aliens

      // Act
      render(<LibraryWorkCard work={work} />)

      // Assert
      const caption = screen.getByText('Size and file come from Jellyfin; every other value was read from the file by ffprobe.')
      expect(screen.getByRole('table', { name: 'Versions' }).parentElement!.nextElementSibling).toBe(caption)
    })
  })
})
