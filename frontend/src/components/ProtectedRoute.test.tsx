import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { refreshSession } from '@/lib/api'
import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { i18n } from '@/lib/i18n'
import { queryClient } from '@/lib/query-client'
import type { SignInResponse } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { owner, resetAuthStore, sessionFor, signInAs } from '@/test/auth-session'

vi.mock('@/lib/auth-api')

/** The sign-in route, saying which address the gate sent the visitor away from. */
function SignInProbe() {
  const state = useLocation().state as { from?: string } | null
  return <p>Sign-in page, then {state?.from ?? 'no page remembered'}</p>
}

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/login" element={<SignInProbe />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/settings/users" element={<p>Users page</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

/** A page with a half-typed field, which only a fresh mount empties. */
function renderFormPage() {
  render(
    <MemoryRouter initialEntries={['/profile']}>
      <Routes>
        <Route element={<ProtectedRoute />}>
          <Route path="/profile" element={<input aria-label="Draft" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
  return { user: userEvent.setup(), draft: () => screen.getByRole<HTMLInputElement>('textbox', { name: 'Draft' }) }
}

describe('ProtectedRoute', () => {
  beforeEach(() => {
    resetAuthStore()
    vi.mocked(authApi.refresh).mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
    queryClient.clear()
  })

  it('says it is restoring the session while it asks the server for one', async () => {
    // Arrange
    let answer!: (session: SignInResponse) => void
    vi.mocked(authApi.refresh).mockReturnValue(new Promise<SignInResponse>((resolve) => { answer = resolve }))

    // Act
    renderAt('/settings/users')
    const restoringShown = screen.queryByText('Restoring your session…') !== null
    const pageShownWhileRestoring = screen.queryByText('Users page') !== null
    // Answered before the test ends: the refresh is single-flight for the whole module, and one
    // left pending would be shared by every later test.
    await act(async () => answer(sessionFor(owner)))

    // Assert
    expect(restoringShown).toBe(true)
    expect(pageShownWhileRestoring).toBe(false)
    expect(authApi.refresh).toHaveBeenCalledOnce()
  })

  it('shows the page once the session is restored', async () => {
    // Arrange
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner))

    // Act
    renderAt('/settings/users')

    // Assert
    expect(await screen.findByText('Users page')).toBeInTheDocument()
  })

  it('shows the page at once to a signed-in user', () => {
    // Arrange
    signInAs(owner)

    // Act
    renderAt('/settings/users')

    // Assert
    expect(screen.getByText('Users page')).toBeInTheDocument()
    expect(authApi.refresh).not.toHaveBeenCalled()
  })

  it('mounts the page afresh when the UI language changes, so it is worked out again in the new language', async () => {
    // Arrange
    signInAs(owner)
    const { user, draft } = renderFormPage()
    await user.type(draft(), 'half typed')

    // Act
    let draftAfter: string
    try {
      await act(() => useAuthStore.getState().setLanguage('uk'))
      draftAfter = draft().value
    } finally {
      await act(() => i18n.changeLanguage('en-US'))
    }

    // Assert
    expect(draftAfter).toBe('')
  })

  it('keeps the page as it is when a refresh brings the language it already runs in', async () => {
    // Arrange
    signInAs(owner)
    const { user, draft } = renderFormPage()
    await user.type(draft(), 'half typed')

    // Act
    act(() => useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'en' }, 'token-2')))

    // Assert
    expect(i18n.language).toBe('en-US')
    expect(draft()).toHaveValue('half typed')
  })

  it('sends a visitor without a session to sign-in, remembering where they were going', () => {
    // Arrange
    useAuthStore.setState({ status: 'anonymous' })

    // Act
    renderAt('/settings/users?search=ol')

    // Assert
    expect(screen.getByText('Sign-in page, then /settings/users?search=ol')).toBeInTheDocument()
    expect(screen.queryByText('Users page')).not.toBeInTheDocument()
  })

  it('remembers no page for the next person after the user signs out on purpose', () => {
    // Arrange
    signInAs(owner)
    renderAt('/settings/users?search=ol')

    // Act
    act(() => useAuthStore.getState().clear({ deliberate: true }))

    // Assert
    expect(screen.getByText('Sign-in page, then no page remembered')).toBeInTheDocument()
    expect(screen.queryByText('Users page')).not.toBeInTheDocument()
  })

  it('remembers no page for the next person after another tab signs out', async () => {
    // Arrange
    signInAs(owner)
    renderAt('/settings/users')
    const otherTab = new BroadcastChannel('mam-auth')

    // Act
    await act(async () => {
      otherTab.postMessage('signed-out')
      await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    })
    otherTab.close()

    // Assert
    expect(screen.getByText('Sign-in page, then no page remembered')).toBeInTheDocument()
  })

  it('still remembers the page when the session ends by itself', async () => {
    // Arrange
    signInAs(owner, 'token-1')
    vi.mocked(authApi.refresh).mockRejectedValue(apiError(401, 'Your session has ended. Sign in again.'))
    renderAt('/settings/users?search=ol')

    // Act
    await act(() => refreshSession('token-1').catch(() => undefined))

    // Assert
    expect(screen.getByText('Sign-in page, then /settings/users?search=ol')).toBeInTheDocument()
    expect(useAuthStore.getState().deliberateSignOut).toBe(false)
  })

  it('offers a retry while the server cannot be reached, and shows the page once it answers', async () => {
    // Arrange
    const user = userEvent.setup()
    useAuthStore.setState({ status: 'unavailable' })
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner))
    renderAt('/settings/users')
    const message = screen.getByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(message).toHaveTextContent('The server could not be reached, so your session could not be restored.')
    expect(await screen.findByText('Users page')).toBeInTheDocument()
    expect(authApi.refresh).toHaveBeenCalledOnce()
  })

  it('asks the server again every 3 seconds while it cannot be reached', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    useAuthStore.setState({ status: 'unavailable' })
    vi.mocked(authApi.refresh).mockRejectedValue(networkError())
    renderAt('/settings/users')

    // Act
    await act(() => vi.advanceTimersByTimeAsync(6_000))

    // Assert
    expect(authApi.refresh).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(useAuthStore.getState().status).toBe('unavailable')
  })
})
