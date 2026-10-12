import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { copyRemovalsApi } from '@/lib/copy-removals-api'
import { duplicatesApi } from '@/lib/duplicates-api'
import { qk } from '@/lib/queries'
import type { CurrentUser, LibraryDuplicateGroup } from '@/lib/types'
import { DuplicatesPage } from '@/pages/DuplicatesPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { removalsPage } from '@/test/copy-removals'
import { byteIdenticalGroupOf, duplicatesPage, possibleGroupOf, sameFileGroup, sameWorkGroup } from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')
vi.mock('@/lib/copy-removals-api')

// MAM-21 — the Duplicates page around its cards: the tabs of the classes with Dismissed last, the tab in
// the address, the query each tab sends the server, its paging, and the states of its list. MAM-40 — the
// tabs in plain words, Dismissed as Kept as they are, and the owner's Removed copies last.

/** The address the page is at, so a test can read what the tabs put there. */
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

const address = () => screen.getByRole('status', { name: 'Address' }).textContent

/** What the last request for the groups asked for. */
const lastQuery = () => vi.mocked(duplicatesApi.list).mock.lastCall?.[0]

/** The headings of the group cards on screen, in order. */
const cardTitles = () =>
  screen.queryAllByRole('article').map((card) => within(card).getByRole('heading', { level: 2 }).textContent)

const everyClass: LibraryDuplicateGroup[] = [sameFileGroup, byteIdenticalGroupOf(), sameWorkGroup, possibleGroupOf()]

const lead = 'The files in your library that are one file under two names, exact copies, different files of one film or episode, or maybe the same film, each group with the evidence behind it. Removing a copy moves it into the quarantine folder of its media folder, where you can restore it or delete it for good under Removed copies; keeping copies as they are, or confirming a possible match, removes nothing.'

/** The tabs every signed-in user has, in their order. */
const memberTabs = ['All', 'One file, two names', 'Exact copies', 'Same film, different files', 'Maybe the same film', 'Kept as they are']

