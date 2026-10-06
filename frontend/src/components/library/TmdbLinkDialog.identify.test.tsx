import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, within } from '@testing-library/react'
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
  conflictSentence,
  landingStripling,
  legacyWrittenItemOf,
  linkedDialogOf,
  linkedItemOf,
  linkedPenguinsDialogOf,
  linkedPenguinsItemOf,
  linkId,
  lockedSentence,
  noEpisodeImdbReason,
  noEpisodeNumbersReason,
  noMatchSentence,
  notConfirmedSentence,
  penguins,
  queuedItemOf,
  switchedOffReason,
  writesBesideSentence,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — an item's identification as the dialog shows it (Vasyl's decision of 2026-10-06, ADR 0026):
// identified with when, never identified (a link set under the first rules put ids only), waiting or
// running with how long it takes, or refused with the server's sentence as it is. Identify in Jellyfin,
// Retry and Identify again each ask first. An episode Jellyfin lists as a film is identified through its
// own IMDb id; an episode link the app has no id to identify by stays in the app, with the sentence why.

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

/** The question every Identify, Retry and Identify again asks first. */
const identifyQuestion = () => screen.findByRole('dialog', { name: 'Identify in Jellyfin?' })

const filmUnderWay = 'Jellyfin identifies the item in the background; this takes a few seconds. You can close this window.'
const episodeUnderWay = 'Jellyfin identifies the item through the Open Movie Database in the background; this takes about 20 seconds. You can close this window.'
const seriesUnderWay = 'Jellyfin identifies the series and refreshes its seasons and episodes in the background; this takes minutes. You can close this window: the result shows here and on the Library card.'
const notIdentifiedLine = 'The ids are in Jellyfin, but the app hasn\'t identified the item: Jellyfin still shows the title, overview and images it had. Identify it to replace them with TMDb\'s.'

