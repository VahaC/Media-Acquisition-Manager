import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { searchApi } from '../search-api'
import type { SearchReleasesQuery, SearchRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-12 — runs a search. The answer is the stored search itself, so it goes into the cache under
 * its id: the page opening `?id=` with it shows it at once instead of asking for it again.
 */
export const useCreateSearch = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SearchRequest) => searchApi.create(request),
    onSuccess: (search) => queryClient.setQueryData(qk.search.detail(search.id), search),
  })
}

/**
 * How long a stored search's answer counts as fresh. Its sources never change once stored, and its
 * library matches only with a sync; without it, the answer `useCreateSearch` puts in the cache would
 * be stale at once, and opening `?id=` with it would ask the server for the same search again.
 */
const SEARCH_STALE_MS = 60_000

/** A stored search by id; nothing is asked while there is no id (`''`). */
export const useSearch = (id: string) =>
  useQuery({
    queryKey: qk.search.detail(id),
    queryFn: () => searchApi.get(id),
    enabled: id !== '',
    staleTime: SEARCH_STALE_MS,
    // A 404 — expired, or not this user's — won't change on a second try.
    retry: false,
  })

/**
 * One page of a stored search's releases, filtered, sorted and paged by the server. The previous
 * page stays on screen while the next one loads, so changing a filter doesn't blank the list.
 */
export const useSearchReleases = (id: string, query: SearchReleasesQuery) =>
  useQuery({
    queryKey: qk.search.releases(id, query),
    queryFn: () => searchApi.releases(id, query),
    enabled: id !== '',
    placeholderData: keepPreviousData,
    // A 400 for a filter or a 404 for an expired search won't change on a second try.
    retry: false,
  })
