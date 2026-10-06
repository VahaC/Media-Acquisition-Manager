import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import { apiError } from '@/test/api-error'
import type { LibraryWork } from '@/lib/types'
import {
  candidateOf,
  dialogOf,
  landingStripling,
  linkedDialogOf,
  penguins,
  penguinsDialogOf,
  planetEarthShow,
  workLinkOf,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — finding the film or show the suggestions missed: a pasted TMDb or IMDb link or id, and a
// free TMDb search, each its own form whose Enter never saves, their answers picked like a suggestion.

function renderDialog(work: LibraryWork = landingStripling) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <TmdbLinkDialog work={work} onClose={onClose} />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { onClose, user: userEvent.setup() }
}

/** Waits until the dialog's content has loaded. */
const loaded = () => screen.findByRole('heading', { name: 'Suggestions' })

const pasteForm = () => screen.getByRole('search', { name: 'Paste a link' })
const searchForm = () => screen.getByRole('search', { name: 'Search TMDb' })
const pasteField = () => within(pasteForm()).getByRole('textbox', { name: 'TMDb or IMDb link' })
/** The season and the episode the Save form holds. */
const numbers = () => [
  screen.getByRole('textbox', { name: 'Season (optional)' }),
  screen.getByRole('textbox', { name: 'Episode (optional)' }),
].map((field) => (field as HTMLInputElement).value)

/** Show 1044 as a lookup answers it for the short: by an episode link with its numbers, by a bare number with none. */
const showEpisode = { ...planetEarthShow, seasonNumber: 1, episodeNumber: 8 }
const showOnly = { ...planetEarthShow, seasonNumber: null, episodeNumber: null }

