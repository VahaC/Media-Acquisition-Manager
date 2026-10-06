import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDateTime } from '@/lib/format'
import { libraryLinksApi } from '@/lib/library-links-api'
import {
  libraryIdentifierOf,
  linkedPenguinsDialogOf,
  linkedPenguinsItemOf,
  penguins,
  penguinsDialogOf,
  penguinsItemOf,
  penguinsLinkId,
  switchedOffReason,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb dialog of a series. Since Vasyl's decision of 2026-10-06 a series' TMDb id is
// written into Jellyfin like a film's, with no question of its own, and Jellyfin is then asked to look
// up its missing metadata. The dialog shows the state and when Jellyfin was asked; a link the server
// keeps in the app (a library switched off) still says why in the server's own sentence, and one kept
// there before the app wrote series offers Write to Jellyfin.

const dialogName = `Link “${penguins.title}” to TMDb`

function renderDialog() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <TmdbLinkDialog work={penguins} onClose={onClose} />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { onClose, user: userEvent.setup() }
}

/** The dialog once its content has loaded. */
async function loaded() {
  const dialog = await screen.findByRole('dialog', { name: dialogName })
  await within(dialog).findByRole('heading', { name: 'Suggestions' })
  return dialog
}

const pickShow = () => screen.getByRole('radio', { name: 'Pick Пінгвіни з Мадагаскару (2008)' })
const saveButton = () => screen.getByRole('button', { name: 'Save' })

/** The sentence the server kept on a series' link under the first rules, which kept it in the app. */
const firstRulesError = 'Jellyfin rewrites every season and episode of a series whenever it saves the series, so a series link stays in the app.'

/** When the fixture's series was looked up, as the dialog shows it. */
const lookedUp = formatDateTime('2026-10-06T09:00:05Z', { dateStyle: 'medium', timeStyle: 'short' })

