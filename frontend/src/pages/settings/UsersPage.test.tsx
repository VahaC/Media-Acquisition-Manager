import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDateTime } from '@/lib/format'
import type { CurrentUser, UserList, UserSummary, UsersQuery } from '@/lib/types'
import { usersApi } from '@/lib/users-api'
import { UsersPage } from '@/pages/settings/UsersPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'

vi.mock('@/lib/users-api')

const vasyl: UserSummary = {
  id: owner.id,
  userName: 'vasyl',
  isOwner: true,
  isDisabled: false,
  createdUtc: '2026-09-01T08:00:00Z',
  lastSignInUtc: '2026-09-28T09:30:00Z',
}
const olena: UserSummary = {
  id: member.id,
  userName: 'olena',
  isOwner: false,
  isDisabled: false,
  createdUtc: '2026-09-10T08:00:00Z',
  lastSignInUtc: null,
}
const taras: UserSummary = {
  id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000003',
  userName: 'taras',
  isOwner: false,
  isDisabled: true,
  createdUtc: '2026-09-12T08:00:00Z',
  lastSignInUtc: '2026-09-20T18:45:00Z',
}

/** The page the server answers `query` with, out of `total` users. */
function pageOf(query: UsersQuery, items: UserSummary[], total = items.length): UserList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}

/** What the last request for the list asked for. */
const lastQuery = () => vi.mocked(usersApi.list).mock.lastCall?.[0]

/** The table row (or phone card) of the user named `name`. */
const rowOf = (name: string) => screen.getByText(name).closest('tr')!

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConfirmProvider>
          <UsersPage />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** Opens Reset password on the row of `name` and sets a new password there. */
async function resetPassword(user: ReturnType<typeof userEvent.setup>, name: string) {
  await screen.findByText(name)
  await user.click(within(rowOf(name)).getByRole('button', { name: 'Reset password' }))
  const dialog = await screen.findByRole('dialog', { name: `Reset the password of ${name}` })
  await user.type(within(dialog).getByLabelText('New password'), 'correct horse battery')
  await user.type(within(dialog).getByLabelText('Confirm password'), 'correct horse battery')
  await user.click(within(dialog).getByRole('button', { name: 'Reset password' }))
}

