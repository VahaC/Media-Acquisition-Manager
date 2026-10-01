import { render, screen, within } from '@testing-library/react'
import { MatchBadges } from '@/components/search/MatchBadges'
import { aliens } from '@/test/library'

/** The badges, in order, as their text. */
const badgeTexts = () =>
  within(screen.getByRole('list', { name: 'Why it matched' })).getAllByRole('listitem').map((item) => item.textContent)

describe('MatchBadges', () => {
  it('names each id the work matched by, with its value and source on hover', () => {
    // Arrange
    const [imdb, tmdb] = aliens.identifiers

    // Act
    render(<MatchBadges evidence="Identifier" conflictingSchemes={[]} matchedIdentifiers={[imdb, tmdb]} />)

    // Assert
    expect(badgeTexts()).toEqual(['Matched by IMDb id', 'Matched by TMDb id'])
    expect(screen.getByText('Matched by IMDb id')).toHaveAttribute('title', 'IMDb tt0090605, reported by Home Jellyfin')
    expect(screen.getByText('Matched by TMDb id')).toHaveAttribute('title', 'TMDb 679, reported by Home Jellyfin')
  })

  it('names an id match without its scheme when the matched ids are not given', () => {
    // Arrange
    const evidence = 'Identifier' as const

    // Act
    render(<MatchBadges evidence={evidence} conflictingSchemes={[]} />)

    // Assert
    expect(badgeTexts()).toEqual(['Matched by id'])
  })

  it('shows a title and year match with the matched title, whose kind and source show on hover', () => {
    // Arrange
    const matchedTitle = { text: 'Aliens', kind: 'Original' as const, year: 1986, sourceName: 'Home Jellyfin' }

    // Act
    render(<MatchBadges evidence="TitleAndYear" conflictingSchemes={[]} matchedTitle={matchedTitle} />)

    // Assert
    expect(badgeTexts()).toEqual(['Matched by title and year', '“Aliens”, 1986'])
    expect(screen.getByText('“Aliens”, 1986')).toHaveAttribute('title', 'The original title, reported by Home Jellyfin')
    expect(screen.getByText('Matched by title and year')).toHaveClass('ui-badge', 'ui-badge-secondary')
  })

  it('calls a title-only match a possible one to check, in the warning tone', () => {
    // Arrange
    const matchedTitle = { text: 'Чужі', kind: 'Display' as const, year: null, sourceName: null }

    // Act
    render(<MatchBadges evidence="TitleOnly" conflictingSchemes={[]} matchedTitle={matchedTitle} />)

    // Assert
    expect(badgeTexts()).toEqual(['Possible match — check it', '“Чужі”'])
    expect(screen.getByText('Possible match — check it')).toHaveClass('ui-badge', 'library-badge-warning')
    expect(screen.getByText('“Чужі”')).toHaveAttribute('title', 'The title the library shows')
  })

  it('flags every scheme whose ids conflict, saying nothing was merged', () => {
    // Arrange
    const conflictingSchemes = ['Imdb', 'Tmdb'] as const

    // Act
    render(<MatchBadges evidence="TitleAndYear" conflictingSchemes={[...conflictingSchemes]} />)

    // Assert
    expect(badgeTexts()).toEqual(['Matched by title and year', 'Conflicting IMDb ids', 'Conflicting TMDb ids'])
    expect(screen.getByText('Conflicting IMDb ids')).toHaveClass('library-badge-warning')
    expect(screen.getByText('Conflicting IMDb ids').getAttribute('title')).toMatch(/Nothing was merged/)
  })

  it.each([
    ['Identifier', 'In your library: Чужі (1986) — matched by id'],
    ['TitleAndYear', 'In your library: Чужі (1986) — matched by title and year'],
    ['TitleOnly', 'Possible match in your library: Чужі (1986)'],
  ] as const)('says a release is in the library and names the copy in the text a phone shows (%s)', (evidence, text) => {
    // Arrange
    const inLibraryOf = 'Чужі (1986)'

    // Act
    render(<MatchBadges evidence={evidence} conflictingSchemes={[]} inLibraryOf={inLibraryOf} />)

    // Assert
    expect(badgeTexts()).toEqual([text])
    expect(screen.getByText(text)).not.toHaveAttribute('title')
  })
})
