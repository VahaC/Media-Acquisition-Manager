import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, FileHashing, FileInspectionOverview, FileInspectionRun } from '@/lib/types'
import { member, owner, signInAs } from '@/test/auth-session'
import { inspectionOf, inspectionRunOf, sourceOf, sourcesOf } from '@/test/library'
import { openSourceDetails } from '@/test/sources-details'

vi.mock('@/lib/library-api')

// MAM-19 — the hashing at the end of a file inspection, on the panel's inspection row: its progress
// while the run is under way, and once the run has finished how many of the files that could be copies
// it hashed and the files it couldn't hash. LibrarySyncPanel.inspection.test.tsx covers the row's
// other lines and Inspect files; LibrarySyncPanel.space.test.tsx the room the files take.

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
  if (details) await openSourceDetails(userEvent.setup())
}

/** The sources answer every request with Home Jellyfin and the inspection as `changes` set it. */
function serveInspection(changes: Partial<FileInspectionOverview>) {
  vi.mocked(libraryApi.sources).mockResolvedValue({ ...sourcesOf(sourceOf({})), inspection: inspectionOf(changes) })
}

/** The inspection's row under the sources, once they have arrived. */
async function findInspectionRow() {
  await screen.findByText('Home Jellyfin')
  return document.querySelector<HTMLElement>('.library-inspection')!
}

/** A run that has finished with `hashing` as its last step went. */
const finishedWith = (hashing: Partial<FileHashing>): FileInspectionRun =>
  inspectionRunOf({ hashing: { ...inspectionRunOf({}).hashing, ...hashing } })

/** A run under way, every file checked, its hashing as `hashing` sets it. */
const runningWith = (hashing: Partial<FileHashing>): FileInspectionRun => inspectionRunOf({
  status: 'Running',
  finishedUtc: null,
  filesChecked: 870,
  filesProbed: 40,
  hashing: { ...inspectionRunOf({}).hashing, ...hashing },
})

/** The first hashing problem's sentence, as the inventory words a file that changed while it was read. */
const changedWhileHashing = '“/library/movies/Alien. Collection/[02.1] Aliens (1986).mkv” changed while the app was reading it for its hash, so the hash can\'t be said to belong to either version.'

/** Any of the hashing's outcome lines. */
const hashingOutcome = /needed a hash|Files that could be copies|couldn't be hashed/

