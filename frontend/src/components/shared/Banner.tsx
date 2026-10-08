import type { ReactNode } from 'react'
import { CircleAlert, CircleCheck, TriangleAlert, X, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'

type BannerTone = 'error' | 'ok' | 'warning'

interface BannerProps {
  /** Red for a failure (the default), green for a result that went through, amber for a caution. */
  tone?: BannerTone
  /** The sentence: the UI's own, the server's, or both. */
  children: ReactNode
  /** A button at the end of the line, such as Retry on a failed refresh. */
  action?: ReactNode
  /** Adds the Dismiss button, for news the person may clear once read (a refused action). */
  onDismiss?: () => void
  /**
   * MAM-38 — the banner's id, for a control that points at it with `aria-describedby`: a live region
   * inserted already filled is often not read out, so the Download dialog's Send reads its warning.
   */
  id?: string
}

const TONE_ICONS: Record<BannerTone, LucideIcon> = {
  error: CircleAlert,
  ok: CircleCheck,
  warning: TriangleAlert,
}

/**
 * MAM-16 — news about the last request, above content that stays on screen: a failed refresh over
 * the last loaded list, a refused action, a test result.
 *
 * Only a failure interrupts a screen reader (`alert`); a result that went through and a caution
 * wait their turn (`status`). The colours live in ui-primitives.css, where the error tone is the
 * banner without a `data-tone`.
 */
export function Banner({ tone = 'error', children, action, onDismiss, id }: BannerProps) {
  const { t } = useTranslation('common')
  const Icon = TONE_ICONS[tone]

  return (
    <div id={id} className="ui-banner" data-tone={tone === 'error' ? undefined : tone} role={tone === 'error' ? 'alert' : 'status'}>
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      <span className="ui-banner-text">{children}</span>
      {(action || onDismiss) && (
        <span className="ui-banner-actions">
          {action}
          {onDismiss && (
            // type="button": a form's Banner sits inside the <form>, where a bare button submits it.
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="ui-banner-dismiss"
              title={t('dismiss')}
              aria-label={t('dismiss')}
              onClick={onDismiss}
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          )}
        </span>
      )}
    </div>
  )
}
