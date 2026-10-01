import { render, screen, within } from '@testing-library/react'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import type { LibraryWork } from '@/lib/types'
import { aliens, continental, unknownWork } from '@/test/library'

function renderCard(work: LibraryWork) {
  render(<LibraryWorkCard work={work} />)
  return screen.getByRole('article')
}

/** The versions of the card, in the order it shows them, as the text of each. */
const versionTexts = () =>
  within(screen.getByRole('list', { name: 'Versions' })).getAllByRole('listitem').map((item) => item.textContent)

describe('LibraryWorkCard', () => {
  it('shows the title as a heading, the original title that differs, the year and the source once', () => {
    // Arrange
    const work = aliens

    // Act
    const card = renderCard(work)

    // Assert
    expect(screen.getByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(card).toHaveAccessibleName('Чужі')
    expect(screen.getByText('Original title: Aliens')).toBeInTheDocument()
    expect(screen.getByText('1986')).toBeInTheDocument()
    expect(screen.getAllByText('From Home Jellyfin')).toHaveLength(1)
  })

  it('shows each identifier as a badge naming its source on hover', () => {
    // Arrange
    const work = aliens

    // Act
    renderCard(work)

    // Assert
    const badges = within(screen.getByRole('list', { name: 'Identifiers' })).getAllByRole('listitem')
    expect(badges.map((badge) => badge.textContent)).toEqual(['IMDb tt0090605', 'TMDb 679'])
    expect(screen.getByText('IMDb tt0090605')).toHaveAttribute('title', 'Reported by Home Jellyfin')
    expect(screen.queryByText(/^No (IMDb|TMDb) id$/)).not.toBeInTheDocument()
  })

  it('flags every identifier the work is missing', () => {
    // Arrange
    const work = continental

    // Act
    renderCard(work)

    // Assert
    const badges = within(screen.getByRole('list', { name: 'Identifiers' })).getAllByRole('listitem')
    expect(badges.map((badge) => badge.textContent)).toEqual(['IMDb tt29669983', 'No TMDb id'])
    expect(screen.getByText('No TMDb id')).toHaveClass('ui-badge', 'library-badge-warning')
  })

  it('leaves out an original title the same as the title', () => {
    // Arrange
    const work: LibraryWork = { ...continental, originalTitle: 'The Continental' }

    // Act
    renderCard(work)

    // Assert
    expect(screen.queryByText(/^Original title:/)).not.toBeInTheDocument()
  })

  it('orders the versions by edition label and shows each file as its name over its folder', () => {
    // Arrange — the fixture has Theatrical Cut first.
    const work = aliens

    // Act
    renderCard(work)

    // Assert
    const [special, theatrical] = versionTexts()
    expect(special).toMatch(/^Special Edition\[02\.2\] Aliens \(1986\)/)
    expect(theatrical).toMatch(/^Theatrical Cut\[02\.1\] Aliens \(1986\)/)
    const name = screen.getByText('[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv')
    expect(name).toHaveAttribute('title', aliens.versions[0].files[0].servicePath)
    expect(screen.getAllByText('/media/movies/Alien. Collection')).toHaveLength(2)
  })

  it('shows the app path when the mappings changed it, with the size and the library', () => {
    // Arrange
    const work = aliens

    // Act
    renderCard(work)

    // Assert
    expect(screen.getByText(`In this app: ${aliens.versions[0].files[0].appPath}`)).toBeInTheDocument()
    expect(screen.getByText('13.83 GiB')).toBeInTheDocument()
    expect(screen.getByText('18.27 GiB')).toBeInTheDocument()
    expect(screen.getAllByText('Library: Фільми')).toHaveLength(2)
  })

  it('shows no app path when it is the same as the service path', () => {
    // Arrange
    const work = continental

    // Act
    renderCard(work)

    // Assert
    expect(screen.queryByText(/^In this app:/)).not.toBeInTheDocument()
    expect(screen.getByText('/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)')).toBeInTheDocument()
  })

  it('says unknown for every value the library did not report, and why a file has no app path', () => {
    // Arrange
    const work = unknownWork

    // Act
    renderCard(work)

    // Assert
    expect(screen.getByRole('heading', { level: 2, name: 'Unknown title' })).toBeInTheDocument()
    expect(screen.getByText('Year unknown')).toBeInTheDocument()
    expect(screen.getByText('Size unknown')).toBeInTheDocument()
    expect(screen.getByText('Library unknown')).toBeInTheDocument()
    expect(screen.getByText('No path mapping of “Home Jellyfin” covers /srv/other/untitled.mkv.')).toBeInTheDocument()
    expect(screen.queryByText(/^In this app:/)).not.toBeInTheDocument()
    const badges = within(screen.getByRole('list', { name: 'Identifiers' })).getAllByRole('listitem')
    expect(badges.map((badge) => badge.textContent)).toEqual(['No IMDb id', 'No TMDb id'])
  })

  it('says when a version has no file, and when a file has no app path without a reason', () => {
    // Arrange
    const [withFile, withoutFile] = unknownWork.versions
    const work: LibraryWork = {
      ...unknownWork,
      versions: [withoutFile, { ...withFile, files: [{ ...withFile.files[0], pathProblem: null }] }],
    }

    // Act
    renderCard(work)

    // Assert
    const [unlabelled, disc] = versionTexts()
    expect(unlabelled).toContain('untitled.mkv')
    expect(unlabelled).toContain('This file has no path in this app.')
    expect(disc).toBe('DiscNo file reported for this version.')
  })

  it('says when the work has no versions', () => {
    // Arrange
    const work: LibraryWork = { ...continental, versions: [] }

    // Act
    renderCard(work)

    // Assert
    expect(screen.getByText('No versions reported.')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Versions' })).not.toBeInTheDocument()
    // Without a version the source is named by an identifier.
    expect(screen.getByText('From Home Jellyfin')).toBeInTheDocument()
  })

  it('splits a Windows path at its backslashes', () => {
    // Arrange
    const [version] = continental.versions
    const file = { ...version.files[0], servicePath: 'D:\\Movies\\Taken (2008)\\Taken.mkv', appPath: null, pathStatus: 'Invalid' as const }
    const work: LibraryWork = { ...continental, versions: [{ ...version, files: [file] }] }

    // Act
    renderCard(work)

    // Assert
    expect(screen.getByText('Taken.mkv')).toBeInTheDocument()
    expect(screen.getByText('D:\\Movies\\Taken (2008)')).toBeInTheDocument()
  })
})
