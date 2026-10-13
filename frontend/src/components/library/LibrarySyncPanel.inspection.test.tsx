import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, FileInspectionOverview, FileInspectionRun, LibrarySource } from '@/lib/types'
import { openSourceDetails } from '@/test/sources-details'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { inspectionOf, inspectionRunOf, sourceOf, sourcesOf } from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-9 — the file inspection on the sources panel: each source's counts, the panel's inspection row
// and Inspect files.

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

/** The sources answer every request with Home Jellyfin and the inspection as `changes` set it. */
function serveInspection(changes: Partial<FileInspectionOverview>, ...sources: LibrarySource[]) {
  const list = sourcesOf(...(sources.length > 0 ? sources : [sourceOf({})]))
  vi.mocked(libraryApi.sources).mockResolvedValue({ ...list, inspection: inspectionOf(changes) })
}

/** The row of the source named `name`. */
const rowOf = (name: string) => screen.getByText(name).closest('li')!

/** The inspection's row under the sources, once they have arrived. */
async function findInspectionRow() {
  await screen.findByText('Home Jellyfin')
  return document.querySelector<HTMLElement>('.library-inspection')!
}

const running = inspectionRunOf({ status: 'Running', finishedUtc: null, filesTotal: 870, filesChecked: 120, filesProbed: 40 })
const queued = inspectionRunOf({
  id: '1a000000-0000-4000-8000-000000000002',
  trigger: 'Requested',
  status: 'Queued',
  requestedByUserName: 'vasyl',
  createdUtc: '2026-09-30T09:00:00Z',
  startedUtc: null,
  finishedUtc: null,
  filesTotal: null,
  filesChecked: 0,
  filesProbed: 0,
})
const failed = inspectionRunOf({
  status: 'Failed',
  filesTotal: null,
  filesChecked: 0,
  filesProbed: 0,
  error: 'No media folders are allowed yet. Add the folders the app may read under Media folders in the user menu, then inspect again.',
})

