import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DuplicateGroupCard } from '@/components/duplicates/DuplicateGroupCard'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { copyRemovalsApi } from '@/lib/copy-removals-api'
import { formatFileSize } from '@/lib/format'
import { qk } from '@/lib/queries'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { removalOf } from '@/test/copy-removals'
import {
  backedUpAliens,
  byteIdenticalGroupOf,
  lastReadCopyReason,
  notInspectedReason,
  possibleGroupOf,
  sameFileGroup,
  sameWorkGroup,
} from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')
vi.mock('@/lib/copy-removals-api')

// MAM-40 — Remove this copy on each file of copies of a work and exact copies: on its row's line, with every
// row closed, and in the table's Removal row, one a file; disabled with the server's sentence when the file
// can't be removed now (a line in its open row too), or with the owner-only reason for a member; a
// confirmation naming the file, its path and size, what stays and where it goes; the request with the identity
// the page showed; and the server's refusal inline on the card.

function renderCard(group: LibraryDuplicateGroup, isOwner = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onLeft = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <DuplicateGroupCard group={group} isOwner={isOwner} onLeft={onLeft} />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, onLeft, user: userEvent.setup() }
}

/** The rows of the group's files, which open them. */
const fileRows = () =>
  within(screen.getByRole('list', { name: 'Files' })).getAllByRole('button').filter((button) => button.hasAttribute('aria-expanded'))

/** Every Remove this copy on the card. */
const removeButtons = () => screen.queryAllByRole('button', { name: 'Remove this copy' })

/** The Remove this copy of the file in row `index`, on that row's line. */
const removeOf = (index: number) =>
  within(fileRows()[index].closest('li')!).getByRole('button', { name: 'Remove this copy' })

/** A confirmation's buttons, in the order the footer lays them out. */
const footerButtons = (dialog: HTMLElement) =>
  within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')

/** The exact copies: the backup disk's copy first, the library's Theatrical Cut second. */
const exactCopies = byteIdenticalGroupOf()

const [backup, library] = exactCopies.files

