import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { AccessTokensCard } from '@/components/profile/AccessTokensCard'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDate, formatDateTime } from '@/lib/format'
import { resources } from '@/lib/i18n'
import { profileApi } from '@/lib/profile-api'
import { qk } from '@/lib/queries'
import { apiError, networkError } from '@/test/api-error'
import { member, signInAs } from '@/test/auth-session'
import { backupToken, createdToken, expiredToken, laptopToken } from '@/test/access-tokens'

vi.mock('@/lib/profile-api')

/** The formats the card shows its dates and its last use in. */
const dateFormat: Intl.DateTimeFormatOptions = { dateStyle: 'medium' }
const instantFormat: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

const card = () => screen.getByRole('region', { name: 'Access tokens' })

/** The table row (or phone card) of the token named `name`. */
const rowOf = (name: string) => screen.getByText(name).closest('tr')!

/** The cell of `row` under the column named `column`. */
const cellOf = (row: HTMLElement, column: string) => row.querySelector(`td[data-label="${column}"]`)!

function renderCard(client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  signInAs(member)
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <AccessTokensCard />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

describe('AccessTokensCard', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.accessTokens).mockResolvedValue({ items: [laptopToken, backupToken, expiredToken] })
  })

  it('lists each token with its name, access, prefix, when it was created, last used and expires', async () => {
    // Arrange
    const expected = laptopToken

    // Act
    renderCard()

    // Assert
    await screen.findByText(expected.name)
    const row = rowOf(expected.name)
    expect(within(row).getByText('Read and download')).toHaveClass('ui-badge', 'ui-badge-secondary')
    expect(within(row).getByText('mam_pat_AbC123…')).toHaveClass('profile-mono')
    expect(cellOf(row, 'Created')).toHaveTextContent(formatDate(expected.createdUtc, dateFormat))
    expect(cellOf(row, 'Last used')).toHaveTextContent(formatDateTime(expected.lastUsedUtc!, instantFormat))
    expect(cellOf(row, 'Expires')).toHaveTextContent(formatDate(expected.expiresUtc!, dateFormat))
    expect(within(row).queryByText('Expired')).not.toBeInTheDocument()
    expect(row).not.toHaveAttribute('data-disabled')
  })

  it('says Never for a token not used yet and for one that never expires', async () => {
    // Arrange
    const expected = backupToken

    // Act
    renderCard()

    // Assert
    await screen.findByText(expected.name)
    const row = rowOf(expected.name)
    expect(within(row).getByText('Read only')).toBeInTheDocument()
    expect(cellOf(row, 'Last used')).toHaveTextContent('Never')
    expect(cellOf(row, 'Expires')).toHaveTextContent('Never')
  })

  // MAM-46 — English says Never in both cells; Ukrainian says "never" under Last used but "indefinitely"
  // under Expires, so the Expires cell reads a key of its own.
  it('says in Ukrainian that a token not used yet was never used and one that never expires is valid indefinitely', async () => {
    // Arrange
    const expected = backupToken
    const words = resources.uk.profile.accessTokens
    const ukrainian = createInstance()
    void ukrainian.init({
      lng: 'uk',
      fallbackLng: 'en',
      resources,
      ns: Object.keys(resources.en),
      defaultNS: 'common',
      interpolation: { escapeValue: false },
      initAsync: false,
    })
    signInAs(member)

    // Act
    render(
      <I18nextProvider i18n={ukrainian}>
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <ConfirmProvider>
            <AccessTokensCard />
          </ConfirmProvider>
        </QueryClientProvider>
      </I18nextProvider>,
    )

    // Assert
    await screen.findByText(expected.name)
    const row = rowOf(expected.name)
    expect(cellOf(row, words.columns.lastUsed)).toHaveTextContent(words.never)
    expect(cellOf(row, words.columns.expires)).toHaveTextContent(words.expiresNever)
    expect(words.expiresNever).not.toBe(words.never)
  })

  it('marks an expired token and mutes its row, keeping it listed until it is revoked', async () => {
    // Arrange
    const expected = expiredToken

    // Act
    renderCard()

    // Assert
    await screen.findByText(expected.name)
    const row = rowOf(expected.name)
    expect(within(row).getByText('Expired')).toHaveClass('ui-badge', 'ui-badge-outline')
    expect(row).toHaveAttribute('data-disabled')
    expect(within(row).getByRole('button', { name: 'Revoke' })).toBeEnabled()
  })

  it('keeps the server\'s order, newest first', async () => {
    // Arrange
    vi.mocked(profileApi.accessTokens).mockResolvedValue({ items: [expiredToken, laptopToken, backupToken] })

    // Act
    renderCard()

    // Assert
    await screen.findByText(laptopToken.name)
    const names = within(card()).getAllByRole('row').slice(1).map((row) => row.querySelector('.settings-name-text')?.textContent)
    expect(names).toEqual([expiredToken.name, laptopToken.name, backupToken.name])
  })

  it('says how to create a token when there is none', async () => {
    // Arrange
    vi.mocked(profileApi.accessTokens).mockResolvedValue({ items: [] })

    // Act
    renderCard()

    // Assert
    expect(await screen.findByText('No access tokens yet. Click Create token to give a script or an AI assistant one of its own.'))
      .toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(within(card()).getByRole('button', { name: 'Create token' })).toBeEnabled()
  })

  it('opens the create dialog from Create token', async () => {
    // Arrange
    const { user } = renderCard()
    await screen.findByText(laptopToken.name)

    // Act
    await user.click(screen.getByRole('button', { name: 'Create token' }))

    // Assert
    expect(await screen.findByRole('dialog', { name: 'Create an access token' })).toBeInTheDocument()
  })

  it('lists a new token once it is created, without its secret', async () => {
    // Arrange
    const created = createdToken.token
    vi.mocked(profileApi.createAccessToken).mockImplementation(async () => {
      vi.mocked(profileApi.accessTokens).mockResolvedValue({ items: [created, laptopToken, backupToken, expiredToken] })
      return createdToken
    })
    const { client, user } = renderCard()
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    await screen.findByText(laptopToken.name)
    await user.click(within(card()).getByRole('button', { name: 'Create token' }))
    const form = await screen.findByRole('dialog', { name: 'Create an access token' })
    await user.type(within(form).getByRole('textbox', { name: 'Name' }), created.name)
    await user.click(within(form).getByRole('button', { name: 'Create token' }))
    const secret = await screen.findByRole('dialog', { name: 'Your new access token' })

    // Act
    await user.click(within(secret).getByRole('button', { name: 'Done' }))

    // Assert
    expect(await within(card()).findByText(created.name)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(within(rowOf(created.name)).getByText('mam_pat_Nas4Kq…')).toBeInTheDocument()
    // MAM-5 — the secret is shown once, in the dialog: the list never carries it.
    expect(card()).not.toHaveTextContent(createdToken.secret)
    expect(document.body).not.toHaveTextContent(createdToken.secret)
    expect(profileApi.accessTokens).toHaveBeenCalledTimes(2)
    // The owner's own count on the Users page follows too.
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.profile.accessTokens })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: qk.settings.users.all })
  })

  it('does not revoke a token when the confirmation is cancelled', async () => {
    // Arrange
    const { user } = renderCard()
    await screen.findByText(laptopToken.name)
    await user.click(within(rowOf(laptopToken.name)).getByRole('button', { name: 'Revoke' }))
    const confirmation = await screen.findByRole('dialog', { name: `Revoke ${laptopToken.name}?` })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(profileApi.revokeAccessToken).not.toHaveBeenCalled()
  })

  it('revokes a token only once the confirmation is accepted, and lists the tokens again without it', async () => {
    // Arrange
    vi.mocked(profileApi.revokeAccessToken).mockImplementation(async () => {
      vi.mocked(profileApi.accessTokens).mockResolvedValue({ items: [backupToken, expiredToken] })
    })
    const { user } = renderCard()
    await screen.findByText(laptopToken.name)
    await user.click(within(rowOf(laptopToken.name)).getByRole('button', { name: 'Revoke' }))
    const confirmation = await screen.findByRole('dialog', { name: `Revoke ${laptopToken.name}?` })
    const calledBeforeConfirming = vi.mocked(profileApi.revokeAccessToken).mock.calls.length

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Revoke' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByText(laptopToken.name)).not.toBeInTheDocument())
    expect(calledBeforeConfirming).toBe(0)
    expect(profileApi.revokeAccessToken).toHaveBeenCalledExactlyOnceWith(laptopToken.id)
    expect(confirmation).toHaveTextContent(
      `Scripts and AI assistants that use ${laptopToken.name} are refused from now on. This can't be undone`)
    expect(screen.getByText(backupToken.name)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says a refused Revoke above the tokens, in the app\'s words for a bare 404, until it is dismissed', async () => {
    // Arrange
    vi.mocked(profileApi.revokeAccessToken).mockRejectedValue(apiError(404))
    const { user } = renderCard()
    await screen.findByText(laptopToken.name)
    await user.click(within(rowOf(laptopToken.name)).getByRole('button', { name: 'Revoke' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Revoke' }))
    const banner = await within(card()).findByRole('alert')

    // Act
    await user.click(within(banner).getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(banner).toHaveTextContent('Could not revoke the token. Reload the page and try again.')
    expect(within(card()).queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(laptopToken.name)).toBeInTheDocument()
  })

  it('shows the server\'s sentence when a Revoke is refused with one', async () => {
    // Arrange
    vi.mocked(profileApi.revokeAccessToken).mockRejectedValue(networkError())
    const { user } = renderCard()
    await screen.findByText(laptopToken.name)

    // Act
    await user.click(within(rowOf(laptopToken.name)).getByRole('button', { name: 'Revoke' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Revoke' }))

    // Assert
    expect(await within(card()).findByRole('alert')).toHaveTextContent('The server could not be reached.')
  })

  it('says the tokens could not be loaded, with the sentence and Try again, and keeps Create token', async () => {
    // Arrange
    vi.mocked(profileApi.accessTokens)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockResolvedValueOnce({ items: [laptopToken] })
    const { user } = renderCard()
    const state = await within(card()).findByRole('alert')

    // Act
    await user.click(within(state).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load your access tokens.')
    expect(state).toHaveTextContent('The database is unavailable.')
    expect(await screen.findByText(laptopToken.name)).toBeInTheDocument()
    expect(within(card()).getByRole('button', { name: 'Create token' })).toBeInTheDocument()
  })

  it('keeps the tokens on screen under a banner when a refresh fails', async () => {
    // Arrange
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(qk.profile.accessTokens, { items: [laptopToken] })
    vi.mocked(profileApi.accessTokens).mockRejectedValue(networkError())

    // Act
    renderCard(client)

    // Assert
    const banner = await within(card()).findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing your access tokens as last loaded. The server could not be reached.')
    expect(within(banner).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(screen.getByText(laptopToken.name)).toBeInTheDocument()
  })
})
