import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LibrarySyncPanel } from '@/components/library/LibrarySyncPanel'
import { libraryApi } from '@/lib/library-api'
import type { CurrentUser, LibrarySource } from '@/lib/types'
import { openSourceDetails } from '@/test/sources-details'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { homeJellyfinId, runOf, sourceOf, sourcesOf } from '@/test/library'

vi.mock('@/lib/library-api')

async function renderPanel(signedIn: CurrentUser = owner, { details = true } = {}) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibrarySyncPanel />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  const user = userEvent.setup()
  if (details) await openSourceDetails(user)
  return { client, user }
}

/** The sources answer every request with `sources`. */
function serve(...sources: LibrarySource[]) {
  vi.mocked(libraryApi.sources).mockResolvedValue(sourcesOf(...sources))
}

/** The row of the source named `name`. */
const rowOf = (name: string) => screen.getByText(name).closest('li')!

/** The status line of the source named `name`. */
const statusOf = (name: string) => within(rowOf(name)).getByRole('status')

/** The status line of the source named `name`, once the sources have arrived. */
const findStatusOf = async (name: string) => within((await screen.findByText(name)).closest('li')!).getByRole('status')

/** MAM-47 — an example of a path mapper's sentence code: no mapping covers the file. */
const notCoveredCode = 'integrations.path-mapper.not-covered'

const failedRun = runOf({
  id: '5e000000-0000-4000-8000-000000000002',
  status: 'Failed',
  finishedUtc: '2026-09-30T09:00:00Z',
  itemsRead: 0,
  itemsTotal: null,
  worksCount: null,
  versionsCount: null,
  filesCount: null,
  error: 'Jellyfin could not be reached at http://127.0.0.1:9. Check the address and that Jellyfin is running.',
})

