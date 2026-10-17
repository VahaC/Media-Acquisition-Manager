import { formatNumber } from './format'
import { i18n } from './i18n'
import { episodePlace, seasonName } from './library-episodes'
import { splitPath } from './library-versions'
import type { ReplacementCopy, ReplacementEpisode, ReplacementOptions, ReplacementWork } from './types'

// MAM-24 — how the Download dialog reads the server's answer about old copies (ADR 0032): which
// copies may be ticked, what the ticks send, how each work, season and episode is named, and what
// the summary lists. Plain functions over i18n, as lib/duplicates.ts words the Duplicates page.

/** A copy may be ticked when nothing of its work or its own stands in the way. */
export function canTick(work: ReplacementWork, copy: ReplacementCopy): boolean {
  return work.unavailable === null && copy.unavailable === null
}

/** Every copy of a work, in the order the dialog lists them: a film's, then a series' episodes'. */
export function copiesOf(work: ReplacementWork): ReplacementCopy[] {
  return [...work.copies, ...work.episodes.flatMap((episode) => episode.copies)]
}

/**
 * The ids of the copies the answer lets the owner tick, none while the whole release can't be
 * replaced. A file listed under two works carries the stricter reason on both (the server's rule), so
 * an id is tickable wherever it is listed.
 */
export function tickableIds(options: ReplacementOptions | undefined): Set<string> {
  if (options === undefined || options.unavailable !== null) return new Set()
  return new Set(options.works.flatMap((work) => copiesOf(work).filter((copy) => canTick(work, copy)).map((copy) => copy.file.id)))
}

/** The ticked ids in the order the dialog lists them, each once: what a schedule sends. */
export function tickedInOrder(options: ReplacementOptions, ticked: ReadonlySet<string>): string[] {
  const ids = options.works.flatMap((work) => copiesOf(work).map((copy) => copy.file.id)).filter((id) => ticked.has(id))
  return [...new Set(ids)]
}

/** Whether any copy of the work is ticked. */
export function hasTickedCopy(work: ReplacementWork, ticked: ReadonlySet<string>): boolean {
  return copiesOf(work).some((copy) => ticked.has(copy.file.id))
}

/**
 * The works whose title-and-year match needs the owner's tick and has a ticked copy, in their order:
 * what Send waits for, and — confirmed — what a schedule sends. A confirmation without a ticked copy
 * of its work is never sent: the server refuses one.
 */
export function worksToConfirm(options: ReplacementOptions, ticked: ReadonlySet<string>): ReplacementWork[] {
  return options.works.filter((work) => work.needsConfirmation && work.unavailable === null && hasTickedCopy(work, ticked))
}

/** The path a copy is shown and listed by: the app's own, else the library's (a copy the app can't reach). */
export function copyPath(copy: ReplacementCopy): string {
  return copy.file.appPath ?? copy.file.servicePath
}

/**
 * Each work's name, in the answer's order: a film's title and year ("Alien (1979)"), a series' title.
 * Two works that would read alike are told apart by the libraries holding them; works that still read
 * alike — two items of one film in one library — by the file name of each one's first copy when those
 * differ, else by their place among them, the way the server tells their confirmations apart.
 */
export function workNames(works: readonly ReplacementWork[]): string[] {
  const names = works.map((work) => {
    const title = work.title ?? i18n.t('library:work.unknownTitle')
    if (work.kind === 'Series') return title
    return work.year === null
      ? i18n.t('search:match.workYearUnknown', { title })
      : i18n.t('search:match.work', { title, year: work.year })
  })
  const byLibrary = names.map((name, at) => {
    const libraries = works[at].libraryNames
    const twin = names.some((other, index) => index !== at && other === name)
    return twin && libraries.length > 0
      ? i18n.t('downloads:replace.workIn', { work: name, libraries: libraries.join(i18n.t('common:separator')) })
      : name
  })
  return byLibrary.map((name, at) => {
    const twins = byLibrary.flatMap((other, index) => (other === name ? [index] : []))
    if (twins.length < 2) return name
    const files = twins.map((index) => firstFileName(works[index]))
    return files.every((file) => file !== null) && new Set(files).size === files.length
      ? i18n.t('downloads:replace.workCopy', { work: name, file: files[twins.indexOf(at)] })
      : i18n.t('downloads:replace.workPlace', { work: name, place: formatNumber(twins.indexOf(at) + 1), total: formatNumber(twins.length) })
  })
}

/** The file name of a work's first listed copy, or null when it lists none. */
function firstFileName(work: ReplacementWork): string | null {
  const first = copiesOf(work).at(0)
  return first === undefined ? null : splitPath(copyPath(first)).name
}

