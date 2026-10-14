import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReleaseDetailsDialog } from '@/components/search/ReleaseDetailsDialog'
import type { LibraryMatch, SearchRelease } from '@/lib/types'
import { aliens, continental } from '@/test/library'
import {
  aliensMatch,
  continentalMatch,
  otherUhd,
  releaseOf,
  tolokaRemux,
  untitledRip,
  yellowstoneEpisodes,
  yellowstoneMatch,
  yellowstonePack,
} from '@/test/search'

function renderDialog(release: SearchRelease, library: LibraryMatch[] = [aliensMatch]) {
  const onClose = vi.fn()
  render(<ReleaseDetailsDialog release={release} library={library} onClose={onClose} />)
  return { onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog') }
}

/** The facts the dialog read from the title, as `[term, value]` pairs: the list right under its heading. */
function readAs(dialog: HTMLElement): [string, string][] {
  const list = within(dialog).getByRole('heading', { name: 'Read from the title' }).nextElementSibling!
  return [...list.querySelectorAll('dt')].map((term) => [term.textContent!, term.nextElementSibling!.textContent!])
}

/** The facts list after the heading `name`, as `[term, value]` pairs. */
function factsUnder(dialog: HTMLElement, name: string): [string, string][] {
  const section = within(dialog).getByRole('heading', { name }).closest('section')!
  const terms = [...section.querySelector('dl')!.querySelectorAll('dt')]
  return terms.map((term) => [term.textContent!, term.nextElementSibling!.textContent!])
}

describe('ReleaseDetailsDialog', () => {
  it('is named by the release title and says where the listing comes from', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(dialog).toHaveAccessibleName(tolokaRemux.title)
    expect(dialog).toHaveAccessibleDescription(/^What Toloka lists for this release/)
  })

  it('shows the listing\'s own description under the title when it gives one', () => {
    // Arrange
    // MAM-33 — Toloka's topic title, where Prowlarr's title keeps the English part alone.
    const release = releaseOf({
      title: 'Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
      description: 'Чужі / Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
    })

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    const description = within(dialog).getByText(release.description!)
    expect(description).toHaveClass('search-release-description')
    expect(description.previousElementSibling).toBe(within(dialog).getByRole('heading', { name: release.title }))
    // The dialog's own description still says where the listing comes from.
    expect(dialog).toHaveAccessibleDescription(/^What Toloka lists for this release/)
  })

  it('shows no description line when the listing gives none', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(dialog.querySelector('.search-release-description')).toBeNull()
  })

  it('shows what the listing says, including how the release is downloaded', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(factsUnder(dialog, 'The listing')).toEqual([
      ['Source', 'Toloka'],
      ['Size', '29.5 GiB'],
      ['Seeders', '12'],
      ['Peers', '15'],
      ['Published', '9/28/2026, 9:30:00 PM'],
      ['Listed as', 'A movie'],
      ['Downloaded as', 'A torrent file'],
    ])
  })

  it('names the facts the listing left out as unknown', () => {
    // Arrange
    const release = otherUhd

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(factsUnder(dialog, 'The listing')).toEqual([
      ['Source', 'Other'],
      ['Size', 'Unknown'],
      ['Seeders', 'Unknown'],
      ['Peers', 'Unknown'],
      ['Published', 'Unknown'],
      ['Listed as', 'A movie'],
      ['Downloaded as', 'Known once it is fetched'],
    ])
  })

  it('shows what the title claims and what it was read as', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    const claimed = within(within(dialog).getByRole('list', { name: 'Claimed' })).getAllByRole('listitem')
    expect(claimed.map((chip) => chip.textContent)).toContain('BDRemux')
    const readAs = within(dialog).getByRole('heading', { name: 'Read from the title' }).nextElementSibling!
    expect([...readAs.querySelectorAll('dd')].map((value) => value.textContent)).toEqual(['Чужі · Aliens', '1986'])
  })

  it('says why a title without a year was read as nothing, and where a match comes from then', () => {
    // Arrange
    const release = untitledRip

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    // MAM-30 — no longer "by an id only": the description is read too.
    expect(within(dialog).getByText(
      'The title gives no year or season, so the app can\'t tell the name from the rest of it. Any match comes from the description or from an id in the listing.'))
      .toBeInTheDocument()
  })

  it('shows what the description was read as, beside what the title was', () => {
    // Arrange
    // MAM-30 — Jackett's title keeps the English part alone; Toloka's topic title, its description, names it in Ukrainian too.
    const release = releaseOf({
      title: 'Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
      description: 'Чужі / Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
      claimedWork: { titles: ['Aliens'], year: 1986, lastYear: null, episodes: null },
      descriptionWork: { titles: ['Чужі', 'Aliens'], year: 1986, lastYear: null, episodes: null },
    })

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(readAs(dialog)).toEqual([['Titles', 'Aliens'], ['Year', '1986']])
    const heading = within(dialog).getByRole('heading', { level: 3, name: 'Read from the description' })
    // Right after what the title was read as, in the listing's column.
    expect(heading.previousElementSibling).toBe(within(dialog).getByRole('heading', { name: 'Read from the title' }).nextElementSibling)
    const facts = heading.nextElementSibling!
    expect([...facts.querySelectorAll('dt')].map((term) => [term.textContent, term.nextElementSibling!.textContent]))
      .toEqual([['Titles', 'Чужі · Aliens'], ['Year', '1986']])
  })

  it('reads the seasons a TV release\'s description claims', () => {
    // Arrange
    const release = {
      ...yellowstonePack,
      descriptionWork: { titles: ['Єллоустоун', 'Yellowstone'], year: 2018, lastYear: 2024, episodes: yellowstonePack.claimedWork.episodes },
    }

    // Act
    const { dialog } = renderDialog(release, [yellowstoneMatch])

    // Assert
    const facts = within(dialog).getByRole('heading', { name: 'Read from the description' }).nextElementSibling!
    expect([...facts.querySelectorAll('dt')].map((term) => [term.textContent, term.nextElementSibling!.textContent])).toEqual([
      ['Titles', 'Єллоустоун · Yellowstone'],
      ['Year', '2018–2024'],
      ['Seasons', 'Seasons 1–5'],
    ])
  })

  it('says why a description without a year was read as nothing', () => {
    // Arrange
    const release = releaseOf({
      description: '«Чужі» BDRemux 1080p',
      descriptionWork: { titles: [], year: null, lastYear: null, episodes: null },
    })

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    const heading = within(dialog).getByRole('heading', { name: 'Read from the description' })
    expect(heading.nextElementSibling).toHaveTextContent(
      'The description gives no year or season, so the app can\'t tell the name from the rest of it.')
    expect(heading.nextElementSibling).toHaveClass('settings-muted')
  })

  it.each<[string, (release: SearchRelease) => void]>([
    ['the listing gives none', (release) => { release.descriptionWork = null }],
    // Until the server sends the field it is missing, not null.
    ['a server older than the field leaves it out', (release) => { delete (release as Partial<SearchRelease>).descriptionWork }],
  ])('shows no description block when %s', (_, change) => {
    // Arrange
    const release = releaseOf({ description: 'Чужі / Aliens (1986)' })
    change(release)

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(within(dialog).queryByRole('heading', { name: 'Read from the description' })).not.toBeInTheDocument()
    expect(within(dialog).queryByText(/^The description gives no year/)).not.toBeInTheDocument()
    expect(readAs(dialog)).toEqual([['Titles', 'Чужі · Aliens'], ['Year', '1986']])
  })

  it('lists the ids the listing gives, or says it gives none', () => {
    // Arrange
    const withIds = otherUhd

    // Act
    const { dialog } = renderDialog(withIds)

    // Assert
    const ids = within(within(dialog).getByRole('list', { name: 'Ids in the listing' })).getAllByRole('listitem')
    expect(ids.map((id) => id.textContent)).toEqual(['IMDb tt0090605'])
    expect(within(dialog).queryByText('The listing gives no IMDb, TMDb or TVDB id.')).not.toBeInTheDocument()
  })

  it('says when the listing gives no id', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(within(dialog).getByText('The listing gives no IMDb, TMDb or TVDB id.')).toBeInTheDocument()
  })

  it('shows every library work the release matches, strongest first, with why and its verified facts', () => {
    // Arrange
    const release = releaseOf({
      libraryMatches: [
        { workId: aliens.id, evidence: 'Identifier', conflictingSchemes: ['Tmdb'], heldEpisodeCount: null },
        { workId: continental.id, evidence: 'TitleOnly', conflictingSchemes: [], heldEpisodeCount: null },
      ],
    })

    // Act
    const { dialog } = renderDialog(release, [aliensMatch, continentalMatch])

    // Assert
    const library = within(dialog).getByRole('heading', { level: 3, name: 'In your library' }).closest('section')!
    const works = within(library).getAllByRole('heading', { level: 4 }).map((heading) => heading.textContent)
    expect(works).toEqual(['Чужі', 'The Continental'])
    const aliensBlock = within(library).getByRole('region', { name: 'Чужі' })
    const evidence = within(within(aliensBlock).getByRole('list', { name: 'Why it matched' })).getAllByRole('listitem')
    expect(evidence.map((item) => item.textContent)).toEqual(['Matched by id', 'Conflicting TMDb ids'])
    expect(within(aliensBlock).getByText('Original title: Aliens')).toBeInTheDocument()
    // The Library page's own version blocks: edition labels in the card's order, and what ffprobe read.
    const versions = [...within(aliensBlock).getByRole('list', { name: 'Versions' }).children]
    expect(versions.map((item) => item.querySelector('.library-version-label')?.textContent))
      .toEqual(['Special Edition', 'Theatrical Cut'])
    expect(within(aliensBlock).getByText('2:17:17')).toBeInTheDocument()
    expect(within(aliensBlock).getByText('HEVC Main 10')).toBeInTheDocument()
    const continentalBlock = within(library).getByRole('region', { name: 'The Continental' })
    expect(within(continentalBlock).getByText('Possible match — check it')).toBeInTheDocument()
  })

  it('says when the release matches nothing in the library', () => {
    // Arrange
    const release = untitledRip

    // Act
    const { dialog } = renderDialog(release)

    // Assert
    expect(within(dialog).getByText('Nothing in your library matches this release.')).toBeInTheDocument()
  })

  it('puts Open source first and Close last, and closes from Close', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog(tolokaRemux)
    // The footer's Close comes before the corner's ✕, which has the same name.
    const [close] = within(dialog).getAllByRole('button', { name: 'Close' })

    // Act
    await user.click(close)

    // Assert
    const footer = close.parentElement!
    expect([...footer.children].map((control) => control.textContent)).toEqual(['Open source', 'Close'])
    expect(within(footer).getByRole('link', { name: 'Open source' })).toHaveAttribute('href', 'https://toloka.to/t123456')
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('says a TV release is listed as TV, and reads its span of years and the seasons it claims', () => {
    // Arrange — MAM-32
    const release = yellowstonePack

    // Act
    const { dialog } = renderDialog(release, [yellowstoneMatch])

    // Assert
    expect(factsUnder(dialog, 'The listing')).toContainEqual(['Listed as', 'TV'])
    expect(readAs(dialog)).toEqual([
      ['Titles', 'Yellowstone'],
      ['Year', '2018–2024'],
      ['Seasons', 'Seasons 1–5'],
    ])
  })

  it('reads the episodes a TV release claims of its season', () => {
    // Arrange
    const release = yellowstoneEpisodes

    // Act
    const { dialog } = renderDialog(release, [yellowstoneMatch])

    // Assert
    expect(readAs(dialog)).toEqual([
      ['Titles', 'Yellowstone'],
      ['Year', '2018'],
      ['Seasons', 'Season 1'],
      ['Episodes', 'Episodes 1–9'],
    ])
    expect(within(dialog).getByText('TVDB 341164')).toBeInTheDocument()
  })

  it('shows a series the release matches with how many of its episodes the library holds, and its seasons', () => {
    // Arrange
    const release = yellowstonePack

    // Act
    const { dialog } = renderDialog(release, [yellowstoneMatch])

    // Assert
    const work = within(dialog).getByRole('heading', { level: 4, name: 'Єллоустоун' }).closest('section')!
    expect(within(work).getByText('Series')).toBeInTheDocument()
    expect(within(work).getByText('19 of its episodes in your library')).toBeInTheDocument()
    expect(within(within(work).getByRole('list', { name: 'Seasons' })).getAllByRole('button')).toHaveLength(4)
    expect(within(work).queryByText('No versions reported.')).not.toBeInTheDocument()
  })
})
