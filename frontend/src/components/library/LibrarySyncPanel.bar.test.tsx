import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, FileInspectionOverview, FolderScanOverview, LibrarySource, LibrarySourceList } from '@/lib/types'
import { member, owner, signInAs } from '@/test/auth-session'
import { inspectionOf, inspectionRunOf, runOf, scanOf, scanRunOf, sourceOf, sourcesOf } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-36 — the Sources card as a status bar: what it says of each library, the media folders and the
// inspection with the details closed, and how Details opens the rows the other panel tests read.

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
  return { user: userEvent.setup() }
}

function serve(list: LibrarySourceList) {
  vi.mocked(libraryApi.sources).mockResolvedValue(list)
}

/** The sources as `sourcesOf` gives them, with the scan or the inspection changed. */
function serveWith(scan: Partial<FolderScanOverview>, inspection: Partial<FileInspectionOverview>, ...items: LibrarySource[]) {
  serve({ items: items.length > 0 ? items : [sourceOf({})], scan: scanOf(scan), inspection: inspectionOf(inspection) })
}

/** The bar's items, once the sources have arrived. */
const findItems = async () => within(await screen.findByRole('list', { name: 'Status of the sources' })).getAllByRole('listitem')

/** The item that starts with `name`: a library's name, "Folders" or "Inspection". */
const findItem = async (name: string) => (await findItems()).find((item) => item.textContent?.startsWith(name))!

/** The text of the item's amber chips, in their order. */
const chipsOf = async (name: string) =>
  Array.from((await findItem(name)).querySelectorAll('.library-badge-warning'), (chip) => chip.textContent)

const queued = runOf({ status: 'Queued', startedUtc: null, finishedUtc: null, itemsRead: 0, itemsTotal: null })

