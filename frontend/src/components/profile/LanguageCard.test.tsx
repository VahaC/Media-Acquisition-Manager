import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { LanguageCard } from '@/components/profile/LanguageCard'
import { useAuthStore } from '@/lib/auth-store'
import { i18n } from '@/lib/i18n'
import { profileApi } from '@/lib/profile-api'
import { queryClient as appQueryClient } from '@/lib/query-client'
import type { CurrentUser, LanguageSetting } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { member, resetAuthStore, signInAs } from '@/test/auth-session'

vi.mock('@/lib/profile-api')

const card = () => screen.getByRole('region', { name: 'Language' })
const radio = (name: string) => within(card()).getByRole('radio', { name })
/** The card's status line, there once a save says Saved. */
const status = () => within(card()).getByRole('status')

function renderCard(signedIn: CurrentUser = member) {
  signInAs(signedIn)
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <LanguageCard />
    </QueryClientProvider>,
  )
  return { user: userEvent.setup() }
}

describe('LanguageCard', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.saveLanguage).mockImplementation(async (request) => request)
  })

  afterEach(async () => {
    appQueryClient.clear()
    resetAuthStore()
    // The app's one i18n instance: a test that switched it to Ukrainian must not leave it there.
    await act(() => i18n.changeLanguage('en-US'))
  })

  it('offers the browser\'s language, English and Українська, with the account\'s choice picked', () => {
    // Act
    renderCard({ ...member, language: 'en' })

    // Assert
    expect(within(card()).getByRole('radiogroup', { name: 'Language' })).toBeInTheDocument()
    expect(within(card()).getAllByRole('radio').map((option) => option.closest('label')?.textContent))
      .toEqual(['As in the browser', 'English', 'Українська'])
    expect(radio('English')).toBeChecked()
    expect(within(card()).getByText(
      'Saved with your account, so it follows you to other browsers and devices. Before you sign in, the browser\'s language is used.',
    )).toBeInTheDocument()
  })

  it('names each language in itself, marked with its language for a screen reader', () => {
    // Act
    renderCard()

    // Assert
    expect(within(card()).getByText('English')).toHaveAttribute('lang', 'en')
    expect(within(card()).getByText('Українська')).toHaveAttribute('lang', 'uk')
    expect(within(card()).getByText('As in the browser')).not.toHaveAttribute('lang')
  })

  it('picks As in the browser for an account that follows the browser', () => {
    // Act
    renderCard({ ...member, language: null })

    // Assert
    expect(radio('As in the browser')).toBeChecked()
    expect(radio('English')).not.toBeChecked()
    expect(radio('Українська')).not.toBeChecked()
  })

  it('saves a choice at once, with no Save button, and runs the app in it', async () => {
    // Arrange
    const { user } = renderCard()
    // Found by its English name before the app turns Ukrainian; the language names stay as they are.
    const region = card()

    // Act
    await user.click(within(region).getByRole('radio', { name: 'Українська' }))

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
    expect(profileApi.saveLanguage).toHaveBeenCalledExactlyOnceWith({ language: 'uk' })
    expect(useAuthStore.getState().user?.language).toBe('uk')
    expect(within(region).getByRole('radio', { name: 'Українська' })).toBeChecked()
    expect(within(region).queryByRole('button')).not.toBeInTheDocument()
  })

  it('says Saved. when the choice leaves the app in the language it was in', async () => {
    // Arrange
    const { user } = renderCard({ ...member, language: null })

    // Act
    await user.click(radio('English'))

    // Assert
    await vi.waitFor(() => expect(status()).toHaveTextContent('Saved.'))
    expect(profileApi.saveLanguage).toHaveBeenCalledExactlyOnceWith({ language: 'en' })
    expect(i18n.language).toBe('en-US')
    expect(radio('English')).toBeChecked()
  })

  it('saves As in the browser as no language of the account\'s own', async () => {
    // Arrange
    const { user } = renderCard({ ...member, language: 'en' })

    // Act
    await user.click(radio('As in the browser'))

    // Assert
    await vi.waitFor(() => expect(status()).toHaveTextContent('Saved.'))
    expect(profileApi.saveLanguage).toHaveBeenCalledExactlyOnceWith({ language: null })
    expect(useAuthStore.getState().user?.language).toBeNull()
  })

  it('shows the choice as picked while it saves', async () => {
    // Arrange
    let answer!: (saved: LanguageSetting) => void
    vi.mocked(profileApi.saveLanguage).mockReturnValue(new Promise<LanguageSetting>((resolve) => { answer = resolve }))
    const { user } = renderCard({ ...member, language: 'en' })

    // Act
    await user.click(radio('As in the browser'))
    const pickedWhileSaving = (radio('As in the browser') as HTMLInputElement).checked
    await act(async () => answer({ language: null }))

    // Assert
    expect(pickedWhileSaving).toBe(true)
    await vi.waitFor(() => expect(status()).toHaveTextContent('Saved.'))
  })

  it('goes back to the saved choice and says why when the save fails', async () => {
    // Arrange
    vi.mocked(profileApi.saveLanguage).mockRejectedValue(apiError(400, 'The language is "en", "uk" or null.'))
    const { user } = renderCard({ ...member, language: 'en' })

    // Act
    await user.click(radio('Українська'))

    // Assert
    expect(await within(card()).findByText('The language is "en", "uk" or null.')).toBeInTheDocument()
    expect(radio('English')).toBeChecked()
    expect(radio('Українська')).not.toBeChecked()
    expect(within(card()).queryByRole('status')).not.toBeInTheDocument()
    expect(i18n.language).toBe('en-US')
    expect(useAuthStore.getState().user?.language).toBe('en')
  })

  it('says it could not save when the server gives no reason', async () => {
    // Arrange
    vi.mocked(profileApi.saveLanguage).mockRejectedValue(apiError(500))
    const { user } = renderCard({ ...member, language: 'en' })

    // Act
    await user.click(radio('As in the browser'))

    // Assert
    expect(await within(card()).findByText('Could not save your language.')).toBeInTheDocument()
    expect(radio('English')).toBeChecked()
  })
})