describe('LibrarySyncPanel file inspection', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    serveInspection({})
  })

  it('counts each source\'s files by what the last look found, the parts that are not zero only', async () => {
    // Arrange
    serveInspection(
      {},
      sourceOf({ inspection: { inspectedCount: 650, problemCount: 3, notInspectedCount: 1_217, problem: null } }),
      sourceOf({ integrationId: 'cartoons', name: 'Cartoons', inspection: { inspectedCount: 1, problemCount: 0, notInspectedCount: 0, problem: null } }),
      sourceOf({ integrationId: 'empty', name: 'Empty', inspection: { inspectedCount: 0, problemCount: 0, notInspectedCount: 0, problem: null } }),
    )

    // Act
    await renderPanel(member)

    // Assert
    await screen.findByText('Home Jellyfin')
    expect(within(rowOf('Home Jellyfin')).getByText('Files: 650 inspected · 3 couldn\'t be read · 1,217 not inspected yet'))
      .toHaveClass('settings-muted')
    expect(within(rowOf('Cartoons')).getByText('Files: 1 inspected')).toBeInTheDocument()
    expect(within(rowOf('Empty')).queryByText(/^Files:/)).not.toBeInTheDocument()
  })

  it('shows the owner why the first of a source\'s files could not be read', async () => {
    // Arrange
    const problem = '“/media/films/x.mkv” is outside the media folders the app may read.'
    serveInspection({}, sourceOf({ inspection: { inspectedCount: 869, problemCount: 1, notInspectedCount: 0, problem } }))

    // Act
    await renderPanel()

    // Assert
    // MAM-42 — with how many, as the bar's chip on the library counts them.
    const line = (await screen.findByText(`1 file couldn't be read: ${problem}`)).closest('p')!
    expect(line).toHaveClass('library-source-warning')
    expect(rowOf('Home Jellyfin')).toContainElement(line)
  })

  it('tells a member how many of a source\'s files could not be read, and that the owner can see why', async () => {
    // Arrange
    // MAM-42 — the bar's "2 couldn't be read" chip on the library has its line for a member too.
    serveInspection({}, sourceOf({ inspection: { inspectedCount: 868, problemCount: 2, notInspectedCount: 0, problem: null } }))

    // Act
    await renderPanel(member)

    // Assert
    const line = (await screen.findByText('2 files couldn\'t be read. The owner can see why.')).closest('p')!
    expect(line).toHaveClass('library-source-warning')
    expect(rowOf('Home Jellyfin')).toContainElement(line)
  })

  it('does not repeat a source\'s path problem as its inspection problem', async () => {
    // Arrange
    const problem = 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.'
    serveInspection({}, sourceOf({
      filesWithoutAppPathCount: 1,
      pathProblem: problem,
      inspection: { inspectedCount: 869, problemCount: 1, notInspectedCount: 0, problem },
    }))

    // Act
    await renderPanel()

    // Assert
    expect(await screen.findByText(/1 file has no path in this app/)).toBeInTheDocument()
    expect(screen.getByText(/1 file has no path in this app/)).toHaveTextContent(problem)
    // MAM-42 — the count still shows, as the bar's chip does, without the sentence a second time.
    expect(screen.getByText('1 file couldn\'t be read.').closest('p')).toHaveClass('library-source-warning')
    expect(screen.queryByText(new RegExp(`couldn't be read: `))).not.toBeInTheDocument()
  })

  it.each<[string, Partial<FileInspectionOverview>, string | RegExp, FileInspectionRun['status'] | undefined]>([
    ['never run', { lastFinishedRun: null }, 'Files have not been inspected yet.', undefined],
    ['queued', { activeRun: queued }, 'Waiting to inspect files…', 'Queued'],
    ['started, before it has listed the files', { activeRun: { ...running, filesTotal: null, filesChecked: 0 } }, 'Inspecting files…', 'Running'],
    ['reading', { activeRun: running }, 'Inspecting files… 120 of 870 checked', 'Running'],
    ['finished', {}, /^Files last inspected Sep 30, 2026, 11:05\sAM: 870 checked, 870 read by ffprobe\.$/, 'Succeeded'],
    ['failed', { lastFinishedRun: failed }, /^File inspection failed Sep 30, 2026, 11:05\sAM: No media folders are allowed yet\./, 'Failed'],
  ])('shows an inspection that is %s on its own line', async (_, changes, text, status) => {
    // Arrange
    serveInspection(changes)

    // Act
    await renderPanel()

    // Assert
    const line = within(await findInspectionRow()).getByRole('status')
    expect(line).toHaveTextContent(text)
    if (status === undefined) expect(line).not.toHaveAttribute('data-status')
    else expect(line).toHaveAttribute('data-status', status)
  })

  it('tells everyone else only that the last inspection failed', async () => {
    // Arrange — the API leaves the sentence out for anyone but the owner.
    serveInspection({ lastFinishedRun: { ...failed, error: null } })

    // Act
    await renderPanel(member)

    // Assert
    expect(within(await findInspectionRow()).getByRole('status'))
      .toHaveTextContent('The last file inspection failed. The owner can see why.')
  })

  it('says the inspection is off without a media folder, and links the owner to Media folders', async () => {
    // Arrange
    serveInspection({ hasAllowedRoots: false, lastFinishedRun: null })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    expect(row.getByRole('status')).toHaveTextContent('File inspection is off: no media folder is allowed yet. Choose media folders')
    expect(row.getByRole('link', { name: 'Choose media folders' })).toHaveAttribute('href', '/settings/media-folders')
    const button = row.getByRole('button', { name: 'Inspect files' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title',
      'No media folder is allowed yet. Choose the folders the app may read under Media folders first.')
  })

  it('tells everyone else that the inspection is off, without a link', async () => {
    // Arrange
    serveInspection({ hasAllowedRoots: false })

    // Act
    await renderPanel(member)

    // Assert
    const row = within(await findInspectionRow())
    expect(row.getByRole('status')).toHaveTextContent(/^File inspection is off: no media folder is allowed yet\.$/)
    expect(row.queryByRole('link')).not.toBeInTheDocument()
  })

  it('keeps Inspect files disabled for anyone but the owner, and says why', async () => {
    // Arrange
    const { user } = await renderPanel(member)
    const button = within(await findInspectionRow()).getByRole('button', { name: 'Inspect files' })

    // Act
    await user.click(button)

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner can inspect the files.')
    expect(libraryApi.startInspection).not.toHaveBeenCalled()
  })

  it('keeps Inspect files disabled while a run waits to start, and enabled while one runs', async () => {
    // Arrange
    serveInspection({ activeRun: queued, hasQueuedRun: true })
    const { client } = await renderPanel()
    const whileQueued = within(await findInspectionRow()).getByRole('button', { name: 'Inspect files' })
    const queuedReason = whileQueued.parentElement?.getAttribute('title')
    const disabledWhileQueued = (whileQueued as HTMLButtonElement).disabled
    serveInspection({ activeRun: running })

    // Act
    await client.refetchQueries()

    // Assert
    await screen.findByText('Inspecting files… 120 of 870 checked')
    expect(disabledWhileQueued).toBe(true)
    expect(queuedReason).toBe('An inspection is already waiting to start.')
    expect(screen.getByRole('button', { name: 'Inspect files' })).toBeEnabled()
  })

  it('says a run waits behind the running one and keeps Inspect files disabled until it starts', async () => {
    // Arrange
    serveInspection({ activeRun: running, hasQueuedRun: true })

    // Act
    await renderPanel()

    // Assert
    // The running one is what happens now; the queued one only adds "and again after this".
    const row = await findInspectionRow()
    expect(within(row).getByRole('status')).toHaveTextContent(
      'Inspecting files… 120 of 870 checked; another inspection starts after this one.',
    )
    const button = within(row).getByRole('button', { name: 'Inspect files' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'An inspection is already waiting to start.')
  })

  it('shows a start during a running inspection as the run that follows it, at once', async () => {
    // Arrange
    serveInspection({ activeRun: running })
    vi.mocked(libraryApi.startInspection).mockResolvedValue(queued)
    const { user } = await renderPanel()
    const row = await findInspectionRow()
    // The next answer of the sources never comes, so what shows is what the start put in the cache.
    vi.mocked(libraryApi.sources).mockReturnValue(new Promise(() => {}))

    // Act
    await user.click(within(row).getByRole('button', { name: 'Inspect files' }))

    // Assert
    // Not a silent click: the running line says at once that the queued run follows it.
    expect(await within(row).findByText('Inspecting files… 120 of 870 checked; another inspection starts after this one.'))
      .toBeInTheDocument()
    expect(libraryApi.startInspection).toHaveBeenCalledExactlyOnceWith()
  })

  it('starts an inspection for the owner, shows it waiting at once and asks for the sources again', async () => {
    // Arrange
    let answer: (run: FileInspectionRun) => void = () => {}
    vi.mocked(libraryApi.startInspection).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { user } = await renderPanel()
    await user.click(within(await findInspectionRow()).getByRole('button', { name: 'Inspect files' }))
    const startingButton = screen.getByRole('button', { name: 'Starting…' })
    const disabledWhileStarting = (startingButton as HTMLButtonElement).disabled
    // The next answer of the sources never comes, so what shows is what the start put in the cache.
    vi.mocked(libraryApi.sources).mockReturnValue(new Promise(() => {}))

    // Act
    await act(async () => answer(queued))

    // Assert
    expect(await screen.findByText('Waiting to inspect files…')).toBeInTheDocument()
    expect(disabledWhileStarting).toBe(true)
    expect(libraryApi.startInspection).toHaveBeenCalledExactlyOnceWith()
    await vi.waitFor(() => expect(libraryApi.sources).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a refused start as the API sentence under the inspection row', async () => {
    // Arrange — the folders were removed on another device; the refetch turns the row off.
    const sentence = 'No media folders are allowed yet. Add the folders the app may read under Media folders in the user menu, then inspect again.'
    vi.mocked(libraryApi.startInspection).mockImplementation(async () => {
      serveInspection({ hasAllowedRoots: false })
      throw apiError(409, sentence)
    })
    const { user } = await renderPanel()

    // Act
    await user.click(within(await findInspectionRow()).getByRole('button', { name: 'Inspect files' }))

    // Assert
    const row = within(await findInspectionRow())
    expect(await row.findByRole('alert')).toHaveTextContent(sentence)
    expect(row.getByRole('alert')).toHaveClass('library-source-status')
    expect(row.getByRole('alert')).toHaveAttribute('data-status', 'Failed')
    expect(row.getByRole('status')).toHaveTextContent(/^File inspection is off/)
    expect(within(rowOf('Home Jellyfin')).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('names the failure when a refused start carries no sentence', async () => {
    // Arrange
    vi.mocked(libraryApi.startInspection).mockRejectedValue(apiError(500))
    const { user } = await renderPanel()

    // Act
    await user.click(within(await findInspectionRow()).getByRole('button', { name: 'Inspect files' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not start the file inspection.')
    expect(screen.getByRole('button', { name: 'Inspect files' })).toBeEnabled()
  })

  it('shows both rows, Media folders and the inspection, while no media library is connected', async () => {
    // Arrange — MAM-18: the app's own scan finds names without a library, and the inspection reads them.
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf())

    // Act
    await renderPanel()

    // Assert
    expect(await screen.findByText(/^No media library is connected yet\./)).toBeInTheDocument()
    const inspection = document.querySelector<HTMLElement>('.library-inspection')!
    expect(within(inspection).getByRole('status')).toHaveTextContent(/^Files last inspected /)
    expect(within(inspection).getByRole('button', { name: 'Inspect files' })).toBeEnabled()
    expect(within(screen.getByText('Media folders').closest('li')!).getByRole('button', { name: 'Scan folders' }))
      .toBeEnabled()
  })
})
