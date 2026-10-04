import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { integrationsApi } from '@/lib/integrations-api'
import { qk } from '@/lib/queries'
import type { ConnectionTestResult, CurrentUser, IntegrationList, IntegrationSummary, IntegrationsQuery } from '@/lib/types'
import { IntegrationsPage } from '@/pages/settings/IntegrationsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { destinationsPage, movies, sampleCategories } from '@/test/destinations'
import { adapterTypes, homeJellyfin, localQbittorrent, retiredClient, toloka } from '@/test/integrations'
import { aliens, locations, sourceOf, sourcesOf, worksPage } from '@/test/library'

vi.mock('@/lib/integrations-api')

/** The page the server answers `query` with, out of `total` integrations. */
function pageOf(query: IntegrationsQuery, items: IntegrationSummary[], total = items.length): IntegrationList {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize }
}

/** What the last request for the list asked for. */
const lastQuery = () => vi.mocked(integrationsApi.list).mock.lastCall?.[0]

/** The table row (or phone card) of the integration named `name`. */
const rowOf = (name: string) => screen.getByText(name).closest('tr')!

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConfirmProvider>
          <IntegrationsPage />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

describe('IntegrationsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      pageOf(query, [homeJellyfin, localQbittorrent, toloka]))
    vi.mocked(integrationsApi.adapterTypes).mockResolvedValue(adapterTypes)
  })

  it('lists every integration with its service, kind, address and a Disabled badge', async () => {
    // Arrange
    const query = { search: undefined, kind: undefined, page: 1, pageSize: 25 }

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Toloka')).toBeInTheDocument()
    expect(lastQuery()).toEqual(query)
    const jellyfin = within(rowOf('Home Jellyfin'))
    expect(jellyfin.getByText('Jellyfin')).toBeInTheDocument()
    expect(jellyfin.getByText('Media library')).toBeInTheDocument()
    expect(jellyfin.getByText('http://127.0.0.1:9')).toBeInTheDocument()
    expect(jellyfin.queryByText('Disabled')).not.toBeInTheDocument()
    expect(within(rowOf('Local qBittorrent')).getByText('Download client')).toBeInTheDocument()
    expect(within(rowOf('Toloka')).getByText('Release source')).toBeInTheDocument()
    expect(within(rowOf('Toloka')).getByText('http://127.0.0.1:9/1/api')).toBeInTheDocument()
    expect(within(rowOf('Toloka')).getByText('Disabled')).toBeInTheDocument()
    expect(rowOf('Toloka')).toHaveAttribute('data-disabled')
    expect(screen.getByText('1–3 of 3')).toBeInTheDocument()
  })

  it('asks the server for the search once typing pauses and starts again at page 1', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      pageOf(query, query.search ? [localQbittorrent] : query.page === 1 ? [homeJellyfin, toloka] : [localQbittorrent], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.type(screen.getByRole('searchbox', { name: 'Search integrations' }), ' qbit ')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ search: 'qbit', kind: undefined, page: 1, pageSize: 25 }))
    expect(vi.mocked(integrationsApi.list).mock.calls.map(([query]) => query)).toEqual([
      { search: undefined, kind: undefined, page: 1, pageSize: 25 },
      { search: undefined, kind: undefined, page: 2, pageSize: 25 },
      { search: 'qbit', kind: undefined, page: 1, pageSize: 25 },
    ])
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
  })

  it('filters by kind on the server and starts again at page 1', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      query.kind === 'DownloadClient'
        ? pageOf(query, [localQbittorrent])
        : pageOf(query, query.page === 1 ? [homeJellyfin, toloka] : [localQbittorrent], 30))
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Download clients')

    // Assert
    expect(await screen.findByText('1–1 of 1')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, kind: 'DownloadClient', page: 1, pageSize: 25 })
    expect(screen.getByText('Local qBittorrent')).toBeInTheDocument()
    expect(screen.queryByText('Home Jellyfin')).not.toBeInTheDocument()
    expect([...screen.getByRole('combobox', { name: 'Kind' }).querySelectorAll('option')].map((option) => option.textContent))
      .toEqual(['All kinds', 'Media libraries', 'Release sources', 'Download clients'])
  })

  it('remembers the page size on this device', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) => pageOf(query, [homeJellyfin, toloka], 120))
    const { user } = renderPage()
    await screen.findByText('1–25 of 120')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–50 of 120')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('1–50 of 120')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, kind: undefined, page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:integrations:pageSize')).toBe('50')
    // Its own key: the Users page keeps its size.
    expect(localStorage.getItem('mam:users:pageSize')).toBeNull()
  })

  it('opens with the page size this device chose before', async () => {
    // Arrange
    localStorage.setItem('mam:integrations:pageSize', '10')

    // Act
    renderPage()

    // Assert
    await screen.findByText('Toloka')
    expect(lastQuery()).toEqual({ search: undefined, kind: undefined, page: 1, pageSize: 10 })
    expect(screen.getByRole('combobox', { name: 'Per page' })).toHaveValue('10')
  })

  it('says when nothing matches, and Clear filters shows everything again', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      pageOf(query, query.search || query.kind ? [] : [homeJellyfin, toloka]))
    const { user } = renderPage()
    await screen.findByText('Toloka')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'MediaLibrary')
    await user.type(screen.getByRole('searchbox', { name: 'Search integrations' }), 'zz')
    await screen.findByText('No integrations match these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByText('Toloka')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, kind: undefined, page: 1, pageSize: 25 })
    expect(screen.getByRole('searchbox', { name: 'Search integrations' })).toHaveValue('')
    expect(screen.getByRole('combobox', { name: 'Kind' })).toHaveValue('')
    expect(screen.queryByText('No integrations match these filters.')).not.toBeInTheDocument()
  })

  it('names Add integration in the empty state', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) => pageOf(query, []))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(/^No integrations yet\./)).toHaveTextContent('Click Add integration to connect')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument()
  })

  it('tests an integration from its row and shows the answer above the list', async () => {
    // Arrange
    let answer: (result: ConnectionTestResult) => void = () => {}
    vi.mocked(integrationsApi.test).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { user } = renderPage()
    await screen.findByText('Home Jellyfin')
    await user.click(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Test' }))
    const disabledWhileTesting = (within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Testing…' }) as HTMLButtonElement).disabled

    // Act
    await act(async () => answer({ status: 'Succeeded', message: 'Connected to Jellyfin 12.1.0 (VahaC media).' }))

    // Assert
    const banner = await screen.findByRole('status')
    expect(banner).toHaveTextContent('Home Jellyfin: Connected to Jellyfin 12.1.0 (VahaC media).')
    expect(banner).toHaveAttribute('data-tone', 'ok')
    expect(disabledWhileTesting).toBe(true)
    expect(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Test' })).toBeEnabled()
    expect(integrationsApi.test).toHaveBeenCalledExactlyOnceWith(homeJellyfin.id)
    // A test changes nothing, so the list isn't asked for again.
    expect(integrationsApi.list).toHaveBeenCalledOnce()
  })

  it('shows a failed test as the service\'s sentence in an error banner', async () => {
    // Arrange
    vi.mocked(integrationsApi.test).mockResolvedValue({
      status: 'AuthenticationFailed',
      message: 'qBittorrent refused the user name or password.',
    })
    const { user } = renderPage()
    await screen.findByText('Local qBittorrent')

    // Act
    await user.click(within(rowOf('Local qBittorrent')).getByRole('button', { name: 'Test' }))

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Local qBittorrent: qBittorrent refused the user name or password.')
    expect(banner).not.toHaveAttribute('data-tone')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it.each([
    ['a test that connected', 'Succeeded', 'status'],
    ['a test that did not', 'Refused', 'alert'],
  ] as const)('takes the answer of %s away on Dismiss', async (_, status, role) => {
    // Arrange
    vi.mocked(integrationsApi.test).mockResolvedValue({ status, message: 'The service answered.' })
    const { user } = renderPage()
    await screen.findByText('Home Jellyfin')
    await user.click(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Test' }))
    await screen.findByRole(role)

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole(role)).not.toBeInTheDocument()
    expect(screen.queryByText('Home Jellyfin: The service answered.')).not.toBeInTheDocument()
  })

  it('takes a refused action\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(integrationsApi.test).mockRejectedValue(apiError(404))
    const { user } = renderPage()
    await screen.findByText('Home Jellyfin')
    await user.click(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Test' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText('Home Jellyfin')).toBeInTheDocument()
  })

  it('shows the API sentence above the list when a test is refused, and the last action wins', async () => {
    // Arrange
    vi.mocked(integrationsApi.test)
      .mockResolvedValueOnce({ status: 'Succeeded', message: 'Connected to qBittorrent v5.2.3.' })
      .mockRejectedValueOnce(apiError(409, 'The stored credentials of this integration can\'t be read. Enter them again.'))
    const { user } = renderPage()
    await screen.findByText('Local qBittorrent')
    await user.click(within(rowOf('Local qBittorrent')).getByRole('button', { name: 'Test' }))
    await screen.findByRole('status')

    // Act
    await user.click(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Test' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The stored credentials of this integration can\'t be read. Enter them again.')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByText('Home Jellyfin')).toBeInTheDocument()
  })

  it('removes an integration only once the confirmation is accepted', async () => {
    // Arrange
    vi.mocked(integrationsApi.remove).mockImplementation(async () => {
      vi.mocked(integrationsApi.list).mockImplementation(async (query) => pageOf(query, [homeJellyfin, localQbittorrent]))
    })
    const { user } = renderPage()
    await screen.findByText('Toloka')
    await user.click(within(rowOf('Toloka')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Toloka?' })
    const calledBeforeConfirming = vi.mocked(integrationsApi.remove).mock.calls.length

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByText('Toloka')).not.toBeInTheDocument())
    expect(calledBeforeConfirming).toBe(0)
    expect(integrationsApi.remove).toHaveBeenCalledExactlyOnceWith(toloka.id)
    expect(confirmation).toHaveTextContent('Nothing changes in the service itself.')
  })

  it('does not remove an integration when the confirmation is cancelled', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Toloka')
    await user.click(within(rowOf('Toloka')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Toloka?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(integrationsApi.remove).not.toHaveBeenCalled()
    expect(screen.getByText('Toloka')).toBeInTheDocument()
  })

  it('warns before removing a media library that the library it imported goes with it', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Home Jellyfin')

    // Act
    await user.click(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Remove' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Home Jellyfin?' })
    expect(confirmation).toHaveTextContent(
      'The app forgets Home Jellyfin, its stored credentials and its path mappings, and removes the library it imported: its movies, series, versions and files leave the Library page for everyone. Your files and Jellyfin itself are not touched. This can\'t be undone.')
    expect(integrationsApi.remove).not.toHaveBeenCalled()
  })

  it('warns before removing a download client that the destinations using it go with it, the default too', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Local qBittorrent')

    // Act
    await user.click(within(rowOf('Local qBittorrent')).getByRole('button', { name: 'Remove' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Local qBittorrent?' })
    expect(confirmation).toHaveTextContent(
      'The app forgets Local qBittorrent, its stored credentials and its path mappings, and removes every destination that uses it, including the default destination if it is one of them. Their categories stay in qBittorrent, and qBittorrent itself is not touched. This can\'t be undone.')
    expect(confirmation).not.toHaveTextContent('Library page')
    expect(integrationsApi.remove).not.toHaveBeenCalled()
  })

  it('says nothing about a library or destinations when removing a release source', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Toloka')

    // Act
    await user.click(within(rowOf('Toloka')).getByRole('button', { name: 'Remove' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Toloka?' })
    expect(confirmation).toHaveTextContent(
      'The app forgets Toloka, its stored credentials and its path mappings. Nothing changes in the service itself.')
    expect(confirmation).not.toHaveTextContent('Library page')
    expect(confirmation).not.toHaveTextContent('destination')
  })

  it('drops the cached library once an integration is removed, so the Library page loads afresh', async () => {
    // Arrange — the Library was opened earlier in this visit, and its answers are still cached.
    vi.mocked(integrationsApi.remove).mockResolvedValue()
    const { client, user } = renderPage()
    const worksQuery = { search: undefined, locationId: undefined, identifiers: undefined, page: 1, pageSize: 25 }
    client.setQueryData(qk.library.works(worksQuery), worksPage(worksQuery, [aliens]))
    client.setQueryData(qk.library.locations, locations)
    client.setQueryData(qk.library.sources, sourcesOf(sourceOf({})))
    const cachedBefore = client.getQueryCache().findAll({ queryKey: qk.library.all }).length
    await screen.findByText('Home Jellyfin')
    await user.click(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Home Jellyfin?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    await vi.waitFor(() => expect(client.getQueryCache().findAll({ queryKey: qk.library.all })).toEqual([]))
    expect(cachedBefore).toBe(3)
    expect(integrationsApi.remove).toHaveBeenCalledExactlyOnceWith(homeJellyfin.id)
  })

  it('marks the cached destinations and categories stale once an integration is removed', async () => {
    // Arrange — the Destinations page was opened earlier in this visit, and its answers are still cached.
    vi.mocked(integrationsApi.remove).mockResolvedValue()
    const { client, user } = renderPage()
    const destinationsQuery = { search: undefined, page: 1, pageSize: 25 }
    client.setQueryData(qk.settings.destinations.list(destinationsQuery), destinationsPage(destinationsQuery, [movies]))
    client.setQueryData(qk.settings.destinations.clientDestinations(localQbittorrent.id), sampleCategories)
    await screen.findByText('Local qBittorrent')
    await user.click(within(rowOf('Local qBittorrent')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Local qBittorrent?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    await vi.waitFor(() =>
      expect(client.getQueryState(qk.settings.destinations.list(destinationsQuery))?.isInvalidated).toBe(true))
    expect(client.getQueryState(qk.settings.destinations.clientDestinations(localQbittorrent.id))?.isInvalidated).toBe(true)
    expect(integrationsApi.remove).toHaveBeenCalledExactlyOnceWith(localQbittorrent.id)
  })

  it('steps back a page after removing the only integration of the last page', async () => {
    // Arrange
    const firstPage = Array.from({ length: 25 }, (_, index): IntegrationSummary =>
      ({ ...homeJellyfin, id: `5d1c7a90-0000-4000-8000-1000000000${String(index).padStart(2, '0')}`, name: `Library ${index}` }))
    let lastPage = [toloka]
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      pageOf(query, query.page === 1 ? firstPage : lastPage, firstPage.length + lastPage.length))
    vi.mocked(integrationsApi.remove).mockImplementation(async () => {
      lastPage = []
    })
    const { user } = renderPage()
    await screen.findByText('1–25 of 26')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('Toloka')
    await user.click(within(rowOf('Toloka')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Toloka?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    expect(await screen.findByText('1–25 of 25')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, kind: undefined, page: 1, pageSize: 25 })
    expect(screen.getByText('Library 0')).toBeInTheDocument()
  })

  it('keeps Test and Edit of a type this version can\'t connect to disabled, and says why', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) => pageOf(query, [homeJellyfin, retiredClient]))
    const reason = 'This version of the app can\'t connect to “transmission”. You can only remove this integration.'

    // Act
    renderPage()
    await screen.findByText('Old Transmission')

    // Assert
    const row = within(rowOf('Old Transmission'))
    const test = row.getByRole('button', { name: 'Test' })
    const edit = row.getByRole('button', { name: 'Edit' })
    expect(test).toBeDisabled()
    expect(test.parentElement).toHaveAttribute('title', reason)
    expect(edit).toBeDisabled()
    expect(edit.parentElement).toHaveAttribute('title', reason)
    expect(row.getByRole('button', { name: 'Remove' })).toBeEnabled()
    // The type id stands in for the name this build no longer has.
    expect(row.getByText('transmission')).toBeInTheDocument()
    expect(within(rowOf('Home Jellyfin')).getByRole('button', { name: 'Edit' })).toBeEnabled()
  })

  it('opens Add integration from the header', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Toloka')

    // Act
    await user.click(screen.getByRole('button', { name: 'Add integration' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Add integration' })
    expect(await within(dialog).findByRole('option', { name: 'qBittorrent' })).toBeInTheDocument()
  })

  it('opens Edit for the integration of that row', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Local qBittorrent')

    // Act
    await user.click(within(rowOf('Local qBittorrent')).getByRole('button', { name: 'Edit' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Edit Local qBittorrent' })
    expect(within(dialog).getByLabelText('Name')).toHaveValue('Local qBittorrent')
    expect(within(dialog).getByLabelText('Address')).toHaveValue('http://127.0.0.1:9/qbt')
  })

  it('keeps the rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText('Toloka')
    vi.mocked(integrationsApi.list).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last loaded integrations.')
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(screen.getByText('Toloka')).toBeInTheDocument()
  })

  it('asks again from Retry on the refresh banner, and drops the banner once the list is back', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByText('Toloka')
    vi.mocked(integrationsApi.list).mockRejectedValueOnce(networkError())
    await client.refetchQueries({ queryKey: qk.settings.integrations.lists })
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByText('Toloka')).toBeInTheDocument()
  })

  it('says the integrations could not be loaded, with Try again, and loads them from there', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockRejectedValueOnce(apiError(500, 'Database is locked.'))
    const { user } = renderPage()
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the integrations.')
    expect(state).toHaveTextContent('Database is locked.')
    expect(await screen.findByText('Toloka')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the API sentence in place of the list for a non-owner', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderPage(member)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Only the installation owner can do this.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(integrationsApi.list).toHaveBeenCalledOnce()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add integration' })).not.toBeInTheDocument()
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Kind' })).not.toBeInTheDocument()
  })
})
