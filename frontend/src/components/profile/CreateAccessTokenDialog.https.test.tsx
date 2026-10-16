// @vitest-environment-options {"url": "https://mam.example.home/"}
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateAccessTokenDialog } from '@/components/profile/CreateAccessTokenDialog'
import { profileApi } from '@/lib/profile-api'
import { member, signInAs } from '@/test/auth-session'
import { createdToken } from '@/test/access-tokens'

vi.mock('@/lib/profile-api')

// MAM-45 — the shown-once step at an HTTPS address. A file of its own because the page's address is
// jsdom's, set for this file by the line above: a loaded page's window.location can't be redefined.

const ORIGIN = 'https://mam.example.home'

describe('CreateAccessTokenDialog at an HTTPS address', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.createAccessToken).mockResolvedValue(createdToken)
  })

  it('points both Claude lines at the HTTPS address and leaves --allow-http out of the Claude Desktop configuration', async () => {
    // Arrange
    signInAs(member)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <CreateAccessTokenDialog onClose={vi.fn()} />
      </QueryClientProvider>,
    )
    const user = userEvent.setup()
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Claude on the NAS')

    // Act
    await user.click(screen.getByRole('button', { name: 'Create token' }))

    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Your new access token' })
    expect(window.location.origin).toBe(ORIGIN)
    expect(within(dialog).getByRole('textbox', { name: 'Claude Code' })).toHaveValue(
      `claude mcp add --transport http --scope user mam ${ORIGIN}/mcp --header "Authorization: Bearer ${createdToken.secret}"`)
    const desktop = within(dialog).getByRole('textbox', { name: 'Claude Desktop' }) as HTMLTextAreaElement
    expect(JSON.parse(desktop.value)).toEqual({
      mcpServers: {
        mam: {
          command: 'npx',
          args: ['-y', 'mcp-remote', `${ORIGIN}/mcp`, '--transport', 'http-only', '--header', 'Authorization:${AUTH_HEADER}'],
          env: { AUTH_HEADER: `Bearer ${createdToken.secret}` },
        },
      },
    })
    expect(desktop.value).not.toContain('--allow-http')
  })
})
