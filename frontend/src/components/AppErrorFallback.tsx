import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import type { ChunkRecoveryOutcome } from '@/lib/chunk-reload'

/**
 * What {@link AppErrorFallback} should say. `null` means an ordinary render
 * crash — nothing to do with code splitting.
 */
export type AppErrorKind = ChunkRecoveryOutcome | null

interface AppErrorFallbackProps {
  kind: AppErrorKind
  /** Re-renders the subtree without a full page load. Absent where retrying in
   *  place cannot help (a missing chunk stays missing until the document is
   *  fetched again). */
  onRetry?: () => void
}

/**
 * The screen shown instead of a blank page when a route fails to render. Kept
 * deliberately plain: it has to work when an arbitrary part of the app is
 * broken, so it pulls in no data, no icons, and no page-specific stylesheet —
 * only the always-loaded primitives from `index.css` / `ui-primitives.css`.
 * Its text comes from the `errors` resources, which are bundled and in memory
 * before the first render, so no failing request can take them away.
 */
export function AppErrorFallback({ kind, onRetry }: AppErrorFallbackProps) {
  const { t } = useTranslation('errors')
  const { copy, action } = describe(kind)

  return (
    <div className="app-error-fallback" role="alert">
      <div className="ui-card app-error-card">
        <div className="ui-card-header">
          <h2 className="ui-card-title">{t(`fallback.${copy}.title`)}</h2>
          <p className="ui-card-description">{t(`fallback.${copy}.body`)}</p>
        </div>
        <div className="ui-card-footer">
          {action === 'retry' && onRetry && (
            <Button type="button" onClick={onRetry}>{t('fallback.tryAgain')}</Button>
          )}
          {action === 'reload' && (
            <Button type="button" onClick={() => window.location.reload()}>{t('fallback.reload')}</Button>
          )}
        </div>
      </div>
    </div>
  )
}

/** Which `errors:fallback.*` text to show, and which button goes with it. */
function describe(kind: AppErrorKind): {
  copy: 'reloading' | 'exhausted' | 'crash'
  action: 'retry' | 'reload' | 'none'
} {
  switch (kind) {
    case 'reloading':
      // A reload is already under way; this frame is on screen for a moment at
      // most. No button — offering one invites a second reload mid-flight.
      return { copy: 'reloading', action: 'none' }
    case 'exhausted':
      return { copy: 'exhausted', action: 'reload' }
    default:
      return { copy: 'crash', action: 'retry' }
  }
}
