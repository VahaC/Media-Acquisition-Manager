import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { copyRemovalsApi } from '@/lib/copy-removals-api'
import { duplicatesApi } from '@/lib/duplicates-api'
import { formatDateTime, formatFileSize } from '@/lib/format'
import { qk } from '@/lib/queries'
import type { CopyRemoval } from '@/lib/types'
import { DuplicatesPage } from '@/pages/DuplicatesPage'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import { REMOVAL_ID, removalOf, removalsPage, removedNameOf, removedSubtitle, removedVideoPath } from '@/test/copy-removals'

vi.mock('@/lib/duplicates-api')
vi.mock('@/lib/copy-removals-api')

// MAM-40 — the owner's Removed copies tab: the copies he removed that are still in the quarantine, paged by
// the server, with the room they take; each with its paths, size, when and by whom, its state and its files,
// and Restore and Delete for good behind a confirmation, each disabled when the server says it can't be done.

function renderPage(path = '/library/duplicates?tab=Removed') {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/library/duplicates" element={<DuplicatesPage />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The removal's card, once the list is on screen. */
const card = () => screen.findByRole('article', { name: 'Чужі (1986)' })

/** A card's facts, as label and value pairs. */
function facts(of: HTMLElement) {
  return [...of.querySelectorAll('.duplicates-facts > div')].map((pair) => [pair.querySelector('dt')!.textContent, pair.querySelector('dd')!.textContent])
}

/** A confirmation's buttons, in the order the footer lays them out. */
const footerButtons = (dialog: HTMLElement) =>
  within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')

const fileName = 'Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv'

/** The server with `removals` open; Restore and Delete for good end one, so the list no longer has it. */
function serverWith(...removals: CopyRemoval[]) {
  let open = removals
  vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => removalsPage(query, open))
  const end = (state: CopyRemoval['state']) => async (id: string) => {
    const ended = open.find((removal) => removal.id === id)!
    open = open.filter((removal) => removal.id !== id)
    return { ...ended, state, canRestore: false, canDeleteForGood: false }
  }
  vi.mocked(copyRemovalsApi.restore).mockImplementation(end('Restored'))
  vi.mocked(copyRemovalsApi.deleteForGood).mockImplementation(end('Deleted'))
}

/** Another copy removed earlier, of Yellowstone's first episode, listed after the Theatrical Cut's. */
const otherRemoval = removalOf({
  id: 'c0e40000-0000-4000-8000-000000000002',
  title: 'Єллоустоун · S01E01 · Daybreak',
  originalPath: '/media/tv/Yellowstone/Season 01/Yellowstone S01E01 Daybreak WEB-DL.mkv',
  quarantinePath: '/media/.mam-quarantine/c0e40000-0000-4000-8000-000000000002/tv/Yellowstone/Season 01/Yellowstone S01E01 Daybreak WEB-DL.mkv',
  createdUtc: '2026-10-11T18:00:00Z',
})

/** The tab's sentence for Restore and Delete for good while replacement is off. */
const switchedOffReason = 'Replacing and removing copies is switched off on the Media folders page: nothing is put back or deleted while it is off.'

/** The server with `removals` open while replacement is switched off: it refuses Restore and Delete for good. */
function switchedOffServerWith(...removals: CopyRemoval[]) {
  vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => ({ ...removalsPage(query, removals), switchedOff: true }))
}

/**
 * The server with `removals` open, which refuses `action` on the first one with `failure` and ends it meanwhile:
 * the list asked for again no longer has it.
 */
function endedMeanwhileServerWith(failure: Error, ...removals: CopyRemoval[]) {
  let open = removals
  vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => removalsPage(query, open))
  const refuse = async () => {
    open = open.slice(1)
    throw failure
  }
  vi.mocked(copyRemovalsApi.restore).mockImplementation(refuse)
  vi.mocked(copyRemovalsApi.deleteForGood).mockImplementation(refuse)
}

