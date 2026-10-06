import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDateTime } from '@/lib/format'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { LibraryWork } from '@/lib/types'
import { apiError } from '@/test/api-error'
import {
  blitzkrieg,
  blitzkriegDialogOf,
  blitzkriegItemOf,
  blitzkriegLinkId,
  landingStripling,
  linkedDialogOf,
  linkedItemOf,
  linkId,
  noEpisodeImdbReason,
  noEpisodeNumbersReason,
  switchedOffReason,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — after a write, Jellyfin is asked to look up the item's missing metadata, as its own "Search
// for missing metadata" does (Vasyl's decision of 2026-10-06): each item says when it was asked, or
// why Jellyfin didn't take it, with Retry — the server records a refused request with its time too —,
// or, never asked, offers to ask. An episode Jellyfin lists as a film gets its episode's own IMDb id,
// which its item shows; an episode link the app has no id to write for stays in the app, with the
// server's sentence why, and one kept there before the app wrote episodes can be written now.

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

function renderDialog(work: LibraryWork) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <TmdbLinkDialog work={work} onClose={vi.fn()} />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

/** The dialog once its content has loaded. */
async function loaded(work: LibraryWork) {
  const dialog = await screen.findByRole('dialog', { name: `Link “${work.title}” to TMDb` })
  await within(dialog).findByRole('heading', { name: 'Suggestions' })
  return dialog
}

/** The item's row, under "In the library". */
const itemRow = (dialog: HTMLElement, work: LibraryWork) =>
  within(dialog).getByText(`Home Jellyfin · ${work.title}`).closest('li')!

describe('TmdbLinkDialog metadata lookups and episode links', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('says under a written film when Jellyfin was asked to look up its missing metadata', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())

    // Act
    renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)

    // Assert
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText('Written to Jellyfin')).toBeInTheDocument()
    expect(within(row).getByText(`Jellyfin was asked to look up its missing metadata on ${formatDateTime('2026-10-05T18:40:05Z', INSTANT_FORMAT)}.`))
      .toHaveClass('settings-muted')
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it('says a film written before Jellyfin was asked for the lookup hasn\'t been, and asks on its button, with no question', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedDialogOf({ items: [linkedItemOf({ writeState: 'InLibrary', lookupRequestedUtc: null })] }))
      .mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ writeState: 'InLibrary', lookupRequestedUtc: '2026-10-06T11:00:00Z' })] }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText('In Jellyfin')).toBeInTheDocument()
    expect(within(row).getByText('Jellyfin hasn\'t been asked to look up its missing metadata.')).toHaveClass('settings-muted')

    // Act
    await user.click(within(row).getByRole('button', { name: 'Ask Jellyfin to look it up' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(linkId)
    expect(await screen.findByText(`Jellyfin was asked to look up its missing metadata on ${formatDateTime('2026-10-06T11:00:00Z', INSTANT_FORMAT)}.`))
      .toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ask Jellyfin to look it up' })).not.toBeInTheDocument()
  })

  it('offers nothing to ask for a film whose integration is switched off', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({
      items: [linkedItemOf({ writeState: 'InLibrary', lookupRequestedUtc: null, canWrite: false, cannotWriteReason: switchedOffReason })],
    }))

    // Act
    renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)

    // Assert
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText(switchedOffReason)).toBeInTheDocument()
    expect(row).not.toHaveTextContent(/look up its missing metadata/)
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it('keeps a written film written when Jellyfin didn\'t take the lookup, says why on one line, and offers Retry', async () => {
    // Arrange
    const sentence = 'Jellyfin refused the API key.'
    // As the server records it: the request's time with the refusal.
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ lookupRequestedUtc: '2026-10-06T10:00:00Z', lookupError: sentence })] }))

    // Act
    renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)

    // Assert
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText('Written to Jellyfin')).toBeInTheDocument()
    const line = within(row).getByText(`Jellyfin didn't take the request to look up its missing metadata: ${sentence}`)
    expect(line.closest('p')).toHaveAttribute('data-status', 'Failed')
    // Never both: "was asked" under a refusal would say the opposite of the line above.
    expect(row).not.toHaveTextContent(/was asked to look up/)
    expect(row).not.toHaveTextContent(/hasn't been asked/)
    expect(within(row).getByRole('button', { name: 'Retry' })).toBeEnabled()
  })

  it('writes the link again on Retry after a failed lookup, which asks Jellyfin again, and shows the new request', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedDialogOf({ items: [linkedItemOf({ lookupRequestedUtc: '2026-10-06T10:00:00Z', lookupError: 'Jellyfin didn\'t answer in time.' })] }))
      .mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ lookupRequestedUtc: '2026-10-06T11:00:00Z' })] }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog(landingStripling)
    await loaded(landingStripling)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(linkId)
    expect(await screen.findByText(`Jellyfin was asked to look up its missing metadata on ${formatDateTime('2026-10-06T11:00:00Z', INSTANT_FORMAT)}.`))
      .toBeInTheDocument()
    expect(screen.queryByText(/didn't take the request/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })

  it('shows an episode link written as the episode\'s own IMDb id, and the lookup it was followed by', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(blitzkriegDialogOf())

    // Act
    renderDialog(blitzkrieg)
    const dialog = await loaded(blitzkrieg)

    // Assert
    expect(within(dialog).getByText('S1 E1')).toBeInTheDocument()
    const row = itemRow(dialog, blitzkrieg)
    expect(within(row).getByText('Written to Jellyfin')).toBeInTheDocument()
    expect(within(row).getByText('The episode\'s IMDb id: tt9103946')).toBeInTheDocument()
    expect(within(row).getByText(`Jellyfin was asked to look up its missing metadata on ${formatDateTime('2026-10-06T10:00:03Z', INSTANT_FORMAT)}.`))
      .toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each([
    ['without a season and an episode', blitzkriegItemOf({ seasonNumber: null, episodeNumber: null, episodeImdbId: null, cannotWriteReason: noEpisodeNumbersReason }), noEpisodeNumbersReason],
    // Only asking TMDb tells it: the server keeps it as the link's error too, and gives it as the reason.
    ['whose episode TMDb knows no IMDb id of', blitzkriegItemOf({ episodeImdbId: null, writeError: noEpisodeImdbReason, cannotWriteReason: noEpisodeImdbReason }), noEpisodeImdbReason],
  ])('shows an episode link %s as kept in the app, with the server\'s sentence why and nothing to retry', async (_, kept, reason) => {
    // Arrange
    // The item itself can be written (a film item): the reason is its link's own.
    const item = { ...kept, writeState: 'KeptInApp' as const, lookupRequestedUtc: null }
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(blitzkriegDialogOf(item))

    // Act
    renderDialog(blitzkrieg)
    const dialog = await loaded(blitzkrieg)

    // Assert
    const row = itemRow(dialog, blitzkrieg)
    expect(item.canWrite).toBe(true)
    expect(within(row).getByText('Kept in the app')).toBeInTheDocument()
    // Once, as a muted fact: a kept link isn't a failure.
    expect(within(row).getByText(reason)).toHaveClass('settings-muted')
    expect(within(row).queryByText(/IMDb id: tt/)).not.toBeInTheDocument()
    expect(within(row).queryByText(/look up its missing metadata/)).not.toBeInTheDocument()
    expect(within(row).queryByText(/can be written into Jellyfin now/)).not.toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers Write to Jellyfin for a numbered episode link kept in the app before the app wrote episodes, and writes it with no question', async () => {
    // Arrange
    // As the server sends one set under the first rules: kept, with nothing that keeps it now.
    const legacy = blitzkriegItemOf({ writeState: 'KeptInApp', episodeImdbId: null, lookupRequestedUtc: null })
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(blitzkriegDialogOf(legacy))
      .mockResolvedValue(blitzkriegDialogOf())
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog(blitzkrieg)
    const dialog = await loaded(blitzkrieg)
    const row = itemRow(dialog, blitzkrieg)
    expect(within(row).getByText('Kept in the app')).toBeInTheDocument()
    expect(within(row).getByText('Set while the app kept such links to itself: it can be written into Jellyfin now.')).toHaveClass('settings-muted')
    // No lookup to ask for before it is written.
    expect(row).not.toHaveTextContent(/look up its missing metadata/)

    // Act
    await user.click(within(row).getByRole('button', { name: 'Write to Jellyfin' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(blitzkriegLinkId)
    expect(await within(itemRow(dialog, blitzkrieg)).findByText('The episode\'s IMDb id: tt9103946')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Write to Jellyfin' })).not.toBeInTheDocument()
  })

  it('shows a refused Retry of an episode link under its item', async () => {
    // Arrange
    const sentence = 'TMDb couldn\'t be read.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(blitzkriegDialogOf(blitzkriegItemOf({ writeState: 'NotWritten', writeError: sentence, episodeImdbId: null, lookupRequestedUtc: null })))
    vi.mocked(libraryLinksApi.writeLink).mockRejectedValue(apiError(502, sentence))
    const { user } = renderDialog(blitzkrieg)
    await loaded(blitzkrieg)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(blitzkriegLinkId)
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
  })
})
