import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AppErrorBoundary } from '@/components/AppErrorBoundary'

/** Throws while `failure.error` is set; clearing it lets the next render succeed. */
const failure: { error: unknown } = { error: null }

function Page() {
  if (failure.error) throw failure.error
  return <p>Page content</p>
}

const chunkError = () => new TypeError('Failed to fetch dynamically imported module: /assets/HomePage-abc.js')

describe('AppErrorBoundary', () => {
  let reload: ReturnType<typeof vi.fn>

  beforeEach(() => {
    failure.error = null
    sessionStorage.clear()
    reload = vi.fn()
    vi.stubGlobal('location', { reload })
    // React reports every caught render error to the console; the boundary adds its own line.
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('shows the fallback for a render crash, and Try again renders the page again', async () => {
    // Arrange
    const user = userEvent.setup()
    failure.error = new Error('boom')
    render(<AppErrorBoundary><Page /></AppErrorBoundary>)
    const fallbackTitle = screen.getByRole('heading', { name: 'Something went wrong' }).textContent
    failure.error = null

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(fallbackTitle).toBe('Something went wrong')
    expect(screen.getByText('Page content')).toBeInTheDocument()
    expect(reload).not.toHaveBeenCalled()
  })

  it('reloads once for a missing chunk and says so', () => {
    // Arrange
    failure.error = chunkError()

    // Act
    render(<AppErrorBoundary><Page /></AppErrorBoundary>)

    // Assert
    expect(reload).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('heading', { name: 'Loading the new version…' })).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('stops reloading when the chunk is still missing after the reload', () => {
    // Arrange — the first render stands for the document before the reload, the second for the
    // reloaded one, which shares the tab's session storage.
    failure.error = chunkError()
    const first = render(<AppErrorBoundary><Page /></AppErrorBoundary>)
    first.unmount()
    reload.mockClear()

    // Act
    render(<AppErrorBoundary><Page /></AppErrorBoundary>)

    // Assert
    expect(reload).not.toHaveBeenCalled()
    expect(screen.getByRole('heading', { name: 'Could not load this page' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload the page' })).toBeInTheDocument()
  })

  it('clears a caught error when resetKey changes', () => {
    // Arrange
    failure.error = new Error('boom')
    const { rerender } = render(<AppErrorBoundary resetKey="/broken"><Page /></AppErrorBoundary>)
    const fallbackShown = screen.queryByRole('alert') !== null
    failure.error = null

    // Act
    rerender(<AppErrorBoundary resetKey="/working"><Page /></AppErrorBoundary>)

    // Assert
    expect(fallbackShown).toBe(true)
    expect(screen.getByText('Page content')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps healthy children mounted when resetKey changes', () => {
    // Arrange
    const { rerender } = render(<AppErrorBoundary resetKey="/a"><input aria-label="Search" /></AppErrorBoundary>)
    const input = screen.getByRole('textbox', { name: 'Search' })

    // Act
    rerender(<AppErrorBoundary resetKey="/b"><input aria-label="Search" /></AppErrorBoundary>)

    // Assert
    expect(screen.getByRole('textbox', { name: 'Search' })).toBe(input)
  })
})
