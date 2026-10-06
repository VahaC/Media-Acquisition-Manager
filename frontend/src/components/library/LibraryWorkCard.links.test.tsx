import { render, screen, within } from '@testing-library/react'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import { Button } from '@/components/ui/button'
import { formatDateTime } from '@/lib/format'
import type { LibraryWork } from '@/lib/types'
import { yellowstone } from '@/test/library'
import { libraryIdentifierOf, linkedIdentifierOf, linkedLandingStripling, planetEarthJungles, workLinkOf } from '@/test/library-links'

// MAM-34 — a work linked to TMDb by hand on its card: the linked ids with who set them, one badge per
// id whatever claims it, where the link stands against Jellyfin, a conflict, an episode-to-show link,
// and the card's actions.

const setOn = formatDateTime('2026-10-05T18:40:00Z', { dateStyle: 'medium', timeStyle: 'short' })

/** The card's identifier badges, in order, as their text. */
const idBadges = () =>
  within(screen.getByRole('list', { name: 'Identifiers' })).getAllByRole('listitem').map((item) => item.textContent)

describe('LibraryWorkCard with a TMDb link', () => {
  it('names who set a linked id and when on hover, and TMDb for the IMDb id it gave', () => {
    // Arrange
    const work = linkedLandingStripling

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(screen.getByText('TMDb 223021')).toHaveAttribute('title', `Set by vasyl on ${setOn}`)
    expect(screen.getByText('IMDb tt0056164')).toHaveAttribute('title', 'Reported by TMDb')
  })

  it('says "set by hand" without a name to a member, who gets none', () => {
    // Arrange
    const work: LibraryWork = {
      ...linkedLandingStripling,
      identifiers: [linkedIdentifierOf('Tmdb', '223021', { sourceName: null })],
      link: workLinkOf({ setByUserName: null }),
    }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(screen.getByText('TMDb 223021')).toHaveAttribute('title', `Set by hand on ${setOn}`)
  })

  it('shows an id Jellyfin reports and the owner linked as one badge naming both', () => {
    // Arrange — the linked id written into Jellyfin, which the next sync read back.
    const work: LibraryWork = {
      ...linkedLandingStripling,
      identifiers: [libraryIdentifierOf('Tmdb', '223021'), linkedIdentifierOf('Tmdb', '223021')],
      link: workLinkOf({ writeState: 'InLibrary' }),
    }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(idBadges()).toEqual(['TMDb 223021', 'In Jellyfin'])
    expect(screen.getByText('TMDb 223021')).toHaveAttribute('title', `Reported by Home Jellyfin · Set by vasyl on ${setOn}`)
  })

  it('keeps "From" for the library a series came from, never the user who linked it', () => {
    // Arrange — a series has no versions, and its first id is the one set by hand.
    const work: LibraryWork = {
      ...yellowstone,
      identifiers: [linkedIdentifierOf('Imdb', 'tt4236770'), libraryIdentifierOf('Tvdb', '341164')],
    }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(screen.getByText('From Home Jellyfin')).toBeInTheDocument()
    expect(screen.queryByText('From vasyl')).not.toBeInTheDocument()
  })

  it.each([
    ['Written', 'Written to Jellyfin'],
    ['InLibrary', 'In Jellyfin'],
    ['Lost', 'Jellyfin lost it'],
    ['KeptInApp', 'Kept in the app'],
    ['Queued', 'Waiting to be written'],
    ['Writing', 'Writing…'],
  ] as const)('shows a link %s as "%s"', (writeState, text) => {
    // Arrange
    const work: LibraryWork = { ...linkedLandingStripling, link: workLinkOf({ writeState }) }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(idBadges()).toContain(text)
  })

  it('shows a link not written yet as a warning with the owner\'s sentence on hover', () => {
    // Arrange
    const writeError = 'Jellyfin refused to change the item\'s ids. Check that the API key belongs to an administrator.'
    const work: LibraryWork = { ...linkedLandingStripling, link: workLinkOf({ writeState: 'NotWritten', writeError }) }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    const badge = screen.getByText('Not in Jellyfin yet')
    expect(badge).toHaveClass('library-badge-warning')
    expect(badge).toHaveAttribute('title', writeError)
  })

  it('warns when Jellyfin holds a third TMDb id beside the linked one', () => {
    // Arrange
    const work: LibraryWork = {
      ...linkedLandingStripling,
      identifiers: [libraryIdentifierOf('Tmdb', '999'), linkedIdentifierOf('Tmdb', '223021')],
      link: workLinkOf({ writeState: 'Lost', conflictingValue: '999' }),
    }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(idBadges()).toEqual(['TMDb 999', 'TMDb 223021', 'Jellyfin lost it', 'TMDb ids disagree: 999 in Jellyfin, 223021 linked'])
  })

  it.each([
    [1, 8, 'Episode of Планета Земля · S1 E8'],
    [1, null, 'Episode of Планета Земля · S1'],
    [null, null, 'Episode of Планета Земля'],
  ] as const)('says a film linked as season %s episode %s of a show is "%s"', (seasonNumber, episodeNumber, line) => {
    // Arrange
    const work: LibraryWork = {
      ...planetEarthJungles,
      // A linked work is never missing an id (decision 9), an episode link included.
      missingIdentifiers: [],
      link: workLinkOf({
        target: 'ShowEpisode',
        catalogKind: 'Series',
        catalogId: '1044',
        catalogTitle: 'Планета Земля',
        seasonNumber,
        episodeNumber,
        imdbId: null,
        writeState: 'KeptInApp',
      }),
    }

    // Act
    render(<LibraryWorkCard work={work} />)

    // Assert
    expect(screen.getByText(line)).toBeInTheDocument()
    expect(idBadges()).toEqual(['Kept in the app'])
  })

  it('shows the actions it is given under the badges, even with no badge to show', () => {
    // Arrange
    const work: LibraryWork = { ...linkedLandingStripling, identifiers: [], missingIdentifiers: [], link: null }

    // Act
    render(<LibraryWorkCard work={work} actions={<Button type="button">TMDb…</Button>} />)

    // Assert
    expect(screen.queryByRole('list', { name: 'Identifiers' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'TMDb…' }).closest('.library-work-side')).not.toBeNull()
  })
})