describe('LibrarySyncPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    serve(sourceOf({}))
  })

  it('shows the last sync of each source with what it read and stored', async () => {
    // Arrange
    const sources = [sourceOf({}), sourceOf({ integrationId: 'other', name: 'Cartoons', latestRun: runOf({ itemsRead: 1, worksCount: 1, versionsCount: 1, filesCount: 1 }) })]
    serve(...sources)

    // Act
    await renderPanel()

    // Assert
    expect(await screen.findByRole('heading', { level: 2, name: 'Sources' })).toBeInTheDocument()
    await screen.findByText('Home Jellyfin')
    expect(statusOf('Home Jellyfin')).toHaveTextContent(
      /^Synced Sep 30, 2026, 11:00\sAM: read 870 items into 855 works, 870 versions, 870 files\.$/)
    expect(statusOf('Cartoons')).toHaveTextContent(/read 1 item into 1 work, 1 version, 1 file\.$/)
    expect(within(rowOf('Home Jellyfin')).getByText('Jellyfin')).toBeInTheDocument()
    expect(within(rowOf('Home Jellyfin')).queryByText('Disabled')).not.toBeInTheDocument()
  })

  it('counts the series a sync stored beside its works, one series or several', async () => {
    // Arrange — MAM-32: the items are movies, series and episodes; the works movies and episodes.
    const sources = [
      sourceOf({ latestRun: runOf({ itemsRead: 1_375, worksCount: 1_295, seriesCount: 9, versionsCount: 1_400, filesCount: 1_500 }) }),
      sourceOf({ integrationId: 'other', name: 'Cartoons', latestRun: runOf({ itemsRead: 3, worksCount: 2, seriesCount: 1, versionsCount: 2, filesCount: 2 }) }),
    ]
    serve(...sources)

    // Act
    await renderPanel()

    // Assert
    await screen.findByText('Home Jellyfin')
    expect(statusOf('Home Jellyfin')).toHaveTextContent(
      /^Synced Sep 30, 2026, 11:00\sAM: read 1,375 items into 1,295 works and 9 series, 1,400 versions, 1,500 files\.$/)
    expect(statusOf('Cartoons')).toHaveTextContent(/read 3 items into 2 works and 1 series, 2 versions, 2 files\.$/)
  })

  it('says a source was never synced', async () => {
    // Arrange
    serve(sourceOf({ latestRun: null, lastSucceededRun: null }))

    // Act
    await renderPanel()

    // Assert
    expect(await findStatusOf('Home Jellyfin')).toHaveTextContent('Never synced.')
  })

  it.each([
    ['queued', runOf({ status: 'Queued', startedUtc: null, finishedUtc: null, itemsRead: 0, itemsTotal: null }), 'Queued…'],
    ['started, before its first page', runOf({ status: 'Running', finishedUtc: null, itemsRead: 0, itemsTotal: null }), 'Syncing…'],
    ['reading', runOf({ status: 'Running', finishedUtc: null, itemsRead: 50, itemsTotal: 120 }), 'Syncing… 50 of 120 items'],
    ['reading a big library', runOf({ status: 'Running', finishedUtc: null, itemsRead: 1_200, itemsTotal: 12_345 }), 'Syncing… 1,200 of 12,345 items'],
  ])('shows a sync that is %s, and keeps Sync now disabled meanwhile', async (_, run, line) => {
    // Arrange
    serve(sourceOf({ latestRun: run }))

    // Act
    await renderPanel()

    // Assert
    expect(await findStatusOf('Home Jellyfin')).toHaveTextContent(line)
    const button = screen.getByRole('button', { name: 'Syncing…' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Home Jellyfin is already being synced.')
  })

  it('shows the owner why the last sync failed, and which sync the library still shows', async () => {
    // Arrange
    serve(sourceOf({ latestRun: failedRun }))

    // Act
    await renderPanel()

    // Assert
    const status = await findStatusOf('Home Jellyfin')
    expect(status).toHaveTextContent(
      /^Sync failed Sep 30, 2026, 12:00\sPM: Jellyfin could not be reached at http:\/\/127\.0\.0\.1:9\. Check the address/)
    expect(status).toHaveAttribute('data-status', 'Failed')
    expect(screen.getByText(/^Showing the library as synced Sep 30, 2026, 11:00\sAM\.$/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeEnabled()
  })

  it('tells everyone else only that the last sync failed', async () => {
    // Arrange — the API leaves the sentence out for anyone but the owner.
    serve(sourceOf({ latestRun: { ...failedRun, error: null }, lastSucceededRun: null }))

    // Act
    await renderPanel(member)

    // Assert
    expect(await findStatusOf('Home Jellyfin')).toHaveTextContent('The last sync failed. The owner can see why.')
    expect(screen.queryByText(/^Showing the library as synced/)).not.toBeInTheDocument()
    expect(screen.queryByText(/127\.0\.0\.1/)).not.toBeInTheDocument()
  })

  it('warns the owner about files without a path in this app, and links to the path mappings', async () => {
    // Arrange
    serve(sourceOf({
      filesWithoutAppPathCount: 3,
      pathProblem: 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.',
    }))

    // Act
    await renderPanel()

    // Assert
    const warning = (await screen.findByText(/files have no path in this app/)).closest('p')!
    expect(warning).toHaveTextContent(
      '3 files have no path in this app. No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv. Check the path mappings under Integrations.')
    expect(within(warning).getByRole('link', { name: 'Check the path mappings under Integrations.' }))
      .toHaveAttribute('href', '/settings/integrations')
  })

  it('counts a single file without a path in the singular', async () => {
    // Arrange
    serve(sourceOf({ filesWithoutAppPathCount: 1, pathProblem: null }))

    // Act
    await renderPanel()

    // Assert
    const warning = (await screen.findByText(/has no path in this app/)).closest('p')!
    expect(warning).toHaveTextContent('1 file has no path in this app. Check the path mappings under Integrations.')
  })

  it('asks everyone else to have the owner check the path mappings, without a link', async () => {
    // Arrange
    serve(sourceOf({ filesWithoutAppPathCount: 3, pathProblem: null }))

    // Act
    await renderPanel(member)

    // Assert
    expect(await screen.findByText('Some files have no path in this app. Ask the owner to check the path mappings.'))
      .toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  // MAM-47 — the server names each sentence's kind by its code, the same whatever file it names and
  // whatever language it is worded in.
  it('does not quote an unreadable file\'s problem of the same code as the path problem, though it names another file', async () => {
    // Arrange
    serve(sourceOf({
      filesWithoutAppPathCount: 2,
      pathProblem: 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.',
      pathProblemCode: notCoveredCode,
      inspection: {
        inspectedCount: 868,
        problemCount: 2,
        notInspectedCount: 0,
        problem: 'No path mapping of “Home Jellyfin” covers /srv/other/extras.mkv.',
        problemCode: notCoveredCode,
      },
    }))

    // Act
    await renderPanel()

    // Assert
    expect(await screen.findByText(/2 files have no path in this app/)).toHaveTextContent('/srv/other/untitled.mkv')
    expect(screen.getByText('2 files couldn\'t be read.').closest('p')).toHaveClass('library-source-warning')
    expect(screen.queryByText(/extras\.mkv/)).not.toBeInTheDocument()
  })

  it('quotes an unreadable file\'s problem whose code differs from the path problem\'s', async () => {
    // Arrange
    const problem = 'ffprobe couldn\'t read “/media/movies/Alien (1979).mkv”: Invalid data found when processing input.'
    serve(sourceOf({
      filesWithoutAppPathCount: 1,
      pathProblem: 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.',
      pathProblemCode: notCoveredCode,
      inspection: { inspectedCount: 868, problemCount: 2, notInspectedCount: 0, problem, problemCode: 'files.probe.unreadable' },
    }))

    // Act
    await renderPanel()

    // Assert
    const line = (await screen.findByText(`2 files couldn't be read: ${problem}`)).closest('p')!
    expect(line).toHaveClass('library-source-warning')
  })

  it('compares the words when the server sends no codes, quoting another file\'s problem as before', async () => {
    // Arrange — a server older than MAM-47 leaves the codes out.
    const problem = 'No path mapping of “Home Jellyfin” covers /srv/other/extras.mkv.'
    serve(sourceOf({
      filesWithoutAppPathCount: 2,
      pathProblem: 'No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.',
      pathProblemCode: undefined,
      inspection: { inspectedCount: 868, problemCount: 2, notInspectedCount: 0, problem },
    }))

    // Act
    await renderPanel()

    // Assert
    expect(await screen.findByText(`2 files couldn't be read: ${problem}`)).toBeInTheDocument()
  })

  it('starts a sync for the owner and shows it queued at once', async () => {
    // Arrange
    const queued = runOf({ id: '5e000000-0000-4000-8000-000000000003', status: 'Queued', startedUtc: null, finishedUtc: null, itemsRead: 0, itemsTotal: null })
    let answer: (run: typeof queued) => void = () => {}
    vi.mocked(libraryApi.startSync).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { user } = await renderPanel()
    await user.click(await screen.findByRole('button', { name: 'Sync now' }))
    const startingButton = screen.getByRole('button', { name: 'Starting…' })
    const disabledWhileStarting = (startingButton as HTMLButtonElement).disabled
    serve(sourceOf({ latestRun: queued }))

    // Act
    await act(async () => answer(queued))

    // Assert
    expect(await screen.findByText('Queued…')).toBeInTheDocument()
    expect(disabledWhileStarting).toBe(true)
    expect(libraryApi.startSync).toHaveBeenCalledExactlyOnceWith(homeJellyfinId)
    await vi.waitFor(() => expect(libraryApi.sources).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a refused start as the API sentence under its source', async () => {
    // Arrange
    vi.mocked(libraryApi.startSync).mockRejectedValue(apiError(409, '“Home Jellyfin” is already being synced.'))
    const { user } = await renderPanel()

    // Act
    await user.click(await screen.findByRole('button', { name: 'Sync now' }))

    // Assert
    const refusal = await within(rowOf('Home Jellyfin')).findByRole('alert')
    expect(refusal).toHaveTextContent('“Home Jellyfin” is already being synced.')
    // A red status line like a failed run's, not a banner above the list.
    expect(refusal).toHaveClass('library-source-status')
    expect(refusal).toHaveAttribute('data-status', 'Failed')
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeEnabled()
  })

  it('drops a refused start once the run it was refused against has moved on', async () => {
    // Arrange — Sync now while a sync this page hasn't seen yet runs elsewhere: the refusal's refetch
    // brings that sync, and the sentence stays while it runs.
    const elsewhere = runOf({ id: '5e000000-0000-4000-8000-000000000004', status: 'Running', finishedUtc: null, itemsRead: 50 })
    vi.mocked(libraryApi.startSync).mockImplementation(async () => {
      serve(sourceOf({ latestRun: elsewhere }))
      throw apiError(409, '“Home Jellyfin” is already being synced.')
    })
    const { client, user } = await renderPanel()
    await user.click(await screen.findByRole('button', { name: 'Sync now' }))
    await within(rowOf('Home Jellyfin')).findByRole('alert')
    await client.refetchQueries()
    const refusalWhileRunning = within(rowOf('Home Jellyfin')).queryByRole('alert')?.textContent
    serve(sourceOf({ latestRun: { ...elsewhere, status: 'Succeeded', finishedUtc: '2026-09-30T09:00:00Z', itemsRead: 870 } }))

    // Act
    await client.refetchQueries()

    // Assert
    await vi.waitFor(() => expect(statusOf('Home Jellyfin')).toHaveTextContent(/^Synced /))
    expect(refusalWhileRunning).toBe('“Home Jellyfin” is already being synced.')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('names the failure when a refused start carries no sentence', async () => {
    // Arrange — a bare 404: the integration was removed meanwhile.
    vi.mocked(libraryApi.startSync).mockRejectedValue(apiError(404))
    const { user } = await renderPanel()

    // Act
    await user.click(await screen.findByRole('button', { name: 'Sync now' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not start the sync.')
  })

  it('keeps Sync now disabled for anyone but the owner, and says why', async () => {
    // Arrange
    const { user } = await renderPanel(member)
    const button = await screen.findByRole('button', { name: 'Sync now' })

    // Act
    await user.click(button)

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'Only the owner can sync the library.')
    expect(libraryApi.startSync).not.toHaveBeenCalled()
  })

  it.each([
    ['switched off', { isEnabled: false }, 'Home Jellyfin is switched off. Switch it on under Integrations to sync it.', 'Disabled'],
    ['of a type this version can\'t read', { canReadLibrary: false, adapterDisplayName: null }, 'This version of the app can\'t read a library from Home Jellyfin.', 'Can\'t be read'],
  ])('keeps Sync now of a source %s disabled for the owner, with a badge and the reason', async (_, changes, reason, badge) => {
    // Arrange
    serve(sourceOf(changes))

    // Act
    await renderPanel()

    // Assert
    const button = await screen.findByRole('button', { name: 'Sync now' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', reason)
    expect(within(rowOf('Home Jellyfin')).getByText(badge)).toHaveClass('ui-badge')
  })

  it('points the owner to Integrations when no media library is connected', async () => {
    // Arrange
    serve()

    // Act
    await renderPanel()

    // Assert
    expect(await screen.findByText('No media library is connected yet. Add Jellyfin under Integrations, then sync it here.'))
      .toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open Integrations' })).toHaveAttribute('href', '/settings/integrations')
  })

  it('tells everyone else that the owner connects the media library', async () => {
    // Arrange
    serve()

    // Act
    await renderPanel(member)

    // Assert
    expect(await screen.findByText('No media library is connected yet. The owner connects one under Integrations.'))
      .toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows why the sources could not be loaded in place of the list, and Try again loads them', async () => {
    // Arrange
    vi.mocked(libraryApi.sources).mockRejectedValueOnce(apiError(500, 'Database is locked.'))
    const { user } = await renderPanel(owner, { details: false })
    const state = await screen.findByRole('alert')

    // Act
    const syncNowBeforeRetry = screen.queryByRole('button', { name: 'Sync now' })
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the media libraries.')
    expect(state).toHaveTextContent('Database is locked.')
    expect(syncNowBeforeRetry).not.toBeInTheDocument()
    await openSourceDetails(user)
    expect(await screen.findByRole('button', { name: 'Sync now' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the sources on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = await renderPanel()
    await screen.findByText('Home Jellyfin')
    vi.mocked(libraryApi.sources).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last known sync status.')
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(statusOf('Home Jellyfin')).toHaveTextContent(/^Synced /)
  })

  it('asks again from Retry on the refresh banner, and drops the banner once the sources are back', async () => {
    // Arrange
    const { client, user } = await renderPanel()
    await screen.findByText('Home Jellyfin')
    vi.mocked(libraryApi.sources).mockRejectedValueOnce(networkError())
    await client.refetchQueries()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(statusOf('Home Jellyfin')).toHaveTextContent(/^Synced /)
  })
})
