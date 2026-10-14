import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { TmdbLinkEditor, type TmdbLinkSave } from '@/components/library/TmdbLinkEditor'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useLibraryLinkDialog } from '@/lib/queries'
import type { LibraryWork } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the Loading… line
import '@/styles/settings-dialog.css' // .settings-dialog* — the one scrolling body above the buttons
import '@/styles/search.css' // .search-details-* — the 64rem dialog, its title and two columns

/** The Save form; the footer's Save button submits it from outside (R24). */
const PICK_FORM_ID = 'tmdb-link-pick'

interface TmdbLinkDialogProps {
  /**
   * The work to link: a card's, or a row's of the TMDb page. Its title heads the dialog while the
   * dialog's own copy loads.
   */
  work: Pick<LibraryWork, 'id' | 'title' | 'originalTitle'>
  onClose: () => void
}

/**
 * MAM-34 — links a library work to its TMDb film or show by hand (the owner only). The parent mounts it
 * only while it is open, so every opening starts fresh. Its content is TmdbLinkEditor, which the TMDb
 * page's Review tab shows inline too; the dialog brings the title, the one scrolling body and the
 * footer, [Save] [Cancel].
 *
 * A save never waits for Jellyfin: the items come back queued, and the dialog stays open on them while
 * its poll shows each one being identified and then identified, or not identified with the reason and
 * Retry. Only a save that leaves nothing for Jellyfin to do and nothing new to read (every ticked item
 * kept in the app for a reason the item showed before the click) closes it: an item the save found
 * TMDb has no IMDb id for stays on screen with its sentence. While a save is on screen the footer
 * offers Close in place of Cancel, as nothing can cancel an identification, and the owner may close it
 * any time: the result shows on the Library card too.
 *
 * The focus goes to the dialog itself as it opens, whatever has loaded by then, and back to the
 * control that opened it as it closes.
 */
export function TmdbLinkDialog({ work, onClose }: TmdbLinkDialogProps) {
  const { t } = useTranslation(['libraryLinks', 'library', 'common'])
  const query = useLibraryLinkDialog(work.id)
  const { data } = query
  const load = useLoadRetry(query)
  const shown = data?.work ?? work
  const content = useRef<HTMLDivElement>(null)
  // What had the focus as the dialog opened: the card's TMDb… button. Mounted only while open, the
  // dialog has no DialogTrigger, so Radix has nothing to give the focus back to (it went to <body>).
  const [opener] = useState(() => document.activeElement)

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent
        ref={content}
        className="settings-dialog search-details-dialog"
        // The dialog itself, not its first control: that is Remove link, a suggestion or the paste
        // field depending on the work, may not have loaded yet, and would scroll the body past the
        // suggestions. Its title is read out, and Tab goes on from there.
        onOpenAutoFocus={(event) => {
          event.preventDefault()
          content.current?.focus()
        }}
        onCloseAutoFocus={(event) => {
          event.preventDefault()
          if (opener instanceof HTMLElement) opener.focus()
        }}
      >
        <DialogHeader>
          <DialogTitle className="search-details-title">
            {t('dialog.title', { title: shown.title ?? shown.originalTitle ?? t('library:work.unknownTitle') })}
          </DialogTitle>
          <DialogDescription>{t('dialog.description')}</DialogDescription>
        </DialogHeader>
        {load.error ? (
          <>
            <div className="settings-dialog-body">
              {/* A 403 or a 404 (the work a sync removed) is the lasting answer: no Try again for it. */}
              <ErrorState
                title={t('dialog.loadFailed')}
                onRetry={retryCanHelp(load.error) ? load.retry : undefined}
                retrying={load.retrying}
              >
                {getApiErrorMessage(load.error)}
              </ErrorState>
            </div>
            <Footer save={null} onClose={onClose} />
          </>
        ) : data === undefined ? (
          <>
            <div className="settings-dialog-body">
              <p className="settings-muted">{t('common:loading')}</p>
            </div>
            <Footer save={null} onClose={onClose} />
          </>
        ) : (
          <TmdbLinkEditor
            dialog={data}
            formId={PICK_FORM_ID}
            bodyClassName="settings-dialog-body search-details-body"
            notice={query.error && (
              <Banner action={<RetryButton onRetry={() => query.refetch()} />}>
                {t('dialog.refreshFailed')}{' '}
                {getApiErrorMessage(query.error)}
              </Banner>
            )}
            onSaved={(_, outcome) => { if (outcome === 'done') onClose() }}
            footer={(save) => <Footer save={save} onClose={onClose} />}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

/**
 * Save, then Cancel — the order every dialog in the app uses — and Close once a save is on screen: Cancel
 * would promise to cancel an identification that nothing can. Save is disabled, without a reason, until
 * the dialog has loaded (`save` null); after a save its own reason ("Pick a film or a show first.")
 * keeps it off, as the pick was spent.
 */
function Footer({ save, onClose }: { save: TmdbLinkSave | null; onClose: () => void }) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  return (
    <DialogFooter>
      <DisabledReason reason={save?.reason}>
        <Button type="submit" form={PICK_FORM_ID} disabled={save === null || save.reason !== undefined || save.saving}>
          {save?.saving ? t('dialog.saving') : t('dialog.save')}
        </Button>
      </DisabledReason>
      <Button type="button" variant="outline" onClick={onClose}>{save?.saved ? t('common:close') : t('common:cancel')}</Button>
    </DialogFooter>
  )
}
