import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { copyRemovalsApi } from '@/lib/copy-removals-api'
import { duplicatesApi } from '@/lib/duplicates-api'
import type { CurrentUser, DuplicateDecision, LibraryDuplicateGroup } from '@/lib/types'
import { DuplicatesPage } from '@/pages/DuplicatesPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { removalOf, removalsPage } from '@/test/copy-removals'
import { byteIdenticalGroupOf, duplicateFileOf, duplicatesPage, libraryFileOf, possibleGroupOf, removable, sameWorkGroup } from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')
vi.mock('@/lib/copy-removals-api')

// MAM-21 — the owner's decisions as the page shows them: a dismissed match leaves the list for the
// Dismissed tab, and a short row with Undo takes its card's place, as "Not interested" leaves a
// recommendation; an undone dismissal leaves Dismissed the same way, with Dismiss again; a confirmed
// match stays, shown as confirmed; a member reads the decisions and can't make one. MAM-40 — copies kept as
// they are leave the same way for Kept as they are, with Undo; a removed copy leaves a row naming its file
// with the way to Removed copies, and a group with files enough left comes back without it.

/** The address the page is at, so a test can read where the way to Removed copies leads. */
function Address() {
  const location = useLocation()
  return <output aria-label="Address">{location.pathname + location.search}</output>
}

function renderPage(path = '/library/duplicates', signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/library/duplicates" element={<DuplicatesPage />} />
          </Routes>
          <Address />
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const key = possibleGroupOf().key

const dismissedRow = '“Чужі (1986)” is dismissed: it is listed under Kept as they are now.'

const restoredRow = '“Чужі (1986)” is back among the possible matches.'

/**
 * The server as the decisions change it: the possible match carries the decision stored for it, a
 * dismissed one is listed under Dismissed alone, after the copies of Yellowstone's first episode, and
 * Confirm, Dismiss and Undo store or forget it.
 */
function serverWith(initial: DuplicateDecision | null = null) {
  let stored = initial
  vi.mocked(duplicatesApi.list).mockImplementation(async (query) => {
    const match = possibleGroupOf({ decision: stored })
    const dismissed = stored?.decision === 'Dismissed'
    if (query.decision === 'Dismissed') return duplicatesPage(query, dismissed ? [match] : [])
    return duplicatesPage(query, dismissed ? [sameWorkGroup] : [sameWorkGroup, match])
  })
  vi.mocked(duplicatesApi.decide).mockImplementation(async (_, decision) => {
    stored = { decision, decidedUtc: '2026-10-07T09:15:00Z', decidedByUserName: 'vasyl' }
  })
  vi.mocked(duplicatesApi.undo).mockImplementation(async () => {
    stored = null
  })
}

const keptRow = 'Kept the copies of “Єллоустоун (Season 1 · Episode 1)” as they are: the group is listed under Kept as they are now.'

const removedRow = 'Removed “Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv”: it waits in the quarantine, under Removed copies, until you restore it or delete it permanently.'

/** MAM-40 — the server's sentence for a removal whose subtitle stayed in its folder. */
const subtitleStayed = '“Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.en.srt” couldn\'t be moved into the quarantine: permission denied.'

/**
 * MAM-40 — the server as Keep both changes it: Yellowstone's copies carry the decision stored for them, kept
 * ones are listed under Dismissed alone, before the undecided possible match, and Keep both and Undo store or
 * forget it.
 */
function keptServer(initial: DuplicateDecision | null = null) {
  let stored = initial
  vi.mocked(duplicatesApi.list).mockImplementation(async (query) => {
    const copies = { ...sameWorkGroup, decision: stored }
    const kept = stored !== null
    if (query.decision === 'Dismissed') return duplicatesPage(query, kept ? [copies] : [])
    return duplicatesPage(query, kept ? [possibleGroupOf()] : [copies, possibleGroupOf()])
  })
  vi.mocked(duplicatesApi.decide).mockImplementation(async (_, decision) => {
    stored = { decision, decidedUtc: '2026-10-12T09:15:00Z', decidedByUserName: 'vasyl' }
  })
  vi.mocked(duplicatesApi.undo).mockImplementation(async () => {
    stored = null
  })
}

/**
 * MAM-40 — the server as a removal changes it: `group` before the possible match until its first copy is
 * removed, then `after` (none: a group of two is no group with one copy left); the removal is listed under
 * Removed copies.
 */
function removalServer(group: LibraryDuplicateGroup, after: LibraryDuplicateGroup | null = null) {
  let removed = false
  vi.mocked(duplicatesApi.list).mockImplementation(async (query) => {
    const shown = removed ? after : group
    return duplicatesPage(query, shown === null ? [possibleGroupOf()] : [shown, possibleGroupOf()])
  })
  vi.mocked(copyRemovalsApi.remove).mockImplementation(async () => {
    removed = true
    return removalOf()
  })
  vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => removalsPage(query, removed ? [removalOf()] : []))
}

