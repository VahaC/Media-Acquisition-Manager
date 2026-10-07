import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { DuplicateGroupCard } from '@/components/duplicates/DuplicateGroupCard'
import { formatDateTime, formatFileSize } from '@/lib/format'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { aliensSha256, byteIdenticalGroupOf, possibleGroupOf, sameFileGroup, sameWorkGroup } from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')

// MAM-21 — a group of the Duplicates page as it reads: its heading, its class and what tells its files
// apart, the evidence its class gives, the room its files take, what removing copies would free only
// where the server gives it, and the owner's decision.

function renderCard(group: LibraryDuplicateGroup, isOwner = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DuplicateGroupCard group={group} isOwner={isOwner} onLeft={() => {}} />
    </QueryClientProvider>,
  )
}

/** The badges at the card's head, in order. */
const badges = () => within(screen.getByRole('list', { name: 'About this group' })).getAllByRole('listitem').map((item) => item.textContent)

/** The evidence, space, recoverable and decision lines, in order. */
const evidenceLines = () =>
  [...document.querySelectorAll('.duplicates-evidence > p')].map((line) => line.textContent)

const decidedUtc = '2026-10-07T09:15:00Z'
const decidedAt = formatDateTime(decidedUtc, { dateStyle: 'medium', timeStyle: 'short' })