/** One season of a series' claimed episodes, as the server orders them. */
export interface ReplacementSeason {
  /** 0 for the specials; null for the episodes the library gives no season. */
  number: number | null
  episodes: ReplacementEpisode[]
}

/** A series' claimed episodes by season, in the server's order (season, then number, then title). */
export function seasonsOf(work: ReplacementWork): ReplacementSeason[] {
  const seasons: ReplacementSeason[] = []
  for (const episode of work.episodes) {
    const last = seasons.at(-1)
    if (last !== undefined && last.number === episode.seasonNumber) last.episodes.push(episode)
    else seasons.push({ number: episode.seasonNumber, episodes: [episode] })
  }
  return seasons
}

/** An episode as the dialog names it: where it sits and its title, "Season 2 · Episode 3 · Daybreak". */
export function episodeName(episode: ReplacementEpisode): string {
  const place = episodePlace({ seriesId: null, seasonNumber: episode.seasonNumber, episodeNumber: episode.episodeNumber })
  const parts = [place, episode.title].filter((part) => part !== null)
  return parts.length === 0 ? i18n.t('library:work.unknownTitle') : parts.join(i18n.t('common:separator'))
}

/**
 * Each copy's version as the Library names it: its edition label, else "Version N", numbered in the
 * Library's order of the versions (a version of several files numbered once), by version id.
 */
export function versionLabels(copies: readonly ReplacementCopy[]): Map<string, string> {
  const labels = new Map<string, string>()
  for (const copy of copies) {
    if (labels.has(copy.versionId)) continue
    labels.set(copy.versionId, copy.editionLabel ?? i18n.t('library:versions.unlabelled', { number: labels.size + 1 }))
  }
  return labels
}

/** A torrent holding copies of one work, and how many of them. */
export interface HeldTorrent {
  clientName: string
  torrentId: string
  torrentName: string
  count: number
}

/**
 * The key a hold is grouped by: its client and the client's id of its torrent — never the torrent's name,
 * which a cross-seed shares with another torrent the owner removes on its own.
 */
export function holdKey(hold: { clientName: string; torrentId: string }): string {
  return `${hold.clientName}\n${hold.torrentId}`
}

/**
 * The torrents holding copies of a work (not by a second name), each once with how many of the work's
 * copies it holds, in the order the copies are listed: the sentence about one is said once, at the
 * top of the work, rather than under each of its copies.
 */
export function heldTorrentsOf(work: ReplacementWork): HeldTorrent[] {
  const held = new Map<string, HeldTorrent>()
  for (const copy of copiesOf(work)) {
    for (const hold of copy.holds) {
      if (hold.secondName) continue
      const key = holdKey(hold)
      const known = held.get(key)
      if (known === undefined) held.set(key, { clientName: hold.clientName, torrentId: hold.torrentId, torrentName: hold.torrentName, count: 1 })
      else known.count += 1
    }
  }
  return [...held.values()]
}

/**
 * The download clients whose torrents hold ticked copies (not by a second name), each once, in the
 * order the copies are listed: where the summary's conditions say the owner removes those torrents.
 */
export function holdingClients(options: ReplacementOptions, ticked: ReadonlySet<string>): string[] {
  const names = options.works.flatMap((work) => copiesOf(work))
    .filter((copy) => ticked.has(copy.file.id))
    .flatMap((copy) => copy.holds.filter((hold) => !hold.secondName).map((hold) => hold.clientName))
  return [...new Set(names)]
}

/** One group of the summary's replaced copies: a film, or a season of a series, with the copies' paths. */
export interface ReplacementGroup {
  name: string
  paths: string[]
}

/**
 * The ticked copies as the summary lists them, by work and, for a series, by season — "Alien (1979)",
 * "The Penguins of Madagascar, Season 2" — each with the paths of its copies. Frozen into the sent
 * view as they were sent.
 */
export function replacementGroups(options: ReplacementOptions, ticked: ReadonlySet<string>): ReplacementGroup[] {
  const names = workNames(options.works)
  const pathsOf = (copies: readonly ReplacementCopy[]) =>
    [...new Map(copies.filter((copy) => ticked.has(copy.file.id)).map((copy) => [copy.file.id, copyPath(copy)])).values()]
  return options.works.flatMap((work, at) => {
    const film = { name: names[at], paths: pathsOf(work.copies) }
    const seasons = seasonsOf(work).map((season) => ({
      name: i18n.t('downloads:replace.seasonOf', { work: names[at], season: seasonName(season.number) }),
      paths: pathsOf(season.episodes.flatMap((episode) => episode.copies)),
    }))
    return [film, ...seasons].filter((group) => group.paths.length > 0)
  })
}