/** Removes the first card's first file through the confirmation, from its closed row. */
async function removeFirstCopy(user: ReturnType<typeof userEvent.setup>) {
  const card = (await screen.findAllByRole('article'))[0]
  await user.click(within(card).getAllByRole('button', { name: 'Remove this copy' })[0])
  await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))
}

/** MAM-40 — the exact copies with a third copy, on a spare disk: a group with files enough left after a removal. */
function threeCopies(): LibraryDuplicateGroup {
  const spare = duplicateFileOf({
    file: libraryFileOf({ id: '7a1e0000-0000-4000-8000-000000000524', servicePath: '/media/spare/Aliens.mkv', appPath: '/media/spare/Aliens.mkv' }),
    deviceId: 2081,
    inode: 12,
    linkCount: 1,
    works: [],
    removal: removable,
  })
  return byteIdenticalGroupOf({ files: [...byteIdenticalGroupOf().files, spare] })
}

/** What each row of the list is, in order: a card by its heading, a short row by its line. */
const rowsOnScreen = () =>
  within(screen.getByRole('tabpanel')).getAllByRole('listitem')
    .filter((item) => item.parentElement?.classList.contains('duplicates-groups'))
    .map((item) => within(item).queryByRole('heading', { level: 2 })?.textContent ?? item.querySelector('.duplicates-left > span')?.textContent)