describe('LibrarySyncPanel hashing', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    serveInspection({})
  })

  it.each<[string, FileInspectionRun, string]>([
    [
      'still inspecting, before the hashing has listed its files',
      inspectionRunOf({ status: 'Running', finishedUtc: null, filesChecked: 120, filesProbed: 40, hashing: { candidates: null, checked: 0, read: 0, problemCount: 0, problem: null } }),
      'Inspecting files… 120 of 870 checked',
    ],
    ['past a hashing that listed no file', runningWith({ candidates: 0 }), 'Inspecting files… 870 of 870 checked'],
    ['hashing, none done yet', runningWith({ candidates: 2 }), 'Hashing the files that could be copies… 0 of 2'],
    ['hashing many files', runningWith({ candidates: 1_204, checked: 1_003, read: 990, problemCount: 2 }), 'Hashing the files that could be copies… 1,003 of 1,204'],
  ])('shows a run %s on its status line', async (_, run, text) => {
    // Arrange
    serveInspection({ activeRun: run })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    const line = row.getByRole('status')
    expect(line).toHaveTextContent(new RegExp(`^${text}$`))
    expect(line).toHaveAttribute('data-status', 'Running')
    // How it went is known once the run has finished: the last run's outcome stays unsaid meanwhile.
    expect(row.queryByText(hashingOutcome)).not.toBeInTheDocument()
  })

  it('says another inspection follows the one that is hashing', async () => {
    // Arrange
    serveInspection({ activeRun: runningWith({ candidates: 10, checked: 3 }), hasQueuedRun: true })

    // Act
    await renderPanel()

    // Assert
    expect(within(await findInspectionRow()).getByRole('status')).toHaveTextContent(
      /^Hashing the files that could be copies… 3 of 10; another inspection starts after this one\.$/)
  })

  it.each<[string, FileInspectionRun]>([
    ['listed no file to hash', finishedWith({ candidates: 0 })],
    ['ran before the hashing existed', finishedWith({ candidates: null })],
  ])('says no file needed a hash under a run that %s', async (_, run) => {
    // Arrange
    serveInspection({ lastFinishedRun: run })

    // Act
    await renderPanel(member)

    // Assert
    const row = within(await findInspectionRow())
    expect(row.getByRole('status')).toHaveTextContent(/^Files last inspected /)
    expect(row.getByText('No file needed a hash: no two files of one size could be copies of each other.'))
      .toHaveClass('settings-muted')
    expect(row.queryByText(/couldn't be hashed/)).not.toBeInTheDocument()
  })

  it.each<[string, Partial<FileHashing>, string]>([
    ['a few files', { candidates: 4, checked: 4, read: 2 }, 'Files that could be copies: 4 of 4 hashed, 2 read in full this time.'],
    ['none of them read again', { candidates: 2, checked: 2, read: 0 }, 'Files that could be copies: 2 of 2 hashed, 0 read in full this time.'],
    ['many files', { candidates: 1_204, checked: 1_204, read: 1_100 }, 'Files that could be copies: 1,204 of 1,204 hashed, 1,100 read in full this time.'],
  ])('says how many of the files that could be copies a finished run hashed and read: %s', async (_, hashing, text) => {
    // Arrange
    serveInspection({ lastFinishedRun: finishedWith(hashing) })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    expect(row.getByText(text)).toHaveClass('settings-muted')
    expect(row.queryByText(/couldn't be hashed/)).not.toBeInTheDocument()
  })

  it('shows the owner how many files could not be hashed, with the first one\'s sentence', async () => {
    // Arrange
    serveInspection({ lastFinishedRun: finishedWith({ candidates: 4, checked: 4, read: 3, problemCount: 1, problem: changedWhileHashing }) })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    // A file it couldn't hash was checked, but isn't hashed.
    expect(row.getByText('Files that could be copies: 3 of 4 hashed, 3 read in full this time.')).toBeInTheDocument()
    const warning = row.getByText(/couldn't be hashed/).closest('p')!
    expect(warning).toHaveClass('library-source-warning')
    expect(warning).toHaveTextContent(`1 file couldn't be hashed: ${changedWhileHashing}`)
    expect(warning.querySelector('svg')).not.toBeNull()
  })

  it('counts several files the owner reads could not be hashed', async () => {
    // Arrange
    serveInspection({ lastFinishedRun: finishedWith({ candidates: 6, checked: 6, read: 4, problemCount: 2, problem: changedWhileHashing }) })

    // Act
    await renderPanel()

    // Assert
    expect(within(await findInspectionRow()).getByText(/couldn't be hashed/))
      .toHaveTextContent(`2 files couldn't be hashed: ${changedWhileHashing}`)
  })

  it.each<[number, string]>([
    [1, '1 file couldn\'t be hashed. The owner can see why.'],
    [1_002, '1,002 files couldn\'t be hashed. The owner can see why.'],
  ])('tells everyone else only how many files could not be hashed: %s', async (problemCount, text) => {
    // Arrange — the sentence quotes the owner's paths, so the API leaves it out.
    serveInspection({ lastFinishedRun: finishedWith({ candidates: 2_000, checked: 2_000, read: 900, problemCount, problem: null }) })

    // Act
    await renderPanel(member)

    // Assert
    const warning = within(await findInspectionRow()).getByText(/couldn't be hashed/).closest('p')!
    expect(warning).toHaveClass('library-source-warning')
    expect(warning.textContent).toBe(text)
  })

  it.each<[string, Partial<FileInspectionOverview>]>([
    ['never run', { lastFinishedRun: null }],
    ['queued', {
      lastFinishedRun: null,
      activeRun: inspectionRunOf({
        status: 'Queued',
        startedUtc: null,
        finishedUtc: null,
        filesTotal: null,
        filesChecked: 0,
        filesProbed: 0,
        hashing: { candidates: null, checked: 0, read: 0, problemCount: 0, problem: null },
      }),
    }],
    // A run the restart interrupted while it hashed: the reconciler failed it, and what it counted
    // so far isn't an outcome — the run the start queued hashes again.
    ['failed', {
      lastFinishedRun: inspectionRunOf({
        status: 'Failed',
        hashing: { candidates: 10, checked: 4, read: 3, problemCount: 1, problem: changedWhileHashing },
        error: 'The app restarted before this inspection finished. What it had read is kept.',
      }),
    }],
  ])('shows no hashing line for an inspection that is %s', async (_, changes) => {
    // Arrange
    serveInspection(changes)

    // Act
    await renderPanel()

    // Assert
    expect(within(await findInspectionRow()).queryByText(hashingOutcome)).not.toBeInTheDocument()
  })

  it('shows no hashing line while the inspection is off, whatever the last run hashed', async () => {
    // Arrange — the folders were removed after a run that couldn't hash a file.
    serveInspection({
      hasAllowedRoots: false,
      lastFinishedRun: finishedWith({ candidates: 4, checked: 4, read: 3, problemCount: 1, problem: changedWhileHashing }),
    })

    // Act
    await renderPanel()

    // Assert
    const row = within(await findInspectionRow())
    expect(row.getByRole('status')).toHaveTextContent(/^File inspection is off/)
    expect(row.queryByText(hashingOutcome)).not.toBeInTheDocument()
  })
})
