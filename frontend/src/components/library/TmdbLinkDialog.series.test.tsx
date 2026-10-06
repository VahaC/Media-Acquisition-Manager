import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import {
  libraryIdentifierOf,
  linkedPenguinsDialogOf,
  penguins,
  penguinsDialogOf,
  penguinsItemOf,
  seriesKeptInAppReason,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb dialog of a series (Vasyl's decision of 2026-10-06): its link is kept in the app
// and never written into Jellyfin, as Jellyfin saves every season and episode of a series it saves.
// The server says so per item (`canWrite: false` and its sentence); the dialog shows that sentence,
// the state "Kept in the app" with nothing to retry, and asks about the app only before it goes over
// an id Jellyfin holds.

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

describe('TmdbLinkDialog for a series', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf())
  })

  it('says under the series why its link stays in the app, before anything is linked', async () => {
    // Arrange
    renderDialog()

    // Act
    const dialog = await loaded()

    // Assert
    expect(within(dialog).getByText(`Home Jellyfin · ${penguins.title}`)).toBeInTheDocument()
    expect(within(dialog).getByText('No link.')).toBeInTheDocument()
    expect(within(dialog).getByText(seriesKeptInAppReason)).toHaveClass('settings-muted')
    expect(dialog).toHaveAccessibleDescription(
      'Pick the TMDb film or show this work is. A film\'s id is written into its item\'s ids in Jellyfin, and nothing else there changes; a series\' or an episode\'s link is kept in the app.',
    )
  })

  it('links the picked show to the series with no season or episode, and closes as the link is kept in the app', async () => {
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

  it('shows a linked series as kept in the app, with the reason and nothing to retry or write again', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf())

    // Act
    renderDialog()
    const dialog = await loaded()

    // Assert
    expect(within(dialog).getByText('Пінгвіни з Мадагаскару (Show, TMDb 7869)')).toBeInTheDocument()
    expect(within(dialog).getByText('Kept in the app')).toHaveClass('ui-badge', 'ui-badge-outline')
    expect(within(dialog).getByText(seriesKeptInAppReason)).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Write again' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Remove link' })).toBeEnabled()
  })

  it('asks about the app only before linking over the TMDb id Jellyfin holds for the series, giving the server\'s reason', async () => {
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
    expect(confirmation).toHaveTextContent(`Jellyfin holds TMDb 999. ${seriesKeptInAppReason} Link 7869 instead?`)
    expect(confirmation).not.toHaveTextContent('Replace it with')
    await user.click(within(confirmation).getByRole('button', { name: 'Replace' }))

    // Assert
    await vi.waitFor(() => expect(libraryLinksApi.link).toHaveBeenCalledWith(penguins.id, expect.objectContaining({ catalogKind: 'Series', catalogId: '7869' })))
  })
})