describe('DuplicatesPage decisions', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
  })

  it('puts a short row with Undo in a dismissed match\'s place, and keeps it once the list drops the match', async () => {
    // Arrange
    serverWith()
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Dismiss' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    const undo = await screen.findByRole('button', { name: 'Undo' })
    expect(undo).toHaveAccessibleDescription(dismissedRow)
    expect(undo).toHaveFocus()
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith(key, 'Dismissed')
    // Asked for again, the list no longer has the match: the row stays where its card was.
    await vi.waitFor(() => expect(duplicatesApi.list).toHaveBeenCalledTimes(2))
    expect(await screen.findByText('1–1 of 1')).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Єллоустоун', dismissedRow])
  })

  it('lists the dismissed match under Dismissed, where its card offers Undo', async () => {
    // Arrange
    serverWith({ decision: 'Dismissed', decidedUtc: '2026-10-07T09:15:00Z', decidedByUserName: 'vasyl' })

    // Act
    renderPage('/library/duplicates?tab=Dismissed')

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Чужі'])
    expect(within(screen.getByRole('article')).getByRole('button', { name: 'Undo' })).toBeInTheDocument()
  })

  it('brings the match back to its place with the row\'s Undo', async () => {
    // Arrange
    serverWith()
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Dismiss' }))
    const undo = await screen.findByRole('button', { name: 'Undo' })

    // Act
    await user.click(undo)

    // Assert
    expect(await within(screen.getAllByRole('article')[1]).findByRole('button', { name: 'Dismiss' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Єллоустоун', 'Чужі'])
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith(key)
  })

  it.each([
    ['the server failed', apiError(500), 'The decision couldn\'t be undone.'],
    // Its members changed meanwhile: the key names no group now.
    ['the group changed', apiError(404), 'This group has changed since the page loaded: the list shows it again as it is now.'],
  ])('says in the row why its Undo failed when %s, and keeps the row', async (_, error, sentence) => {
    // Arrange
    serverWith()
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Dismiss' }))
    const undo = await screen.findByRole('button', { name: 'Undo' })
    vi.mocked(duplicatesApi.undo).mockRejectedValue(error)

    // Act
    await user.click(undo)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(rowsOnScreen()).toEqual(['Єллоустоун', dismissedRow])
  })

  it('puts a short row with Dismiss again in the place of a dismissal undone on Dismissed', async () => {
    // Arrange
    serverWith({ decision: 'Dismissed', decidedUtc: '2026-10-07T09:15:00Z', decidedByUserName: 'vasyl' })
    const { user } = renderPage('/library/duplicates?tab=Dismissed')
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    // Assert
    const again = await screen.findByRole('button', { name: 'Dismiss again' })
    expect(again).toHaveAccessibleDescription(restoredRow)
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith(key)
    await vi.waitFor(() => expect(duplicatesApi.list).toHaveBeenCalledTimes(2))
    expect(rowsOnScreen()).toEqual([restoredRow])
  })

  it('dismisses the match again from that row, and its card comes back under Dismissed', async () => {
    // Arrange
    serverWith({ decision: 'Dismissed', decidedUtc: '2026-10-07T09:15:00Z', decidedByUserName: 'vasyl' })
    const { user } = renderPage('/library/duplicates?tab=Dismissed')
    await user.click(await screen.findByRole('button', { name: 'Undo' }))
    const again = await screen.findByRole('button', { name: 'Dismiss again' })

    // Act
    await user.click(again)

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Чужі'])
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith(key, 'Dismissed')
  })

  it('drops the short rows on another tab', async () => {
    // Arrange
    serverWith()
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Dismiss' }))
    await screen.findByRole('button', { name: 'Undo' })

    // Act
    await user.click(screen.getByRole('tab', { name: 'Same film, different files' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Єллоустоун' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Єллоустоун'])
    expect(screen.queryByText(dismissedRow)).not.toBeInTheDocument()
  })

  it('keeps a confirmed match on the list, shown as confirmed with Undo', async () => {
    // Arrange
    serverWith()
    const { user } = renderPage()

    // Act
    await user.click(await screen.findByRole('button', { name: 'Confirm' }))

    // Assert
    const card = screen.getAllByRole('article')[1]
    expect(await within(card).findByRole('button', { name: 'Undo' })).toBeInTheDocument()
    expect(within(card).getByText(/^Confirmed by vasyl on/)).toBeInTheDocument()
    expect(within(card).queryByText('Needs review')).not.toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Єллоустоун', 'Чужі'])
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith(key, 'Confirmed')
  })

  // ── MAM-40: Keep both ──

  it('puts a short row with Undo in the place of copies kept as they are, and keeps it once the list drops them', async () => {
    // Arrange
    keptServer()
    const { user } = renderPage()
    await screen.findByRole('button', { name: 'Keep both' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Keep both' }))

    // Assert
    const undo = await screen.findByRole('button', { name: 'Undo' })
    expect(undo).toHaveAccessibleDescription(keptRow)
    expect(undo).toHaveFocus()
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith(sameWorkGroup.key, 'Dismissed')
    await vi.waitFor(() => expect(duplicatesApi.list).toHaveBeenCalledTimes(2))
    expect(await screen.findByText('1–1 of 1')).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual([keptRow, 'Чужі'])
  })

  it('brings the copies back to their place with the row\'s Undo', async () => {
    // Arrange
    keptServer()
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Keep both' }))

    // Act
    await user.click(await screen.findByRole('button', { name: 'Undo' }))

    // Assert
    expect(await screen.findByRole('button', { name: 'Keep both' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Єллоустоун', 'Чужі'])
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith(sameWorkGroup.key)
  })

  it('lists kept copies under Kept as they are, as kept, and puts a row with Keep again in their place once undone', async () => {
    // Arrange
    keptServer({ decision: 'Dismissed', decidedUtc: '2026-10-12T09:15:00Z', decidedByUserName: 'vasyl' })
    const { user } = renderPage('/library/duplicates?tab=Dismissed')
    const card = await screen.findByRole('article', { name: 'Єллоустоун' })
    const badges = within(card).getByRole('list', { name: 'About this group' })

    // Act
    await user.click(within(card).getByRole('button', { name: 'Undo' }))

    // Assert
    expect(badges).toHaveTextContent('Kept as they are')
    const again = await screen.findByRole('button', { name: 'Keep again' })
    expect(again).toHaveAccessibleDescription('“Єллоустоун (Season 1 · Episode 1)” is back among the duplicates.')
    expect(duplicatesApi.undo).toHaveBeenCalledExactlyOnceWith(sameWorkGroup.key)
    await vi.waitFor(() => expect(duplicatesApi.list).toHaveBeenCalledTimes(2))
    expect(rowsOnScreen()).toEqual(['“Єллоустоун (Season 1 · Episode 1)” is back among the duplicates.'])
  })

  it('keeps the copies again from that row, and their card comes back under Kept as they are', async () => {
    // Arrange
    keptServer({ decision: 'Dismissed', decidedUtc: '2026-10-12T09:15:00Z', decidedByUserName: 'vasyl' })
    const { user } = renderPage('/library/duplicates?tab=Dismissed')
    await user.click(await screen.findByRole('button', { name: 'Undo' }))

    // Act
    await user.click(await screen.findByRole('button', { name: 'Keep again' }))

    // Assert
    expect(await screen.findByRole('article', { name: 'Єллоустоун' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Єллоустоун'])
    expect(duplicatesApi.decide).toHaveBeenCalledExactlyOnceWith(sameWorkGroup.key, 'Dismissed')
  })

  // ── MAM-40: Remove this copy ──

  it('puts a short row naming the removed file in the place of a group of two, which leads to Removed copies', async () => {
    // Arrange
    removalServer(byteIdenticalGroupOf())
    const { user } = renderPage()
    await removeFirstCopy(user)

    // Act
    await user.click(await screen.findByRole('button', { name: 'Removed copies' }))

    // Assert
    expect(screen.getByRole('tab', { name: 'Removed copies' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('status', { name: 'Address' })).toHaveTextContent('/library/duplicates?tab=Removed')
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі (1986)' })).toBeInTheDocument()
  })

  it('ends the removal with the list asked for again, the group of two gone and its row in its place', async () => {
    // Arrange
    removalServer(byteIdenticalGroupOf())
    const { user } = renderPage()

    // Act
    await removeFirstCopy(user)

    // Assert
    const open = await screen.findByRole('button', { name: 'Removed copies' })
    expect(open).toHaveAccessibleDescription(removedRow)
    expect(open).toHaveFocus()
    expect(rowsOnScreen()).toEqual([removedRow, 'Чужі'])
    expect(duplicatesApi.list).toHaveBeenCalledTimes(2)
  })

  it('shows a bigger group again without the removed copy, beside the row that names it', async () => {
    // Arrange
    const three = threeCopies()
    // The two copies left are a group of their own, under another key.
    removalServer(three, byteIdenticalGroupOf({ key: '9f8e7d6c5b4a39281706f5e4d3c2b1a0', files: three.files.slice(1) }))
    const { user } = renderPage()

    // Act
    await removeFirstCopy(user)

    // Assert
    await screen.findByRole('button', { name: 'Removed copies' })
    expect(rowsOnScreen()).toEqual([removedRow, 'Чужі', 'Чужі'])
    expect(within(screen.getAllByRole('article')[0]).getByRole('button', { name: 'Keep both' })).toBeInTheDocument()
  })

  it('drops a removed copy\'s row once another tab opened, so All neither names it again nor takes the focus with it', async () => {
    // Arrange
    removalServer(byteIdenticalGroupOf())
    const { user } = renderPage()
    await removeFirstCopy(user)
    await user.click(await screen.findByRole('button', { name: 'Removed copies' }))
    await screen.findByRole('heading', { level: 2, name: 'Чужі (1986)' })

    // Act
    await user.click(screen.getByRole('tab', { name: 'All' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(rowsOnScreen()).toEqual(['Чужі'])
    expect(screen.queryByText(removedRow)).not.toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'All' })).toHaveFocus()
  })

  it('keeps a row apart for each removal of one file, when the file came back to its group in between', async () => {
    // Arrange
    // Put back meanwhile from another tab of the browser: the list shows the three copies again after the removal.
    removalServer(threeCopies(), threeCopies())
    const consoleError = vi.spyOn(console, 'error')
    const { user } = renderPage()
    await removeFirstCopy(user)
    await screen.findByRole('button', { name: 'Removed copies' })
    const card = screen.getAllByRole('article')[0]

    // Act
    await user.click(within(card).getAllByRole('button', { name: 'Remove this copy' })[0])
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove this copy' }))

    // Assert
    await vi.waitFor(() => expect(screen.getAllByRole('button', { name: 'Removed copies' })).toHaveLength(2))
    expect(rowsOnScreen()).toEqual([removedRow, removedRow, 'Чужі', 'Чужі'])
    const keyWarnings = consoleError.mock.calls.map((call) => call.map(String).join(' ')).filter((line) => line.includes('same key'))
    consoleError.mockRestore()
    expect(keyWarnings).toEqual([])
  })

  it.each([
    ['a sentence', removalOf({ reason: subtitleStayed }),
      `Removed “Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv”, but it needs your review under Removed copies: ${subtitleStayed}`],
    ['a review', removalOf({ state: 'NeedsReview' }),
      'Removed “Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv”, but it needs your review under Removed copies.'],
  ])('words the removed copy\'s row by the removal\'s answer when it carries %s, still leading to Removed copies', async (_, answered, row) => {
    // Arrange
    removalServer(byteIdenticalGroupOf())
    vi.mocked(copyRemovalsApi.remove).mockResolvedValue(answered)
    const { user } = renderPage()

    // Act
    await removeFirstCopy(user)

    // Assert
    const open = await screen.findByRole('button', { name: 'Removed copies' })
    expect(open).toHaveAccessibleDescription(row)
    expect(screen.queryByText(removedRow)).not.toBeInTheDocument()
  })

  it('shows a member the decision buttons disabled with the reason, and never asks the server to decide', async () => {
    // Arrange
    serverWith()
    const { user } = renderPage('/library/duplicates', member)
    const dismiss = await screen.findByRole('button', { name: 'Dismiss' })

    // Act
    await user.click(dismiss)
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    // Assert
    expect(dismiss).toBeDisabled()
    expect(dismiss.parentElement).toHaveAttribute('title', 'Only the owner confirms or dismisses a possible match.')
    expect(duplicatesApi.decide).not.toHaveBeenCalled()
    expect(duplicatesApi.undo).not.toHaveBeenCalled()
    expect(rowsOnScreen()).toEqual(['Єллоустоун', 'Чужі'])
  })
})
