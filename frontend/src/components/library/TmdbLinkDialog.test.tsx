import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import { qk } from '@/lib/queries'
import type { LibraryWork } from '@/lib/types'
import { apiError } from '@/test/api-error'
import {
  dialogOf,
  homeJellyfinItemId,
  itemOf,
  landingStripling,
  libraryIdentifierOf,
  linkedDialogOf,
  linkedItemOf,
  linkId,
  planetEarthJungles,
  planetEarthShow,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb dialog of a library work: what it is linked to and where each item stands, the
// suggestions to pick from, Save with its confirmation, Remove link, and Retry for one item.

const dialogName = 'Link “120. Landing Stripling” to TMDb'

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
  return { client, onClose, user: userEvent.setup() }
}

/** The dialog once its content has loaded. */
async function loaded(name = dialogName) {
  const dialog = await screen.findByRole('dialog', { name })
  await within(dialog).findByRole('heading', { name: 'Suggestions' })
  return dialog
}

const saveButton = () => screen.getByRole('button', { name: 'Save' })

describe('TmdbLinkDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf())
  })

  it('shows the work\'s item and the suggestions with their evidence, Save then Cancel, and TMDb\'s notice', async () => {
    // Arrange
    const work = landingStripling

    // Act
    renderDialog(work)
    const dialog = await loaded()

    // Assert
    expect(libraryLinksApi.dialog).toHaveBeenCalledWith(work.id)
    expect(within(dialog).getByText('Home Jellyfin · 120. Landing Stripling')).toBeInTheDocument()
    expect(within(dialog).getByText('Not linked to TMDb yet.')).toBeInTheDocument()
    const suggestions = within(dialog).getByRole('radiogroup', { name: 'Suggestions' })
    expect(within(suggestions).getAllByRole('radio').map((radio) => radio.getAttribute('aria-label')))
      .toEqual(['Pick Посадка Стріплінга (1962)', 'Pick Calypso Cat (1962)'])
    expect(within(suggestions).getByText('Confident')).toBeInTheDocument()
    expect(within(suggestions).getAllByText('6 min ≈ 6 min')).toHaveLength(2)
    expect(within(dialog).getByText(/^Film and show data from TMDB/)).toBeInTheDocument()
    const footer = dialog.querySelector('.ui-dialog-footer')!
    expect(within(footer as HTMLElement).getAllByRole('button').map((button) => button.textContent)).toEqual(['Save', 'Cancel'])
  })

  it('keeps Save disabled with the reason until a film or a show is picked', async () => {
    // Arrange
    renderDialog()
    await loaded()

    // Act
    const save = saveButton()

    // Assert
    expect(save).toBeDisabled()
    expect(save.parentElement).toHaveAttribute('title', 'Pick a film or a show first.')
  })

  it('links the picked film to every item and closes when Jellyfin took it', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())

    // Assert
    expect(libraryLinksApi.link).toHaveBeenCalledWith(landingStripling.id, {
      catalogKind: 'Movie',
      catalogId: '223021',
      seasonNumber: null,
      episodeNumber: null,
      sourceItemIds: null,
    })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('puts the saved dialog in the cache and drops the works and the other link queries', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { client, user } = renderDialog()
    await loaded()
    client.setQueryData(qk.library.works({ page: 1, pageSize: 25 }), { items: [], totalCount: 0, page: 1, pageSize: 25 })
    client.setQueryData(qk.library.links.queue(0), { totalCount: 1, index: 0, current: null })

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())

    // Assert
    expect(client.getQueryData(qk.library.links.work(landingStripling.id))).toEqual(linkedDialogOf())
    expect(client.getQueryState(qk.library.works({ page: 1, pageSize: 25 }))?.isInvalidated).toBe(true)
    expect(client.getQueryState(qk.library.links.queue(0))?.isInvalidated).toBe(true)
    expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(1)
  })

  it('stays open on the item when Jellyfin didn\'t take the write, with its sentence and Retry', async () => {
    // Arrange
    const writeError = 'Jellyfin took the change but still reports other ids; Retry.'
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ writeState: 'NotWritten', writeError })] }))
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())

    // Assert
    expect(onClose).not.toHaveBeenCalled()
    expect(await screen.findByText('Saved, but Jellyfin doesn\'t hold the id yet: the items above say why.')).toBeInTheDocument()
    expect(screen.getByText(writeError)).toBeInTheDocument()
    expect(screen.getByText('Not in Jellyfin yet')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeEnabled()
  })

  it('takes the not-written warning away once a Retry gets the id into Jellyfin', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValueOnce(dialogOf()).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({
      items: [linkedItemOf({ writeState: 'NotWritten', writeError: 'Jellyfin refused to change the item\'s ids.' })],
    }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())
    const warning = await screen.findByText('Saved, but Jellyfin doesn\'t hold the id yet: the items above say why.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await screen.findByText('Written to Jellyfin')

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(linkId)
    expect(warning).not.toBeInTheDocument()
  })

  it('keeps the not-written warning when Jellyfin refuses the Retry too', async () => {
    // Arrange
    const notWritten = linkedDialogOf({
      items: [linkedItemOf({ writeState: 'NotWritten', writeError: 'Jellyfin refused to change the item\'s ids.' })],
    })
    vi.mocked(libraryLinksApi.dialog).mockResolvedValueOnce(dialogOf()).mockResolvedValue(notWritten)
    vi.mocked(libraryLinksApi.link).mockResolvedValue(notWritten)
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())
    await screen.findByText('Saved, but Jellyfin doesn\'t hold the id yet: the items above say why.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(2))

    // Assert
    expect(await screen.findByText('Saved, but Jellyfin doesn\'t hold the id yet: the items above say why.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeEnabled()
  })

  it('shows a refused save above the buttons and keeps the pick', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockRejectedValue(apiError(409, 'A write to the library is under way for this work; try again in a moment.'))
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('A write to the library is under way for this work; try again in a moment.')
    expect(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' })).toBeChecked()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('asks before replacing a TMDb id Jellyfin holds, and sends nothing when told no', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ items: [itemOf({ libraryIdentifiers: [libraryIdentifierOf('Tmdb', '999')] })] }))
    const { user } = renderDialog()
    await loaded()
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))

    // Act
    await user.click(saveButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })
    expect(confirmation).toHaveTextContent('Jellyfin holds TMDb 999. Replace it with 223021?')
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(libraryLinksApi.link).not.toHaveBeenCalled()
  })

  it('replaces the work\'s link once the owner confirms', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { user } = renderDialog()
    await loaded()
    await user.click(screen.getByRole('radio', { name: 'Pick Calypso Cat (1962)' }))

    // Act
    await user.click(saveButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })
    expect(confirmation).toHaveTextContent('This work is linked to TMDb 223021. Replace it with 223022?')
    await user.click(within(confirmation).getByRole('button', { name: 'Replace' }))

    // Assert
    await vi.waitFor(() => expect(libraryLinksApi.link).toHaveBeenCalledWith(landingStripling.id, expect.objectContaining({ catalogId: '223022' })))
  })

  it('asks for a season and an episode, prefilled, when a show is picked for a film, and sends them as text', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ work: planetEarthJungles, suggestions: [planetEarthShow] }))
    vi.mocked(libraryLinksApi.link).mockResolvedValue(dialogOf({ work: planetEarthJungles }))
    const { user } = renderDialog(planetEarthJungles)
    await loaded('Link “Planet_Earth_E08 - Jungles” to TMDb')

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Планета Земля (2006)' }))
    const season = screen.getByRole('textbox', { name: 'Season (optional)' })
    const episode = screen.getByRole('textbox', { name: 'Episode (optional)' })
    expect(season).toHaveValue('1')
    expect(episode).toHaveValue('8')
    await user.clear(episode)
    await user.type(episode, '9')
    await user.click(saveButton())

    // Assert
    expect(screen.queryByText(/With a season and an episode, the episode's IMDb id is written into Jellyfin when TMDb knows it/)).not.toBeNull()
    expect(libraryLinksApi.link).toHaveBeenCalledWith(planetEarthJungles.id, {
      catalogKind: 'Series',
      catalogId: '1044',
      seasonNumber: '1',
      episodeNumber: '9',
      sourceItemIds: null,
    })
  })

  it('says a show picked for an episode of no series links it in the app, as its item does, and promises no write', async () => {
    // Arrange
    // The jungles file as Jellyfin would list it under no series: an episode item, which it never writes.
    const reason = 'Jellyfin\'s ids are written on movie and series items only, so a link on an episode stays in the app.'
    const episode: LibraryWork = { ...planetEarthJungles, kind: 'Episode' }
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({
      work: episode,
      suggestions: [planetEarthShow],
      items: [itemOf({
        sourceItemId: planetEarthJungles.versions[0].sourceItemId!,
        itemTitle: planetEarthJungles.title,
        itemKind: 'Episode',
        canWrite: false,
        cannotWriteReason: reason,
      })],
    }))
    const { user } = renderDialog(episode)
    const dialog = await loaded('Link “Planet_Earth_E08 - Jungles” to TMDb')

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Планета Земля (2006)' }))

    // Assert
    expect(screen.getByRole('textbox', { name: 'Season (optional)' })).toHaveValue('1')
    expect(within(dialog).getByText('The work links as an episode of this show. The link is kept in the app: its item says why.')).toHaveClass('ui-hint')
    expect(within(dialog).getByText(reason)).toBeInTheDocument()
    expect(dialog).not.toHaveTextContent(/written into Jellyfin when TMDb knows it/)
  })

  it('offers the items as checkboxes when the work holds several, and links only the ticked ones', async () => {
    // Arrange
    const second = itemOf({ sourceItemId: 'second-item', itemTitle: 'Landing Stripling (copy)' })
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ items: [itemOf(), second] }))
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { user } = renderDialog()
    await loaded()
    const items = screen.getByRole('group', { name: 'Link these items' })
    expect(within(items).getAllByRole('checkbox').map((box) => (box as HTMLInputElement).checked)).toEqual([true, true])

    // Act
    await user.click(within(items).getByRole('checkbox', { name: 'Home Jellyfin · Landing Stripling (copy)' }))
    await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
    await user.click(saveButton())

    // Assert
    expect(libraryLinksApi.link).toHaveBeenCalledWith(landingStripling.id, expect.objectContaining({ sourceItemIds: [homeJellyfinItemId] }))
  })

  it('removes the link after a destructive confirmation', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.remove).mockResolvedValue({ dialog: dialogOf() })
    const { client, user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Remove link' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove the TMDb link?' })
    await user.click(within(confirmation).getByRole('button', { name: 'Remove link' }))

    // Assert
    expect(libraryLinksApi.remove).toHaveBeenCalledWith(landingStripling.id)
    expect(await screen.findByText('Not linked to TMDb yet.')).toBeInTheDocument()
    expect(client.getQueryData(qk.library.links.work(landingStripling.id))).toEqual(dialogOf())
  })

  it('keeps the link and says why when Jellyfin couldn\'t be changed back', async () => {
    // Arrange
    const sentence = 'Jellyfin couldn\'t be changed back, so the link stays: Jellyfin didn\'t answer.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.remove).mockRejectedValue(apiError(502, sentence))
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Remove link' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Remove the TMDb link?' })).getByRole('button', { name: 'Remove link' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(screen.getByText('Посадка Стріплінга (Film, TMDb 223021)')).toBeInTheDocument()
  })

  it('writes one item\'s lost link again and asks for the dialog anew', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedDialogOf({ items: [linkedItemOf({ writeState: 'Lost' })] }))
      .mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Write again' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledWith(linkId)
    expect(await screen.findByText('Written to Jellyfin')).toBeInTheDocument()
    expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(2)
  })

  it('shows a refused Retry under its item', async () => {
    // Arrange
    const sentence = '“Home Jellyfin” is switched off. Switch it on under Integrations, then retry.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ writeState: 'NotWritten', writeError: sentence })] }))
    vi.mocked(libraryLinksApi.writeLink).mockRejectedValue(apiError(409, sentence))
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
  })

  it('says a catalog must be connected first, with Save disabled for that reason', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ catalogConnected: false, suggestions: [] }))

    // Act
    renderDialog()
    await loaded()

    // Assert
    expect(screen.getAllByText('Connect a metadata catalog under Integrations first.').length).toBeGreaterThan(0)
    expect(saveButton().parentElement).toHaveAttribute('title', 'Connect a metadata catalog under Integrations first.')
  })

  it.each([
    [{ searchedUtc: null, noneFitsUtc: null, pending: true, inPopulation: true }, 'The suggestions are being prepared…'],
    [{ searchedUtc: null, noneFitsUtc: null, pending: false, inPopulation: true }, 'No suggestions yet. Paste a link or search TMDb below.'],
    [{ searchedUtc: '2026-10-05T08:01:00Z', noneFitsUtc: null, pending: false, inPopulation: true }, 'TMDb found nothing for this work\'s names. Paste a link or search TMDb below.'],
    [{ searchedUtc: null, noneFitsUtc: null, pending: false, inPopulation: false }, 'Suggestions are prepared for works without a TMDb id. Paste a link or search TMDb below.'],
  ])('says why there are no suggestions (%o)', async (search, sentence) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ suggestions: [], search }))

    // Act
    renderDialog()
    await loaded()

    // Assert
    expect(screen.getByText(sentence)).toBeInTheDocument()
  })

  it('shows why the dialog couldn\'t load, without Try again for a refusal', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderDialog()

    // Assert
    expect(await screen.findByText('Only the installation owner can do this.')).toBeInTheDocument()
    expect(screen.getByText('The work\'s TMDb link couldn\'t be loaded.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(saveButton()).toBeDisabled()
  })

  it('closes on Cancel without asking for anything more', async () => {
    // Arrange
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(libraryLinksApi.link).not.toHaveBeenCalled()
  })
})
