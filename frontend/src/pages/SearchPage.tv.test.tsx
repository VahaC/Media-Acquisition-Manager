import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { searchApi } from '@/lib/search-api'
import { SearchPage } from '@/pages/SearchPage'
import { owner, signInAs } from '@/test/auth-session'
import { releasesPage, searchId, searchOf, tolokaRemux, yellowstonePack, yellowstoneSearch } from '@/test/search'

// MAM-32 — the Search page for a series: a series card in "In your library", TV releases in the list
// with a kind filter, and a season to narrow the search by.

vi.mock('@/lib/search-api')
vi.mock('@/lib/downloads-api')

/** The search part of the address the page is at. */
function LocationProbe() {
  return <output data-testid="location">{useLocation().search}</output>
}

function openStored() {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/search?id=${searchId}`]}>
        <SearchPage />
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

const seasonField = () => screen.getByRole('textbox', { name: 'Season (optional)' })

describe('SearchPage for a series', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(searchApi.get).mockResolvedValue(yellowstoneSearch)
    vi.mocked(searchApi.releases).mockImplementation(async (_, query) =>
      releasesPage(query, query.kind === 'Tv' ? [yellowstonePack] : [tolokaRemux, yellowstonePack]))
  })

  it('lists a series the search found as a card with its seasons, and why it matched', async () => {
    // Arrange
    openStored()

    // Act
    const library = await screen.findByRole('region', { name: 'In your library' })

    // Assert
    const card = await within(library).findByRole('article', { name: 'Єллоустоун' })
    expect(within(card).getByText('Series')).toBeInTheDocument()
    expect(within(card).getByRole('list', { name: 'Seasons' })).toBeInTheDocument()
    expect(within(within(card).getByRole('list', { name: 'Why it matched' })).getAllByRole('listitem')
      .map((item) => item.textContent)).toEqual(['Matched by title and year', '“Yellowstone”, 2018'])
  })

  it('shows a TV release beside the movies, with the episodes of it the library holds', async () => {
    // Arrange
    openStored()

    // Act
    const row = await screen.findByRole('article', { name: yellowstonePack.title })

    // Assert
    expect(within(within(row).getByRole('list', { name: 'Claimed' })).getAllByRole('listitem')[0]).toHaveTextContent('TV')
    expect(within(row).getByText('19 of its episodes in your library')).toBeInTheDocument()
    expect(screen.getByRole('article', { name: tolokaRemux.title })).toBeInTheDocument()
  })

  it('lists the TV releases alone through the kind filter, asked of the server', async () => {
    // Arrange
    const { user } = openStored()
    await screen.findByRole('article', { name: tolokaRemux.title })

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'TV')

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('article', { name: tolokaRemux.title })).not.toBeInTheDocument())
    expect(vi.mocked(searchApi.releases).mock.lastCall?.[1]).toMatchObject({ kind: 'Tv', page: 1 })
    expect(screen.getByRole('article', { name: yellowstonePack.title })).toBeInTheDocument()
  })

  it('shows the season the search on screen was narrowed by in the form', async () => {
    // Arrange
    vi.mocked(searchApi.get).mockResolvedValue({ ...yellowstoneSearch, season: 0 })

    // Act
    openStored()

    // Assert
    await vi.waitFor(() => expect(seasonField()).toHaveValue('0'))
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('2018')
  })

  it('narrows the search by a season, sent as typed and trimmed', async () => {
    // Arrange
    const nextId = '5ea4c000-0000-4000-8000-000000000002'
    vi.mocked(searchApi.create).mockResolvedValue(searchOf({ ...yellowstoneSearch, id: nextId, season: 2 }))
    const { user } = openStored()
    await vi.waitFor(() => expect(seasonField()).toHaveValue(''))
    expect(seasonField()).toHaveAttribute('placeholder', '1')

    // Act
    await user.type(seasonField(), ' 2 ')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    // Assert
    await vi.waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`?id=${nextId}`))
    expect(searchApi.create).toHaveBeenCalledExactlyOnceWith({ text: 'Yellowstone', year: '2018', season: '2' })
  })
})
