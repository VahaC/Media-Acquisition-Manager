import { render, screen, within } from '@testing-library/react'
import { LinkEvidenceBadges } from '@/components/library/LinkEvidenceBadges'
import { evidenceOf } from '@/test/library-links'

/** The evidence badges, in order, as their text. */
const badgeTexts = () =>
  within(screen.getByRole('list', { name: 'Why it is suggested' })).getAllByRole('listitem').map((item) => item.textContent)

describe('LinkEvidenceBadges', () => {
  it('names every piece that speaks for a suggestion', () => {
    // Arrange
    const evidence = evidenceOf({ sibling: { reason: 'SameDirector', siblingTitle: '119. Pecos Pest' } })

    // Act
    render(<LinkEvidenceBadges evidence={evidence} catalogKind="Movie" />)

    // Assert
    expect(badgeTexts()).toEqual([
      'Same title: Landing Stripling',
      'Same year',
      '6 min ≈ 6 min',
      'A film',
      'Animation, as its library',
      'Same director as 119. Pecos Pest',
    ])
    expect(screen.getByText('Same title: Landing Stripling')).toHaveAttribute('title', 'Closest: its original title')
  })

  it('marks what speaks against it in the warning tone', () => {
    // Arrange
    const evidence = evidenceOf({
      year: { libraryYear: 1962, catalogYear: 1965, difference: 3 },
      runtime: { libraryMinutes: 6, catalogMinutes: 90, agrees: false },
      kindAgrees: false,
      genre: { libraryRole: 'Animation', agreement: 'Disagrees' },
    })

    // Act
    render(<LinkEvidenceBadges evidence={evidence} catalogKind="Series" />)

    // Assert
    expect(badgeTexts()).toEqual([
      'Same title: Landing Stripling',
      '3 years apart',
      '6 min ≠ 90 min',
      'A show: the work links as its episode',
      'Not animation, unlike its library',
    ])
    for (const text of ['3 years apart', '6 min ≠ 90 min', 'Not animation, unlike its library']) {
      expect(screen.getByText(text)).toHaveClass('library-badge-warning')
    }
  })

  it('says what isn\'t known, and leaves the genre out when the library has none', () => {
    // Arrange
    const evidence = evidenceOf({
      title: { similarity: 0.614, exact: false, matchedKind: 'AlternativeTitle', matchedTitle: 'Planet Earth' },
      year: { libraryYear: null, catalogYear: 2006, difference: null },
      runtime: { libraryMinutes: null, catalogMinutes: 53, agrees: null },
      genre: { libraryRole: null, agreement: 'Unknown' },
    })

    // Act
    render(<LinkEvidenceBadges evidence={evidence} catalogKind="Movie" />)

    // Assert
    expect(badgeTexts()).toEqual(['Title 61% alike: Planet Earth', 'Year unknown', 'Runtime unknown', 'A film'])
  })

  it('counts one year apart in the singular, and not as a warning', () => {
    // Arrange
    const evidence = evidenceOf({ year: { libraryYear: 1962, catalogYear: 1963, difference: 1 } })

    // Act
    render(<LinkEvidenceBadges evidence={evidence} catalogKind="Movie" />)

    // Assert
    expect(screen.getByText('1 year apart')).not.toHaveClass('library-badge-warning')
  })
})
