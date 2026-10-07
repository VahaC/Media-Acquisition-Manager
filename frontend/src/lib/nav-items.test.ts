import { NAV_ITEMS, navItemsFor } from '@/lib/nav-items'
import { member, owner } from '@/test/auth-session'

describe('navItemsFor', () => {
  it('offers Search, the Library, Duplicates, Recommendations, then Jobs in the header to every signed-in user, and the TMDb page after the Library to the installation owner only', () => {
    // Arrange
    const users = [owner, member]

    // Act
    const header = users.map((user) => navItemsFor('header', user).map((item) => item.path))

    // Assert
    expect(header).toEqual([
      ['/search', '/library', '/library/tmdb', '/library/duplicates', '/recommendations', '/jobs'],
      ['/search', '/library', '/library/duplicates', '/recommendations', '/jobs'],
    ])
  })

  it('offers Profile first in the user menu to every signed-in user, then Integrations, Destinations, Media folders and Users to the installation owner only', () => {
    // Arrange
    const users = [owner, member]

    // Act
    const menu = users.map((user) => navItemsFor('userMenu', user).map((item) => item.path))

    // Assert
    expect(menu).toEqual([
      ['/profile', '/settings/integrations', '/settings/destinations', '/settings/media-folders', '/settings/users'],
      ['/profile'],
    ])
  })

  it('lists every page once', () => {
    // Arrange
    const paths = NAV_ITEMS.map((item) => item.path)

    // Act
    const distinct = new Set(paths)

    // Assert
    expect(distinct.size).toBe(paths.length)
  })
})
