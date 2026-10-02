import { WifiOff } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { RetryButton } from '@/components/shared/RetryButton'
import { useAuthStore } from '@/lib/auth-store'
import { useConnectionStore } from '@/lib/connection-store'
import { useServerProbe } from '@/lib/queries'
import '@/styles/app-layout.css' // .app-offline* — the strip under the header

/**
 * MAM-16 — under the header while the server can't be reached: the sentence every failed request
 * reads then, a note that the app keeps asking, and Retry now. It shows once a request gets no
 * answer (lib/api.ts), asks `GET /api/health` at once and every 5 s after, and goes on the first
 * answer to any request; the page keeps what it showed meanwhile.
 *
 * Only once a session is known, signed in or not. Until then (`initializing`, `unavailable`) the
 * session gate and the sign-in page say the same sentence with a retry of their own, which would make
 * two of each on one screen, and a strip that flashed while a fresh tab is still asking for its
 * session. First-run setup never asks for one, so its own error lines tell it there.
 */
export function OfflineStrip() {
  const { t } = useTranslation('errors')
  const unreachable = useConnectionStore((state) => state.unreachable)
  const status = useAuthStore((state) => state.status)
  const shown = unreachable && (status === 'authenticated' || status === 'anonymous')
  const probe = useServerProbe(shown)

  if (!shown) return null
  return (
    <div className="app-offline" role="alert">
      <WifiOff className="h-4 w-4 app-offline-icon" aria-hidden="true" />
      <span className="app-offline-text">
        {t('serverUnreachable')}{' '}
        <span className="app-offline-muted">{t('offline.retrying')}</span>
      </span>
      <RetryButton label={t('offline.retryNow')} onRetry={() => probe.refetch()} />
    </div>
  )
}