describe('LanguageCard on the signed-in pages', () => {
  // ProtectedRoute mounts the signed-in pages afresh when the UI's language changes; the card is rendered
  // under it, as the Profile page is.
  function renderUnderProtectedRoute(signedIn: CurrentUser) {
    signInAs(signedIn)
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
        <MemoryRouter initialEntries={['/profile']}>
          <Routes>
            <Route element={<ProtectedRoute />}>
              <Route path="/profile" element={<><button type="button">Before the card</button><LanguageCard /></>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )
    return { user: userEvent.setup() }
  }

  const radioByValue = (value: string) =>
    screen.getAllByRole('radio').find((option) => (option as HTMLInputElement).value === value)!

  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.saveLanguage).mockImplementation(async (request) => request)
  })

  afterEach(async () => {
    appQueryClient.clear()
    resetAuthStore()
    await act(() => i18n.changeLanguage('en-US'))
  })

  it('puts the focus back on the chosen pill when an arrow key\'s choice changes the UI\'s language, so the next arrow key goes on', async () => {
    // Arrange
    // An English browser, the account in Ukrainian: «Українська» picked, the UI in Ukrainian.
    const { user } = renderUnderProtectedRoute({ ...member, language: 'uk' })
    await act(() => i18n.changeLanguage('uk'))
    radioByValue('uk').focus()

    // Act
    await user.keyboard('{ArrowLeft}')

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('en-US'))
    await vi.waitFor(() => expect(radioByValue('en')).toHaveFocus())
    expect(radioByValue('en')).toBeChecked()

    // Act
    await user.keyboard('{ArrowLeft}')

    // Assert
    await vi.waitFor(() => expect(useAuthStore.getState().user?.language).toBeNull())
    expect(vi.mocked(profileApi.saveLanguage).mock.calls).toEqual([[{ language: 'en' }], [{ language: null }]])
    expect(radioByValue('browser')).toBeChecked()
    expect(radioByValue('browser')).toHaveFocus()
    expect(i18n.language).toBe('en-US')
  })

  it('leaves the focus where it is when the page mounts afresh without a choice of its own', async () => {
    // Arrange
    renderUnderProtectedRoute({ ...member, language: 'en' })
    const before = screen.getByRole('button', { name: 'Before the card' })
    before.focus()

    // Act
    // Another tab saved Українська: this one follows, and the page mounts afresh in it.
    await act(() => useAuthStore.getState().setLanguage('uk'))

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
    expect(screen.getAllByRole('radio').some((option) => option === document.activeElement)).toBe(false)
  })
})
