import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TmdbLinkEditor } from '@/components/library/TmdbLinkEditor'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { LibraryLinkDialog, LibraryLinkItem } from '@/lib/types'
import { dialogOf, itemOf, linkedDialogOf, linkedItemOf, lockedSentence, noEpisodeImdbReason, switchedOffReason } from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — what a save left, as the editor tells the surface around it: TmdbLinkDialog closes on `done`
// and stays on `kept`, so the owner reads a reason the item didn't show before the click.
// MAM-47 — that reason is told from the one the item showed by its code when the server sends codes: the
// server words its sentences in the page's language, and a renamed library changes the words too.

/** Examples of the server's codes for an item's reasons. */
const switchedOffCode = 'library-links.writability.library-switched-off'
const noEpisodeImdbCode = 'library-links.writability.no-episode-imdb'
const lockedCode = 'library-links.write.locked'

/** The switched-off sentence once the owner renamed the library. */
const renamedSwitchedOffReason = '“Living-room Jellyfin” is switched off. Switch it on under Integrations, then retry.'

/** The short's item as the save answers it: kept in the app, for `changes`' reason. */
const keptItemOf = (changes: Partial<LibraryLinkItem>) =>
  linkedItemOf({ writeState: 'KeptInApp', canWrite: false, identifiedUtc: null, ...changes })

/** The editor on `before` with the film picked, the save to be answered with `after`. */
async function renderPicked(before: LibraryLinkDialog, after: LibraryLinkItem) {
  vi.mocked(libraryLinksApi.link).mockResolvedValue(linkedDialogOf({ items: [after] }))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const onSaved = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <TmdbLinkEditor
          dialog={before}
          formId="tmdb-link-pick"
          bodyClassName="settings-dialog-body"
          onSaved={onSaved}
          footer={(save) => <button type="submit" form={save.formId}>Save</button>}
        />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  const user = userEvent.setup()
  await user.click(screen.getByRole('radio', { name: 'Pick Посадка Стріплінга (1962)' }))
  return { onSaved, user }
}

describe('TmdbLinkEditor', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('calls a save done when the kept reason has the code the item showed before, though its words changed', async () => {
    // Arrange
    const before = dialogOf({ items: [itemOf({ canWrite: false, cannotWriteReason: switchedOffReason, cannotWriteReasonCode: switchedOffCode })] })
    const after = keptItemOf({ cannotWriteReason: renamedSwitchedOffReason, cannotWriteReasonCode: switchedOffCode })
    const { onSaved, user } = await renderPicked(before, after)

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.anything(), 'done'))
  })

  it('stays on an item kept in the app for a reason whose code the item didn\'t show before', async () => {
    // Arrange
    const locked = linkedItemOf({ writeState: 'NotWritten', writeError: lockedSentence, writeErrorCode: lockedCode, identifiedUtc: null })
    const after = keptItemOf({
      cannotWriteReason: noEpisodeImdbReason,
      cannotWriteReasonCode: noEpisodeImdbCode,
      writeError: lockedSentence,
      writeErrorCode: lockedCode,
    })
    const { onSaved, user } = await renderPicked(linkedDialogOf({ items: [locked] }), after)

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.anything(), 'kept'))
  })

  it('compares the words when the server sends no codes, as before MAM-47', async () => {
    // Arrange — a server older than MAM-47 leaves the codes out.
    const before = dialogOf({ items: [itemOf({ canWrite: false, cannotWriteReason: switchedOffReason, cannotWriteReasonCode: undefined })] })
    const after = keptItemOf({ cannotWriteReason: renamedSwitchedOffReason, cannotWriteReasonCode: undefined })
    const { onSaved, user } = await renderPicked(before, after)

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.anything(), 'kept'))
  })
})
