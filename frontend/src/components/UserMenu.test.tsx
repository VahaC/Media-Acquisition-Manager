import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { UserMenu } from '@/components/UserMenu'
import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { queryClient as appQueryClient } from '@/lib/query-client'
import type { CurrentUser } from '@/lib/types'
import { networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'

vi.mock('@/lib/auth-api')

function renderMenu(user: CurrentUser) {
  signInAs(user)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/']}>
        <p>Outside the menu</p>
        <UserMenu user={user} />
        {/* The next focus stop after the menu, like Add user on the Users page. */}
        <button type="button">Next on the page</button>
        <Routes>
          <Route path="/" element={<p>Home page</p>} />
          <Route path="/settings/integrations" element={<p>Integrations page</p>} />
          <Route path="/settings/users" element={<p>Users page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

describe('UserMenu', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    appQueryClient.clear()
  })

  it('names the signed-in user on its button, and starts closed', () => {
    // Arrange
    const signedIn = owner

    // Act
    renderMenu(signedIn)

    // Assert
    const trigger = screen.getByRole('button', { name: 'Signed in as vasyl' })
    expect(trigger).toHaveTextContent('vasyl')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
  })

  it('offers Users to the installation owner, and opens the page from there', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('link', { name: 'Users' }))

    // Assert
    expect(screen.getByText('Users page')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument()
  })

  it('offers Integrations above Users to the installation owner, and opens the page from there', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))
    const entries = screen.getAllByRole('link').map((link) => link.textContent)

    // Act
    await user.click(screen.getByRole('link', { name: 'Integrations' }))

    // Assert
    expect(entries).toEqual(['Integrations', 'Users'])
    expect(screen.getByText('Integrations page')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Integrations' })).not.toBeInTheDocument()
  })

  it('does not offer Integrations or Users to anyone else', async () => {
    // Arrange
    const { user } = renderMenu(member)

    // Act
    await user.click(screen.getByRole('button', { name: 'Signed in as olena' }))

    // Assert
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Integrations' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument()
  })

  it('signs out through the API and ends the session in this tab', async () => {
    // Arrange
    vi.mocked(authApi.signOut).mockResolvedValue()
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    // Assert
    expect(authApi.signOut).toHaveBeenCalledOnce()
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    expect(useAuthStore.getState()).toMatchObject({ accessToken: null, user: null, deliberateSignOut: true })
  })

  it('ends the session in this tab even when the server cannot be reached', async () => {
    // Arrange
    vi.mocked(authApi.signOut).mockRejectedValue(networkError())
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    // Assert
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    expect(authApi.signOut).toHaveBeenCalledOnce()
  })

  it('closes on Escape and gives the focus back to its button', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    const trigger = screen.getByRole('button', { name: 'Signed in as vasyl' })
    await user.click(trigger)

    // Act
    await user.keyboard('{Escape}')

    // Assert
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(trigger).toHaveFocus()
  })

  it('closes when Tab moves the focus on past its last entry', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))
    await user.tab()
    await user.tab()
    await user.tab()
    const lastEntry = screen.getByRole('button', { name: 'Sign out' })
    const focusedOnLastEntry = document.activeElement === lastEntry

    // Act
    await user.tab()

    // Assert
    expect(focusedOnLastEntry).toBe(true)
    expect(lastEntry).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Signed in as vasyl' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: 'Next on the page' })).toHaveFocus()
  })

  it('closes on a click anywhere outside it', async () => {
    // Arrange
    const { user } = renderMenu(owner)
    await user.click(screen.getByRole('button', { name: 'Signed in as vasyl' }))

    // Act
    await user.click(screen.getByText('Outside the menu'))

    // Assert
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument()
  })
})
