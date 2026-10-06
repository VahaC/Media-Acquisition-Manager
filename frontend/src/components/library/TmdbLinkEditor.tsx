import { useId, useImperativeHandle, useRef, useState, type FormEvent, type ReactNode, type Ref } from 'react'
import { CircleAlert, Film, RotateCw, Search, TriangleAlert, Tv, Unlink, Upload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LinkCandidateOption } from '@/components/library/LinkCandidateOption'
import { LinkStateBadge } from '@/components/library/LinkStateBadge'
import { Banner } from '@/components/shared/Banner'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { Field } from '@/components/shared/Field'
import { SegmentedControl } from '@/components/shared/SegmentedControl'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatDateTime } from '@/lib/format'
import { i18n } from '@/lib/i18n'
import {
  useLibraryLinkLookup,
  useLibraryLinkSearch,
  useRemoveLibraryLink,
  useSetLibraryLink,
  useWriteLibraryLink,
} from '@/lib/queries'
import type {
  CatalogWorkKind,
  LibraryLinkDialog,
  LibraryLinkItem,
  LibraryLinkLookupQuery,
  LibraryLinkSearchQuery,
  LibraryWork,
  LibraryWorkLink,
  LinkCandidate,
} from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the "nothing here" lines
import '@/styles/settings-dialog.css' // .settings-dialog-body, .settings-check — the body's grid, the items' boxes
import '@/styles/library.css' // .library-source-status — a write's failure line under its item
import '@/styles/search.css' // .search-details-* — the two columns, their headings and facts; .search-badges
import '@/styles/recommendations.css' // .recommendation-attribution — TMDb's notice
import '@/styles/library-links.css' // .tmdb-link-* — the items, the pick, the paste and search forms, the key hints

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/** A bare TMDb number: it needs a film or a show to say which of TMDb's two numberings it is in (ADR 0017). */
const BARE_NUMBER = /^\s*\d+\s*$/

/** What the Review tab's keys do to the editor on screen (R24). */
export interface TmdbLinkEditorHandle {
  /** Picks the suggestion at `index` (from 0), as a click on it does; false when there is none. */
  pickSuggestion: (index: number) => boolean
  /** Puts the focus in the free search's title field. */
  focusSearch: () => void
  /** Saves the pick, as Save does; nothing while Save is disabled. */
  save: () => void
}

/** What the surface's Save button needs from the editor. */
export interface TmdbLinkSave {
  /** The pick form's id: Save sits outside it and names it in `form=`. */
  formId: string
  /** Why Save is disabled; undefined while it can save. */
  reason: string | undefined
  saving: boolean
}

interface TmdbLinkEditorProps {
  /** The work's dialog as the API answered it. */
  dialog: LibraryLinkDialog
  /**
   * The pick form's id, one per page: Save, and the radios of a lookup's or a search's answer, sit
   * outside the form and name it.
   */
  formId: string
  /** The body's classes: the dialog's one scroller, or the Review tab's card. */
  bodyClassName: string
  /** A line over both columns: the dialog's failed refresh. */
  notice?: ReactNode
  /** Shows the Review tab's keys: 1–5 on the suggestions, / on the search (R24). */
  shortcuts?: boolean
  /** A save went through; `notWritten` when Jellyfin didn't take a ticked item's write. */
  onSaved: (dialog: LibraryLinkDialog, notWritten: boolean) => void
  /** The bar under the body — the surface's Save and its own buttons — given what Save needs. */
  footer: (save: TmdbLinkSave) => ReactNode
  /** For the Review tab's keys. */
  ref?: Ref<TmdbLinkEditorHandle>
}

