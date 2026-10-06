import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDateTime } from '@/lib/format'
import { libraryLinksApi } from '@/lib/library-links-api'
import { qk } from '@/lib/queries'
import type { LibraryWork } from '@/lib/types'
import { apiError } from '@/test/api-error'
import {
  dialogOf,
  homeJellyfinItemId,
  itemOf,
  junglesDialogOf,
  landingStripling,
  libraryIdentifierOf,
  linkedDialogOf,
  linkedItemOf,
  linkId,
  notConfirmedSentence,
  penguins,
  penguinsDialogOf,
  penguinsShow,
  planetEarthJungles,
  planetEarthShow,
  queuedItemOf,
  switchedOffReason,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb dialog of a library work: what it is linked to and where each item stands, the
// suggestions to pick from, what Jellyfin will replace said before Save, Save with its confirmation
// (which never waits for Jellyfin), Remove link, and Retry for one item.

const dialogName = 'Link “120. Landing Stripling” to TMDb'

/** The amber sentence above the buttons that says what saving makes Jellyfin do (the film's). */
const willIdentify = 'Saving makes Jellyfin identify each ticked item as this film: it replaces the item\'s title, overview and images with TMDb\'s, in its library\'s language. There is no undo; your files and folders are not touched.'
const willIdentifyShow = 'Saving makes Jellyfin identify the ticked series as this show: it replaces its title, overview and images with TMDb\'s, in its library\'s language, and refreshes its episodes too. There is no undo; your files and folders are not touched.'
const willIdentifyEpisode = 'Saving makes Jellyfin look each ticked item up by this episode\'s IMDb id in the Open Movie Database and replace its title, year and image — the image may be a frame of the video, not a poster. There is no undo; your files and folders are not touched.'
const savedNotIdentified = 'Saved, but Jellyfin didn\'t identify the item: the items above say why.'
const savedUnderWay = 'Saved. Jellyfin is identifying the item in the background; the item above shows how it goes.'
const savedIdentified = 'Saved, and Jellyfin identified the item.'
const savedKept = 'Saved, but the app keeps the link of an item to itself: the items above say why.'
const filmUnderWay = 'Jellyfin identifies the item in the background; this takes a few seconds. You can close this window.'
const removeMessage = 'The app forgets the link. Jellyfin keeps what the identification gave the item — its title, overview, images and ids — and nothing is changed back there; ids an earlier version of the app wrote there stay too.'
const replaceSuffix = 'its title, overview and images are replaced too, and there is no undo.'

const instant = (value: string) => formatDateTime(value, { dateStyle: 'medium', timeStyle: 'short' })

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
const pickFilm = () => screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' })
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

/** The footer's buttons, in order, as their text. */
const footerButtons = (dialog: HTMLElement) =>
  within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button').map((button) => button.textContent)

/** Presses `label` in the question every Identify, Retry and Identify again asks first. */
async function answerIdentifyQuestion(user: ReturnType<typeof userEvent.setup>, label: string) {
  const question = await screen.findByRole('dialog', { name: 'Identify in Jellyfin?' })
  await user.click(within(question).getByRole('button', { name: label }))
}

describe('TmdbLinkDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf())
  })

  afterEach(() => {
    vi.useRealTimers()
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
    expect(footerButtons(dialog)).toEqual(['Save', 'Cancel'])
  })

  it('describes itself without repeating what Jellyfin replaces, which the statement above Save says', async () => {
    // Arrange
    renderDialog()

    // Act
    const dialog = await loaded()

    // Assert
    expect(dialog).toHaveAccessibleDescription('Pick the TMDb film or show this work is. An item says why when its link stays in the app.')
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

  it('says before Save what Jellyfin will replace: title, overview, images, no undo, files untouched', async () => {
    // Arrange
    const { user } = renderDialog()
    const dialog = await loaded()
    const before = within(dialog).queryByText(/Saving makes Jellyfin/)

    // Act
    await user.click(pickFilm())

    // Assert
    expect(before).not.toBeInTheDocument()
    const statement = within(dialog).getByText(willIdentify)
    const banner = statement.closest('.ui-banner')!
    // A polite status in amber, outside the one scrolling body and above the buttons, where a phone keeps it on screen.
    expect(banner).toHaveAttribute('role', 'status')
    expect(banner).toHaveAttribute('data-tone', 'warning')
    expect(dialog.querySelector('.settings-dialog-body')).not.toContainElement(banner as HTMLElement)
    expect(banner.compareDocumentPosition(dialog.querySelector('.ui-dialog-footer')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('says before Save that a show picked for a series is identified with its episodes refreshed', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(penguinsDialogOf())
    const { user } = renderDialog(penguins)
    const dialog = await loaded(`Link “${penguins.title}” to TMDb`)

    // Act
    await user.click(screen.getByRole('radio', { name: `Pick ${penguinsShow.title} (2008)` }))

    // Assert
    expect(within(dialog).getByText(willIdentifyShow)).toBeInTheDocument()
    expect(dialog).not.toHaveTextContent(willIdentify)
  })

  it('says an episode on a film item goes through the Open Movie Database, may get a frame for an image and keeps its overview, before Save', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(junglesDialogOf())
    const { user } = renderDialog(planetEarthJungles)
    const dialog = await loaded('Link “Planet_Earth_E08 - Jungles” to TMDb')

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Планета Земля (2006)' }))

    // Assert
    expect(within(dialog).getByText(willIdentifyEpisode)).toBeInTheDocument()
    // The 20 seconds and the kept overview are said once, where they apply: the hint in the body.
    expect(within(dialog).getByText(/through the Open Movie Database, when TMDb knows that id \(it keeps the item's overview if the database has none\)/)).toHaveClass('ui-hint')
    expect(dialog).not.toHaveTextContent(willIdentify)
  })

  it('shows no episode statement while the season or the episode is blank, as the link then stays in the app', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(junglesDialogOf())
    const { user } = renderDialog(planetEarthJungles)
    const dialog = await loaded('Link “Planet_Earth_E08 - Jungles” to TMDb')
    await user.click(screen.getByRole('radio', { name: 'Pick Планета Земля (2006)' }))
    const withNumbers = within(dialog).queryByText(willIdentifyEpisode) !== null
    const season = screen.getByRole('textbox', { name: 'Season (optional)' })
    const episode = screen.getByRole('textbox', { name: 'Episode (optional)' })

    // Act
    await user.clear(season)
    const noSeason = within(dialog).queryByText(/Saving makes Jellyfin/) !== null
    await user.type(season, '1')
    await user.clear(episode)
    const noEpisode = within(dialog).queryByText(/Saving makes Jellyfin/) !== null
    await user.type(episode, '8')

    // Assert
    expect(withNumbers).toBe(true)
    expect(noSeason).toBe(false)
    expect(noEpisode).toBe(false)
    expect(within(dialog).getByText(willIdentifyEpisode)).toBeInTheDocument()
  })

  it('promises no Identify for items the server keeps in the app', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ items: [itemOf({ canWrite: false, cannotWriteReason: switchedOffReason })] }))
    const { user } = renderDialog()
    const dialog = await loaded()

    // Act
    await user.click(pickFilm())

    // Assert
    expect(within(dialog).getByText(switchedOffReason)).toBeInTheDocument()
    expect(dialog).not.toHaveTextContent(/Saving makes Jellyfin/)
  })

  it('links the picked film to every item and closes when no item is left waiting', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(pickFilm())
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

  it('closes when every ticked item is kept in the app for a reason the item showed before the save', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ items: [itemOf({ canWrite: false, cannotWriteReason: switchedOffReason })] }))
    const kept = linkedItemOf({ writeState: 'KeptInApp', canWrite: false, cannotWriteReason: switchedOffReason, identifiedUtc: null })
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [kept] }))
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('stays open on an item the save found TMDb has no IMDb id for, so the owner reads why it is kept in the app', async () => {
    // Arrange
    const noImdb = 'TMDb gives this episode no IMDb id, so Jellyfin can not be asked to identify it.'
    const kept = linkedItemOf({ writeState: 'KeptInApp', writeError: noImdb, cannotWriteReason: noImdb, identifiedUtc: null })
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [kept] }))
    const { onClose, user } = renderDialog()
    const dialog = await loaded()

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(onClose).not.toHaveBeenCalled()
    expect(within(dialog).getByText(noImdb)).toBeInTheDocument()
    const banner = within(dialog).getByText(savedKept).closest('.ui-banner')!
    expect(banner).toHaveAttribute('data-tone', 'warning')
    expect(banner).toHaveAttribute('role', 'status')
    expect(within(dialog).queryByText(savedIdentified)).not.toBeInTheDocument()
    expect(footerButtons(dialog)).toEqual(['Save', 'Close'])
  })

  it('puts the saved dialog in the cache and drops the works and the other link queries', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf())
    const { client, user } = renderDialog()
    await loaded()
    client.setQueryData(qk.library.works({ page: 1, pageSize: 25 }), { items: [], totalCount: 0, page: 1, pageSize: 25 })
    client.setQueryData(qk.library.links.queue(0), { totalCount: 1, index: 0, current: null })

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(client.getQueryData(qk.library.links.work(landingStripling.id))).toEqual(linkedDialogOf())
    expect(client.getQueryState(qk.library.works({ page: 1, pageSize: 25 }))?.isInvalidated).toBe(true)
    expect(client.getQueryState(qk.library.links.queue(0))?.isInvalidated).toBe(true)
    expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(1)
  })

  it('stays open on a queued item, says Jellyfin works in the background, then shows it identified when the poll brings it', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(dialogOf())
      .mockResolvedValueOnce(linkedDialogOf({ items: [queuedItemOf({ writeState: 'Writing', writeStateUtc: '2026-10-06T11:00:00Z' })] }))
      .mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [queuedItemOf()] }))
    const { onClose, user } = renderDialog()
    const dialog = await loaded()

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())
    const waiting = await within(dialog).findByText('Waiting to identify')
    const bannerWhileWaiting = within(dialog).getByText(savedUnderWay)
    const underWayWhileWaiting = within(dialog).getByText(filmUnderWay)
    await nextPoll()
    const identifying = await within(dialog).findByText('Identifying…', { selector: '.ui-badge' })
    // The same line element is reused by the next state, so its text is read now.
    const startedLine = within(dialog).getByText(new RegExp(`^${filmUnderWay} Started `)).textContent
    await nextPoll()
    const identified = await within(dialog).findByText('Identified in Jellyfin')
    await nextPoll()
    await nextPoll()

    // Assert
    expect(waiting).toHaveClass('ui-badge')
    expect(bannerWhileWaiting.closest('.ui-banner')).toHaveAttribute('data-tone', 'ok')
    expect(underWayWhileWaiting).toHaveClass('settings-muted')
    expect(identifying).toHaveClass('ui-badge-outline')
    expect(startedLine).toBe(`${filmUnderWay} Started ${instant('2026-10-06T11:00:00Z')}.`)
    expect(identified).toHaveClass('ui-badge-outline')
    expect(within(dialog).getByText(`Confirmed in Jellyfin on ${instant('2026-10-05T18:40:05Z')}.`)).toBeInTheDocument()
    expect(within(dialog).getByText(savedIdentified)).toBeInTheDocument()
    expect(within(dialog).queryByText(savedUnderWay)).not.toBeInTheDocument()
    expect(within(dialog).queryByText(filmUnderWay)).not.toBeInTheDocument()
    // The dialog never closes by itself on success: the badge turning over is the result he asked to see.
    expect(onClose).not.toHaveBeenCalled()
    // The poll stops once nothing waits: the load, the two polls and no more.
    expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(3)
  })

  it('turns Cancel into Close once a save is on screen, with Save off for want of a pick', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [queuedItemOf()] }))
    const { onClose, user } = renderDialog()
    const dialog = await loaded()
    const before = footerButtons(dialog)

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())
    await within(dialog).findByText(savedUnderWay)
    const after = footerButtons(dialog)
    await user.click(within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getByRole('button', { name: 'Close' }))

    // Assert
    expect(before).toEqual(['Save', 'Cancel'])
    expect(after).toEqual(['Save', 'Close'])
    expect(saveButton().parentElement).toHaveAttribute('title', 'Pick a film or a show first.')
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('does not close while one of two ticked items is still queued', async () => {
    // Arrange
    const second = { sourceItemId: 'second-item', itemTitle: 'Landing Stripling (copy)' }
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ items: [itemOf(), itemOf(second)] }))
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({
      items: [linkedItemOf(), queuedItemOf({ ...second, linkId: '11c00000-0000-4000-8000-000000000002' })],
    }))
    const { onClose, user } = renderDialog()
    const dialog = await loaded()

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(await within(dialog).findByText('Waiting to identify')).toBeInTheDocument()
    expect(within(dialog).getByText('Identified in Jellyfin')).toBeInTheDocument()
    expect(within(dialog).getByText(savedUnderWay)).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('takes the saved banner away when another suggestion is picked, so only the statement shows', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [queuedItemOf()] }))
    const { user } = renderDialog()
    const dialog = await loaded()
    await user.click(pickFilm())
    await user.click(saveButton())
    await within(dialog).findByText(savedUnderWay)
    const statementAfterSave = within(dialog).queryByText(/Saving makes Jellyfin/) !== null

    // Act
    await user.click(screen.getByRole('radio', { name: 'Pick Calypso Cat (1962)' }))

    // Assert
    // A save spends the pick: nothing to say about it until another is made.
    expect(statementAfterSave).toBe(false)
    expect(within(dialog).queryByText(savedUnderWay)).not.toBeInTheDocument()
    expect(within(dialog).getByText(willIdentify)).toBeInTheDocument()
    expect(footerButtons(dialog)).toEqual(['Save', 'Cancel'])
  })

  it('stays open on the item when Jellyfin didn\'t identify it, with its sentence and Retry', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({
      items: [linkedItemOf({ writeState: 'NotWritten', writeError: notConfirmedSentence, identifiedUtc: null })],
    }))
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(onClose).not.toHaveBeenCalled()
    const banner = await screen.findByText(savedNotIdentified)
    expect(banner.closest('.ui-banner')).toHaveAttribute('data-tone', 'warning')
    // The sentence is a line of its own on the item, not only a tooltip: a phone has none.
    expect(screen.getByText(notConfirmedSentence).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(screen.getByText('Not identified yet')).toHaveClass('library-badge-warning')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeEnabled()
  })

  it('takes the not-identified warning away once a Retry gets the item identified', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValueOnce(dialogOf()).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({
      items: [linkedItemOf({ writeState: 'NotWritten', writeError: notConfirmedSentence, identifiedUtc: null })],
    }))
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()
    await user.click(pickFilm())
    await user.click(saveButton())
    await screen.findByText(savedNotIdentified)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await answerIdentifyQuestion(user, 'Retry')
    await screen.findByText('Identified in Jellyfin')

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledExactlyOnceWith(linkId)
    expect(screen.queryByText(savedNotIdentified)).not.toBeInTheDocument()
    expect(screen.getByText(savedIdentified)).toBeInTheDocument()
  })

  it('keeps the not-identified warning when Jellyfin refuses the Retry too', async () => {
    // Arrange
    const notIdentified = linkedDialogOf({
      items: [linkedItemOf({ writeState: 'NotWritten', writeError: notConfirmedSentence, identifiedUtc: null })],
    })
    vi.mocked(libraryLinksApi.dialog).mockResolvedValueOnce(dialogOf()).mockResolvedValue(notIdentified)
    vi.mocked(libraryLinksApi.link).mockResolvedValue(notIdentified)
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()
    await user.click(pickFilm())
    await user.click(saveButton())
    await screen.findByText(savedNotIdentified)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await answerIdentifyQuestion(user, 'Retry')
    await waitFor(() => expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(2))

    // Assert
    expect(await screen.findByText(savedNotIdentified)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeEnabled()
  })

  it('shows a refused save above the buttons and keeps the pick', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.link).mockRejectedValue(apiError(409, 'An identification is under way for this work; try again in a moment.'))
    const { onClose, user } = renderDialog()
    await loaded()

    // Act
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('An identification is under way for this work; try again in a moment.')
    expect(pickFilm()).toBeChecked()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('asks before replacing a TMDb id Jellyfin holds, and sends nothing when told no', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf({ items: [itemOf({ libraryIdentifiers: [libraryIdentifierOf('Tmdb', '999')] })] }))
    const { user } = renderDialog()
    await loaded()
    await user.click(pickFilm())

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

  it.each([
    [
      'the id Jellyfin holds',
      dialogOf({ items: [itemOf({ libraryIdentifiers: [libraryIdentifierOf('Tmdb', '999')] })] }),
      'Pick Посадка Стріплінга (1962)',
      `Jellyfin holds TMDb 999. Replace it with 223021? Jellyfin identifies the item as 223021: ${replaceSuffix}`,
    ],
    [
      'the work\'s own link',
      linkedDialogOf(),
      'Pick Calypso Cat (1962)',
      `This work is linked to TMDb 223021. Replace it with 223022? Jellyfin identifies the item as 223022: ${replaceSuffix}`,
    ],
  ])('asks before replacing %s, saying Jellyfin identifies the item as the new one with no undo, in a destructive confirmation', async (_, dialog, pickLabel, message) => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialog)
    const { user } = renderDialog()
    await loaded()
    await user.click(screen.getByRole('radio', { name: pickLabel }))

    // Act
    await user.click(saveButton())
    const confirmation = await screen.findByRole('dialog', { name: 'Replace the TMDb id?' })

    // Assert
    expect(within(confirmation).getByText(message)).toBeInTheDocument()
    expect(within(confirmation).getByRole('button', { name: 'Replace' })).toHaveClass('ui-button-destructive')
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
    expect(screen.queryByText(/Jellyfin identifies the item by the episode's IMDb id, through the Open Movie Database/)).not.toBeNull()
    expect(libraryLinksApi.link).toHaveBeenCalledWith(planetEarthJungles.id, {
      catalogKind: 'Series',
      catalogId: '1044',
      seasonNumber: '1',
      episodeNumber: '9',
      sourceItemIds: null,
    })
  })

  it('says a show picked for an episode of no series links it in the app, as its item does, and promises no Identify', async () => {
    // Arrange
    // The jungles file as Jellyfin would list it under no series: an episode item, which it never identifies.
    const reason = 'Jellyfin identifies movie and series items only, so a link on an episode stays in the app.'
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
    expect(dialog).not.toHaveTextContent(/identifies the item by the episode's IMDb id/)
    expect(dialog).not.toHaveTextContent(/Saving makes Jellyfin/)
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
    await user.click(pickFilm())
    await user.click(saveButton())

    // Assert
    expect(libraryLinksApi.link).toHaveBeenCalledWith(landingStripling.id, expect.objectContaining({ sourceItemIds: [homeJellyfinItemId] }))
  })

  it('removes the link after a destructive confirmation that says Jellyfin keeps what the identification gave the item', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.remove).mockResolvedValue({ dialog: dialogOf() })
    const { client, user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Remove link' }))
    const confirmation = await screen.findByRole('dialog', { name: 'Remove the TMDb link?' })
    const asked = confirmation.textContent
    const remove = within(confirmation).getByRole('button', { name: 'Remove link' })
    const destructive = remove.classList.contains('ui-button-destructive')
    await user.click(remove)

    // Assert
    expect(asked).toContain(removeMessage)
    expect(destructive).toBe(true)
    expect(libraryLinksApi.remove).toHaveBeenCalledWith(landingStripling.id)
    expect(await screen.findByText('Not linked to TMDb yet.')).toBeInTheDocument()
    expect(client.getQueryData(qk.library.links.work(landingStripling.id))).toEqual(dialogOf())
  })

  it('says a link being identified can\'t be removed, and keeps showing it', async () => {
    // Arrange
    const sentence = 'An identification is under way for this work; try again in a moment.'
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.remove).mockRejectedValue(apiError(409, sentence))
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Remove link' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Remove the TMDb link?' })).getByRole('button', { name: 'Remove link' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(screen.getByText('Посадка Стріплінга (Film, TMDb 223021)')).toBeInTheDocument()
  })

  it('identifies one item\'s lost link again, after a question, and asks for the dialog anew', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog)
      .mockResolvedValueOnce(linkedDialogOf({ items: [linkedItemOf({ writeState: 'Lost' })] }))
      .mockResolvedValue(linkedDialogOf())
    vi.mocked(libraryLinksApi.writeLink).mockResolvedValue()
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Identify again' }))
    await answerIdentifyQuestion(user, 'Identify again')

    // Assert
    expect(libraryLinksApi.writeLink).toHaveBeenCalledWith(linkId)
    expect(await screen.findByText('Identified in Jellyfin')).toBeInTheDocument()
    expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(2)
  })

  it('shows a refused Retry under its item', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({ items: [linkedItemOf({ writeState: 'NotWritten', writeError: switchedOffReason, identifiedUtc: null })] }))
    vi.mocked(libraryLinksApi.writeLink).mockRejectedValue(apiError(409, switchedOffReason))
    const { user } = renderDialog()
    await loaded()

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await answerIdentifyQuestion(user, 'Retry')

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(switchedOffReason)
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
