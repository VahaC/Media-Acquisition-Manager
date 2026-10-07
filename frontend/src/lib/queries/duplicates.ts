import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isGroupGone } from '../duplicates'
import { duplicatesApi } from '../duplicates-api'
import type { DuplicateDecisionChange, DuplicateDecisionRequest, LibraryDuplicatesQuery } from '../types'
import { qk } from './keys'

/**
 * MAM-21 — one page of one tab of the Duplicates page, filtered and paged by the server. The page on
 * screen stays while the next page of its tab loads, as the Library's works do — but another tab's groups
 * never stand in for this tab's (the useRecommendations idiom): Dismissed would show undecided matches
 * with their buttons, and a tab that had none would say "No file has two names" over a tab that has some,
 * for as long as the server takes to group the whole library.
 */
export const useLibraryDuplicates = (query: LibraryDuplicatesQuery) =>
  useQuery({
    queryKey: qk.library.duplicates(query),
    queryFn: () => duplicatesApi.list(query),
    placeholderData: (previous, previousQuery) => {
      const asked = previousQuery?.queryKey[2]
      return asked?.class === query.class && asked?.decision === query.decision ? keepPreviousData(previous) : undefined
    },
    // A 400 for a bad page, class or decision won't change on a second try.
    retry: false,
  })

/**
 * MAM-21 — Confirm or Dismiss of a possible match (owner-only). Every tab is asked for again on success: a
 * dismissed group leaves every list but Dismissed, and a confirmed one shows its decision. A change that
 * keeps the group on screen ends once the list shows it, so its card never flashes its old buttons; one
 * that takes it off the list (`leavesList`) ends at once, so the page puts the group's short row in its
 * place before the list, asked for again, drops it (DuplicatesPage).
 *
 * And on a 404 the group on screen is no longer one (isGroupGone): the list shows what it is now instead of
 * a card whose buttons can only fail again.
 */
export const useDecideDuplicate = () => {
  const queryClient = useQueryClient()
  const refresh = () => queryClient.invalidateQueries({ queryKey: qk.library.duplicatesAll })
  return useMutation({
    mutationFn: ({ key, decision }: DuplicateDecisionChange & DuplicateDecisionRequest) => duplicatesApi.decide(key, decision),
    onSuccess: (_, { leavesList }) => {
      const refreshed = refresh()
      return leavesList ? undefined : refreshed
    },
    onError: (error) => (isGroupGone(error) ? refresh() : undefined),
  })
}

/**
 * MAM-21 — Undo of a decision (owner-only): the group is undecided again, back in every list it belongs
 * to and out of Dismissed, so every tab is asked for again on success, `leavesList` as for a decision — and
 * on a 404, as for a decision.
 */
export const useUndoDuplicateDecision = () => {
  const queryClient = useQueryClient()
  const refresh = () => queryClient.invalidateQueries({ queryKey: qk.library.duplicatesAll })
  return useMutation({
    mutationFn: ({ key }: DuplicateDecisionChange) => duplicatesApi.undo(key),
    onSuccess: (_, { leavesList }) => {
      const refreshed = refresh()
      return leavesList ? undefined : refreshed
    },
    onError: (error) => (isGroupGone(error) ? refresh() : undefined),
  })
}