/**
 * MAM-34 — a library work's TMDb link, edited: the content of the work's TMDb dialog, and of the TMDb
 * page's Review tab, which shows it inline one work at a time (R24). Moved out of TmdbLinkDialog when
 * the Review tab took it too; the surface around it brings the title, Save's place and its own buttons.
 *
 * On the left, the work as it stands: what it is linked to (with Remove link), and each library item it
 * holds — the ids Jellyfin reports for it, where its link stands against Jellyfin, when Jellyfin was
 * asked to look up its missing metadata (or why it couldn't be), and Retry or Write again for that
 * item. On the right, the pick: the prepared suggestions, best first, each with its poster, facts and
 * evidence; the items to link when the work holds several; a season and an episode when a show is
 * picked for a film or an episode of no series. Under the pick, a pasted TMDb or IMDb link and a free
 * TMDb search, each its own form with its own button — Enter in them looks up or searches, never saves
 * — whose answers are picked the same way.
 *
 * Save writes the ids into Jellyfin at once — a film's or a series', or for an episode Jellyfin lists
 * as a film (with its season and episode), the episode's own IMDb id —, and Jellyfin is then asked to
 * look up the item's missing metadata, as its own "Search for missing metadata" does: only what the
 * item lacks is filled, and its title stays (Vasyl's decision of 2026-10-06). What stays in the app —
 * an episode of no series, an episode's link without a season and an episode or whose episode TMDb
 * knows no IMDb id of — says why under its item (`cannotWriteReason`), and the editor shows that
 * sentence rather than deciding by the item's kind or the link's numbers. A link kept in the app before
 * that decision, which the server can write now (`canWrite`, no reason), says so and offers Write to
 * Jellyfin; one written before Jellyfin was asked for the lookup offers to ask. A save that would
 * replace an id Jellyfin holds, or the work's link, asks first (R24); a series is saved with no
 * question of its own, as a film is (ADR 0026 says what Jellyfin does to its seasons and episodes, and
 * why that is accepted). A save whose write Jellyfin didn't confirm stays on the items' states, saying
 * so above the buttons while one of those items is still not written. Every failure is a line where it
 * happened; no toasts.
 */
