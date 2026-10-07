import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DuplicateGroupCard } from '@/components/duplicates/DuplicateGroupCard'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { aliensSha256, byteIdenticalGroupOf, daybreakFileName, duplicateFileOf, libraryFileOf, sameFileGroup, sameWorkGroup } from '@/test/duplicates'
import { windowIs } from '@/test/match-media'

vi.mock('@/lib/duplicates-api')

// MAM-21 — a group's files as the Library's version rows and table show a work's versions, each file a
// version named by the end of its path that tells it apart: its open row has its whole path, with its
// identity, hash, editions and the works holding it; the table compares them, with rows of the same facts.

function renderCard(group: LibraryDuplicateGroup) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DuplicateGroupCard group={group} isOwner onLeft={() => {}} />
    </QueryClientProvider>,
  )
  return userEvent.setup()
}

/** The rows of the group's files, which open them. */
const fileRows = () => within(screen.getByRole('list', { name: 'Files' })).getAllByRole('button')

/** The facts of the open row, as label and value pairs. */
function openFacts() {
  const facts = document.querySelector('.duplicates-facts')!
  return [...facts.children].map((pair) => [pair.querySelector('dt')!.textContent, pair.querySelector('dd')!.textContent])
}

/** The table's row headed `header`, as the text of each file's cell. */
function tableRow(header: string) {
  const row = screen.getByRole('rowheader', { name: header }).closest('tr')!
  return within(row).getAllByRole('cell').map((cell) => cell.textContent)
}

describe('DuplicateGroupCard files', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('lists the files as closed rows named by their files, in the group\'s order', () => {
    // Arrange
    const group = sameFileGroup

    // Act
    renderCard(group)

    // Assert
    const rows = fileRows()
    expect(rows.map((row) => row.getAttribute('aria-expanded'))).toEqual(['false', 'false'])
    expect(rows[0]).toHaveTextContent(/^Aliens \(1986\) \[Theatrical Cut\] BDRip 1080p H\.265\.mkv/)
    expect(rows[1]).toHaveTextContent(/^\[02\.1\] Aliens \(1986\) \[Theatrical Cut, Fundamental Collection\]/)
  })

  it('tells two files of one name apart by the folder their open rows show', async () => {
    // Arrange
    const user = renderCard(sameWorkGroup)
    const [hd, uhd] = fileRows()

    // Act
    await user.click(hd)
    const hdFolder = screen.getByText('/media/tv/Yellowstone/Season 1')
    await user.click(uhd)

    // Assert
    expect(hd).toHaveTextContent(daybreakFileName)
    expect(uhd).toHaveTextContent(daybreakFileName)
    expect(hdFolder).toHaveClass('library-file-folder')
    expect(screen.getByText('/media/tv-4k/Yellowstone/Season 1')).toHaveClass('library-file-folder')
    expect(screen.queryByText('/media/tv/Yellowstone/Season 1')).not.toBeInTheDocument()
  })

  it('shows an open file\'s identity, hash, editions and the works holding it, with their ids', async () => {
    // Arrange
    const user = renderCard(sameWorkGroup)

    // Act
    await user.click(fileRows()[1])

    // Assert
    expect(openFacts()).toEqual([
      ['Identity', 'Device 2049 · inode 4242 · 1 link'],
      ['SHA-256', 'Not hashed'],
      ['Editions', 'None in its names'],
      ['Held by', 'DaybreakEpisode2018Of ЄллоустоунSeason 1 · Episode 1From Home JellyfinIMDb tt7608156TVDB 6468512'],
    ])
  })

  it('says a file the app hasn\'t looked at yet has no identity yet', async () => {
    // Arrange
    const user = renderCard(sameWorkGroup)

    // Act
    await user.click(fileRows()[0])

    // Assert
    expect(openFacts()[0]).toEqual(['Identity', 'Not looked at yet'])
  })

  it('shows an open file\'s hash, its editions and the version name of the work holding it', async () => {
    // Arrange
    const user = renderCard(byteIdenticalGroupOf())

    // Act
    await user.click(fileRows()[1])

    // Assert
    expect(openFacts()).toEqual([
      ['Identity', 'Device 2049 · inode 1311 · 1 link'],
      ['SHA-256', aliensSha256],
      ['Editions', 'Theatrical cut'],
      ['Held by', 'ЧужіMovie1986Version: Theatrical CutFrom Home JellyfinIMDb tt0090605TMDb 679'],
    ])
  })

  it('says no library reports a file the folder scan found', async () => {
    // Arrange
    const user = renderCard(sameFileGroup)

    // Act
    await user.click(fileRows()[0])

    // Assert
    expect(openFacts()[3]).toEqual(['Held by', 'No library reports this file; the folder scan found it.'])
  })

  it('compares the files in a table, their folders and facts a row each, once the table is picked', async () => {
    // Arrange
    const user = renderCard(sameWorkGroup)

    // Act
    await user.click(screen.getByRole('radio', { name: 'Table' }))

    // Assert
    const table = screen.getByRole('table', { name: 'Files' })
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent))
      .toEqual([`tv/…/${daybreakFileName}`, `tv-4k/…/${daybreakFileName}`])
    expect(tableRow('Folder')).toEqual(['/media/tv/Yellowstone/Season 1', '/media/tv-4k/Yellowstone/Season 1'])
    expect(tableRow('Identity')).toEqual(['Not looked at yet', 'Device 2049 · inode 4242 · 1 link'])
    expect(tableRow('SHA-256')).toEqual(['Not hashed', 'Not hashed'])
    expect(tableRow('Editions')).toEqual(['None in its names', 'None in its names'])
    expect(tableRow('Held by')).toEqual([
      'DaybreakEpisode2018Of ЄллоустоунSeason 1 · Episode 1From Home JellyfinIMDb tt7608156',
      'DaybreakEpisode2018Of ЄллоустоунSeason 1 · Episode 1From Home JellyfinIMDb tt7608156TVDB 6468512',
    ])
    expect(screen.getByText(/^Size, file, folder and who holds it come from the library or the folder scan/)).toBeInTheDocument()
    expect(localStorage.getItem('mam:versionsView')).toBe('table')
  })

  it('keeps the rows on a narrow screen, where the switch is hidden', () => {
    // Arrange
    windowIs(true)
    localStorage.setItem('mam:versionsView', 'table')

    // Act
    renderCard(sameWorkGroup)

    // Assert
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Files' })).toBeInTheDocument()
  })

  it('offers no table for more files than it compares', () => {
    // Arrange
    const files = Array.from({ length: 7 }, (_, index) => duplicateFileOf({
      file: libraryFileOf({ id: `7a1e0000-0000-4000-8000-00000000060${index}`, servicePath: `/media/copies/${index}/Aliens.mkv` }),
    }))
    localStorage.setItem('mam:versionsView', 'table')

    // Act
    renderCard({ ...sameFileGroup, files })

    // Assert
    expect(screen.queryByRole('radiogroup', { name: 'Show versions as' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(fileRows()).toHaveLength(7)
  })
})
