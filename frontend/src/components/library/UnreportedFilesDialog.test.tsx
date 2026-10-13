import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { UnreportedFilesDialog, type UnreportedFilesTab } from '@/components/library/UnreportedFilesDialog'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, UnreportedFile, UnreportedFileList } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'

vi.mock('@/lib/library-api')

// MAM-42 — the Files in no library dialog: the files the scan found that no library lists, each with its
// name over its folder, its size, why a library most likely doesn't list it and what the app's look found;
// three tabs paged by the server; and the owner's Ignore and Stop ignoring, disabled with the reason for a
// member.

const isoError = 'ffprobe couldn\'t read “/media/Adobe Premiere Rush 2.10.0.30.iso” as media: Invalid data found when processing input.'

function fileOf(changes: Partial<UnreportedFile>): UnreportedFile {
  return {
    id: 'f0000000-0000-4000-8000-000000000001',
    appPath: '/media/Adobe Premiere Rush 2.10.0.30.iso',
    sizeBytes: 1_288_490_189,
    firstSeenUtc: '2026-10-12T15:24:00Z',
    ignoredUtc: null,
    place: { kind: 'NoLibraryFolder', libraryName: null },
    inspection: { status: 'MediaUnreadable', problem: isoError, inspectedUtc: '2026-10-12T18:13:00Z', media: null },
    ...changes,
  }
}

const sample = fileOf({
  id: 'f0000000-0000-4000-8000-000000000002',
  appPath: '/media/movies/Dune (2021)/Dune (2021) sample.mkv',
  sizeBytes: 52_428_800,
  place: { kind: 'LibraryFolder', libraryName: 'Фільми' },
  inspection: {
    status: 'Inspected',
    problem: null,
    inspectedUtc: '2026-10-12T18:13:00Z',
    media: { durationMilliseconds: 60_000, bitRate: 7_000_000, tracks: [] },
  },
})

function listOf(items: UnreportedFile[], changes: Partial<UnreportedFileList> = {}): UnreportedFileList {
  return { items, totalCount: items.length, page: 1, pageSize: 25, countedCount: 17, problemCount: 1, ignoredCount: 0, ...changes }
}

function renderDialog(signedIn: CurrentUser = owner, initialTab: UnreportedFilesTab = 'All') {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <UnreportedFilesDialog initialTab={initialTab} onClose={onClose} />
    </QueryClientProvider>,
  )
  return { user: userEvent.setup(), onClose, client }
}

/** The row of the file whose name is `name`. */
const rowOf = async (name: string) => (await screen.findByText(name)).closest('li')!

