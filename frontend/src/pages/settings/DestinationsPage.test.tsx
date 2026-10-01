import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { destinationsApi } from '@/lib/destinations-api'
import { integrationsApi } from '@/lib/integrations-api'
import type { CurrentUser, Destination, DestinationCheck, DestinationPathStatus } from '@/lib/types'
import { DestinationsPage } from '@/pages/settings/DestinationsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { cartoons, checkOf, destinationsPage, incoming, movies, sampleCategories } from '@/test/destinations'
import { localQbittorrent } from '@/test/integrations'

vi.mock('@/lib/destinations-api')
vi.mock('@/lib/integrations-api')

/** What the last request for the list asked for. */
const lastQuery = () => vi.mocked(destinationsApi.list).mock.lastCall?.[0]

/** Matches a destination's name only: its category often has the same text. */
const asName = { selector: '.settings-name-text' }

/** The table row (or phone card) of the destination named `name`. */
const rowOf = (name: string) => screen.getByText(name, asName).closest('tr')!

/** The server lists `items`, out of `total`, whatever page is asked for. */
function listing(items: Destination[], total = items.length) {
  vi.mocked(destinationsApi.list).mockImplementation(async (query) => destinationsPage(query, items, total))
}

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConfirmProvider>
          <DestinationsPage />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

