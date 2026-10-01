import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { allowedRootsApi } from '../allowed-roots-api'
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
 */
export const useUpdateAllowedRoots = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (paths: string[]) => allowedRootsApi.update(paths),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.settings.allowedRoots }),
      queryClient.invalidateQueries({ queryKey: qk.library.sources }),
    ]),
  })
}
