import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { LibraryLinkList, LibraryLinksQuery } from '@/lib/types'
import { LibraryTmdbPage } from '@/pages/LibraryTmdbPage'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import {
  catchUpOf,
  conflictSentence,
  dialogOf,
  landingStripling,
  linkedDialogOf,
  linkedItemOf,
  linkId,
  linkRowOf,
  linksPage,
  queueOf,
  suggestionRunOf,
} from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb page's Not identified, Conflicts and Unattached tabs: the links Jellyfin or the app
// refused or failed to identify, each with its reason on screen (each opening its work's dialog to
// Retry, to link another film or show, or to remove the link), the links Jellyfin holds another TMDb id
// against (each opening its work's dialog to keep one of the two), and the links whose item the
// library no longer reports (each removable after a destructive confirmation), a page at a time.

function renderPage(tab: 'NotIdentified' | 'Conflicts' | 'Unattached') {
  signInAs(owner)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[`/library/tmdb?tab=${tab}`]}>
          <Routes>
            <Route path="/library/tmdb" element={<LibraryTmdbPage />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

/** The card of the link headed `title`. */
const cardOf = async (title: string) => (await screen.findByText(title, { selector: '.tmdb-link-row-title' })).closest('article')!

/** A link whose item Jellyfin no longer reports: its work went with it, its title stays. */
const goneRow = linkRowOf({ workId: null, workTitle: null, itemTitle: '120. Landing Stripling (1962) [old cut]', writeState: 'InLibrary' })

/** A link Jellyfin or the app refused to identify: its item is in the library, the reason is the server's sentence. */
const refusedRow = linkRowOf({ writeState: 'NotWritten', writeError: conflictSentence, identifiedUtc: null })

/**
 * The Update Jellyfin panel's asks, which these tests are not about: the Pending list and the one-link
 * count of the Not identified list, both answered with none.
 */
const isPanelAsk = (query: LibraryLinksQuery) => query.filter === 'Pending' || (query.filter === 'NotWritten' && query.pageSize === 1)

/** Answers the tab's list with `answer`, and the Update Jellyfin panel's asks with none. */
function serveLists(answer: (query: LibraryLinksQuery) => Promise<LibraryLinkList>) {
  vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => (isPanelAsk(query) ? linksPage(query, []) : answer(query)))
}

/** The tab's own asks for its list, without the Update Jellyfin panel's. */
const listAsks = () => vi.mocked(libraryLinksApi.links).mock.calls.map(([query]) => query).filter((query) => !isPanelAsk(query))