describe('DestinationsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    listing([cartoons, incoming, movies])
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [localQbittorrent], totalCount: 1, page: query.page, pageSize: query.pageSize }))
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue(sampleCategories)
  })

  it('lists every destination with its client, category, where it saves and the Default badge', async () => {
    // Arrange
    const query = { search: undefined, page: 1, pageSize: 25 }

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Incoming', asName)).toBeInTheDocument()
    expect(lastQuery()).toEqual(query)
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent))
      .toEqual(['Name', 'Download client', 'Category', 'Saves to', 'Actions'])
    const row = within(rowOf('Movies'))
    expect(row.getByText('Default')).toHaveClass('ui-badge')
    expect(row.getByText('Local qBittorrent')).toBeInTheDocument()
    expect(row.getByText('Movies', { selector: '.destinations-key' })).toBeInTheDocument()
    expect(row.getByText('/downloads/movies')).toBeInTheDocument()
    expect(row.getByText('/media/movies')).toBeInTheDocument()
    expect(row.getByText('In the media library Фільми')).toBeInTheDocument()
    expect(within(rowOf('Cartoons')).queryByText('Default')).not.toBeInTheDocument()
    expect(within(rowOf('Cartoons')).getByText('In the media library Мульфільми')).toBeInTheDocument()
    expect(rowOf('Movies').querySelector('td[data-label="Saves to"]')).toHaveClass('destinations-saves-to')
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument()
  })

  it('names every media library the folder lands in', async () => {
    // Arrange
    listing([{ ...movies, check: checkOf({ libraryNames: ['Фільми', 'Колекції'] }) }])

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('In the media libraries Фільми and Колекції')).toBeInTheDocument()
  })

  it('shows the client\'s folder once when this app sees it at the same path', async () => {
    // Arrange
    listing([{ ...movies, check: checkOf({ servicePath: '/media/movies', appPath: '/media/movies' }) }])

    // Act
    renderPage()

    // Assert
    await screen.findByText('In the media library Фільми')
    expect(within(rowOf('Movies')).getAllByText('/media/movies')).toHaveLength(1)
    expect(within(rowOf('Movies')).queryByText('which this app reaches at')).not.toBeInTheDocument()
  })

  it.each<[DestinationPathStatus, string, Partial<DestinationCheck>, string[]]>([
    ['OutsideLibraries', 'Outside the media libraries', {
      servicePath: '/downloads/incoming',
      appPath: '/media/incoming',
      problem: '“/media/incoming” is outside every media library folder, so automatic replacement stays off for this destination.',
    }, ['/downloads/incoming', '/media/incoming']],
    ['PathUnknown', 'Folder unknown', {
      servicePath: null,
      appPath: null,
      problem: 'The client doesn\'t say which folder this category saves to, so automatic replacement stays off for this destination.',
    }, []],
    ['LibrariesUnknown', 'Media libraries not known yet', {
      problem: 'The app doesn\'t know the media library folders yet. Sync a library from the Library page, then look again.',
    }, ['/downloads/movies', '/media/movies']],
    ['CategoryMissing', 'Category missing', {
      servicePath: null,
      appPath: null,
      problem: '“Local qBittorrent” has no category “Movies” any more.',
    }, []],
    ['ClientDisabled', 'Client switched off', {
      servicePath: null,
      appPath: null,
      problem: '“Local qBittorrent” is switched off. Switch it on under Integrations to check this destination.',
    }, []],
    ['ClientUnavailable', 'Client unavailable', {
      servicePath: null,
      appPath: null,
      problem: 'qBittorrent didn\'t answer at http://127.0.0.1:9/qbt. Check the address and that qBittorrent is running.',
    }, []],
  ])('flags a destination whose check is %s with its label and the API sentence', async (status, label, check, paths) => {
    // Arrange
    listing([{ ...movies, check: checkOf({ status, libraryNames: [], ...check }) }])

    // Act
    renderPage()

    // Assert
    const savesTo = await vi.waitFor(() => rowOf('Movies').querySelector<HTMLElement>('td.destinations-saves-to')!)
    const cell = within(savesTo)
    expect(cell.getByText(label)).toHaveClass('library-badge-warning')
    expect(cell.getByText(check.problem!)).toHaveClass('destinations-check-problem')
    expect([...savesTo.querySelectorAll('.destinations-paths > span:not(.sr-only)')].map((path) => path.textContent))
      .toEqual(paths)
    expect(cell.queryByText(/^In the media librar/)).not.toBeInTheDocument()
  })

  it('asks the server for the search once typing pauses and starts again at page 1', async () => {
    // Arrange
    vi.mocked(destinationsApi.list).mockImplementation(async (query) =>
      destinationsPage(query, query.search ? [movies] : query.page === 1 ? [cartoons, incoming] : [movies], 30))
    const { user } = renderPage()
    await screen.findByText('Page 1 of 2')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Page 2 of 2')

    // Act
    await user.type(screen.getByRole('searchbox', { name: 'Search destinations' }), ' mov ')

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ search: 'mov', page: 1, pageSize: 25 }))
    expect(vi.mocked(destinationsApi.list).mock.calls.map(([query]) => query)).toEqual([
      { search: undefined, page: 1, pageSize: 25 },
      { search: undefined, page: 2, pageSize: 25 },
      { search: 'mov', page: 1, pageSize: 25 },
    ])
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
  })

  it('remembers the page size on this device', async () => {
    // Arrange
    listing([cartoons, movies], 120)
    const { user } = renderPage()
    await screen.findByText('Page 1 of 5')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:destinations:pageSize')).toBe('50')
    // Its own key: the Integrations page keeps its size.
    expect(localStorage.getItem('mam:integrations:pageSize')).toBeNull()
  })

  it('says when nothing matches, and Clear search shows everything again', async () => {
    // Arrange
    vi.mocked(destinationsApi.list).mockImplementation(async (query) =>
      destinationsPage(query, query.search ? [] : [cartoons, movies]))
    const { user } = renderPage()
    await screen.findByText('Cartoons', asName)
    await user.type(screen.getByRole('searchbox', { name: 'Search destinations' }), '4K')
    await screen.findByText('No destinations match “4K”.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear search' }))

    // Assert
    expect(await screen.findByText('Cartoons', asName)).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 25 })
    expect(screen.getByRole('searchbox', { name: 'Search destinations' })).toHaveValue('')
    expect(screen.queryByText('No destinations match “4K”.')).not.toBeInTheDocument()
  })

  it('names Add destination in the empty state', async () => {
    // Arrange
    listing([])

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(/^No destinations yet\./)).toHaveTextContent('Click Add destination to pick a category')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear search' })).not.toBeInTheDocument()
  })

  it('removes a destination only once the confirmation is accepted', async () => {
    // Arrange
    vi.mocked(destinationsApi.remove).mockImplementation(async () => {
      listing([cartoons, movies])
    })
    const { user } = renderPage()
    await screen.findByText('Incoming', asName)
    await user.click(within(rowOf('Incoming')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Incoming?' })
    const calledBeforeConfirming = vi.mocked(destinationsApi.remove).mock.calls.length

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByText('Incoming', asName)).not.toBeInTheDocument())
    expect(calledBeforeConfirming).toBe(0)
    expect(destinationsApi.remove).toHaveBeenCalledExactlyOnceWith(incoming.id)
    expect(confirmation).toHaveTextContent(
      'The app forgets the destination Incoming. Its category stays in Local qBittorrent, and nothing changes in the client. This can\'t be undone.')
  })

  it('warns before removing the default that no destination is the default after it', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Incoming', asName)

    // Act
    await user.click(within(rowOf('Movies')).getByRole('button', { name: 'Remove' }))

    // Assert
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Movies?' })
    expect(confirmation).toHaveTextContent(
      'Movies is the default destination: no destination is the default until you pick another one.')
    expect(destinationsApi.remove).not.toHaveBeenCalled()
  })

  it('does not remove a destination when the confirmation is cancelled', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Incoming', asName)
    await user.click(within(rowOf('Incoming')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Incoming?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(destinationsApi.remove).not.toHaveBeenCalled()
    expect(screen.getByText('Incoming', asName)).toBeInTheDocument()
  })

  it('shows the fallback sentence above the list when a removal is refused without one', async () => {
    // Arrange
    vi.mocked(destinationsApi.remove).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    await screen.findByText('Incoming', asName)
    await user.click(within(rowOf('Incoming')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Incoming?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not change this destination. Reload the page and try again.')
    expect(screen.getByText('Incoming', asName)).toBeInTheDocument()
  })

  it('steps back a page after removing the only destination of the last page', async () => {
    // Arrange
    const firstPage = Array.from({ length: 25 }, (_, index): Destination =>
      ({ ...cartoons, id: `6a3e9b20-0000-4000-8000-1000000000${String(index).padStart(2, '0')}`, name: `Shelf ${index}` }))
    let lastPage = [incoming]
    vi.mocked(destinationsApi.list).mockImplementation(async (query) =>
      destinationsPage(query, query.page === 1 ? firstPage : lastPage, firstPage.length + lastPage.length))
    vi.mocked(destinationsApi.remove).mockImplementation(async () => {
      lastPage = []
    })
    const { user } = renderPage()
    await screen.findByText('Page 1 of 2')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Incoming', asName)
    await user.click(within(rowOf('Incoming')).getByRole('button', { name: 'Remove' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove Incoming?' })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

    // Assert
    expect(await screen.findByText('Page 1 of 1')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ search: undefined, page: 1, pageSize: 25 })
    expect(screen.getByText('Shelf 0')).toBeInTheDocument()
  })

  it('collapses each row action to its icon with the label kept in its title', async () => {
    // Arrange
    renderPage()

    // Act
    const buttons = within(await vi.waitFor(() => rowOf('Movies'))).getAllByRole('button')

    // Assert
    expect(buttons.map((button) => button.getAttribute('title'))).toEqual(['Edit', 'Remove'])
    for (const button of buttons) expect(button.querySelector('.label-text')).toHaveTextContent(button.title)
    expect(buttons[1]).toHaveClass('ui-button-danger')
  })

  it('opens Add destination from the header', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Incoming', asName)

    // Act
    await user.click(screen.getByRole('button', { name: 'Add destination' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Add destination' })
    expect(await within(dialog).findByRole('option', { name: 'Local qBittorrent' })).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Name')).toHaveValue('')
  })

  it('opens Edit for the destination of that row', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('Incoming', asName)

    // Act
    await user.click(within(rowOf('Cartoons')).getByRole('button', { name: 'Edit' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Edit Cartoons' })
    expect(within(dialog).getByLabelText('Name')).toHaveValue('Cartoons')
    expect(within(dialog).getByRole('combobox', { name: 'Category' })).toHaveValue('Cartoons')
  })

  it('keeps the rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText('Incoming', asName)
    vi.mocked(destinationsApi.list).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last loaded destinations.')
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(screen.getByText('Incoming', asName)).toBeInTheDocument()
  })

  it('shows its own sentence in place of the list when the first load fails without one', async () => {
    // Arrange
    vi.mocked(destinationsApi.list).mockRejectedValue(apiError(500))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the destinations.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows the API sentence in place of the list for a non-owner', async () => {
    // Arrange
    vi.mocked(destinationsApi.list).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderPage(member)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Only the installation owner can do this.')
    expect(destinationsApi.list).toHaveBeenCalledOnce()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add destination' })).not.toBeInTheDocument()
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
  })
})