describe('DuplicateGroupCard', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  describe('one file under several names', () => {
    it('says which file the names lead to, the room it takes once, and that removing a name frees nothing', () => {
      // Arrange
      const group = sameFileGroup

      // Act
      renderCard(group)

      // Assert
      expect(screen.getByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
      expect(screen.getByText('1986')).toBeInTheDocument()
      expect(badges()).toEqual(['Same file'])
      expect(evidenceLines()).toEqual([
        'One file under 2 names: device 2049, inode 1311. The file system counts 2 names.',
        `1 file · ${formatFileSize(14_845_708_473)}`,
        'Removing a name frees nothing while the file has another.',
      ])
    })

    it('says when the file system\'s count of the names is unknown', () => {
      // Arrange
      const group: LibraryDuplicateGroup = {
        ...sameFileGroup,
        evidence: { ...sameFileGroup.evidence, identity: { deviceId: 2049, inode: 1311, linkCount: null, nameCount: 2 } },
      }

      // Act
      renderCard(group)

      // Assert
      expect(evidenceLines()[0]).toBe('One file under 2 names: device 2049, inode 1311. The file system\'s count of its names is unknown.')
    })

    it('offers no decision: one file\'s names are one by its identity', () => {
      // Arrange
      const group = sameFileGroup

      // Act
      renderCard(group)

      // Assert
      expect(screen.queryByRole('group', { name: 'Decide about this possible match' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /Confirm|Dismiss|Undo/ })).not.toBeInTheDocument()
    })
  })

  describe('byte-identical copies', () => {
    it('shows the SHA-256 they share and what removing all but one copy would free', () => {
      // Arrange
      const group = byteIdenticalGroupOf()

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Byte-identical'])
      expect(evidenceLines()).toEqual([
        `The same bytes, SHA-256:${aliensSha256}`,
        `2 files · ${formatFileSize(29_691_416_946)}`,
        `Removing all but one copy would free about ${formatFileSize(14_845_708_473)}.`,
      ])
      expect(screen.getAllByText(aliensSha256)[0].tagName).toBe('CODE')
    })

    it.each([
      ['UnlistedNames', 'No estimate: a file has more names than the app found, and a name outside the media folders keeps its bytes whatever is removed here.'],
      ['SharingFileSystem', 'No estimate: a file is on a file system that can share blocks between files (btrfs, ZFS, XFS and the like), so a copy may take no room of its own.'],
      ['UnknownFileSystem', 'No estimate: the file system of a file is unknown, or one whose server or lower layer may share blocks (a network share, an overlay, FUSE).'],
    ] as const)('says why there is no estimate when the server gives none: %s', (gap, sentence) => {
      // Arrange
      const group = byteIdenticalGroupOf({ recoverable: { sizeBytes: null, gap } })

      // Act
      renderCard(group)

      // Assert
      expect(evidenceLines()[2]).toBe(sentence)
      expect(screen.queryByText(/would free/)).not.toBeInTheDocument()
    })
  })

  describe('copies of one work', () => {
    it('heads an episode\'s group by its series, its season and episode and its own title', () => {
      // Arrange
      const group = sameWorkGroup

      // Act
      renderCard(group)

      // Assert
      const heading = screen.getByRole('heading', { level: 2 })
      expect(heading).toHaveTextContent('Єллоустоун')
      expect(screen.getByRole('article', { name: 'Єллоустоун' })).toBeInTheDocument()
      expect([...heading.nextElementSibling!.children].map((part) => part.textContent)).toEqual(['Season 1 · Episode 1', 'Daybreak'])
    })

    it('shows the ids that make the works one, a series\' with its season and episode, and names the files not measured', () => {
      // Arrange
      const group = sameWorkGroup

      // Act
      renderCard(group)

      // Assert
      expect(evidenceLines()).toEqual([
        'One work by its ids:IMDb tt7608156Show TVDB 341164, season 1, episode 1',
        `1 file · ${formatFileSize(9_663_676_416)} · 1 name not measured yet`,
      ])
    })

    it('says only that its names aren\'t measured yet when none is, rather than "0 files · 0 B"', () => {
      // Arrange
      const group: LibraryDuplicateGroup = { ...sameWorkGroup, space: { sizeBytes: 0, fileCount: 0, nameCount: 2, unmeasuredCount: 2 } }

      // Act
      renderCard(group)

      // Assert
      expect(evidenceLines()).toEqual([
        'One work by its ids:IMDb tt7608156Show TVDB 341164, season 1, episode 1',
        '2 names not measured yet',
      ])
    })

    it('flags it for review with what tells the copies apart, and estimates nothing: no copy of a work is redundant', () => {
      // Arrange
      const group = sameWorkGroup

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Same work', 'Needs review', 'Dynamic ranges differ'])
      expect(screen.queryByText(/would free|No estimate|frees nothing/)).not.toBeInTheDocument()
      expect(screen.queryByRole('group', { name: 'Decide about this possible match' })).not.toBeInTheDocument()
    })

    it('names each distinction, the registry of ids that disagree and whether they are their shows\'', () => {
      // Arrange
      const group: LibraryDuplicateGroup = {
        ...sameWorkGroup,
        distinctions: [
          { kind: 'IdentifierConflict', scheme: 'Tmdb', ofSeries: false },
          { kind: 'IdentifierConflict', scheme: 'Tvdb', ofSeries: true },
          { kind: 'EpisodeNumbers', scheme: null, ofSeries: false },
          { kind: 'Edition', scheme: null, ofSeries: false },
          { kind: 'DynamicRange', scheme: null, ofSeries: false },
        ],
        evidence: { ...sameWorkGroup.evidence, identifiers: [{ scheme: 'Tvdb', value: '81189', seasonNumber: 3, episodeNumber: null }] },
      }

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual([
        'Same work',
        'Needs review',
        'TMDb ids differ',
        'Their shows\' TVDB ids differ',
        'Episode numbers differ',
        'Editions differ',
        'Dynamic ranges differ',
      ])
      expect(evidenceLines()[0]).toBe('One work by its ids:Show TVDB 81189, season 3')
    })
  })

  describe('a possible match', () => {
    it('shows the title and year it shares, flagged for review, and estimates nothing', () => {
      // Arrange
      const group = possibleGroupOf()

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Possible match', 'Needs review'])
      expect(evidenceLines()).toEqual([
        'The same title and year:Aliens (1986)',
        'Their running times agree.',
        `2 files · ${formatFileSize(34_465_781_813)}`,
      ])
    })

    it.each([
      [false, 'Their running times differ.'],
      [null, 'Their running times aren\'t measured.'],
    ])('says when the running times agree is %s', (durationsAgree, sentence) => {
      // Arrange
      const group = possibleGroupOf({ evidence: { ...possibleGroupOf().evidence, durationsAgree } })

      // Act
      renderCard(group)

      // Assert
      expect(evidenceLines()[1]).toBe(sentence)
    })

    it('shows a confirmed match as confirmed, by whom and when to the owner, and that nothing is removed because of it', () => {
      // Arrange
      const group = possibleGroupOf({ decision: { decision: 'Confirmed', decidedUtc, decidedByUserName: 'vasyl' } })

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Possible match', 'Confirmed'])
      expect(evidenceLines().at(-1)).toBe(`Confirmed by vasyl on ${decidedAt}. It stays a possible match: nothing is removed because of it.`)
    })

    it('tells a member when it was confirmed, without who did', () => {
      // Arrange
      const group = possibleGroupOf({ decision: { decision: 'Confirmed', decidedUtc, decidedByUserName: null } })

      // Act
      renderCard(group, false)

      // Assert
      expect(evidenceLines().at(-1)).toBe(`Confirmed on ${decidedAt}. It stays a possible match: nothing is removed because of it.`)
      expect(screen.queryByText(/vasyl/)).not.toBeInTheDocument()
    })

    it.each([
      ['vasyl', `Dismissed by vasyl on ${decidedAt}.`],
      [null, `Dismissed on ${decidedAt}.`],
    ])('shows a dismissed match as dismissed (decided by %s)', (decidedByUserName, line) => {
      // Arrange
      const group = possibleGroupOf({ decision: { decision: 'Dismissed', decidedUtc, decidedByUserName } })

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Possible match', 'Dismissed'])
      expect(evidenceLines().at(-1)).toBe(line)
    })
  })
})
