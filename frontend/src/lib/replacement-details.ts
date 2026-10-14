import { formatDuration, formatFileSize } from './format'
import { i18n } from './i18n'
import { episodePlace, seasonName } from './library-episodes'
import { codecName, resolutionClass } from './media-labels'
import type { ReplacementDetails, ReplacementJobCopy, ReplacementJobIdentifier, ReplacementJobNewFile, ReplacementJobWork, ReplacementName } from './types'

// MAM-27 — how a replacement's job page (and the Jobs list's Copies panel) words what the check found:
// a ticked copy's and a new file's facts, a new file's chips, the works and their ids. Plain functions
// over i18n, as lib/replacements.ts words the Download dialog: the copies table and the new files
// table both name a new file, and a component file may export its component alone.

/** The parts that apply, joined by the UI's separator; null when none does. */
function joined(parts: (string | null)[]): string | null {
  const shown = parts.filter((part) => part !== null)
  return shown.length === 0 ? null : shown.join(i18n.t('common:separator'))
}

/**
 * Where an episode sits, as the Library names it; null for a film, or when the library gave no number.
 * A file of several episodes is named by their range ("Season 1 · Episodes 1–2"), as the sentence of a
 * check that stopped over it names them: by its first one alone, the place would contradict it.
 */
function placeOf(seasonNumber: number | null, episodeNumber: number | null, episodeNumberEnd: number | null): string | null {
  if (episodeNumber === null || episodeNumberEnd === null || episodeNumberEnd <= episodeNumber) {
    return episodePlace({ seriesId: null, seasonNumber, episodeNumber })
  }
  return joined([
    seasonNumber === null ? null : seasonName(seasonNumber),
    i18n.t('library:episode.range', { first: episodeNumber, last: episodeNumberEnd }),
  ])
}

/**
 * A ticked copy's facts under its path, as it was when the owner ticked it: its size, its measured
 * running time (what a new file's is held against), its place in its series — with the episodes the
 * library's last read says it covers —, and its edition.
 */
export function copyFacts(copy: ReplacementJobCopy): string {
  return [
    formatFileSize(copy.sizeBytes),
    copy.durationMilliseconds === null ? null : formatDuration(copy.durationMilliseconds),
    placeOf(copy.seasonNumber, copy.episodeNumber, copy.episodeNumberEnd),
    copy.editionLabel,
  ].filter((part) => part !== null).join(i18n.t('common:separator'))
}

/**
 * A new file's facts on the disk: its size and running time as the media inspector read them — the
 * client's size while it isn't read yet. Null while nothing is known.
 */
export function newFileFacts(file: ReplacementJobNewFile): string | null {
  const size = file.sizeBytes ?? file.clientSizeBytes
  return joined([
    size === null ? null : formatFileSize(size),
    file.durationMilliseconds === null ? null : formatDuration(file.durationMilliseconds),
  ])
}

/**
 * Where the library placed a new episode file ("Season 1 · Episode 4", or "Season 1 · Episodes 1–2"
 * for a file of several); null for a film, or before it numbered it.
 */
export function newFilePlace(file: ReplacementJobNewFile): string | null {
  return placeOf(file.seasonNumber, file.episodeNumber, file.episodeNumberEnd)
}

/**
 * A new file's picture as the Library's chips name a file's (FileChips): its class (2160p), or its
 * frame below 480p, and its codec. None until the media inspector read it.
 */
export function newFileChips(file: ReplacementJobNewFile): string[] {
  const { width, height, videoCodec } = file
  const resolution = width === null || height === null ? null : resolutionClass(width, height)
  return [
    width === null || height === null
      ? null
      : resolution === null
        ? i18n.t('library:media.resolution', { width, height })
        : i18n.t('library:versions.resolutionClass', { value: resolution }),
    videoCodec === null ? null : codecName(videoCodec),
  ].filter((chip) => chip !== null)
}

/** A work as the Download dialog named it: a film's title and year ("Alien (1979)"), a series' title. */
export function workName(work: ReplacementJobWork): string {
  const title = work.title ?? i18n.t('library:work.unknownTitle')
  if (work.kind !== 'Movie') return title
  return work.year === null
    ? i18n.t('search:match.workYearUnknown', { title })
    : i18n.t('search:match.work', { title, year: work.year })
}

/** One registry id as the Library names it: "IMDb tt0078748". */
export function identifierName(identifier: ReplacementJobIdentifier): string {
  return i18n.t('library:work.identifier', { scheme: i18n.t(`library:schemes.${identifier.scheme}`), value: identifier.value })
}

/**
 * The ids the replacement expects a new file's item to carry: every id of every work it was scheduled
 * for, each once, joined ("IMDb tt0078748 · TMDb 348"). Null when the works carry none — a match by
 * title and year alone, whose check has no id to compare.
 */
export function expectedIdentifiers(works: readonly ReplacementJobWork[]): string | null {
  const names = [...new Set(works.flatMap((work) => work.identifiers.map(identifierName)))]
  return names.length === 0 ? null : names.join(i18n.t('common:separator'))
}

// ── what the owner can still drop ──

/**
 * MAM-27 — whether the server offers Drop on any copy now. It never drops the last copy left (Keep
 * both keeps that one), so with a single copy — a film, most often — none is, and what to do says
 * nothing of Drop rather than point at a disabled button.
 */
export function anyCopyDroppable(details: ReplacementDetails): boolean {
  return details.copies.some((copy) => copy.canDrop)
}

/**
 * MAM-27 — whether dropping the copies only a drop settles leaves another in the replacement for
 * Retry to check again. When every copy left is one, it doesn't: the last is never dropped, so only
 * keeping both settles the replacement.
 */
export function dropLeavesOthers(details: ReplacementDetails): boolean {
  return details.copies.filter((copy) => copy.droppedUtc === null).length > details.dropOnlyCopyCount
}

/** The names the replacement holds in the quarantine now: what Restore puts back and Delete for good deletes. */
export function quarantinedNames(details: ReplacementDetails): ReplacementName[] {
  return details.names.filter((name) => name.state === 'Quarantined')
}

/**
 * The old copies the replacement holds in the quarantine now — its videos there —: what Restore's and
 * Delete's labels, titles and confirmations are counted by ("Restore old copy", "Delete old copies
 * permanently").
 */
export function quarantinedCopies(details: ReplacementDetails): number {
  return quarantinedNames(details).filter((name) => name.kind === 'Video').length
}
