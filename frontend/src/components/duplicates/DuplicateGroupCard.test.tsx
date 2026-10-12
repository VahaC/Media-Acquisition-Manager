import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { DuplicateGroupCard } from '@/components/duplicates/DuplicateGroupCard'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDateTime, formatFileSize } from '@/lib/format'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { aliensSha256, aliensHolder, byteIdenticalGroupOf, possibleGroupOf, sameFileGroup, sameWorkGroup } from '@/test/duplicates'

vi.mock('@/lib/duplicates-api')

// MAM-21 — a group of the Duplicates page as it reads: its heading, its class and what tells its files
// apart, the evidence its class gives, the room its files take, what removing copies would free only
// where the server gives it, and the owner's decision. MAM-40 — the class in plain words, an episode's
// copies as an episode's, and one line saying what the group is and what can be done with it.

function renderCard(group: LibraryDuplicateGroup, isOwner = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <DuplicateGroupCard group={group} isOwner={isOwner} onLeft={() => {}} />
      </ConfirmProvider>
    </QueryClientProvider>,
  )
}

/** The badges at the card's head, in order. */
const badges = () => within(screen.getByRole('list', { name: 'About this group' })).getAllByRole('listitem').map((item) => item.textContent)

/** MAM-40 — the plain line under the card's head: what the group is, and what can be done with it. */
const explanation = () => document.querySelector('.duplicates-group > p')?.textContent

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
      expect(badges()).toEqual(['One file, two names'])
      expect(explanation()).toBe('One file reached by two names or more (hardlinks): it takes its room once, so there is nothing to keep or remove.')
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
      expect(badges()).toEqual(['Exact copies'])
      expect(explanation()).toBe('These files hold exactly the same bytes. Remove a copy you don\'t need, or keep them as they are.')
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
      expect(badges()).toEqual(['Same episode, different files', 'Needs review', 'Dynamic ranges differ'])
      expect(screen.queryByText(/would free|No estimate|frees nothing/)).not.toBeInTheDocument()
      expect(screen.queryByRole('group', { name: 'Decide about this possible match' })).not.toBeInTheDocument()
    })

    it('says an episode\'s copies are different files of the same episode, and what to do with them', () => {
      // Arrange
      const group = sameWorkGroup

      // Act
      renderCard(group)

      // Assert
      expect(badges()[0]).toBe('Same episode, different files')
      expect(explanation()).toBe('Different files of the same episode, by its ids: another size or quality. Compare them, then remove a copy you don\'t need, or keep them as they are.')
    })

    it('says a film\'s copies are different files of the same film, and what to do with them', () => {
      // Arrange — the Theatrical Cut and its copy on the backup disk, held by the film as two copies of one work.
      const group: LibraryDuplicateGroup = {
        ...sameWorkGroup,
        needsReview: false,
        distinctions: [],
        evidence: { ...sameWorkGroup.evidence, identifiers: [{ scheme: 'Imdb', value: 'tt0090605', seasonNumber: null, episodeNumber: null }] },
        files: byteIdenticalGroupOf().files.map((file) => ({ ...file, works: [aliensHolder] })),
      }

      // Act
      renderCard(group)

      // Assert
      expect(screen.getByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
      expect(badges()).toEqual(['Same film, different files'])
      expect(explanation()).toBe('Different files of the same film, by its ids: another size, quality or source. Compare them, then remove a copy you don\'t need, or keep them as they are.')
    })

    it.each([
      ['vasyl', `Kept as they are by vasyl on ${decidedAt}. A new copy joining the group brings it back to the list.`],
      [null, `Kept as they are on ${decidedAt}. A new copy joining the group brings it back to the list.`],
    ])('shows copies kept as they are as kept, not dismissed (decided by %s)', (decidedByUserName, line) => {
      // Arrange
      const group: LibraryDuplicateGroup = { ...sameWorkGroup, decision: { decision: 'Dismissed', decidedUtc, decidedByUserName } }

      // Act
      renderCard(group, decidedByUserName !== null)

      // Assert
      expect(badges()).toEqual(['Same episode, different files', 'Kept as they are', 'Dynamic ranges differ'])
      expect(evidenceLines().at(-1)).toBe(line)
      expect(screen.queryByText(/Dismissed/)).not.toBeInTheDocument()
    })

    it('names each distinction, the registry of ids that disagree and whether they are their shows\'', () => {
      // Arrange
      const group: LibraryDuplicateGroup = {
        ...sameWorkGroup,
        // MAM-37 — never Edition: copies whose names say different editions are no copies of one work.
        distinctions: [
          { kind: 'IdentifierConflict', scheme: 'Tmdb', ofSeries: false },
          { kind: 'IdentifierConflict', scheme: 'Tvdb', ofSeries: true },
          { kind: 'DynamicRange', scheme: null, ofSeries: false },
          { kind: 'RunningTimes', scheme: null, ofSeries: false },
        ],
        evidence: { ...sameWorkGroup.evidence, identifiers: [{ scheme: 'Tvdb', value: '81189', seasonNumber: 3, episodeNumber: null }] },
      }

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual([
        'Same episode, different files',
        'Needs review',
        'TMDb ids differ',
        'Their shows\' TVDB ids differ',
        'Dynamic ranges differ',
        'Running times differ',
      ])
      expect(evidenceLines()[0]).toBe('One work by its ids:Show TVDB 81189, season 3')
    })

    it('says how long the shortest and the longest copy run when the shortest runs under three quarters of the longest', () => {
      // Arrange — MAM-37: «Ніж у воді» (1962) and a 27-minute featurette that carries the film's ids, as ffprobe
      // measured them on production.
      const group: LibraryDuplicateGroup = {
        ...sameWorkGroup,
        distinctions: [{ kind: 'RunningTimes', scheme: null, ofSeries: false }],
        evidence: {
          ...sameWorkGroup.evidence,
          identifiers: [
            { scheme: 'Imdb', value: 'tt0056291', seasonNumber: null, episodeNumber: null },
            { scheme: 'Tmdb', value: '11502', seasonNumber: null, episodeNumber: null },
          ],
          runningTimes: { shortestMilliseconds: 1_600_699, longestMilliseconds: 5_731_959 },
        },
      }

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Same episode, different files', 'Needs review', 'Running times differ'])
      expect(evidenceLines()).toEqual([
        'One work by its ids:IMDb tt0056291TMDb 11502',
        'The shortest copy runs 26:41, under three quarters of the longest, 1:35:32.',
        `1 file · ${formatFileSize(9_663_676_416)} · 1 name not measured yet`,
      ])
    })

    it('flags running times that differ widely beside ids that disagree, after them', () => {
      // Arrange — MAM-37: «Ніж у воді» (1962) and the featurette under its IMDb id, the featurette's library
      // giving it a TMDb id of its own.
      const group: LibraryDuplicateGroup = {
        ...sameWorkGroup,
        distinctions: [
          { kind: 'IdentifierConflict', scheme: 'Tmdb', ofSeries: false },
          { kind: 'RunningTimes', scheme: null, ofSeries: false },
        ],
        evidence: {
          ...sameWorkGroup.evidence,
          identifiers: [{ scheme: 'Imdb', value: 'tt0056291', seasonNumber: null, episodeNumber: null }],
          runningTimes: { shortestMilliseconds: 1_600_699, longestMilliseconds: 5_731_959 },
        },
      }

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Same episode, different files', 'Needs review', 'TMDb ids differ', 'Running times differ'])
      expect(evidenceLines().slice(0, 2)).toEqual([
        'One work by its ids:IMDb tt0056291',
        'The shortest copy runs 26:41, under three quarters of the longest, 1:35:32.',
      ])
    })

    it('says nothing of the running times the server gives when they don\'t differ widely', () => {
      // Arrange — MAM-37: Terminator 2 (1991) three times, nothing in the names, the shortest at 88 % of the
      // longest: nothing to review.
      const group: LibraryDuplicateGroup = {
        ...sameWorkGroup,
        needsReview: false,
        distinctions: [],
        evidence: {
          ...sameWorkGroup.evidence,
          identifiers: [{ scheme: 'Imdb', value: 'tt0103064', seasonNumber: null, episodeNumber: null }],
          runningTimes: { shortestMilliseconds: 8_243_392, longestMilliseconds: 9_366_400 },
        },
      }

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Same episode, different files'])
      expect(evidenceLines()).toEqual([
        'One work by its ids:IMDb tt0103064',
        `1 file · ${formatFileSize(9_663_676_416)} · 1 name not measured yet`,
      ])
      expect(screen.queryByText(/The shortest copy runs/)).not.toBeInTheDocument()
    })
  })

  describe('a possible match', () => {
    it('shows the title and year it shares, flagged for review, and estimates nothing', () => {
      // Arrange
      const group = possibleGroupOf()

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Maybe the same film', 'Needs review'])
      expect(explanation()).toBe('Maybe the same film: the titles and years match, but no ids say so. Confirm it if they are one film, or dismiss it if they aren\'t; nothing is removed either way.')
      expect(evidenceLines()).toEqual([
        'The same title and year:Aliens (1986)',
        'Their running times agree.',
        `2 files · ${formatFileSize(34_465_781_813)}`,
      ])
    })

    it('flags it when its files\' editions differ', () => {
      // Arrange — MAM-37: the one class flagged Editions differ; the Special Edition beside the Theatrical Cut.
      const group = possibleGroupOf({ distinctions: [{ kind: 'Edition', scheme: null, ofSeries: false }] })

      // Act
      renderCard(group)

      // Assert
      expect(badges()).toEqual(['Maybe the same film', 'Needs review', 'Editions differ'])
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
      expect(badges()).toEqual(['Maybe the same film', 'Confirmed'])
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
      expect(badges()).toEqual(['Maybe the same film', 'Dismissed'])
      expect(evidenceLines().at(-1)).toBe(line)
    })
  })
})
