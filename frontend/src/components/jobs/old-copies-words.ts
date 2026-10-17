import type { TFunction } from 'i18next'

/** An action on the old copies a replacement holds in the quarantine. */
export type OldCopiesAction = 'restore' | 'deleteForGood'

/** Where the action's words go: its button, the button's hover title, and its confirmation's title. */
export type OldCopiesActionPart = 'action' | 'title' | 'confirmTitle'

/**
 * MAM-46 — the words of Restore and Delete for good, counted by the old copies they act on, wherever
 * they are offered (the job's page and a Jobs row). Exactly one copy has keys of its own (`…OneCopy`):
 * Ukrainian's `_one` form also counts 21, 31 and 101, so a counted form can't say "the old copy" without
 * its number (docs/i18n/glossary-uk.md, Counted words), as downloads' `replace.heldByOneCopy` does.
 */
export function oldCopiesWords(t: TFunction<'jobs'>, action: OldCopiesAction, part: OldCopiesActionPart, count: number): string {
  return count === 1 ? t(`oldCopies.${action}.${part}OneCopy`) : t(`oldCopies.${action}.${part}`, { count })
}