describe('LibrarySyncPanel status bar', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    serve(sourcesOf(sourceOf({})))
  })

  it('tells each library, the media folders and the inspection in one item, with when each last ran', async () => {
    // Arrange
    serve(sourcesOf(
      sourceOf({}),
      sourceOf({ integrationId: 'other', name: 'Cartoons', latestRun: runOf({ finishedUtc: '2026-09-29T20:30:00Z' }) }),
    ))

    // Act
    renderPanel()

    // Assert
    const items = await findItems()
    expect(items.map((item) => item.querySelector('.library-bar-name')?.textContent))
      .toEqual(['Home Jellyfin', 'Cartoons', 'Folders', 'Inspection', undefined])
    expect(within(await findItem('Home Jellyfin')).getByText(/^Sep 30, 11:00\sAM$/)).toBeInTheDocument()
    expect(within(await findItem('Cartoons')).getByText(/^Sep 29, 11:30\sPM$/)).toBeInTheDocument()
    expect(within(await findItem('Folders')).getByText(/^Sep 30, 11:12\sAM$/)).toBeInTheDocument()
    expect(within(await findItem('Inspection')).getByText(/^Sep 30, 11:05\sAM$/)).toBeInTheDocument()
  })

  it('adds up the files and the room they take at the end', async () => {
    // Arrange
    const total = { sizeBytes: 7_195_143_962_624, fileCount: 882, nameCount: 882, unmeasuredCount: 0 }
    serveWith({}, { space: total })

    // Act
    renderPanel()

    // Assert
    const items = await findItems()
    expect(items.at(-1)).toHaveTextContent(/^882 files · 6\.5\d TiB$/)
  })

  it.each([['owner', owner], ['member', member]] as const)('marks what needs a look with amber chips, each on the item whose row warns of it, for the %s', async (_, signedIn) => {
    // Arrange
    // MAM-42 — the library's own files that couldn't be read on the library, the files in no library that
    // couldn't be read on Folders, and only what the hashing couldn't read on Inspection.
    serveWith(
      {
        lastFinishedRun: scanRunOf({ filesUnreported: 17, problemCount: 2 }),
        files: { inspectedCount: 16, problemCount: 1, notInspectedCount: 0, problem: null },
      },
      { lastFinishedRun: inspectionRunOf({ hashing: { candidates: 40, checked: 40, read: 2, problemCount: 2, problem: null } }) },
      sourceOf({ filesWithoutAppPathCount: 3, inspection: { inspectedCount: 867, problemCount: 2, notInspectedCount: 0, problem: null } }),
    )

    // Act
    renderPanel(signedIn)

    // Assert
    expect(await chipsOf('Home Jellyfin')).toEqual(['3 files without an app path', '2 couldn\'t be read'])
    expect(await chipsOf('Folders')).toEqual(['17 in no library', '2 folders not scanned', '1 couldn\'t be read'])
    expect(await chipsOf('Inspection')).toEqual(['2 couldn\'t be hashed'])
  })

  it('counts the files in no library now, not what the last scan found, leaving the ignored ones out', async () => {
    // Arrange
    // The scan found 17; since then the owner ignored one and a sync took two more on.
    serveWith(
      { lastFinishedRun: scanRunOf({ filesUnreported: 17 }), files: { inspectedCount: 12, problemCount: 0, notInspectedCount: 2, problem: null }, ignoredCount: 1 },
      {},
    )

    // Act
    renderPanel()

    // Assert
    expect(await chipsOf('Folders')).toEqual(['14 in no library'])
  })

  it('has no Inspection chip for the hashing of a run still under way', async () => {
    // Arrange
    // A running run's hashing has not finished: the row under Details tells its outcome only after.
    serveWith({}, { activeRun: inspectionRunOf({ status: 'Running', finishedUtc: null, hashing: { candidates: 40, checked: 20, read: 2, problemCount: 1, problem: null } }) })

    // Act
    renderPanel()

    // Assert
    expect(await chipsOf('Inspection')).toEqual([])
  })

  it.each<[string, string, string | undefined]>([
    ['17 in no library', 'In no library (17)', undefined],
    ['1 couldn\'t be read', 'Couldn\'t be read (1)', 'Problems'],
  ])('opens the files in no library on its tab from the Folders chip %s', async (chip, tab, view) => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue({
      items: [], totalCount: 0, page: 1, pageSize: 25, countedCount: 17, problemCount: 1, ignoredCount: 0,
    })
    serveWith({ files: { inspectedCount: 16, problemCount: 1, notInspectedCount: 0, problem: null } }, {})
    const { user } = renderPanel(member)

    // Act
    await user.click(within(await findItem('Folders')).getByRole('button', { name: chip }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Files in no library' })
    expect(await within(dialog).findByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true')
    expect(libraryApi.unreportedFiles).toHaveBeenCalledWith({ view, page: 1, pageSize: 25 })
  })

  it('has no chip for a library and folders that are as they should be', async () => {
    // Arrange
    // MAM-42 — ignored files alone are no warning.
    serveWith({ lastFinishedRun: scanRunOf({ filesUnreported: 0 }), files: { inspectedCount: 0, problemCount: 0, notInspectedCount: 0, problem: null }, ignoredCount: 2 }, {})

    // Act
    renderPanel()

    // Assert
    await findItems()
    expect(document.querySelector('.library-badge-warning')).toBeNull()
  })

  it.each<[string, Partial<LibrarySource>, string | RegExp, string | undefined]>([
    ['never synced', { latestRun: null, lastSucceededRun: null }, 'never synced', undefined],
    ['queued', { latestRun: queued }, 'queued', 'Queued'],
    ['starting', { latestRun: runOf({ status: 'Running', finishedUtc: null, itemsRead: 0, itemsTotal: null }) }, 'syncing…', 'Running'],
    ['reading', { latestRun: runOf({ status: 'Running', finishedUtc: null, itemsRead: 1_200, itemsTotal: 12_345 }) }, 'syncing 1,200 of 12,345', 'Running'],
    ['failed', { latestRun: runOf({ status: 'Failed', error: 'Jellyfin refused the API key.' }) }, 'sync failed', 'Failed'],
    ['switched off', { isEnabled: false }, 'disabled', undefined],
    ['of a type this version can\'t read', { canReadLibrary: false }, 'can\'t be read', undefined],
  ])('says a library that is %s', async (_, changes, detail, status) => {
    // Arrange
    serve(sourcesOf(sourceOf(changes)))

    // Act
    renderPanel()

    // Assert
    const item = await findItem('Home Jellyfin')
    expect(within(item).getByText(detail)).toBeInTheDocument()
    expect(item.dataset.status).toBe(status)
  })

  it.each<[string, Partial<FolderScanOverview>, string, string | undefined]>([
    ['never scanned', { lastFinishedRun: null }, 'never scanned', undefined],
    ['queued', { activeRun: scanRunOf({ status: 'Queued', filesFound: 0, filesUnreported: null }), hasQueuedRun: true }, 'queued', 'Queued'],
    ['running', { activeRun: scanRunOf({ status: 'Running', filesFound: 1_200, filesUnreported: null }) }, 'scanning… 1,200 files', 'Running'],
    ['running, one file found', { activeRun: scanRunOf({ status: 'Running', filesFound: 1, filesUnreported: null }) }, 'scanning… 1 file', 'Running'],
    ['failed', { lastFinishedRun: scanRunOf({ status: 'Failed', error: 'The disk is gone.' }) }, 'scan failed', 'Failed'],
  ])('says the media folders\' scan when it is %s', async (_, scan, detail, status) => {
    // Arrange
    serveWith(scan, {})

    // Act
    renderPanel()

    // Assert
    const item = await findItem('Folders')
    expect(within(item).getByText(detail)).toBeInTheDocument()
    expect(item.dataset.status).toBe(status)
  })

  it.each<[string, Partial<FileInspectionOverview>, string, string | undefined]>([
    ['never run', { lastFinishedRun: null }, 'never inspected', undefined],
    ['queued', { activeRun: inspectionRunOf({ status: 'Queued', filesTotal: null, filesChecked: 0 }), hasQueuedRun: true }, 'queued', 'Queued'],
    ['reading files', { activeRun: inspectionRunOf({ status: 'Running', filesTotal: 870, filesChecked: 100 }) }, 'inspecting 100 of 870', 'Running'],
    ['before its first count', { activeRun: inspectionRunOf({ status: 'Running', filesTotal: null, filesChecked: 0 }) }, 'inspecting…', 'Running'],
    [
      'hashing the files that could be copies',
      { activeRun: inspectionRunOf({ status: 'Running', hashing: { candidates: 30, checked: 5, read: 5, problemCount: 0, problem: null } }) },
      'hashing 5 of 30',
      'Running',
    ],
    ['failed', { lastFinishedRun: inspectionRunOf({ status: 'Failed', error: 'ffprobe is missing.' }) }, 'inspection failed', 'Failed'],
  ])('says the inspection when it is %s', async (_, inspection, detail, status) => {
    // Arrange
    serveWith({}, inspection)

    // Act
    renderPanel()

    // Assert
    const item = await findItem('Inspection')
    expect(within(item).getByText(detail)).toBeInTheDocument()
    expect(item.dataset.status).toBe(status)
  })

  it.each<[string, CurrentUser, boolean]>([
    ['gives the owner the way to the settings', owner, true],
    ['leaves a member the sentence alone', member, false],
  ])('puts one sentence in place of the folders and the inspection without a media folder, and %s', async (_, signedIn, link) => {
    // Arrange
    serveWith({ lastFinishedRun: null }, { hasAllowedRoots: false, lastFinishedRun: null })

    // Act
    renderPanel(signedIn)

    // Assert
    const items = await findItems()
    expect(items.map((item) => item.textContent)).toEqual([
      expect.stringMatching(/^Home Jellyfin/),
      link ? 'No media folder is allowed yet. Choose media folders' : 'No media folder is allowed yet.',
    ])
    if (link) expect(screen.getByRole('link', { name: 'Choose media folders' })).toHaveAttribute('href', '/settings/media-folders')
    else expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('opens the rows under Details in place of the bar, and Hide details brings the bar back', async () => {
    // Arrange
    const { user } = renderPanel()
    await findItems()
    const syncNowWhileClosed = screen.queryByRole('button', { name: 'Sync now' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Details' }))

    // Assert
    expect(syncNowWhileClosed).not.toBeInTheDocument()
    const hide = screen.getByRole('button', { name: 'Hide details' })
    expect(hide).toHaveAttribute('aria-expanded', 'true')
    expect(hide).toHaveAttribute('aria-controls', document.querySelector('.library-details')!.id)
    expect(screen.queryByRole('list', { name: 'Status of the sources' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Scan folders' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inspect files' })).toBeInTheDocument()
    expect(screen.getByText(/^The media libraries this list is read from/)).toBeInTheDocument()

    await user.click(hide)

    expect(await findItems()).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Sync now' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Details' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('keeps the sentence about a missing media library in view with the details closed', async () => {
    // Arrange
    serve(sourcesOf())

    // Act
    renderPanel()

    // Assert
    expect(await screen.findByText('No media library is connected yet. Add Jellyfin under Integrations, then sync it here.'))
      .toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open Integrations' })).toHaveAttribute('href', '/settings/integrations')
    expect(screen.queryByRole('button', { name: 'Sync now' })).not.toBeInTheDocument()
  })

  it('keeps the heading of the card for the page\'s outline and a screen reader', async () => {
    // Arrange
    renderPanel()

    // Act
    const heading = await screen.findByRole('heading', { level: 2, name: 'Sources' })

    // Assert
    expect(heading).toHaveClass('sr-only')
  })
})
