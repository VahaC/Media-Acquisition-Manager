import { distinctPathEnds, editionsText, fileVersions, firstWork, groupHeading, groupName, identityText, tabFrom, tabQuery } from '@/lib/duplicates'
import {
  aliensHolder,
  daybreakFileName,
  daybreakHolderOf,
  duplicateFileOf,
  libraryFileOf,
  possibleGroupOf,
  sameFileGroup,
  sameWorkGroup,
} from '@/test/duplicates'

describe('tabFrom', () => {
  it.each([
    ['SameFile', 'SameFile'],
    ['Dismissed', 'Dismissed'],
    [null, 'All'],
    ['Hardlinks', 'All'],
    ['possible', 'All'],
  ])('reads ?tab=%s as the %s tab', (param, expected) => {
    // Arrange
    const asked = param

    // Act
    const tab = tabFrom(asked)

    // Assert
    expect(tab).toBe(expected)
  })
})

describe('tabQuery', () => {
  it.each([
    ['All', {}],
    ['SameFile', { class: 'SameFile' }],
    ['ByteIdentical', { class: 'ByteIdentical' }],
    ['SameWork', { class: 'SameWork' }],
    ['Possible', { class: 'Possible' }],
    ['Dismissed', { decision: 'Dismissed' }],
  ] as const)('asks the server for %s with %o', (tab, expected) => {
    // Arrange
    const of = tab

    // Act
    const query = tabQuery(of)

    // Assert
    expect(query).toEqual(expected)
  })
})

describe('firstWork', () => {
  it('picks the first work by title in Ukrainian order, Ґ after Г and Cyrillic before Latin, letter case ignored', () => {
    // Arrange
    const group = possibleGroupOf({
      files: [
        duplicateFileOf({ works: [{ ...aliensHolder, id: 'a', title: 'Aliens' }, { ...aliensHolder, id: 'b', title: 'ґава' }] }),
        duplicateFileOf({ works: [{ ...aliensHolder, id: 'c', title: 'Гнів' }] }),
      ],
    })

    // Act
    const work = firstWork(group)

    // Assert
    expect(work?.title).toBe('Гнів')
  })

  // MAM-21 — as the server orders the groups: an episode by the series its card is headed by, then its place.
  it('picks an episode by its series\' title, then its season and number, the specials after the numbered seasons', () => {
    // Arrange
    const group = possibleGroupOf({
      files: [
        duplicateFileOf({ works: [daybreakHolderOf({ id: 'a', title: 'Abyss', episode: { seriesId: null, seasonNumber: 0, episodeNumber: 1 } })] }),
        duplicateFileOf({ works: [daybreakHolderOf({ id: 'b', title: 'Kill the Messenger', episode: { seriesId: null, seasonNumber: 1, episodeNumber: 2 } })] }),
        duplicateFileOf({ works: [daybreakHolderOf({ id: 'c', title: 'Zebra', episode: { seriesId: null, seasonNumber: 1, episodeNumber: 1 } })] }),
        duplicateFileOf({ works: [{ ...aliensHolder, id: 'd', title: 'Яма' }] }),
      ],
    })

    // Act
    const work = firstWork(group)

    // Assert
    expect(work?.id).toBe('c')
  })

  it('has none for a group no library reports', () => {
    // Arrange
    const group = possibleGroupOf({ files: [duplicateFileOf({ works: [] })] })

    // Act
    const work = firstWork(group)

    // Assert
    expect(work).toBeNull()
  })
})

describe('groupHeading', () => {
  it('heads a film\'s group by its title, with its year under it', () => {
    // Arrange
    const group = sameFileGroup

    // Act
    const heading = groupHeading(group)

    // Assert
    expect(heading).toEqual({ title: 'Чужі', meta: ['1986'] })
  })

  it('names a work without a year as such', () => {
    // Arrange
    const group = possibleGroupOf({ files: [duplicateFileOf({ works: [{ ...aliensHolder, year: null }] })] })

    // Act
    const heading = groupHeading(group)

    // Assert
    expect(heading).toEqual({ title: 'Чужі', meta: ['Year unknown'] })
  })

  it('heads an episode\'s group by its series, with its season, episode and own title under it', () => {
    // Arrange
    const group = sameWorkGroup

    // Act
    const heading = groupHeading(group)

    // Assert
    expect(heading).toEqual({ title: 'Єллоустоун', meta: ['Season 1 · Episode 1', 'Daybreak'] })
  })

  it('heads an episode of no series by its own title, with where it sits and its year', () => {
    // Arrange
    const group = possibleGroupOf({
      files: [duplicateFileOf({
        works: [daybreakHolderOf({ title: 'Tom & Jerry Kids', year: 1990, seriesTitle: null, episode: { seriesId: null, seasonNumber: 1, episodeNumber: 4 } })],
      })],
    })

    // Act
    const heading = groupHeading(group)

    // Assert
    expect(heading).toEqual({ title: 'Tom & Jerry Kids', meta: ['Season 1 · Episode 4', '1990'] })
  })

  it('heads a possible match no library reports by the title and year it offers', () => {
    // Arrange
    const group = possibleGroupOf({ files: [duplicateFileOf({ works: [] }), duplicateFileOf({ works: [] })] })

    // Act
    const heading = groupHeading(group)

    // Assert
    expect(heading).toEqual({ title: 'Aliens (1986)', meta: [] })
  })

  it('heads another group no library reports by its first file\'s name', () => {
    // Arrange
    const group = { ...sameFileGroup, files: sameFileGroup.files.map((file) => ({ ...file, works: [] })) }

    // Act
    const heading = groupHeading(group)

    // Assert
    expect(heading).toEqual({ title: 'Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv', meta: [] })
  })
})

