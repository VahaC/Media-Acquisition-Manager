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

export const useDisableUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.disable(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
  })
}

export const useEnableUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.enable(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
  })
}

/** Nothing on the list changes with a password, so this one invalidates nothing. */
export const useResetUserPassword = () =>
  useMutation({
    mutationFn: ({ id, password }: { id: string; password: string }) => usersApi.resetPassword(id, password),
  })

export const useDeleteUser = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => usersApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
  })
}