describe('TmdbLinkDialog finding a film or a show', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ suggestions: [] }))
  })

  it('looks a pasted IMDb link up on Enter without saving, and offers what it names to pick', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.lookup).mockResolvedValue({ items: [candidateOf()] })
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.type(within(pasteForm()).getByRole('textbox', { name: 'TMDb or IMDb link' }), 'https://www.imdb.com/title/tt0056164/{Enter}')

    // Assert
    expect(libraryLinksApi.lookup).toHaveBeenCalledWith(landingStripling.id, { text: 'https://www.imdb.com/title/tt0056164/', kind: undefined })
    expect(libraryLinksApi.link).not.toHaveBeenCalled()
    const found = await within(pasteForm()).findByRole('radiogroup', { name: 'What the link names' })
    expect(within(found).getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' })).not.toBeChecked()
    expect(screen.queryByRole('radiogroup', { name: 'A TMDb number of a' })).not.toBeInTheDocument()
  })

  it('asks whether a bare number is a film\'s or a show\'s, and sends the choice', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.lookup).mockResolvedValue({ items: [planetEarthShow] })
    const { user } = renderDialog()
    await loaded()
    await user.type(within(pasteForm()).getByRole('textbox', { name: 'TMDb or IMDb link' }), '1044')

    // Act
    const kind = within(pasteForm()).getByRole('radiogroup', { name: 'A TMDb number of a' })
    await user.click(within(kind).getByRole('radio', { name: 'Show' }))
    await user.click(within(pasteForm()).getByRole('button', { name: 'Look up' }))

    // Assert
    expect(libraryLinksApi.lookup).toHaveBeenCalledWith(landingStripling.id, { text: '1044', kind: 'Series' })
    expect(await within(pasteForm()).findByRole('radio', { name: 'Pick Планета Земля (2006)' })).toBeInTheDocument()
  })

  it('shows the API\'s sentence under the field when the text names nothing', async () => {
    // Arrange
    const sentence = 'Paste a TMDb or IMDb link, an IMDb id like tt0056164, or a TMDb number.'
    vi.mocked(libraryLinksApi.lookup).mockRejectedValue(apiError(400, sentence))
    const { user } = renderDialog()
    await loaded()
    const field = within(pasteForm()).getByRole('textbox', { name: 'TMDb or IMDb link' })

    // Act
    await user.type(field, 'Landing Stripling{Enter}')

    // Assert
    expect(await within(pasteForm()).findByRole('alert')).toHaveTextContent(sentence)
    expect(field).toHaveAttribute('aria-invalid', 'true')
  })

  it('looks the same text up again after a failure', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.lookup)
      .mockRejectedValueOnce(apiError(502, 'TMDb couldn\'t be reached.'))
      .mockResolvedValue({ items: [candidateOf()] })
    const { user } = renderDialog()
    await loaded()
    await user.type(within(pasteForm()).getByRole('textbox', { name: 'TMDb or IMDb link' }), 'tt0056164{Enter}')
    await within(pasteForm()).findByRole('alert')

    // Act
    await user.click(within(pasteForm()).getByRole('button', { name: 'Look up' }))

    // Assert
    expect(await within(pasteForm()).findByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' })).toBeInTheDocument()
    expect(libraryLinksApi.lookup).toHaveBeenCalledTimes(2)
  })

  it('searches TMDb from the title and year the server cleaned, and saves a result picked from it', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.search).mockResolvedValue({ items: [candidateOf()] })
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { onClose, user } = renderDialog()
    await loaded()
    expect(within(searchForm()).getByRole('searchbox', { name: 'Title' })).toHaveValue('Landing Stripling')
    expect(within(searchForm()).getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('1962')

    // Act
    await user.type(within(searchForm()).getByRole('searchbox', { name: 'Title' }), '{Enter}')
    await user.click(await within(searchForm()).findByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    // The pick is named in the Save form, by the items and numbers it goes with.
    expect(screen.getByText('Your pick: Посадка Стріплінга · TMDb 223021').closest('form')).toHaveAttribute('id', 'tmdb-link-pick')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    expect(libraryLinksApi.search).toHaveBeenCalledWith(landingStripling.id, { title: 'Landing Stripling', year: '1962' })
    expect(libraryLinksApi.link).toHaveBeenCalledWith(landingStripling.id, expect.objectContaining({ catalogKind: 'Movie', catalogId: '223021' }))
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('starts the search from the server\'s cleaned title and the year its brackets name, not the library\'s raw ones', async () => {
    // Arrange
    // As Jellyfin names the series after its folder, with the span's last year.
    const named = { ...penguins, year: 2015 }
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf({ work: named }))
    renderDialog(named)

    // Act
    await loaded()

    // Assert
    expect(within(searchForm()).getByRole('searchbox', { name: 'Title' })).toHaveValue('The Penguins Of Madagascar')
    expect(within(searchForm()).getByRole('textbox', { name: 'Year (optional)' })).toHaveValue('2008')
  })

  it('takes the season and the episode from each answer picked, not from the one picked before it', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.lookup).mockImplementation(async (_, query) => ({ items: [query.kind === 'Series' ? showOnly : showEpisode] }))
    const { user } = renderDialog()
    await loaded()
    await user.type(pasteField(), 'https://www.themoviedb.org/tv/1044/season/1/episode/8{Enter}')
    await user.click(await within(pasteForm()).findByRole('radio', { name: 'Pick Планета Земля (2006)' }))
    const episodeLink = numbers()
    await user.clear(pasteField())
    await user.type(pasteField(), '1044')
    await user.click(within(pasteForm()).getByRole('radio', { name: 'Show' }))
    await user.click(within(pasteForm()).getByRole('button', { name: 'Look up' }))
    await vi.waitFor(() => expect(libraryLinksApi.lookup).toHaveBeenLastCalledWith(landingStripling.id, { text: '1044', kind: 'Series' }))

    // Act
    const bareNumber = await within(pasteForm()).findByRole('radio', { name: 'Pick Планета Земля (2006)' })
    const checkedBefore = (bareNumber as HTMLInputElement).checked
    await user.click(bareNumber)

    // Assert
    expect(episodeLink).toEqual(['1', '8'])
    // An answer of its own, not the episode link's pick shown checked again.
    expect(checkedBefore).toBe(false)
    expect(bareNumber).toBeChecked()
    expect(numbers()).toEqual(['', ''])
  })

  it('takes a pasted episode link\'s numbers for the show the work is linked to as well', async () => {
    // Arrange
    const linked = { ...landingStripling, link: workLinkOf({ target: 'ShowEpisode', catalogKind: 'Series', catalogId: '1044', seasonNumber: 2, episodeNumber: 3 }) }
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ work: linked, suggestions: [] }))
    vi.mocked(libraryLinksApi.lookup).mockResolvedValue({ items: [showEpisode] })
    const { user } = renderDialog(linked)
    await loaded()

    // Act
    await user.type(pasteField(), 'https://www.themoviedb.org/tv/1044/season/1/episode/8{Enter}')
    await user.click(await within(pasteForm()).findByRole('radio', { name: 'Pick Планета Земля (2006)' }))

    // Assert
    expect(numbers()).toEqual(['1', '8'])
  })

  it('says when TMDb found nothing', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.search).mockResolvedValue({ items: [] })
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(within(searchForm()).getByRole('button', { name: 'Search' }))

    // Assert
    expect(await within(searchForm()).findByText('TMDb found nothing for it.')).toBeInTheDocument()
    expect(libraryLinksApi.search).toHaveBeenCalledWith(landingStripling.id, { title: 'Landing Stripling', year: '1962' })
  })

  it('shows why a search was refused under the search', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.search).mockRejectedValue(apiError(400, 'A year is four digits, like 1962.'))
    const { user } = renderDialog()
    await loaded()
    const year = within(searchForm()).getByRole('textbox', { name: 'Year (optional)' })
    await user.clear(year)

    // Act
    await user.type(year, '62{Enter}')

    // Assert
    expect(await within(searchForm()).findByRole('alert')).toHaveTextContent('A year is four digits, like 1962.')
    expect(libraryLinksApi.search).toHaveBeenCalledWith(landingStripling.id, { title: 'Landing Stripling', year: '62' })
    expect(libraryLinksApi.link).not.toHaveBeenCalled()
  })
})
