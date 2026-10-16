import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { CreateUserRequest, UsersQuery } from '../types'
import { usersApi } from '../users-api'
import { qk } from './keys'

/**
 * One page of the Users settings page, searched and paged by the server. The previous page stays
 * on screen while the next one loads, so typing a search doesn't blank the table on every pause.
 */
export const useUsers = (query: UsersQuery) =>
  useQuery({
    queryKey: qk.settings.users.list(query),
    queryFn: () => usersApi.list(query),
    placeholderData: keepPreviousData,
    // A 403 for a non-owner or a 400 for a bad page won't change on a second try.
    retry: false,
  })

/**
 * The user-management mutations. Each refetches the list before it resolves, so a dialog closes
 * or a button settles onto the updated row rather than onto the old one for a moment.
 */
export const useCreateUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateUserRequest) => usersApi.create(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
  })
}

/**
 * MAM-5 — disabling also revokes the user's access tokens, so the Profile page's list (the owner's
 * own, should it be on screen) is asked for again with the users.
 */
export const useDisableUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.disable(id),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
      queryClient.invalidateQueries({ queryKey: qk.profile.accessTokens }),
    ]),
  })
}

export const useEnableUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.enable(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
  })
}

/**
 * MAM-5 — a reset revokes the user's access tokens, so their count on the list drops to 0; the owner's
 * reset of their own password empties the Profile page's list too.
 */
export const useResetUserPassword = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, password }: { id: string; password: string }) => usersApi.resetPassword(id, password),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
      queryClient.invalidateQueries({ queryKey: qk.profile.accessTokens }),
    ]),
  })
}

/**
 * MAM-5 — the owner revokes every access token of one user, their own included, so the Profile page's
 * list is asked for again with the users.
 */
export const useRevokeUserTokens = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.revokeTokens(id),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
      queryClient.invalidateQueries({ queryKey: qk.profile.accessTokens }),
    ]),
  })
}

export const useDeleteUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
  })
}
