import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, FileInspectionOverview, FolderScanOverview, LibrarySource, LibrarySpace } from '@/lib/types'
import { member, owner, signInAs } from '@/test/auth-session'
import { inspectionOf, scanOf, sourceOf } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-19 — how much room the files take on disk, each file counted once however many names lead to
// it (ADR 0018): a line in every source row and in the Media folders row, and the total of every
// file on the inspection row. LibrarySyncPanel.hashing.test.tsx covers the hashing on that row.

function renderPanel(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibrarySyncPanel />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/** The sources answer every request with `sources`, the scan and the inspection as the changes set them. */
function serve({ sources = [sourceOf({})], scan = {}, inspection = {} }: {
  sources?: LibrarySource[]
  scan?: Partial<FolderScanOverview>
  inspection?: Partial<FileInspectionOverview>
}) {
  vi.mocked(libraryApi.sources).mockResolvedValue({ items: sources, inspection: inspectionOf(inspection), scan: scanOf(scan) })
}

/** The row of the source named `name`, once the sources have arrived. */
const findRowOf = async (name: string) => (await screen.findByText(name)).closest('li')!

/** The Media folders row, once the sources have arrived. */
const findScanRow = () => findRowOf('Media folders')

/** The inspection's row under the sources, once they have arrived. */
async function findInspectionRow() {
  await screen.findByText('Media folders')
  return document.querySelector<HTMLElement>('.library-inspection')!
}

/** Nothing measured: the names are there, but the app hasn't read a file behind them yet. */
const unmeasured: LibrarySpace = { sizeBytes: 0, fileCount: 0, nameCount: 0, unmeasuredCount: 870 }

describe('LibrarySyncPanel space', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    serve({})
  })

  it('counts a file two hardlinks lead to once in its source\'s row, under the files line', async () => {
    // Arrange — one 1.5 GiB file, reported under two names.
    serve({ sources: [sourceOf({ space: { sizeBytes: 1_610_612_736, fileCount: 1, nameCount: 2, unmeasuredCount: 0 } })] })

    // Act
    renderPanel()

    // Assert
    const row = within(await findRowOf('Home Jellyfin'))
    const line = row.getByText('Takes 1.5 GiB on disk: 1 file. 1 more name leads to a file counted once.')
    expect(line).toHaveClass('settings-muted')
    expect(line.previousElementSibling).toHaveTextContent(/^Files: 870 inspected$/)
  })

  it.each<[string, LibrarySpace, string]>([
    ['one name each', { sizeBytes: 7_146_825_580_544, fileCount: 870, nameCount: 870, unmeasuredCount: 0 }, 'Takes 6.5 TiB on disk: 870 files.'],
    ['names past one per file', { sizeBytes: 7_146_825_580_544, fileCount: 870, nameCount: 873, unmeasuredCount: 0 }, 'Takes 6.5 TiB on disk: 870 files. 3 more names lead to files counted once.'],
    ['one file not measured yet', { sizeBytes: 3_891_719_630, fileCount: 1, nameCount: 1, unmeasuredCount: 1 }, 'Takes 3.62 GiB on disk: 1 file. 1 file not measured yet.'],
    ['many files not measured yet', { sizeBytes: 1_023, fileCount: 1, nameCount: 1, unmeasuredCount: 1_217 }, 'Takes 1,023 B on disk: 1 file. 1,217 files not measured yet.'],
    [
      'both',
      { sizeBytes: 1_288_490_188_800, fileCount: 1_200, nameCount: 1_202, unmeasuredCount: 3 },
      'Takes 1.17 TiB on disk: 1,200 files. 2 more names lead to files counted once. 3 files not measured yet.',
    ],
  ])('says how much room a source\'s files take: %s', async (_, space, text) => {
    // Arrange
    serve({ sources: [sourceOf({ space })] })

    // Act
    renderPanel()

    // Assert
    expect(within(await findRowOf('Home Jellyfin')).getByText(text)).toHaveClass('settings-muted')
  })

  it('shows each source its own room', async () => {
    // Arrange
    serve({
      sources: [
        sourceOf({}),
        sourceOf({ integrationId: 'cartoons', name: 'Cartoons', space: { sizeBytes: 183_500_800, fileCount: 1, nameCount: 1, unmeasuredCount: 0 } }),
      ],
    })

    // Act
    renderPanel()

    // Assert
    expect(within(await findRowOf('Home Jellyfin')).getByText('Takes 6.5 TiB on disk: 870 files.')).toBeInTheDocument()
    expect(within(await findRowOf('Cartoons')).getByText('Takes 175 MiB on disk: 1 file.')).toBeInTheDocument()
  })

  it('shows no space line in a row whose files the app has not measured yet', async () => {
    // Arrange
    serve({
      sources: [sourceOf({ inspection: { inspectedCount: 0, problemCount: 0, notInspectedCount: 870, problem: null }, space: unmeasured })],
      scan: { space: { ...unmeasured, unmeasuredCount: 12 } },
      inspection: { space: { ...unmeasured, unmeasuredCount: 882 } },
    })

    // Act
    renderPanel()

    // Assert
    expect(within(await findRowOf('Home Jellyfin')).getByText(/^Files: 870 not inspected yet$/)).toBeInTheDocument()
    expect(screen.queryByText(/on disk/)).not.toBeInTheDocument()
  })

  it('says how much room the files in no library take in the Media folders row, under its files line', async () => {
    // Arrange
    serve({ scan: { space: { sizeBytes: 48_318_382_080, fileCount: 12, nameCount: 14, unmeasuredCount: 0 } } })

    // Act
    renderPanel()

    // Assert
    const line = within(await findScanRow()).getByText('Takes 45 GiB on disk: 12 files. 2 more names lead to files counted once.')
    expect(line).toHaveClass('settings-muted')
    expect(line.previousElementSibling).toHaveTextContent(/^Files in no library: 12 inspected$/)
  })

  it('puts the room after the warnings that explain the files line, in a source row and in the Media folders row', async () => {
    // Arrange — the owner's sentence for the first file that couldn't be read belongs right under the
    // count it explains, so the room comes after it.
    const sourceProblem = 'ffprobe couldn’t read “/media/movies/Broken.mkv” as media.'
    const scanProblem = 'ffprobe couldn’t read “/media/home/Broken.mkv” as media.'
    serve({
      sources: [sourceOf({ inspection: { inspectedCount: 869, problemCount: 1, notInspectedCount: 0, problem: sourceProblem } })],
      scan: { files: { inspectedCount: 11, problemCount: 1, notInspectedCount: 0, problem: scanProblem } },
    })

    // Act
    renderPanel()

    // Assert
    const source = within(await findRowOf('Home Jellyfin')).getByText('Takes 6.5 TiB on disk: 870 files.')
    expect(source.previousElementSibling).toHaveTextContent(sourceProblem)
    const scan = within(await findScanRow()).getByText('Takes 45 GiB on disk: 12 files.')
    expect(scan.previousElementSibling).toHaveTextContent(scanProblem)
  })

  it('says how much room every file takes on the inspection row, under the hashing line', async () => {
    // Arrange — a library name and a scan name of one file count it once, as two names.
    serve({ inspection: { space: { sizeBytes: 7_195_143_962_624, fileCount: 882, nameCount: 883, unmeasuredCount: 5 } } })

    // Act
    renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    const line = row.getByText('All files take 6.54 TiB on disk: 882 files. 1 more name leads to a file counted once. 5 files not measured yet.')
    expect(line).toHaveClass('settings-muted')
    expect(line.previousElementSibling)
      .toHaveTextContent(/^No file needed a hash: no two files of one size could be copies of each other\.$/)
  })

  it('says how much room a single file takes on the inspection row', async () => {
    // Arrange
    serve({ inspection: { space: { sizeBytes: 3_891_719_630, fileCount: 1, nameCount: 1, unmeasuredCount: 0 } } })

    // Act
    renderPanel()

    // Assert
    expect(within(await findInspectionRow()).getByText('All files take 3.62 GiB on disk: 1 file.')).toBeInTheDocument()
  })

  it('leaves the total out while the inspection is off, and keeps the rows\' own', async () => {
    // Arrange — what the app measured before the folders were removed stays in the rows.
    serve({ inspection: { hasAllowedRoots: false } })

    // Act
    renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    expect(row.getByRole('status')).toHaveTextContent(/^File inspection is off/)
    expect(row.queryByText(/on disk/)).not.toBeInTheDocument()
    expect(within(await findRowOf('Home Jellyfin')).getByText('Takes 6.5 TiB on disk: 870 files.')).toBeInTheDocument()
    expect(within(await findScanRow()).getByText('Takes 45 GiB on disk: 12 files.')).toBeInTheDocument()
  })

  it.each<[string, CurrentUser]>([
    ['the owner', owner],
    ['everyone else', member],
  ])('shows %s the same room in every row', async (_, signedIn) => {
    // Arrange — the room isn't the owner's only: it names no path.
    serve({
      sources: [sourceOf({ space: { sizeBytes: 1_610_612_736, fileCount: 1, nameCount: 2, unmeasuredCount: 0 } })],
      scan: { space: { sizeBytes: 48_318_382_080, fileCount: 12, nameCount: 12, unmeasuredCount: 1 } },
      inspection: { space: { sizeBytes: 49_928_994_816, fileCount: 13, nameCount: 14, unmeasuredCount: 1 } },
    })

    // Act
    renderPanel(signedIn)

    // Assert
    expect(within(await findRowOf('Home Jellyfin')).getByText('Takes 1.5 GiB on disk: 1 file. 1 more name leads to a file counted once.'))
      .toBeInTheDocument()
    expect(within(await findScanRow()).getByText('Takes 45 GiB on disk: 12 files. 1 file not measured yet.')).toBeInTheDocument()
    expect(within(await findInspectionRow())
      .getByText('All files take 46.5 GiB on disk: 13 files. 1 more name leads to a file counted once. 1 file not measured yet.'))
      .toBeInTheDocument()
  })
})