export function TmdbLinkEditor({ dialog, formId, bodyClassName, notice, shortcuts = false, onSaved, footer, ref }: TmdbLinkEditorProps) {
  const { t } = useTranslation(['libraryLinks', 'library', 'recommendations', 'common'])
  const confirm = useConfirm()
  const work = dialog.work
  const setLink = useSetLibraryLink(work.id)
  const removeLink = useRemoveLibraryLink(work.id)
  const writeLink = useWriteLibraryLink(work.id)
  const currentId = useId()
  const pickId = useId()
  const searchTitle = useRef<HTMLInputElement>(null)

  const [pick, setPick] = useState<LinkCandidate | null>(null)
  const [season, setSeason] = useState('')
  const [episode, setEpisode] = useState('')
  // The items not ticked, so every item — one a refetch adds too — starts ticked.
  const [unticked, setUnticked] = useState<string[]>([])
  const [saveError, setSaveError] = useState<string | null>(null)
  // The items the last save left not written into Jellyfin. The warning above the buttons stands
  // while one of them still is, so a Retry that lands takes it away, and one refused again keeps it.
  const [notWrittenIds, setNotWrittenIds] = useState<string[]>([])
  const [removeError, setRemoveError] = useState<string | null>(null)
  const [writeErrors, setWriteErrors] = useState<Record<string, string>>({})
  // A double click on Save, or Enter pressed twice, sends one link.
  const inFlight = useRef(false)

  const items = dialog.items
  const tickedItems = items.filter((item) => !unticked.includes(item.sourceItemId))
  const notWritten = items.some((item) => notWrittenIds.includes(item.sourceItemId) && item.writeState === 'NotWritten')
  // A show picked for a film or an episode of no series links it as the show's episode (ADR 0026).
  const episodeLink = pick !== null && pick.catalogKind === 'Series' && work.kind !== 'Series'

  const saveReason = !dialog.catalogConnected
    ? t('dialog.noCatalog')
    : pick === null
      ? t('dialog.pickFirst')
      : tickedItems.length === 0 ? t('dialog.noItemsTicked') : undefined

  const choose = (candidate: LinkCandidate) => {
    setPick(candidate)
    setSaveError(null)
    // The numbers are the pick's own — a suggestion's guess, those a pasted episode link names, none
    // for a bare number —, never the pick's before it nor the work's link's: a pasted link to another
    // episode of the linked show means that episode.
    setSeason(candidate.seasonNumber === null ? '' : String(candidate.seasonNumber))
    setEpisode(candidate.episodeNumber === null ? '' : String(candidate.episodeNumber))
  }

  const savePick = async () => {
    if (pick === null || saveReason !== undefined || inFlight.current) return
    inFlight.current = true
    try {
      const replaced = replacedId(pick, tickedItems, episodeLink)
      if (replaced !== null) {
        const message = !replaced.inLibrary
          ? t('dialog.replaceLink', { old: replaced.value, new: pick.catalogId })
          : replaced.keptReason === null
            ? t('dialog.replaceLibrary', { old: replaced.value, new: pick.catalogId })
            // A link the server won't write (any while its library is switched off) doesn't replace
            // the id in Jellyfin: the question gives the server's reason instead.
            : t('dialog.replaceKept', { old: replaced.value, new: pick.catalogId, reason: replaced.keptReason })
        if (!(await confirm({ title: t('dialog.replaceTitle'), message, confirmLabel: t('dialog.replace') }))) return
      }
      setSaveError(null)
      setNotWrittenIds([])
      const ticked = tickedItems.map((item) => item.sourceItemId)
      const next = await setLink.mutateAsync({
        catalogKind: pick.catalogKind,
        catalogId: pick.catalogId,
        seasonNumber: episodeLink && season.trim() !== '' ? season.trim() : null,
        episodeNumber: episodeLink && episode.trim() !== '' ? episode.trim() : null,
        // Every item is the API's default; only a narrower choice is named.
        sourceItemIds: ticked.length === items.length ? null : ticked,
      })
      // A write Jellyfin didn't take is a state with its sentence, not a failure of the save: the
      // editor stays on the items, which say why and offer Retry.
      const failed = next.items
        .filter((item) => ticked.includes(item.sourceItemId) && item.writeState === 'NotWritten')
        .map((item) => item.sourceItemId)
      if (failed.length > 0) {
        setPick(null)
        setNotWrittenIds(failed)
      }
      onSaved(next, failed.length > 0)
    } catch (e: unknown) {
      setSaveError(getApiErrorMessage(e, t('dialog.saveFailed')))
    } finally {
      inFlight.current = false
    }
  }

  const save = (event: FormEvent) => {
    event.preventDefault()
    void savePick()
  }

  useImperativeHandle(ref, () => ({
    pickSuggestion: (index: number) => {
      const candidate = dialog.suggestions[index]
      if (candidate === undefined) return false
      choose(candidate)
      return true
    },
    focusSearch: () => searchTitle.current?.focus(),
    save: () => void savePick(),
  }))

  const remove = async () => {
    setRemoveError(null)
    const confirmed = await confirm({
      title: t('dialog.removeTitle'),
      message: t('dialog.removeMessage'),
      confirmLabel: t('dialog.remove'),
      destructive: true,
    })
    if (!confirmed) return
    try {
      await removeLink.mutateAsync()
      setNotWrittenIds([])
    } catch (e: unknown) {
      setRemoveError(getApiErrorMessage(e, t('dialog.removeFailed')))
    }
  }

  /** Retry, Write again, Write to Jellyfin or the lookup's ask for one item's link. */
  const write = async (linkId: string) => {
    setWriteErrors((errors) => {
      const next = { ...errors }
      delete next[linkId]
      return next
    })
    try {
      await writeLink.mutateAsync(linkId)
    } catch (e: unknown) {
      setWriteErrors((errors) => ({ ...errors, [linkId]: getApiErrorMessage(e, t('dialog.writeFailed')) }))
    }
  }

  const toggleItem = (sourceItemId: string, ticked: boolean) =>
    setUnticked((ids) => (ticked ? ids.filter((id) => id !== sourceItemId) : [...ids, sourceItemId]))

  return (
    <>
      <div className={bodyClassName}>
        {notice && <div className="tmdb-link-wide">{notice}</div>}
        {!dialog.catalogConnected && (
          <div className="tmdb-link-wide">
            <Banner tone="warning">{t('dialog.noCatalog')}</Banner>
          </div>
        )}

        <section className="search-details-column" aria-labelledby={currentId}>
          <h3 id={currentId} className="search-details-heading">{t('dialog.current')}</h3>
          <CurrentLink
            link={work.link}
            removing={removeLink.isPending}
            error={removeError}
            onRemove={() => void remove()}
          />

          <h3 className="search-details-heading">{t('dialog.items')}</h3>
          <ul className="tmdb-link-items">
            {items.map((item) => (
              <ItemRow
                key={item.sourceItemId}
                item={item}
                writing={writeLink.isPending && writeLink.variables === item.linkId}
                error={item.linkId === null ? undefined : writeErrors[item.linkId]}
                onWrite={(linkId) => void write(linkId)}
              />
            ))}
          </ul>
        </section>

        <section className="search-details-column" aria-labelledby={pickId}>
          <h3 id={pickId} className="search-details-heading">{t('dialog.suggestions')}</h3>
          {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
          <form id={formId} className="tmdb-link-form" noValidate onSubmit={save}>
            <SuggestionList dialog={dialog} pick={pick} shortcuts={shortcuts} onPick={choose} />

            {pick !== null && !dialog.suggestions.some((candidate) => samePick(candidate, pick)) && (
              <p className="tmdb-link-picked">{t('dialog.picked', { title: pick.title, id: pick.catalogId })}</p>
            )}

            {items.length > 1 && (
              <fieldset className="tmdb-link-fieldset">
                <legend className="ui-label">{t('dialog.itemsLegend')}</legend>
                {items.map((item) => (
                  <label key={item.sourceItemId} className="settings-check">
                    <input
                      type="checkbox"
                      checked={!unticked.includes(item.sourceItemId)}
                      onChange={(event) => toggleItem(item.sourceItemId, event.target.checked)}
                    />
                    <span>{itemName(item)}</span>
                  </label>
                ))}
              </fieldset>
            )}

            {episodeLink && (
              <div className="tmdb-link-numbers">
                <Field label={t('dialog.season')}>
                  {(control) => (
                    <Input {...control} inputMode="numeric" value={season} onChange={(event) => setSeason(event.target.value)} />
                  )}
                </Field>
                <Field label={t('dialog.episodeNumber')}>
                  {(control) => (
                    <Input {...control} inputMode="numeric" value={episode} onChange={(event) => setEpisode(event.target.value)} />
                  )}
                </Field>
                {/* Only a film item takes the episode's IMDb id: an episode of no series keeps its link in
                    the app whatever its numbers, as its item's sentence says. */}
                <p className="ui-hint tmdb-link-wide">{work.kind === 'Movie' ? t('dialog.episodeHint') : t('dialog.episodeHintKept')}</p>
              </div>
            )}
          </form>

          <PasteForm workId={work.id} workKind={work.kind} formId={formId} pick={pick} onPick={choose} />
          <SearchForm
            workId={work.id}
            startTitle={dialog.searchTitle}
            startYear={dialog.searchYear}
            formId={formId}
            titleRef={searchTitle}
            shortcuts={shortcuts}
            pick={pick}
            onPick={choose}
          />

          <p className="recommendation-attribution">{t('recommendations:attribution')}</p>
        </section>
      </div>

      {/* Outside the body, above the buttons. */}
      {notWritten && <Banner tone="warning">{t('dialog.savedNotWritten')}</Banner>}
      {saveError && <Banner>{saveError}</Banner>}
      {footer({ formId, reason: saveReason, saving: setLink.isPending })}
    </>
  )
}

// ── the work as it stands ──

/** What the work is linked to, with Remove link; a sentence when it isn't linked. */
function CurrentLink({ link, removing, error, onRemove }: {
  link: LibraryWorkLink | null
  removing: boolean
  error: string | null
  onRemove: () => void
}) {
  const { t } = useTranslation('libraryLinks')
  if (link === null) return <p className="settings-muted">{t('dialog.notLinked')}</p>

  const removeLabel = removing ? t('dialog.removing') : t('dialog.remove')
  return (
    <>
      <dl className="search-details-facts">
        <dt>{t('dialog.currentLink')}</dt>
        <dd>
          {link.catalogTitle === null
            ? t('dialog.linkedId', { kind: t(`candidate.kind.${link.catalogKind}`), id: link.catalogId })
            : t('dialog.linkedTitle', { title: link.catalogTitle, kind: t(`candidate.kind.${link.catalogKind}`), id: link.catalogId })}
        </dd>
        {link.target === 'ShowEpisode' && link.seasonNumber !== null && (
          <>
            <dt>{t('dialog.episode')}</dt>
            <dd>{episodeNumbers(link.seasonNumber, link.episodeNumber)}</dd>
          </>
        )}
        {link.imdbId !== null && (
          <>
            <dt>{t('dialog.imdb')}</dt>
            <dd>{link.imdbId}</dd>
          </>
        )}
        <dt>{t('dialog.set')}</dt>
        <dd>
          {link.setByUserName === null
            ? t('dialog.setOn', { time: formatDateTime(link.setUtc, INSTANT_FORMAT) })
            : t('dialog.setBy', { user: link.setByUserName, time: formatDateTime(link.setUtc, INSTANT_FORMAT) })}
        </dd>
      </dl>
      {/* A destructive verb stays out of the Save row: an outline danger button by the link it removes. */}
      <div className="tmdb-link-actions">
        <Button type="button" variant="outline" size="sm" className="ui-button-danger" title={t('dialog.remove')} disabled={removing} onClick={onRemove}>
          <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="label-text">{removeLabel}</span>
        </Button>
      </div>
      {error && <FailureLine message={error} />}
    </>
  )
}

/**
 * One library item: its name, the ids Jellyfin reports for it, and its link's state against Jellyfin
 * with Retry (not written) or Write again (Jellyfin lost it), or why it isn't written, or — kept in the
 * app before the app wrote such links, and writable now — Write to Jellyfin; an episode link's own IMDb
 * id; and when Jellyfin was asked to look up the item's missing metadata, or why it didn't take that,
 * with Retry, which writes the ids again (nothing changes where they are in place) and asks again — or,
 * never asked, a button that asks. No click asks first: a series' is sent like a film's.
 */
function ItemRow({ item, writing, error, onWrite }: {
  item: LibraryLinkItem
  writing: boolean
  error: string | undefined
  onWrite: (linkId: string) => void
}) {
  const { t } = useTranslation(['libraryLinks', 'library'])
  const state = item.writeState
  const idsIn = state === 'Written' || state === 'InLibrary'
  // A failed lookup leaves the ids in (the state stays), so only its line says to retry.
  const lookupFailed = item.lookupError !== null && idsIn
  // Why the link stays in the app, as the server sends it: the item's sentence or its link's (no season
  // and episode, or no IMDb id of the episode on TMDb) — shown whatever `canWrite` says, as an episode
  // link's reason sits on a film item, which can be written. The link's own error stands in only where
  // the server can't write the item: a link kept in the app that the server can write — one kept under
  // the first rules, a series' or a numbered episode's, whatever error it still holds — is no longer
  // kept for that error, and offers Write to Jellyfin.
  const keptReason = item.cannotWriteReason ?? (state === 'KeptInApp' && !item.canWrite ? item.writeError : null)
  // Kept in the app under the first rules and nothing keeps it there now: the server writes it on this
  // click, or on Update Jellyfin's (Vasyl's decision of 2026-10-06). Without this, such an item showed
  // the badge alone, with no way on.
  const writableNow = state === 'KeptInApp' && item.canWrite && keptReason === null
  // Written before the app asked Jellyfin for the lookup (or with the request never recorded): the
  // click sends no change where the ids are in place, and asks.
  const neverLookedUp = idsIn && item.canWrite && item.lookupRequestedUtc === null && item.lookupError === null
  const action = state === 'NotWritten' || lookupFailed
    ? t('dialog.retry')
    : state === 'Lost'
      ? t('dialog.writeAgain')
      : writableNow ? t('dialog.writeNow') : neverLookedUp ? t('dialog.askLookup') : null
  const linkId = item.linkId

  return (
    <li className="tmdb-link-item">
      <p className="tmdb-link-item-name">{itemName(item)}</p>
      {item.libraryIdentifiers.length === 0 ? (
        <p className="settings-muted">{t('dialog.noItemIds')}</p>
      ) : (
        <ul className="search-badges" aria-label={t('dialog.itemIds')}>
          {item.libraryIdentifiers.map((identifier) => (
            <li key={`${identifier.scheme}:${identifier.value}`}>
              <span className="ui-badge ui-badge-outline">
                {t('library:work.identifier', { scheme: t(`library:schemes.${identifier.scheme}`), value: identifier.value })}
              </span>
            </li>
          ))}
        </ul>
      )}
      {state === null || linkId === null ? (
        <p className="settings-muted">{t('dialog.notLinkedItem')}</p>
      ) : (
        <div className="tmdb-link-state">
          <LinkStateBadge state={state} />
          {item.conflictingValue !== null && item.catalogId !== null && (
            <span className="ui-badge library-badge-warning" title={t('card.conflictHint')}>
              <TriangleAlert className="h-3 w-3" aria-hidden="true" />
              {t('card.conflict', { library: item.conflictingValue, link: item.catalogId })}
            </span>
          )}
          {action !== null && (
            <div className="tmdb-link-actions">
              <Button type="button" variant="outline" size="sm" title={action} disabled={writing} onClick={() => onWrite(linkId)}>
                {writableNow && !writing
                  ? <Upload className="h-3.5 w-3.5" aria-hidden="true" />
                  : <RotateCw className={writing ? 'h-3.5 w-3.5 animate-spin' : 'h-3.5 w-3.5'} aria-hidden="true" />}
                <span className="label-text">{writing ? t('dialog.writing') : action}</span>
              </Button>
            </div>
          )}
        </div>
      )}
      {/* The state's own sentence, on screen as a phone shows no tooltip. */}
      {item.writeError !== null && (state === 'NotWritten' || state === 'Lost') && (
        <p className="library-source-status" data-status="Failed">
          <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{item.writeError}</span>
        </p>
      )}
      {item.target === 'ShowEpisode' && item.episodeImdbId !== null && (
        <p className="settings-muted">{t('dialog.episodeImdb', { id: item.episodeImdbId })}</p>
      )}
      {/* A request Jellyfin didn't take is recorded with its time too: one line, the failure's, so the
          item never says both that Jellyfin was asked and that it wasn't. */}
      {item.lookupError !== null ? (
        <p className="library-source-status" data-status="Failed">
          <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{t('dialog.lookupNotTaken', { error: item.lookupError })}</span>
        </p>
      ) : item.lookupRequestedUtc !== null ? (
        <p className="settings-muted">{t('dialog.lookupAsked', { time: formatDateTime(item.lookupRequestedUtc, INSTANT_FORMAT) })}</p>
      ) : neverLookedUp ? (
        <p className="settings-muted">{t('dialog.lookupNever')}</p>
      ) : null}
      {keptReason !== null && <p className="settings-muted">{keptReason}</p>}
      {writableNow && <p className="settings-muted">{t('dialog.writableNow')}</p>}
      {error && <FailureLine message={error} />}
    </li>
  )
}

// ── the pick ──

/** The prepared suggestions as radio cards, or why there are none. With `shortcuts`, each shows its key. */
function SuggestionList({ dialog, pick, shortcuts, onPick }: {
  dialog: LibraryLinkDialog
  pick: LinkCandidate | null
  shortcuts: boolean
  onPick: (candidate: LinkCandidate) => void
}) {
  const { t } = useTranslation('libraryLinks')
  const name = useId()
  const { search, suggestions } = dialog

  return (
    <>
      {search.noneFitsUtc !== null && (
        <p className="settings-muted">{t('dialog.noneFits', { time: formatDateTime(search.noneFitsUtc, INSTANT_FORMAT) })}</p>
      )}
      {suggestions.length > 0 ? (
        <div className="tmdb-link-candidates" role="radiogroup" aria-label={t('dialog.suggestions')}>
          {suggestions.map((candidate, index) => (
            <LinkCandidateOption
              key={`${candidate.catalogKind}:${candidate.catalogId}`}
              candidate={candidate}
              name={name}
              checked={pick !== null && samePick(candidate, pick)}
              // The keys 1 to 5 pick the first five; the API sends five at most.
              shortcut={shortcuts && index < 5 ? String(index + 1) : undefined}
              onPick={onPick}
            />
          ))}
        </div>
      ) : (
        <p className="settings-muted">
          {search.pending
            ? t('dialog.preparing')
            : !search.inPopulation
              ? t('dialog.notInPopulation')
              : search.searchedUtc === null ? t('dialog.noSuggestions') : t('dialog.nothingFound')}
        </p>
      )}
    </>
  )
}

/**
 * A pasted TMDb or IMDb link, IMDb id or TMDb number, looked up on TMDb. A bare number asks whether it
 * is a film's or a show's. Its own form: Enter looks it up and never saves.
 */
function PasteForm({ workId, workKind, formId, pick, onPick }: {
  workId: string
  workKind: LibraryWork['kind']
  formId: string
  pick: LinkCandidate | null
  onPick: (candidate: LinkCandidate) => void
}) {
  const { t } = useTranslation('libraryLinks')
  const headingId = useId()
  const name = useId()
  const [text, setText] = useState('')
  const [numberKind, setNumberKind] = useState<CatalogWorkKind>(workKind === 'Series' ? 'Series' : 'Movie')
  const [submitted, setSubmitted] = useState<LibraryLinkLookupQuery | null>(null)
  const lookup = useLibraryLinkLookup(workId, submitted)
  const bareNumber = BARE_NUMBER.test(text)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const next: LibraryLinkLookupQuery = { text: text.trim(), kind: bareNumber ? numberKind : undefined }
    // The same text again after a failure asks again; a new one is a new query.
    if (submitted !== null && submitted.text === next.text && submitted.kind === next.kind) void lookup.refetch()
    else setSubmitted(next)
  }

  const results = lookup.data?.items ?? []
  return (
    <form role="search" className="tmdb-link-form" aria-labelledby={headingId} noValidate onSubmit={submit}>
      <h3 id={headingId} className="search-details-heading">{t('dialog.paste')}</h3>
      <Field
        label={t('dialog.pasteLabel')}
        hint={t('dialog.pasteHint')}
        error={lookup.error ? getApiErrorMessage(lookup.error, t('dialog.lookupFailed')) : null}
      >
        {(control) => <Input {...control} value={text} onChange={(event) => setText(event.target.value)} />}
      </Field>
      <div className="tmdb-link-actions">
        {bareNumber && (
          <SegmentedControl
            label={t('dialog.numberKind')}
            options={[
              { value: 'Movie', label: t('dialog.numberKinds.Movie'), icon: Film },
              { value: 'Series', label: t('dialog.numberKinds.Series'), icon: Tv },
            ]}
            value={numberKind}
            onChange={setNumberKind}
            showLabels
          />
        )}
        <Button type="submit" variant="outline" size="sm" disabled={lookup.isFetching}>
          {lookup.isFetching ? t('dialog.lookingUp') : t('dialog.lookUp')}
        </Button>
      </div>
      {results.length > 0 && (
        <div className="tmdb-link-candidates" role="radiogroup" aria-label={t('dialog.lookupResults')}>
          {results.map((candidate) => (
            <LinkCandidateOption
              key={`${candidate.catalogKind}:${candidate.catalogId}`}
              candidate={candidate}
              name={name}
              form={formId}
              checked={pick !== null && samePick(candidate, pick)}
              onPick={onPick}
            />
          ))}
        </div>
      )}
    </form>
  )
}

