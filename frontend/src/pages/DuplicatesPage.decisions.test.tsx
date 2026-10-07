import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { duplicatesApi } from '@/lib/duplicates-api'
import type { CurrentUser, DuplicateDecision } from '@/lib/types'
import { DuplicatesPage } from '@/pages/DuplicatesPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { duplicatesPage, possibleGroupOf, sameWorkGroup } from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')

// MAM-21 — the owner's decisions as the page shows them: a dismissed match leaves the list for the
// Dismissed tab, and a short row with Undo takes its card's place, as "Not interested" leaves a
// recommendation; an undone dismissal leaves Dismissed the same way, with Dismiss again; a confirmed
// match stays, shown as confirmed; a member reads the decisions and can't make one.

function renderPage(path = '/library/duplicates', signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/library/duplicates" element={<DuplicatesPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const key = possibleGroupOf().key

const dismissedRow = '“Чужі (1986)” is dismissed: it is listed under Dismissed now.'

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
    await user.click(screen.getByRole('tab', { name: 'Same work' }))

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
