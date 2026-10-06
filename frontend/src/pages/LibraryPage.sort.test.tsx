import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { libraryApi } from '@/lib/library-api'
import { LibraryPage } from '@/pages/LibraryPage'
import { owner, signInAs } from '@/test/auth-session'
import { aliens, continental, locations, sourceOf, sourcesOf, unknownWork, worksPage } from '@/test/library'

// MAM-8 — the order of the Library's works: sorted by the server, sent only when it isn't Title (its default),
// a new order starts at page 1, and it is no filter, so Clear filters leaves it.

vi.mock('@/lib/library-api')

/** What the last request for the works asked for. */
const lastQuery = () => vi.mocked(libraryApi.works).mock.lastCall?.[0]

const sortSelect = () => screen.getByRole('combobox', { name: 'Sort by' })

function renderPage() {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/library']}>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

describe('LibraryPage sort', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, [aliens, continental, unknownWork], 30))
    vi.mocked(libraryApi.locations).mockResolvedValue(locations)
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf(sourceOf({})))
  })

  it('offers the orders, Title A–Z first and picked, and sends none for it', async () => {
    // Arrange
    renderPage()

    // Act
    await screen.findByText('1–25 of 30')

    // Assert
    expect(within(sortSelect()).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Title A–Z',
      'Title Z–A',
      'Year, newest first',
      'Year, oldest first',
      'Largest on disk',
      'Recently added',
    ])
    expect(sortSelect()).toHaveValue('Title')
    expect(lastQuery()?.sort).toBeUndefined()
  })

  it('stands the order beside the row the filters wrap in, never inside it, so it cannot come between the filters and Clear filters', async () => {
    // Arrange
    renderPage()

    // Act
    await screen.findByText('1–25 of 30')

    // Assert
    const label = sortSelect().closest('label')
    const toolbar = screen.getByRole('searchbox', { name: 'Search the library' }).parentElement
    expect(label).toHaveTextContent('Sort by')
    expect(label).toHaveClass('settings-sort')
    expect(toolbar).toHaveClass('settings-toolbar')
    expect(toolbar).not.toContainElement(label)
    expect(label?.parentElement).toBe(toolbar?.parentElement)
    expect(label?.parentElement).toHaveClass('settings-filterbar')
  })

  it.each([
    ['Title Z–A', 'TitleDescending'],
    ['Year, newest first', 'Newest'],
    ['Year, oldest first', 'Oldest'],
    ['Largest on disk', 'Largest'],
    ['Recently added', 'Added'],
  ] as const)('asks the server for %s and starts again at page 1', async (option, sort) => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.selectOptions(sortSelect(), option)

    // Assert
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ page: 1, pageSize: 25, sort })
    expect(sortSelect()).toHaveValue(sort)
  })

  it('sends no order again once Title A–Z is picked back', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.selectOptions(sortSelect(), 'Largest on disk')
    await vi.waitFor(() => expect(lastQuery()?.sort).toBe('Largest'))

    // Act
    await user.selectOptions(sortSelect(), 'Title A–Z')

    // Assert
    await vi.waitFor(() => expect(lastQuery()?.sort).toBeUndefined())
    expect(sortSelect()).toHaveValue('Title')
  })

  it('keeps the order picked with the filters, and Clear filters leaves it', async () => {
    // Arrange
    vi.mocked(libraryApi.works).mockImplementation(async (query) =>
      worksPage(query, query.identifiers ? [] : [aliens, continental]))
    const { user } = renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Чужі' })
    await user.selectOptions(sortSelect(), 'Recently added')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Identifiers' }), 'Missing an id')
    await screen.findByText('Nothing in the library matches these filters.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(lastQuery()).toEqual({ page: 1, pageSize: 25, sort: 'Added' })
    expect(sortSelect()).toHaveValue('Added')
  })
})
