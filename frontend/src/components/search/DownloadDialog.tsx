import { useId, useRef, useState, type FormEvent } from 'react'
import { CircleCheck, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useDownloadOptions, useSendDownload } from '@/lib/queries'
import { newRequestId } from '@/lib/request-id'
import type { DownloadClientOption, DownloadDestinationOption, DownloadJobStatus, SearchRelease } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field, .auth-hint, .auth-muted — the fields as every form dialog lays them out
import '@/styles/settings-dialog.css' // .settings-dialog* — the one scrolling body, the error line above the buttons
import '@/styles/library.css' // .library-source-status, .library-source-warning — the outcome line and its icon
import '@/styles/search.css' // .search-details-* — the title that wraps clear of the close button, the summary

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

interface DownloadDialogProps {
  release: SearchRelease
  onClose: () => void
}

/**
 * MAM-13 — sends one release to a download client: a client that can take it, one of its
 * destinations (a category of the client, which decides the folder), and a summary of what goes
 * where, existing copies included — this version adds a download and never replaces or removes a
 * file. Every signed-in user may send (MAM-13's decision 1). The parent mounts it only while it is
 * open, so every opening is a new submission with its own request id.
 *
 * It opens on the default destination and the client that owns it; otherwise on the only client and
 * its only destination; otherwise on the placeholders. Every rule is the server's, shown as its
 * sentence in the form; a job that failed shows its sentence there too and keeps the form, so the
 * person can pick again and send. Any other job replaces the form with what happened, and Close.
 */
export function DownloadDialog({ release, onClose }: DownloadDialogProps) {
  const { t } = useTranslation(['downloads', 'common'])
  const options = useDownloadOptions(release.id)
  const send = useSendDownload()
  const clientFieldId = useId()
  const destinationFieldId = useId()
  const destinationHintId = useId()
  // One id per submission, made when the dialog opens. A resend after a thrown error (the network, a
  // refusal) keeps it: a request that did reach the server is then answered with the job it made
  // instead of becoming a second download. Only a Failed job replaces it, as nothing reached the
  // client and the next Send is a deliberate new attempt.
  const [requestId, setRequestId] = useState(newRequestId)
  // Undefined until the person picks, so the pick follows the options once they have loaded — derived
  // during render, never copied in by an effect.
  const [pickedClientId, setPickedClientId] = useState<string>()
  const [pickedDestinationId, setPickedDestinationId] = useState<string>()
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState<{ outcome: Outcome; clientName: string } | null>(null)
  // A second click before the button shows it is sending must not send again: isPending reaches the
  // screen only once the mutation has told React, after the click that started it.
  const inFlight = useRef(false)

  const clients = options.data?.clients ?? []
  const preselected = preselect(clients)
  const client = clients.find((candidate) => candidate.id === (pickedClientId ?? preselected.clientId))
  const destinationId = pickedDestinationId ?? preselected.destinationId
  const destination = client?.destinations.find((candidate) => candidate.id === destinationId)
  const sending = send.isPending
  const ready = client !== undefined && destination !== undefined

  /** Another client has other destinations: the last client's pick goes, the new client's own preselection comes. */
  const pickClient = (next: string) => {
    setPickedClientId(next)
    setPickedDestinationId(destinationOf(clients.find((candidate) => candidate.id === next)))
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!client || !destination || inFlight.current) return
    inFlight.current = true
    setError(null)
    try {
      const job = await send.mutateAsync({
        requestId,
        releaseId: release.id,
        downloadClientId: client.id,
        destinationId: destination.id,
      })
      inFlight.current = false
      if (job.status === 'Failed') {
        setRequestId(newRequestId())
        setError(job.error ?? t('failedNoReason'))
        return
      }
      setSent({
        outcome: job.status === 'Submitted' && job.clientJobId === null ? 'SubmittedUnlisted' : job.status,
        clientName: job.downloadClientName,
      })
    } catch (e: unknown) {
      inFlight.current = false
      setError(getApiErrorMessage(e, t('failed')))
    }
  }

  // The body while the form is up: the options' state, or the two pickers.
  const fields = (() => {
    if (options.isLoading) return <p className="auth-muted">{t('common:loading')}</p>
    if (options.error && !options.data) {
      return (
        <p className="settings-dialog-error" role="alert">
          {getApiErrorMessage(options.error, t('optionsFailed'))}
        </p>
      )
    }
    if (clients.length === 0) return <p className="auth-muted">{t('noClients')}</p>
    return (
      <>
        {/* A failed refresh keeps the clients last loaded on screen, with a line saying so. */}
        {options.error && (
          <p className="settings-dialog-error" role="alert">
            {t('optionsRefreshFailed')}{' '}{getApiErrorMessage(options.error)}
          </p>
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
        </div>
      </>
    )
  })()

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog">
        <DialogHeader>
          <DialogTitle className="search-details-title">{t('dialog.title', { title: release.title })}</DialogTitle>
          <DialogDescription>{t('dialog.description')}</DialogDescription>
        </DialogHeader>
        {sent ? (
          // Not a form any more: what happened, what was sent, and Close.
          <div className="settings-dialog-form">
            <div className="settings-dialog-body">
              <OutcomeLine outcome={sent.outcome} client={sent.clientName} />
              <SendSummary release={release} client={client} destination={destination} />
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
              {clients.length > 0 && <SendSummary release={release} client={client} destination={destination} />}
            </div>
            {error && <p className="settings-dialog-error" role="alert">{error}</p>}
            {/* Primary first, Cancel last — the order every dialog in the app uses. */}
            <DialogFooter>
              <DisabledReason reason={clients.length > 0 && !ready && !sending ? t('pickFirst') : undefined}>
                <Button type="submit" disabled={!ready || sending}>{sending ? t('sending') : t('send')}</Button>
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
 * The pick the dialog opens with: the default destination and the client that owns it, when the
 * default is one of the listed clients'; otherwise the only client, with its only destination;
 * otherwise neither (`''`, the placeholders).
 */
function preselect(clients: DownloadClientOption[]): { clientId: string; destinationId: string } {
  const client = clients.find((candidate) => candidate.destinations.some((destination) => destination.isDefault))
    ?? (clients.length === 1 ? clients[0] : undefined)
  return { clientId: client?.id ?? '', destinationId: destinationOf(client) }
}

/** A client's own destination to start from: the default when it holds it, else its only one, else none. */
function destinationOf(client: DownloadClientOption | undefined): string {
  const destinations = client?.destinations ?? []
  const chosen = destinations.find((destination) => destination.isDefault)
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
 * copies, which stay.
 */
function SendSummary({ release, client, destination }: {
  release: SearchRelease
  client: DownloadClientOption | undefined
  destination: DownloadDestinationOption | undefined
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
          {destination
            ? t('summary.destinationValue', { name: destination.name, category: destination.clientDestinationKey })
            : notPicked}
        </dd>
        <dt>{t('summary.existingCopies')}</dt>
        <dd>{t('summary.kept')}</dd>
      </dl>
    </section>
  )
}
