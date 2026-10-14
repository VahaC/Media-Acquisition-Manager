import { keepPreviousData, type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { integrationsApi } from '../integrations-api'
import type { CreateIntegrationRequest, IntegrationsQuery, TestIntegrationRequest, UpdateIntegrationRequest } from '../types'
import { qk } from './keys'

/**
 * One page of the Integrations settings page, searched, filtered and paged by the server. The
 * previous page stays on screen while the next one loads, so typing a search doesn't blank the table.
 *
 * MAM-7 — it always runs, like useUsers: a non-owner who opens the address sees the API's 403
 * sentence in place of the list, rather than an empty page. MAM-31 — unless `enabled` is false: the
 * Destinations page reads the download clients for its words only for the owner, whom the API answers.
 */
export const useIntegrations = (query: IntegrationsQuery, { enabled = true }: { enabled?: boolean } = {}) =>
  useQuery({
    queryKey: qk.settings.integrations.list(query),
    queryFn: () => integrationsApi.list(query),
    placeholderData: keepPreviousData,
    // A 403 for a non-owner or a 400 for a bad page won't change on a second try.
    retry: false,
    enabled,
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
    onSuccess: () => afterIntegrationChange(queryClient),
  })
}

export const useUpdateIntegration = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateIntegrationRequest }) =>
      integrationsApi.update(id, request),
    onSuccess: () => afterIntegrationChange(queryClient),
  })
}

export const useDeleteIntegration = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => integrationsApi.remove(id),
    onSuccess: () => afterIntegrationChange(queryClient),
  })
}

/**
 * After a saved change to any integration. MAM-8 — the Library is read from the media library
 * integrations: removing one purges what it imported, and a new one, a new name, the switch or a
 * path mapping changes the sources and the files' paths. The cached Library is dropped rather than
 * invalidated, so the next visit starts from Loading… instead of showing a removed library until
 * its refetch lands.
 *
 * MAM-11 — the destinations and the clients' categories are invalidated too: removing a download
 * client removes its destinations, and its name, switch, credentials and path mappings — or a media
 * library's — are part of every destination's check.
 */
function afterIntegrationChange(queryClient: QueryClient) {
  queryClient.removeQueries({ queryKey: qk.library.all })
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: qk.settings.integrations.lists }),
    queryClient.invalidateQueries({ queryKey: qk.settings.destinations.all }),
  ])
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