describe('DuplicatesPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, everyClass))
  })

  it('shows a member its title, what removing does, and the six tabs on All, asking for every group but the dismissed ones', async () => {
    // Arrange
    const signedIn = member

    // Act
    renderPage('/library/duplicates', signedIn)

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'Duplicates' })).toBeInTheDocument()
    expect(screen.getByText(lead)).toBeInTheDocument()
    const tabs = within(screen.getByRole('tablist', { name: 'Kinds of duplicate' })).getAllByRole('tab')
    expect(tabs.map((tab) => tab.textContent)).toEqual(memberTabs)
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByRole('heading', { level: 2, name: 'Єллоустоун' })).toBeInTheDocument()
    expect(cardTitles()).toEqual(['Чужі', 'Чужі', 'Єллоустоун', 'Чужі'])
    expect(screen.getByRole('tabpanel', { name: 'All' })).toBeInTheDocument()
    expect(lastQuery()).toEqual({ page: 1, pageSize: 25 })
    expect(screen.getByText('1–4 of 4')).toBeInTheDocument()
  })

  it('shows the owner Removed copies last, after the six tabs', () => {
    // Arrange
    const signedIn = owner

    // Act
    renderPage('/library/duplicates', signedIn)

    // Assert
    const tabs = within(screen.getByRole('tablist', { name: 'Kinds of duplicate' })).getAllByRole('tab')
    expect(tabs.map((tab) => tab.textContent)).toEqual([...memberTabs, 'Removed copies'])
  })

  it('reads a member\'s ?tab=Removed as All, asking for the groups and never for the removals', async () => {
    // Arrange
    const path = '/library/duplicates?tab=Removed'

    // Act
    renderPage(path, member)

    // Assert
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('tab', { name: 'Removed copies' })).not.toBeInTheDocument()
    await vi.waitFor(() => expect(lastQuery()).toEqual({ page: 1, pageSize: 25 }))
    expect(copyRemovalsApi.list).not.toHaveBeenCalled()
  })

  it('asks for the removals alone on the owner\'s Removed copies, never for the groups', async () => {
    // Arrange
    vi.mocked(copyRemovalsApi.list).mockImplementation(async (query) => removalsPage(query, []))

    // Act
    renderPage('/library/duplicates?tab=Removed')

    // Assert
    expect(screen.getByRole('tab', { name: 'Removed copies' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tabpanel', { name: 'Removed copies' })).toBeInTheDocument()
    await vi.waitFor(() => expect(copyRemovalsApi.list).toHaveBeenCalledExactlyOnceWith({ page: 1, pageSize: 25 }))
    expect(duplicatesApi.list).not.toHaveBeenCalled()
  })

  it('puts the tab picked in the address, and leaves All out of it', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–4 of 4')

    // Act
    await user.click(screen.getByRole('tab', { name: 'One file, two names' }))
    const sameFile = address()
    await user.click(screen.getByRole('tab', { name: 'All' }))

    // Assert
    expect(sameFile).toBe('/library/duplicates?tab=SameFile')
    expect(address()).toBe('/library/duplicates')
  })

  it.each([
    ['One file, two names', { class: 'SameFile' }],
    ['Exact copies', { class: 'ByteIdentical' }],
    ['Same film, different files', { class: 'SameWork' }],
    ['Maybe the same film', { class: 'Possible' }],
    ['Kept as they are', { decision: 'Dismissed' }],
  ])('asks the server for the %s tab\'s groups alone', async (name, filter) => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–4 of 4')

    // Act
    await user.click(screen.getByRole('tab', { name }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ ...filter, page: 1, pageSize: 25 }))
    expect(screen.getByRole('tabpanel', { name })).toBeInTheDocument()
  })

  it('opens on the tab its address names', async () => {
    // Arrange
    const path = '/library/duplicates?tab=Dismissed'

    // Act
    renderPage(path)

    // Assert
    expect(screen.getByRole('tab', { name: 'Kept as they are' })).toHaveAttribute('aria-selected', 'true')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ decision: 'Dismissed', page: 1, pageSize: 25 }))
    expect(vi.mocked(duplicatesApi.list)).toHaveBeenCalledOnce()
  })

  it('reads a tab its address doesn\'t know as All', async () => {
    // Arrange
    const path = '/library/duplicates?tab=Hardlinks'

    // Act
    renderPage(path)

    // Assert
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ page: 1, pageSize: 25 }))
  })

  it('pages through the server\'s groups and starts another tab at page 1', async () => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) =>
      duplicatesPage(query, query.page === 1 ? [sameFileGroup] : [possibleGroupOf()], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.click(screen.getByRole('tab', { name: 'Maybe the same film' }))

    // Assert
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(vi.mocked(duplicatesApi.list).mock.calls.map(([query]) => query)).toEqual([
      { page: 1, pageSize: 25 },
      { page: 2, pageSize: 25 },
      { class: 'Possible', page: 1, pageSize: 25 },
    ])
  })

  it('asks for the page size picked, from page 1, and keeps it for this device', async () => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, [sameFileGroup], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '10')

    // Assert
    expect(await screen.findByText('1–10 of 30')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ page: 1, pageSize: 10 })
    expect(localStorage.getItem('mam:duplicates:pageSize')).toBe('10')
  })

  it('moves to the last page there is when fewer groups are left than the page on screen needs', async () => {
    // Arrange
    let total = 30
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, query.page * 25 <= total + 24 ? [sameFileGroup] : [], total))
    const { client, user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    total = 20
    await client.invalidateQueries({ queryKey: qk.library.duplicatesAll })

    // Assert
    expect(await screen.findByText('1–20 of 20')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ page: 1, pageSize: 25 })
  })

  it('says Loading… rather than show the last tab\'s groups while another tab loads', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–4 of 4')
    vi.mocked(duplicatesApi.list).mockReturnValue(new Promise(() => {}))

    // Act
    await user.click(screen.getByRole('tab', { name: 'Kept as they are' }))

    // Assert
    expect(await screen.findByText('Loading…')).toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it('keeps the page on screen while the next page of its tab loads', async () => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, [sameFileGroup], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    vi.mocked(duplicatesApi.list).mockReturnValue(new Promise(() => {}))

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))

    // Assert
    expect(lastQuery()).toEqual({ page: 2, pageSize: 25 })
    expect(cardTitles()).toEqual(['Чужі'])
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })

  it('says Loading… while the first page is on its way', () => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockReturnValue(new Promise(() => {}))

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it.each([
    ['All', 'No duplicates found. The app finds them in what the syncs, the folder scan and the inspection of your files have read.'],
    ['SameFile', 'No file has two names the app found.'],
    ['ByteIdentical', 'No two files have the same bytes, as far as the app has hashed them.'],
    ['SameWork', 'No movie or episode has two copies.'],
    ['Possible', 'No possible match is left to check.'],
    ['Dismissed', 'Nothing is kept as it is: no group\'s copies were kept and no possible match was dismissed.'],
  ])('says the %s tab has no group', async (tab, sentence) => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockImplementation(async (query) => duplicatesPage(query, []))

    // Act
    renderPage(tab === 'All' ? '/library/duplicates' : `/library/duplicates?tab=${tab}`)

    // Assert
    expect(await screen.findByText(sentence)).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('keeps the same cards on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText('1–4 of 4')
    vi.mocked(duplicatesApi.list).mockRejectedValue(apiError(500, 'The database is unavailable.'))

    // Act
    await client.refetchQueries({ queryKey: qk.library.duplicatesAll })

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refresh failed, showing the last loaded groups. The database is unavailable.')
    expect(cardTitles()).toEqual(['Чужі', 'Чужі', 'Єллоустоун', 'Чужі'])
    expect(within(screen.getByRole('alert')).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('shows a page the server refuses as its sentence, with no Try again: asking again gets the same answer', async () => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockRejectedValue(apiError(400, 'Page size must be between 1 and 100.'))

    // Act
    renderPage()

    // Assert
    const failure = await screen.findByRole('alert')
    expect(failure).toHaveTextContent('Could not load the duplicates.')
    expect(failure).toHaveTextContent('Page size must be between 1 and 100.')
    expect(within(failure).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each([
    ['the server failed', apiError(500, 'The database is unavailable.'), 'The database is unavailable.'],
    ['no answer came', networkError(), 'Could not load the duplicates.'],
  ])('shows a first load that failed because %s with its sentence and Try again, which asks again and shows the groups', async (_, error, sentence) => {
    // Arrange
    vi.mocked(duplicatesApi.list).mockRejectedValueOnce(error)
    const { user } = renderPage()
    const failure = await screen.findByRole('alert')
    const shown = failure.textContent

    // Act
    await user.click(within(failure).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(shown).toContain('Could not load the duplicates.')
    expect(shown).toContain(sentence)
    expect(await screen.findByText('1–4 of 4')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(duplicatesApi.list).toHaveBeenCalledTimes(2)
  })
})
