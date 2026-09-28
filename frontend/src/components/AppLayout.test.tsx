import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/AppLayout'
import { owner, resetAuthStore, signInAs } from '@/test/auth-session'

function CrashingPage(): never {
  throw new Error('boom')
}

function renderAt(path: string) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<p>Home content</p>} />
            <Route path="crash" element={<CrashingPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AppLayout', () => {
  beforeEach(() => {
    resetAuthStore()
    // React reports every caught render error to the console; the boundary adds its own line.
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows a crashed page\'s fallback inside the content column, under the header', () => {
    // Arrange
    const path = '/crash'

    // Act
    renderAt(path)

    // Assert
    expect(within(screen.getByRole('main')).getByRole('heading', { name: 'Something went wrong' }))
      .toBeInTheDocument()
    expect(screen.getByRole('banner')).toContainElement(
      screen.getByRole('link', { name: 'Media Acquisition Manager' }),
    )
  })

  it('leaves the crashed page through the header link', async () => {
    // Arrange
    const user = userEvent.setup()
    renderAt('/crash')

    // Act
    await user.click(screen.getByRole('link', { name: 'Media Acquisition Manager' }))

    // Assert
    expect(screen.getByText('Home content')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Something went wrong' })).not.toBeInTheDocument()
  })

  it('shows the user menu in the header once someone is signed in', () => {
    // Arrange
    signInAs(owner)

    // Act
    renderAt('/')

    // Assert
    expect(within(screen.getByRole('banner')).getByRole('button', { name: 'Signed in as vasyl' }))
      .toBeInTheDocument()
  })

  it('has no user menu before anyone is signed in', () => {
    // Arrange
    const path = '/'

    // Act
    renderAt(path)

    // Assert
    expect(within(screen.getByRole('banner')).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Home content')).toBeInTheDocument()
  })
})
