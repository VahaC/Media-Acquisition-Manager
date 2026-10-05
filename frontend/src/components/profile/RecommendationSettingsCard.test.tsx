import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RecommendationSettingsCard } from '@/components/profile/RecommendationSettingsCard'
import { profileApi } from '@/lib/profile-api'
import { qk } from '@/lib/queries'
import type { RecommendationSettings } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { member, signInAs } from '@/test/auth-session'
import { allTabsOn } from '@/test/recommendations'

vi.mock('@/lib/profile-api')

const card = () => screen.getByRole('region', { name: 'Recommendations' })
const limitField = () => screen.getByRole('textbox', { name: 'Daily search limit' })
const save = () => screen.getByRole('button', { name: 'Save' })

/** The boxes of the four tabs, as name and whether each is ticked. */
const boxes = () =>
  within(screen.getByRole('group', { name: 'Lists' })).getAllByRole('checkbox')
    .map((box) => [box.parentElement?.textContent, (box as HTMLInputElement).checked])

function renderCard(client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  signInAs(member)
  render(
    <QueryClientProvider client={client}>
      <RecommendationSettingsCard />
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

describe('RecommendationSettingsCard', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue({ ...allTabsOn, cartoons: false })
    vi.mocked(profileApi.saveRecommendationSettings).mockImplementation(async (request) => ({ ...request, searchesLast24Hours: 12 }))
  })

  it('shows the user\'s tabs, their daily search limit and the searches run for them in the last 24 hours', async () => {
    // Arrange
    const settings = { ...allTabsOn, cartoons: false }

    // Act
    renderCard()

    // Assert
    expect(await screen.findByRole('textbox', { name: 'Daily search limit' })).toHaveValue(String(settings.dailySearchLimit))
    expect(boxes()).toEqual([['Movies', true], ['Series', true], ['Cartoons', false], ['Documentaries', true]])
    expect(within(card()).getByText('12 searches in the last 24 hours.')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Lists' })).toHaveAccessibleDescription(
      'A switched-off tab is hidden, and its works are never searched for you.')
    expect(limitField()).toHaveAccessibleDescription(
      'How many release-source searches the app may run for your lists in 24 hours; 0 runs none.')
  })

  it('says one search in the singular', async () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue({ ...allTabsOn, searchesLast24Hours: 1 })

    // Act
    renderCard()

    // Assert
    expect(await screen.findByText('1 search in the last 24 hours.')).toBeInTheDocument()
  })

  it('saves every field through its one Save, says Saved., and puts the answer in the cache', async () => {
    // Arrange
    const { client, user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })
    await user.click(screen.getByRole('checkbox', { name: 'Series' }))
    await user.click(screen.getByRole('checkbox', { name: 'Cartoons' }))
    await user.clear(limitField())
    await user.type(limitField(), ' 30 ')

    // Act
    await user.click(save())

    // Assert
    expect(profileApi.saveRecommendationSettings).toHaveBeenCalledExactlyOnceWith({
      movies: true,
      series: false,
      cartoons: true,
      documentaries: true,
      dailySearchLimit: 30,
    })
    expect(await screen.findByRole('status')).toHaveTextContent('Saved.')
    expect(limitField()).toHaveValue('30')
    expect(client.getQueryData<RecommendationSettings>(qk.profile.recommendations)).toMatchObject({ series: false, cartoons: true, dailySearchLimit: 30 })
  })

  it('drops Saved. once a field is changed again', async () => {
    // Arrange
    const { user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })
    await user.click(save())
    await screen.findByRole('status')

    // Act
    await user.click(screen.getByRole('checkbox', { name: 'Movies' }))

    // Assert
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows the server\'s sentence under the limit when it refuses the number', async () => {
    // Arrange
    vi.mocked(profileApi.saveRecommendationSettings).mockRejectedValue(apiError(400, 'The daily search limit is a whole number from 0 to 500.'))
    const { user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })
    await user.clear(limitField())
    await user.type(limitField(), '900')

    // Act
    await user.click(save())

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The daily search limit is a whole number from 0 to 500.')
    expect(limitField()).toHaveAttribute('aria-invalid', 'true')
    expect(limitField()).toHaveAccessibleDescription('The daily search limit is a whole number from 0 to 500.')
    expect(profileApi.saveRecommendationSettings).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ dailySearchLimit: 900 }))
    expect(limitField()).toHaveValue('900')
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument()
  })

  it.each([['abc'], ['12.5'], ['-1'], [''], ['1234567890']])('refuses the limit “%s” without asking the server, in the server\'s words', async (typed) => {
    // Arrange
    const { user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })
    await user.clear(limitField())
    if (typed !== '') await user.type(limitField(), typed)

    // Act
    await user.click(save())

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The daily search limit is a whole number from 0 to 500.')
    expect(profileApi.saveRecommendationSettings).not.toHaveBeenCalled()
  })

  it('clears the limit\'s sentence once the field is edited', async () => {
    // Arrange
    const { user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })
    await user.clear(limitField())
    await user.type(limitField(), 'abc')
    await user.click(save())
    await screen.findByRole('alert')

    // Act
    await user.type(limitField(), '{Backspace}')

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says in a banner above Save why a save failed for any other reason, and keeps what was typed', async () => {
    // Arrange
    vi.mocked(profileApi.saveRecommendationSettings).mockRejectedValue(apiError(500, 'The database is unavailable.'))
    const { user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })
    await user.click(screen.getByRole('checkbox', { name: 'Documentaries' }))

    // Act
    await user.click(save())

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The database is unavailable.')
    expect(limitField()).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('checkbox', { name: 'Documentaries' })).not.toBeChecked()
  })

  it('reads Saving… and disables Save while the save is on its way', async () => {
    // Arrange
    vi.mocked(profileApi.saveRecommendationSettings).mockReturnValue(new Promise<RecommendationSettings>(() => {}))
    const { user } = renderCard()
    await screen.findByRole('textbox', { name: 'Daily search limit' })

    // Act
    await user.click(save())

    // Assert
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled()
  })

  it('says the settings could not be loaded, and Try again loads them', async () => {
    // Arrange
    vi.mocked(profileApi.recommendationSettings)
      .mockRejectedValueOnce(apiError(500, 'The database is unavailable.'))
      .mockResolvedValueOnce(allTabsOn)
    const { user } = renderCard()
    const errorState = await screen.findByRole('alert')
    const said = errorState.textContent

    // Act
    await user.click(within(errorState).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(said).toContain('Could not load your recommendation settings.')
    expect(said).toContain('The database is unavailable.')
    expect(await screen.findByRole('textbox', { name: 'Daily search limit' })).toHaveValue('60')
  })

  it('keeps the form under a banner when a refresh of the settings fails', async () => {
    // Arrange
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(qk.profile.recommendations, allTabsOn)
    vi.mocked(profileApi.recommendationSettings).mockRejectedValue(apiError(500, 'The database is unavailable.'))

    // Act
    renderCard(client)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Refresh failed, showing your recommendation settings as last loaded. The database is unavailable.')
    expect(limitField()).toHaveValue('60')
  })
})
