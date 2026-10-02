import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import { downloadsApi } from '@/lib/downloads-api'
import { useDownloadJobFiles } from '@/lib/queries'
import type { DownloadJobFileList, DownloadJobFilesQuery } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { apiError, networkError } from '@/test/api-error'
import { downloadedJob, downloadingJob, fileOf, filesPage } from '@/test/downloads'

vi.mock('@/lib/downloads-api')

const FIRST_PAGE: DownloadJobFilesQuery = { page: 1, pageSize: 25 }
const SECOND_PAGE: DownloadJobFilesQuery = { page: 2, pageSize: 25 }

/**
 * A job's files and the load-retry state over them, the way the job's page reads both. The files'
 * query is the app's own: it is asked again every 10 s exactly while it is in error, which is the
 * case the hook exists for besides Try again.
 */
function renderFiles(jobId: string = downloadingJob.id, query: DownloadJobFilesQuery = FIRST_PAGE) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return renderHook(
    (props: { jobId: string; query: DownloadJobFilesQuery }) => {
      const files = useDownloadJobFiles(props.jobId, props.query)
      return { files, load: useLoadRetry(files) }
    },
    {
      initialProps: { jobId, query },
      wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
    },
  )
}

/** Holds the next request for files unanswered, until the test answers or fails it. */
function holdFiles() {
  let answer!: (page: DownloadJobFileList) => void
  let fail!: (error: unknown) => void
  vi.mocked(downloadsApi.files).mockReturnValueOnce(new Promise<DownloadJobFileList>((resolve, reject) => {
    answer = resolve
    fail = reject
  }))
  return { answer, fail }
}

describe('useLoadRetry', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(downloadsApi.files).mockImplementation(async (_, query) => filesPage(query, [fileOf({})]))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('keeps the failed first load and reports retrying while Try again is pending', async () => {
    // Arrange
    const failure = networkError()
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(failure)
    const { result } = renderFiles()
    await waitFor(() => expect(result.current.load.error).toBe(failure))
    holdFiles()

    // Act
    act(() => result.current.load.retry())
    await waitFor(() => expect(result.current.files.isFetching).toBe(true))

    // Assert
    // TanStack has put the query back to loading with no error: on its own, the page would show
    // "Loading…" in place of the ErrorState.
    expect(result.current.files.isLoading).toBe(true)
    expect(result.current.files.error).toBeNull()
    expect(result.current.load.error).toBe(failure)
    expect(result.current.load.retrying).toBe(true)
    expect(downloadsApi.files).toHaveBeenCalledTimes(2)
  })

  it('drops the failure and stops retrying once the retry brings the files', async () => {
    // Arrange
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
    const { result } = renderFiles()
    await waitFor(() => expect(result.current.load.error).not.toBeNull())
    const retried = holdFiles()
    act(() => result.current.load.retry())

    // Act
    retried.answer(filesPage(FIRST_PAGE, [fileOf({})]))
    await waitFor(() => expect(result.current.load.retrying).toBe(false))

    // Assert
    expect(result.current.files.data?.totalCount).toBe(1)
    expect(result.current.load.error).toBeNull()
  })

  it('shows the new failure once the retry fails again', async () => {
    // Arrange
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
    const { result } = renderFiles()
    await waitFor(() => expect(result.current.load.error).not.toBeNull())
    const retried = holdFiles()
    act(() => result.current.load.retry())
    const failure = apiError(500, 'The database is locked. Try again in a moment.')

    // Act
    retried.fail(failure)
    await waitFor(() => expect(result.current.load.retrying).toBe(false))

    // Assert
    expect(result.current.load.error).toBe(failure)
    expect(result.current.files.error).toBe(failure)
  })

  it('keeps the failure through a poll of a query in error, without reporting retrying', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const failure = networkError()
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(failure)
    const { result } = renderFiles()
    await waitFor(() => expect(result.current.load.error).toBe(failure))
    holdFiles()

    // Act
    await act(() => vi.advanceTimersByTimeAsync(10_000))
    await waitFor(() => expect(result.current.files.isFetching).toBe(true))

    // Assert
    expect(downloadsApi.files).toHaveBeenCalledTimes(2)
    expect(result.current.files.isLoading).toBe(true)
    expect(result.current.load.error).toBe(failure)
    // Nobody pressed Try again: the button keeps its label through the poll.
    expect(result.current.load.retrying).toBe(false)
  })

  it('leaves a failed refresh to the banner over the files on screen', async () => {
    // Arrange
    const { result } = renderFiles()
    await waitFor(() => expect(result.current.files.data).toBeDefined())
    const failure = networkError()
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(failure)

    // Act
    await act(async () => { await result.current.files.refetch() })
    await waitFor(() => expect(result.current.files.error).toBe(failure))

    // Assert
    expect(result.current.files.data?.totalCount).toBe(1)
    expect(result.current.load.error).toBeNull()
    expect(result.current.load.retrying).toBe(false)
  })

  it('keeps the failure of the page asked for while the last page\'s files stand in for it', async () => {
    // Arrange
    const { result, rerender } = renderFiles()
    await waitFor(() => expect(result.current.files.data).toBeDefined())
    const failure = networkError()
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(failure)
    rerender({ jobId: downloadingJob.id, query: SECOND_PAGE })
    await waitFor(() => expect(result.current.load.error).toBe(failure))
    holdFiles()

    // Act
    act(() => result.current.load.retry())
    await waitFor(() => expect(result.current.files.isFetching).toBe(true))

    // Assert
    // keepPreviousData puts the first page back on screen while the second is asked again.
    expect(result.current.files.isPlaceholderData).toBe(true)
    expect(result.current.files.data?.page).toBe(1)
    expect(result.current.load.error).toBe(failure)
    expect(result.current.load.retrying).toBe(true)
  })

  it('starts another job\'s files at loading, without the last job\'s failure', async () => {
    // Arrange
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
    const { result, rerender } = renderFiles(downloadingJob.id)
    await waitFor(() => expect(result.current.load.error).not.toBeNull())
    holdFiles()

    // Act
    rerender({ jobId: downloadedJob.id, query: FIRST_PAGE })
    await waitFor(() => expect(downloadsApi.files).toHaveBeenLastCalledWith(downloadedJob.id, FIRST_PAGE))

    // Assert
    expect(result.current.files.isLoading).toBe(true)
    expect(result.current.load.error).toBeNull()
    expect(result.current.load.retrying).toBe(false)
  })

  it('does not carry Retrying… over to another job whose files fail mid-retry', async () => {
    // Arrange
    // Only the clock is faked, so each job's failure is reported at a time of its own.
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-02T10:00:00Z'))
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(networkError())
    const { result, rerender } = renderFiles(downloadingJob.id)
    await waitFor(() => expect(result.current.load.error).not.toBeNull())
    holdFiles()
    act(() => result.current.load.retry())
    await waitFor(() => expect(result.current.load.retrying).toBe(true))
    vi.setSystemTime(new Date('2026-10-02T10:00:05Z'))
    const failure = networkError()
    vi.mocked(downloadsApi.files).mockRejectedValueOnce(failure)

    // Act
    rerender({ jobId: downloadedJob.id, query: FIRST_PAGE })
    await waitFor(() => expect(result.current.load.error).toBe(failure))

    // Assert
    // The first job's retry is still unanswered; the second job's button reads Try again.
    expect(result.current.load.retrying).toBe(false)
  })
})
