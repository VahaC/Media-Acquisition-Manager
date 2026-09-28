import { QueryClient } from '@tanstack/react-query'

// The app's one query cache lives outside React so the auth store can empty
// it when the signed-in user changes. Cached queries carry no owner: without that,
// the next user in the same tab is shown the previous user's lists and feature flags.
export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
})
