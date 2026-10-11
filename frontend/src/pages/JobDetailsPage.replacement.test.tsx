import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { qk } from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import type { CurrentUser, DownloadJob, DownloadJobReplacement, ReplacementDetails, ReplacementJobState, ReplacementNameState } from '@/lib/types'
import { JobDetailsPage } from '@/pages/JobDetailsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, fileOf, filesPage } from '@/test/downloads'
import {
  alienHeldSubtitle,
  alienSubtitle,
  alienVideo,
  nameOf,
  REPLACEMENT_ID,
  replacementDetailsOf,
  replacementOf,
} from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-26 — the old copies of the replacement a job carries, on the job's page: every name the move
// planned and where it is now, and the owner's Retry, Restore and Delete for good (ADR 0030).

/** The sentences the section says, spelled out rather than read from the locale. */
const RESTORE_MESSAGE = 'Every file of this replacement in the quarantine goes back where it was, never over a file there now, and no folder is created for it. The new copy stays as it is. Jellyfin lists the old copy again and identifies it afresh, so a TMDb link you set by hand may need Identify again.'
const SWITCHED_OFF = 'Replacement is switched off on the Media folders page: nothing is put back or deleted while it is off.'
const CHANGED = 'What this replacement holds in the quarantine changed since you opened it. Open Delete for good again.'
const HELD = 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.'
const NONE_YET = 'No old copy has moved yet: each moves into the quarantine once the new file is verified.'

/** The downloaded job carrying the replacement, in `changes`'s state. */
function jobWith(changes: Partial<DownloadJobReplacement> = {}): DownloadJob {
  return { ...downloadedJob, replacement: replacementOf({ state: 'Quarantined', quarantinedCopyCount: 1, ...changes }) }
}

