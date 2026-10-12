import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { copyRemovalsApi } from '../copy-removals-api'
import { isGroupGone } from '../duplicates'
import type { CopyRemovalsQuery, RemoveCopyRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-40 — one page of the owner's removals still in the quarantine, for the Duplicates page's Removed
 * copies tab, paged by the server. The page on screen stays while the next one loads. Not retried: a 400
 * for a bad page, or a 403 for anyone but the owner, won't change on a second try.
 */
export const useCopyRemovals = (query: CopyRemovalsQuery) =>
  useQuery({
    queryKey: qk.replacements.removals(query),
    queryFn: () => copyRemovalsApi.list(query),
    placeholderData: keepPreviousData,
    retry: false,
  })

/**
 * What a removal, a Restore or a Delete for good changes: the removals, every tab of the Duplicates page —
 * a copy in the quarantine counts in no group, and one put back joins its group again — and the Media
 * folders' quarantine room.
 */
const refreshQuarantine = (queryClient: QueryClient) => Promise.all([
  queryClient.invalidateQueries({ queryKey: qk.replacements.removalsAll }),
  queryClient.invalidateQueries({ queryKey: qk.library.duplicatesAll }),
  queryClient.invalidateQueries({ queryKey: qk.settings.allowedRoots }),
])

/**
 * MAM-40 — the owner's Remove this copy on a group's card. It ends once the lists are asked for again, so
 * the card never shows the copy it just removed with its buttons again: a group of two leaves the list, a
 * bigger one comes back without the copy. Never retried on its own, and sent even while the browser says
 * it is offline (`networkMode: 'always'`): held back, it would go out on its own later, after the owner may
 * have left the page.
 *
 * A refusal changes nothing on the disk, but says the list is out of date: a 404 is a group whose members
 * changed since the page loaded (isGroupGone), a 409 a check the file no longer passes — the list is asked
 * for again, so the card shows the group and its verdicts as they are now.
 */
export const useRemoveDuplicateCopy = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: RemoveCopyRequest) => copyRemovalsApi.remove(request),
    networkMode: 'always',
    onSuccess: () => refreshQuarantine(queryClient),
    onError: (error) => {
      if (isGroupGone(error) || (isAxiosError(error) && error.response?.status === 409)) {
        void queryClient.invalidateQueries({ queryKey: qk.library.duplicatesAll })
      }
    },
  })
}

/**
 * MAM-40 — puts every name of one removal in the quarantine back where it was. Once it answers, the
 * removals, the Duplicates page and the quarantine room are asked for again, whatever the answer: a
 * refusal part-way still brought some names back, and a 404 is a removal that ended meanwhile. Pending
 * until they are back, so its buttons stay held until the page shows the new state. Never retried on its
 * own, and sent even while the browser says it is offline (useRemoveDuplicateCopy's reason).
 */
export const useRestoreCopyRemoval = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => copyRemovalsApi.restore(id),
    networkMode: 'always',
    onSettled: () => refreshQuarantine(queryClient),
  })
}

/**
 * MAM-40 — deletes every name of one removal in the quarantine for good. Then as Restore: everything it
 * touches is asked for again whatever the answer. Never retried on its own, and sent even while the
 * browser says it is offline (useRemoveDuplicateCopy's reason).
 */
export const useDeleteCopyRemovalForGood = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => copyRemovalsApi.deleteForGood(id),
    networkMode: 'always',
    onSettled: () => refreshQuarantine(queryClient),
  })
}
