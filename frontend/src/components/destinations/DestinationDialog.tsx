import { useId, useState, type FormEvent } from 'react'
import { TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DestinationSavesTo } from '@/components/destinations/DestinationSavesTo'
import { DestinationSectionsField } from '@/components/destinations/DestinationSectionsField'
import { Banner } from '@/components/shared/Banner'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { sectionRequests, sectionsMappedTo } from '@/lib/destination-sections'
import { useClientDestinations, useCreateDestination, useDestinationSections, useIntegrations, useUpdateDestination } from '@/lib/queries'
import type { ClientDestination, Destination, DestinationCheck, IntegrationsQuery, IntegrationSummary } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field, .auth-hint, .auth-muted — the fields as every settings dialog lays them out
import '@/styles/settings-dialog.css' // .settings-dialog*, .settings-check — the scrolling body and the buttons under it, the checkbox
import '@/styles/library.css' // .library-source-warning — a folder the client can't save to, an amber icon before its sentence
import '@/styles/destinations.css' // .destinations-folder-problems — the lines under the Folder field

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
 *
 * MAM-32 — and the release sources' tracker sections whose releases the Download dialog sends here
 * first (DestinationSectionsField). They are sent with the save only once the owner has ticked or
 * unticked one: left out, an edit keeps the sections as they are, so a sections list that failed to
 * load can't clear them.
 *
 * MAM-30 — a client that saves to folders (Transmission) offers folders instead: the media library
 * folders it can reach through its path mappings, and its own download folder, each named by its
 * libraries or as the client's folder. The field says Folder by the picked client's kind, known
 * with the clients list before its own list arrives. A folder the client can't see stays listed
 * but can't be picked, and the libraries it offers no folder for are listed under the field, each
 * with the sentence saying how to fix it. Under the name, that the client shows it as the torrent's
 * label, which can't hold a comma. Editing, its own client is also asked about the stored folder.
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
  // Null until the owner ticks or unticks a section: the picks then follow the list as it loads.
  const [pickedSections, setPickedSections] = useState<ReadonlySet<string> | null>(null)
  const nameHintId = useId()
  const folderProblemId = useId()
  // MAM-30 — editing, the destination's own client is asked about its stored folder too (D4).
  const editedId = destination !== undefined && clientId === destination.downloadClientId ? destination.id : undefined
  const categories = useClientDestinations(clientId, editedId)
  const sections = useDestinationSections()
  const picked = pickedSections ?? (sections.data ? sectionsMappedTo(sections.data, destination?.id) : new Set<string>())
  const saving = create.isPending || update.isPending

  const listedClients = clients.data?.items ?? []
  // The destination's own client stays choosable while the list doesn't hold it: still on its way,
  // or failed to load.
  const keepsSavedClient = destination !== undefined
    && !listedClients.some((client) => client.id === destination.downloadClientId)
  const pickedClient = listedClients.find((client) => client.id === clientId)
  const clientName = pickedClient?.name
    ?? (clientId === destination?.downloadClientId ? destination.downloadClientName : '')
  // MAM-30 — what the picked client's destinations are, from the clients list so the field's words
  // are right while its list loads; the saved client's own while the clients list isn't there, then
  // the list's answer. Before a client is picked, the kind every listed client shares, if one: an
  // installation with Transmission alone shouldn't ask for a category first.
  const kind = clientId === ''
    ? sharedKindOf(listedClients)
    : pickedClient?.destinationKind
      ?? (clientId === destination?.downloadClientId ? destination.destinationKind : null)
      ?? categories.data?.destinationKind
      ?? null
  const folders = kind === 'Folder'

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

  /**
   * MAM-30 — what a folder is to the owner: the libraries it holds and whether it is the client's own
   * download folder; null for a folder that is neither, named by its path alone.
   */
  const originOf = (folder: ClientDestination) => {
    const names = folder.check.libraryNames
    if (folder.isClientDefaultFolder) {
      return names.length > 0
        ? t('dialog.folderOrigin.librariesAndClient', { names, client: clientName })
        : t('dialog.folderOrigin.client', { client: clientName })
    }
    return names.length > 0 ? t('dialog.folderOrigin.libraries', { names }) : null
  }

  /** A choice of the select: a category by its name; a folder by what it is, then its path. */
  const optionLabelOf = (entry: ClientDestination) => {
    const origin = folders ? originOf(entry) : null
    const label = origin === null ? entry.displayName : t('dialog.folderOption', { origin, folder: entry.displayName })
    return entry.check.status === 'FolderNotSeen' ? t('dialog.folderOptionNotSeen', { option: label }) : label
  }

  // MAM-30 — the folders the client can't save to, each with its sentence: the ones it can't see
  // (the picked one says it under the field already), and the libraries it offers no folder for.
  const folderProblems = categories.data && categories.data.error === null
    ? [
        ...offered
          .filter((entry) => entry.check.status === 'FolderNotSeen' && entry.key !== key)
          .map((entry) => ({
            id: `folder:${entry.key}`,
            text: t('dialog.folderProblem', { origin: originOf(entry) ?? entry.displayName, problem: entry.check.problem ?? '' }),
          })),
        ...categories.data.unplaced.map((library) => ({
          id: `library:${library.libraryName}:${library.folder}`,
          text: t('dialog.folderProblem', { origin: library.libraryName, problem: library.problem }),
        })),
      ]
    : []

  const toggleSection = (key: string) => {
    const next = new Set(picked)
    if (!next.delete(key)) next.add(key)
    setPickedSections(next)
  }

  /** Another client has other categories: the one picked for the last client goes. */
  const pickClient = (next: string) => {
    setClientId(next)
    setKey('')
  }

  // What the owner should know about the category field right now; nothing once all is well.
  // MAM-30 — in a folder's words for a client that saves to folders.
  const categoryInfo = (() => {
    if (clientId === '') return <p className="auth-hint">{t('dialog.categoryNeedsClient')}</p>
    if (categories.isLoading) {
      return (
        <p className="auth-muted">
          {folders ? t('dialog.foldersLoading', { client: clientName }) : t('dialog.categoriesLoading')}
        </p>
      )
    }
    // MAM-16 — why the select has nothing to offer, in the red line a field's error takes; the select
    // itself is not marked invalid, as nothing picked in it is wrong. The same for the clients below.
    if (categories.error && !categories.data) {
      const fallback = folders ? t('dialog.foldersFailed') : t('dialog.categoriesFailed')
      return <FieldErrorLine>{getApiErrorMessage(categories.error, fallback)}</FieldErrorLine>
    }
    // 200 with the client's own sentence: it couldn't be asked (switched off, unreachable, …).
    if (categories.data?.error) return <FieldErrorLine>{categories.data.error}</FieldErrorLine>
    if (chosen) return <DestinationSavesTo check={chosen.check} />
    if (key !== '' && key === savedKey && savedKeyMissing) {
      // MAM-30 — a folder the client no longer offers is one it doesn't see, not a missing category.
      const missing: DestinationCheck = {
        status: folders ? 'FolderNotSeen' : 'CategoryMissing',
        servicePath: null,
        appPath: null,
        libraryNames: [],
        problem: folders
          ? t('dialog.folderMissingHint', { client: clientName })
          : t('dialog.categoryMissingHint', { client: clientName }),
      }
      return <DestinationSavesTo check={missing} />
    }
    if (categories.data && offered.length === 0 && !keepsSavedKey) {
      return (
        <p className="auth-hint">
          {folders ? t('dialog.noFolders', { client: clientName }) : t('dialog.noCategories', { client: clientName })}
        </p>
      )
    }
    return null
  })()
  // MAM-30 — the select reads the lines under it too, so why options are disabled and which libraries
  // have no folder are heard there. Each line by its own id, not the list's: a list named by its
  // aria-label would be read as that name in place of its lines.
  const categoryDescribedBy = [
    ...(categoryInfo ? [categoryInfoId] : []),
    ...folderProblems.map((_, at) => `${folderProblemId}-${at}`),
  ].join(' ')

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
      sections: pickedSections !== null && sections.data ? sectionRequests(sections.data, pickedSections) : undefined,
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
            {destination
              ? folders ? t('dialog.editDescriptionFolder') : t('dialog.editDescription')
              : folders ? t('dialog.createDescriptionFolder') : t('dialog.createDescription')}
          </DialogDescription>
        </DialogHeader>
        {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
        <form className="settings-dialog-form" noValidate onSubmit={(event) => void save(event)}>
          <div className="settings-dialog-body">
            {/* MAM-30 — the hint sits beside the label, not in it, so it doesn't become the field's name. */}
            <div className="auth-field">
              <label className="auth-field">
                <span className="ui-label">{t('dialog.name')}</span>
                <Input
                  value={name}
                  placeholder={t('dialog.namePlaceholder')}
                  autoComplete="off"
                  autoFocus
                  aria-describedby={folders && clientName !== '' ? nameHintId : undefined}
                  onChange={(event) => setName(event.target.value)}
                />
              </label>
              {/* MAM-30 — Transmission shows the destination's name as the torrent's label, and a label
                  can't hold a comma: said before the server refuses one. */}
              {folders && clientName !== '' && (
                <p id={nameHintId} className="auth-hint">{t('dialog.labelHint', { client: clientName })}</p>
              )}
            </div>

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
                <FieldErrorLine>{getApiErrorMessage(clients.error, t('dialog.clientsFailed'))}</FieldErrorLine>
              )}
              {clients.data && listedClients.length === 0 && !keepsSavedClient && (
                <p className="auth-hint">{t('dialog.noClients')}</p>
              )}
            </div>

            <div className="auth-field">
              <label htmlFor={categoryFieldId} className="ui-label">
                {folders ? t('dialog.folder') : t('dialog.category')}
              </label>
              <select
                id={categoryFieldId}
                className="ui-input"
                value={key}
                disabled={clientId === '' || !categories.data}
                aria-describedby={categoryDescribedBy || undefined}
                onChange={(event) => setKey(event.target.value)}
              >
                <option value="" disabled>{folders ? t('dialog.folderPlaceholder') : t('dialog.categoryPlaceholder')}</option>
                {/* MAM-30 — a folder the client can't see can't be picked (D5); the saved one stays
                    choosable, as an edit that keeps it asks nobody. */}
                {offered.map((category) => (
                  <option
                    key={category.key}
                    value={category.key}
                    disabled={category.check.status === 'FolderNotSeen' && category.key !== savedKey}
                  >
                    {optionLabelOf(category)}
                  </option>
                ))}
                {savedKey !== undefined && keepsSavedKey && (
                  <option value={savedKey}>
                    {savedKeyMissing ? t('dialog.categoryMissing', { name: savedKey }) : savedKey}
                  </option>
                )}
              </select>
              {categoryInfo && <div id={categoryInfoId}>{categoryInfo}</div>}
              {folderProblems.length > 0 && (
                <ul className="destinations-folder-problems" aria-label={t('dialog.folderProblems', { client: clientName })}>
                  {folderProblems.map((problem, at) => (
                    <li key={problem.id} id={`${folderProblemId}-${at}`} className="library-source-warning">
                      <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>{problem.text}</span>
                    </li>
                  ))}
                </ul>
              )}
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

            <DestinationSectionsField
              list={sections.data}
              isLoading={sections.isLoading}
              error={sections.error}
              destinationId={destination?.id}
              picked={picked}
              onToggle={toggleSection}
            />
          </div>
          {/* MAM-16 — outside the scrolling body, above the buttons; the fields keep what was picked. */}
          {error && <Banner>{error}</Banner>}
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

// ── helpers ──

/**
 * MAM-30 — the kind of destination every listed client has, for the field's words before one is
 * picked; null when they differ, when none is listed, or when their type isn't registered.
 */
function sharedKindOf(clients: IntegrationSummary[]) {
  const kinds = new Set(clients.map((client) => client.destinationKind))
  return kinds.size === 1 ? [...kinds][0] : null
}