/** The server answers `job`, its one file, and `details` for its replacement. */
function serve(job: DownloadJob, details: ReplacementDetails = replacementDetailsOf()) {
  vi.mocked(downloadsApi.get).mockResolvedValue(job)
  vi.mocked(downloadsApi.files).mockImplementation(async (_, query) => filesPage(query, [fileOf({ progress: 1 })]))
  vi.mocked(replacementsApi.get).mockResolvedValue(details)
}

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/jobs/${downloadedJob.id}`]}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs/:id" element={<JobDetailsPage />} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The Old copies section, once its names have arrived. */
async function oldCopies() {
  const section = await screen.findByRole('region', { name: 'Old copies' })
  await within(section).findByRole('table')
  return section
}

/** The section's row of the name that was at `originalPath`. */
const nameRowOf = (section: HTMLElement, originalPath: string) =>
  within(section).getByText(originalPath, { selector: '.jobs-file-path' }).closest('tr')!

/** A confirmation's buttons, in the order the footer lays them out. */
const footerButtons = (dialog: HTMLElement) =>
  within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')

describe('JobDetailsPage — the old copies of a replacement', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    serve(jobWith())
  })

  it('lists the old copies between the facts and the files: each name where it was, its place in the quarantine, its size and where it is now', async () => {
    // Arrange
    const details = replacementDetailsOf()

    // Act
    renderPage()

    // Assert
    const section = await oldCopies()
    expect(replacementsApi.get).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    const facts = screen.getByRole('region', { name: 'Details' })
    const files = screen.getByRole('region', { name: 'Files' })
    expect(facts.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(section.compareDocumentPosition(files) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(within(section).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Old copy', 'Size', 'Where it is now'])
    expect(within(section).getByText('The quarantine holds 40 GiB of this replacement, in 2 files.')).toBeInTheDocument()
    expect(details.names.map((name) => within(nameRowOf(section, name.originalPath)).getByText(name.quarantinePath, { selector: '.jobs-file-path' })))
      .toHaveLength(3)
    const video = within(nameRowOf(section, alienVideo.originalPath))
    expect(video.getByText('40 GiB')).toBeInTheDocument()
    expect(video.getByText('In the quarantine.').closest('p')).toHaveClass('library-source-status')
    expect(within(nameRowOf(section, alienSubtitle.originalPath)).getByText('50 KiB')).toBeInTheDocument()
    const held = within(nameRowOf(section, alienHeldSubtitle.originalPath))
    expect(held.getByText('Stayed in its folder.').closest('p')).toHaveClass('library-source-warning')
    expect(held.getByText(alienHeldSubtitle.problem!).closest('p')).toHaveClass('library-source-warning')
  })

  it('names every cell for a phone, and stacks the paths and where each name is', async () => {
    // Arrange
    renderPage()

    // Act
    const section = await oldCopies()

    // Assert
    const cells = [...nameRowOf(section, alienVideo.originalPath).querySelectorAll('td')]
    expect(cells.map((cell) => cell.getAttribute('data-label'))).toEqual(['Old copy', 'Size', 'Where it is now'])
    expect(cells.map((cell) => cell.classList.contains('jobs-stacked-cell'))).toEqual([true, false, true])
    expect(within(section).getByText(alienVideo.quarantinePath).closest('.ui-hint')).toHaveTextContent(`Quarantine: ${alienVideo.quarantinePath}`)
  })

  it.each<[ReplacementNameState, string, string]>([
    ['Planned', 'In its folder, not moved.', 'library-source-status'],
    ['Moving', 'Being moved into the quarantine…', 'library-source-status'],
    ['Quarantined', 'In the quarantine.', 'library-source-status'],
    ['Stayed', 'Stayed in its folder.', 'library-source-warning'],
    ['Restoring', 'Being put back…', 'library-source-status'],
    ['Restored', 'Back where it was.', 'library-source-status'],
    ['Deleting', 'Being deleted for good…', 'library-source-status'],
    ['Deleted', 'Deleted for good.', 'library-source-status'],
    ['Lost', 'Found neither where it was nor in the quarantine.', 'library-source-warning'],
  ])('says where a name is now: %s', async (state, sentence, tone) => {
    // Arrange
    serve(jobWith(), replacementDetailsOf({ names: [nameOf({ state })] }))

    // Act
    renderPage()

    // Assert
    const section = await oldCopies()
    const line = within(nameRowOf(section, alienVideo.originalPath)).getByText(sentence).closest('p')
    expect(line).toHaveClass(tone)
    expect(line?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('says a name with another name frees nothing, and shows why one wasn\'t put back', async () => {
    // Arrange
    const problem = 'The app couldn\'t rename “Alien.mkv” back: “/media/movies/Alien (1979)/Alien.mkv” already holds a file.'
    serve(jobWith(), replacementDetailsOf({ names: [nameOf({ otherNames: true, problem })] }))

    // Act
    renderPage()

    // Assert
    const row = within(nameRowOf(await oldCopies(), alienVideo.originalPath))
    expect(row.getByText('In the quarantine.')).toBeInTheDocument()
    expect(row.getByText(problem).closest('p')).toHaveClass('library-source-warning')
    expect(row.getByText('It has another name (a hardlink), so deleting it frees no space.')).toBeInTheDocument()
  })

  it('says no old copy has moved yet, with no action, while the replacement holds no names', async () => {
    // Arrange
    serve(jobWith({ state: 'Verified', quarantinedCopyCount: 0 }), replacementDetailsOf({
      state: 'Verified', canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await screen.findByRole('region', { name: 'Old copies' })
    expect(await within(section).findByText(NONE_YET)).toBeInTheDocument()
    expect(within(section).queryByRole('table')).not.toBeInTheDocument()
    expect(within(section).queryByRole('button')).not.toBeInTheDocument()
  })

  it('says no old copy moves while replacement is switched off, when the replacement holds no names yet', async () => {
    // Arrange
    serve(jobWith({ state: 'Verified', quarantinedCopyCount: 0, switchedOff: true }), replacementDetailsOf({
      state: 'Verified', switchedOff: true, canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await screen.findByRole('region', { name: 'Old copies' })
    expect(await within(section).findByText('No old copy has moved yet, and none moves while replacement is switched off.')).toBeInTheDocument()
    expect(within(section).queryByText(NONE_YET)).not.toBeInTheDocument()
  })

  it.each<[string, ReplacementJobState, string, boolean]>([
    ['cancelled with its download', 'Cancelled', 'You cancelled the download, so nothing will be replaced.', false],
    ['failed on its new file', 'Failed', '“Alien.1979.Directors.Cut.2160p.mkv” has no video stream.', false],
    ['stopped for review by the check of its new files', 'NeedsReview', 'Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).', false],
    ['stopped for review by the move\'s checks, with Retry', 'NeedsReview', HELD, true],
  ])('says nothing was moved, never that a move comes, for a replacement %s before anything moved', async (_, state, reason, canRetry) => {
    // Arrange
    serve(jobWith({ state, reason, quarantinedCopyCount: 0, canRetry }), replacementDetailsOf({
      state, reason, canRetry, canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0, names: [],
    }))

    // Act
    renderPage()

    // Assert
    const section = await screen.findByRole('region', { name: 'Old copies' })
    expect(await within(section).findByText('Nothing of this replacement was moved.')).toBeInTheDocument()
    expect(within(section).queryByText(NONE_YET)).not.toBeInTheDocument()
    expect(within(section).queryAllByRole('button').map((button) => button.textContent)).toEqual(canRetry ? ['Retry'] : [])
  })

  it('says nothing of it is in the quarantine once every name is back', async () => {
    // Arrange
    serve(jobWith({ state: 'Restored', quarantinedCopyCount: 0 }), replacementDetailsOf({
      state: 'Restored',
      canRestore: false,
      canDeleteForGood: false,
      quarantinedBytes: 0,
      freesBytes: 0,
      names: [nameOf({ state: 'Restored' }), { ...alienSubtitle, state: 'Restored' }],
    }))

    // Act
    renderPage()

    // Assert
    const section = await oldCopies()
    expect(within(section).getByText('Nothing of this replacement is in the quarantine.')).toBeInTheDocument()
    expect(within(section).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows a member that the old copies are the owner\'s, with no action and nothing asked', async () => {
    // Arrange
    const signedIn = member

    // Act
    renderPage(signedIn)

    // Assert
    const section = await screen.findByRole('region', { name: 'Old copies' })
    expect(within(section).getByText('Only the installation owner sees the old copies a replacement moves, and restores them or deletes them for good.')).toBeInTheDocument()
    expect(within(section).queryByRole('button')).not.toBeInTheDocument()
    expect(replacementsApi.get).not.toHaveBeenCalled()
  })

  it('shows no Old copies section for a job without a replacement', async () => {
    // Arrange
    serve(downloadedJob)

    // Act
    renderPage()

    // Assert
    await screen.findByRole('region', { name: 'Files' })
    expect(screen.queryByRole('region', { name: 'Old copies' })).not.toBeInTheDocument()
    expect(replacementsApi.get).not.toHaveBeenCalled()
  })

  it('says the old copies could not be loaded, with the server sentence, while the facts stay, and Try again shows them', async () => {
    // Arrange
    vi.mocked(replacementsApi.get).mockRejectedValueOnce(networkError())
    const { user } = renderPage()
    const section = await screen.findByRole('region', { name: 'Old copies' })
    const state = await within(section).findByRole('alert')

    // Act
    await user.click(within(section).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the old copies.')
    expect(state).toHaveTextContent('The server could not be reached.')
    expect(await within(section).findByRole('table')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Details' })).toBeInTheDocument()
  })

  it('offers Restore and Delete for good for a quarantined replacement, the deletion in red outline, and no Retry', async () => {
    // Arrange
    renderPage()

    // Act
    const section = await oldCopies()

    // Assert
    const actions = section.querySelector<HTMLElement>('.settings-actions')!
    expect(within(actions).getAllByRole('button').map((button) => button.textContent)).toEqual(['Restore', 'Delete for good'])
    const remove = within(actions).getByRole('button', { name: 'Delete for good' })
    expect(remove).toHaveClass('ui-button-outline', 'ui-button-danger')
    expect(remove).toHaveAttribute('title', 'Delete the old copies in the quarantine for good')
    expect(remove.querySelector('.label-text')).toHaveTextContent('Delete for good')
    expect(within(actions).getByRole('button', { name: 'Restore' })).toHaveAttribute('title', 'Put the old copies in the quarantine back where they were')
  })

  // ── Retry ──

  it('sends a replacement the mover stopped back to be moved, without a confirmation, and says so', async () => {
    // Arrange
    const stopped = jobWith({ state: 'NeedsReview', reason: HELD, canRetry: true, quarantinedCopyCount: 0 })
    serve(stopped, replacementDetailsOf({
      state: 'NeedsReview', reason: HELD, canRetry: true, canRestore: false, canDeleteForGood: false,
      quarantinedBytes: 0, freesBytes: 0, names: [nameOf({ state: 'Planned' })],
    }))
    const retried = jobWith({ state: 'Verified', quarantinedCopyCount: 0 })
    vi.mocked(replacementsApi.retry).mockResolvedValue(retried)
    const { user } = renderPage()
    const section = await oldCopies()
    vi.mocked(downloadsApi.get).mockResolvedValue(retried)

    // Act
    await user.click(within(section).getByRole('button', { name: 'Retry' }))

    // Assert
    expect(replacementsApi.retry).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await screen.findByRole('status')).toHaveTextContent('Retried: the app checks the old copies again and moves them once nothing stands in the way.')
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  // ── Restore ──

  it('puts the old copies back after a plain confirmation that says the new copy stays and Jellyfin identifies the old one afresh', async () => {
    // Arrange
    const restored = jobWith({ state: 'Restored', quarantinedCopyCount: 0 })
    vi.mocked(replacementsApi.restore).mockResolvedValue(restored)
    const { user } = renderPage()
    const section = await oldCopies()
    vi.mocked(downloadsApi.get).mockResolvedValue(restored)
    await user.click(within(section).getByRole('button', { name: 'Restore' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Put the old copy back?' })
    const buttons = footerButtons(confirmation).map((button) => button.textContent)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Restore' }))

    // Assert
    expect(confirmation).toHaveTextContent(RESTORE_MESSAGE)
    expect(buttons).toEqual(['Restore', 'Cancel'])
    expect(replacementsApi.restore).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    const result = await screen.findByRole('status')
    expect(result).toHaveTextContent('Restored: 2 files are back where they were.')
    expect(result).toHaveClass('ui-banner')
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
    expect(downloadsApi.get).toHaveBeenCalledTimes(2)
  })

  it('puts nothing back on Cancel', async () => {
    // Arrange
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Restore' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Put the old copy back?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.restore).not.toHaveBeenCalled()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('offers Restore, and Retry, for a replacement in review that holds earlier copies in the quarantine, but not Delete for good', async () => {
    // Arrange
    const reason = '“Alien.mkv” couldn\'t be moved into the quarantine: the folder isn\'t on the quarantine\'s mount. 1 of 3 names is in the quarantine.'
    serve(jobWith({ state: 'NeedsReview', reason, canRetry: true }), replacementDetailsOf({
      state: 'NeedsReview', reason, canRetry: true, canRestore: true, canDeleteForGood: false,
      names: [alienVideo, { ...alienSubtitle, state: 'Planned' }],
    }))
    vi.mocked(replacementsApi.restore).mockResolvedValue(jobWith({ state: 'Restored', quarantinedCopyCount: 0 }))
    const { user } = renderPage()
    const section = await oldCopies()
    const actions = within(section).getAllByRole('button').map((button) => button.textContent)

    // Act
    await user.click(within(section).getByRole('button', { name: 'Restore' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Put the old copy back?' })).getByRole('button', { name: 'Restore' }))

    // Assert
    expect(actions).toEqual(['Retry', 'Restore'])
    expect(replacementsApi.restore).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
    expect(await screen.findByRole('status')).toHaveTextContent('Restored: 1 file is back where it was.')
  })

  it('shows the server sentence in the page\'s error banner when a restore stops part-way', async () => {
    // Arrange
    const sentence = '“Alien.en.srt” couldn\'t be put back: “/media/movies/Alien (1979)/Alien.en.srt” already holds a file. 1 of 2 names is back where it was.'
    vi.mocked(replacementsApi.restore).mockRejectedValue(apiError(409, sentence))
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Put the old copy back?' })).getByRole('button', { name: 'Restore' }))

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    expect(section).not.toContainElement(banner)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
  })

  it('says Restoring… with a spinner while the restore is on its way, and holds Delete for good meanwhile', async () => {
    // Arrange
    vi.mocked(replacementsApi.restore).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Put the old copy back?' })).getByRole('button', { name: 'Restore' }))

    // Assert
    const restoring = await within(section).findByRole('button', { name: 'Restoring…' })
    expect(restoring).toBeDisabled()
    expect(restoring.querySelector('svg')).toHaveClass('animate-spin')
    expect(within(section).getByRole('button', { name: 'Delete for good' })).toBeDisabled()
  })

  // ── Delete for good ──

  it('asks once more before deleting for good, listing every path in the quarantine with its size and the room it frees, Delete for good first', async () => {
    // Arrange
    const completed = jobWith({ state: 'Completed', quarantinedCopyCount: 0 })
    vi.mocked(replacementsApi.deleteForGood).mockResolvedValue(completed)
    const { user } = renderPage()
    const section = await oldCopies()
    vi.mocked(downloadsApi.get).mockResolvedValue(completed)
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    const listed = within(confirmation).getAllByRole('listitem').map((item) => [
      item.querySelector('.jobs-file-path')?.textContent,
      item.querySelector('.jobs-quarantine-size')?.textContent,
    ])
    const buttons = footerButtons(confirmation)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

    // Assert
    expect(confirmation).toHaveTextContent('These 2 files are deleted from the quarantine, and can\'t be restored afterwards:')
    expect(listed).toEqual([[alienVideo.originalPath, '40 GiB'], [alienSubtitle.originalPath, '50 KiB']])
    expect(confirmation).not.toHaveTextContent(alienHeldSubtitle.originalPath)
    expect(confirmation).toHaveTextContent('Frees 40 GiB.')
    expect(confirmation).not.toHaveTextContent(/another name/)
    expect(buttons.map((button) => button.textContent)).toEqual(['Delete for good', 'Cancel'])
    expect(buttons[0]).toHaveClass('ui-button-destructive')
    expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])
    expect(await screen.findByRole('status')).toHaveTextContent('Deleted for good: 2 files.')
    expect(replacementsApi.get).toHaveBeenCalledTimes(2)
  })

  it('says how many names have another name and free nothing, with the room the server says the rest frees', async () => {
    // Arrange
    serve(jobWith(), replacementDetailsOf({ names: [{ ...alienVideo, otherNames: true }, alienSubtitle], freesBytes: alienSubtitle.sizeBytes }))
    const { user } = renderPage()
    const section = await oldCopies()

    // Act
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    expect(confirmation).toHaveTextContent('Frees 50 KiB.')
    expect(confirmation).toHaveTextContent('1 file has another name (a hardlink), so deleting it frees no space.')
  })

  it('deletes nothing on Cancel', async () => {
    // Arrange
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.deleteForGood).not.toHaveBeenCalled()
  })

  it('sends exactly the names the confirmation listed, though the replacement changed while it was open', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(409, CHANGED))
    const { client, user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    const another = nameOf({ id: 'e1a50000-0000-4000-8000-000000000104', originalPath: '/media/movies/Alien (1979)/Alien.nfo' })
    act(() => client.setQueryData(qk.replacements.job(REPLACEMENT_ID), replacementDetailsOf({ names: [alienVideo, alienSubtitle, another] })))

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

    // Assert
    expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(CHANGED)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('takes a refusal\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(409, CHANGED))
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })).getByRole('button', { name: 'Delete for good' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says its own sentence when a deletion fails and the API gives none', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })).getByRole('button', { name: 'Delete for good' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not delete the old copies. Reload the page and try again.')
  })

  it('says Deleting… with a spinner while the deletion is on its way, and holds Restore meanwhile', async () => {
    // Arrange
    vi.mocked(replacementsApi.deleteForGood).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const section = await oldCopies()
    await user.click(within(section).getByRole('button', { name: 'Delete for good' }))

    // Act
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete the old copy for good?' })).getByRole('button', { name: 'Delete for good' }))

    // Assert
    const deleting = await within(section).findByRole('button', { name: 'Deleting…' })
    expect(deleting).toBeDisabled()
    expect(deleting.querySelector('svg')).toHaveClass('animate-spin')
    expect(within(section).getByRole('button', { name: 'Restore' })).toBeDisabled()
  })

  // ── switched off ──

  it('disables Restore and Delete for good, saying why on hover and in a line of its own, while replacement is switched off; Retry stays', async () => {
    // Arrange
    const reason = '“Alien.mkv” couldn\'t be moved into the quarantine: the folder is read-only. 1 of 3 names is in the quarantine.'
    serve(jobWith({ state: 'NeedsReview', reason, canRetry: true, switchedOff: true }), replacementDetailsOf({
      state: 'NeedsReview', reason, switchedOff: true, canRetry: true, canRestore: true, canDeleteForGood: true,
    }))
    const { user } = renderPage()
    const section = await oldCopies()

    // Act
    await user.click(within(section).getByRole('button', { name: 'Restore' }))

    // Assert
    for (const name of ['Restore', 'Delete for good']) {
      const button = within(section).getByRole('button', { name })
      expect(button).toBeDisabled()
      expect(button.closest('.settings-action-reason')).toHaveAttribute('title', SWITCHED_OFF)
    }
    expect(within(section).getByRole('button', { name: 'Retry' })).toBeEnabled()
    expect(within(section).getByText(SWITCHED_OFF).closest('p')).toHaveClass('library-source-warning')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(replacementsApi.restore).not.toHaveBeenCalled()
  })

  // ── asked again as the job moves ──

  describe('while the job moves', () => {
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    /** Lets the polling interval pass once, with the answers it brings. */
    const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

    it('asks for the old copies again when the job comes back with its replacement changed, and only then', async () => {
      // Arrange
      serve(jobWith({ state: 'Quarantining', quarantinedCopyCount: 0 }), replacementDetailsOf({
        state: 'Quarantining', canRestore: false, canDeleteForGood: false, quarantinedBytes: 0, freesBytes: 0,
        names: [nameOf({ state: 'Moving' }), { ...alienSubtitle, state: 'Planned' }],
      }))
      renderPage()
      const section = await oldCopies()
      await within(section).findByText('Being moved into the quarantine…')

      // Act
      await nextPoll()
      const unchanged = vi.mocked(replacementsApi.get).mock.calls.length
      serve(jobWith(), replacementDetailsOf())
      await nextPoll()

      // Assert
      expect(unchanged).toBe(1)
      expect(await within(section).findByText('The quarantine holds 40 GiB of this replacement, in 2 files.')).toBeInTheDocument()
      expect(replacementsApi.get).toHaveBeenCalledTimes(2)
      expect(within(section).getByRole('button', { name: 'Delete for good' })).toBeEnabled()
    })
  })
})
