import { isAxiosError } from 'axios'
import { formatNumber } from './format'
import { i18n } from './i18n'
import { episodePlace } from './library-episodes'
import { splitPath } from './library-versions'
import type {
  LibraryDuplicateFile,
  LibraryDuplicateGroup,
  LibraryDuplicateWork,
  LibraryDuplicatesQuery,
  MediaEdition,
  ShownVersion,
} from './types'

// MAM-21 — how the Duplicates page reads its tab from the address, asks the server for it, heads a
// group's card and words a file's facts. Plain functions over i18n, as library-episodes.ts words a
// season, so the card and the comparison table say each fact the same way.

/**
 * The Duplicates page's tabs, in the order of specification §3.3, with every class first and the
 * dismissed possible matches last.
 */
export const DUPLICATE_TABS = ['All', 'SameFile', 'ByteIdentical', 'SameWork', 'Possible', 'Dismissed'] as const

/** One tab of the Duplicates page. */
export type DuplicateTab = (typeof DUPLICATE_TABS)[number]

/**
 * What took a possible match off the tab on screen: the owner dismissed it, or undid its dismissal on the
 * Dismissed tab.
 */
export type DuplicateLeaving = 'Dismissed' | 'Restored'

/**
 * The tab `?tab=` names; All when it is left out or names no tab (a link from an older address, a typo),
 * rather than a page that refuses to open.
 */
export function tabFrom(param: string | null): DuplicateTab {
  return DUPLICATE_TABS.find((tab) => tab === param) ?? 'All'
}

/**
 * The filters a tab asks the server for: none for All (every group but the dismissed ones), a class for
 * a class's tab, and the dismissed possible matches for Dismissed.
 */
export function tabQuery(tab: DuplicateTab): Pick<LibraryDuplicatesQuery, 'class' | 'decision'> {
  if (tab === 'All') return {}
  if (tab === 'Dismissed') return { decision: 'Dismissed' }
  return { class: tab }
}

/** Library titles in Ukrainian alphabetical order, letter case ignored: the server's LibraryTitleOrder. */
const TITLE_ORDER = new Intl.Collator('uk', { sensitivity: 'accent' })

/** Two strings by their UTF-16 code units, the server's ordinal tie-break. */
function compareOrdinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0
}

/**
 * Where an episode's place sorts, as the Library lists a show's seasons: the numbered seasons, then the
 * specials (season 0), then a place without a season.
 */
function seasonRank(work: LibraryDuplicateWork): number {
  const season = work.episode?.seasonNumber ?? null
  return work.episode === null ? 0 : season === null ? 2 : season === 0 ? 1 : 0
}

/**
 * The work a group is headed by: its first as the server orders the groups — by the title its card shows
 * (an episode's its series', then its season and number), then ordinal, then id; null when no library
 * reports any of its files.
 */
export function firstWork(group: LibraryDuplicateGroup): LibraryDuplicateWork | null {
  const works = group.files.flatMap((file) => file.works)
  const shown = (work: LibraryDuplicateWork) => work.seriesTitle ?? work.title ?? ''
  const [first] = works.toSorted((a, b) =>
    TITLE_ORDER.compare(shown(a), shown(b))
      || compareOrdinal(shown(a), shown(b))
      || seasonRank(a) - seasonRank(b)
      || (a.episode?.seasonNumber ?? 0) - (b.episode?.seasonNumber ?? 0)
      || (a.episode?.episodeNumber ?? Number.MAX_SAFE_INTEGER) - (b.episode?.episodeNumber ?? Number.MAX_SAFE_INTEGER)
      || compareOrdinal(a.id, b.id))
  return first ?? null
}

/** A group card's heading and the facts under it. */
export interface DuplicateHeading {
  title: string
  /** In their order; none for a group no library reports. */
  meta: string[]
}

/**
 * What a group's card is headed by. Its first work's title with the year; for an episode its series'
 * title, with where the episode sits and its own title under it, so two halves of one episode read as
 * the episode they are. A group no library reports is headed by the title and year a possible match
 * offers, else by its first file's name.
 */
export function groupHeading(group: LibraryDuplicateGroup): DuplicateHeading {
  const work = firstWork(group)
  if (work !== null) {
    const title = work.title ?? i18n.t('library:work.unknownTitle')
    const place = work.episode === null ? null : episodePlace(work.episode)
    if (work.seriesTitle !== null) {
      return { title: work.seriesTitle, meta: [place, title].filter((part) => part !== null) }
    }
    const year = work.year === null ? i18n.t('library:work.unknownYear') : String(work.year)
    return { title, meta: [place, year].filter((part) => part !== null) }
  }
  const [offered] = group.evidence.titles
  if (offered !== undefined) {
    return { title: i18n.t('duplicates:group.titleYear', { title: offered.text, year: offered.year }), meta: [] }
  }
  const [file] = group.files
  return { title: file === undefined ? i18n.t('library:work.unknownTitle') : splitPath(file.file.servicePath).name, meta: [] }
}

