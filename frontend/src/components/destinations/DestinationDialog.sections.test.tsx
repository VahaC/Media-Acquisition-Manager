import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DestinationDialog } from '@/components/destinations/DestinationDialog'
import { destinationsApi } from '@/lib/destinations-api'
import { integrationsApi } from '@/lib/integrations-api'
import { qk } from '@/lib/queries'
import type { Destination, DestinationSectionSource } from '@/lib/types'
import { networkError } from '@/test/api-error'
import { cartoons, movies, sampleCategories, sectionOf, sectionsOf, spareQbittorrent, tolokaSections } from '@/test/destinations'
import { localQbittorrent } from '@/test/integrations'

// MAM-32 — the tracker sections of the destination dialog (ADR 0020): Toloka's sections, each with the
// destination it is mapped to, ticked here and saved with the destination's one Save.

vi.mock('@/lib/destinations-api')
vi.mock('@/lib/integrations-api')

/** A second release source that couldn't be asked, with one section still mapped to Cartoons. */
const offSource: DestinationSectionSource = {
  id: '5d1c7a90-0000-4000-8000-000000000003',
  name: 'Other',
  sections: [sectionOf('Cartoons', ['200'], { destinationId: cartoons.id, destinationName: cartoons.name })],
  error: '“Other” is switched off. Switch it on under Integrations to read its sections.',
}

function renderDialog(destination?: Destination) {
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DestinationDialog destination={destination} onClose={onClose} />
    </QueryClientProvider>,
  )
  const name = destination ? `Edit ${destination.name}` : 'Add destination'
  return { client, onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name }) }
}

/** The sections field: its label names the group. */
const field = (dialog: HTMLElement) => within(dialog).getByRole('group', { name: 'Tracker sections (optional)' })

/** The row of the source named `name`, which opens its sections. */
const sourceRow = (dialog: HTMLElement, name: string) => within(dialog).getByRole('button', { name: new RegExp(`^${name}`) })

/** The section boxes under the legend `legend`, as their labels. */
const sectionsUnder = (dialog: HTMLElement, legend: string) =>
  within(within(dialog).getByRole('group', { name: legend })).getAllByRole('checkbox').map((box) => box.closest('label')!.textContent)

/** The box of the section named `name`. */
const box = (dialog: HTMLElement, name: string) => within(dialog).getByRole('checkbox', { name: new RegExp(`^${name}`) })

/** Saves the edit and returns the request it sent. */
async function saveEdit(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))
  await vi.waitFor(() => expect(destinationsApi.update).toHaveBeenCalledOnce())
  return vi.mocked(destinationsApi.update).mock.lastCall?.[1]
}

