import { newFileChips, newFileFacts } from '@/lib/replacement-details'
import type { ReplacementJobNewFile } from '@/lib/types'
import '@/styles/library.css' // .library-version-chips, .library-version-chip — the picture's chips
import '@/styles/jobs.css' // .jobs-name-paths, .jobs-file-path — the lines stacked, the path that breaks anywhere

/**
 * MAM-27 — a new file as both of a replacement's tables show it (the copies table's paired file and
 * the New files table's disk cell): its path when `withPath` — the New files table has a column of
 * its own for it —, then its size and running time, then its picture's chips. One piece, so the same
 * file never reads two ways on the same page. Nothing but the path while the media inspector hasn't
 * read it.
 */
export function NewFileFacts({ file, withPath }: { file: ReplacementJobNewFile; withPath: boolean }) {
  const facts = newFileFacts(file)
  const chips = newFileChips(file)

  return (
    <span className="jobs-name-paths">
      {withPath && <span className="jobs-file-path">{file.appPath ?? file.relativePath}</span>}
      {facts !== null && <span className="ui-hint">{facts}</span>}
      {chips.length > 0 && (
        <span className="library-version-chips">
          {/* The chips have no id, and their order is fixed. */}
          {chips.map((chip, index) => <span key={index} className="library-version-chip">{chip}</span>)}
        </span>
      )}
    </span>
  )
}
