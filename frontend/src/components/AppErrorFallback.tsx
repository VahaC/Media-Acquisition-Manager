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
 */
export function AppErrorFallback({ kind, onRetry }: AppErrorFallbackProps) {
  const { title, body, action } = describe(kind)

  return (
    <div className="app-error-fallback" role="alert">
      <div className="ui-card app-error-card">
        <div className="ui-card-header">
          <h2 className="ui-card-title">{title}</h2>
          <p className="ui-card-description">{body}</p>
        </div>
        <div className="ui-card-footer">
          {action === 'retry' && onRetry && (
            <Button type="button" onClick={onRetry}>Try again</Button>
          )}
          {action === 'reload' && (
            <Button type="button" onClick={() => window.location.reload()}>Reload the page</Button>
          )}
        </div>
      </div>
    </div>
  )
}

function describe(kind: AppErrorKind): {
  title: string
  body: string
  action: 'retry' | 'reload' | 'none'
} {
  switch (kind) {
    case 'reloading':
      // A reload is already under way; this frame is on screen for a moment at
      // most. No button — offering one invites a second reload mid-flight.
      return {
        title: 'Loading the new version…',
        body: 'The app was updated while this page was open. Reloading to pick up the new build.',
        action: 'none',
      }
    case 'exhausted':
      return {
        title: 'Could not load this page',
        body: 'Part of the app failed to load even after a refresh. If this keeps happening, '
          + 'the running build may be incomplete — check the container logs.',
        action: 'reload',
      }
    default:
      return {
        title: 'Something went wrong',
        body: 'This page hit an unexpected error. The rest of the app is still usable.',
        action: 'retry',
      }
  }
}
