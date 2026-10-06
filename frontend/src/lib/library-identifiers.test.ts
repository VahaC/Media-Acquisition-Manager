import { groupedIdentifierKey, groupIdentifiers } from '@/lib/library-identifiers'
import { libraryIdentifierOf, linkedIdentifierOf } from '@/test/library-links'

describe('groupIdentifiers', () => {
  it('makes one entry of an id that two sources claim, with both sources in order', () => {
    // Arrange
    const reported = libraryIdentifierOf('Tmdb', '223021')
    const linked = linkedIdentifierOf('Tmdb', '223021')

    // Act
    const groups = groupIdentifiers([libraryIdentifierOf('Imdb', 'tt0056164'), linked, reported])

    // Assert
    expect(groups).toEqual([
      { scheme: 'Imdb', value: 'tt0056164', sources: [libraryIdentifierOf('Imdb', 'tt0056164')] },
      { scheme: 'Tmdb', value: '223021', sources: [linked, reported] },
    ])
  })

  it('keeps two values of one scheme apart', () => {
    // Arrange
    const identifiers = [libraryIdentifierOf('Tmdb', '999'), linkedIdentifierOf('Tmdb', '223021')]

    // Act
    const groups = groupIdentifiers(identifiers)

    // Assert
    expect(groups.map((group) => group.value)).toEqual(['999', '223021'])
  })
})

describe('groupedIdentifierKey', () => {
  it('names the sources, so an id whose sources change gets another key', () => {
    // Arrange
    const [alone] = groupIdentifiers([linkedIdentifierOf('Tmdb', '223021')])
    const [both] = groupIdentifiers([linkedIdentifierOf('Tmdb', '223021'), libraryIdentifierOf('Tmdb', '223021')])

    // Act
    const keys = [groupedIdentifierKey(alone), groupedIdentifierKey(both)]

    // Assert
    expect(keys).toEqual(['Tmdb|223021|Person:vasyl', 'Tmdb|223021|Person:vasyl|MediaLibrary:Home Jellyfin'])
  })
})