/**
 * A group in a sentence about it: its heading with what tells it from another group of that title — a
 * film's year, an episode's place — "Чужі (1986)", "Yellowstone (Season 1 · Episode 2)".
 */
export function groupName(group: LibraryDuplicateGroup): string {
  const { title, meta } = groupHeading(group)
  const [detail] = meta
  return detail === undefined ? title : i18n.t('duplicates:group.name', { title, detail })
}

/**
 * What tells each path of a list from the others, read from its end: the file's name when no other path has
 * it; else the nearest folder that differs, with the folders between it and the name left out —
 * "hardlinks/Arrival (2016).mkv" beside "Arrival (2016)/Arrival (2016).mkv", "tv-4k/…/S01E01.mkv" beside
 * "tv/…/S01E01.mkv" — in the path's own separators. Two names of one file, or one file name in two folders,
 * would otherwise make rows that read the same. Where leaving the folders out would make two read alike
 * again, those two keep every folder from the one that differs.
 */
export function distinctPathEnds(paths: readonly string[]): string[] {
  // Where each part of a path starts: after every separator, '/' or '\'.
  const starts = paths.map((path) => [0, ...[...path].flatMap((char, index) => (char === '/' || char === '\\' ? [index + 1] : []))])
  const end = (at: number, parts: number) => {
    const own = starts[at]
    return parts >= own.length ? paths[at] : paths[at].slice(own[own.length - parts])
  }
  const full = paths.map((path, at) => {
    for (let parts = 1; parts < starts[at].length; parts++) {
      const mine = end(at, parts)
      if (paths.every((_, other) => other === at || end(other, parts) !== mine)) return { text: mine, parts }
    }
    return { text: path, parts: starts[at].length }
  })
  const short = full.map(({ text, parts }, at) => {
    if (parts <= 2) return text
    const own = starts[at]
    const folder = paths[at].slice(own[own.length - parts], own[own.length - parts + 1])
    // The folder with its separator, then the name with the separator before it.
    return `${folder}…${paths[at].slice(own[own.length - 1] - 1)}`
  })
  return short.map((text, at) => (short.some((other, index) => index !== at && other === text) ? full[at].text : text))
}

/**
 * Each file of a group as the version VersionRows and VersionTable show, named by the end of its path that
 * tells it from the group's other files (distinctPathEnds) — the two halves of an episode by their names,
 * a hardlink's two names by their folders. Its whole path is in its open row and the table's Folder row.
 */
export function fileVersions(group: LibraryDuplicateGroup): ShownVersion[] {
  const labels = distinctPathEnds(group.files.map(({ file }) => file.servicePath))
  return group.files.map(({ file }, index) => ({ id: file.id, editionLabel: labels[index], files: [file] }))
}

/**
 * The file a name leads to, as the app's last look read it: "Device 2049 · inode 1311 · 2 links". Without
 * an identity it says whether the app hasn't looked yet or looked and found none — never a guess.
 */
export function identityText(file: LibraryDuplicateFile): string {
  if (file.deviceId === null || file.inode === null) {
    return i18n.t(file.file.inspection === null ? 'duplicates:facts.notLookedAt' : 'duplicates:facts.identityUnknown')
  }
  // Ids, not amounts: no thousands separators.
  const device = String(file.deviceId)
  const inode = String(file.inode)
  return file.linkCount === null
    ? i18n.t('duplicates:facts.identityLinksUnknown', { device, inode })
    : i18n.t('duplicates:facts.identityValue', { count: file.linkCount, value: formatNumber(file.linkCount), device, inode })
}

/** The editions a file's names say it is, in the user's words, or that its names name none. */
export function editionsText(editions: readonly MediaEdition[]): string {
  return editions.length === 0
    ? i18n.t('duplicates:facts.noEditions')
    : editions.map((edition) => i18n.t(`duplicates:edition.${edition}`)).join(i18n.t('common:separator'))
}

/**
 * Whether a decision's request found no group under its key: its members changed since the page loaded
 * (a sync, a scan or a link), and it is listed again under a new key, or not at all (ADR 0029).
 */
export function isGroupGone(error: unknown): boolean {
  return isAxiosError(error) && error.response?.status === 404
}
