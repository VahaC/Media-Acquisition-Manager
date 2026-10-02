import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, getDefaultNormalizer, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { formatDateTime } from '@/lib/format'
import type { CurrentUser, DownloadJob, DownloadJobFile } from '@/lib/types'
import { JobDetailsPage } from '@/pages/JobDetailsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, fileOf, filesPage, jobOf, sentMinutesAgo } from '@/test/downloads'

vi.mock('@/lib/downloads-api')

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

const filmFile = fileOf({})

/** A sample the client was told to skip; two spaces in its name, as qBittorrent names it. */
const sampleFile = fileOf({
  position: 1,
  path: 'Interstellar.2014.2160p.UHD.BluRay.Remux/Sample/Interstellar  sample.mkv',
  sizeBytes: 52_428_800,
  progress: 0,
  isWanted: false,
})

/** The job's facts as `[term, value]` pairs. */
function facts(): [string, string][] {
  const terms = [...screen.getByRole('region', { name: 'Details' }).querySelectorAll('dt')]
  return terms.map((term) => [term.textContent!, term.nextElementSibling!.textContent!])
}

/** The server answers `job`, and `files` (out of `total`) for every page asked for. */
function serve(job: DownloadJob, files: DownloadJobFile[] = [filmFile, sampleFile], total = files.length) {
  vi.mocked(downloadsApi.get).mockResolvedValue(job)
  vi.mocked(downloadsApi.files).mockImplementation(async (_, query) => filesPage(query, files, total))
}

/** The files table's row of the file at `path`, its spaces matched as they are. */
const fileRowOf = (path: string) => screen
  .getByText(path, { selector: '.jobs-file-path', normalizer: getDefaultNormalizer({ trim: false, collapseWhitespace: false }) })
  .closest('tr')!

/** Waits for the files table, once the files have arrived. */
const filesTable = async () => within(await screen.findByRole('region', { name: 'Files' })).findByRole('table')

function renderAt(path: string, signedIn: CurrentUser = member) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs" element={<p>Jobs list</p>} />
            <Route path="jobs/:id" element={<JobDetailsPage />} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

