import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { integrationsApi } from '../integrations-api'
import type { CreateIntegrationRequest, IntegrationsQuery, TestIntegrationRequest, UpdateIntegrationRequest } from '../types'
import { qk } from './keys'

/**
 * One page of the Integrations settings page, searched, filtered and paged by the server. The
 * previous page stays on screen while the next one loads, so typing a search doesn't blank the table.
 *
 * MAM-7 — it always runs, like useUsers: a non-owner who opens the address sees the API's 403
 * sentence in place of the list, rather than an empty page.
 */
export const useIntegrations = (query: IntegrationsQuery) =>
  useQuery({
    queryKey: qk.settings.integrations.list(query),
    queryFn: () => integrationsApi.list(query),
    placeholderData: keepPreviousData,
    // A 403 for a non-owner or a 400 for a bad page won't change on a second try.
    retry: false,
  })

/**
 * The types of service this build can connect to. Only the integration dialog reads it, and only
 * the owner can open that; the list changes only with a new version of the app, so it is fetched once.
 */
export const useIntegrationAdapterTypes = () =>
  useQuery({
    queryKey: qk.settings.integrations.adapterTypes,
    queryFn: () => integrationsApi.adapterTypes(),
    staleTime: Infinity,
    retry: false,
  })

/**
 * The integration mutations. Each refetches the list before it resolves, so the dialog closes onto
 * the updated row rather than onto the old one for a moment. None touches the adapter types.
 */
export const useCreateIntegration = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateIntegrationRequest) => integrationsApi.create(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.integrations.lists }),
  })
}

export const useUpdateIntegration = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateIntegrationRequest }) =>
      integrationsApi.update(id, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.integrations.lists }),
  })
}

export const useDeleteIntegration = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => integrationsApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.settings.integrations.lists }),
  })
}

/** A test of a saved integration. It changes nothing, so it invalidates nothing. */
export const useTestIntegration = () =>
  useMutation({
    mutationFn: (id: string) => integrationsApi.test(id),
  })

/** A test of the dialog's unsaved draft. It stores nothing, so it invalidates nothing. */
export const useTestIntegrationDraft = () =>
  useMutation({
    mutationFn: (request: TestIntegrationRequest) => integrationsApi.testDraft(request),
  })
