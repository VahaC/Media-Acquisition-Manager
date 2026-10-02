import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Search as SearchIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { createSearchParams, useLocation, useNavigate, useNavigationType, useSearchParams } from 'react-router-dom'
import { Input } from '@/components/ui/input'
import { useCachedSearch } from '@/lib/queries'
import { isSearchId } from '@/lib/search-query'
import '@/styles/app-layout.css' // .app-search* — the field, its icon and the "/" key, set in the header

/** The page the field searches on. */
const SEARCH_PATH = '/search'

/**
 * MAM-16 — the search field in the header, on every page once someone is signed in. Enter opens the
 * Search page at `/search?q=<the text>`, which makes the search (ADR 0015 says what part of the line
 * becomes the year); an empty Enter opens the page bare, where it says how to search. "/" anywhere
 * else on the page jumps to it.
 *
 * On the Search page the field says what the page searched for: the text of the search on screen, or
 * the `?q=` it is still making. Elsewhere it starts empty. What the person types stays until they
 * search, go to another page, or go Back or Forward: a search that answers while they type, and the
 * page moving to it, never overwrites it.
 */
export function HeaderSearch() {
  const { t } = useTranslation(['common', 'search'])
  const navigate = useNavigate()
  const location = useLocation()
  const navigationType = useNavigationType()
  const [searchParams] = useSearchParams()
  const inputRef = useRef<HTMLInputElement>(null)
  const onSearchPage = location.pathname === SEARCH_PATH
  const addressId = onSearchPage ? (searchParams.get('id') ?? '') : ''
  // The same guard as the page's, so a forged id reads nothing from the cache either.
  const { data } = useCachedSearch(isSearchId(addressId) ? addressId : '')
  // What the Search page searched for: the search on screen, or the `?q=` it is still making (the
  // page reads `?q=` only without an id).
  const searched = data?.text ?? (onSearchPage && addressId === '' ? (searchParams.get('q') ?? '').trim() : '')
  // What the person typed; null while they haven't, so the field follows the search on screen as it
  // loads. The header never unmounts, so a move to another page, Back and Forward drop it, to show
  // what is there. The Search page moving itself — from `?q=` to the search it made, or to the one
  // Apply made — keeps it: the person may be typing the next title while the sources answer.
  const [typed, setTyped] = useState<string | null>(null)
  const [seen, setSeen] = useState({ key: location.key, pathname: location.pathname })
  if (location.key !== seen.key) {
    setSeen({ key: location.key, pathname: location.pathname })
    if (location.pathname !== seen.pathname || navigationType === 'POP') setTyped(null)
  }
  const value = typed ?? searched

  useEffect(() => {
    const focusOnSlash = (event: KeyboardEvent) => {
      if (event.key !== '/' || event.defaultPrevented || event.isComposing) return
      // Ctrl+/, Cmd+/ and Alt+/ are other programs' shortcuts.
      if (event.ctrlKey || event.metaKey || event.altKey) return
      // A slash typed into a field is text. And an open dialog keeps the focus inside itself, so
      // taking the slash there would swallow it with nothing happening; Radix marks its dialogs
      // `data-state="open"` and sets no aria-modal.
      if (isTypedInto(event.target) || document.querySelector('[role="dialog"][data-state="open"]')) return
      event.preventDefault()
      inputRef.current?.focus()
    }
    document.addEventListener('keydown', focusOnSlash)
    return () => document.removeEventListener('keydown', focusOnSlash)
  }, [])

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const text = value.trim()
    // URLSearchParams encodes the text, so a `&` or a `#` in a title stays part of it.
    navigate(text === '' ? SEARCH_PATH : { pathname: SEARCH_PATH, search: `?${createSearchParams({ q: text })}` })
    // The field now follows the search it asked for.
    setTyped(null)
    // The results are what the person reads next; on a phone this also puts the keyboard away.
    inputRef.current?.blur()
  }

  return (
    <form role="search" className="app-search" onSubmit={submit}>
      <SearchIcon className="app-search-icon" data-current={onSearchPage || undefined} aria-hidden="true" />
      <Input
        ref={inputRef}
        type="search"
        className="app-search-input"
        value={value}
        aria-label={t('search:form.label')}
        aria-keyshortcuts="/"
        placeholder={t('headerSearch.placeholder')}
        autoComplete="off"
        onChange={(event) => setTyped(event.target.value)}
      />
      {value === '' && (
        // aria-hidden: aria-keyshortcuts on the field already tells a screen reader about the key.
        // preventDefault keeps the mousedown from taking the focus to the page first.
        <kbd
          className="app-search-key"
          title={t('headerSearch.hint')}
          aria-hidden="true"
          onMouseDown={(event) => {
            event.preventDefault()
            inputRef.current?.focus()
          }}
        >
          {t('headerSearch.key')}
        </kbd>
      )}
    </form>
  )
}

/** Whether a keystroke at `target` types into it: a form control, or editable content. */
function isTypedInto(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement
    || target instanceof HTMLTextAreaElement
    || target instanceof HTMLSelectElement
    || (target instanceof HTMLElement && target.isContentEditable)
}