/**
 * A free TMDb search, starting from the title and year the server gives for the work: its title cleaned
 * the way the server's own searches clean it, as TMDb finds nothing by "120. Landing Stripling" or a
 * series' folder name — the cleaning lives on the server alone. Its own form: Enter searches and never
 * saves. The first page of TMDb's answer, films and shows by the work's kind. With `shortcuts`, its
 * title field says that "/" reaches it.
 */
function SearchForm({ workId, startTitle, startYear, formId, titleRef, shortcuts, pick, onPick }: {
  workId: string
  startTitle: string | null
  startYear: number | null
  formId: string
  titleRef: Ref<HTMLInputElement>
  shortcuts: boolean
  pick: LinkCandidate | null
  onPick: (candidate: LinkCandidate) => void
}) {
  const { t } = useTranslation('libraryLinks')
  const headingId = useId()
  const name = useId()
  const [title, setTitle] = useState(startTitle ?? '')
  const [year, setYear] = useState(startYear === null ? '' : String(startYear))
  const [submitted, setSubmitted] = useState<LibraryLinkSearchQuery | null>(null)
  const search = useLibraryLinkSearch(workId, submitted)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const next: LibraryLinkSearchQuery = { title: title.trim(), year: year.trim() || undefined }
    if (submitted !== null && submitted.title === next.title && submitted.year === next.year) void search.refetch()
    else setSubmitted(next)
  }

  const results = search.data?.items
  return (
    <form role="search" className="tmdb-link-form" aria-labelledby={headingId} noValidate onSubmit={submit}>
      <h3 id={headingId} className="search-details-heading">
        {t('dialog.search')}
        {/* aria-hidden: aria-keyshortcuts on the field tells a screen reader about the key. */}
        {shortcuts && <>{' '}<kbd className="tmdb-link-key" aria-hidden="true">{t('review.keys.search')}</kbd></>}
      </h3>
      <div className="tmdb-link-search-fields">
        <Field label={t('dialog.searchTitle')}>
          {(control) => (
            <Input
              {...control}
              ref={titleRef}
              type="search"
              aria-keyshortcuts={shortcuts ? '/' : undefined}
              value={title}
              onChange={(event) => setTitle(event.target.value)}
            />
          )}
        </Field>
        <Field label={t('dialog.searchYear')}>
          {(control) => <Input {...control} inputMode="numeric" value={year} onChange={(event) => setYear(event.target.value)} />}
        </Field>
        <Button type="submit" variant="outline" size="sm" title={t('dialog.searchButton')} disabled={search.isFetching}>
          <Search className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="label-text">{search.isFetching ? t('dialog.searching') : t('dialog.searchButton')}</span>
        </Button>
      </div>
      {search.error && <FailureLine message={getApiErrorMessage(search.error, t('dialog.searchFailed'))} />}
      {results !== undefined && results.length === 0 && <p className="settings-muted">{t('dialog.nothingMatches')}</p>}
      {results !== undefined && results.length > 0 && (
        <div className="tmdb-link-candidates" role="radiogroup" aria-label={t('dialog.searchResults')}>
          {results.map((candidate) => (
            <LinkCandidateOption
              key={`${candidate.catalogKind}:${candidate.catalogId}`}
              candidate={candidate}
              name={name}
              form={formId}
              checked={pick !== null && samePick(candidate, pick)}
              onPick={onPick}
            />
          ))}
        </div>
      )}
    </form>
  )
}

