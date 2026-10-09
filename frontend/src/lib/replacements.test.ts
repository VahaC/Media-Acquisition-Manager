import {
  heldTorrentsOf,
  holdingClients,
  replacementGroups,
  seasonsOf,
  tickableIds,
  tickedInOrder,
  versionLabels,
  workNames,
  worksToConfirm,
} from '@/lib/replacements'
import { continental } from '@/test/library'
import {
  aliensWorkOf,
  aThundering,
  copyOf,
  daybreak,
  killTheMessenger,
  optionsUnavailable,
  replacementOptionsOf,
  seasonOneTorrent,
  specialCopy,
  theatricalCopy,
  yellowstoneWorkOf,
} from '@/test/replacements'

// MAM-24 — how the Download dialog reads the server's answer about old copies: what may be ticked,
// what is sent, and how works, seasons, versions and the summary's groups are named.

describe('tickableIds', () => {
  it('lets the owner tick only the copies with no reason of their own or of their work', () => {
    // Arrange
    const options = replacementOptionsOf([aliensWorkOf(), yellowstoneWorkOf()])

    // Act
    const ids = tickableIds(options)

    // Assert
    expect([...ids]).toEqual([theatricalCopy.file.id, ...daybreak.copies.map((copy) => copy.file.id), killTheMessenger.copies[0].file.id])
  })

  it('lets nothing be ticked of a work that can\'t be replaced, or while the release can\'t be', () => {
    // Arrange
    const titleOnly = replacementOptionsOf([aliensWorkOf({ unavailable: 'TitleOnly' })])

    // Act
    const ids = [tickableIds(titleOnly), tickableIds(optionsUnavailable('SwitchedOff')), tickableIds(undefined)]

    // Assert
    expect(ids.map((set) => set.size)).toEqual([0, 0, 0])
  })
})

describe('tickedInOrder', () => {
  it('sends the ticked copies in the order the dialog lists them, each once', () => {
    // Arrange
    const shared = copyOf(theatricalCopy.file, theatricalCopy.versionId)
    const options = replacementOptionsOf([yellowstoneWorkOf(), aliensWorkOf({ copies: [shared] }), aliensWorkOf({ workId: continental.id, copies: [shared] })])
    const ticked = new Set([theatricalCopy.file.id, killTheMessenger.copies[0].file.id, daybreak.copies[0].file.id])

    // Act
    const ids = tickedInOrder(options, ticked)

    // Assert
    expect(ids).toEqual([daybreak.copies[0].file.id, killTheMessenger.copies[0].file.id, theatricalCopy.file.id])
  })
})

describe('worksToConfirm', () => {
  it('asks for the confirmation of a title-and-year match only once one of its copies is ticked', () => {
    // Arrange
    const options = replacementOptionsOf([aliensWorkOf(), yellowstoneWorkOf()])

    // Act
    const none = worksToConfirm(options, new Set([daybreak.copies[0].file.id]))
    const aliensTicked = worksToConfirm(options, new Set([theatricalCopy.file.id]))

    // Assert
    expect(none).toEqual([])
    expect(aliensTicked.map((work) => work.confirmationText)).toEqual(['This release is Чужі (1986)'])
  })
})

describe('workNames', () => {
  it('names a film by its title and year, and a series by its title', () => {
    // Arrange
    const works = [aliensWorkOf(), aliensWorkOf({ workId: continental.id, title: 'The Continental', year: null }), yellowstoneWorkOf()]

    // Act
    const names = workNames(works)

    // Assert
    expect(names).toEqual(['Чужі (1986)', 'The Continental (year unknown)', 'Єллоустоун'])
  })

  it('tells two works that would read alike apart by the libraries holding them', () => {
    // Arrange
    const works = [aliensWorkOf(), aliensWorkOf({ workId: continental.id, libraryNames: ['Кіно', 'Архів'] })]

    // Act
    const names = workNames(works)

    // Assert
    expect(names).toEqual(['Чужі (1986) in Фільми', 'Чужі (1986) in Кіно · Архів'])
  })

  it('tells two works of one library apart by their first copy\'s file name, as the server does', () => {
    // Arrange
    const other = { ...specialCopy, file: { ...specialCopy.file, appPath: '/media/movies/Aliens (1986)/Aliens.1986.Remux.mkv' } }
    const works = [aliensWorkOf({ copies: [theatricalCopy] }), aliensWorkOf({ workId: continental.id, copies: [other] })]

    // Act
    const names = workNames(works)

    // Assert
    expect(names).toEqual([
      `Чужі (1986) in Фільми (the copy “${theatricalCopy.file.appPath!.split('/').at(-1)}”)`,
      'Чужі (1986) in Фільми (the copy “Aliens.1986.Remux.mkv”)',
    ])
  })

  it('numbers two works of one library whose first copies share a file name', () => {
    // Arrange
    const works = [aliensWorkOf({ copies: [theatricalCopy] }), aliensWorkOf({ workId: continental.id, copies: [theatricalCopy] })]

    // Act
    const names = workNames(works)

    // Assert
    expect(names).toEqual(['Чужі (1986) in Фільми (1 of 2)', 'Чужі (1986) in Фільми (2 of 2)'])
  })
})

