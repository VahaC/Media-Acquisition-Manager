import { useId, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { CircleCheck, TriangleAlert } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { ReplacementLine } from '@/components/jobs/ReplacementLine'
import { ReplacementWorks } from '@/components/search/ReplacementWorks'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useAuthStore } from '@/lib/auth-store'
import { formatNumber } from '@/lib/format'
import {
  useDownloadOptions,
  useDownloadOverwriteCheck,
  useReplacementOptions,
  useScheduleReplacement,
  useSendDownload,
} from '@/lib/queries'
import {
  holdingClients,
  replacementGroups,
  tickableIds,
  tickedInOrder,
  worksToConfirm,
  type ReplacementGroup,
} from '@/lib/replacements'
import { newRequestId } from '@/lib/request-id'
import type {
  DownloadClientOption,
  DownloadDestinationOption,
  DownloadJob,
  DownloadJobReplacement,
  DownloadJobStatus,
  SearchRelease,
} from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { cn, getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field, .auth-hint, .auth-muted — the fields as every form dialog lays them out
import '@/styles/settings-dialog.css' // .settings-dialog*, .settings-check — the one scrolling body, the buttons under it, the radios
import '@/styles/library.css' // .library-source-status, -warning, .library-file-app-path, .library-link — the lines, the paths, the link
import '@/styles/search.css' // .search-details-* — the title that wraps clear of the close button, the summary
import '@/styles/downloads.css' // .download-dialog-wide, .download-mode*, .download-summary-* — the old copies

/**
 * What the dialog says once a job comes back that isn't Failed: its status, with Submitted told
 * apart by whether the client lists the release yet.
 */
type Outcome = Exclude<DownloadJobStatus, 'Failed'> | 'SubmittedUnlisted'

/**
 * The outcomes the person should look at again: the release is on its way or reached the client, but
 * not yet as a download the app can follow.
 *
 * MAM-14 — also a paused or cancelled job: a repeated Send under the same request id answers the job
 * it already made, which the app may have seen paused in the client, or the person cancelled, since.
 */
const WARNING_OUTCOMES: ReadonlySet<Outcome> = new Set<Outcome>(['Created', 'NeedsLinking', 'Paused', 'Cancelled'])

/** MAM-24 — what a send does with the copies the library already holds: keeps them, or replaces the ticked ones. */
type CopiesMode = 'KeepBoth' | 'Replace'

/**
 * MAM-24 — the summary's Existing copies: kept, or the ticked copies by film or season with the clients
 * the conditions name. Frozen into the sent view as it was sent, so a later answer can't change it.
 */
type CopiesSummary =
  | { mode: 'KeepBoth' }
  | { mode: 'Replace'; groups: ReplacementGroup[]; clientName: string }

/** What the sent view shows: the job's outcome and its replacement, and what was sent. */
interface Sent {
  outcome: Outcome
  clientName: string
  replacement: DownloadJobReplacement | null
  copies: CopiesSummary
}

interface DownloadDialogProps {
  release: SearchRelease
  onClose: () => void
}

/**
 * MAM-13 — sends one release to a download client: a client that can take it, one of its
 * destinations (a category of the client, which decides the folder, or — MAM-30 — a folder the client
 * saves to, named by its name alone), and a summary of what goes where, existing copies included. Every signed-in user may send (MAM-13's decision 1). MAM-38 — the
 * server refuses a release whose files would land on a different file already in the destination's
 * folder (a Failed job, its sentence naming the file); where it can't look into that folder, a
 * warning under the destination says so before Send, and Send waits for the first answer about the
 * folder picked. The parent mounts it only while it is open, so every opening is a new submission
 * with its own request id.
 *
 * It opens on the default destination and the client that owns it; otherwise on the only client and
 * its only destination; otherwise on the placeholders. MAM-32 — the server now names the destination
 * to open on (`suggestedDestinationId`): the one the release's tracker section is mapped to, else the
 * default. None suggested — two sections' destinations tie, or there is no default — opens on the
 * placeholders, unless there is only one destination to pick. A client picked by hand comes with the
 * suggested destination when it holds it, else the default when it holds that, else its only one,
 * else none. Every rule is the server's, shown as its
 * sentence in the form; a job that failed shows its sentence there too and keeps the form, so the
 * person can pick again and send. Any other job replaces the form with what happened, and Close.
 *
 * MAM-24 — Existing copies: Keep both, the default, or Replace after verification, the installation
 * owner's alone (ADR 0033; a member reads why, and nothing is asked). Once a destination is picked,
 * the owner's copies of the matched works are listed with why each can or can't be replaced, read-only
 * under Keep both; picked, Replace reads the torrents holding them too (ADR 0031), and Send schedules
 * the replacement of the copies ticked, with the title-and-year matches confirmed (ADR 0032). The mode
 * is the person's click alone: an answer that takes Replace away keeps it picked, with Send disabled
 * and the reason, and never falls back to Keep both on its own (Vasyl's decision 7 of 2026-10-09).
 * The ticks that count are worked out during render from the latest answer, so a destination's copies
 * never carry another's ticks, and a copy that can't be replaced any more is never sent.
 */
export function DownloadDialog({ release, onClose }: DownloadDialogProps) {
  const { t } = useTranslation(['downloads', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const options = useDownloadOptions(release.id)
  const optionsLoad = useLoadRetry(options)
  const send = useSendDownload()
  const schedule = useScheduleReplacement()
  const clientFieldId = useId()
  const destinationFieldId = useId()
  const destinationHintId = useId()
  const warningId = useId()
  const modeName = useId()
  const replaceHintId = useId()
  const replacementErrorId = useId()
  const clientWarningId = useId()
  // One id per submission, made when the dialog opens. A resend after a thrown error (the network, a
  // refusal) keeps it: a request that did reach the server is then answered with the job it made
  // instead of becoming a second download. Only a Failed job replaces it, as nothing reached the
  // client and the next Send is a deliberate new attempt.
  const [requestId, setRequestId] = useState(newRequestId)
  // Undefined until the person picks, so the pick follows the options once they have loaded — derived
  // during render, never copied in by an effect.
  const [pickedClientId, setPickedClientId] = useState<string>()
  const [pickedDestinationId, setPickedDestinationId] = useState<string>()
  // MAM-24 — Keep both until the person picks Replace, and never back on its own. The picks are kept
  // across both modes and every answer; what is sent is worked out from them during render.
  const [mode, setMode] = useState<CopiesMode>('KeepBoth')
  const [pickedFiles, setPickedFiles] = useState<ReadonlySet<string>>(new Set())
  const [pickedConfirmations, setPickedConfirmations] = useState<ReadonlySet<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState<Sent | null>(null)
  // A second click before the button shows it is sending must not send again: isPending reaches the
  // screen only once the mutation has told React, after the click that started it.
  const inFlight = useRef(false)

  const clients = options.data?.clients ?? []
  const suggestedId = options.data?.suggestedDestinationId ?? null
  const preselected = preselect(clients, suggestedId)
  const client = clients.find((candidate) => candidate.id === (pickedClientId ?? preselected.clientId))
  const destinationId = pickedDestinationId ?? preselected.destinationId
  const destination = client?.destinations.find((candidate) => candidate.id === destinationId)
  const sending = send.isPending || schedule.isPending
  const ready = client !== undefined && destination !== undefined
  const replace = mode === 'Replace'
  // MAM-38 — asked once a destination is picked; a question that failed warns of nothing, as the send
  // checks again whatever the dialog showed.
  const overwrite = useDownloadOverwriteCheck(destination?.id ?? '')
  const notCheckedReason = ready ? (overwrite.data?.notCheckedReason ?? null) : null
  // Send waits for the first answer about the folder picked, so the warning is there before anyone can
  // send — a dialog opened on a suggested destination asks only once the options are in, and the
  // server asks the client in turn. Never once the question has failed, which warns of nothing: the
  // send checks again anyway, and a Send held for a question that keeps failing would never let go.
  const checking = ready && overwrite.isFetching && overwrite.data === undefined && overwrite.errorUpdateCount === 0
  // MAM-30 — a folder its download client can't see takes no send at all, Keep both's included (D5): the
  // one warning says so in either mode, never "pick Keep both", and Send waits in either mode, as the
  // server would only fail the job.
  const folderNotSeen = notCheckedReason === 'NotCheckedFolderNotSeen'
  const replaceNotChecked = folderNotSeen
    ? t('overwrite.NotCheckedFolderNotSeen')
    : t('replace.notChecked')

  // MAM-24 — the owner's copies through the destination picked; never asked for a member, whose route
  // it isn't. The held marks (every client's torrents) only once Replace is picked.
  const replacement = useReplacementOptions(release.id, destination?.id ?? '', replace, isOwner && ready)
  const replacementLoad = useLoadRetry(replacement)
  // Another answer standing in while this one loads (another destination's, the one without the held
  // marks) still lists its copies, but decides nothing: Replace and Send wait for the answer asked.
  const answer = isOwner && ready ? replacement.data : undefined
  const tickable = tickableIds(answer)
  const ticked: ReadonlySet<string> = new Set([...pickedFiles].filter((fileId) => tickable.has(fileId)))
  const works = answer !== undefined && answer.unavailable === null ? answer.works : []
  const toConfirm = answer === undefined ? [] : worksToConfirm(answer, ticked)
  const confirmedWorkIds = toConfirm.filter((work) => pickedConfirmations.has(work.workId)).map((work) => work.workId)
  const unconfirmed = toConfirm.find((work) => !pickedConfirmations.has(work.workId))
  const clientWarnings = answer === undefined || works.length === 0 ? [] : answer.clients.filter((check) => check.status !== 'Read')

  /**
   * Why Replace can't be picked, or — picked — can't go yet, as a sentence: the member's, the missing
   * destination, the answer on its way, the release's reason, or no copy to tick. Null when nothing
   * stands in the way, and while the first answer failed, which its error state says.
   */
  const replaceReason = ((): { text: string; node: ReactNode } | null => {
    const plain = (text: string) => ({ text, node: text })
    if (!isOwner) return plain(t('replace.notOwner'))
    if (!ready) return plain(t('replace.pickDestination'))
    if (replacementLoad.error) return null
    if (answer === undefined || replacement.isPlaceholderData) return plain(t('replace.checking'))
    switch (answer.unavailable) {
      case null:
        return tickable.size === 0 ? plain(t('replace.noneAvailable')) : null
      case 'SwitchedOff':
        // The page where it is switched on, a link in the sentence; Send's hover reads it plain. The tag
        // is <page>, not <link>: Trans reads <link> as HTML's empty element and drops the words in it.
        return {
          text: t('replace.unavailable.SwitchedOff'),
          node: (
            <Trans
              t={t}
              i18nKey="replace.switchedOffLinked"
              components={{ page: <Link className="library-link" to="/settings/media-folders" /> }}
            />
          ),
        }
      case 'DestinationNotUsable':
        return plain(answer.destinationProblem ?? t('replace.unavailable.DestinationNotUsable'))
      default:
        return plain(t(`replace.unavailable.${answer.unavailable}`))
    }
  })()
  // Disabled only while it isn't picked: picked, it stays so, and Send says why it waits.
  const replaceDisabled = !replace && (replaceReason !== null || replacementLoad.error !== null)

  /**
   * MAM-24 — why a replacement can't be sent yet, in this order: the copies are being checked, the
   * check failed, the folder's first answer is on its way, the release's reason, a folder the app
   * can't check for files already there (a replacement goes only after that check), no copy to tick,
   * none ticked, a match by title and year not confirmed.
   */
  const replaceSendReason = (): string | undefined => {
    if (replacement.isFetching) return t('replace.checking')
    if (replacement.error) return t('replace.optionsFailed')
    if (answer === undefined || replacement.isPlaceholderData) return t('replace.checking')
    if (checking) return t('checkingFolder')
    if (answer.unavailable !== null) return replaceReason?.text
    if (notCheckedReason) return replaceNotChecked
    if (tickable.size === 0) return t('replace.noneAvailable')
    if (ticked.size === 0) return t('replace.noneTicked')
    if (unconfirmed) return t('replace.confirmFirst', { text: unconfirmed.confirmationText })
    return undefined
  }

  /** Why Send is disabled, shown on hover; none while it is sending, which the button itself says. */
  const disabledReason = () => {
    if (sending) return undefined
    if (clients.length > 0 && !ready) return t('pickFirst')
    if (replace) return replaceSendReason()
    if (checking) return t('checkingFolder')
    if (folderNotSeen) return t('overwrite.NotCheckedFolderNotSeen')
    return undefined
  }
  const replaceHeld = replace && ready && replaceSendReason() !== undefined
  const keepBothHeld = !replace && folderNotSeen

  /** Ticks or unticks copies; a pick the answer no longer lets be ticked simply doesn't count. */
  const tick = (fileIds: readonly string[], on: boolean) => {
    setPickedFiles((current) => {
      const next = new Set(current)
      for (const fileId of fileIds) {
        if (on) next.add(fileId)
        else next.delete(fileId)
      }
      return next
    })
  }

  const confirm = (workId: string, on: boolean) => {
    setPickedConfirmations((current) => {
      const next = new Set(current)
      if (on) next.add(workId)
      else next.delete(workId)
      return next
    })
  }

  /**
   * The summary's Existing copies for the form as it stands. Its conditions name the clients whose
   * torrents hold ticked copies, where the owner removes them; while none does, the client sent to.
   */
  const holders = answer === undefined ? [] : holdingClients(answer, ticked)
  const copiesSummary: CopiesSummary = replace
    ? {
        mode: 'Replace',
        groups: answer === undefined ? [] : replacementGroups(answer, ticked),
        clientName: holders.length > 0 ? holders.join(t('common:separator')) : client?.name ?? '',
      }
    : { mode: 'KeepBoth' }

  /** Another client has other destinations: the last client's pick goes, the new client's own preselection comes. */
  const pickClient = (next: string) => {
    setPickedClientId(next)
    setPickedDestinationId(destinationPickedWith(clients.find((candidate) => candidate.id === next), suggestedId))
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!client || !destination || inFlight.current) return
    // Enter in a field submits the form whatever Send shows: a replacement goes only as Send allows it.
    if (replace && (answer === undefined || replaceSendReason() !== undefined)) return
    if (keepBothHeld) return
    inFlight.current = true
    setError(null)
    const download = { requestId, releaseId: release.id, downloadClientId: client.id, destinationId: destination.id }
    // What the summary showed at Send, kept for the sent view.
    const copies = copiesSummary
    try {
      // The ticks, the radios and the confirmations are disabled while it goes, so a resend under the
      // same request id carries the same body.
      const job: DownloadJob = replace && answer !== undefined
        ? await schedule.mutateAsync({ ...download, fileIds: tickedInOrder(answer, ticked), confirmedWorkIds })
        : await send.mutateAsync(download)
      inFlight.current = false
      if (job.status === 'Failed') {
        setRequestId(newRequestId())
        setError(job.error ?? t('failedNoReason'))
        return
      }
      setSent({
        outcome: job.status === 'Submitted' && job.clientJobId === null ? 'SubmittedUnlisted' : job.status,
        clientName: job.downloadClientName,
        replacement: job.replacement,
        copies,
      })
    } catch (e: unknown) {
      inFlight.current = false
      setError(getApiErrorMessage(e, t('failed')))
    }
  }

  // MAM-24 — Keep both or Replace, why Replace can't be picked, the copies' read failing, the clients
  // whose torrents couldn't be read, and the copies. Plain lines, no live region: only a failure
  // interrupts (ErrorState, Banner), and Send reads the warnings it is held for or warned of.
  const existingCopies = (
    <>
      <fieldset className="download-mode">
        <legend className="ui-label">{t('replace.legend')}</legend>
        <div className="download-mode-options">
          <label className="settings-check">
            <input
              type="radio"
              name={modeName}
              value="KeepBoth"
              checked={!replace}
              disabled={sending}
              onChange={() => setMode('KeepBoth')}
            />
            <span>{t('replace.keepBoth')}</span>
          </label>
          <label className="settings-check">
            <input
              type="radio"
              name={modeName}
              value="Replace"
              checked={replace}
              disabled={sending || replaceDisabled}
              aria-describedby={replaceReason !== null ? replaceHintId : replacementLoad.error ? replacementErrorId : undefined}
              onChange={() => setMode('Replace')}
            />
            <span>{t('replace.replace')}</span>
          </label>
        </div>
        {replaceReason !== null && <p id={replaceHintId} className="auth-hint">{replaceReason.node}</p>}
      </fieldset>
      {replacementLoad.error && (
        // A 404 for a release that expired or a destination removed won't change on a second try.
        <div id={replacementErrorId}>
          <ErrorState
            title={t('replace.optionsFailed')}
            onRetry={retryCanHelp(replacementLoad.error) ? replacementLoad.retry : undefined}
            retrying={replacementLoad.retrying}
          >
            {getApiErrorMessage(replacementLoad.error)}
          </ErrorState>
        </div>
      )}
      {/* A failed refresh keeps the copies last listed on screen, with a banner saying so. */}
      {answer !== undefined && replacement.error && (
        <Banner action={retryCanHelp(replacement.error) ? <RetryButton onRetry={() => replacement.refetch()} /> : undefined}>
          {t('replace.optionsRefreshFailed')}{' '}{getApiErrorMessage(replacement.error)}
        </Banner>
      )}
      {clientWarnings.map((check, at) => (
        <p key={check.clientName} id={`${clientWarningId}-${at}`} className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>
            {check.status === 'Partial'
              ? t('replace.clientPartial', { count: check.unplacedTorrents, value: formatNumber(check.unplacedTorrents), client: check.clientName })
              : check.error === null
                ? t('replace.clientNotReadNoReason', { client: check.clientName })
                : t('replace.clientNotRead', { client: check.clientName, error: check.error })}
          </span>
        </p>
      ))}
      {answer !== undefined && works.length > 0 && (
        <ReplacementWorks
          release={release}
          options={answer}
          ticked={ticked}
          confirmed={pickedConfirmations}
          readOnly={!replace || sending}
          onTick={tick}
          onConfirm={confirm}
        />
      )}
    </>
  )
  // In Replace mode Send reads what it is warned of: the folder that can't be checked, and the clients
  // whose torrents couldn't be read for the held marks.
  const sendDescribedBy = [
    ...(notCheckedReason ? [warningId] : []),
    ...(replace ? clientWarnings.map((_, at) => `${clientWarningId}-${at}`) : []),
  ].join(' ')

  // The body while the form is up: the options' state, or the two pickers.
  const fields = (() => {
    if (optionsLoad.error) {
      // A 404 for a release that expired won't change on a second try: no Try again then.
      return (
        <ErrorState
          title={t('optionsFailed')}
          onRetry={retryCanHelp(optionsLoad.error) ? optionsLoad.retry : undefined}
          retrying={optionsLoad.retrying}
        >
          {getApiErrorMessage(optionsLoad.error)}
        </ErrorState>
      )
    }
    if (options.isLoading) return <p className="auth-muted">{t('common:loading')}</p>
    if (clients.length === 0) return <p className="auth-muted">{t('noClients')}</p>
    return (
      <>
        {/* A failed refresh keeps the clients last loaded on screen, with a banner saying so; Retry
            only where a second try can help, as on the first load. */}
        {options.error && (
          <Banner
            action={retryCanHelp(options.error) ? <RetryButton onRetry={() => options.refetch()} /> : undefined}
          >
            {t('optionsRefreshFailed')}{' '}{getApiErrorMessage(options.error)}
          </Banner>
        )}
        {/* MAM-4 — each label sits beside its select, tied by id: a label wrapping a select made the
            browser read the select's own value into its name. */}
        <div className="auth-field">
          <label htmlFor={clientFieldId} className="ui-label">{t('client')}</label>
          <select
            id={clientFieldId}
            className="ui-input"
            value={client?.id ?? ''}
            disabled={sending}
            onChange={(event) => pickClient(event.target.value)}
          >
            <option value="" disabled>{t('clientPlaceholder')}</option>
            {clients.map((option) => <option key={option.id} value={option.id}>{option.name}</option>)}
          </select>
        </div>
        <div className="auth-field">
          <label htmlFor={destinationFieldId} className="ui-label">{t('destination')}</label>
          <select
            id={destinationFieldId}
            className="ui-input"
            value={destination?.id ?? ''}
            disabled={!client || sending}
            aria-describedby={client ? undefined : destinationHintId}
            onChange={(event) => setPickedDestinationId(event.target.value)}
          >
            <option value="" disabled>{t('destinationPlaceholder')}</option>
            {client?.destinations.map((option) => <option key={option.id} value={option.id}>{option.name}</option>)}
          </select>
          {!client && <p id={destinationHintId} className="auth-hint">{t('destinationNeedsClient')}</p>}
          {/* MAM-38 — under the destination it is about. MAM-24 — a replacement never goes without the
              check, so in Replace mode it says so rather than that the release goes without it. */}
          {notCheckedReason && (
            <Banner tone="warning" id={warningId}>
              {replace ? replaceNotChecked : t(`overwrite.${notCheckedReason}`)}
            </Banner>
          )}
        </div>
        {existingCopies}
      </>
    )
  })()

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      {/* MAM-24 — wider once the owner's copies are listed: on the answer, not on the radio, so the
          radios never move under the pointer. */}
      <DialogContent className={cn('settings-dialog', works.length > 0 && 'download-dialog-wide')}>
        <DialogHeader>
          <DialogTitle className="search-details-title">{t('dialog.title', { title: release.title })}</DialogTitle>
          <DialogDescription>{t('dialog.description')}</DialogDescription>
        </DialogHeader>
        {sent ? (
          // Not a form any more: what happened, what was sent, and Close.
          <div className="settings-dialog-form">
            <div className="settings-dialog-body">
              <OutcomeLine outcome={sent.outcome} client={sent.clientName} />
              {sent.replacement && (
                <ReplacementLine replacement={sent.replacement} client={sent.clientName} downloadFailed={false} />
              )}
              <SendSummary release={release} client={client} destination={destination} copies={sent.copies} />
            </div>
            <DialogFooter>
              <Button type="button" autoFocus onClick={onClose}>{t('common:close')}</Button>
            </DialogFooter>
          </div>
        ) : (
          // noValidate: the rules live on the server, and its sentence is the one to show.
          <form className="settings-dialog-form" noValidate onSubmit={(event) => void submit(event)}>
            <div className="settings-dialog-body">
              {fields}
              {clients.length > 0 && (
                <SendSummary release={release} client={client} destination={destination} copies={copiesSummary} />
              )}
            </div>
            {/* MAM-16 — outside the scrolling body, above the buttons; the picks stay to send again. */}
            {error && <Banner>{error}</Banner>}
            {/* Primary first, Cancel last — the order every dialog in the app uses. */}
            <DialogFooter>
              <DisabledReason reason={disabledReason()}>
                {/* MAM-38 — focusing Send reads the warning: a live region inserted already filled often
                    isn't read out, and the warning is meant to be heard before Send. */}
                <Button
                  type="submit"
                  disabled={!ready || sending || checking || replaceHeld || keepBothHeld}
                  aria-describedby={sendDescribedBy === '' ? undefined : sendDescribedBy}
                >
                  {sending ? t('sending') : t('send')}
                </Button>
              </DisabledReason>
              <Button type="button" variant="outline" onClick={onClose}>{t('common:cancel')}</Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}

/**
 * The pick the dialog opens with: the suggested destination and the client that owns it (MAM-32 —
 * the server's pick, by the release's tracker section or else the default); otherwise the only
 * client, with its only destination; otherwise neither (`''`, the placeholders).
 */
function preselect(clients: DownloadClientOption[], suggestedId: string | null): { clientId: string; destinationId: string } {
  const client = clients.find((candidate) => candidate.destinations.some((destination) => destination.id === suggestedId))
    ?? (clients.length === 1 ? clients[0] : undefined)
  return { clientId: client?.id ?? '', destinationId: destinationOf(client, suggestedId) }
}

/**
 * A client's own destination to open on: the suggested one when it holds it, else its only one, else
 * none. Never the default by itself: none suggested means the server weighed it and picked nothing.
 */
function destinationOf(client: DownloadClientOption | undefined, suggestedId: string | null): string {
  const destinations = client?.destinations ?? []
  const chosen = destinations.find((destination) => destination.id === suggestedId)
    ?? (destinations.length === 1 ? destinations[0] : undefined)
  return chosen?.id ?? ''
}

/**
 * A client's own destination once the person picks that client by hand: the suggested one when it
 * holds it, else the installation's default when it holds that, else its only one, else none. The
 * person chose the client, so its default is a fair start even where the server suggested nothing.
 */
function destinationPickedWith(client: DownloadClientOption | undefined, suggestedId: string | null): string {
  const destinations = client?.destinations ?? []
  const chosen = destinations.find((destination) => destination.id === suggestedId)
    ?? destinations.find((destination) => destination.isDefault)
    ?? (destinations.length === 1 ? destinations[0] : undefined)
  return chosen?.id ?? ''
}

// ── what happened ──

/** The client's answer as one line: a tick when it took the release, a warning when it is still to be checked. */
function OutcomeLine({ outcome, client }: { outcome: Outcome; client: string }) {
  const { t } = useTranslation('downloads')
  const text = <span>{t(`outcome.${outcome}`, { client })}</span>

  if (WARNING_OUTCOMES.has(outcome)) {
    return (
      <p className="library-source-warning" role="status">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        {text}
      </p>
    )
  }
  return (
    <p className="library-source-status" data-status="Succeeded" role="status">
      <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
      {text}
    </p>
  )
}

// ── the summary ──

/**
 * What Send sends, as the Details dialog lists a release's facts: the source and the release, the
 * client and the destination with its category (the client's own name for it), and the existing
 * copies — kept, or, MAM-24, the ones replaced after verification.
 */
function SendSummary({ release, client, destination, copies }: {
  release: SearchRelease
  client: DownloadClientOption | undefined
  destination: DownloadDestinationOption | undefined
  copies: CopiesSummary
}) {
  const { t } = useTranslation('downloads')
  const titleId = useId()
  const notPicked = t('summary.notPicked')

  return (
    <section className="search-details-column" aria-labelledby={titleId}>
      <h3 id={titleId} className="search-details-heading">{t('summary.title')}</h3>
      <dl className="search-details-facts">
        <dt>{t('summary.source')}</dt>
        <dd>{release.sourceName}</dd>
        <dt>{t('summary.release')}</dt>
        <dd>{release.title}</dd>
        <dt>{t('summary.client')}</dt>
        <dd>{client?.name ?? notPicked}</dd>
        <dt>{t('summary.destination')}</dt>
        <dd>
          {/* MAM-30 — a folder destination is named by its name alone, never as a "category": it comes without
              its key (members never see a folder), and a folder client's key would still be no category. */}
          {!destination
            ? notPicked
            : destination.clientDestinationKey === null || client?.destinationKind === 'Folder'
              ? destination.name
              : t('summary.destinationValue', { name: destination.name, category: destination.clientDestinationKey })}
        </dd>
        <dt>{t('summary.existingCopies')}</dt>
        <dd><ExistingCopiesSummary copies={copies} /></dd>
      </dl>
    </section>
  )
}

/**
 * MAM-24 — the Existing copies row: kept, nothing ticked yet, or the copies replaced by film or season
 * with their paths in the app, then the conditions, worded for what this version does: it schedules
 * the replacement and, once the download is done, checks the new file (MAM-25), then moves the old
 * copy into the quarantine, where it stays until the owner restores it or deletes it for good (MAM-26).
 */
function ExistingCopiesSummary({ copies }: { copies: CopiesSummary }) {
  const { t } = useTranslation('downloads')

  if (copies.mode === 'KeepBoth') return t('summary.kept')
  if (copies.groups.length === 0) return t('summary.notPicked')
  return (
    <>
      <p>{t('summary.replaced')}</p>
      <ul className="download-summary-groups">
        {/* A fixed order, and two groups may read alike: keyed by place. */}
        {copies.groups.map((group, at) => (
          <li key={at}>
            {t('summary.replacedGroup', { group: group.name, count: group.paths.length, value: formatNumber(group.paths.length) })}
            <ul className="download-summary-paths">
              {group.paths.map((path) => <li key={path} className="library-file-app-path">{path}</li>)}
            </ul>
          </li>
        ))}
      </ul>
      <p>{t('summary.conditions', { client: copies.clientName })}</p>
    </>
  )
}