describe('DuplicateGroupCard Remove this copy', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
  })

  it('offers the owner Remove this copy on each file\'s row with every row closed, beside the row, in red outline, with what it does', () => {
    // Arrange
    const group = exactCopies

    // Act
    renderCard(group)

    // Assert
    const rows = fileRows()
    expect(rows.map((row) => row.getAttribute('aria-expanded'))).toEqual(['false', 'false'])
    expect(removeButtons()).toHaveLength(2)
    rows.forEach((row, index) => {
      const remove = removeOf(index)
      // Beside the row that opens, never in it: a button holds no other button.
      expect(row).not.toContainElement(remove)
      expect(remove).toBeEnabled()
      expect(remove).toHaveClass('ui-button-danger')
      expect(remove).toHaveAttribute('title', 'Move this file into the quarantine of its media folder')
    })
    expect(screen.queryByText(/can't be removed/)).not.toBeInTheDocument()
  })

  it('keeps one Remove a file when a row is open: on its row\'s line, not again in its open part', async () => {
    // Arrange
    const { user } = renderCard(exactCopies)

    // Act
    await user.click(fileRows()[0])

    // Assert
    expect(fileRows()[0]).toHaveAttribute('aria-expanded', 'true')
    expect(removeButtons()).toHaveLength(2)
    const details = document.querySelector<HTMLElement>('.library-version-details')!
    expect(details).toHaveTextContent('SHA-256')
    expect(within(details).queryByRole('button', { name: 'Remove this copy' })).not.toBeInTheDocument()
  })

  it('opens nothing when Remove this copy is pressed: its row stays closed under the confirmation', async () => {
    // Arrange
    const { user } = renderCard(exactCopies)
    // Taken before the confirmation opens, which hides the rest of the page from the accessibility tree.
    const rows = fileRows()

    // Act
    await user.click(removeOf(1))

    // Assert
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(rows.map((row) => row.getAttribute('aria-expanded'))).toEqual(['false', 'false'])
  })

  it('offers Remove this copy under each file in the table\'s Removal row', async () => {
    // Arrange
    const { user } = renderCard(exactCopies)

    // Act
    await user.click(screen.getByRole('radio', { name: 'Table' }))

    // Assert
    const row = screen.getByRole('rowheader', { name: 'Removal' }).closest('tr')!
    const buttons = within(row).getAllByRole('button')
    expect(buttons.map((button) => button.textContent)).toEqual(['Remove this copy', 'Remove this copy'])
    expect(buttons.every((button) => !(button as HTMLButtonElement).disabled)).toBe(true)
  })

  it.each([
    [0, notInspectedReason],
    [1, lastReadCopyReason],
  ])('shows Remove of file %s disabled with the server\'s sentence on its closed row, as a line too once it is open', async (index, sentence) => {
    // Arrange
    const { user } = renderCard(sameWorkGroup)
    const closed = removeOf(index)
    const shownClosed = screen.queryByText(sentence)

    // Act
    await user.click(fileRows()[index])

    // Assert
    expect(closed).toBeDisabled()
    expect(closed.parentElement).toHaveAttribute('title', sentence)
    expect(shownClosed).not.toBeInTheDocument()
    const line = screen.getByText(sentence).closest('p')
    expect(line).toHaveClass('library-source-warning')
    expect(line!.closest('.library-version-details')).not.toBeNull()
    expect(removeButtons()).toHaveLength(2)
  })

  it('says only that a copy can\'t be removed now when the server gives no sentence', async () => {
    // Arrange
    const group = byteIdenticalGroupOf({ files: [{ ...backup, removal: { allowed: false, reason: null } }, library] })
    const { user } = renderCard(group)

    // Act
    await user.click(fileRows()[0])

    // Assert
    expect(removeOf(0).parentElement).toHaveAttribute('title', 'This copy can\'t be removed now.')
    expect(screen.getByText('This copy can\'t be removed now.')).toBeInTheDocument()
    expect(removeOf(1)).toBeEnabled()
  })

  it('shows a member every Remove disabled with the owner-only reason on the closed rows, without the server\'s sentence, and never asks', async () => {
    // Arrange
    const { user } = renderCard(sameWorkGroup, false)

    // Act
    await user.click(removeOf(0))
    await user.click(fileRows()[0])

    // Assert
    expect(removeButtons()).toHaveLength(2)
    for (const remove of removeButtons()) {
      expect(remove).toBeDisabled()
      expect(remove.parentElement).toHaveAttribute('title', 'Only the owner keeps or removes copies.')
    }
    expect(screen.queryByText(notInspectedReason)).not.toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(copyRemovalsApi.remove).not.toHaveBeenCalled()
  })

  it.each([
    ['one file\'s names', sameFileGroup],
    ['a possible match', possibleGroupOf()],
  ])('offers no Remove on %s, even in the table', async (_, group) => {
    // Arrange
    const { user } = renderCard(group)

    // Act
    await user.click(screen.getByRole('radio', { name: 'Table' }))

    // Assert
    expect(screen.queryByRole('rowheader', { name: 'Removal' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove this copy' })).not.toBeInTheDocument()
  })

  it.each([
    ['one file\'s names', sameFileGroup],
    ['a possible match', possibleGroupOf()],
  ])('offers no Remove on %s in the rows, closed or open', async (_, group) => {
    // Arrange
    const { user } = renderCard(group)
    const closed = removeButtons()

    // Act
    for (const row of fileRows()) await user.click(row)

    // Assert
    expect(closed).toEqual([])
    expect(removeButtons()).toEqual([])
    expect(document.querySelector('.library-version-actions')).toBeNull()
  })

  it('asks first, naming the file, its path and size, what stays and where it goes, with Remove this copy first and Cancel last', async () => {
    // Arrange
    const { user } = renderCard(exactCopies)

    // Act
    await user.click(removeOf(0))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Remove “Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv”?' })
    expect(dialog).toHaveTextContent('This file moves into the quarantine folder at the top of its media folder, with its subtitles and other files of the same name:')
    expect(dialog).toHaveTextContent('You can restore it or delete it permanently under Removed copies on this page.')
    expect(dialog).toHaveTextContent('This file stays:')
    const [removed, stays] = within(dialog).getAllByRole('list')
    expect(within(removed).getAllByRole('listitem').map((item) => item.textContent))
      .toEqual([`${backup.file.appPath}${formatFileSize(backup.readSizeBytes!)}`])
    expect(within(stays).getAllByRole('listitem').map((item) => item.textContent))
      .toEqual([`${library.file.appPath}${formatFileSize(library.readSizeBytes!)}`])
    const buttons = footerButtons(dialog)
    expect(buttons.map((button) => button.textContent)).toEqual(['Remove this copy', 'Cancel'])
    // Not red: the quarantine keeps the copy, and Removed copies puts it back.
    expect(buttons[0]).not.toHaveClass('ui-button-destructive')
  })

  it('names every file that stays, by its count', async () => {
    // Arrange
    const third = { ...library, file: { ...library.file, id: '7a1e0000-0000-4000-8000-000000000524', appPath: '/media/spare/Aliens.mkv' } }
    const { user } = renderCard(byteIdenticalGroupOf({ files: [backup, library, third] }))

    // Act
    await user.click(removeOf(0))

    // Assert
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('These files stay:')
    expect(within(within(dialog).getAllByRole('list')[1]).getAllByRole('listitem')).toHaveLength(2)
  })

  it('sends nothing when the owner cancels', async () => {
    // Arrange
    const { onLeft, user } = renderCard(exactCopies)
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(copyRemovalsApi.remove).not.toHaveBeenCalled()
    expect(onLeft).not.toHaveBeenCalled()
  })

  it('removes the copy with the identity the page showed, asks for the lists again, and tells the page which file left', async () => {
    // Arrange
    vi.mocked(copyRemovalsApi.remove).mockResolvedValue(removalOf())
    const { client, onLeft, user } = renderCard(exactCopies)
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    await vi.waitFor(() => expect(onLeft).toHaveBeenCalledExactlyOnceWith({
      kind: 'Removed',
      path: backedUpAliens.file.appPath,
      needsReview: false,
      reason: null,
    }))
    expect(copyRemovalsApi.remove).toHaveBeenCalledExactlyOnceWith({
      groupKey: exactCopies.key,
      fileId: backup.file.id,
      deviceId: 2065,
      inode: 77,
      sizeBytes: 14_845_708_473,
    })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.replacements.removalsAll })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.settings.allowedRoots })
  })

  it('tells the page when the removal it answered needs review, with the server\'s sentence', async () => {
    // Arrange
    const reason = '“Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.en.srt” couldn\'t be moved into the quarantine: permission denied.'
    vi.mocked(copyRemovalsApi.remove).mockResolvedValue(removalOf({ state: 'NeedsReview', reason }))
    const { onLeft, user } = renderCard(exactCopies)
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    await vi.waitFor(() => expect(onLeft).toHaveBeenCalledExactlyOnceWith({
      kind: 'Removed',
      path: backedUpAliens.file.appPath,
      needsReview: true,
      reason,
    }))
  })

  it('sends the library\'s size when the app read none', async () => {
    // Arrange
    vi.mocked(copyRemovalsApi.remove).mockResolvedValue(removalOf())
    const group = byteIdenticalGroupOf({ files: [{ ...backup, readSizeBytes: null, file: { ...backup.file, sizeBytes: 14_845_708_000 } }, library] })
    const { user } = renderCard(group)
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    await vi.waitFor(() => expect(copyRemovalsApi.remove).toHaveBeenCalledOnce())
    expect(vi.mocked(copyRemovalsApi.remove).mock.calls[0][0].sizeBytes).toBe(14_845_708_000)
  })

  it('says Removing… on the button pressed and holds the card\'s other actions while the move is on its way', async () => {
    // Arrange
    vi.mocked(copyRemovalsApi.remove).mockReturnValue(new Promise(() => {}))
    const { user } = renderCard(exactCopies)
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    const pending = await screen.findByRole('button', { name: 'Removing…' })
    expect(pending).toBeDisabled()
    expect(fileRows()[0].closest('li')).toContainElement(pending)
    expect(removeOf(1)).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Keep both' })).toBeDisabled()
  })

  it.each([
    ['refuses it', apiError(409, 'Still seeded by torrent “Aliens.1986.BDRip” — remove it in qBittorrent without its files, then press Remove again.'),
      'Still seeded by torrent “Aliens.1986.BDRip” — remove it in qBittorrent without its files, then press Remove again.'],
    ['refuses the request', apiError(400, 'The request names no file.'), 'The request names no file.'],
    ['fails', apiError(500), 'The copy couldn\'t be removed.'],
    ['finds the group gone', apiError(404), 'This group has changed since the page loaded: the list shows it again as it is now.'],
  ])('says in the card why the removal failed when the server %s, and keeps the card', async (_, failure, sentence) => {
    // Arrange
    vi.mocked(copyRemovalsApi.remove).mockRejectedValue(failure)
    const { onLeft, user } = renderCard(exactCopies)
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(onLeft).not.toHaveBeenCalled()
    expect(removeOf(0)).toBeEnabled()
  })

  it.each([
    ['a check refused it', 409],
    ['the group changed', 404],
  ])('asks for the list again when %s, so the card shows the group as it is now', async (_, status) => {
    // Arrange
    vi.mocked(copyRemovalsApi.remove).mockRejectedValue(apiError(status, status === 409 ? 'Changed since the app inspected it.' : undefined))
    const { client, user } = renderCard(exactCopies)
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    await user.click(removeOf(0))

    // Act
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    await vi.waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll }))
    expect(invalidate).not.toHaveBeenCalledWith({ queryKey: qk.replacements.removalsAll })
  })
})
