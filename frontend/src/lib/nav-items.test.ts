import { NAV_ITEMS, navItemsFor } from '@/lib/nav-items'
import { member, owner } from '@/test/auth-session'

describe('navItemsFor', () => {
  it('offers Search, the Library, then Jobs in the header to every signed-in user', () => {
    // Arrange
    const users = [owner, member]

    // Act
    const header = users.map((user) => navItemsFor('header', user).map((item) => item.path))

    // Assert
    expect(header).toEqual([['/search', '/library', '/jobs'], ['/search', '/library', '/jobs']])
  })

  it('offers Integrations, Destinations, Media folders and Users in the user menu to the installation owner only', () => {
    // Arrange
    const users = [owner, member]

    // Act
    const menu = users.map((user) => navItemsFor('userMenu', user).map((item) => item.path))

    // Assert
    expect(menu).toEqual([['/settings/integrations', '/settings/destinations', '/settings/media-folders', '/settings/users'], []])
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
