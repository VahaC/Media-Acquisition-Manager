import { useEffect, useEffectEvent, useId, useRef, useState } from 'react'
import { Ban, Check, ChevronRight, CircleCheck, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { TmdbLinkEditor, type TmdbLinkEditorHandle, type TmdbLinkSave } from '@/components/library/TmdbLinkEditor'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatNumber } from '@/lib/format'
import { i18n } from '@/lib/i18n'
import { reviewKeyOf } from '@/lib/keyboard'
import { useClearNoneFits, useLibraryLinkQueue, useMarkNoneFits } from '@/lib/queries'
import type { LibraryLinkDialog, LibraryWork } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted, .settings-empty — the loading and empty lines
import '@/styles/settings-dialog.css' // .settings-dialog-body — the editor's body, as in the dialog
import '@/styles/search.css' // .search-details-body — the dialog's two columns on a wide screen
import '@/styles/library.css' // .library-source-status, -warning — the last action's line
import '@/styles/library-links.css' // .tmdb-review-*, .tmdb-link-key — the card, its buttons and their keys

/** The Review tab's Save form; its Save button submits it from outside, as the dialog's does. */
const REVIEW_FORM_ID = 'tmdb-review-pick'

/** What the last save or "none fits" did, said above the next work. */
interface LastAction {
  kind: 'linked' | 'notWritten' | 'noneFits'
  workId: string
  text: string
}

/**
 * MAM-34 — the TMDb page's Review tab: the works without a TMDb id, one at a time, each with the TMDb
 * dialog's content inline (TmdbLinkEditor). Save links the pick, "None fits" marks that no suggestion
 * fits, Skip moves on; the first two take the work out of the queue, so the same place then shows the
 * next work, and Skip wraps to the first after the last.
 *
 * The keys of R24, while this tab is on screen with a work: 1 to 5 pick a suggestion (they only pick:
 * Enter or Save links it, one Save per surface), 0 is "None fits", → is Skip, / goes to the search
 * field. They're taken in the capture phase, before HeaderSearch's "/" (which then sees the key
 * handled and leaves it) and before a control's own handling — so the rules of `reviewKeyOf` leave
 * alone a key typed into a field, an arrow of a radio group or of the tabs, every key while a dialog
 * is open, and modified keys. Each button shows its key (not on a phone) and says it in
 * `aria-keyshortcuts`.
 */
