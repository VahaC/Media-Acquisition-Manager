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

// MAM-34 — the TMDb dialog of a series. Since Vasyl's decision of 2026-10-06 saving a series' link makes
// Jellyfin identify it like a film, in the background (the Penguins of Madagascar took 159 seconds on
// the live check), refreshing its seasons and episodes too. The dialog says that before Save, never
// waits for it, and shows the series' state and when Jellyfin confirmed it; a link the server keeps in
// the app (a library switched off) still says why in the server's own sentence, and one kept there
// before the app identified series offers Identify in Jellyfin.

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

/** Presses `label` in the question every Identify, Retry and Identify again asks first. */
async function answerIdentifyQuestion(user: ReturnType<typeof userEvent.setup>, label: string) {
  const question = await screen.findByRole('dialog', { name: 'Identify in Jellyfin?' })
  await user.click(within(question).getByRole('button', { name: label }))
}

/** The sentence the server kept on a series' link under the first rules, which kept it in the app. */
const firstRulesError = 'Jellyfin rewrites every season and episode of a series whenever it saves the series, so a series link stays in the app.'

const willIdentifyShow = 'Saving makes Jellyfin identify the ticked series as this show: it replaces its title, overview and images with TMDb\'s, in its library\'s language, and refreshes its episodes too. There is no undo; your files and folders are not touched.'
const seriesUnderWay = 'Jellyfin identifies the series and refreshes its seasons and episodes in the background; this takes minutes. You can close this window: the result shows here and on the Library card.'

/** When the fixture's series was identified, as the dialog shows it. */
const identifiedOn = formatDateTime('2026-10-06T09:00:05Z', { dateStyle: 'medium', timeStyle: 'short' })