describe('UsersPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(usersApi.list).mockImplementation(async (query) => pageOf(query, [vasyl, olena, taras]))
  })

  it('lists every user with their badges, dates and "Never" before a first sign-in', async () => {
    // Arrange
    const format: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('olena')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 25 })
    expect(within(rowOf('vasyl')).getByText('Owner')).toBeInTheDocument()
    expect(within(rowOf('vasyl')).getByText(formatDateTime(vasyl.lastSignInUtc!, format))).toBeInTheDocument()
    expect(within(rowOf('olena')).getByText('Never')).toBeInTheDocument()
    expect(within(rowOf('olena')).getByText(formatDateTime(olena.createdUtc, format))).toBeInTheDocument()
    expect(within(rowOf('taras')).getByText('Disabled')).toBeInTheDocument()
    expect(within(rowOf('taras')).getByRole('button', { name: 'Enable' })).toBeInTheDocument()
    expect(screen.getByText('1–3 of 3')).toBeInTheDocument()
  })

  it('asks the server for the next page, and starts a new search at page 1 once typing pauses', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockImplementation(async (query) =>
      pageOf(query, query.search ? [olena] : query.page === 1 ? [vasyl, olena, taras] : [taras], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.type(screen.getByRole('searchbox', { name: 'Search users' }), ' ol ')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ search: 'ol', page: 1, pageSize: 25 }))
    expect(vi.mocked(usersApi.list).mock.calls.map(([query]) => query)).toEqual([
      { search: undefined, page: 1, pageSize: 25 },
      { search: undefined, page: 2, pageSize: 25 },
      { search: 'ol', page: 1, pageSize: 25 },
    ])
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
  })

  it('starts again at page 1 when a search is cleared, not at the page it left', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockImplementation(async (query) =>
      pageOf(query, query.search ? [olena] : query.page === 1 ? [vasyl, olena, taras] : [taras], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')
    const searchBox = screen.getByRole('searchbox', { name: 'Search users' })
    await user.type(searchBox, 'ol')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ search: 'ol', page: 1, pageSize: 25 }))

    // Act
    await user.clear(searchBox)

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 25 }))
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(screen.getByText('vasyl')).toBeInTheDocument()
  })

  it('remembers the page size on this device and starts again at page 1', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockImplementation(async (query) => pageOf(query, [vasyl, olena, taras], 120))
    const { user } = renderPage()
    await screen.findByText('1–25 of 120')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–50 of 120')

    // Act
    await user.selectOptions(screen.getByLabelText('Per page'), '50')

    // Assert
    expect(await screen.findByText('1–50 of 120')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:users:pageSize')).toBe('50')
  })

  it('opens with the page size this device chose before', async () => {
    // Arrange
    localStorage.setItem('mam:users:pageSize', '10')

    // Act
    renderPage()

    // Assert
    await screen.findByText('olena')
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 10 })
    expect(screen.getByLabelText('Per page')).toHaveValue('10')
  })

  it('names the page-size list by its visible "Per page" label, not by its value', async () => {
    // Arrange
    renderPage()

    // Act
    await screen.findByText('olena')

    // Assert
    const pageSize = screen.getByRole('combobox', { name: 'Per page' })
    expect(pageSize).toHaveValue('25')
    // A browser reads a label that wraps the select together with the select's own value, so its
    // name became "25"; jsdom leaves the value out, so what is pinned here is the label beside it.
    expect(pageSize.closest('label')).toBeNull()
  })

  it('says when no user matches the search, and Clear search shows everyone again', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockImplementation(async (query) => pageOf(query, query.search ? [] : [vasyl, olena]))
    const { user } = renderPage()
    await screen.findByText('olena')
    await user.type(screen.getByRole('searchbox', { name: 'Search users' }), 'zz')
    await screen.findByText('No users match “zz”.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear search' }))

    // Assert
    expect(await screen.findByText('olena')).toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search users' })).toHaveValue('')
    expect(screen.queryByText('No users match “zz”.')).not.toBeInTheDocument()
  })

  it('disables a user at once, without asking, and shows the refreshed row', async () => {
    // Arrange
    vi.mocked(usersApi.disable).mockImplementation(async () => {
      vi.mocked(usersApi.list).mockImplementation(async (query) =>
        pageOf(query, [vasyl, { ...olena, isDisabled: true }, taras]))
      return { ...olena, isDisabled: true }
    })
    const { user } = renderPage()
    await screen.findByText('olena')

    // Act
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Disable' }))

    // Assert
    expect(await within(rowOf('olena')).findByRole('button', { name: 'Enable' })).toBeInTheDocument()
    expect(usersApi.disable).toHaveBeenCalledWith(olena.id)
    expect(within(rowOf('olena')).getByText('Disabled')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('enables a disabled user at once', async () => {
    // Arrange
    vi.mocked(usersApi.enable).mockResolvedValue({ ...taras, isDisabled: false })
    const { user } = renderPage()
    await screen.findByText('taras')

    // Act
    await user.click(within(rowOf('taras')).getByRole('button', { name: 'Enable' }))

    // Assert
    expect(usersApi.enable).toHaveBeenCalledWith(taras.id)
    await vi.waitFor(() => expect(usersApi.list).toHaveBeenCalledTimes(2))
  })

  it('shows the API sentence above the list when an action is refused', async () => {
    // Arrange
    vi.mocked(usersApi.disable).mockRejectedValue(apiError(409, 'The installation owner can\'t be disabled.'))
    const { user } = renderPage()
    await screen.findByText('olena')

    // Act
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Disable' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The installation owner can\'t be disabled.')
    expect(screen.getByText('olena')).toBeInTheDocument()
  })

  it('takes a refused action\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(usersApi.disable).mockRejectedValue(apiError(409, 'The installation owner can\'t be disabled.'))
    const { user } = renderPage()
    await screen.findByText('olena')
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Disable' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText('olena')).toBeInTheDocument()
  })

  it('does not delete a user when the confirmation is cancelled', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('olena')
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Delete' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete olena?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(usersApi.remove).not.toHaveBeenCalled()
  })

  it('deletes a user only once the confirmation is accepted', async () => {
    // Arrange
    vi.mocked(usersApi.remove).mockImplementation(async () => {
      vi.mocked(usersApi.list).mockImplementation(async (query) => pageOf(query, [vasyl, taras]))
    })
    const { user } = renderPage()
    await screen.findByText('olena')
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Delete' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete olena?' })
    const calledBeforeConfirming = vi.mocked(usersApi.remove).mock.calls.length

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Delete' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByText('olena')).not.toBeInTheDocument())
    expect(calledBeforeConfirming).toBe(0)
    expect(usersApi.remove).toHaveBeenCalledExactlyOnceWith(olena.id)
    expect(confirmation).toHaveTextContent('olena can no longer sign in and is signed out everywhere.')
  })

  it('steps back a page after deleting the only user of the last page', async () => {
    // Arrange
    const ivan: UserSummary = { ...olena, id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000004', userName: 'ivan' }
    const firstPage = Array.from({ length: 25 }, (_, index): UserSummary =>
      ({ ...olena, id: `0b6f2c1e-4a1d-4f7a-9c1e-1000000000${String(index).padStart(2, '0')}`, userName: `user${index}` }))
    let lastPage = [ivan]
    vi.mocked(usersApi.list).mockImplementation(async (query) =>
      pageOf(query, query.page === 1 ? firstPage : lastPage, firstPage.length + lastPage.length))
    vi.mocked(usersApi.remove).mockImplementation(async () => {
      lastPage = []
    })
    const { user } = renderPage()
    await screen.findByText('1–25 of 26')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('ivan')
    await user.click(within(rowOf('ivan')).getByRole('button', { name: 'Delete' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Delete ivan?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Delete' }))

    // Assert
    expect(await screen.findByText('1–25 of 25')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 25 })
    expect(usersApi.remove).toHaveBeenCalledExactlyOnceWith(ivan.id)
    expect(screen.getByText('user0')).toBeInTheDocument()
  })

  it('keeps Disable and Delete of the owner disabled, and says why', async () => {
    // Arrange
    renderPage()

    // Act
    await screen.findByText('vasyl')

    // Assert
    const disable = within(rowOf('vasyl')).getByRole('button', { name: 'Disable' })
    const remove = within(rowOf('vasyl')).getByRole('button', { name: 'Delete' })
    expect(disable).toBeDisabled()
    expect(disable.parentElement).toHaveAttribute('title', 'The installation owner can\'t be disabled.')
    expect(remove).toBeDisabled()
    expect(remove.parentElement).toHaveAttribute('title', 'The installation owner can\'t be deleted.')
    expect(within(rowOf('vasyl')).getByRole('button', { name: 'Reset password' })).toBeEnabled()
    expect(within(rowOf('olena')).getByRole('button', { name: 'Delete' })).toBeEnabled()
  })

  it('keeps the rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText('olena')
    vi.mocked(usersApi.list).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last loaded users.')
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(screen.getByText('olena')).toBeInTheDocument()
  })

  it('asks again from Retry on the refresh banner, and drops the banner once the list is back', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByText('olena')
    vi.mocked(usersApi.list).mockRejectedValueOnce(networkError())
    await client.refetchQueries()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByText('olena')).toBeInTheDocument()
    expect(usersApi.list).toHaveBeenCalledTimes(3)
  })

  it('says the users could not be loaded, with the API sentence and Try again, in place of the list', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockRejectedValueOnce(networkError())

    // Act
    const { user } = renderPage()
    const state = await screen.findByRole('alert')

    // Assert
    expect(state).toHaveTextContent('Could not load the users.')
    expect(state).toHaveTextContent('The server could not be reached.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('olena')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the API sentence in place of the list, with no Try again, when a member opens it', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderPage(member)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Only the installation owner can do this.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add user' })).not.toBeInTheDocument()
  })

  it('says a page that failed to load could not be loaded, rather than showing the last page under a banner', async () => {
    // Arrange
    vi.mocked(usersApi.list).mockImplementation(async (query) => {
      if (query.page === 2) throw networkError()
      return pageOf(query, [vasyl, olena], 30)
    })
    const { user } = renderPage()
    await screen.findByText('olena')

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))

    // Assert
    const state = await screen.findByRole('alert')
    expect(state).toHaveTextContent('Could not load the users.')
    expect(screen.getAllByRole('alert')).toHaveLength(1)
    expect(screen.queryByText('olena')).not.toBeInTheDocument()
  })

  it('adds a user through the Add user dialog and lists them once it closes', async () => {
    // Arrange
    const ivan: UserSummary = { ...olena, id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000004', userName: 'ivan' }
    vi.mocked(usersApi.create).mockImplementation(async () => {
      vi.mocked(usersApi.list).mockImplementation(async (query) => pageOf(query, [ivan, vasyl, olena, taras]))
      return ivan
    })
    const { user } = renderPage()
    await screen.findByText('olena')
    await user.click(screen.getByRole('button', { name: 'Add user' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add user' })
    await user.type(within(dialog).getByLabelText('User name'), 'ivan')
    await user.type(within(dialog).getByLabelText('Password'), 'correct horse battery')
    await user.type(within(dialog).getByLabelText('Confirm password'), 'correct horse battery')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add user' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(usersApi.create).toHaveBeenCalledWith({ userName: 'ivan', password: 'correct horse battery' })
    expect(screen.getByText('ivan')).toBeInTheDocument()
  })

  it('opens Reset password for the user of that row', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('olena')

    // Act
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Reset password' }))

    // Assert
    expect(await screen.findByRole('dialog', { name: 'Reset the password of olena' })).toBeInTheDocument()
  })

  it('says above the list whose password was reset once the dialog closes', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockResolvedValue()
    const { user } = renderPage()

    // Act
    await resetPassword(user, 'olena')

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('status')).toHaveTextContent('The password of olena was reset.')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(usersApi.resetPassword).toHaveBeenCalledWith(olena.id, 'correct horse battery')
  })

  it('takes the reset\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockResolvedValue()
    const { user } = renderPage()
    await resetPassword(user, 'olena')
    const banner = await screen.findByRole('status')

    // Act
    await user.click(within(banner).getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByText('olena')).toBeInTheDocument()
  })

  it('takes the reset\'s banner away with the next action, which reports only its own outcome', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockResolvedValue()
    vi.mocked(usersApi.disable).mockRejectedValue(apiError(409, 'The installation owner can\'t be disabled.'))
    const { user } = renderPage()
    await resetPassword(user, 'olena')
    await screen.findByRole('status')

    // Act
    await user.click(within(rowOf('olena')).getByRole('button', { name: 'Disable' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The installation owner can\'t be disabled.')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('keeps a refused reset in its dialog and says nothing above the list', async () => {
    // Arrange
    vi.mocked(usersApi.resetPassword).mockRejectedValue(apiError(404))
    const { user } = renderPage()

    // Act
    await resetPassword(user, 'olena')

    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Reset the password of olena' })
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not reset the password.')
    expect(screen.getAllByRole('alert')).toHaveLength(1)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
