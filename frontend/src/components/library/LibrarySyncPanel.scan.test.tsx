import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, FileInspectionOverview, FolderScanOverview, FolderScanRun, LibrarySource } from '@/lib/types'
import { openSourceDetails } from '@/test/sources-details'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { inspectionOf, scanOf, scanRunOf, sourceOf, sourcesOf } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-18 — the Media folders row on the sources panel: how the scan of the media folders for the
// files no library reported stands, the folders it couldn't go into, the names it stored by what the
// inspection found, and Scan folders.

async function renderPanel(signedIn: CurrentUser = owner, { details = true } = {}) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibrarySyncPanel />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  const user = userEvent.setup()
  if (details) await openSourceDetails(user)
  return { client, user }
}

/**
 * The sources answer every request with Home Jellyfin, the scan as `changes` set it and the
 * inspection as `inspection` sets it — whose `hasAllowedRoots` is the row's too.
 */
function serveScan(changes: Partial<FolderScanOverview>, inspection: Partial<FileInspectionOverview> = {}, ...sources: LibrarySource[]) {
  const list = sourcesOf(...(sources.length > 0 ? sources : [sourceOf({})]))
  vi.mocked(libraryApi.sources).mockResolvedValue({ ...list, inspection: inspectionOf(inspection), scan: scanOf(changes) })
}

/** The Media folders row, once the sources have arrived. */
const findScanRow = async () => (await screen.findByText('Media folders')).closest('li')!

const queued = scanRunOf({
  id: '5ca00000-0000-4000-8000-000000000002',
  status: 'Queued',
  createdUtc: '2026-09-30T09:00:00Z',
  startedUtc: null,
  finishedUtc: null,
  filesFound: 0,
  filesUnreported: null,
})
const running = scanRunOf({ id: '5ca00000-0000-4000-8000-000000000003', status: 'Running', finishedUtc: null, filesFound: 120, filesUnreported: null })
const failed = scanRunOf({
  status: 'Failed',
  filesFound: 300,
  filesUnreported: null,
  error: 'The scan stopped because of an error in the app. Its log says more.',
})
const linkOut = '“/media/films/Old” is a link that leads out of the media folders, so the app doesn\'t follow it.'