describe('seasonsOf', () => {
  it('groups a series\' episodes by season, in the server\'s order', () => {
    // Arrange
    const work = yellowstoneWorkOf()

    // Act
    const seasons = seasonsOf(work)

    // Assert
    expect(seasons.map((season) => [season.number, season.episodes.map((episode) => episode.title)])).toEqual([
      [1, ['Daybreak', 'Kill the Messenger']],
      [2, ['A Thundering']],
    ])
  })
})

describe('versionLabels', () => {
  it('names each version by its label, else by its number in the Library\'s order, once for its several files', () => {
    // Arrange
    const secondFile = copyOf({ ...specialCopy.file, id: 'f11e0000-0000-4000-8000-000000000001' }, daybreak.copies[1].versionId)

    // Act
    const labels = versionLabels([daybreak.copies[0], theatricalCopy, daybreak.copies[1], secondFile])

    // Assert
    expect([...labels.entries()]).toEqual([
      [daybreak.copies[0].versionId, 'Version 1'],
      [theatricalCopy.versionId, 'Theatrical Cut'],
      [daybreak.copies[1].versionId, 'Version 3'],
    ])
  })
})

describe('heldTorrentsOf', () => {
  it('counts the copies of a work each torrent holds, leaving out a torrent whose file is only another name', () => {
    // Arrange
    const work = yellowstoneWorkOf({
      episodes: [daybreak, killTheMessenger, { ...aThundering, copies: [{ ...aThundering.copies[0], holds: [{ clientName: 'Local qBittorrent', torrentId: 'b2c3d4e5f60718293a4b5c6d7e8f900112233445', torrentName: 'Season.2.Pack', secondName: true }] }] }],
    })

    // Act
    const held = heldTorrentsOf(work)

    // Assert
    expect(held).toEqual([{ clientName: 'Local qBittorrent', torrentId: seasonOneTorrent.torrentId, torrentName: 'Yellowstone.S01.1080p.WEB-DL', count: 2 }])
  })

  it('counts two torrents of one name apart, since the owner removes each on its own', () => {
    // Arrange
    const crossSeed = { ...seasonOneTorrent, torrentId: '1122334455667788990011223344556677889900' }
    const work = yellowstoneWorkOf({
      episodes: [daybreak, { ...killTheMessenger, copies: [{ ...killTheMessenger.copies[0], holds: [crossSeed] }] }, aThundering],
    })

    // Act
    const held = heldTorrentsOf(work)

    // Assert
    expect(held.map((torrent) => [torrent.torrentId, torrent.torrentName, torrent.count])).toEqual([
      [seasonOneTorrent.torrentId, 'Yellowstone.S01.1080p.WEB-DL', 1],
      [crossSeed.torrentId, 'Yellowstone.S01.1080p.WEB-DL', 1],
    ])
  })
})

describe('holdingClients', () => {
  it('names each client whose torrent holds a ticked copy once, leaving out second names and unticked copies', () => {
    // Arrange
    const seedbox = { clientName: 'Seedbox qBittorrent', torrentId: 'c3d4e5f60718293a4b5c6d7e8f90011223344556', torrentName: 'Aliens.1986.Theatrical', secondName: false }
    const linked = { clientName: 'NAS qBittorrent', torrentId: 'd4e5f60718293a4b5c6d7e8f9001122334455667', torrentName: 'Aliens.1986.Linked', secondName: true }
    const options = replacementOptionsOf([
      aliensWorkOf({ copies: [{ ...theatricalCopy, holds: [seedbox, linked] }, specialCopy] }),
      yellowstoneWorkOf(),
    ])
    const ticked = new Set([theatricalCopy.file.id, daybreak.copies[0].file.id, killTheMessenger.copies[0].file.id])

    // Act
    const clients = holdingClients(options, ticked)

    // Assert
    expect(clients).toEqual(['Seedbox qBittorrent', 'Local qBittorrent'])
    expect(holdingClients(options, new Set([daybreak.copies[1].file.id]))).toEqual([])
  })
})

describe('replacementGroups', () => {
  it('groups the ticked copies by film and by season, with their paths in the app', () => {
    // Arrange
    const options = replacementOptionsOf([aliensWorkOf(), yellowstoneWorkOf()])
    const ticked = new Set([theatricalCopy.file.id, daybreak.copies[0].file.id, killTheMessenger.copies[0].file.id])

    // Act
    const groups = replacementGroups(options, ticked)

    // Assert
    expect(groups).toEqual([
      { name: 'Чужі (1986)', paths: [theatricalCopy.file.appPath] },
      { name: 'Єллоустоун, Season 1', paths: [daybreak.copies[0].file.appPath, killTheMessenger.copies[0].file.appPath] },
    ])
  })
})