describe('LibraryTmdbPage lists of links', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockResolvedValue(queueOf(0, 0, null))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
    serveLists(async (query) => linksPage(query, query.filter === 'Conflict'
      ? [linkRowOf({ conflictingValue: '999', writeState: 'Lost' })]
      : query.filter === 'NotWritten' ? [refusedRow] : [goneRow]))
  })

  describe('Not identified', () => {
    it('lists the links Jellyfin or the app refused or failed to identify, with each reason on screen', async () => {
      // Arrange
      renderPage('NotIdentified')

      // Act
      const card = await cardOf('120. Landing Stripling')

      // Assert
      expect(listAsks()).toEqual([{ filter: 'NotWritten', page: 1, pageSize: 25 }])
      expect(screen.getByText(/^Jellyfin or the app refused or failed to identify these items; each card says why\./)).toBeInTheDocument()
      expect(within(card).getByText('Home Jellyfin · 120. Landing Stripling')).toBeInTheDocument()
      expect(within(card).getByText('Linked to Посадка Стріплінга (Film, TMDb 223021)')).toBeInTheDocument()
      const badge = within(card).getByText('Not identified yet')
      expect(badge).toHaveClass('library-badge-warning')
      // On the card as a line, verbatim, and not only as the badge's tooltip: a phone shows no tooltip.
      expect(badge).toHaveAttribute('title', conflictSentence)
      expect(within(card).getByText(conflictSentence, { selector: 'span' })).toBeInTheDocument()
      expect(within(card).getByText(/^Set by vasyl on /)).toBeInTheDocument()
      expect(within(card).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    })

    it('opens the work\'s dialog from a row, which has Retry', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.dialog).mockResolvedValue(linkedDialogOf({
        items: [linkedItemOf({ writeState: 'NotWritten', writeError: conflictSentence, identifiedUtc: null })],
      }))
      const { user } = renderPage('NotIdentified')
      const card = await cardOf('120. Landing Stripling')

      // Act
      await user.click(within(card).getByRole('button', { name: 'TMDb…' }))

      // Assert
      const dialog = await screen.findByRole('dialog', { name: 'Link “120. Landing Stripling” to TMDb' })
      expect(libraryLinksApi.dialog).toHaveBeenCalledExactlyOnceWith(landingStripling.id)
      expect(await within(dialog).findByRole('button', { name: 'Retry' })).toBeInTheDocument()
      expect(within(dialog).getByText(conflictSentence)).toBeInTheDocument()
    })

    it('says no link is waiting on the owner when none was refused or failed', async () => {
      // Arrange
      serveLists(async (query) => linksPage(query, []))

      // Act
      renderPage('NotIdentified')

      // Assert
      expect(await screen.findByText('No link is waiting on you: none was refused or failed.')).toBeInTheDocument()
    })

    it('asks the server for the next page', async () => {
      // Arrange
      serveLists(async (query) => linksPage(query, [refusedRow], 40))
      const { user } = renderPage('NotIdentified')
      await cardOf('120. Landing Stripling')

      // Act
      await user.click(screen.getByRole('button', { name: 'Next page' }))

      // Assert
      expect(listAsks().at(-1)).toEqual({ filter: 'NotWritten', page: 2, pageSize: 25 })
    })

    it('shows why the list couldn\'t load, with Try again', async () => {
      // Arrange
      serveLists(async () => {
        throw apiError(502, 'The server is restarting.')
      })

      // Act
      renderPage('NotIdentified')

      // Assert
      expect(await screen.findByText('The server is restarting.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    })
  })

  describe('Conflicts', () => {
    it('lists the links Jellyfin disagrees with, with both ids, who set each and where it stands', async () => {
      // Arrange
      renderPage('Conflicts')

      // Act
      const card = await cardOf('120. Landing Stripling')

      // Assert
      expect(listAsks()).toEqual([{ filter: 'Conflict', page: 1, pageSize: 25 }])
      expect(within(card).getByText('Home Jellyfin · 120. Landing Stripling')).toBeInTheDocument()
      expect(within(card).getByText('Linked to Посадка Стріплінга (Film, TMDb 223021)')).toBeInTheDocument()
      expect(within(card).getByText('TMDb ids disagree: 999 in Jellyfin, 223021 linked')).toBeInTheDocument()
      expect(within(card).getByText('Jellyfin lost it')).toBeInTheDocument()
      expect(within(card).getByText(/^Set by vasyl on /)).toBeInTheDocument()
      expect(within(card).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    })

    it('shows the sentence of a link Jellyfin lost on the card too, as a phone shows no tooltip', async () => {
      // Arrange
      serveLists(async (query) => linksPage(query, [
        linkRowOf({ conflictingValue: '999', writeState: 'Lost', writeError: 'The library read the item again without its TMDb id.' }),
      ]))

      // Act
      renderPage('Conflicts')
      const card = await cardOf('120. Landing Stripling')

      // Assert
      expect(within(card).getByText('The library read the item again without its TMDb id.', { selector: 'span' })).toBeInTheDocument()
    })

    it('opens the work\'s TMDb dialog from its card', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.dialog).mockResolvedValue(dialogOf())
      const { user } = renderPage('Conflicts')
      const card = await cardOf('120. Landing Stripling')

      // Act
      await user.click(within(card).getByRole('button', { name: 'TMDb…' }))

      // Assert
      expect(await screen.findByRole('dialog', { name: 'Link “120. Landing Stripling” to TMDb' })).toBeInTheDocument()
      expect(libraryLinksApi.dialog).toHaveBeenCalledExactlyOnceWith(landingStripling.id)
    })

    it('says when no link disagrees with Jellyfin', async () => {
      // Arrange
      serveLists(async (query) => linksPage(query, []))

      // Act
      renderPage('Conflicts')

      // Assert
      expect(await screen.findByText('No link disagrees with the TMDb id Jellyfin holds.')).toBeInTheDocument()
    })

    it('shows why the list couldn\'t load, with Try again', async () => {
      // Arrange
      serveLists(async () => {
        throw apiError(502, 'The server is restarting.')
      })

      // Act
      renderPage('Conflicts')

      // Assert
      expect(await screen.findByText('The server is restarting.')).toBeInTheDocument()
      expect(screen.getByText('The links couldn\'t be loaded.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    })
  })

  describe('Unattached', () => {
    it('lists the links whose item is gone, by the item\'s title, each with Remove', async () => {
      // Arrange
      renderPage('Unattached')

      // Act
      const card = await cardOf('120. Landing Stripling (1962) [old cut]')

      // Assert
      expect(listAsks()).toEqual([{ filter: 'Unattached', page: 1, pageSize: 25 }])
      expect(within(card).getByText('Identified in Jellyfin')).toBeInTheDocument()
      expect(within(card).getByRole('button', { name: 'Remove' })).toHaveClass('ui-button-danger')
      expect(within(card).queryByRole('button', { name: 'TMDb…' })).not.toBeInTheDocument()
    })

    it('removes a link after a destructive confirmation, and asks for the list again', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.removeLink).mockResolvedValue()
      const { user } = renderPage('Unattached')
      const card = await cardOf('120. Landing Stripling (1962) [old cut]')
      serveLists(async (query) => linksPage(query, []))

      // Act
      await user.click(within(card).getByRole('button', { name: 'Remove' }))
      const confirmation = await screen.findByRole('dialog', { name: 'Remove the link?' })
      const message = confirmation.textContent
      await user.click(within(confirmation).getByRole('button', { name: 'Remove' }))

      // Assert
      expect(message).toContain('The app forgets the link of “120. Landing Stripling (1962) [old cut]”.')
      expect(libraryLinksApi.removeLink).toHaveBeenCalledExactlyOnceWith(linkId)
      expect(await screen.findByText('Every link\'s item is still in the library.')).toBeInTheDocument()
      expect(listAsks()).toHaveLength(2)
    })

    it('sends nothing when the removal isn\'t confirmed', async () => {
      // Arrange
      const { user } = renderPage('Unattached')
      const card = await cardOf('120. Landing Stripling (1962) [old cut]')

      // Act
      await user.click(within(card).getByRole('button', { name: 'Remove' }))
      await user.click(within(await screen.findByRole('dialog', { name: 'Remove the link?' })).getByRole('button', { name: 'Cancel' }))

      // Assert
      expect(libraryLinksApi.removeLink).not.toHaveBeenCalled()
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('shows a refused removal under its card, and keeps the card', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.removeLink).mockRejectedValue(apiError(409, 'A write to the library is under way for this work; try again in a moment.'))
      const { user } = renderPage('Unattached')
      const card = await cardOf('120. Landing Stripling (1962) [old cut]')

      // Act
      await user.click(within(card).getByRole('button', { name: 'Remove' }))
      await user.click(within(await screen.findByRole('dialog', { name: 'Remove the link?' })).getByRole('button', { name: 'Remove' }))

      // Assert
      expect(await within(card).findByRole('alert')).toHaveTextContent('A write to the library is under way for this work; try again in a moment.')
      expect(card).toBeInTheDocument()
    })

    it('asks the server for the next page', async () => {
      // Arrange
      serveLists(async (query) => linksPage(query, [goneRow], 40))
      const { user } = renderPage('Unattached')
      await cardOf('120. Landing Stripling (1962) [old cut]')

      // Act
      await user.click(screen.getByRole('button', { name: 'Next page' }))

      // Assert
      expect(listAsks().at(-1)).toEqual({ filter: 'Unattached', page: 2, pageSize: 25 })
    })

    it('says when every link\'s item is still in the library', async () => {
      // Arrange
      serveLists(async (query) => linksPage(query, []))

      // Act
      renderPage('Unattached')

      // Assert
      expect(await screen.findByText('Every link\'s item is still in the library.')).toBeInTheDocument()
    })
  })
})
