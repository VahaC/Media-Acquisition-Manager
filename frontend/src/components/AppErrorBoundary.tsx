import { Component, type ErrorInfo, type ReactNode } from 'react'
import { AppErrorFallback, type AppErrorKind } from '@/components/AppErrorFallback'
import { isChunkLoadError } from '@/lib/chunk-error'
import { recoverFromChunkError } from '@/lib/chunk-reload'

interface AppErrorBoundaryProps {
  children: ReactNode
  /**
   * A caught error is cleared when this changes, so the subtree gets a fresh
   * attempt. While nothing has failed a change does nothing, and the children
   * keep their state.
   */
  resetKey?: unknown
}

interface AppErrorBoundaryState {
  /** Null while healthy. Set to the kind of failure once one is caught. */
  failure: { kind: AppErrorKind } | null
}

/**
 * Stops a render error from blanking the page, and turns the one recoverable
 * kind — a code-split chunk that no longer exists on the server — into a reload
 * instead of an error screen. See `lib/chunk-error.ts` for why that case is
 * routine here and `lib/chunk-reload.ts` for the limits on reloading.
 *
 * A class, because `getDerivedStateFromError` / `componentDidCatch` are still
 * the only way to catch a render error in React 19 — there is no hook
 * equivalent, and pulling in a library for forty lines would add the fifth
 * unused dependency to this project.
 */
export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  state: AppErrorBoundaryState = { failure: null }

  // Pure by contract: decide *what* to show, do nothing about it. The reload
  // lives in componentDidCatch, which is allowed to have side effects.
  //
  // A chunk failure starts out as 'reloading' — the optimistic case, and the
  // usual one. componentDidCatch corrects it a tick later if the reload was
  // refused. This ordering matters: React can paint between the two, and a
  // flash of "Could not load this page" before a successful reload reads as a
  // real failure to the user, whereas the reverse reads as a slow reload.
  static getDerivedStateFromError(error: unknown): AppErrorBoundaryState {
    return { failure: { kind: isChunkLoadError(error) ? 'reloading' : null } }
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    if (!isChunkLoadError(error)) {
      // Not our recoverable case. React has already logged the error itself;
      // keep the component trail, which React does not print in production.
      console.error('Unhandled render error', error, info.componentStack)
      return
    }

    // May navigate away from this document. Whatever it decides is exactly what
    // the user should be told, so the outcome *is* the state.
    const outcome = recoverFromChunkError()
    this.setState({ failure: { kind: outcome } })
  }

  // `prevState`, not `this.state`: only a failure that was already on screen
  // is reset. An error thrown by the same render that brought the new key is
  // caught in this very update, and resetting it would only render the failing
  // route again straight into the same error.
  componentDidUpdate(prevProps: AppErrorBoundaryProps, prevState: AppErrorBoundaryState): void {
    if (prevState.failure && prevProps.resetKey !== this.props.resetKey) this.reset()
  }

  /** Clears the error so the subtree can mount again. */
  reset = (): void => {
    this.setState({ failure: null })
  }

  render(): ReactNode {
    const { failure } = this.state
    if (!failure) return this.props.children
    return <AppErrorFallback kind={failure.kind} onRetry={this.reset} />
  }
}
