import { render, screen, within } from '@testing-library/react'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import type { LibraryWork } from '@/lib/types'
import { aliens, tomAndJerryKids, yellowstone } from '@/test/library'

// MAM-32 — a series and an episode of no series as Library cards. Opening a season asks the server,
// which SeriesSeasons.test.tsx covers; these cards are only looked at.

function renderCard(work: LibraryWork) {
  render(<LibraryWorkCard work={work} />)
  return screen.getByRole('article')
}

/** The badges of the card's identifiers list, as text. */
const badges = () =>
  within(screen.getByRole('list', { name: 'Identifiers' })).getAllByRole('listitem').map((item) => item.textContent)

describe('LibraryWorkCard of a series or an episode', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('shows a series with its title, kind and year, and a show\'s ids by their names', () => {
    // Arrange
    const work = yellowstone

    // Act
    const card = renderCard(work)

    // Assert
    expect(card).toHaveAccessibleName('Єллоустоун')
    expect(within(card).getByText('Series')).toBeInTheDocument()
    expect(within(card).getByText('Original title: Yellowstone')).toBeInTheDocument()
    expect(within(card).getByText('2018')).toBeInTheDocument()
    expect(within(card).getByText('From Home Jellyfin')).toBeInTheDocument()
    expect(badges()).toEqual(['IMDb tt4236770', 'TVDB 341164', 'No TMDb TV id'])
  })

  it('lists a series\' seasons in place of versions, each with its episode count and closed', () => {
    // Arrange
    const work = yellowstone

    // Act
    renderCard(work)

    // Assert
    const seasons = within(screen.getByRole('list', { name: 'Seasons' })).getAllByRole('button')
    expect(seasons.map((season) => season.textContent)).toEqual([
      'Season 19 episodes',
      'Season 210 episodes',
      'Specials1 episode',
      'No season2 episodes',
    ])
    expect(seasons.every((season) => season.getAttribute('aria-expanded') === 'false')).toBe(true)
    expect(screen.queryByRole('list', { name: 'Versions' })).not.toBeInTheDocument()
    expect(screen.queryByRole('radiogroup', { name: 'Show versions as' })).not.toBeInTheDocument()
  })

  it('says so when a series has no season to list', () => {
    // Arrange
    const work: LibraryWork = { ...yellowstone, seasons: [] }

    // Act
    renderCard(work)

    // Assert
    expect(screen.getByText('No episodes reported.')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Seasons' })).not.toBeInTheDocument()
  })

  it('says where an episode of no series sits, and flags the ids of its kind it lacks', () => {
    // Arrange
    const work = tomAndJerryKids

    // Act
    const card = renderCard(work)

    // Assert
    expect(card).toHaveAccessibleName('Tom & Jerry Kids')
    expect(within(card).getByText('Season 1 · Episode 4')).toBeInTheDocument()
    expect(within(card).queryByText('Series')).not.toBeInTheDocument()
    expect(badges()).toEqual(['No IMDb id', 'No TVDB id'])
  })

  it('offers no table for more versions than it compares, and lists them as rows though the table is chosen', () => {
    // Arrange — seven copies; the table compares two to six. The table is this browser's choice, so
    // the rows come from the count alone.
    localStorage.setItem('mam:versionsView', 'table')
    const work = tomAndJerryKids

    // Act
    renderCard(work)

    // Assert
    expect(screen.queryByRole('radiogroup', { name: 'Show versions as' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table', { name: 'Versions' })).not.toBeInTheDocument()
    const list = screen.getByRole('list', { name: 'Versions' })
    expect(within(list).getAllByRole('button')).toHaveLength(7)
  })

  it('still offers the table for six versions, and shows it when it is chosen', () => {
    // Arrange
    localStorage.setItem('mam:versionsView', 'table')
    const work: LibraryWork = { ...tomAndJerryKids, versions: tomAndJerryKids.versions.slice(0, 6) }

    // Act
    renderCard(work)

    // Assert
    expect(screen.getByRole('radiogroup', { name: 'Show versions as' })).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Versions' })).toBeInTheDocument()
  })

  it('gives a movie neither a kind nor a place', () => {
    // Arrange
    const work = aliens

    // Act
    const card = renderCard(work)

    // Assert
    expect(within(card).queryByText('Series')).not.toBeInTheDocument()
    expect(within(card).queryByText(/Episode/)).not.toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Seasons' })).not.toBeInTheDocument()
  })
})