describe('DuplicatesPage Removed copies', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
  })

  it('shows the room the quarantine holds, and each removal with its paths, size, when and by whom, its state and its files', async () => {
    // Arrange
    const removal = removalOf()
    serverWith(removal)

    // Act
    renderPage()

    // Assert
    const shown = await card()
    expect(screen.getByText(`The quarantine holds ${formatFileSize(removal.sizeBytes)} of removed copies.`)).toBeInTheDocument()
    expect(within(shown).getByText(fileName)).toBeInTheDocument()
    expect(facts(shown)).toEqual([
      ['Was at', removedVideoPath],
      ['In the quarantine', removal.quarantinePath],
      ['Size', formatFileSize(removal.sizeBytes)],
      ['Removed', `${formatDateTime(removal.createdUtc, { dateStyle: 'medium', timeStyle: 'short' })} by vasyl`],
      ['Where it is now', 'In the quarantine.'],
    ])
    const table = within(shown).getByRole('table', { name: 'Its files' })
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['File', 'Size', 'Where it is now'])
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => within(row).getAllByRole('cell')[0].getAttribute('data-label'))).toEqual(['File', 'File'])
    expect(rows[1]).toHaveTextContent(removedSubtitle.originalPath)
    expect(rows[1]).toHaveTextContent(`Quarantine: ${removedSubtitle.quarantinePath}`)
    expect(rows[1]).toHaveTextContent('In the quarantine.')
    expect(within(shown).getAllByRole('button').map((button) => button.textContent)).toEqual(['Restore', 'Delete permanently'])
    expect(within(shown).getByRole('button', { name: 'Delete permanently' })).toHaveClass('ui-button-danger')
    expect(within(shown).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says what needs review in plain words, with the server\'s sentence and each name\'s own', async () => {
    // Arrange
    serverWith(removalOf({
      state: 'NeedsReview',
      reason: 'The subtitle could not be moved: it stays in its folder.',
      canDeleteForGood: false,
      names: [removedNameOf(), { ...removedSubtitle, state: 'Stayed', problem: 'Permission denied.' }],
    }))

    // Act
    renderPage()

    // Assert
    const shown = await card()
    expect(facts(shown).at(-1)).toEqual(['Where it is now', 'Needs your review.'])
    expect(within(shown).getByText('The subtitle could not be moved: it stays in its folder.')).toBeInTheDocument()
    expect(within(shown).getByText('Stayed in its folder.')).toBeInTheDocument()
    expect(within(shown).getByText('Permission denied.')).toBeInTheDocument()
  })

  it.each([
    ['Restore', { canRestore: false }],
    ['Delete permanently', { canDeleteForGood: false }],
  ])('shows %s disabled with the reason, as a line too, when the server says it can\'t be done now', async (label, changes) => {
    // Arrange
    serverWith(removalOf(changes))
    const { user } = renderPage()
    const shown = await card()
    const button = within(shown).getByRole('button', { name: label })

    // Act
    await user.click(button)

    // Assert
    expect(button).toBeDisabled()
    const reason = 'Not now: something of it is still under way, or nothing of it is in the quarantine to act on.'
    expect(button.parentElement).toHaveAttribute('title', reason)
    expect(within(shown).getByText(reason)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('holds Restore and Delete for good with the reason while replacement is switched off, and says so once with the way to Media folders', async () => {
    // Arrange
    switchedOffServerWith(removalOf(), otherRemoval)
    const { user } = renderPage()
    const shown = await card()
    const restore = within(shown).getByRole('button', { name: 'Restore' })

    // Act
    await user.click(restore)

    // Assert
    for (const label of ['Restore', 'Delete permanently']) {
      const button = within(shown).getByRole('button', { name: label })
      expect(button).toBeDisabled()
      expect(button.parentElement).toHaveAttribute('title', switchedOffReason)
    }
    expect(screen.getAllByText(/^Replacing and removing copies is switched off/)).toHaveLength(1)
    const line = screen.getByText(/^Replacing and removing copies is switched off/).closest('p')!
    expect(line).toHaveTextContent('Replacing and removing copies is switched off: nothing is put back or deleted while it is off. Switch it on in Media folders.')
    expect(within(line).getByRole('link', { name: 'Media folders' })).toHaveAttribute('href', '/settings/media-folders')
    expect(within(shown).queryByText('Not now: something of it is still under way, or nothing of it is in the quarantine to act on.')).not.toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(copyRemovalsApi.restore).not.toHaveBeenCalled()
  })

  it('gives the removal\'s own reason over the switch\'s for an action the removal rules out anyway', async () => {
    // Arrange
    switchedOffServerWith(removalOf({ canRestore: false }))

    // Act
    renderPage()

    // Assert
    const shown = await card()
    expect(within(shown).getByRole('button', { name: 'Restore' }).parentElement)
      .toHaveAttribute('title', 'Not now: something of it is still under way, or nothing of it is in the quarantine to act on.')
    expect(within(shown).getByRole('button', { name: 'Delete permanently' }).parentElement).toHaveAttribute('title', switchedOffReason)
  })

  it('leaves out the switched-off line when no removal on the page could act with the switch on', async () => {
    // Arrange
    switchedOffServerWith(removalOf({ canRestore: false, canDeleteForGood: false }))

    // Act
    renderPage()

    // Assert
    await card()
    expect(screen.queryByText(/^Replacing and removing copies is switched off/)).not.toBeInTheDocument()
  })

  it('says no removed copy is in the quarantine when there is none', async () => {
    // Arrange
    serverWith()

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('No removed copy is in the quarantine. A copy you remove on its group\'s card waits here until you restore it or delete it permanently.'))
      .toBeInTheDocument()
    expect(screen.queryByText(/The quarantine holds/)).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('pages through the server\'s removals and asks for the page size picked, from page 1', async () => {
    // Arrange
    vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => removalsPage(query, [removalOf()], 30, 445_371_254_190))
    const { user } = renderPage()
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '10')

    // Assert
    expect(await screen.findByText('1–10 of 30')).toBeInTheDocument()
    expect(vi.mocked(copyRemovalsApi.list).mock.calls.map(([query]) => query)).toEqual([
      { page: 1, pageSize: 25 },
      { page: 2, pageSize: 25 },
      { page: 1, pageSize: 10 },
    ])
    expect(screen.getByText(`The quarantine holds ${formatFileSize(445_371_254_190)} of removed copies.`)).toBeInTheDocument()
    expect(duplicatesApi.list).not.toHaveBeenCalled()
  })

  it('asks before putting the copy back, listing what goes back, with Restore first and Cancel last', async () => {
    // Arrange
    serverWith(removalOf())
    const { user } = renderPage()
    const shown = await card()

    // Act
    await user.click(within(shown).getByRole('button', { name: 'Restore' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: `Put “${fileName}” back?` })
    expect(dialog).toHaveTextContent('These 2 files go back where they were, never over a file there now')
    expect(within(dialog).getAllByRole('listitem').map((item) => [
      item.querySelector('.jobs-file-path')?.textContent,
      item.querySelector('.jobs-quarantine-size')?.textContent,
    ])).toEqual([
      [removedVideoPath, formatFileSize(14_845_708_473)],
      [removedSubtitle.originalPath, formatFileSize(61_440)],
    ])
    expect(footerButtons(dialog).map((button) => button.textContent)).toEqual(['Restore', 'Cancel'])
    expect(footerButtons(dialog)[0]).not.toHaveClass('ui-button-destructive')
    // MAM-41 — only Delete for good asks in the wide confirmation, as on the Jobs page.
    expect(dialog).not.toHaveClass('confirm-dialog-wide')
  })

  it('puts the copy back by its id, says so above the list, and asks for what it changed again', async () => {
    // Arrange
    serverWith(removalOf())
    const { client, user } = renderPage()
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    await user.click(within(await card()).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Restore' }))

    // Assert
    expect(await screen.findByRole('status')).toHaveTextContent(`Restored “${fileName}”: it is back where it was.`)
    expect(copyRemovalsApi.restore).toHaveBeenCalledExactlyOnceWith(REMOVAL_ID)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.replacements.removalsAll })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.settings.allowedRoots })
    expect(await screen.findByText(/^No removed copy is in the quarantine/)).toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it('sends nothing when the owner cancels the restore', async () => {
    // Arrange
    serverWith(removalOf())
    const { user } = renderPage()
    await user.click(within(await card()).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(copyRemovalsApi.restore).not.toHaveBeenCalled()
  })

  it('asks once more before deleting for good, in the wide confirmation, naming every path with its size and the room it frees, Delete for good first in red', async () => {
    // Arrange
    serverWith(removalOf({ freesBytes: 61_440, names: [removedNameOf({ otherNames: true }), removedSubtitle] }))
    const { user } = renderPage()

    // Act
    await user.click(within(await card()).getByRole('button', { name: 'Delete permanently' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: `Delete “${fileName}” permanently?` })
    expect(dialog).toHaveTextContent('These 2 files are deleted from the quarantine, and can\'t be restored afterwards:')
    expect(within(dialog).getAllByRole('listitem').map((item) => [
      item.querySelector('.jobs-file-path')?.textContent,
      item.querySelector('.jobs-quarantine-size')?.textContent,
    ])).toEqual([
      [removedVideoPath, formatFileSize(14_845_708_473)],
      [removedSubtitle.originalPath, formatFileSize(61_440)],
    ])
    expect(dialog).toHaveTextContent(`Frees ${formatFileSize(61_440)}.`)
    expect(dialog).toHaveTextContent('1 file has another name (a hardlink), so deleting it frees no space.')
    const buttons = footerButtons(dialog)
    expect(buttons.map((button) => button.textContent)).toEqual(['Delete permanently', 'Cancel'])
    expect(buttons[0]).toHaveClass('ui-button-destructive')
    expect(dialog).toHaveClass('confirm-dialog', 'confirm-dialog-wide')
  })

  it('deletes the copy for good by its id, with no names, and says so above the list', async () => {
    // Arrange
    serverWith(removalOf())
    const { user } = renderPage()
    await user.click(within(await card()).getByRole('button', { name: 'Delete permanently' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete permanently' }))

    // Assert
    expect(await screen.findByRole('status')).toHaveTextContent(`Deleted “${fileName}” permanently.`)
    expect(copyRemovalsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REMOVAL_ID)
    expect(await screen.findByText(/^No removed copy is in the quarantine/)).toBeInTheDocument()
  })

  it.each([
    ['Restore', `Restored “${fileName}”: it is back where it was.`],
    ['Delete permanently', `Deleted “${fileName}” permanently.`],
  ])('moves the focus from the card %s took away to what it did, above the removals left', async (action, done) => {
    // Arrange
    serverWith(removalOf(), otherRemoval)
    const { user } = renderPage()
    await user.click(within(await card()).getByRole('button', { name: action }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: action }))

    // Assert
    const result = await screen.findByRole('status')
    expect(result).toHaveTextContent(done)
    await vi.waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(1))
    expect(result).toHaveFocus()
  })

  it.each(['Restore', 'Delete permanently'])('moves the focus to the empty state when %s leaves no removed copy', async (action) => {
    // Arrange
    serverWith(removalOf())
    const { user } = renderPage()
    await user.click(within(await card()).getByRole('button', { name: action }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: action }))

    // Assert
    expect(await screen.findByText(/^No removed copy is in the quarantine/)).toHaveFocus()
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it.each([
    ['Restore', apiError(409, 'Nothing of this removal is in the quarantine, so there\'s nothing to put back.'),
      'Nothing of this removal is in the quarantine, so there\'s nothing to put back.'],
    ['Delete permanently', apiError(409, 'This removal holds nothing in the quarantine to delete.'),
      'This removal holds nothing in the quarantine to delete.'],
    ['Restore', apiError(404), 'This removal has ended meanwhile: the list shows the removed copies as they are now.'],
  ])('says above the list why %s failed once the list, asked for again, no longer has the card, and moves the focus there', async (action, failure, sentence) => {
    // Arrange
    endedMeanwhileServerWith(failure, removalOf(), otherRemoval)
    const { user } = renderPage()
    await user.click(within(await card()).getByRole('button', { name: action }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: action }))

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent(`“${fileName}”: ${sentence}`)
    expect(screen.queryByRole('article', { name: 'Чужі (1986)' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('article')).toHaveLength(1)
    expect(banner).toHaveFocus()
  })

  it.each([
    ['Restore', 'Restore', apiError(409, 'Replacement is switched off on the Media folders page: nothing is put back while it is off.'),
      'Replacement is switched off on the Media folders page: nothing is put back while it is off.'],
    ['Restore', 'Restore', apiError(500), 'Could not put the copy back. Reload the page and try again.'],
    ['Delete permanently', 'Delete permanently', apiError(409, 'A file changed in the quarantine: nothing was deleted.'),
      'A file changed in the quarantine: nothing was deleted.'],
    ['Delete permanently', 'Delete permanently', apiError(500), 'Could not delete the copy. Reload the page and try again.'],
  ])('says on the card why %s failed, the server\'s sentence first, and keeps the card', async (action, confirmLabel, failure, sentence) => {
    // Arrange
    serverWith(removalOf())
    vi.mocked(copyRemovalsApi.restore).mockRejectedValue(failure)
    vi.mocked(copyRemovalsApi.deleteForGood).mockRejectedValue(failure)
    const { user } = renderPage()
    await user.click(within(await card()).getByRole('button', { name: action }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: confirmLabel }))

    // Assert
    const shown = await card()
    expect(await within(shown).findByRole('alert')).toHaveTextContent(sentence)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(within(shown).getByRole('button', { name: action })).toBeEnabled()
  })

  it('says Restoring… on the button pressed and holds Delete for good while the restore is on its way', async () => {
    // Arrange
    serverWith(removalOf())
    vi.mocked(copyRemovalsApi.restore).mockReturnValue(new Promise(() => {}))
    const { user } = renderPage()
    const shown = await card()
    await user.click(within(shown).getByRole('button', { name: 'Restore' }))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Restore' }))

    // Assert
    expect(await within(shown).findByRole('button', { name: 'Restoring…' })).toBeDisabled()
    expect(within(shown).getByRole('button', { name: 'Delete permanently' })).toBeDisabled()
  })

  it('keeps the same cards on screen under a banner when a refresh fails', async () => {
    // Arrange
    serverWith(removalOf())
    const { client } = renderPage()
    await card()
    vi.mocked(copyRemovalsApi.list).mockRejectedValue(apiError(500, 'The database is unavailable.'))

    // Act
    await client.refetchQueries({ queryKey: qk.replacements.removalsAll })

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refresh failed, showing the removed copies as last loaded. The database is unavailable.')
    expect(await card()).toBeInTheDocument()
  })

  it('shows a first load that failed with its sentence and Try again, which asks again and shows the removals', async () => {
    // Arrange
    serverWith(removalOf())
    vi.mocked(copyRemovalsApi.list).mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
    const { user } = renderPage()
    const failure = await screen.findByRole('alert')
    const shown = failure.textContent

    // Act
    await user.click(within(failure).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(shown).toContain('Could not load the removed copies.')
    expect(shown).toContain('The database is unavailable.')
    expect(await card()).toBeInTheDocument()
    expect(copyRemovalsApi.list).toHaveBeenCalledTimes(2)
  })
})
