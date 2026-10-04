import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReleaseFilters, type ReleaseFilterValues } from '@/components/search/ReleaseFilters'
import type { SearchFacets } from '@/lib/types'
import { aliensSearch, other, searchSourceOf, toloka } from '@/test/search'

const noFilters: ReleaseFilterValues = { source: '', kind: '', language: '', quality: '', minSize: '', maxSize: '', sort: 'seeders' }

/** A source that answered with nothing, which the source filter leaves out. */
const quiet = searchSourceOf({ id: '5ea4c000-0000-4000-8000-000000000013', name: 'Quiet', releaseCount: 0 })

/** The filters as a page holds them: every change kept and recorded. */
function renderFilters(facets: SearchFacets = aliensSearch.facets) {
  const changes: ReleaseFilterValues[] = []
  function Host() {
    const [values, setValues] = useState(noFilters)
    return (
      <ReleaseFilters
        sources={[other, quiet, toloka]}
        facets={facets}
        values={values}
        onChange={(next) => {
          changes.push(next)
          setValues(next)
        }}
      />
    )
  }
  render(<Host />)
  return { changes, user: userEvent.setup() }
}

/** The option labels of the select named `name`, in order. */
const optionsOf = (name: string) =>
  [...screen.getByRole('combobox', { name }).querySelectorAll('option')].map((option) => option.textContent)

describe('ReleaseFilters', () => {
  it('offers the sources that listed something, by the search\'s order', () => {
    // Arrange
    const facets = aliensSearch.facets

    // Act
    renderFilters(facets)

    // Assert
    expect(optionsOf('Source')).toEqual(['All sources', 'Other', 'Toloka'])
  })

  it('offers the claimed audio languages by name, and Unknown when a release claims none', () => {
    // Arrange
    const facets = aliensSearch.facets

    // Act
    renderFilters(facets)

    // Assert
    expect(optionsOf('Audio language')).toEqual(['All languages', 'English', 'Ukrainian', 'Unknown'])
  })

  it('offers the claimed resolutions, largest first, and Unknown when a release claims none', () => {
    // Arrange
    const facets = aliensSearch.facets

    // Act
    renderFilters(facets)

    // Assert
    expect(optionsOf('Quality')).toEqual(['All qualities', '2160p', '1080p', 'Unknown'])
  })

  it('leaves Unknown out of a filter when every release claims a value', () => {
    // Arrange
    const facets: SearchFacets = { audioLanguages: ['ukr'], hasUnknownAudioLanguage: false, resolutions: [1080], hasUnknownResolution: false, kinds: ['Movie'] }

    // Act
    renderFilters(facets)

    // Assert
    expect(optionsOf('Audio language')).toEqual(['All languages', 'Ukrainian'])
    expect(optionsOf('Quality')).toEqual(['All qualities', '1080p'])
  })

  it('offers every order the server sorts by, most seeders first', () => {
    // Arrange
    const facets = aliensSearch.facets

    // Act
    renderFilters(facets)

    // Assert
    expect(optionsOf('Sort')).toEqual(['Most seeders', 'Largest', 'Smallest', 'Newest', 'Best quality'])
    expect(screen.getByRole('combobox', { name: 'Sort' })).toHaveValue('seeders')
  })

  it('reports each choice with the value the server filters by', async () => {
    // Arrange
    const { changes, user } = renderFilters()

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Source' }), 'Toloka')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Audio language' }), 'Unknown')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Quality' }), '1080p')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort' }), 'Largest')

    // Assert
    expect(changes.at(-1)).toEqual({ ...noFilters, source: toloka.id, language: 'unknown', quality: '1080', sort: 'largest' })
  })

  it('keeps the sizes as typed in GiB, under one group', async () => {
    // Arrange
    const { changes, user } = renderFilters()

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Smallest size in GiB' }), '4.5')
    await user.type(screen.getByRole('textbox', { name: 'Largest size in GiB' }), '30')

    // Assert
    expect(changes.at(-1)).toEqual({ ...noFilters, minSize: '4.5', maxSize: '30' })
    expect(screen.getByRole('group', { name: 'Size, GiB' })).toContainElement(screen.getByRole('textbox', { name: 'Largest size in GiB' }))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says so under the row when a size is not one, and marks that box', async () => {
    // Arrange
    const { user } = renderFilters()

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Largest size in GiB' }), 'big')

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a size in GiB, such as 4.5.')
    // MAM-16 — the error line a form's field shows, under the row.
    expect(screen.getByRole('alert')).toHaveClass('ui-field-error')
    expect(screen.getByRole('textbox', { name: 'Largest size in GiB' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('textbox', { name: 'Largest size in GiB' })).toHaveAccessibleDescription('Enter a size in GiB, such as 4.5.')
    expect(screen.getByRole('textbox', { name: 'Smallest size in GiB' })).not.toHaveAttribute('aria-invalid')
  })

  it('offers the kinds the sources listed, and reports the one picked', async () => {
    // Arrange — MAM-32
    const { changes, user } = renderFilters({ ...aliensSearch.facets, kinds: ['Movie', 'Tv'] })

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'TV')

    // Assert
    expect(optionsOf('Kind')).toEqual(['Movies and TV', 'Movies', 'TV'])
    expect(changes.at(-1)).toEqual({ ...noFilters, kind: 'Tv' })
  })

  it('offers only the kind every release is', () => {
    // Arrange
    const facets = { ...aliensSearch.facets, kinds: ['Movie' as const] }

    // Act
    renderFilters(facets)

    // Assert
    expect(optionsOf('Kind')).toEqual(['Movies and TV', 'Movies'])
  })
})
