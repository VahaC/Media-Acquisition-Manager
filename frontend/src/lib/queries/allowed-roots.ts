import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { allowedRootsApi } from '../allowed-roots-api'
import type { UpdateAllowedRootsRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-9 — the media folders for their settings page. It always runs, like useUsers: a non-owner who
 * opens the address sees the API's 403 sentence in place of the form, rather than an empty one.
 */
export const useAllowedRoots = () =>
  useQuery({
    queryKey: qk.settings.allowedRoots,
    queryFn: () => allowedRootsApi.get(),
    // A 403 for a non-owner won't change on a second try.
    retry: false,
  })

/**
 * Saves the whole list of media folders. It resolves once the folders are read again, so the form
 * settles onto the stored (canonical) paths. The Library's sources are asked for again too: whether
 * any folder is allowed is part of their answer, and turns the file inspection on or off.
 *
 * MAM-24 — the save carries the replacement switch and ticks, which decide every reason the Download
 * dialog gives for a copy (switched off, folder not ticked), and the media folders decide which ticks
 * count: every answer about what a release may replace is asked for again.
 */
export const useUpdateAllowedRoots = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateAllowedRootsRequest) => allowedRootsApi.update(request),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.settings.allowedRoots }),
      queryClient.invalidateQueries({ queryKey: qk.library.sources }),
      queryClient.invalidateQueries({ queryKey: qk.replacements.all }),
    ]),
  })
}