describe('DestinationDialog tracker sections', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [localQbittorrent, spareQbittorrent], totalCount: 2, page: query.page, pageSize: query.pageSize }))
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue(sampleCategories)
    vi.mocked(destinationsApi.sections).mockResolvedValue(sectionsOf(tolokaSections))
    vi.mocked(destinationsApi.update).mockResolvedValue()
  })

  it('lists the one source open, its own sections apart from the standard ones, ticking those mapped here', async () => {
    // Arrange
    const { dialog } = renderDialog(movies)

    // Act
    await within(dialog).findByRole('group', { name: 'Its own sections' })

    // Assert
    expect(field(dialog)).toHaveAccessibleDescription(/^The Download dialog picks this destination for a release listed in a section you tick here/)
    expect(sourceRow(dialog, 'Toloka')).toHaveAttribute('aria-expanded', 'true')
    expect(sourceRow(dialog, 'Toloka')).toHaveTextContent('Toloka2 picked')
    expect(sectionsUnder(dialog, 'Its own sections')).toEqual(['Українські фільми', 'Мультсеріали · now Cartoons', 'Телесеріали'])
    expect(sectionsUnder(dialog, 'Standard categories')).toEqual(['Movies/HD', 'TV'])
    expect(box(dialog, 'Українські фільми')).toBeChecked()
    expect(box(dialog, 'Movies/HD')).toBeChecked()
    expect(box(dialog, 'Мультсеріали')).not.toBeChecked()
    expect(destinationsApi.sections).toHaveBeenCalledOnce()
  })

  it('leaves the sections out of an edit whose sections were not touched, so they stay as they are', async () => {
    // Arrange
    const { dialog, user } = renderDialog(movies)
    await within(dialog).findByRole('group', { name: 'Its own sections' })

    // Act
    const request = await saveEdit(user, dialog)

    // Assert
    expect(request).not.toHaveProperty('sections', expect.anything())
  })

  it('sends every ticked section once one is ticked, a section of another destination moved here', async () => {
    // Arrange
    const { dialog, user } = renderDialog(movies)
    await within(dialog).findByRole('group', { name: 'Its own sections' })
    await user.click(box(dialog, 'Мультсеріали'))
    await user.click(box(dialog, 'Movies/HD'))

    // Act
    const request = await saveEdit(user, dialog)

    // Assert
    expect(sourceRow(dialog, 'Toloka')).toHaveTextContent('Toloka2 picked')
    expect(request?.sections).toEqual([
      { releaseSourceId: tolokaSections.id, categoryIds: ['100016'], name: 'Українські фільми', isSourceSpecific: true },
      { releaseSourceId: tolokaSections.id, categoryIds: ['100044', '100125', '100195'], name: 'Мультсеріали', isSourceSpecific: true },
    ])
  })

  it('sends no section at all once every one is unticked', async () => {
    // Arrange
    const { dialog, user } = renderDialog(movies)
    await within(dialog).findByRole('group', { name: 'Its own sections' })
    await user.click(box(dialog, 'Українські фільми'))
    await user.click(box(dialog, 'Movies/HD'))

    // Act
    const request = await saveEdit(user, dialog)

    // Assert
    expect(sourceRow(dialog, 'Toloka')).toHaveTextContent('TolokaNone picked')
    expect(request?.sections).toEqual([])
  })

  it('adds a destination with the sections ticked, none ticked to begin with', async () => {
    // Arrange
    vi.mocked(destinationsApi.create).mockResolvedValue(cartoons)
    const { dialog, onClose, user } = renderDialog()
    await user.type(within(dialog).getByLabelText('Name'), 'Cartoons')
    await vi.waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Download client' })).toBeEnabled())
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Download client' }), localQbittorrent.id)
    await vi.waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Category' })).toBeEnabled())
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Category' }), 'Cartoons')
    await within(dialog).findByRole('group', { name: 'Its own sections' })
    expect(within(dialog).queryAllByRole('checkbox', { checked: true })).toHaveLength(0)
    await user.click(box(dialog, 'Мультсеріали'))

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add destination' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(destinationsApi.create).toHaveBeenCalledExactlyOnceWith({
      name: 'Cartoons',
      downloadClientId: localQbittorrent.id,
      clientDestinationKey: 'Cartoons',
      isDefault: false,
      sections: [{ releaseSourceId: tolokaSections.id, categoryIds: ['100044', '100125', '100195'], name: 'Мультсеріали', isSourceSpecific: true }],
    })
  })

  it('keeps several sources closed until one is opened, and says why one couldn\'t be asked beside its mapped sections', async () => {
    // Arrange
    vi.mocked(destinationsApi.sections).mockResolvedValue(sectionsOf(offSource, tolokaSections))
    const { dialog, user } = renderDialog(cartoons)
    await within(dialog).findByText(offSource.error!)
    const closed = within(dialog).queryAllByRole('checkbox', { name: /^(Cartoons|Мультсеріали)/ })

    // Act
    await user.click(sourceRow(dialog, 'Other'))

    // Assert
    expect(closed).toHaveLength(0)
    expect(sourceRow(dialog, 'Toloka')).toHaveAttribute('aria-expanded', 'false')
    expect(sourceRow(dialog, 'Other')).toHaveTextContent('Other1 picked')
    expect(box(dialog, 'Cartoons')).toBeChecked()
  })

  it('says it is asking the sources while the sections are on their way', async () => {
    // Arrange
    vi.mocked(destinationsApi.sections).mockReturnValue(new Promise(() => {}))

    // Act
    const { dialog } = renderDialog(movies)

    // Assert
    expect(await within(field(dialog)).findByText('Asking the release sources for their sections…')).toBeInTheDocument()
  })

  it('says the sections could not be loaded, and an edit then keeps the ones saved', async () => {
    // Arrange
    vi.mocked(destinationsApi.sections).mockRejectedValue(networkError())
    const { dialog, user } = renderDialog(movies)
    await within(field(dialog)).findByRole('alert')

    // Act
    const request = await saveEdit(user, dialog)

    // Assert
    expect(within(field(dialog)).getByRole('alert')).toHaveTextContent('The server could not be reached.')
    expect(request).not.toHaveProperty('sections', expect.anything())
  })

  it('says where to add a release source when there is none', async () => {
    // Arrange
    vi.mocked(destinationsApi.sections).mockResolvedValue(sectionsOf())

    // Act
    const { dialog } = renderDialog(movies)

    // Assert
    expect(await within(dialog).findByText('No release source yet. Add one under Integrations to pick its sections.')).toBeInTheDocument()
  })

  it('asks for the sections again once a save goes through, as it moved them', async () => {
    // Arrange
    const { client, dialog, user } = renderDialog(movies)
    await within(dialog).findByRole('group', { name: 'Its own sections' })
    await user.click(box(dialog, 'Мультсеріали'))

    // Act
    await saveEdit(user, dialog)

    // Assert — the dialog is still open in this test, so the dropped sections are asked for at once.
    await vi.waitFor(() => expect(destinationsApi.sections).toHaveBeenCalledTimes(2))
    expect(client.getQueryState(qk.settings.destinations.sections)?.status).toBe('success')
  })
})