export function TmdbReviewQueue() {
  const { t } = useTranslation(['libraryLinks', 'library', 'common'])
  const titleId = useId()
  const [index, setIndex] = useState(0)
  const queue = useLibraryLinkQueue(index)
  const { data, error, isLoading, isPlaceholderData, isFetching } = queue
  const load = useLoadRetry(queue)
  const markNoneFits = useMarkNoneFits()
  const clearNoneFits = useClearNoneFits()
  const editor = useRef<TmdbLinkEditorHandle>(null)
  const [last, setLast] = useState<LastAction | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  // The work just saved or marked: until the queue answers again, "Loading the next work…" stands in
  // for it, so a second Enter can't save it again.
  const [leaving, setLeaving] = useState<string | null>(null)

  const total = data?.totalCount ?? 0
  const current = data?.current ?? null
  // A save or "none fits" on the last work leaves the place past the end: back to the first. Only on
  // this place's own answer, not the one kept on screen while it loads.
  if (data && !isPlaceholderData && current === null && total > 0 && index > 0) setIndex(0)
  // Once another work is on screen, the one that left is forgotten: should it come back ("none fits"
  // taken back), a later poll of it mustn't hide it.
  if (leaving !== null && current !== null && current.work.id !== leaving) setLeaving(null)
  const shown = current !== null && !(leaving === current.work.id && isFetching) ? current : null

  const skip = () => {
    if (total === 0) return
    setActionError(null)
    setIndex((index + 1) % total)
  }

  const noneFits = async () => {
    if (shown === null || markNoneFits.isPending) return
    const work = shown.work
    setActionError(null)
    try {
      await markNoneFits.mutateAsync(work.id)
      setLeaving(work.id)
      setLast({ kind: 'noneFits', workId: work.id, text: t('review.markedNoneFits', { work: workTitle(work) }) })
    } catch (e: unknown) {
      setActionError(getApiErrorMessage(e, t('review.noneFitsFailed')))
    }
  }

  const undo = async (workId: string) => {
    setActionError(null)
    try {
      await clearNoneFits.mutateAsync(workId)
      setLast(null)
    } catch (e: unknown) {
      setActionError(getApiErrorMessage(e, t('review.undoFailed')))
    }
  }

  const saved = (next: LibraryLinkDialog, notWritten: boolean) => {
    const link = next.work.link
    const linkText = link === null
      ? ''
      : link.catalogTitle === null
        ? t('dialog.linkedId', { kind: t(`candidate.kind.${link.catalogKind}`), id: link.catalogId })
        : t('dialog.linkedTitle', { title: link.catalogTitle, kind: t(`candidate.kind.${link.catalogKind}`), id: link.catalogId })
    const values = { work: workTitle(next.work), link: linkText }
    setActionError(null)
    setLeaving(next.work.id)
    setLast(notWritten
      ? { kind: 'notWritten', workId: next.work.id, text: t('review.linkedNotWritten', values) }
      : { kind: 'linked', workId: next.work.id, text: t('review.linked', values) })
  }

  // The key's meaning is read when it comes, from what is on screen then; the listener itself is
  // added once, while this tab is mounted.
  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    const key = reviewKeyOf(event, REVIEW_FORM_ID)
    if (key === null) return
    // A key whose thing isn't on screen (no fifth suggestion, no work) is left to the page.
    switch (key.action) {
      case 'pick':
        if (editor.current?.pickSuggestion(key.index)) event.preventDefault()
        return
      case 'search':
        // preventDefault also tells HeaderSearch, which listens after this, to leave the "/" alone.
        if (editor.current) {
          event.preventDefault()
          editor.current.focusSearch()
        }
        return
      case 'save':
        // preventDefault: Enter on a radio of the pick would submit its form a second time.
        if (editor.current) {
          event.preventDefault()
          editor.current.save()
        }
        return
      case 'noneFits':
        if (shown !== null) {
          event.preventDefault()
          void noneFits()
        }
        return
      case 'skip':
        if (total > 0) {
          event.preventDefault()
          skip()
        }
    }
  })

  useEffect(() => {
    const listener = (event: KeyboardEvent) => onKeyDown(event)
    // Capture: before HeaderSearch's listener on the document, and before the focused control's own.
    document.addEventListener('keydown', listener, true)
    return () => document.removeEventListener('keydown', listener, true)
  }, [])

  return (
    <>
      {last && (
        <LastActionLine
          action={last}
          undoing={clearNoneFits.isPending}
          onUndo={() => void undo(last.workId)}
        />
      )}
      {actionError && <Banner onDismiss={() => setActionError(null)}>{actionError}</Banner>}

      {/* Outside the ternary below, so a failed refresh adds a line above the work instead of
          replacing it. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => queue.refetch()} />}>
          {t('review.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {load.error ? (
        <ErrorState
          title={t('review.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && total === 0 ? (
        <div className="settings-empty">
          <p>{t('review.empty')}</p>
        </div>
      ) : data && shown !== null ? (
        <section className="ui-card tmdb-review-work" aria-labelledby={titleId}>
          <div className="ui-card-header">
            {/* The place of the answer on screen, which stays while the next place loads. */}
            <p className="ui-card-description">
              {t('review.position', { position: formatNumber(data.index + 1), total: formatNumber(total) })}
            </p>
            <h2 id={titleId} className="ui-card-title">{workHeading(shown.work)}</h2>
          </div>
          <div className="ui-card-content">
            {/* A fresh editor per work: nothing picked, every item ticked. */}
            <TmdbLinkEditor
              key={shown.work.id}
              ref={editor}
              dialog={shown}
              formId={REVIEW_FORM_ID}
              bodyClassName="settings-dialog-body search-details-body"
              shortcuts
              onSaved={saved}
              footer={(save) => (
                <ReviewActions
                  save={save}
                  marking={markNoneFits.isPending}
                  onNoneFits={() => void noneFits()}
                  onSkip={skip}
                />
              )}
            />
          </div>
        </section>
      ) : data ? (
        <p className="settings-muted">{t('review.next')}</p>
      ) : null}
    </>
  )
}