/** A refusal under the control it answers (LibrarySyncPanel's start error line). */
function FailureLine({ message }: { message: string }) {
  return (
    <p role="alert" className="library-source-status" data-status="Failed">
      <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{message}</span>
    </p>
  )
}

// ── helpers ──

/** An item as "Home Jellyfin · 120. Landing Stripling", standing in for what the library didn't give. */
function itemName(item: LibraryLinkItem): string {
  return i18n.t('libraryLinks:dialog.item', {
    source: item.sourceName ?? i18n.t('libraryLinks:dialog.unknownSource'),
    title: item.itemTitle ?? i18n.t('libraryLinks:dialog.untitledItem'),
  })
}

/** "S1 E8", or "S1" for a season alone. */
function episodeNumbers(seasonNumber: number, episodeNumber: number | null): string {
  return episodeNumber === null
    ? i18n.t('libraryLinks:dialog.seasonOnly', { season: seasonNumber })
    : i18n.t('libraryLinks:dialog.seasonEpisode', { season: seasonNumber, episode: episodeNumber })
}

/**
 * Whether two answers are the same pick: the same film or show with the same season and episode. A
 * lookup of the show a pasted episode link named, by its bare number, answers the same show without
 * the episode: a radio of its own, not already checked, so picking it takes its numbers.
 */
