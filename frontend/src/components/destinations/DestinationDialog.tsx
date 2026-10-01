import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { DestinationSavesTo } from '@/components/destinations/DestinationSavesTo'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useClientDestinations, useCreateDestination, useIntegrations, useUpdateDestination } from '@/lib/queries'
import type { Destination, DestinationCheck, IntegrationsQuery } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field, .auth-hint, .auth-muted — the fields as every settings dialog lays them out
import '@/styles/settings-dialog.css' // .settings-dialog*, .settings-check — the scrolling body, the error line, the checkbox

/**
 * The download clients to pick from: the server filters them by kind and orders them by name. The
 * API's largest page — an installation has a handful of download clients, not a hundred.
 */
const DOWNLOAD_CLIENTS: IntegrationsQuery = { kind: 'DownloadClient', page: 1, pageSize: 100 }

interface DestinationDialogProps {
  /** The destination to edit; without it the dialog adds a new one. */
  destination?: Destination
  onClose: () => void
}

/**
 * MAM-11 — adds a destination or edits one: a name, a download client, one of the client's own
 * categories, and whether it is the default. The parent mounts it only while it is open, so every
 * opening starts from the saved state. Its one Save commits every field, and it closes only once the
 * server has taken them.
 *
 * The categories are asked from the client live once one is picked, each with where it saves, so
 * the owner sees before saving whether the folder lands in a media library. Editing keeps the saved
 * category choosable even when the client no longer has it, marked as missing: a new name or the
 * default alone can still be saved.
 *
 * Every rule is the server's, shown as its sentence — the client confirming the category included.
 */
