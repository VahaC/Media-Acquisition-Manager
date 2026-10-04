import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, MemoryRouter, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { HeaderSearch } from '@/components/HeaderSearch'
import { Dialog, DialogContent, DialogDescription, DialogTitle } from '@/components/ui/dialog'
import { qk } from '@/lib/queries'
import { searchApi } from '@/lib/search-api'
import { aliensSearch, searchId } from '@/test/search'

vi.mock('@/lib/search-api')

/** The whole address the app is at: its path, then its search. */
function AddressProbe() {
  const { pathname, search } = useLocation()
  return <output data-testid="address">{pathname + search}</output>
}

const address = () => screen.getByTestId('address').textContent

/** The browser's Back button. */
function BackButton() {
  const navigate = useNavigate()
  return <button type="button" onClick={() => navigate(-1)}>Back</button>
}

/** What the Search page does once the search of its `?q=` answers: the address takes its id instead. */
function SearchMade() {
  const [, setSearchParams] = useSearchParams()
  return <button type="button" onClick={() => setSearchParams({ id: searchId }, { replace: true })}>Search made</button>
}

/**
 * The field at `path` (the last of `before` and it), beside the kinds of field a page has, a link to
 * the Library page, Back and, with `dialog`, an open dialog — the way it sits in the header above
 * every page.
 */
function renderAt(
  path: string,
  { client = new QueryClient(), dialog = false, before = [] }: { client?: QueryClient; dialog?: boolean; before?: string[] } = {},
) {
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[...before, path]} initialIndex={before.length}>
        <HeaderSearch />
        <BackButton />
        <SearchMade />
        <input aria-label="A page's field" />
        <textarea aria-label="A page's notes" />
        <select aria-label="A page's choice"><option>One</option></select>
        <Link to="/library">Library</Link>
        {dialog && (
          <Dialog open>
            <DialogContent>
              <DialogTitle>Release details</DialogTitle>
              <DialogDescription>What the source lists.</DialogDescription>
            </DialogContent>
          </Dialog>
        )}
        <AddressProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const field = () => screen.getByRole('searchbox', { name: 'Search for a movie or a series' })

/** The client with the stored Aliens search in its cache, as the Search page leaves it. */
function clientWithAliens() {
  const client = new QueryClient()
  client.setQueryData(qk.search.detail(searchId), aliensSearch)
  return client
}