describe('TmdbLinkDialog identifications and episode links', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('says under an identified film when Jellyfin confirmed it, with nothing to click', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())

    // Act
    renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)

    // Assert
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText('Identified in Jellyfin')).toHaveClass('ui-badge-outline')
    expect(within(row).getByText(`Confirmed in Jellyfin on ${formatDateTime('2026-10-05T18:40:05Z', INSTANT_FORMAT)}.`)).toHaveClass('settings-muted')
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each(['Written', 'InLibrary'] as const)('shows a film %s under the first rules as not identified yet, says so, and asks before it identifies it on the button', async (writeState) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedDialogOf({ items: [legacyWrittenItemOf({ writeState })] }))
      .mockResolvedValue(linkedDialogOf({ items: [queuedItemOf()] }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText('Not identified yet')).toHaveClass('library-badge-warning')
    expect(within(row).getByText(notIdentifiedLine)).toHaveClass('settings-muted')
    expect(row).not.toHaveTextContent(/Confirmed in Jellyfin/)

    // Act
    await user.click(within(row).getByRole('button', { name: 'Identify in Jellyfin' }))
    const refused = await identifyQuestion()
    const message = refused.textContent
    await user.click(within(refused).getByRole('button', { name: 'Cancel' }))
    const afterCancel = vi.mocked(libraryLinksApi.writeLink).mock.calls.length
    await user.click(within(row).getByRole('button', { name: 'Identify in Jellyfin' }))
    const question = await identifyQuestion()
    const destructive = within(question).getByRole('button', { name: 'Identify in Jellyfin' }).classList.contains('ui-button-destructive')
    await user.click(within(question).getByRole('button', { name: 'Identify in Jellyfin' }))

    // Assert
    expect(message).toContain('Jellyfin will replace the title, overview and images of “120. Landing Stripling” with TMDb\'s, in the background, and there is no undo. Your files and folders are not touched.')
    expect(destructive).toBe(true)
    expect(afterCancel).toBe(0)
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(linkId)
    expect(await within(dialog).findByText('Waiting to identify')).toBeInTheDocument()
    expect(within(dialog).queryByText(notIdentifiedLine)).not.toBeInTheDocument()
  })

  it('offers nothing to identify for a film whose integration is switched off', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({
      items: [legacyWrittenItemOf({ writeState: 'InLibrary', canWrite: false, cannotWriteReason: switchedOffReason })],
    }))

    // Act
    renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)

    // Assert
    const row = itemRow(dialog, landingStripling)
    expect(within(row).getByText(switchedOffReason)).toBeInTheDocument()
    expect(row).not.toHaveTextContent(/Identify it to replace them/)
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each([
    ['Retry', linkedItemOf({ writeState: 'NotWritten', writeError: notConfirmedSentence, identifiedUtc: null })],
    ['Identify again', linkedItemOf({ writeState: 'Lost' })],
  ])('asks the same question before %s, sends nothing when told no, and sends once on a double click', async (action, item) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({ items: [item] }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog(landingStripling)
    await loaded(landingStripling)

    // Act
    await user.click(screen.getByRole('button', { name: action }))
    await user.click(within(await identifyQuestion()).getByRole('button', { name: 'Cancel' }))
    const afterCancel = vi.mocked(libraryLinksApi.writeLink).mock.calls.length
    // Two clicks before the question has taken the page, as a quick double click makes.
    const button = screen.getByRole('button', { name: action })
    fireEvent.click(button)
    fireEvent.click(button)
    const questions = (await screen.findAllByRole('dialog', { name: 'Identify in Jellyfin?' })).length
    await user.click(within(await identifyQuestion()).getByRole('button', { name: action }))

    // Assert
    expect(afterCancel).toBe(0)
    expect(questions).toBe(1)
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(linkId)
    expect(screen.queryByRole('dialog', { name: 'Identify in Jellyfin?' })).not.toBeInTheDocument()
  })

  it('says the item couldn\'t be sent to Jellyfin when the request fails with nothing readable', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ writeState: 'Lost' })] }))
    vi.mocked(libraryLinksApi.writeLink).mockRejectedValue(apiError(500))
    const { user } = renderDialog(landingStripling)
    await loaded(landingStripling)

    // Act
    await user.click(screen.getByRole('button', { name: 'Identify again' }))
    await user.click(within(await identifyQuestion()).getByRole('button', { name: 'Identify again' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The item couldn\'t be sent to Jellyfin.')
  })

  it.each([
    ['a library that saves beside the media', writesBesideSentence],
    ['a locked item', lockedSentence],
    ['an id Jellyfin holds that is not the link\'s', conflictSentence],
    ['no match in Jellyfin', noMatchSentence],
    ['a match the item doesn\'t show', notConfirmedSentence],
  ])('shows the refusal for %s on the item as the server gave it, with Retry', async (_, sentence) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({
      items: [linkedItemOf({ writeState: 'NotWritten', writeError: sentence, identifiedUtc: null })],
    }))

    // Act
    renderDialog(landingStripling)
    const dialog = await loaded(landingStripling)

    // Assert
    const row = itemRow(dialog, landingStripling)
    // A line of its own, not a tooltip, and the editor neither parses nor shortens it.
    expect(within(row).getByText(sentence).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(within(row).getByText('Not identified yet')).toHaveClass('library-badge-warning')
    expect(within(row).getByRole('button', { name: 'Retry' })).toBeEnabled()
  })

  it.each([
    ['a film', landingStripling, linkedDialogOf({ items: [queuedItemOf()] }), filmUnderWay],
    ['an episode Jellyfin lists as a film', blitzkrieg, blitzkriegDialogOf(blitzkriegItemOf({ writeState: 'Queued', identifiedUtc: null })), episodeUnderWay],
  ])('says how long %s takes while it waits for Jellyfin', async (_, work, answer, line) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(answer)

    // Act
    renderDialog(work)
    const dialog = await loaded(work)

    // Assert
    const row = itemRow(dialog, work)
    expect(within(row).getByText('Waiting to identify')).toBeInTheDocument()
    expect(within(row).getByText(line)).toHaveClass('settings-muted')
    // A line for the waiting item only: nothing is confirmed or offered yet.
    expect(row).not.toHaveTextContent(/Confirmed in Jellyfin/)
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it('says a queued series takes minutes, and gives no since-when until Jellyfin has begun', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedPenguinsDialogOf({
      items: [linkedPenguinsItemOf({ writeState: 'Queued', writeStateUtc: '2026-10-06T09:00:00Z', identifiedUtc: null })],
    }))

    // Act
    renderDialog(penguins)
    const dialog = await loaded(penguins)

    // Assert
    const row = within(dialog).getByText(`Home Jellyfin · ${penguins.title}`).closest('li')!
    expect(within(row).getByText('Waiting to identify')).toBeInTheDocument()
    expect(within(row).getByText(seriesUnderWay)).toBeInTheDocument()
    expect(row).not.toHaveTextContent(/Started /)
  })

  it('shows an episode link identified through the episode\'s own IMDb id, with its season and episode and when', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(blitzkriegDialogOf())

    // Act
    renderDialog(blitzkrieg)
    const dialog = await loaded(blitzkrieg)

    // Assert
    expect(within(dialog).getByText('S1 E1')).toBeInTheDocument()
    const row = itemRow(dialog, blitzkrieg)
    expect(within(row).getByText('Identified in Jellyfin')).toBeInTheDocument()
    expect(within(row).getByText('The episode\'s IMDb id: tt9103946')).toBeInTheDocument()
    expect(within(row).getByText(`Confirmed in Jellyfin on ${formatDateTime('2026-10-06T10:00:03Z', INSTANT_FORMAT)}.`)).toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it.each([
    ['without a season and an episode', blitzkriegItemOf({ seasonNumber: null, episodeNumber: null, episodeImdbId: null, cannotWriteReason: noEpisodeNumbersReason }), noEpisodeNumbersReason],
    // Only asking TMDb tells it: the server keeps it as the link's error too, and gives it as the reason.
    ['whose episode TMDb knows no IMDb id of', blitzkriegItemOf({ episodeImdbId: null, writeError: noEpisodeImdbReason, cannotWriteReason: noEpisodeImdbReason }), noEpisodeImdbReason],
  ])('shows an episode link %s as kept in the app, with the server\'s sentence why and nothing to retry', async (_, kept, reason) => {
    // Arrange
    // The item itself can be identified (a film item): the reason is its link's own.
    const item = { ...kept, writeState: 'KeptInApp' as const, identifiedUtc: null }
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
    expect(within(row).queryByText(/Confirmed in Jellyfin/)).not.toBeInTheDocument()
    expect(within(row).queryByText(/Jellyfin can identify it now/)).not.toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers Identify in Jellyfin for a numbered episode link kept in the app before the app identified episodes, and identifies it after a question', async () => {
    // Arrange
    // As the server sends one set under the first rules: kept, with nothing that keeps it now.
    const legacy = blitzkriegItemOf({ writeState: 'KeptInApp', episodeImdbId: null, identifiedUtc: null })
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(blitzkriegDialogOf(legacy))
      .mockResolvedValue(blitzkriegDialogOf())
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog(blitzkrieg)
    const dialog = await loaded(blitzkrieg)
    const row = itemRow(dialog, blitzkrieg)
    expect(within(row).getByText('Kept in the app')).toBeInTheDocument()
    expect(within(row).getByText('Set while the app kept such links to itself: Jellyfin can identify it now.')).toHaveClass('settings-muted')
    // Nothing confirmed before it is identified.
    expect(row).not.toHaveTextContent(/Confirmed in Jellyfin/)

    // Act
    await user.click(within(row).getByRole('button', { name: 'Identify in Jellyfin' }))
    await user.click(within(await identifyQuestion()).getByRole('button', { name: 'Identify in Jellyfin' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(blitzkriegLinkId)
    expect(await within(itemRow(dialog, blitzkrieg)).findByText('The episode\'s IMDb id: tt9103946')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Identify in Jellyfin' })).not.toBeInTheDocument()
  })

  it('offers no Identify for a link whose ids Jellyfin holds but whose link the app keeps for a reason of its own', async () => {
    // Arrange
    // An episode link with no season and episode that an earlier build wrote ids for: the server would answer 409.
    const item = blitzkriegItemOf({ writeState: 'Written', seasonNumber: null, episodeNumber: null, episodeImdbId: null, cannotWriteReason: noEpisodeNumbersReason, identifiedUtc: null })
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(blitzkriegDialogOf(item))

    // Act
    renderDialog(blitzkrieg)
    const dialog = await loaded(blitzkrieg)

    // Assert
    const row = itemRow(dialog, blitzkrieg)
    expect(within(row).getByText(noEpisodeNumbersReason)).toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    expect(within(row).queryByText(notIdentifiedLine)).not.toBeInTheDocument()
  })

  it('shows a refused Retry of an episode link under its item', async () => {
    // Arrange
    const sentence = 'TMDb couldn\'t be read.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(blitzkriegDialogOf(blitzkriegItemOf({ writeState: 'NotWritten', writeError: sentence, episodeImdbId: null, identifiedUtc: null })))
    vi.mocked(libraryLinksApi.writeLink).mockRejectedValue(apiError(502, sentence))
    const { user } = renderDialog(blitzkrieg)
    await loaded(blitzkrieg)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await user.click(within(await identifyQuestion()).getByRole('button', { name: 'Retry' }))

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(blitzkriegLinkId)
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
  })
})
