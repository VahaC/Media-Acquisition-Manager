import { useEffect, useId, useRef, useState } from 'react'
import { Globe } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { SegmentedControl } from '@/components/shared/SegmentedControl'
import { useAuthStore } from '@/lib/auth-store'
import { LANGUAGE_ENDONYMS } from '@/lib/i18n'
import { useSaveLanguage } from '@/lib/queries'
import type { UiLanguage } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-settings, .profile-inline, .profile-muted, .profile-language-status — the card's rows and its status

/** A pill of the choice: the browser's language, or one the account names. */
type LanguageChoice = 'browser' | UiLanguage

const choiceOf = (language: UiLanguage | null): LanguageChoice => language ?? 'browser'
const languageOf = (choice: LanguageChoice): UiLanguage | null => (choice === 'browser' ? null : choice)

/**
 * When a choice changes the UI's language, ProtectedRoute mounts the page afresh and the focused pill
 * goes with it: the focus would fall to the page's body, and the next arrow key would do nothing. The
 * choice is kept here, outside the card, so the fresh card puts the focus back on its pill, where a
 * screen reader reads it out in the new language. Set only while the card holds the focus.
 */
let refocusAfterRemount: LanguageChoice | null = null

/**
 * MAM-46 — the Profile page's Language card: the language the web app is shown in, saved with the
 * account, so it follows the user to their other browsers and devices; "As in the browser" saves none
 * and lets each browser decide, as it does before anyone signs in.
 *
 * A choice saves at once, like the theme above it (no Save button). Shown as picked while it saves;
 * once the server has it the app runs in it, and the signed-in pages mount afresh if the UI's language
 * changed, which is the confirmation, with the focus back on the chosen pill. A choice that leaves the
 * UI as it was (English on an English browser) says "Saved." instead. A failed save goes back to the
 * stored choice, with the reason above.
 *
 * The stored choice is the signed-in user's, which the Profile page brings up to date from
 * `GET /api/auth/me` when it loads (useProfile), so a language saved on another device shows here.
 */
export function LanguageCard() {
  const { t, i18n } = useTranslation('profile')
  const titleId = useId()
  const cardRef = useRef<HTMLElement>(null)
  const stored = useAuthStore((state) => state.user?.language ?? null)
  const save = useSaveLanguage()
  const [pending, setPending] = useState<LanguageChoice | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  // Only the latest choice settles the card: an earlier one that ends meanwhile says nothing.
  const latest = useRef(0)
  const shown = pending ?? choiceOf(stored)

  // Mount only: a card mounted afresh after its own choice changed the UI's language takes the focus
  // back. Focusing a radio doesn't check it, so a later choice still on its way is left alone.
  useEffect(() => {
    if (refocusAfterRemount === null) return
    cardRef.current?.querySelector<HTMLInputElement>(`input[type="radio"][value="${refocusAfterRemount}"]`)?.focus()
    refocusAfterRemount = null
  }, [])

  // A card the language change left mounted (one outside the signed-in pages) kept its focus: no fresh
  // card is coming to take it, and a later one must not. Under ProtectedRoute this card is replaced in
  // the same render instead, so this never runs there.
  const languageShown = useRef(i18n.language)
  useEffect(() => {
    if (languageShown.current === i18n.language) return
    languageShown.current = i18n.language
    refocusAfterRemount = null
  }, [i18n.language])

  const choose = async (choice: LanguageChoice) => {
    const attempt = ++latest.current
    const languageBefore = i18n.language
    if (cardRef.current?.contains(document.activeElement)) refocusAfterRemount = choice
    setPending(choice)
    setError(null)
    setSaved(false)
    // A save that left the UI's language as it was (or failed) keeps this card, and its focus: nothing to
    // put back. Not cleared otherwise: the fresh card mounts after this runs (React renders it later) and
    // needs the choice to focus.
    const keepFocusHere = () => {
      if (i18n.language === languageBefore && refocusAfterRemount === choice) refocusAfterRemount = null
    }
    try {
      await save.mutateAsync(languageOf(choice))
      keepFocusHere()
      if (attempt !== latest.current) return
      setPending(null)
      setSaved(true)
    } catch (e: unknown) {
      keepFocusHere()
      if (attempt !== latest.current) return
      setPending(null)
      setError(getApiErrorMessage(e, t('language.failed')))
    }
  }

  return (
    <section ref={cardRef} className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('language.title')}</h2>
        <p className="ui-card-description">{t('language.description')}</p>
      </div>
      <div className="ui-card-content profile-settings">
        {error && <Banner>{error}</Banner>}
        <div className="profile-inline">
          <span>{t('language.label')}</span>
          <SegmentedControl
            label={t('language.label')}
            options={[
              { value: 'browser', label: t('language.browser'), icon: Globe },
              // Endonyms: each language named in itself, the same whatever the UI's language.
              { value: 'en', label: LANGUAGE_ENDONYMS.en, lang: 'en' },
              { value: 'uk', label: LANGUAGE_ENDONYMS.uk, lang: 'uk' },
            ]}
            value={shown}
            onChange={(choice) => void choose(choice)}
            showLabels
          />
        </div>
        {/* Under the row rather than beside the pills: there it widened the row, which moved the pills
            (or wrapped them under the label) right after a tap. */}
        {saved && <p role="status" className="profile-muted profile-language-status">{t('language.saved')}</p>}
      </div>
    </section>
  )
}
