import type { LibraryIdentifier, MediaIdentifierScheme } from './types'

/** MAM-34 — one id of a work with every source that claims it. */
export interface GroupedIdentifier {
  scheme: MediaIdentifierScheme
  value: string
  /** In the order the API lists them; never empty. */
  sources: LibraryIdentifier[]
}

/**
 * MAM-34 — a work's ids, one entry per scheme and value whatever claims it. A TMDb id the owner linked
 * and Jellyfin now reports too comes from the API twice (a row per source, ADR 0008), and two badges
 * "TMDb 679" side by side would read as two ids. The first appearance keeps its place, so the API's
 * scheme-then-value order stays.
 */
export function groupIdentifiers(identifiers: readonly LibraryIdentifier[]): GroupedIdentifier[] {
  const groups: GroupedIdentifier[] = []
  for (const identifier of identifiers) {
    const group = groups.find((candidate) => candidate.scheme === identifier.scheme && candidate.value === identifier.value)
    if (group) group.sources.push(identifier)
    else groups.push({ scheme: identifier.scheme, value: identifier.value, sources: [identifier] })
  }
  return groups
}

/**
 * MAM-34 — a React key for a grouped id that names its sources too, so a list never meets two equal
 * keys and an id whose sources change is drawn afresh.
 */
export function groupedIdentifierKey(group: GroupedIdentifier): string {
  return [group.scheme, group.value, ...group.sources.map((source) => `${source.sourceKind}:${source.sourceName ?? ''}`)].join('|')
}