describe('LibrarySyncPanel media folders scan', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    serveScan({})
  })

  it('shows the Media folders row after the sources, in their list', async () => {
    // Arrange
    serveScan({}, {}, sourceOf({}), sourceOf({ integrationId: 'cartoons', name: 'Cartoons' }))

    // Act
    await renderPanel()

    // Assert
    const row = await findScanRow()
    expect(row).toHaveClass('library-source', 'library-scan')
    const list = row.parentElement!
    expect(list).toHaveClass('library-source-list')
    expect(list.children).toHaveLength(3)
    expect(list.lastElementChild).toBe(row)
    expect(document.querySelector('.library-inspection')).not.toBe(row)
  })

  it('shows the row without a media library: the sentence, then the row alone in its list, then the inspection', async () => {
    // Arrange
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf())

    // Act
    await renderPanel()

    // Assert
    const row = await findScanRow()
    const content = row.closest('.library-sources')!
    expect(Array.from(content.children, (child) => child.className))
      .toEqual(['settings-empty', 'library-bar', 'library-details'])
    expect(Array.from(row.closest('.library-details')!.children, (child) => child.className))
      .toEqual(['library-source-list', 'library-source library-inspection'])
    expect(row.parentElement!.children).toHaveLength(1)
    expect(within(content.firstElementChild as HTMLElement).getByText(/^No media library is connected yet\./))
      .toBeInTheDocument()
    expect(within(row).getByRole('status')).toHaveTextContent(/^Scanned /)
  })

  it.each<[string, Partial<FolderScanOverview>, string | RegExp, FolderScanRun['status'] | undefined]>([
    ['never run', { lastFinishedRun: null }, 'The media folders have not been scanned yet.', undefined],
    ['queued', { activeRun: queued, hasQueuedRun: true }, 'Waiting to scan the media folders…', 'Queued'],
    ['running, one file found', { activeRun: { ...running, filesFound: 1 } }, 'Scanning the media folders… 1 file found', 'Running'],
    ['running, many files found', { activeRun: { ...running, filesFound: 1_200 } }, 'Scanning the media folders… 1,200 files found', 'Running'],
    // MAM-42 — the files in no library are a line of their own, counted now.
    ['finished', {}, /^Scanned Sep 30, 2026, 11:12\sAM: 1,394 files\.$/, 'Succeeded'],
    [
      'finished with one file',
      { lastFinishedRun: scanRunOf({ filesFound: 1, filesUnreported: 1 }) },
      /^Scanned Sep 30, 2026, 11:12\sAM: 1 file\.$/,
      'Succeeded',
    ],
    [
      'failed',
      { lastFinishedRun: failed },
      /^Scan failed Sep 30, 2026, 11:12\sAM: The scan stopped because of an error in the app\. Its log says more\.$/,
      'Failed',
    ],
  ])('shows a scan that is %s on the row\'s status line', async (_, changes, text, status) => {
    // Arrange
    serveScan(changes)

    // Act
    await renderPanel()

    // Assert
    const line = within(await findScanRow()).getByRole('status')
    expect(line).toHaveTextContent(text)
    expect(line).toHaveClass('library-source-status')
    if (status === undefined) expect(line).not.toHaveAttribute('data-status')
    else expect(line).toHaveAttribute('data-status', status)
  })

  it('tells everyone else only that the last scan failed', async () => {
    // Arrange — the API leaves the sentence out for anyone but the owner.
    serveScan({ lastFinishedRun: { ...failed, error: null } })

    // Act
    await renderPanel(member)

    // Assert
    expect(within(await findScanRow()).getByRole('status')).toHaveTextContent(/^The last scan failed\. The owner can see why\.$/)
  })

  it('says a scan waits behind the running one and keeps Scan folders disabled until it starts', async () => {
    // Arrange
    serveScan({ activeRun: running, hasQueuedRun: true })

    // Act
    await renderPanel()

    // Assert
    // The running one is what happens now; the queued one only adds "and again after this".
    const row = within(await findScanRow())
    expect(row.getByRole('status')).toHaveTextContent(
      /^Scanning the media folders… 120 files found; another scan starts after this one\.$/)
    const button = row.getByRole('button', { name: 'Scan folders' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'A scan is already waiting to start.')
  })

  it('says scanning is off without a media folder, and links the owner to Media folders', async () => {
    // Arrange — checked first: a scan that finished before the folders were removed stays unsaid.
    serveScan({ lastFinishedRun: scanRunOf({ problemCount: 2, problem: linkOut }) }, { hasAllowedRoots: false })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findScanRow())
    const line = row.getByRole('status')
    expect(line).toHaveTextContent(/^Scanning is off: no media folder is allowed yet\. Choose media folders$/)
    expect(line).toHaveClass('library-source-warning')
    expect(row.getByRole('link', { name: 'Choose media folders' })).toHaveAttribute('href', '/settings/media-folders')
    expect(row.queryByText(/wasn't scanned|weren't scanned/)).not.toBeInTheDocument()
  })

  it('tells everyone else that scanning is off, without a link', async () => {
    // Arrange
    serveScan({}, { hasAllowedRoots: false })

    // Act
    await renderPanel(member)

    // Assert
    const row = within(await findScanRow())
    expect(row.getByRole('status')).toHaveTextContent(/^Scanning is off: no media folder is allowed yet\.$/)
    expect(row.queryByRole('link')).not.toBeInTheDocument()
  })

  it('warns the owner about the folders a scan did not go into, with the first one\'s sentence', async () => {
    // Arrange
    serveScan({ lastFinishedRun: scanRunOf({ problemCount: 3, problem: linkOut }) })

    // Act
    await renderPanel()

    // Assert
    const warning = within(await findScanRow()).getByText(/weren't scanned/).closest('p')!
    expect(warning).toHaveClass('library-source-warning')
    expect(warning).toHaveTextContent(`3 folders or links weren't scanned: ${linkOut}`)
  })

  it('tells everyone else how many folders a scan did not go into and to ask the owner which', async () => {
    // Arrange — the sentence quotes the owner's paths, so the API leaves it out.
    serveScan({ lastFinishedRun: scanRunOf({ problemCount: 1, problem: null }) })

    // Act
    await renderPanel(member)

    // Assert
    const warning = within(await findScanRow()).getByText(/wasn't scanned/).closest('p')!
    expect(warning).toHaveClass('library-source-warning')
    expect(warning).toHaveTextContent(/^1 folder or link wasn't scanned\. Ask the owner which\.$/)
  })

  it('shows no problem warning for a scan that had none', async () => {
    // Arrange
    serveScan({})

    // Act
    await renderPanel()

    // Assert
    const row = within(await findScanRow())
    expect(row.queryByText(/wasn't scanned|weren't scanned/)).not.toBeInTheDocument()
    expect(row.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('counts the names in no library by what the last look found, with the owner\'s first problem', async () => {
    // Arrange
    const problem = '“/media/films/Rambo (1982).mkv” is outside the media folders the app may read.'
    serveScan({ files: { inspectedCount: 14, problemCount: 2, notInspectedCount: 1_203, problem } })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findScanRow())
    expect(row.getByText('Files in no library: 14 inspected · 2 couldn\'t be read · 1,203 not inspected yet'))
      .toHaveClass('settings-muted')
    // MAM-42 — with how many, as the bar's chip counts them.
    expect(row.getByText(`2 files couldn't be read: ${problem}`).closest('p')).toHaveClass('library-source-warning')
  })

  it.each<[string, CurrentUser, string | null, string]>([
    ['the owner, with the first file\'s sentence', owner, 'ffprobe couldn\'t read “/media/Adobe Premiere Rush 2.10.0.30.iso” as media.', '1 file couldn\'t be read: ffprobe couldn\'t read “/media/Adobe Premiere Rush 2.10.0.30.iso” as media.'],
    ['a member, who reads that the owner can see why', member, null, '1 file couldn\'t be read. The owner can see why.'],
  ])('warns of the files in no library and of those that couldn\'t be read, each line with Show files, for %s', async (_, signedIn, problem, problemLine) => {
    // Arrange
    // MAM-42 — the bar shows "17 in no library" and "1 couldn't be read" on Folders: the row says both.
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue({
      items: [], totalCount: 0, page: 1, pageSize: 25, countedCount: 17, problemCount: 1, ignoredCount: 0,
    })
    serveScan({ lastFinishedRun: scanRunOf({ filesUnreported: 17 }), files: { inspectedCount: 16, problemCount: 1, notInspectedCount: 0, problem } })
    const { user } = await renderPanel(signedIn)

    // Act
    const row = within(await findScanRow())
    const unreported = row.getByText('17 files are in no library.').closest('p')!
    const problems = row.getByText(problemLine).closest('p')!
    // Before the click: the open dialog hides the page from the accessibility tree.
    const unreportedButton = within(unreported).getByRole('button', { name: 'Show files' })
    await user.click(within(problems).getByRole('button', { name: 'Show files' }))

    // Assert
    expect(unreported).toHaveClass('library-source-warning')
    expect(unreportedButton).toHaveClass('library-link')
    expect(problems).toHaveClass('library-source-warning')
    const dialog = await screen.findByRole('dialog', { name: 'Files in no library' })
    expect(await within(dialog).findByRole('tab', { name: 'Couldn\'t be read (1)' })).toHaveAttribute('aria-selected', 'true')
  })

  it('counts the ignored files in the files line and, with none counted, offers them there', async () => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue({
      items: [], totalCount: 0, page: 1, pageSize: 25, countedCount: 0, problemCount: 0, ignoredCount: 1,
    })
    serveScan({ files: { inspectedCount: 0, problemCount: 0, notInspectedCount: 0, problem: null }, ignoredCount: 1 })
    const { user } = await renderPanel()

    // Act
    const row = within(await findScanRow())
    const line = row.getByText(/^Files in no library: 1 ignored/)
    await user.click(within(line).getByRole('button', { name: 'Show files' }))

    // Assert
    // Nothing to warn of: an ignored file is the owner's decision.
    expect(line).toHaveClass('settings-muted')
    expect(row.queryByText(/in no library\.$/)).not.toBeInTheDocument()
    const dialog = await screen.findByRole('dialog', { name: 'Files in no library' })
    expect(await within(dialog).findByRole('tab', { name: 'Ignored (1)' })).toHaveAttribute('aria-selected', 'true')
  })

  it.each<[string, FolderScanOverview['files'], string]>([
    ['inspected only', { inspectedCount: 1, problemCount: 0, notInspectedCount: 0, problem: null }, 'Files in no library: 1 inspected'],
    ['not inspected yet', { inspectedCount: 0, problemCount: 0, notInspectedCount: 3, problem: null }, 'Files in no library: 3 not inspected yet'],
  ])('leaves the parts that are zero out of the files line when they are %s', async (_, files, line) => {
    // Arrange
    serveScan({ files })

    // Act
    await renderPanel(member)

    // Assert
    expect(within(await findScanRow()).getByText(line)).toBeInTheDocument()
  })

  it('shows no files line while the scans stored no name', async () => {
    // Arrange
    serveScan({ lastFinishedRun: null, files: { inspectedCount: 0, problemCount: 0, notInspectedCount: 0, problem: null } })

    // Act
    await renderPanel()

    // Assert
    expect(within(await findScanRow()).queryByText(/^Files in no library:/)).not.toBeInTheDocument()
  })

  it.each<[string, CurrentUser, Partial<FolderScanOverview>, Partial<FileInspectionOverview>, string]>([
    ['for anyone but the owner', member, {}, {}, 'Only the owner can scan the media folders.'],
    [
      'without a media folder',
      owner,
      {},
      { hasAllowedRoots: false },
      'No media folder is allowed yet. Choose the folders the app may read under Media folders first.',
    ],
    ['while a scan waits to start', owner, { activeRun: queued, hasQueuedRun: true }, {}, 'A scan is already waiting to start.'],
  ])('keeps Scan folders disabled %s, and says why', async (_, signedIn, changes, inspection, reason) => {
    // Arrange
    serveScan(changes, inspection)
    const { user } = await renderPanel(signedIn)
    const button = within(await findScanRow()).getByRole('button', { name: 'Scan folders' })

    // Act
    await user.click(button)

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', reason)
    expect(libraryApi.startScan).not.toHaveBeenCalled()
  })

  it('keeps Scan folders enabled while a scan runs, its icon spinning', async () => {
    // Arrange — a running scan doesn't stop another: the new one queues behind it.
    serveScan({ activeRun: running })

    // Act
    await renderPanel()

    // Assert
    const button = within(await findScanRow()).getByRole('button', { name: 'Scan folders' })
    expect(button).toBeEnabled()
    expect(button).toHaveAttribute('title', 'Scan folders')
    expect(button.querySelector('.animate-spin')).not.toBeNull()
  })

  it('starts a scan for the owner, shows it waiting at once and asks for the sources again', async () => {
    // Arrange
    let answer: (run: FolderScanRun) => void = () => {}
    vi.mocked(libraryApi.startScan).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { user } = await renderPanel()
    await user.click(within(await findScanRow()).getByRole('button', { name: 'Scan folders' }))
    const startingButton = screen.getByRole('button', { name: 'Starting…' })
    const disabledWhileStarting = (startingButton as HTMLButtonElement).disabled
    // The next answer of the sources never comes, so what shows is what the start put in the cache.
    vi.mocked(libraryApi.sources).mockReturnValue(new Promise(() => {}))

    // Act
    await act(async () => answer(queued))

    // Assert
    const row = within(await findScanRow())
    expect(await row.findByText('Waiting to scan the media folders…')).toBeInTheDocument()
    expect(disabledWhileStarting).toBe(true)
    // Its own button, not a Show files: the label says Starting… until the sources asked for again come back, which they never do here.
    expect(row.getByRole('button', { name: /^(Scan folders|Starting…)$/ }).parentElement).toHaveAttribute('title', 'A scan is already waiting to start.')
    expect(libraryApi.startScan).toHaveBeenCalledExactlyOnceWith()
    await vi.waitFor(() => expect(libraryApi.sources).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a start during a running scan as the scan that follows it, at once', async () => {
    // Arrange
    serveScan({ activeRun: running })
    vi.mocked(libraryApi.startScan).mockResolvedValue(queued)
    const { user } = await renderPanel()
    const row = within(await findScanRow())
    // The next answer of the sources never comes, so what shows is what the start put in the cache.
    vi.mocked(libraryApi.sources).mockReturnValue(new Promise(() => {}))

    // Act
    await user.click(row.getByRole('button', { name: 'Scan folders' }))

    // Assert
    expect(await row.findByText('Scanning the media folders… 120 files found; another scan starts after this one.'))
      .toBeInTheDocument()
    expect(libraryApi.startScan).toHaveBeenCalledExactlyOnceWith()
  })

  it('shows a refused start as the API sentence under the Media folders row', async () => {
    // Arrange — the folders were removed on another device; the refetch turns the row off.
    const sentence = 'No media folders are allowed yet. Add the folders the app may read under Media folders in the user menu, then scan again.'
    vi.mocked(libraryApi.startScan).mockImplementation(async () => {
      serveScan({}, { hasAllowedRoots: false })
      throw apiError(409, sentence)
    })
    const { user } = await renderPanel()

    // Act
    await user.click(within(await findScanRow()).getByRole('button', { name: 'Scan folders' }))

    // Assert
    const row = within(await findScanRow())
    const refusal = await row.findByRole('alert')
    expect(refusal).toHaveTextContent(sentence)
    // A red status line like a failed run's, not a banner above the list.
    expect(refusal).toHaveClass('library-source-status')
    expect(refusal).toHaveAttribute('data-status', 'Failed')
    expect(await row.findByText(/^Scanning is off/)).toBeInTheDocument()
    expect(screen.getAllByRole('alert')).toHaveLength(1)
  })

  it('names the failure when a refused start carries no sentence', async () => {
    // Arrange
    vi.mocked(libraryApi.startScan).mockRejectedValue(apiError(500))
    const { user } = await renderPanel()

    // Act
    await user.click(within(await findScanRow()).getByRole('button', { name: 'Scan folders' }))

    // Assert
    const row = within(await findScanRow())
    expect(await row.findByRole('alert')).toHaveTextContent(/^Could not start the scan of the media folders\.$/)
    expect(row.getByRole('button', { name: 'Scan folders' })).toBeEnabled()
  })
})
