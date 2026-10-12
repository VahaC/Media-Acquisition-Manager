import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DuplicateGroupCard } from '@/components/duplicates/DuplicateGroupCard'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { duplicatesApi } from '@/lib/duplicates-api'
import { qk } from '@/lib/queries'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { backedUpAliens, byteIdenticalGroupOf, duplicateFileOf, libraryFileOf, possibleGroupOf, removable, sameFileGroup, sameWorkGroup } from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')

// MAM-21 — the owner confirms or dismisses a possible match on its card, or undoes the decision; a member
// sees the buttons disabled with the reason. No card acts on many groups. MAM-40 — copies of a work and exact
// copies are kept as they are instead (Keep both, Keep all), or undone once kept; one file's names have
// nothing to decide.

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

const actions = () => screen.getByRole('group', { name: 'Decide about this possible match' })

/** MAM-40 — the actions of copies of a work and exact copies. */
const copyActions = () => screen.getByRole('group', { name: 'Keep or remove these copies' })

/** MAM-40 — the Theatrical Cut, its copy on the backup disk and a third copy on another disk: Keep all. */
const threeCopies = byteIdenticalGroupOf({
  files: [
    ...byteIdenticalGroupOf().files,
    duplicateFileOf({
      file: libraryFileOf({ id: '7a1e0000-0000-4000-8000-000000000524', servicePath: '/media/spare/Aliens.mkv', appPath: '/media/spare/Aliens.mkv' }),
      deviceId: 2081,
      inode: 12,
      linkCount: 1,
      works: [],
      removal: removable,
    }),
  ],
})

const keptCopies = byteIdenticalGroupOf({ decision: { decision: 'Dismissed', decidedUtc: '2026-10-12T09:15:00Z', decidedByUserName: 'vasyl' } })

const confirmed = possibleGroupOf({ decision: { decision: 'Confirmed', decidedUtc: '2026-10-07T09:15:00Z', decidedByUserName: 'vasyl' } })

const dismissed = possibleGroupOf({ decision: { decision: 'Dismissed', decidedUtc: '2026-10-07T09:15:00Z', decidedByUserName: 'vasyl' } })

const changed = 'This group has changed since the page loaded: the list shows it again as it is now.'

