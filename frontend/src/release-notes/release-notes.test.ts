// Guards the format the What's-new modal will rely on: one semver-named file per version, a
// frontmatter of exactly `version` + `title` (never a date), no H1 (the modal renders version and
// title itself), a body that opens with the Highlights, and no link to a section that isn't there.

const notes = import.meta.glob<string>('./*.md', { query: '?raw', import: 'default', eager: true })
const entries = Object.entries(notes).map(([path, raw]) => ({
  file: path.slice(2),
  raw: raw.replace(/\r\n/g, '\n'),
}))

interface ParsedNote {
  frontmatter: Record<string, string>
  body: string
}

function parse(raw: string): ParsedNote {
  const match = /^---\n([\s\S]*?)\n---\n([\s\S]*)$/.exec(raw)
  if (!match) throw new Error('The note does not start with a --- frontmatter block.')
  const frontmatter: Record<string, string> = {}
  for (const line of match[1].split('\n')) {
    if (line.trim() === '') continue
    const separator = line.indexOf(':')
    if (separator < 0) throw new Error(`Frontmatter line "${line}" is not "key: value".`)
    frontmatter[line.slice(0, separator).trim()] = line.slice(separator + 1).trim()
  }
  return { frontmatter, body: match[2] }
}

/** Lines outside fenced code blocks — a `#` inside a code sample is not a heading. */
function proseLines(body: string): string[] {
  let inFence = false
  return body.split('\n').filter((line) => {
    if (line.trimStart().startsWith('```')) {
      inFence = !inFence
      return false
    }
    return !inFence
  })
}

/**
 * GitHub-style heading slugs (what a markdown renderer's slug plugin produces): lower case,
 * punctuation dropped, spaces to hyphens, and a repeated heading gets `-1`, `-2`, ….
 */
function headingSlugs(body: string): Set<string> {
  const seen = new Map<string, number>()
  const slugs = new Set<string>()
  for (const line of proseLines(body)) {
    const heading = /^#{1,6}\s+(.+?)\s*#*\s*$/.exec(line)
    if (!heading) continue
    const text = heading[1].replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    const base = text.toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc}\s-]/gu, '').replace(/\s/g, '-')
    const count = seen.get(base) ?? 0
    seen.set(base, count + 1)
    slugs.add(count === 0 ? base : `${base}-${count}`)
  }
  return slugs
}

describe('release notes', () => {
  it('has a note for the version in package.json', () => {
    // Arrange
    const files = entries.map((entry) => entry.file)

    // Act
    const current = `${__APP_VERSION__}.md`

    // Assert
    expect(files).toContain(current)
  })

  describe.each(entries)('$file', ({ file, raw }) => {
    it('is named after a semver version', () => {
      // Arrange
      const semverFile = /^\d+\.\d+\.\d+\.md$/

      // Act
      const matches = semverFile.test(file)

      // Assert
      expect(matches).toBe(true)
    })

    it('has exactly a version and a title in its frontmatter, and no date', () => {
      // Arrange
      const expectedVersion = file.replace(/\.md$/, '')

      // Act
      const { frontmatter } = parse(raw)

      // Assert
      expect(Object.keys(frontmatter).sort()).toEqual(['title', 'version'])
      expect(frontmatter).not.toHaveProperty('date')
      expect(frontmatter.version).toBe(expectedVersion)
      expect(frontmatter.title).not.toBe('')
    })

    it('starts its body with the Highlights section', () => {
      // Arrange
      const { body } = parse(raw)

      // Act
      const firstLine = body.trimStart().split('\n')[0]

      // Assert
      expect(firstLine).toBe('## Highlights')
    })

    it('has no H1 heading', () => {
      // Arrange
      const { body } = parse(raw)

      // Act
      const h1Lines = proseLines(body).filter((line) => /^#\s/.test(line))

      // Assert
      expect(h1Lines).toEqual([])
    })

    it('links only to sections that exist in the same note', () => {
      // Arrange
      const { body } = parse(raw)
      const slugs = headingSlugs(body)

      // Act
      const anchors = [...body.matchAll(/\]\(#([^)\s]+)\)/g)].map((m) => m[1])
      const dead = anchors.filter((anchor) => !slugs.has(anchor))

      // Assert
      expect(dead).toEqual([])
    })
  })
})
