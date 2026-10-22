import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReleaseRow } from '@/components/search/ReleaseRow'
import type { LibraryMatch, SearchRelease } from '@/lib/types'
import { aliens, unknownWork } from '@/test/library'
import {
  aliensMatch,
  firstBloodByWords,
  otherUhd,
  ramboIIIByWords,
  ramboIIIRip,
  ramboIVMatch,
  releaseOf,
  tolokaRemux,
  untitledRip,
  yellowstoneEpisodes,
  yellowstoneMatch,
  yellowstonePack,
} from '@/test/search'

function renderRow(release: SearchRelease, library: LibraryMatch[] = [aliensMatch]) {
  const onDetails = vi.fn()
  const onDownload = vi.fn()
  render(<ReleaseRow release={release} library={library} onDetails={onDetails} onDownload={onDownload} />)
  return { onDetails, onDownload, user: userEvent.setup(), row: screen.getByRole('article') }
}

/** The texts of the row's library badges, in order. */
const badgeTexts = () =>
  within(screen.getByRole('list', { name: 'Why it matched' })).getAllByRole('listitem').map((item) => item.textContent)

/** The groups of the row's claims line — the picture, the audio, the subtitles — in order. */
const claimedGroups = () =>
  within(screen.getByRole('list', { name: 'Claimed' })).getAllByRole('listitem').map((item) => item.textContent)

const SEEDERS_HINT = 'Seeders: people who have the whole release and are sharing it now. The more there are, the faster it downloads.'
const PEERS_HINT = 'Peers: everyone connected to this torrent, seeders and those still downloading together, as the source counts them.'

