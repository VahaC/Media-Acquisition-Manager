import { cartoons, movies, sectionOf, sectionsOf, tolokaSections } from '@/test/destinations'
import { sectionKey, sectionRequests, sectionsMappedTo } from './destination-sections'

describe('destination sections', () => {
  it('keys a section by its source and its category ids, so two parts of one name stay apart', () => {
    // Arrange — «Мультсеріали» with one id mapped to Cartoons and the others to none.
    const mapped = sectionOf('Мультсеріали', ['100044'], { destinationId: cartoons.id })
    const unmapped = sectionOf('Мультсеріали', ['100125', '100195'])

    // Act
    const keys = [sectionKey(tolokaSections.id, mapped), sectionKey(tolokaSections.id, unmapped)]

    // Assert
    expect(keys).toEqual([`${tolokaSections.id}\n100044`, `${tolokaSections.id}\n100125,100195`])
  })

  it('ticks the sections mapped to the destination being edited, and none for a new one', () => {
    // Arrange
    const list = sectionsOf(tolokaSections)

    // Act
    const forMovies = sectionsMappedTo(list, movies.id)
    const forNew = sectionsMappedTo(list, undefined)

    // Assert
    expect([...forMovies]).toEqual([`${tolokaSections.id}\n100016`, `${tolokaSections.id}\n2040`])
    expect(forNew.size).toBe(0)
  })

  it('sends each ticked section as the list gave it, in the list\'s order', () => {
    // Arrange
    const list = sectionsOf(tolokaSections)
    const picked = new Set([`${tolokaSections.id}\n5000`, `${tolokaSections.id}\n100044,100125,100195`])

    // Act
    const requests = sectionRequests(list, picked)

    // Assert
    expect(requests).toEqual([
      { releaseSourceId: tolokaSections.id, categoryIds: ['100044', '100125', '100195'], name: 'Мультсеріали', isSourceSpecific: true },
      { releaseSourceId: tolokaSections.id, categoryIds: ['5000'], name: 'TV', isSourceSpecific: false },
    ])
  })
})