/** A series' item as the server sends one kept in the app under the first rules: nothing keeps it there now. */
const keptItem = () => linkedPenguinsItemOf({ writeState: 'KeptInApp', identifiedUtc: null })

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
    expect(dialog).toHaveAccessibleDescription('Pick the TMDb film or show this work is. An item says why when its link stays in the app.')
    expect(dialog).not.toHaveTextContent(/rating|Search for missing metadata|titles stay/)
  })

  it('links the picked show to the series on Save with no question of its own, says it takes minutes, and stays open on the queued series', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'Queued', identifiedUtc: null })],
    }))
    const { onClose, user } = renderDialog()
    const dialog = await loaded()

    // Act
    await user.click(pickShow())
    const numbers = screen.queryByRole('textbox', { name: 'Season (optional)' })
    const statementShown = within(dialog).queryByText(willIdentifyShow) !== null
    await user.click(saveButton())

    // Assert
    expect(numbers).not.toBeInTheDocument()
    expect(statementShown).toBe(true)
    expect(libraryLinksApi.link).toHaveBeenCalledExactlyOnceWith(penguins.id, {
      catalogKind: 'Series',
      catalogId: '7869',
      seasonNumber: null,
      episodeNumber: null,
      sourceItemIds: null,
    })
    expect(await within(dialog).findByText('Waiting to identify')).toBeInTheDocument()
    expect(within(dialog).getByText(seriesUnderWay)).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('shows a running series with since when, and a linked series as identified, with when', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'Writing', writeStateUtc: '2026-10-06T09:00:04Z', identifiedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('Identifying…', { selector: '.ui-badge' })).toBeInTheDocument()
    expect(item).toHaveTextContent(`${seriesUnderWay} Started ${formatDateTime('2026-10-06T09:00:04Z', { dateStyle: 'medium', timeStyle: 'short' })}.`)
    expect(within(item).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows a linked series as identified, with when, and nothing to retry', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf())

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    expect(within(dialog).getByText('Пінгвіни з Мадагаскару (Show, TMDb 7869)')).toBeInTheDocument()
    expect(within(dialog).getByText('Identified in Jellyfin')).toHaveClass('ui-badge', 'ui-badge-outline')
    expect(within(dialog).getByText(`Confirmed in Jellyfin on ${identifiedOn}.`)).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Identify again' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Identify in Jellyfin' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Remove link' })).toBeEnabled()
  })

  it('asks before replacing the TMDb id Jellyfin holds for the series, as for a film, in a destructive confirmation, and links once that is answered', async () => {
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
    const destructive = replace.classList.contains('ui-button-destructive')
    await user.click(replace)

    // Assert
    expect(message).toContain('Jellyfin holds TMDb 999. Replace it with 7869? Jellyfin identifies the item as 7869: its title, overview and images are replaced too, and there is no undo.')
    expect(message).not.toMatch(/rating/)
    expect(destructive).toBe(true)
    expect(asked).toBe(0)
    await vi.waitFor(() => expect(libraryLinksApi.link).toHaveBeenCalledExactlyOnceWith(penguins.id, expect.objectContaining({ catalogKind: 'Series', catalogId: '7869' })))
  })

  it('shows the server\'s reason under a series it won\'t identify, and gives that reason before linking over Jellyfin\'s id', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf({
      items: [penguinsItemOf({ canWrite: false, cannotWriteReason: switchedOffReason, libraryIdentifiers: [libraryIdentifierOf('TmdbTv', '999')] })],
    }))
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedPenguinsDialogOf())
    const { user } = renderDialog()
    const dialog = await loaded()

    // Act
    await user.click(pickShow())
    // The server won't identify the item, so the statement promises nothing.
    const statementShown = within(dialog).queryByText(/Saving makes Jellyfin/) !== null
    await user.click(saveButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })

    // Assert
    expect(statementShown).toBe(false)
    expect(within(dialog).getByText(switchedOffReason)).toHaveClass('settings-muted')
    expect(confirmation).toHaveTextContent(`Jellyfin holds TMDb 999. ${switchedOffReason} Link 7869 instead?`)
    expect(confirmation).not.toHaveTextContent('Replace it with')
    // Nothing reaches Jellyfin, so nothing irreversible is confirmed.
    expect(within(confirmation).getByRole('button', { name: 'Replace' })).not.toHaveClass('ui-button-destructive')
  })

  it('offers Identify in Jellyfin for a series link kept in the app before the app identified series', async () => {
    // Arrange
    // As the server sends one set under the first rules: kept, with no reason and no error.
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({ items: [keptItem()] }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('Kept in the app')).toBeInTheDocument()
    expect(within(item).getByText('Set while the app kept such links to itself: Jellyfin can identify it now.')).toHaveClass('settings-muted')
    expect(within(item).getByRole('button', { name: 'Identify in Jellyfin' })).toBeEnabled()
    expect(item).not.toHaveTextContent(/Confirmed in Jellyfin/)
    expect(item.querySelector('.library-source-warning')).toBeNull()
  })

  it('offers Identify in Jellyfin for a series link set under the first rules whose ids are in Jellyfin but was never identified', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ identifiedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('Not identified yet')).toHaveClass('library-badge-warning')
    expect(within(item).getByText('The ids are in Jellyfin, but the app hasn\'t identified the item: Jellyfin still shows the title, overview and images it had. Identify it to replace them with TMDb\'s.')).toHaveClass('settings-muted')
    expect(within(item).getByRole('button', { name: 'Identify in Jellyfin' })).toBeEnabled()
    expect(item).not.toHaveTextContent(/Confirmed in Jellyfin/)
  })

  it('identifies a series link kept in the app on Identify in Jellyfin, after a question', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedPenguinsDialogOf({ items: [keptItem()] }))
      .mockResolvedValue(linkedPenguinsDialogOf())
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Identify in Jellyfin' }))
    await answerIdentifyQuestion(user, 'Identify in Jellyfin')

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(penguinsLinkId)
    expect(await screen.findByText(`Confirmed in Jellyfin on ${identifiedOn}.`)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Identify in Jellyfin' })).not.toBeInTheDocument()
  })

  it('offers Identify in Jellyfin for a series link kept in the app that the server can identify, whatever error it still holds', async () => {
    // Arrange
    // Kept under the first rules with their sentence as its error; the server identifies it now.
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'KeptInApp', writeError: firstRulesError, identifiedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText('Kept in the app')).toBeInTheDocument()
    expect(within(item).queryByText(firstRulesError)).not.toBeInTheDocument()
    expect(within(item).getByText('Set while the app kept such links to itself: Jellyfin can identify it now.')).toBeInTheDocument()
    expect(within(item).getByRole('button', { name: 'Identify in Jellyfin' })).toBeEnabled()
  })

  it('gives a kept link\'s own error as its reason when the server can\'t identify the item and says no more, with nothing to click', async () => {
    // Arrange
    const sentence = 'The library can\'t take this link.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'KeptInApp', canWrite: false, cannotWriteReason: null, writeError: sentence, identifiedUtc: null })],
    }))

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    const item = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(item).getByText(sentence)).toHaveClass('settings-muted')
    expect(item).not.toHaveTextContent(/Jellyfin can identify it now/)
    expect(within(item).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each([
    ['Retry', linkedPenguinsItemOf({ writeState: 'NotWritten', writeError: 'Jellyfin didn\'t answer in time.', identifiedUtc: null })],
    ['Identify again', linkedPenguinsItemOf({ writeState: 'Lost', identifiedUtc: null })],
  ])('identifies the series again on %s, after a question that names the series', async (action, item) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({ items: [item] }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    const dialog = await loaded()
    expect(dialog).not.toHaveTextContent(/rating/)

    // Act
    await user.click(screen.getByRole('button', { name: action }))
    const question = await screen.findByRole('dialog', { name: 'Identify in Jellyfin?' })
    const message = question.textContent
    const asked = vi.mocked(libraryLinksApi.writeLink).mock.calls.length
    await user.click(within(question).getByRole('button', { name: action }))

    // Assert
    expect(message).toContain(`Jellyfin will replace the title, overview and images of “${penguins.title}” with TMDb's, in the background, and there is no undo.`)
    expect(asked).toBe(0)
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(penguinsLinkId)
  })
})
