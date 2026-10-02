import { useState } from 'react'
import { Loader2, RotateCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'

interface RetryButtonProps {
  /** Asks again; the button reads "Retrying…" until the promise settles. */
  onRetry: () => Promise<unknown>
  /** The label while idle; "Retry" by default. */
  label?: string
}

/**
 * MAM-16 — Retry in a Banner over data that failed to refresh, and the offline strip's Retry now.
 *
 * "Retrying…" follows this button's own click, never the query's `isFetching`: the lists here are
 * polled, and a label that followed every poll would read "Retrying…" on a button nobody pressed.
 */
export function RetryButton({ onRetry, label }: RetryButtonProps) {
  const { t } = useTranslation('common')
  const [retrying, setRetrying] = useState(false)

  const retry = async () => {
    setRetrying(true)
    try {
      await onRetry()
    } finally {
      // The answer usually takes the banner, and this button with it, off the screen; React drops
      // the update then.
      setRetrying(false)
    }
  }

  return (
    // type="button": a form's Banner sits inside the <form>, where a bare button submits it.
    // Disabled while pending: the request is already on its way, and a second click would only join it.
    <Button type="button" variant="outline" size="sm" disabled={retrying} onClick={() => void retry()}>
      {retrying
        ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        : <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />}
      {retrying ? t('retrying') : label ?? t('retry')}
    </Button>
  )
}
