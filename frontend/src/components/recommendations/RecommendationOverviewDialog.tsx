import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorState } from '@/components/shared/ErrorState'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useRecommendationOverview } from '@/lib/queries'
import type { Recommendation } from '@/lib/types'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the "nothing here" line
import '@/styles/settings-dialog.css' // .settings-dialog* — the one scrolling body above the buttons
import '@/styles/recommendations.css' // .recommendation-overview* — the dialog's width and its text

interface RecommendationOverviewDialogProps {
  item: Recommendation
  onClose: () => void
}

/**
 * What a recommended work is about, from its card's Description: its title, its original title and
 * year under it, and the catalog's overview — asked for when the dialog opens, in the UI's language,
 * else in English (English alone while the UI speaks only English). A work the catalog has no overview
 * of says so; a failure says why, with Try again when another try can help.
 *
 * The parent mounts it only while it is open. Its body is the one scroller; Close sits last.
 */
export function RecommendationOverviewDialog({ item, onClose }: RecommendationOverviewDialogProps) {
  const { t, i18n } = useTranslation(['recommendations', 'common'])
  // The UI language's code alone — "en" of en-GB — the shape the catalog is asked in.
  const language = i18n.language.split('-')[0]
  const overview = useRecommendationOverview({ kind: item.kind, catalogId: item.catalogId }, language)
  // The card's Description, to give the focus back to on close, as TmdbLinkDialog does: the parent
  // mounts the dialog with no DialogTrigger, so Radix has nothing to give it back to (it went to <body>).
  const [opener] = useState(() => document.activeElement)
  const about = [
    ...(item.originalTitle !== null && item.originalTitle !== item.title ? [item.originalTitle] : []),
    ...(item.year !== null ? [String(item.year)] : []),
  ].join(t('common:separator'))

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent
        className="settings-dialog recommendation-overview-dialog"
        onCloseAutoFocus={(event) => {
          event.preventDefault()
          if (opener instanceof HTMLElement) opener.focus()
        }}
      >
        <DialogHeader>
          <DialogTitle className="recommendation-overview-title">{item.title}</DialogTitle>
          <DialogDescription>{about.length > 0 ? about : t('overview.description')}</DialogDescription>
        </DialogHeader>
        <div className="settings-dialog-body">
          {overview.isPending ? (
            <p className="settings-muted">{t('common:loading')}</p>
          ) : overview.error ? (
            <ErrorState
              title={t('overview.loadFailed')}
              onRetry={retryCanHelp(overview.error) ? () => void overview.refetch() : undefined}
              retrying={overview.isFetching}
            >
              {getApiErrorMessage(overview.error)}
            </ErrorState>
          ) : overview.data.overview === null ? (
            <p className="settings-muted">{t('overview.none')}</p>
          ) : (
            // In the language the catalog wrote it in, so a screen reader reads it in that one.
            <p className="recommendation-overview" lang={overview.data.language ?? undefined}>{overview.data.overview}</p>
          )}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>{t('common:close')}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