export function DestinationDialog({ destination, onClose }: DestinationDialogProps) {
  const { t } = useTranslation(['destinations', 'common'])
  const clients = useIntegrations(DOWNLOAD_CLIENTS)
  const create = useCreateDestination()
  const update = useUpdateDestination()
  const clientFieldId = useId()
  const categoryFieldId = useId()
  const categoryInfoId = useId()
  const defaultHintId = useId()
  const [name, setName] = useState(destination?.name ?? '')
  const [clientId, setClientId] = useState(destination?.downloadClientId ?? '')
  const [key, setKey] = useState(destination?.clientDestinationKey ?? '')
  const [isDefault, setIsDefault] = useState(destination?.isDefault ?? false)
  const [error, setError] = useState<string | null>(null)
  const categories = useClientDestinations(clientId)
  const saving = create.isPending || update.isPending

  const listedClients = clients.data?.items ?? []
  // The destination's own client stays choosable while the list doesn't hold it: still on its way,
  // or failed to load.
  const keepsSavedClient = destination !== undefined
    && !listedClients.some((client) => client.id === destination.downloadClientId)
  const clientName = listedClients.find((client) => client.id === clientId)?.name
    ?? (clientId === destination?.downloadClientId ? destination.downloadClientName : '')

  const offered = categories.data?.items ?? []
  const savedKey = destination !== undefined && clientId === destination.downloadClientId
    ? destination.clientDestinationKey
    : undefined
  // Kept while the client doesn't list it: gone from the client, or the client not asked yet.
  const keepsSavedKey = savedKey !== undefined && !offered.some((category) => category.key === savedKey)
  // Missing only once the client has answered with its list; a client that couldn't be asked says
  // nothing about it.
  const savedKeyMissing = keepsSavedKey && categories.data !== undefined && categories.data.error === null
  const chosen = offered.find((category) => category.key === key)

  /** Another client has other categories: the one picked for the last client goes. */
  const pickClient = (next: string) => {
    setClientId(next)
    setKey('')
  }

  // What the owner should know about the category field right now; nothing once all is well.
  const categoryInfo = (() => {
    if (clientId === '') return <p className="auth-hint">{t('dialog.categoryNeedsClient')}</p>
    if (categories.isLoading) return <p className="auth-muted">{t('dialog.categoriesLoading')}</p>
    if (categories.error && !categories.data) {
      return (
        <p className="settings-dialog-error" role="alert">
          {getApiErrorMessage(categories.error, t('dialog.categoriesFailed'))}
        </p>
      )
    }
    // 200 with the client's own sentence: it couldn't be asked (switched off, unreachable, …).
    if (categories.data?.error) return <p className="settings-dialog-error" role="alert">{categories.data.error}</p>
    if (chosen) return <DestinationSavesTo check={chosen.check} />
    if (key !== '' && key === savedKey && savedKeyMissing) {
      const missing: DestinationCheck = {
        status: 'CategoryMissing',
        servicePath: null,
        appPath: null,
        libraryNames: [],
        problem: t('dialog.categoryMissingHint', { client: clientName }),
      }
      return <DestinationSavesTo check={missing} />
    }
    if (categories.data && offered.length === 0 && !keepsSavedKey) {
      return <p className="auth-hint">{t('dialog.noCategories', { client: clientName })}</p>
    }
    return null
  })()

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    const request = {
      name: name.trim(),
      // Left out rather than sent empty: the server answers "pick one" for a missing client, where an
      // empty id would be a body it can't read.
      downloadClientId: clientId || undefined,
      // As the client spells it, never trimmed: it is the client's own name for the category.
      clientDestinationKey: key,
      isDefault,
    }
    try {
      if (destination) await update.mutateAsync({ id: destination.id, request })
      else await create.mutateAsync(request)
      onClose()
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, destination ? t('dialog.editFailed') : t('dialog.createFailed')))
    }
  }

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog">
        <DialogHeader>
          <DialogTitle>
            {destination ? t('dialog.editTitle', { name: destination.name }) : t('dialog.createTitle')}
          </DialogTitle>
          <DialogDescription>
            {destination ? t('dialog.editDescription') : t('dialog.createDescription')}
          </DialogDescription>
        </DialogHeader>
        {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
        <form className="settings-dialog-form" noValidate onSubmit={(event) => void save(event)}>
          <div className="settings-dialog-body">
            <label className="auth-field">
              <span className="ui-label">{t('dialog.name')}</span>
              <Input
                value={name}
                placeholder={t('dialog.namePlaceholder')}
                autoComplete="off"
                autoFocus
                onChange={(event) => setName(event.target.value)}
              />
            </label>

            {/* MAM-4 — the label sits beside the select, tied by id: a label wrapping a select made
                the browser read the select's own value into its name. */}
            <div className="auth-field">
              <label htmlFor={clientFieldId} className="ui-label">{t('dialog.client')}</label>
              <select
                id={clientFieldId}
                className="ui-input"
                value={clientId}
                disabled={!clients.data && !keepsSavedClient}
                onChange={(event) => pickClient(event.target.value)}
              >
                <option value="" disabled>{t('dialog.clientPlaceholder')}</option>
                {listedClients.map((client) => (
                  <option key={client.id} value={client.id}>
                    {client.isEnabled ? client.name : t('dialog.clientSwitchedOff', { name: client.name })}
                  </option>
                ))}
                {destination && keepsSavedClient && (
                  <option value={destination.downloadClientId}>{destination.downloadClientName}</option>
                )}
              </select>
              {clients.isLoading && <p className="auth-muted">{t('common:loading')}</p>}
              {clients.error && !clients.data && (
                <p className="settings-dialog-error" role="alert">
                  {getApiErrorMessage(clients.error, t('dialog.clientsFailed'))}
                </p>
              )}
              {clients.data && listedClients.length === 0 && !keepsSavedClient && (
                <p className="auth-hint">{t('dialog.noClients')}</p>
              )}
            </div>

            <div className="auth-field">
              <label htmlFor={categoryFieldId} className="ui-label">{t('dialog.category')}</label>
              <select
                id={categoryFieldId}
                className="ui-input"
                value={key}
                disabled={clientId === '' || !categories.data}
                aria-describedby={categoryInfo ? categoryInfoId : undefined}
                onChange={(event) => setKey(event.target.value)}
              >
                <option value="" disabled>{t('dialog.categoryPlaceholder')}</option>
                {offered.map((category) => (
                  <option key={category.key} value={category.key}>{category.displayName}</option>
                ))}
                {savedKey !== undefined && keepsSavedKey && (
                  <option value={savedKey}>
                    {savedKeyMissing ? t('dialog.categoryMissing', { name: savedKey }) : savedKey}
                  </option>
                )}
              </select>
              {categoryInfo && <div id={categoryInfoId}>{categoryInfo}</div>}
            </div>

            <div className="auth-field">
              <label className="settings-check">
                <input
                  type="checkbox"
                  checked={isDefault}
                  aria-describedby={defaultHintId}
                  onChange={(event) => setIsDefault(event.target.checked)}
                />
                {t('dialog.default')}
              </label>
              <p id={defaultHintId} className="auth-hint">{t('dialog.defaultHint')}</p>
            </div>
          </div>
          {error && <p className="settings-dialog-error" role="alert">{error}</p>}
          {/* Primary first, Cancel last — the order every dialog in the app uses. */}
          <DialogFooter>
            <Button type="submit" disabled={saving}>
              {destination
                ? saving ? t('dialog.editSubmitting') : t('dialog.editSubmit')
                : saving ? t('dialog.createSubmitting') : t('dialog.createSubmit')}
            </Button>
            <Button type="button" variant="outline" onClick={onClose}>{t('common:cancel')}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