function samePick(one: LinkCandidate, other: LinkCandidate): boolean {
  return one.catalogKind === other.catalogKind && one.catalogId === other.catalogId
    && one.seasonNumber === other.seasonNumber && one.episodeNumber === other.episodeNumber
}

/**
 * The id a save would replace, when any (R24): an id Jellyfin holds of the pick's scheme that isn't the
 * pick — only for a link to the work itself, whose id takes the place of Jellyfin's: in Jellyfin when
 * the server can write the item, else in the app (a library switched off, or one that can't set ids),
 * `keptReason` then being the server's sentence why — else another link of a ticked item. Null when
 * nothing would be replaced.
 */
function replacedId(pick: LinkCandidate, ticked: LibraryLinkItem[], episodeLink: boolean): { value: string; inLibrary: boolean; keptReason: string | null } | null {
  if (!episodeLink) {
    const scheme = pick.catalogKind === 'Series' ? 'TmdbTv' : 'Tmdb'
    for (const item of ticked) {
      const held = item.libraryIdentifiers.find((identifier) => identifier.scheme === scheme && identifier.value !== pick.catalogId)
      if (held) return { value: held.value, inLibrary: true, keptReason: item.canWrite ? null : item.cannotWriteReason }
    }
  }
  const linked = ticked.find((item) => item.linkId !== null && item.catalogId !== null
    && (item.catalogId !== pick.catalogId || item.catalogKind !== pick.catalogKind))
  return linked?.catalogId == null ? null : { value: linked.catalogId, inLibrary: false, keptReason: null }
}
