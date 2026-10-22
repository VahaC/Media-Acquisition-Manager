import { releaseName } from './release-name'

describe('releaseName', () => {
  it('is the listing\'s description when the listing gave one', () => {
    // Arrange
    // A Ukrainian-only Toloka release, whose title "Strip Cyrillic Letters" left as punctuation.
    const release = { title: '\' (2016) HDTV 1080p', description: 'Сім\'я (2016) HDTV 1080p' }

    // Act
    const name = releaseName(release)

    // Assert
    expect(name).toBe('Сім\'я (2016) HDTV 1080p')
  })

  it('is the title when the listing gave no description', () => {
    // Arrange
    const release = { title: 'Aliens.1986.2160p.UHD.BluRay.x265-GROUP', description: null }

    // Act
    const name = releaseName(release)

    // Assert
    expect(name).toBe('Aliens.1986.2160p.UHD.BluRay.x265-GROUP')
  })
})