describe('groupName', () => {
  it.each([
    ['a film by its title and year', sameFileGroup, 'Чужі (1986)'],
    ['an episode by its series and place', sameWorkGroup, 'Єллоустоун (Season 1 · Episode 1)'],
    ['names no library reports by the title and year they offer', possibleGroupOf({ files: [duplicateFileOf({ works: [] })] }), 'Aliens (1986)'],
  ])('names %s', (_, group, expected) => {
    // Arrange
    const of = group

    // Act
    const name = groupName(of)

    // Assert
    expect(name).toBe(expected)
  })
})

describe('distinctPathEnds', () => {
  it.each([
    ['by its name alone when no other has it', ['/media/S03E05a.mkv', '/media/S03E05b.mkv'], ['S03E05a.mkv', 'S03E05b.mkv']],
    ['a hardlink\'s names by the folder each is in', ['/media/movies/Arrival (2016)/Arrival.mkv', '/media/hardlinks/Arrival.mkv'],
      ['Arrival (2016)/Arrival.mkv', 'hardlinks/Arrival.mkv']],
    ['by the nearest folder that differs, the ones between left out', ['/media/tv/Yellowstone/Season 1/S01E01.mkv', '/media/tv-4k/Yellowstone/Season 1/S01E01.mkv'],
      ['tv/…/S01E01.mkv', 'tv-4k/…/S01E01.mkv']],
    ['in a Windows path\'s own separators', ['D:\\Films\\HD\\Heat.mkv', 'D:\\Films\\4K\\Heat.mkv'], ['HD\\Heat.mkv', '4K\\Heat.mkv']],
    ['every folder from the one that differs where leaving them out would read alike',
      ['/a/x/m/n.mkv', '/a/y/m/n.mkv', '/b/x/m/n.mkv', '/b/y/m/n.mkv'],
      ['a/x/m/n.mkv', 'a/y/m/n.mkv', 'b/x/m/n.mkv', 'b/y/m/n.mkv']],
  ])('tells each path apart %s', (_, paths, expected) => {
    // Arrange
    const of = paths

    // Act
    const ends = distinctPathEnds(of)

    // Assert
    expect(ends).toEqual(expected)
  })
})

describe('fileVersions', () => {
  it('makes each file a version named by what tells its path apart, in the group\'s order', () => {
    // Arrange
    const group = sameWorkGroup

    // Act
    const versions = fileVersions(group)

    // Assert
    expect(versions).toEqual([
      { id: group.files[0].file.id, editionLabel: `tv/…/${daybreakFileName}`, files: [group.files[0].file] },
      { id: group.files[1].file.id, editionLabel: `tv-4k/…/${daybreakFileName}`, files: [group.files[1].file] },
    ])
  })
})

describe('identityText', () => {
  it.each([
    ['its device, inode and links', duplicateFileOf({}), 'Device 2049 · inode 1311 · 2 links'],
    ['a single link', duplicateFileOf({ linkCount: 1 }), 'Device 2049 · inode 1311 · 1 link'],
    ['links it doesn\'t know', duplicateFileOf({ linkCount: null }), 'Device 2049 · inode 1311 · links unknown'],
    ['that the app hasn\'t looked yet', duplicateFileOf({ deviceId: null, inode: null, file: libraryFileOf({ inspection: null }) }), 'Not looked at yet'],
    ['that a look found no identity', duplicateFileOf({ deviceId: null, inode: null }), 'Unknown'],
  ])('says %s', (_, file, expected) => {
    // Arrange
    const of = file

    // Act
    const text = identityText(of)

    // Assert
    expect(text).toBe(expected)
  })
})

describe('editionsText', () => {
  it('names each edition in the user\'s words', () => {
    // Arrange
    const editions = ['DirectorsCut', 'Imax'] as const

    // Act
    const text = editionsText(editions)

    // Assert
    expect(text).toBe('Director\'s cut · IMAX')
  })

  it('says a file\'s names name none', () => {
    // Arrange
    const editions = [] as const

    // Act
    const text = editionsText(editions)

    // Assert
    expect(text).toBe('None in its names')
  })
})