describe('TmdbLinkDialog for a series', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf())
  })

  it('says nothing of keeping a series\' link in the app before it is linked, nor of its seasons and episodes', async () => {
    // Arrange
    renderDialog()

    // Act
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('No link.')).toBeInTheDocument()
    expect(item).toHaveTextContent(`Home Jellyfin · ${penguins.title}Jellyfin reports no ids for it.No link.`)
    expect(dialog).toHaveAccessibleDescription(
      'Pick the TMDb film or show this work is. Its ids are written into its items in Jellyfin: a film\'s, a series\', or an episode\'s own IMDb id (by its season and episode) for an episode Jellyfin lists as a film. Jellyfin then fills in only what they are missing, as its Search for missing metadata does: their titles stay. An item says why when its link stays in the app.',
    )
    expect(dialog).not.toHaveTextContent(/rating/)
  })

  it('links the picked show to the series with no season or episode on Save, with no question, and closes once it is written', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedPenguinsDialogOf())
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(pickShow())
    const numbers = screen.queryByRole('textbox', { name: 'Season (optional)' })
    await user.click(saveButton())

    // Assert
    expect(numbers).not.toBeInTheDocument()
    expect(libraryLinksApi.link).toHaveBeenCalledExactlyOnceWith(penguins.id, {
      catalogKind: 'Series',
      catalogId: '7869',
      seasonNumber: null,
      episodeNumber: null,
      sourceItemIds: null,
    })
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Saved, but Jellyfin doesn\'t hold the id yet: the items above say why.')).not.toBeInTheDocument()
  })

  it('shows a linked series as written, with when Jellyfin was asked to look up its metadata, and nothing to retry', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf())

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    expect(within(dialog).getByText('Пінгвіни з Мадагаскару (Show, TMDb 7869)')).toBeInTheDocument()
    expect(within(dialog).getByText('Written to Jellyfin')).toHaveClass('ui-badge', 'ui-badge-outline')
    expect(within(dialog).getByText(`Jellyfin was asked to look up its missing metadata on ${lookedUp}.`)).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Write again' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Remove link' })).toBeEnabled()
  })

  it('asks before replacing the TMDb id Jellyfin holds for the series, as for a film, and links once that is answered', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf({
      items: [penguinsItemOf({ libraryIdentifiers: [libraryIdentifierOf('TmdbTv', '999')] })],
    }))
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedPenguinsDialogOf())
    const { user } = renderDialog()
    await loaded()
    await user.click(pickShow())

    // Act
    await user.click(saveButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })
    const message = confirmation.textContent
    const asked = vi.mocked(libraryLinksApi.link).mock.calls.length
    const replace = within(confirmation).getByRole('button', { name: 'Replace' })
    await user.click(replace)

    // Assert
    expect(message).toContain('Jellyfin holds TMDb 999. Replace it with 7869?')
    expect(message).not.toMatch(/rating|season/)
    expect(replace).not.toHaveClass('ui-button-destructive')
    expect(asked).toBe(0)
    await vi.waitFor(() => expect(libraryLinksApi.link).toHaveBeenCalledExactlyOnceWith(penguins.id, expect.objectContaining({ catalogKind: 'Series', catalogId: '7869' })))
  })

  it('shows the server\'s reason under a series it won\'t write, and gives that reason before linking over Jellyfin\'s id', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf({
      items: [penguinsItemOf({ canWrite: false, cannotWriteReason: switchedOffReason, libraryIdentifiers: [libraryIdentifierOf('TmdbTv', '999')] })],
    }))
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedPenguinsDialogOf())
    const { user } = renderDialog()
    const dialog = await loaded()
    await user.click(pickShow())

    // Act
    await user.click(saveButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })

    // Assert
    expect(within(dialog).getByText(switchedOffReason)).toHaveClass('settings-muted')
    expect(confirmation).toHaveTextContent(`Jellyfin holds TMDb 999. ${switchedOffReason} Link 7869 instead?`)
    expect(confirmation).not.toHaveTextContent('Replace it with')
  })

  it('offers Write to Jellyfin for a series link kept in the app before the app wrote series', async () => {
    // Arrange
    // As the server sends one set under the first rules: kept, with no reason and no error.
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'KeptInApp', lookupRequestedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('Kept in the app')).toBeInTheDocument()
    expect(within(item).getByText('Set while the app kept such links to itself: it can be written into Jellyfin now.')).toHaveClass('settings-muted')
    expect(within(item).getByRole('button', { name: 'Write to Jellyfin' })).toBeEnabled()
    expect(item).not.toHaveTextContent(/look up its missing metadata/)
    expect(item).not.toHaveTextContent(/rating/)
    expect(item.querySelector('.library-source-warning')).toBeNull()
  })

  it('writes a series link kept in the app on Write to Jellyfin, with no question', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedPenguinsDialogOf({ items: [linkedPenguinsItemOf({ writeState: 'KeptInApp', lookupRequestedUtc: null })] }))
      .mockResolvedValue(linkedPenguinsDialogOf())
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Write to Jellyfin' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(penguinsLinkId)
    expect(await screen.findByText(`Jellyfin was asked to look up its missing metadata on ${lookedUp}.`)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Write to Jellyfin' })).not.toBeInTheDocument()
  })

  it('offers Write to Jellyfin for a series link kept in the app that the server can write, whatever error it still holds', async () => {
    // Arrange
    // Kept under the first rules with their sentence as its error; the server writes it now.
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'KeptInApp', writeError: firstRulesError, lookupRequestedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('Kept in the app')).toBeInTheDocument()
    expect(within(item).queryByText(firstRulesError)).not.toBeInTheDocument()
    expect(within(item).getByText('Set while the app kept such links to itself: it can be written into Jellyfin now.')).toBeInTheDocument()
    expect(within(item).getByRole('button', { name: 'Write to Jellyfin' })).toBeEnabled()
  })

  it('gives a kept link\'s own error as its reason when the server can\'t write the item and says no more, with nothing to click', async () => {
    // Arrange
    const sentence = 'The library can\'t take this link.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'KeptInApp', canWrite: false, cannotWriteReason: null, writeError: sentence, lookupRequestedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText(sentence)).toHaveClass('settings-muted')
    expect(item).not.toHaveTextContent(/can be written into Jellyfin now/)
    expect(within(item).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each([
    ['Retry', linkedPenguinsItemOf({ writeState: 'NotWritten', writeError: 'Jellyfin didn\'t answer in time.', lookupRequestedUtc: null })],
    ['Write again', linkedPenguinsItemOf({ writeState: 'Lost', lookupRequestedUtc: null })],
  ])('writes the series again on %s, with no question', async (action, item) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({ items: [item] }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    const dialog = await loaded()
    expect(dialog).not.toHaveTextContent(/rating/)

    // Act
    await user.click(screen.getByRole('button', { name: action }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(penguinsLinkId)
  })

  it('retries a written series\' failed lookup, which writes its ids again and asks Jellyfin again', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ lookupError: 'Jellyfin didn\'t answer in time.' })],
    }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(penguinsLinkId)
  })
})