describe('JobDetailsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    serve(downloadingJob)
  })

  it('shows the job\'s release, where it went, where it stands, how far it got, when, and the client\'s id', async () => {
    // Arrange
    const path = `/jobs/${downloadingJob.id}`

    // Act
    renderAt(path)

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
    expect(downloadsApi.get).toHaveBeenCalledExactlyOnceWith(downloadingJob.id)
    expect(facts()).toEqual([
      ['Source', 'Toloka'],
      ['Download client', 'Local qBittorrent'],
      ['Destination', 'Movies — category “Movies”'],
      ['Status', 'Downloading'],
      ['Progress', '45.6%'],
      ['Sent', formatDateTime(downloadingJob.createdUtc, INSTANT_FORMAT)],
      ['Status changed', formatDateTime(downloadingJob.statusChangedUtc, INSTANT_FORMAT)],
      ['Client\'s id', downloadingJob.clientJobId!],
    ])
  })

  it('names what isn\'t known: no progress yet, no id from the client yet, and a destination without a name', async () => {
    // Arrange
    serve(jobOf({ progress: null, clientJobId: null, destinationName: null }))

    // Act
    renderAt(`/jobs/${downloadingJob.id}`)

    // Assert
    await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })
    expect(facts()).toEqual(expect.arrayContaining([
      ['Destination', 'No destination — category “Movies”'],
      ['Progress', 'Not known yet'],
      ['Client\'s id', 'Not listed by the client yet'],
    ]))
  })

  it('shows the block reason and the error under the status', async () => {
    // Arrange
    const blockReason = '“Local qBittorrent” no longer lists this torrent. Cancel the job to stop tracking it.'
    serve(jobOf({ blockReason }))

    // Act
    renderAt(`/jobs/${downloadingJob.id}`)

    // Assert
    expect((await screen.findByText(blockReason)).closest('p')).toHaveClass('library-source-warning')
  })

  it('lists the job\'s files by position, with their size and progress, the path exactly as the client names it', async () => {
    // Arrange
    const path = `/jobs/${downloadingJob.id}`

    // Act
    renderAt(path)

    // Assert
    const files = await filesTable()
    expect(downloadsApi.files).toHaveBeenLastCalledWith(downloadingJob.id, { page: 1, pageSize: 25 })
    expect(within(files).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['File', 'Size', 'Progress'])
    expect([...files.querySelectorAll('.jobs-file-path')].map((cell) => cell.textContent)).toEqual([filmFile.path, sampleFile.path])
    const film = within(fileRowOf(filmFile.path))
    expect(film.getByText('80 GiB')).toBeInTheDocument()
    expect(film.getByRole('progressbar', { name: 'Download progress' })).toHaveAttribute('value', '0.4567')
    expect(film.getByText('45.6%')).toBeInTheDocument()
  })

  it('marks a file the client skips as Skipped, with no progress', async () => {
    // Arrange
    const path = `/jobs/${downloadingJob.id}`

    // Act
    renderAt(path)

    // Assert
    await filesTable()
    const sample = within(fileRowOf(sampleFile.path))
    expect(sample.getByText('Skipped')).toHaveClass('ui-badge')
    expect(sample.getByText('Skipped')).toHaveAttribute('title', 'Skipped in the download client: it isn\'t downloaded.')
    expect(sample.queryByRole('progressbar')).not.toBeInTheDocument()
    expect(sample.getByText('50 MiB')).toBeInTheDocument()
  })

  it('says a file\'s size isn\'t known when the client didn\'t give it', async () => {
    // Arrange
    serve(downloadingJob, [fileOf({ sizeBytes: null, progress: null, isWanted: null })])

    // Act
    renderAt(`/jobs/${downloadingJob.id}`)

    // Assert
    await filesTable()
    const row = within(fileRowOf(filmFile.path))
    expect(row.getByText('Size unknown')).toBeInTheDocument()
    expect(row.getByText('Not known yet')).toBeInTheDocument()
  })

  it('says the client hasn\'t listed the files yet when there are none', async () => {
    // Arrange
    serve(downloadingJob, [])

    // Act
    renderAt(`/jobs/${downloadingJob.id}`)

    // Assert
    expect(await screen.findByText(
      'The download client hasn\'t listed this download\'s files yet. A magnet link lists them once its metadata has arrived.',
    )).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Files' })).queryByRole('table')).not.toBeInTheDocument()
  })

  it('pages through the files and keeps their page size per device', async () => {
    // Arrange
    serve(downloadingJob, [filmFile], 60)
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    await screen.findByText('1–25 of 60')

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–50 of 60')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument()
    expect(downloadsApi.files).toHaveBeenLastCalledWith(downloadingJob.id, { page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:jobFiles:pageSize')).toBe('50')
  })

  it('shows the server sentence in place of the files when they are refused, with no Try again', async () => {
    // Arrange
    vi.mocked(downloadsApi.files).mockRejectedValue(apiError(400, 'The page must be 1 or more.'))

    // Act
    renderAt(`/jobs/${downloadingJob.id}`)

    // Assert
    const files = within(await screen.findByRole('region', { name: 'Files' }))
    const state = await files.findByRole('alert')
    expect(state).toHaveTextContent('Could not load this job\'s files.')
    expect(state).toHaveTextContent('The page must be 1 or more.')
    expect(files.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('says the files could not be loaded while the facts stay, and Try again loads them', async () => {
    // Arrange
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    const files = within(await screen.findByRole('region', { name: 'Files' }))
    await files.findByRole('alert')

    // Act
    await user.click(files.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(await within(await filesTable()).findByText(filmFile.path, { selector: '.jobs-file-path' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('says there is no job of theirs for %s when the server answers a bare 404, asking nothing about files', async (_, signedIn) => {
    // Arrange
    vi.mocked(downloadsApi.get).mockRejectedValue(apiError(404))

    // Act
    renderAt(`/jobs/${downloadingJob.id}`, signedIn)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'There is no download job of yours at this address. It may belong to someone else, or the link is wrong.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(downloadsApi.files).not.toHaveBeenCalled()
    expect(screen.getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/jobs')
  })

  it.each(['not-a-job', '..%2Fsettings', 'd0b10000-0000-4000-8000-00000000000'])(
    'words a malformed id (%s) like a missing job, without asking the server',
    async (id) => {
      // Arrange
      const path = `/jobs/${id}`

      // Act
      renderAt(path)

      // Assert
      expect(await screen.findByRole('alert')).toHaveTextContent('There is no download job of yours at this address.')
      expect(downloadsApi.get).not.toHaveBeenCalled()
      expect(downloadsApi.files).not.toHaveBeenCalled()
    },
  )

  it('says the job could not be loaded for another reason, with the server sentence, and Try again shows it', async () => {
    // Arrange
    vi.mocked(downloadsApi.get).mockRejectedValueOnce(networkError())
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load this download job.')
    expect(state).toHaveTextContent('The server could not be reached.')
    expect(await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
  })

  it('goes back to the list from the Jobs link', async () => {
    // Arrange
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })

    // Act
    await user.click(screen.getByRole('link', { name: 'Jobs' }))

    // Assert
    expect(await screen.findByText('Jobs list')).toBeInTheDocument()
  })

  it('cancels the job after a confirmation and shows it cancelled, with Cancel gone', async () => {
    // Arrange
    const cancelled = { ...downloadingJob, status: 'Cancelled' as const }
    vi.mocked(downloadsApi.cancel).mockResolvedValue(cancelled)
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    await user.click(await screen.findByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    vi.mocked(downloadsApi.get).mockResolvedValue(cancelled)

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(downloadsApi.cancel).toHaveBeenCalledExactlyOnceWith(downloadingJob.id)
    expect(await screen.findByText('Cancelled')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
  })

  it('shows the server sentence when the cancel is refused', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(
      apiError(409, 'This download is already downloaded, so there\'s nothing to stop following.'))
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    await user.click(await screen.findByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(await screen.findByRole('alert'))
      .toHaveTextContent('This download is already downloaded, so there\'s nothing to stop following.')
  })

  it('takes a refused cancel\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(apiError(500))
    const { user } = renderAt(`/jobs/${downloadingJob.id}`)
    await user.click(await screen.findByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
  })

  it('offers no Cancel for a job that has ended', async () => {
    // Arrange
    serve(downloadedJob)

    // Act
    renderAt(`/jobs/${downloadedJob.id}`)

    // Assert
    await screen.findByRole('heading', { level: 1, name: downloadedJob.releaseTitle })
    expect(screen.queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
  })

  it.each<[string, string, number, boolean]>([
    ['offers no Cancel', 'a minute', 1, false],
    ['offers Cancel', '6 minutes', 6, true],
  ])('%s on a Created job sent %s ago: under 5 minutes old, it may still be being sent', async (_, __, minutes, offered) => {
    // Arrange
    serve(jobOf({ status: 'Created', progress: null, clientJobId: null, createdUtc: sentMinutesAgo(minutes) }))

    // Act
    renderAt(`/jobs/${downloadingJob.id}`)

    // Assert
    await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })
    expect(screen.queryByRole('button', { name: 'Cancel job' }) !== null).toBe(offered)
  })

  describe('while the job is followed', () => {
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    /** Lets the polling interval pass once, with the answers it brings. */
    const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(10_000))

    it('asks for the job every 10 s, its files again when it moved, and nothing more once it is downloaded', async () => {
      // Arrange
      renderAt(`/jobs/${downloadingJob.id}`)
      await filesTable()
      serve({ ...downloadingJob, status: 'Downloaded', progress: 1 }, [{ ...filmFile, progress: 1 }, sampleFile])

      // Act
      await nextPoll()
      await screen.findByText('Downloaded')
      await nextPoll()
      await nextPoll()

      // Assert
      expect(downloadsApi.get).toHaveBeenCalledTimes(2)
      expect(downloadsApi.files).toHaveBeenCalledTimes(2)
    })

    it('leaves the files be while the job comes back unchanged, and asks for them again once its progress moves', async () => {
      // Arrange
      renderAt(`/jobs/${downloadingJob.id}`)
      await filesTable()

      // Act
      await nextPoll()
      await nextPoll()
      const unchanged = vi.mocked(downloadsApi.files).mock.calls.length
      serve({ ...downloadingJob, progress: 0.5 }, [{ ...filmFile, progress: 0.5 }, sampleFile])
      await nextPoll()

      // Assert
      expect(downloadsApi.get).toHaveBeenCalledTimes(4)
      expect(unchanged).toBe(1)
      expect(await within(fileRowOf(filmFile.path)).findByText('50%')).toBeInTheDocument()
      expect(downloadsApi.files).toHaveBeenCalledTimes(2)
    })

    it('asks for the files again while a followed job lists none, even when the job comes back unchanged', async () => {
      // Arrange
      serve(downloadingJob, [])
      renderAt(`/jobs/${downloadingJob.id}`)
      await screen.findByText(/hasn't listed this download's files yet/)
      serve(downloadingJob, [filmFile])

      // Act
      await nextPoll()

      // Assert
      expect(await screen.findByText(filmFile.path, { selector: '.jobs-file-path' })).toBeInTheDocument()
      expect(downloadsApi.files).toHaveBeenCalledTimes(2)
    })

    it('asks nothing more for a job that has ended', async () => {
      // Arrange
      serve(downloadedJob)
      renderAt(`/jobs/${downloadedJob.id}`)
      await filesTable()

      // Act
      await nextPoll()
      await nextPoll()

      // Assert
      expect(downloadsApi.get).toHaveBeenCalledOnce()
      expect(downloadsApi.files).toHaveBeenCalledOnce()
    })

    it('keeps the files\' failed load on screen through the poll that asks again, without reading Retrying…', async () => {
      // Arrange
      serve({ ...downloadingJob, status: 'Paused' })
      vi.mocked(downloadsApi.files)
        .mockRejectedValueOnce(networkError())
        .mockReturnValueOnce(new Promise(() => {}))
      renderAt(`/jobs/${downloadingJob.id}`)
      const files = within(await screen.findByRole('region', { name: 'Files' }))
      await files.findByRole('alert')

      // Act
      await nextPoll()

      // Assert
      expect(downloadsApi.files).toHaveBeenCalledTimes(2)
      expect(files.getByRole('alert')).toHaveTextContent('Could not load this job\'s files.')
      expect(files.getByRole('button', { name: 'Try again' })).toBeEnabled()
      expect(files.queryByText('Loading…')).not.toBeInTheDocument()
    })

    it('asks for the files again on the next poll after their load failed, while the job stays put, and shows them', async () => {
      // Arrange
      serve({ ...downloadingJob, status: 'Paused' })
      vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
      renderAt(`/jobs/${downloadingJob.id}`)
      await within(await screen.findByRole('region', { name: 'Files' })).findByRole('alert')

      // Act
      await nextPoll()

      // Assert
      expect(await within(await filesTable()).findByText(filmFile.path, { selector: '.jobs-file-path' })).toBeInTheDocument()
      expect(downloadsApi.files).toHaveBeenCalledTimes(2)
      expect(within(screen.getByRole('region', { name: 'Files' })).queryByRole('alert')).not.toBeInTheDocument()
    })

    it('asks for the files of a job that has ended again after their load failed, and nothing more once they came', async () => {
      // Arrange
      serve(downloadedJob)
      vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
      renderAt(`/jobs/${downloadedJob.id}`)
      await within(await screen.findByRole('region', { name: 'Files' })).findByRole('alert')

      // Act
      await nextPoll()
      await filesTable()
      await nextPoll()
      await nextPoll()

      // Assert
      expect(downloadsApi.get).toHaveBeenCalledOnce()
      expect(downloadsApi.files).toHaveBeenCalledTimes(2)
    })

    it('keeps the facts on screen under a banner when a refresh fails', async () => {
      // Arrange
      renderAt(`/jobs/${downloadingJob.id}`)
      await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })
      vi.mocked(downloadsApi.get).mockRejectedValue(networkError())

      // Act
      await nextPoll()

      // Assert
      expect(await screen.findByText(/Refresh failed, showing what was last loaded\./)).toBeInTheDocument()
      expect(screen.getByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
    })

    it('asks for the job again from Retry on the refresh banner, and drops the banner once it is back', async () => {
      // Arrange
      const { user } = renderAt(`/jobs/${downloadingJob.id}`)
      await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })
      vi.mocked(downloadsApi.get).mockRejectedValueOnce(networkError())
      await nextPoll()
      await screen.findByRole('alert')

      // Act
      await user.click(screen.getByRole('button', { name: 'Retry' }))

      // Assert
      await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
      expect(screen.getByRole('heading', { level: 1, name: downloadingJob.releaseTitle })).toBeInTheDocument()
    })

    it('says the job is gone, with no Retry, when a refresh answers a bare 404', async () => {
      // Arrange
      renderAt(`/jobs/${downloadingJob.id}`)
      await screen.findByRole('heading', { level: 1, name: downloadingJob.releaseTitle })
      vi.mocked(downloadsApi.get).mockRejectedValue(apiError(404))

      // Act
      await nextPoll()

      // Assert
      expect(await screen.findByRole('alert')).toHaveTextContent('There is no download job of yours at this address.')
      expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    })
  })
})
