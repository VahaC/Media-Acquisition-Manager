import type { DestinationSection, DestinationSectionList, DestinationSectionRequest } from './types'

// MAM-32 — the tracker sections a destination's dialog ticks (ADR 0020). The list names a section by
// its source and its category ids, never by its name alone: when the ids of one name are mapped to
// two destinations, the list shows that name twice, once for each part.

/** One section's key in the dialog's picks: its source and its category ids, which no other section of the list shares. */
export function sectionKey(sourceId: string, section: DestinationSection): string {
  return `${sourceId}\n${section.categoryIds.join(',')}`
}

/** The keys of the sections mapped to `destinationId` now: what the Edit dialog opens ticked. */
export function sectionsMappedTo(list: DestinationSectionList, destinationId: string | undefined): Set<string> {
  const keys = new Set<string>()
  if (destinationId === undefined) return keys
  for (const source of list.sources) {
    for (const section of source.sections) {
      if (section.destinationId === destinationId) keys.add(sectionKey(source.id, section))
    }
  }
  return keys
}

/**
 * What a save sends for the ticked `picked` keys: each section as the list gave it, so its name and
 * rank are stored with it. In the list's order, which is the sources' own.
 */
export function sectionRequests(list: DestinationSectionList, picked: ReadonlySet<string>): DestinationSectionRequest[] {
  return list.sources.flatMap((source) => source.sections
    .filter((section) => picked.has(sectionKey(source.id, section)))
    .map((section) => ({
      releaseSourceId: source.id,
      categoryIds: section.categoryIds,
      name: section.name,
      isSourceSpecific: section.isSourceSpecific,
    })))
}