// ── the buttons and the last action ──

/** Save (Enter), None fits (0), Skip (→): primary first. Each shows its key beside its label. */
function ReviewActions({ save, marking, onNoneFits, onSkip }: {
  save: TmdbLinkSave
  marking: boolean
  onNoneFits: () => void
  onSkip: () => void
}) {
  const { t } = useTranslation('libraryLinks')
  const saveLabel = save.saving ? t('review.saving') : t('review.save')
  const noneFitsLabel = marking ? t('review.marking') : t('review.noneFits')

  return (
    <div className="tmdb-review-actions">
      <DisabledReason reason={save.reason}>
        <Button
          type="submit"
          form={save.formId}
          size="sm"
          title={t('review.save')}
          aria-keyshortcuts="Enter"
          disabled={save.reason !== undefined || save.saving}
        >
          <Check className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="label-text">{saveLabel}</span>
          <ShortcutKey>{t('review.keys.save')}</ShortcutKey>
        </Button>
      </DisabledReason>
      <Button type="button" variant="outline" size="sm" title={t('review.noneFits')} aria-keyshortcuts="0" disabled={marking} onClick={onNoneFits}>
        <Ban className="h-3.5 w-3.5" aria-hidden="true" />
        <span className="label-text">{noneFitsLabel}</span>
        <ShortcutKey>{t('review.keys.noneFits')}</ShortcutKey>
      </Button>
      <Button type="button" variant="outline" size="sm" title={t('review.skip')} aria-keyshortcuts="ArrowRight" onClick={onSkip}>
        <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
        <span className="label-text">{t('review.skip')}</span>
        <ShortcutKey>{t('review.keys.skip')}</ShortcutKey>
      </Button>
    </div>
  )
}

/** A key hint; aria-hidden, as `aria-keyshortcuts` on the control tells a screen reader about the key. */
function ShortcutKey({ children }: { children: string }) {
  return <kbd className="tmdb-link-key" aria-hidden="true">{children}</kbd>
}

/**
 * What the last save or "none fits" did, `role="status"` so it is read out while the focus stays. A
 * link Jellyfin didn't take has the warning's amber icon; "none fits" can be taken back.
 */
function LastActionLine({ action, undoing, onUndo }: { action: LastAction; undoing: boolean; onUndo: () => void }) {
  const { t } = useTranslation('libraryLinks')
  const warning = action.kind === 'notWritten'

  return (
    <div className="tmdb-review-last">
      <p
        role="status"
        className={warning ? 'library-source-warning' : 'library-source-status'}
        data-status={warning ? undefined : 'Succeeded'}
      >
        {warning
          ? <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          : <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />}
        <span>{action.text}</span>
      </p>
      {action.kind === 'noneFits' && (
        <Button type="button" variant="outline" size="sm" disabled={undoing} onClick={onUndo}>
          {undoing ? t('review.undoing') : t('review.undo')}
        </Button>
      )}
    </div>
  )
}

// ── helpers ──

/** A work's title as its card heads it, or the words for an unknown one. */
function workTitle(work: LibraryWork): string {
  return work.title ?? work.originalTitle ?? i18n.t('library:work.unknownTitle')
}

/** "120. Landing Stripling (1962)", the year left out when unknown. */
function workHeading(work: LibraryWork): string {
  return work.year === null
    ? workTitle(work)
    : i18n.t('libraryLinks:candidate.titleYear', { title: workTitle(work), year: work.year })
}
