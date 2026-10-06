import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryApi } from '@/lib/library-api'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { CurrentUser } from '@/lib/types'
import { LibraryPage } from '@/pages/LibraryPage'
import { member, owner, signInAs } from '@/test/auth-session'
import { aliens, locations, sourceOf, sourcesOf, worksPage } from '@/test/library'
import { dialogOf, landingStripling } from '@/test/library-links'

vi.mock('@/lib/library-api')
vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the Library page's "TMDb…" on each card: the owner opens the work's TMDb dialog; a member
// sees the action disabled with the reason. Nothing is asked for it until the dialog opens.

/** The TMDb page in these tests: a Back button, as the browser's. */
function TmdbPageStandIn() {
  const navigate = useNavigate()
  return <button type="button" onClick={() => navigate(-1)}>Browser Back</button>
}

function renderPage(signedIn: CurrentUser = owner, path = '/library') {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/library" element={<LibraryPage />} />
            <Route path="/library/tmdb" element={<TmdbPageStandIn />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

const identifiersFilter = () => screen.getByRole('combobox', { name: 'Identifiers' })

/** The card of the work titled `title`. */
const cardOf = async (title: string) => (await screen.findByRole('heading', { level: 2, name: title })).closest('article')!

describe('LibraryPage TMDb links', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryApi.works).mockImplementation(async (query) => worksPage(query, [aliens, landingStripling]))
    vi.mocked(libraryApi.locations).mockResolvedValue(locations)
    vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf(sourceOf({})))
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf())
  })

  it('gives every card a TMDb action, and asks for no link until one is opened', async () => {
    // Arrange
    renderPage()

    // Act
    const card = await cardOf('Чужі')

    // Assert
    expect(within(card).getByRole('button', { name: 'TMDb…' })).toBeEnabled()
    expect(within(await cardOf('120. Landing Stripling')).getByRole('button', { name: 'TMDb…' })).toBeEnabled()
    expect(libraryLinksApi.dialog).not.toHaveBeenCalled()
  })

  it('opens the work\'s TMDb dialog from its card and closes it on Cancel', async () => {
    // Arrange
    const { user } = renderPage()
    const card = await cardOf('120. Landing Stripling')

    // Act
    await user.click(within(card).getByRole('button', { name: 'TMDb…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Link “120. Landing Stripling” to TMDb' })
    await within(dialog).findByRole('heading', { name: 'Suggestions' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(libraryLinksApi.dialog).toHaveBeenCalledWith(landingStripling.id)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('gives the focus to the dialog as it opens, by a click or a key, and back to the card\'s TMDb… as it closes', async () => {
    // Arrange
    const { user } = renderPage()
    const button = within(await cardOf('120. Landing Stripling')).getByRole('button', { name: 'TMDb…' })

    // Act
    await user.click(button)
    const byClick = await screen.findByRole('dialog', { name: 'Link “120. Landing Stripling” to TMDb' })
    const focusedByClick = document.activeElement
    await within(byClick).findByRole('heading', { name: 'Suggestions' })
    const focusedLoaded = document.activeElement
    await user.keyboard('{Escape}')
    await waitFor(() => expect(button).toHaveFocus())
    await user.keyboard('{Enter}')
    const byKey = await screen.findByRole('dialog', { name: 'Link “120. Landing Stripling” to TMDb' })
    await within(byKey).findByRole('heading', { name: 'Suggestions' })
    const focusedByKey = document.activeElement
    await user.click(within(byKey).getByRole('button', { name: 'Cancel' }))

    // Assert
    // The dialog itself each time, whatever had loaded: never a control of its body.
    expect(focusedByClick).toBe(byClick)
    expect(focusedLoaded).toBe(byClick)
    expect(focusedByKey).toBe(byKey)
    await waitFor(() => expect(button).toHaveFocus())
  })

  it('shows a member the action disabled with the reason', async () => {
    // Arrange
    renderPage(member)

    // Act
    const button = within(await cardOf('Чужі')).getByRole('button', { name: 'TMDb…' })

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner can link works to TMDb.')
  })

  it('offers "Review works without a TMDb id" only with the "Missing an id" filter on, as a link to the TMDb page', async () => {
    // Arrange
    const { user } = renderPage()
    await cardOf('Чужі')
    const before = screen.queryByRole('link', { name: 'Review works without a TMDb id' })

    // Act
    await user.selectOptions(identifiersFilter(), 'Missing')

    // Assert
    expect(before).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Review works without a TMDb id' })).toHaveAttribute('href', '/library/tmdb')
  })

  it('shows a member the entry disabled with the reason', async () => {
    // Arrange
    const { user } = renderPage(member)
    await cardOf('Чужі')

    // Act
    await user.selectOptions(identifiersFilter(), 'Missing')

    // Assert
    const button = screen.getByRole('button', { name: 'Review works without a TMDb id' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner can link works to TMDb.')
    expect(screen.queryByRole('link', { name: 'Review works without a TMDb id' })).not.toBeInTheDocument()
  })

  it('opens with "Missing an id" on when its address says so, as the TMDb page links back', async () => {
    // Arrange
    renderPage(owner, '/library?identifiers=Missing')

    // Act
    await cardOf('Чужі')

    // Assert
    expect(identifiersFilter()).toHaveValue('Missing')
    expect(libraryApi.works).toHaveBeenCalledWith(expect.objectContaining({ identifiers: 'Missing', page: 1 }))
    expect(screen.getByRole('link', { name: 'Review works without a TMDb id' })).toBeInTheDocument()
  })

  it('finds the search and the filter again on Back from the TMDb page', async () => {
    // Arrange
    const { user } = renderPage()
    await cardOf('Чужі')
    await user.type(screen.getByRole('searchbox', { name: 'Search the library' }), 'Landing')
    await user.selectOptions(identifiersFilter(), 'Missing')

    // Act
    await user.click(screen.getByRole('link', { name: 'Review works without a TMDb id' }))
    await user.click(await screen.findByRole('button', { name: 'Browser Back' }))

    // Assert
    expect(await screen.findByRole('searchbox', { name: 'Search the library' })).toHaveValue('Landing')
    expect(identifiersFilter()).toHaveValue('Missing')
  })
})