describe('HeaderSearch', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('opens the Search page at the trimmed line on Enter, and lets go of the focus', async () => {
    // Arrange
    const { user } = renderAt('/library')
    await user.type(field(), '  Interstellar (2014) & more  ')

    // Act
    await user.keyboard('{Enter}')

    // Assert
    expect(address()).toBe(`/search?${new URLSearchParams({ q: 'Interstellar (2014) & more' })}`)
    expect(field()).not.toHaveFocus()
    expect(field()).toHaveValue('Interstellar (2014) & more')
  })

  it.each([
    ['empty', ''],
    ['blank', '   '],
  ])('opens the bare Search page on Enter in an %s field', async (_, typed) => {
    // Arrange
    const { user } = renderAt('/library')
    await user.click(field())
    if (typed !== '') await user.keyboard(typed)

    // Act
    await user.keyboard('{Enter}')

    // Assert
    expect(address()).toBe('/search')
  })

  it('jumps to the field when / is pressed on the page, without typing the slash', async () => {
    // Arrange
    const { user } = renderAt('/library')

    // Act
    await user.keyboard('/')

    // Assert
    expect(field()).toHaveFocus()
    expect(field()).toHaveValue('')
  })

  it.each([
    ['a text field', "A page's field"],
    ['a text area', "A page's notes"],
  ])('leaves / to %s, which types it', async (_, name) => {
    // Arrange
    const { user } = renderAt('/library')
    await user.click(screen.getByLabelText(name))

    // Act
    await user.keyboard('/')

    // Assert
    expect(screen.getByLabelText(name)).toHaveValue('/')
    expect(field()).not.toHaveFocus()
  })

  it('leaves / to a select', async () => {
    // Arrange
    const { user } = renderAt('/library')
    const select = screen.getByRole('combobox', { name: "A page's choice" })
    select.focus()

    // Act
    await user.keyboard('/')

    // Assert
    expect(select).toHaveFocus()
  })

  it('leaves / to editable content', () => {
    // Arrange
    renderAt('/library')
    const editor = document.createElement('div')
    editor.contentEditable = 'true'
    // jsdom has no isContentEditable; a browser answers true here.
    Object.defineProperty(editor, 'isContentEditable', { value: true })
    document.body.append(editor)

    // Act
    const typed = fireEvent.keyDown(editor, { key: '/' })

    // Assert
    expect(typed).toBe(true)
    expect(field()).not.toHaveFocus()
    editor.remove()
  })

  it('leaves / alone while a dialog is open, as the dialog keeps the focus', () => {
    // Arrange
    renderAt('/library', { dialog: true })
    const dialog = screen.getByRole('dialog', { name: 'Release details' })

    // Act
    const typed = fireEvent.keyDown(dialog, { key: '/' })

    // Assert
    expect(typed).toBe(true)
    // The open dialog hides the rest of the page from the accessibility tree.
    expect(screen.getByRole('searchbox', { name: 'Search for a movie or a series', hidden: true })).not.toHaveFocus()
  })

  it.each([
    ['Ctrl', { ctrlKey: true }],
    ['Cmd', { metaKey: true }],
    ['Alt', { altKey: true }],
  ])('leaves %s+/ to the browser and other programs', (_, modifier) => {
    // Arrange
    renderAt('/library')

    // Act
    const typed = fireEvent.keyDown(document.body, { key: '/', ...modifier })

    // Assert
    expect(typed).toBe(true)
    expect(field()).not.toHaveFocus()
  })

  it('leaves / to an input method that is composing text', () => {
    // Arrange
    renderAt('/library')

    // Act
    const typed = fireEvent.keyDown(document.body, { key: '/', isComposing: true })

    // Assert
    expect(typed).toBe(true)
    expect(field()).not.toHaveFocus()
  })

  it('leaves / alone once something on the page has handled it', () => {
    // Arrange
    renderAt('/library')
    const handle = (event: KeyboardEvent) => event.preventDefault()
    document.body.addEventListener('keydown', handle)

    // Act
    fireEvent.keyDown(document.body, { key: '/' })
    document.body.removeEventListener('keydown', handle)

    // Assert
    expect(field()).not.toHaveFocus()
  })

  it('shows the text of the search on screen on the Search page, without asking for it', () => {
    // Arrange
    const client = clientWithAliens()

    // Act
    renderAt(`/search?id=${searchId}`, { client })

    // Assert
    expect(field()).toHaveValue('Aliens')
    expect(searchApi.get).not.toHaveBeenCalled()
  })

  it('follows the search on screen once the Search page has loaded it', async () => {
    // Arrange
    const { client } = renderAt(`/search?id=${searchId}`)
    expect(field()).toHaveValue('')

    // Act
    act(() => { client.setQueryData(qk.search.detail(searchId), aliensSearch) })

    // Assert
    await vi.waitFor(() => expect(field()).toHaveValue('Aliens'))
    expect(searchApi.get).not.toHaveBeenCalled()
  })

  it('keeps what the person typed when the search on screen loads after it', async () => {
    // Arrange
    const { client, user } = renderAt(`/search?id=${searchId}`)
    await user.type(field(), 'Interstellar')

    // Act
    act(() => { client.setQueryData(qk.search.detail(searchId), aliensSearch) })
    // TanStack Query tells its observers on the next task, after act has returned.
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)))

    // Assert
    expect(field()).toHaveValue('Interstellar')
  })

  it('keeps what the person is typing when the Search page moves from the line to the search it made', async () => {
    // Arrange
    const { user } = renderAt(`/search?${new URLSearchParams({ q: 'Aliens' })}`, { client: clientWithAliens() })
    await user.clear(field())
    await user.type(field(), 'Inters')

    // Act
    await user.click(screen.getByRole('button', { name: 'Search made' }))

    // Assert
    expect(address()).toBe(`/search?id=${searchId}`)
    expect(field()).toHaveValue('Inters')
  })

  it('shows the search Back returns to, in place of what the person typed', async () => {
    // Arrange
    const { user } = renderAt('/search?id=5ea4c000-0000-4000-8000-000000000002', {
      client: clientWithAliens(),
      before: [`/search?id=${searchId}`],
    })
    await user.type(field(), 'Inters')

    // Act
    await user.click(screen.getByRole('button', { name: 'Back' }))

    // Assert
    expect(field()).toHaveValue('Aliens')
  })

  it('follows the line it searched for after Enter, on the Search page too', async () => {
    // Arrange
    const { user } = renderAt(`/search?id=${searchId}`, { client: clientWithAliens() })
    await user.clear(field())
    await user.type(field(), '  Interstellar  ')

    // Act
    await user.keyboard('{Enter}')

    // Assert
    expect(address()).toBe('/search?q=Interstellar')
    expect(field()).toHaveValue('Interstellar')
  })

  it('shows the line a search is still being made from', () => {
    // Arrange
    const path = `/search?${new URLSearchParams({ q: 'Interstellar (2014)' })}`

    // Act
    renderAt(path)

    // Assert
    expect(field()).toHaveValue('Interstellar (2014)')
  })

  it.each([
    ['a way out to another API path', '..%2Flibrary%2Fworks'],
    ['not an id at all', 'aliens'],
  ])('reads nothing for an id in the address that is %s', (_, id) => {
    // Arrange
    const client = new QueryClient()
    client.setQueryData(qk.search.detail(decodeURIComponent(id)), aliensSearch)

    // Act
    renderAt(`/search?id=${id}&q=Aliens`, { client })

    // Assert
    expect(field()).toHaveValue('')
    expect(searchApi.get).not.toHaveBeenCalled()
  })

  it('starts empty on other pages, also after the Search page', async () => {
    // Arrange
    const { user } = renderAt(`/search?id=${searchId}`, { client: clientWithAliens() })
    expect(field()).toHaveValue('Aliens')

    // Act
    await user.click(screen.getByRole('link', { name: 'Library' }))

    // Assert
    expect(address()).toBe('/library')
    expect(field()).toHaveValue('')
  })

  it('drops what the person typed once they move to another page', async () => {
    // Arrange
    const { user } = renderAt('/')
    await user.type(field(), 'Inters')

    // Act
    await user.click(screen.getByRole('link', { name: 'Library' }))

    // Assert
    expect(field()).toHaveValue('')
  })

  it('shows the / key only while the field is empty, and a press on it focuses the field', async () => {
    // Arrange
    const { user } = renderAt('/library')
    const key = screen.getByTitle('Press / to search')
    expect(key).toHaveTextContent('/')

    // Act
    const pressed = fireEvent.mouseDown(key)

    // Assert
    expect(pressed).toBe(false)
    expect(field()).toHaveFocus()
    await user.keyboard('A')
    expect(screen.queryByTitle('Press / to search')).not.toBeInTheDocument()
  })

  it('tells assistive technology about the / key on the field itself', () => {
    // Arrange
    const path = '/library'

    // Act
    renderAt(path)

    // Assert
    expect(field()).toHaveAttribute('aria-keyshortcuts', '/')
    expect(field()).toHaveAttribute('placeholder', 'Search a movie or a series by title, e.g. Interstellar (2014)')
    expect(screen.getByRole('search')).toContainElement(field())
  })

  it.each([
    ['marks', `/search?id=${searchId}`, true],
    ['does not mark', '/library', false],
  ])('%s its search icon on %s', (_, path, current) => {
    // Arrange
    const at = path

    // Act
    renderAt(at)

    // Assert
    expect(screen.getByRole('search').querySelector('svg')?.hasAttribute('data-current')).toBe(current)
  })
})
