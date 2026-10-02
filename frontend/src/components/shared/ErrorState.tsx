import type { ReactNode } from 'react'
import { CircleAlert, Loader2, RotateCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'

interface ErrorStateProps {
  /** What failed, from the page's locale file: "Could not load the users." */
  title: string
  /** The server's own sentence (`getApiErrorMessage(error)`), left out when it sent none. */
  children?: ReactNode
  /**
   * Asks again. Left out for a failure a second try can't change (a 400, 403 or 404; see
   * retryCanHelp): no button then.
   */
  onRetry?: () => void
  /** While a retry from Try again is pending (useLoadRetry). */
  retrying?: boolean
}

/**
 * MAM-16 — in place of a list or panel whose first load failed, where there is no data to keep on
 * screen: what failed, the server's sentence, and Try again.
 *
 * The title and the sentence are two lines, not one or the other: the title says which load failed
 * and the sentence why, and either alone left the reader guessing at the other half.
 */
export function ErrorState({ title, children, onRetry, retrying = false }: ErrorStateProps) {
  const { t } = useTranslation('common')

  return (
    <div className="ui-error-state" role="alert">
      <div className="ui-error-state-head">
        <span className="ui-error-state-icon">
          <CircleAlert className="h-4 w-4" aria-hidden="true" />
        </span>
        <div className="ui-error-state-text">
          <p className="ui-error-state-title">{title}</p>
          {children && <p className="ui-error-state-body">{children}</p>}
        </div>
      </div>
      {onRetry && (
        <div className="ui-error-state-actions">
          {/* Disabled while pending: the request is already on its way, and a second click would
              only join it. */}
          <Button type="button" variant="outline" size="sm" disabled={retrying} onClick={onRetry}>
            {retrying
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              : <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />}
            {retrying ? t('retrying') : t('tryAgain')}
          </Button>
        </div>
      )}
    </div>
  )
}
