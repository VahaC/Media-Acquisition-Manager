import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { destinationsApi } from '../destinations-api'
import type { CreateDestinationRequest, DestinationsQuery, UpdateDestinationRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-11 — one page of the Destinations settings page, searched and paged by the server, each row
 * with its live check. The previous page stays on screen while the next one loads, so typing a
 * search doesn't blank the table.
 *
 * It always runs, like useIntegrations: a non-owner who opens the address sees the API's 403
 * sentence in place of the list, rather than an empty page.
 */
export const useDestinations = (query: DestinationsQuery) =>
  useQuery({
    queryKey: qk.settings.destinations.list(query),
    queryFn: () => destinationsApi.list(query),
    placeholderData: keepPreviousData,
    // A 403 for a non-owner or a 400 for a bad page won't change on a second try.
    retry: false,
  })

/**
 * The categories a download client offers, for the destination dialog's picker. Asked only once a
 * client is chosen (`''` is none), and asked live each time the dialog opens: the owner may have just
 * created the category in the client.
 */
export const useClientDestinations = (downloadClientId: string) =>
  useQuery({
    queryKey: qk.settings.destinations.clientDestinations(downloadClientId),
    queryFn: () => destinationsApi.clientDestinations(downloadClientId),
    enabled: downloadClientId !== '',
    // A client that couldn't be asked is answered with 200 and its sentence, already after a wait;
    // a second try would only double it.
    retry: false,
  })

/**
 * MAM-32 — the release sources' sections, for the destination dialog's picker. Asked live each time
 * the dialog opens (every enabled source is asked for its categories), and never retried: a source
 * that couldn't be asked is already in the 200 with its sentence.
 */
export const useDestinationSections = () =>
  useQuery({
    queryKey: qk.settings.destinations.sections,
    queryFn: () => destinationsApi.sections(),
    retry: false,
  })

/**
 * The destination mutations. Each refetches the list before it resolves, so the dialog closes onto
 * the updated rows — the default badge moved off another row included — rather than onto the old
 * ones for a moment. None touches the clients' categories: a destination changes nothing in a client.
 * MAM-32 — each drops the sections too: a save moves them, and a removal frees its own.
 */
export const useCreateDestination = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateDestinationRequest) => destinationsApi.create(request),
    onSuccess: () => invalidateDestinations(queryClient),
  })
}

export const useUpdateDestination = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateDestinationRequest }) =>
      destinationsApi.update(id, request),
    onSuccess: () => invalidateDestinations(queryClient),
  })
}

export const useDeleteDestination = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => destinationsApi.remove(id),
    onSuccess: () => invalidateDestinations(queryClient),
  })
}

/**
 * The lists, waited for so the dialog closes onto the new rows, and the sections, dropped without
 * waiting: they are asked of every source, and only the next dialog reads them.
 */
async function invalidateDestinations(queryClient: QueryClient) {
  void queryClient.invalidateQueries({ queryKey: qk.settings.destinations.sections })
  await queryClient.invalidateQueries({ queryKey: qk.settings.destinations.lists })
}