describe('DuplicateGroupCard decisions', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
  })

  it('offers the owner Confirm and Dismiss on an undecided possible match, each with what it does', () => {
    // Arrange
    const group = possibleGroupOf()

    // Act
    renderCard(group)

    // Assert
    const buttons = within(actions()).getAllByRole('button')
    expect(buttons.map((button) => button.textContent)).toEqual(['Confirm', 'Dismiss'])
    expect(buttons.map((button) => button.getAttribute('title'))).toEqual([
      'These are one work: keep it as checked, with nothing removed',
      'These aren\'t one work: take the group off the list',
    ])
    expect(buttons.every((button) => !(button as HTMLButtonElement).disabled)).toBe(true)
  })

  it('confirms the match by its key and asks for every tab again, staying on the list', async () => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockResolvedValue()
    const { client, onLeft, user } = renderCard(possibleGroupOf())
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    // Act
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    // Assert
    await vi.waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll }))
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith('3d4e5f60718293a4b5c6d7e8f90a1b2c', 'Confirmed')
    expect(onLeft).not.toHaveBeenCalled()
  })

  it('says Confirming… on the button pressed and holds both while the request is on its way', async () => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockReturnValue(new Promise(() => {}))
    const { user } = renderCard(possibleGroupOf())

    // Act
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    // Assert
    const buttons = within(actions()).getAllByRole('button')
    expect(buttons.map((button) => button.textContent)).toEqual(['Confirming…', 'Dismiss'])
    expect(buttons.every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
  })

  it('dismisses the match and tells the page it left the list', async () => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockResolvedValue()
    const { onLeft, user } = renderCard(possibleGroupOf())

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    await vi.waitFor(() => expect(onLeft).toHaveBeenCalledExactlyOnceWith({ kind: 'Dismissed' }))
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith('3d4e5f60718293a4b5c6d7e8f90a1b2c', 'Dismissed')
  })

  it.each([
    ['Confirm', apiError(500), 'The match couldn\'t be confirmed.'],
    ['Dismiss', apiError(500), 'The match couldn\'t be dismissed.'],
    ['Confirm', apiError(400, 'Only a possible match is confirmed or dismissed: this group is one by its files\' identity, bytes or registry ids, which no one has to confirm.'),
      'Only a possible match is confirmed or dismissed: this group is one by its files\' identity, bytes or registry ids, which no one has to confirm.'],
  ])('says why %s failed in the card, the server\'s sentence first, and keeps the buttons', async (label, failure, sentence) => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockRejectedValue(failure)
    const { onLeft, user } = renderCard(possibleGroupOf())

    // Act
    await user.click(screen.getByRole('button', { name: label }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(onLeft).not.toHaveBeenCalled()
    expect(within(actions()).getAllByRole('button').map((button) => button.textContent)).toEqual(['Confirm', 'Dismiss'])
  })

  // MAM-21 — a bare 404: the group's members changed since the page loaded, so its key names no group now.
  it.each([
    ['Confirm', possibleGroupOf()],
    ['Dismiss', possibleGroupOf()],
    ['Undo', confirmed],
  ])('says the group changed when %s finds it gone, and asks for the list again', async (label, group) => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockRejectedValue(apiError(404))
    vi.mocked(duplicatesApi.undo).mockRejectedValue(apiError(404))
    const { client, onLeft, user } = renderCard(group)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    // Act
    await user.click(screen.getByRole('button', { name: label }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(changed)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll })
    expect(onLeft).not.toHaveBeenCalled()
  })

  it('offers Undo alone once the match is decided, which forgets the decision by its key and stays', async () => {
    // Arrange
    vi.mocked(duplicatesApi.undo).mockResolvedValue()
    const { client, onLeft, user } = renderCard(confirmed)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    // Act
    const buttons = within(actions()).getAllByRole('button')
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    // Assert
    expect(buttons.map((button) => button.textContent)).toEqual(['Undo'])
    expect(buttons[0]).toHaveAttribute('title', 'Forget the decision, so the group asks again')
    await vi.waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll }))
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith('3d4e5f60718293a4b5c6d7e8f90a1b2c')
    expect(onLeft).not.toHaveBeenCalled()
  })

  it('tells the page a dismissed match left the Dismissed tab once its Undo went through', async () => {
    // Arrange
    vi.mocked(duplicatesApi.undo).mockResolvedValue()
    const { onLeft, user } = renderCard(dismissed)

    // Act
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    // Assert
    await vi.waitFor(() => expect(onLeft).toHaveBeenCalledExactlyOnceWith({ kind: 'Restored' }))
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith('3d4e5f60718293a4b5c6d7e8f90a1b2c')
  })

  it('says why Undo failed in the card', async () => {
    // Arrange
    vi.mocked(duplicatesApi.undo).mockRejectedValue(apiError(500))
    const { user } = renderCard(confirmed)

    // Act
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The decision couldn\'t be undone.')
  })

  it.each([
    ['an undecided match', possibleGroupOf(), ['Confirm', 'Dismiss']],
    ['a decided match', confirmed, ['Undo']],
  ])('shows a member the buttons of %s disabled with the reason, and never asks the server', async (_, group, labels) => {
    // Arrange
    const { user } = renderCard(group, false)
    const buttons = within(actions()).getAllByRole('button')

    // Act
    for (const button of buttons) await user.click(button)

    // Assert
    expect(buttons.map((button) => button.textContent)).toEqual(labels)
    expect(buttons.every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
    expect(buttons.map((button) => button.parentElement?.getAttribute('title')))
      .toEqual(labels.map(() => 'Only the owner confirms or dismisses a possible match.'))
    expect(duplicatesApi.decide).not.toHaveBeenCalled()
    expect(duplicatesApi.undo).not.toHaveBeenCalled()
  })

  it.each([
    ['one file\'s names', sameFileGroup],
    ['a possible match', possibleGroupOf()],
  ])('offers no way to keep, remove, move or rename %s', async (_, group) => {
    // Arrange
    const { user } = renderCard(group)

    // Act — every file's row open in turn, where a copy's Remove would be.
    for (const row of within(screen.getByRole('list', { name: 'Files' })).getAllByRole('button')) await user.click(row)

    // Assert
    expect(screen.queryByRole('button', { name: /delete|remove|move|rename|keep/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Keep or remove these copies' })).not.toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  // ── MAM-40: Keep both ──

  it.each([
    ['exact copies', byteIdenticalGroupOf()],
    ['copies of one work', sameWorkGroup],
  ])('offers the owner Keep both on two %s, with what it does, and nothing to confirm or dismiss', (_, group) => {
    // Arrange
    const of = group

    // Act
    renderCard(of)

    // Assert
    const buttons = within(copyActions()).getAllByRole('button')
    expect(buttons.map((button) => button.textContent)).toEqual(['Keep both'])
    expect(buttons[0]).toHaveAttribute('title', 'Keep every copy as it is: the group moves to Kept as they are until a new copy joins it')
    expect(buttons[0]).toBeEnabled()
    expect(screen.queryByRole('button', { name: /Confirm|Dismiss/ })).not.toBeInTheDocument()
  })

  it('offers Keep all on three copies', () => {
    // Arrange
    const group = threeCopies

    // Act
    renderCard(group)

    // Assert
    expect(within(copyActions()).getAllByRole('button').map((button) => button.textContent)).toEqual(['Keep all'])
  })

  it('keeps the copies as the group\'s Dismissed, by its key, and tells the page they left the list', async () => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockResolvedValue()
    const { client, onLeft, user } = renderCard(byteIdenticalGroupOf())
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    // Act
    await user.click(screen.getByRole('button', { name: 'Keep both' }))

    // Assert
    await vi.waitFor(() => expect(onLeft).toHaveBeenCalledExactlyOnceWith({ kind: 'Dismissed' }))
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith('1b2c3d4e5f60718293a4b5c6d7e8f90a', 'Dismissed')
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.library.duplicatesAll })
  })

  it('says Keeping… and holds every action of the card while Keep both is on its way', async () => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockReturnValue(new Promise(() => {}))
    const { user } = renderCard(byteIdenticalGroupOf())

    // Act
    await user.click(screen.getByRole('button', { name: 'Keep both' }))

    // Assert
    expect(within(copyActions()).getByRole('button')).toHaveTextContent('Keeping…')
    expect(within(copyActions()).getByRole('button')).toBeDisabled()
    const removes = screen.getAllByRole('button', { name: 'Remove this copy' })
    expect(removes).toHaveLength(2)
    for (const remove of removes) expect(remove).toBeDisabled()
  })

  it.each([
    [apiError(500), 'The copies couldn\'t be kept.'],
    [apiError(400, 'One file\'s names have nothing to keep or remove.'), 'One file\'s names have nothing to keep or remove.'],
    [apiError(404), changed],
  ])('says why Keep both failed in the card, the server\'s sentence first', async (failure, sentence) => {
    // Arrange
    vi.mocked(duplicatesApi.decide).mockRejectedValue(failure)
    const { onLeft, user } = renderCard(byteIdenticalGroupOf())

    // Act
    await user.click(screen.getByRole('button', { name: 'Keep both' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(onLeft).not.toHaveBeenCalled()
    expect(within(copyActions()).getByRole('button')).toHaveTextContent('Keep both')
  })

  it('offers Undo alone on kept copies, which forgets the decision and tells the page they left Kept as they are', async () => {
    // Arrange
    vi.mocked(duplicatesApi.undo).mockResolvedValue()
    const { onLeft, user } = renderCard(keptCopies)
    const buttons = within(copyActions()).getAllByRole('button')

    // Act
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    // Assert
    expect(buttons.map((button) => button.textContent)).toEqual(['Undo'])
    expect(buttons[0]).toHaveAttribute('title', 'Forget the decision, so the group is back among the duplicates')
    await vi.waitFor(() => expect(onLeft).toHaveBeenCalledExactlyOnceWith({ kind: 'Restored' }))
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith('1b2c3d4e5f60718293a4b5c6d7e8f90a')
  })

  it.each([
    ['undecided copies', byteIdenticalGroupOf(), 'Keep both'],
    ['kept copies', keptCopies, 'Undo'],
  ])('shows a member the button of %s disabled with the reason, and never asks the server', async (_, group, label) => {
    // Arrange
    const { user } = renderCard(group, false)
    const button = within(copyActions()).getByRole('button')

    // Act
    await user.click(button)

    // Assert
    expect(button).toHaveTextContent(label)
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner keeps or removes copies.')
    expect(duplicatesApi.decide).not.toHaveBeenCalled()
    expect(duplicatesApi.undo).not.toHaveBeenCalled()
  })

  it('names no other copy as the one to remove: every copy has the same Remove', () => {
    // Arrange
    const group = byteIdenticalGroupOf({ files: [backedUpAliens, duplicateFileOf({ linkCount: 1, removal: removable })] })

    // Act
    renderCard(group)

    // Assert
    const [first, second] = screen.getAllByRole('button', { name: 'Remove this copy' })
    expect(second.outerHTML).toBe(first.outerHTML)
    expect(screen.queryByText(/recommend|suggest|worse|better/i)).not.toBeInTheDocument()
  })
})
