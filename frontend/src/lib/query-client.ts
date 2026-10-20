import { QueryClient } from '@tanstack/react-query'
import { i18n } from './i18n'

// The app's one query cache lives outside React so the auth store can empty
// it when the signed-in user changes. Cached queries carry no owner: without that,
// the next user in the same tab is shown the previous user's lists and feature flags.
export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
})

// MAM-47 — the server's sentences follow Accept-Language, the UI locale (lib/api.ts): after a switch every cached answer
// holds the old language's words. Refetch what is on screen and mark the rest stale. One hook covers every way the
// language changes (sign-in, the Profile's save, another tab, a refreshed session, sign-out); i18n.ts imports no query
// client, so the two modules don't import each other. It settles after one round: an answer that can switch the
// language (the profile's, auth-store adoptAccountLanguage; a refreshed session's) comes back with the one now running,
// and the account's language is applied only when it differs (i18n.ts applyAccountLanguage).
i18n.on('languageChanged', () => void queryClient.invalidateQueries())