describe('ReleaseRow', () => {
  it('shows the title as a heading, and the seeders, the peers, the size and under it the publish date in its corner', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { row } = renderRow(release)

    // Assert
    expect(screen.getByRole('heading', { level: 3, name: tolokaRemux.title })).toBeInTheDocument()
    expect(row).toHaveAccessibleName(tolokaRemux.title)
    const seeders = screen.getByTitle(SEEDERS_HINT)
    const peers = screen.getByTitle(PEERS_HINT)
    expect(within(seeders).getByText('12')).toHaveAttribute('aria-hidden', 'true')
    expect(within(seeders).getByText('12 seeders')).toHaveClass('sr-only')
    expect(within(peers).getByText('15')).toHaveAttribute('aria-hidden', 'true')
    expect(within(peers).getByText('15 peers')).toHaveClass('sr-only')
    const size = screen.getByText('29.5 GiB')
    expect(size.previousElementSibling).toContainElement(seeders)
    expect(size.nextElementSibling).toHaveTextContent('Published 9/28/2026')
  })

  it('names the source that listed it at the foot, beside the actions', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    renderRow(release)

    // Assert
    expect(screen.getByText('Source: Toloka').nextElementSibling).toBe(screen.getByRole('group', { name: 'Actions' }))
  })

  it.each([
    // A Ukrainian-only Toloka release, whose title "Strip Cyrillic Letters" left as punctuation.
    ['\' (2016) HDTV 1080p', 'Сім\'я (2016) HDTV 1080p'],
    // MAM-33 — Toloka's topic title, where Prowlarr's title keeps the English part alone.
    [
      'Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
      'Чужі / Aliens (1986) [Theatrical Cut] BDRemux 1080p H.265 Ukr/Eng | Sub Eng',
    ],
  ])('is headed by the listing\'s description when it gives one, with the title no more beside it: %s', (title, description) => {
    // Arrange
    const release = releaseOf({ title, description })

    // Act
    const { row } = renderRow(release)

    // Assert
    expect(screen.getByRole('heading', { level: 3, name: description })).toBeInTheDocument()
    expect(row).toHaveAccessibleName(description)
    expect(screen.queryByText(title)).not.toBeInTheDocument()
    expect(row.querySelector('.search-release-description')).toBeNull()
    expect(screen.getByRole('heading', { level: 3 }).nextElementSibling).toBe(screen.getByRole('list', { name: 'Claimed' }))
  })

  it('is headed by the title, with no description line, when the listing gives none', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { row } = renderRow(release)

    // Assert
    expect(row.querySelector('.search-release-description')).toBeNull()
    expect(screen.getByRole('heading', { level: 3 }).nextElementSibling).toBe(screen.getByRole('list', { name: 'Claimed' }))
  })

  it('says on hover what the seeders and the peers count', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    renderRow(release)

    // Assert
    expect(screen.getByTitle(SEEDERS_HINT)).toHaveTextContent(/^12/)
    expect(screen.getByTitle(PEERS_HINT)).toHaveTextContent(/^15/)
  })

  it('names every fact the listing left out as unknown', () => {
    // Arrange
    const release = otherUhd

    // Act
    renderRow(release)

    // Assert
    expect(within(screen.getByTitle(SEEDERS_HINT)).getByText('?')).toHaveAttribute('aria-hidden', 'true')
    expect(within(screen.getByTitle(SEEDERS_HINT)).getByText('Seeders unknown')).toHaveClass('sr-only')
    expect(within(screen.getByTitle(PEERS_HINT)).getByText('?')).toHaveAttribute('aria-hidden', 'true')
    expect(within(screen.getByTitle(PEERS_HINT)).getByText('Peers unknown')).toHaveClass('sr-only')
    expect(screen.getByText('Size unknown')).toBeVisible()
    expect(screen.getByText('Publish date unknown')).toBeVisible()
    expect(screen.getByText('Source: Other')).toBeInTheDocument()
  })

  it('counts one seeder and one peer in the singular', () => {
    // Arrange
    const release = releaseOf({ seeders: 1, peers: 1 })

    // Act
    renderRow(release)

    // Assert
    expect(screen.getByText('1 seeder')).toBeInTheDocument()
    expect(screen.getByText('1 peer')).toBeInTheDocument()
  })

  it('groups what the title claims after their icons — the picture, the audio, the subtitles — saying on hover they are claims', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    renderRow(release)

    // Assert
    const claims = screen.getByRole('list', { name: 'Claimed' })
    expect(claims).toHaveAttribute('title', 'What the release\'s title claims, not checked against a file')
    expect(claimedGroups()).toEqual(['1080p · BDRemux · HEVC', 'Ukrainian, English', 'English'])
    expect(within(claims).getByLabelText('Audio').closest('li')).toHaveTextContent('Ukrainian, English')
    expect(within(claims).getByLabelText('Subtitles').closest('li')).toHaveTextContent('English')
  })

  it('leaves out every claim the title does not make, and names each dynamic range it does', () => {
    // Arrange
    const release = otherUhd

    // Act
    renderRow(release)

    // Assert
    expect(claimedGroups()).toEqual(['2160p · HDR10 · Dolby Vision'])
  })

  it('leaves no claims line when the title claims nothing', () => {
    // Arrange
    const release = releaseOf({
      claimed: { resolution: null, medium: null, videoCodec: null, dynamicRanges: [], audioLanguages: [], subtitleLanguages: [] },
    })

    // Act
    renderRow(release)

    // Assert
    expect(screen.queryByRole('list', { name: 'Claimed' })).not.toBeInTheDocument()
  })

  it('says the release is in the library with the evidence, naming the copy and its year in the text a phone shows', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    renderRow(release)

    // Assert
    expect(badgeTexts()).toEqual(['In your library: Чужі (1986) — matched by title and year'])
    expect(screen.getByText('In your library: Чужі (1986) — matched by title and year')).not.toHaveAttribute('title')
  })

  it('names which of two copies of one title the release matches, by its year', () => {
    // Arrange
    const dune1984 = { ...aliensMatch, work: { ...aliens, id: '7a1e0000-0000-4000-8000-000000000101', title: 'Дюна', year: 1984 } }
    const dune2021 = { ...aliensMatch, work: { ...aliens, id: '7a1e0000-0000-4000-8000-000000000102', title: 'Дюна', year: 2021 } }
    const release = releaseOf({ libraryMatches: [{ workId: dune2021.work.id, evidence: 'TitleAndYear', conflictingSchemes: [], heldEpisodeCount: null }] })

    // Act
    renderRow(release, [dune1984, dune2021])

    // Assert
    expect(badgeTexts()).toEqual(['In your library: Дюна (2021) — matched by title and year'])
  })

  it('names a work the search lists by words, on the release\'s own evidence', () => {
    // Arrange
    const library = [ramboIVMatch, ramboIIIByWords, firstBloodByWords]

    // Act
    renderRow(ramboIIIRip, library)

    // Assert
    expect(badgeTexts()).toEqual(['In your library: Рембо III (1988) — matched by title and year'])
    expect(screen.getByText('In your library: Рембо III (1988) — matched by title and year')).toHaveClass('ui-badge-secondary')
  })

  it('words a title and a year the library did not report as unknown', () => {
    // Arrange
    const unknownMatch = { ...aliensMatch, work: unknownWork, matchedIdentifiers: [], matchedTitle: null }
    const release = releaseOf({ libraryMatches: [{ workId: unknownWork.id, evidence: 'Identifier', conflictingSchemes: [], heldEpisodeCount: null }] })

    // Act
    renderRow(release, [unknownMatch])

    // Assert
    expect(badgeTexts()).toEqual(['In your library: Unknown title (year unknown) — matched by id'])
  })

  it('flags each scheme whose ids conflict beside an id match', () => {
    // Arrange
    const release = otherUhd

    // Act
    renderRow(release)

    // Assert
    expect(badgeTexts()).toEqual(['In your library: Чужі (1986) — matched by id', 'Conflicting TMDb ids'])
    expect(screen.getByText('Conflicting TMDb ids')).toHaveClass('ui-badge', 'library-badge-warning')
  })

  it('calls a title-only match a possible one, naming the copy', () => {
    // Arrange
    const release = releaseOf({ libraryMatches: [{ workId: aliensMatch.work.id, evidence: 'TitleOnly', conflictingSchemes: [], heldEpisodeCount: null }] })

    // Act
    renderRow(release)

    // Assert
    expect(screen.getByText('Possible match in your library: Чужі (1986)')).toHaveClass('library-badge-warning')
  })

  it('shows no library badge for a release that matches nothing in the library', () => {
    // Arrange
    const release = untitledRip

    // Act
    renderRow(release)

    // Assert
    expect(screen.queryByRole('list', { name: 'Why it matched' })).not.toBeInTheDocument()
  })

  it('opens the source page in a new tab that gets no handle back to the app', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    renderRow(release)

    // Assert
    const link = screen.getByRole('link', { name: 'Open source' })
    expect(link).toHaveAttribute('href', 'https://toloka.to/t123456')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noreferrer')
    expect(link).toHaveClass('ui-button', 'ui-button-outline')
  })

  it('shows Open source disabled with the reason when the source gave no page', () => {
    // Arrange
    const release = otherUhd

    // Act
    renderRow(release)

    // Assert
    expect(screen.queryByRole('link', { name: 'Open source' })).not.toBeInTheDocument()
    const button = screen.getByRole('button', { name: 'Open source' })
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'The source gave no page for this release.')
  })

  it('asks the parent to download the release from Download', async () => {
    // Arrange
    const { onDetails, onDownload, user } = renderRow(tolokaRemux)

    // Act
    await user.click(screen.getByRole('button', { name: 'Download' }))

    // Assert
    expect(onDownload).toHaveBeenCalledOnce()
    expect(onDetails).not.toHaveBeenCalled()
  })

  it('asks the parent for the details from Details', async () => {
    // Arrange
    const { onDetails, user } = renderRow(tolokaRemux)

    // Act
    await user.click(screen.getByRole('button', { name: 'Details' }))

    // Assert
    expect(onDetails).toHaveBeenCalledOnce()
  })

  it('collapses each action to its icon with the label kept in its title', () => {
    // Arrange
    renderRow(tolokaRemux)

    // Act
    const actions = within(screen.getByRole('group', { name: 'Actions' }))
    const controls = [
      actions.getByRole('button', { name: 'Details' }),
      actions.getByRole('link', { name: 'Open source' }),
      actions.getByRole('button', { name: 'Download' }),
    ]

    // Assert
    expect(controls.map((control) => control.getAttribute('title'))).toEqual(['Details', 'Open source', 'Download'])
    for (const control of controls) expect(control.querySelector('.label-text')).toHaveTextContent(control.title)
  })

  it('says a TV release is TV, with the seasons its title claims and how many of their episodes the library holds', () => {
    // Arrange — MAM-32
    const release = yellowstonePack

    // Act
    renderRow(release, [yellowstoneMatch])

    // Assert
    expect(claimedGroups()[0]).toBe('TV · Seasons 1–5 · WEB-DL · H.264')
    expect(badgeTexts()).toEqual([
      'In your library: Єллоустоун (2018) — matched by title and year',
      '19 of its episodes in your library',
    ])
    expect(screen.getByText('19 of its episodes in your library'))
      .toHaveAttribute('title', 'How many episodes of the seasons or episodes this release claims your library already holds.')
  })

  it('names the episodes a TV release claims of its season, and says when the library holds none of them', () => {
    // Arrange
    const release = yellowstoneEpisodes

    // Act
    renderRow(release, [yellowstoneMatch])

    // Assert
    expect(claimedGroups()[0]).toMatch(/^TV · Season 1 · Episodes 1–9( · |$)/)
    expect(badgeTexts()).toEqual([
      'In your library: Єллоустоун (2018) — matched by title and year',
      'None of its episodes in your library',
    ])
  })

  it('gives a movie release no TV claim and no episode count', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    renderRow(release)

    // Assert
    expect(claimedGroups()[0]).toBe('1080p · BDRemux · HEVC')
    expect(screen.queryByText(/of its episodes in your library$/)).not.toBeInTheDocument()
  })
})
