import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { HeaderSearch } from '@/components/HeaderSearch'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { LibraryLinkDialog } from '@/lib/types'
import { LibraryTmdbPage } from '@/pages/LibraryTmdbPage'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import {
  catchUpOf,
  confidentPage,
  dialogOf,
  itemOf,
  junglesDialogOf,
  landingStripling,
  libraryIdentifierOf,
  linkedDialogOf,
  linkedItemOf,
  linksPage,
  queueOf,
  suggestionRunOf,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/search-api')

// MAM-34 — the TMDb page's Review tab (R24): the works without a TMDb id one at a time, with the
// dialog's content inline, and its keys — 1–5 pick a suggestion, Enter or Save links it, 0 is "None
// fits", → skips (wrapping), / goes to the search field ahead of the header's — each left alone where
// it belongs to something else.

/** The works the queue holds, in its order; a link or "none fits" takes one out, as the API does. */
let waiting: LibraryLinkDialog[] = []

function serveQueue(...dialogs: LibraryLinkDialog[]) {
  waiting = [...dialogs]
  vi.mocked(libraryLinksApi.queue).mockImplementation(async (index) => queueOf(index, waiting.length, waiting[index] ?? null))
}

function leave(workId: string) {
  waiting = waiting.filter((dialog) => dialog.work.id !== workId)
}

/** The places the queue was asked for, in order. */
const askedIndexes = () => vi.mocked(libraryLinksApi.queue).mock.calls.map(([index]) => index)

function renderPage() {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={['/library/tmdb']}>
          {/* The header's search, whose "/" the Review tab must take first. */}
          <HeaderSearch />
          <Routes>
            <Route path="/library/tmdb" element={<LibraryTmdbPage />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The work on screen once it has loaded, by its heading. */
const workHeading = (name: string) => screen.findByRole('heading', { level: 2, name })

const firstPick = () => screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' })
const secondPick = () => screen.getByRole('radio', { name: 'Pick Calypso Cat (1962)' })
const headerSearch = () => screen.getByRole('searchbox', { name: 'Search for a movie or a series' })
const titleField = () => screen.getByRole('searchbox', { name: 'Title' })
const saveButton = () => screen.getByRole('button', { name: 'Save' })

describe('LibraryTmdbPage Review tab', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, []))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, []))
    serveQueue(dialogOf(), junglesDialogOf())
    vi.mocked(libraryLinksApi.link).mockImplementation(async (workId) => {
      leave(workId)
      return linkedDialogOf()
    })
    vi.mocked(libraryLinksApi.markNoneFits).mockImplementation(async (workId) => leave(workId))
    vi.mocked(libraryLinksApi.clearNoneFits).mockImplementation(async () => {
      waiting = [dialogOf(), ...waiting]
    })
  })

  it('shows the first work with its place, the dialog\'s content, and Save, None fits and Skip with their keys', async () => {
    // Arrange
    renderPage()

    // Act
    const card = (await workHeading('120. Landing Stripling (1962)')).closest('section')!

    // Assert
    expect(within(card).getByText('Work 1 of 2')).toBeInTheDocument()
    expect(within(card).getByText('Home Jellyfin · 120. Landing Stripling')).toBeInTheDocument()
    expect(firstPick()).toHaveAttribute('aria-keyshortcuts', '1')
    expect(secondPick()).toHaveAttribute('aria-keyshortcuts', '2')
    expect(titleField()).toHaveAttribute('aria-keyshortcuts', '/')
    expect(within(card.querySelector('.tmdb-review-actions')!).getAllByRole('button').map((button) => button.textContent))
      .toEqual(['SaveEnter', 'None fits0', 'Skip→'])
    expect(saveButton()).toHaveAttribute('aria-keyshortcuts', 'Enter')
    expect(saveButton()).toBeDisabled()
    expect(screen.getByRole('button', { name: 'None fits' })).toHaveAttribute('aria-keyshortcuts', '0')
    expect(screen.getByRole('button', { name: 'Skip' })).toHaveAttribute('aria-keyshortcuts', 'ArrowRight')
    // The hints are for the eye: aria-keyshortcuts tells a screen reader.
    card.querySelectorAll('kbd').forEach((key) => expect(key).toHaveAttribute('aria-hidden', 'true'))
    expect(askedIndexes()).toEqual([0])
  })

  it('picks a suggestion with its digit and links it with Enter, then shows the next work at the same place', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('2')
    const secondPicked = (secondPick() as HTMLInputElement).checked
    await user.keyboard('1')
    const firstPicked = (firstPick() as HTMLInputElement).checked
    await user.keyboard('{Enter}')

    // Assert
    expect([secondPicked, firstPicked]).toEqual([true, true])
    expect(libraryLinksApi.link).toHaveBeenCalledExactlyOnceWith(landingStripling.id, {
      catalogKind: 'Movie',
      catalogId: '223021',
      seasonNumber: null,
      episodeNumber: null,
      sourceItemIds: null,
    })
    expect(await workHeading('Planet_Earth_E08 - Jungles')).toBeInTheDocument()
    expect(screen.getByText('Work 1 of 1')).toBeInTheDocument()
    expect(askedIndexes().every((index) => index === 0)).toBe(true)
    expect(screen.getByText('Linked “120. Landing Stripling” to Посадка Стріплінга (Film, TMDb 223021).')).toBeInTheDocument()
  })

  it('links the suggestion a digit picked with Save too', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    await user.keyboard('1')
    expect(firstPick()).toBeChecked()

    // Act
    await user.click(saveButton())

    // Assert
    expect(libraryLinksApi.link).toHaveBeenCalledExactlyOnceWith(landingStripling.id, expect.objectContaining({ catalogId: '223021' }))
    expect(await workHeading('Planet_Earth_E08 - Jungles')).toBeInTheDocument()
  })

  it('saves nothing on Enter before a suggestion is picked, and picks nothing with a digit past the suggestions', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('3')
    await user.keyboard('{Enter}')

    // Assert
    expect(firstPick()).not.toBeChecked()
    expect(secondPick()).not.toBeChecked()
    expect(libraryLinksApi.link).not.toHaveBeenCalled()
  })

  it('says when Jellyfin didn\'t take the link\'s write, and still moves on', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockImplementation(async (workId) => {
      leave(workId)
      return linkedDialogOf({ items: [linkedItemOf({ writeState: 'NotWritten', writeError: 'Jellyfin refused to change the item\'s ids.' })] })
    })
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('1')
    await user.keyboard('{Enter}')

    // Assert
    expect(await workHeading('Planet_Earth_E08 - Jungles')).toBeInTheDocument()
    // The way to Retry: the Library card has only the state, its reason and Retry are in its dialog.
    expect(screen.getByText('Linked “120. Landing Stripling” to Посадка Стріплінга (Film, TMDb 223021), but Jellyfin '
      + 'doesn\'t hold the id yet: open TMDb… on its Library card to see why and Retry.')).toBeInTheDocument()
  })

  it('marks that none fits with 0, shows the next work at the same place, and takes the mark back with Undo', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('0')
    await workHeading('Planet_Earth_E08 - Jungles')
    const said = screen.getByText(/^Marked that none/).closest('[role="status"]')?.textContent
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    // Assert
    expect(said).toBe('Marked that none of the suggestions fits “120. Landing Stripling”.')
    expect(libraryLinksApi.markNoneFits).toHaveBeenCalledExactlyOnceWith(landingStripling.id)
    expect(libraryLinksApi.clearNoneFits).toHaveBeenCalledExactlyOnceWith(landingStripling.id)
    expect(await workHeading('120. Landing Stripling (1962)')).toBeInTheDocument()
    expect(askedIndexes().every((index) => index === 0)).toBe(true)
  })

  it('says why "none fits" was refused, and keeps the work', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.markNoneFits).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.click(screen.getByRole('button', { name: 'None fits' }))

    // Assert
    expect(await screen.findByText('Only the installation owner can do this.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: '120. Landing Stripling (1962)' })).toBeInTheDocument()
  })

  it('skips to the next work with →, and wraps to the first after the last', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('{ArrowRight}')
    await workHeading('Planet_Earth_E08 - Jungles')
    const second = screen.getByText('Work 2 of 2')
    await user.keyboard('{ArrowRight}')

    // Assert
    expect(second).toBeInTheDocument()
    expect(await workHeading('120. Landing Stripling (1962)')).toBeInTheDocument()
    expect(askedIndexes()).toEqual([0, 1, 0])
    expect(libraryLinksApi.markNoneFits).not.toHaveBeenCalled()
  })

  it('skips with the Skip button as well', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.click(screen.getByRole('button', { name: 'Skip' }))

    // Assert
    expect(await workHeading('Planet_Earth_E08 - Jungles')).toBeInTheDocument()
  })

  it('starts from the first work again when the last one was linked', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    await user.keyboard('{ArrowRight}')
    await workHeading('Planet_Earth_E08 - Jungles')
    vi.mocked(libraryLinksApi.link).mockImplementation(async (workId) => {
      leave(workId)
      return linkedDialogOf({ work: { ...junglesDialogOf().work, link: null } })
    })

    // Act
    await user.keyboard('1')
    await user.keyboard('{Enter}')

    // Assert
    expect(await workHeading('120. Landing Stripling (1962)')).toBeInTheDocument()
    expect(screen.getByText('Work 1 of 1')).toBeInTheDocument()
    expect(askedIndexes().slice(-2)).toEqual([1, 0])
  })

  it('goes to the search field with /, ahead of the header\'s search, without typing the slash', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('/')

    // Assert
    expect(titleField()).toHaveFocus()
    expect(titleField()).toHaveValue('Landing Stripling')
    expect(headerSearch()).not.toHaveFocus()
    expect(headerSearch()).toHaveValue('')
  })

  it('leaves the keys to the search field while typing in it', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    await user.clear(titleField())

    // Act
    await user.type(titleField(), 'Landing 1/0 2{ArrowRight}')

    // Assert
    expect(titleField()).toHaveValue('Landing 1/0 2')
    expect(firstPick()).not.toBeChecked()
    expect(secondPick()).not.toBeChecked()
    expect(libraryLinksApi.markNoneFits).not.toHaveBeenCalled()
    expect(askedIndexes()).toEqual([0])
  })

  it('takes the digits once a suggestion was clicked, but leaves → to its radios', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    await user.click(firstPick())

    // Act
    await user.keyboard('2')
    await user.keyboard('{ArrowRight}')

    // Assert
    expect(secondPick()).toBeChecked()
    expect(askedIndexes()).toEqual([0])
    expect(screen.getByText('Work 1 of 2')).toBeInTheDocument()
  })

  it('leaves → to the tab list, which moves to the next tab', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    screen.getByRole('tab', { name: 'Review' }).focus()

    // Act
    await user.keyboard('{ArrowRight}')

    // Assert
    expect(screen.getByRole('tab', { name: 'Confident' })).toHaveAttribute('aria-selected', 'true')
    expect(askedIndexes()).toEqual([0])
  })

  it('takes no key while a dialog is open over the page', async () => {
    // Arrange — the item holds another TMDb id, so Save asks before replacing it.
    serveQueue(dialogOf({ items: [itemOf({ libraryIdentifiers: [libraryIdentifierOf('Tmdb', '999')] })] }), junglesDialogOf())
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    await user.keyboard('1')
    await user.keyboard('{Enter}')
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })

    // Act
    await user.keyboard('2')
    await user.keyboard('0')
    await user.keyboard('{ArrowRight}')
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(firstPick()).toBeChecked()
    expect(libraryLinksApi.markNoneFits).not.toHaveBeenCalled()
    expect(libraryLinksApi.link).not.toHaveBeenCalled()
    expect(askedIndexes()).toEqual([0])
  })

  it('leaves a key with Ctrl, Alt or Cmd alone', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')

    // Act
    await user.keyboard('{Control>}1{/Control}')
    await user.keyboard('{Alt>}0{/Alt}')
    await user.keyboard('{Meta>}{ArrowRight}{/Meta}')

    // Assert
    expect(firstPick()).not.toBeChecked()
    expect(libraryLinksApi.markNoneFits).not.toHaveBeenCalled()
    expect(askedIndexes()).toEqual([0])
  })

  it('takes no key on another tab, where / goes to the header\'s search', async () => {
    // Arrange
    const { user } = renderPage()
    await workHeading('120. Landing Stripling (1962)')
    await user.click(screen.getByRole('tab', { name: 'Confident' }))
    await screen.findByText(/No work has a confident suggestion/)

    // Act
    await user.keyboard('0')
    await user.keyboard('/')

    // Assert
    expect(headerSearch()).toHaveFocus()
    expect(libraryLinksApi.markNoneFits).not.toHaveBeenCalled()
  })

  it('says when no work is left to review, and leaves / to the header\'s search', async () => {
    // Arrange
    serveQueue()
    const { user } = renderPage()
    await screen.findByText(/No work is left to review/)

    // Act
    await user.keyboard('/')

    // Assert
    expect(headerSearch()).toHaveFocus()
    expect(screen.queryByRole('button', { name: 'Skip' })).not.toBeInTheDocument()
  })

  it('shows why the queue couldn\'t load, without Try again for a refusal', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.queue).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Only the installation owner can do this.')).toBeInTheDocument()
    expect(screen.getByText('The works to review couldn\'t be loaded.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('starts the search from the title and year the server cleaned, as the dialog does', async () => {
    // Arrange
    serveQueue(junglesDialogOf())
    renderPage()

    // Act
    await workHeading('Planet_Earth_E08 - Jungles')

    // Assert
    expect(titleField()).toHaveValue('Planet Earth')
    expect(screen.getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('')
  })

  it('offers the season and the episode for a show picked for an episode, as the dialog does', async () => {
    // Arrange
    serveQueue(junglesDialogOf())
    const { user } = renderPage()
    await workHeading('Planet_Earth_E08 - Jungles')

    // Act
    await user.keyboard('1')
    const numbers = [
      screen.getByRole('textbox', { name: 'Season (optional)' }),
      screen.getByRole('textbox', { name: 'Episode (optional)' }),
    ].map((field) => (field as HTMLInputElement).value)
    await user.keyboard('{Enter}')

    // Assert
    expect(numbers).toEqual(['1', '8'])
    await waitFor(() => expect(libraryLinksApi.link).toHaveBeenCalledOnce())
    expect(libraryLinksApi.link).toHaveBeenCalledWith(junglesDialogOf().work.id, expect.objectContaining({
      catalogKind: 'Series',
      catalogId: '1044',
      seasonNumber: '1',
      episodeNumber: '8',
    }))
  })
})