describe('UnreportedFilesDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([fileOf({}), sample]))
  })

  it('lists each file with its name over its folder, its size, why no library lists it and what ffprobe found', async () => {
    // Arrange

    // Act
    renderDialog()

    // Assert
    const iso = within(await rowOf('Adobe Premiere Rush 2.10.0.30.iso'))
    expect(iso.getByText('/media')).toHaveClass('library-file-folder')
    expect(iso.getByText('1.2 GiB')).toBeInTheDocument()
    expect(iso.getByText('No library\'s folders hold it, so no library looks for it there.')).toBeInTheDocument()
    // What ffprobe said, the owner's sentence, as a warning like the Library page's.
    expect(iso.getByText(isoError).closest('p')).toHaveClass('library-file-problem')
    const dune = within(await rowOf('Dune (2021) sample.mkv'))
    expect(dune.getByText('/media/movies/Dune (2021)')).toBeInTheDocument()
    expect(dune.getByText(/^In Фільми's folders, but Фільми didn't list it: a library skips samples, trailers and extras/)).toBeInTheDocument()
    expect(dune.getByText('1:00')).toBeInTheDocument()
    expect(libraryApi.unreportedFiles).toHaveBeenCalledExactlyOnceWith({ view: undefined, page: 1, pageSize: 25 })
  })

  it('says the libraries\' folders aren\'t known yet, and that a file wasn\'t looked at yet', async () => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([fileOf({ place: { kind: 'FoldersUnknown', libraryName: null }, inspection: null, sizeBytes: null })]))

    // Act
    renderDialog()

    // Assert
    const row = within(await rowOf('Adobe Premiere Rush 2.10.0.30.iso'))
    expect(row.getByText('The app doesn\'t know the libraries\' folders yet. Sync a library first.')).toBeInTheDocument()
    expect(row.getByText('Not inspected yet')).toBeInTheDocument()
    expect(row.getByText('Size unknown')).toBeInTheDocument()
  })

  it('tells a member what ffprobe found without its sentence, and holds Ignore with the reason', async () => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([fileOf({ inspection: { status: 'MediaUnreadable', problem: null, inspectedUtc: '2026-10-12T18:13:00Z', media: null } })]))

    // Act
    renderDialog(member)

    // Assert
    const row = within(await rowOf('Adobe Premiere Rush 2.10.0.30.iso'))
    expect(row.getByText('ffprobe couldn\'t read this file as a video.')).toBeInTheDocument()
    const ignore = row.getByRole('button', { name: 'Ignore' })
    expect(ignore).toBeDisabled()
    expect(ignore.parentElement).toHaveAttribute('title', 'Only the owner can ignore files.')
  })

  it.each<[UnreportedFilesTab, string]>([
    ['All', 'In no library (17)'],
    ['Problems', 'Couldn\'t be read (1)'],
    ['Ignored', 'Ignored (2)'],
  ])('opens on the %s tab it is given, every tab with its count', async (tab, selected) => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([fileOf({})], { ignoredCount: 2 }))

    // Act
    renderDialog(owner, tab)

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Files in no library' })
    expect(await within(dialog).findByRole('tab', { name: selected })).toHaveAttribute('aria-selected', 'true')
    expect(within(dialog).getAllByRole('tab').map((element) => element.textContent))
      .toEqual(['In no library (17)', 'Couldn\'t be read (1)', 'Ignored (2)'])
    expect(libraryApi.unreportedFiles).toHaveBeenCalledWith({ view: tab === 'All' ? undefined : tab, page: 1, pageSize: 25 })
  })

  it('asks the server for another tab from its first page, and for another page of a tab', async () => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([fileOf({}), sample], { totalCount: 30 }))
    const { user } = renderDialog()
    await rowOf('Adobe Premiere Rush 2.10.0.30.iso')

    // Act
    await user.click(screen.getByRole('button', { name: /next page/i }))
    await user.click(screen.getByRole('tab', { name: 'Ignored (0)' }))

    // Assert
    expect(vi.mocked(libraryApi.unreportedFiles).mock.calls.map(([query]) => query)).toEqual([
      { view: undefined, page: 1, pageSize: 25 },
      { view: undefined, page: 2, pageSize: 25 },
      { view: 'Ignored', page: 1, pageSize: 25 },
    ])
  })

  it('ignores a file for the owner, then asks for the files and the sources again', async () => {
    // Arrange
    vi.mocked(libraryApi.ignoreUnreportedFile).mockResolvedValue()
    const { user, client } = renderDialog()
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const row = within(await rowOf('Adobe Premiere Rush 2.10.0.30.iso'))

    // Act
    await user.click(row.getByRole('button', { name: 'Ignore' }))

    // Assert
    expect(libraryApi.ignoreUnreportedFile).toHaveBeenCalledExactlyOnceWith('f0000000-0000-4000-8000-000000000001')
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['library', 'unreported-files'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['library', 'sources'] })
    await vi.waitFor(() => expect(libraryApi.unreportedFiles).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('counts an ignored file again on Stop ignoring, and says since when it is ignored', async () => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([fileOf({ ignoredUtc: '2026-10-13T07:30:00Z' })], { ignoredCount: 1 }))
    vi.mocked(libraryApi.undoIgnoreUnreportedFile).mockResolvedValue()
    const { user } = renderDialog(owner, 'Ignored')
    const row = within(await rowOf('Adobe Premiere Rush 2.10.0.30.iso'))

    // Act
    await user.click(row.getByRole('button', { name: 'Stop ignoring' }))

    // Assert
    expect(row.getByText(/^Ignored Oct 13, 2026/)).toBeInTheDocument()
    expect(libraryApi.undoIgnoreUnreportedFile).toHaveBeenCalledExactlyOnceWith('f0000000-0000-4000-8000-000000000001')
    expect(libraryApi.ignoreUnreportedFile).not.toHaveBeenCalled()
  })

  it.each<[string, Error, string]>([
    ['a file a scan took away meanwhile', apiError(404), 'This file is no longer in no library: a scan or a sync changed it meanwhile.'],
    ['a refusal', apiError(403, 'Only the installation owner can do this.'), 'Only the installation owner can do this.'],
    ['a failure without a sentence', apiError(500), 'Could not ignore the file.'],
  ])('shows %s on the file\'s row', async (_, error, sentence) => {
    // Arrange
    vi.mocked(libraryApi.ignoreUnreportedFile).mockRejectedValue(error)
    const { user } = renderDialog()
    const row = within(await rowOf('Adobe Premiere Rush 2.10.0.30.iso'))

    // Act
    await user.click(row.getByRole('button', { name: 'Ignore' }))

    // Assert
    expect(await row.findByRole('alert')).toHaveTextContent(sentence)
    expect(within(await rowOf('Dune (2021) sample.mkv')).queryByRole('alert')).not.toBeInTheDocument()
  })

  it.each<[UnreportedFilesTab, string]>([
    ['All', 'No file is in no library.'],
    ['Problems', 'Every file in no library could be read.'],
    ['Ignored', 'No file is ignored.'],
  ])('says the %s tab holds nothing', async (tab, sentence) => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockResolvedValue(listOf([], { countedCount: 0, problemCount: 0 }))

    // Act
    renderDialog(owner, tab)

    // Assert
    expect(await screen.findByText(sentence)).toBeInTheDocument()
  })

  it('says why the files couldn\'t be loaded', async () => {
    // Arrange
    vi.mocked(libraryApi.unreportedFiles).mockRejectedValue(apiError(400, 'Page size must be between 1 and 100.'))

    // Act
    renderDialog()

    // Assert
    expect(await screen.findByText('Could not load the files in no library.')).toBeInTheDocument()
    expect(screen.getByText('Page size must be between 1 and 100.')).toBeInTheDocument()
  })

  it('closes on Close, its only button in the footer', async () => {
    // Arrange
    const { user, onClose } = renderDialog()
    await rowOf('Adobe Premiere Rush 2.10.0.30.iso')

    // Act
    await user.click(screen.getAllByRole('button', { name: 'Close' }).at(-1)!)

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
  })
})
