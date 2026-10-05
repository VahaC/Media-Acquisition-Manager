import { useId, useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { Field } from '@/components/shared/Field'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatNumber } from '@/lib/format'
import { useRecommendationSettings, useSaveRecommendationSettings } from '@/lib/queries'
import { RECOMMENDATION_TABS, TAB_SETTINGS } from '@/lib/recommendations'
import type { RecommendationSettings } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-* — the form's column, the boxes' row, the note and the buttons
import '@/styles/settings-dialog.css' // .settings-check — a tab's box before its name

/**
 * MAM-33 — the Profile page's Recommendations card (Vasyl's decision 5): which of the four lists the
 * Recommendations page shows this user, and how many release-source searches the app may run for
 * their lists in 24 hours, with how many it ran. Its own surface with one Save, like the Password
 * card: the theme above it applies at once and saves nothing.
 */
export function RecommendationSettingsCard() {
  const { t } = useTranslation(['profile', 'common'])
  const titleId = useId()
  const settings = useRecommendationSettings()
  const { data, error, isLoading } = settings
  const load = useLoadRetry(settings)

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('recommendations.title')}</h2>
        <p className="ui-card-description">{t('recommendations.description')}</p>
      </div>
      <div className="ui-card-content profile-settings">
        {/* Outside the ternary below, so a failed refresh adds a line above the form instead of
            replacing it. */}
        {error && data && (
          <Banner action={<RetryButton onRetry={() => settings.refetch()} />}>
            {t('recommendations.refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}
        {load.error ? (
          <ErrorState title={t('recommendations.loadFailed')} onRetry={load.retry} retrying={load.retrying}>
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="profile-muted">{t('common:loading')}</p>
        ) : data ? (
          <SettingsForm settings={data} />
        ) : null}
      </div>
    </section>
  )
}

// ── form ──

/** The form as typed: the boxes, and the limit as text so a half-typed number stays as it is. */
interface Draft {
  movies: boolean
  series: boolean
  cartoons: boolean
  documentaries: boolean
  limit: string
}

const draftOf = (settings: RecommendationSettings): Draft => ({
  movies: settings.movies,
  series: settings.series,
  cartoons: settings.cartoons,
  documentaries: settings.documentaries,
  limit: String(settings.dailySearchLimit),
})

/**
 * The fields and Save. Until the person changes something, the fields show the settings as loaded, so
 * a refresh brings another device's change in; a save that goes through puts the answer in their
 * place. A limit that isn't a whole number is refused here with the server's own sentence (the server
 * couldn't read it to word one); the range is the server's to check, and its 400 goes under the field
 * — the boxes, always on or off, are nothing it could refuse. Anything else is a Banner above Save.
 */
function SettingsForm({ settings }: { settings: RecommendationSettings }) {
  const { t } = useTranslation(['profile', 'recommendations'])
  const tabsHintId = useId()
  const save = useSaveRecommendationSettings()
  const [draft, setDraft] = useState<Draft | null>(null)
  const [limitError, setLimitError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const shown = draft ?? draftOf(settings)

  const edit = (changes: Partial<Draft>) => {
    setDraft({ ...shown, ...changes })
    setSaved(false)
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    setSaved(false)
    const limit = shown.limit.trim()
    // Nine digits at most: a longer number is past what the server's whole number holds.
    if (!/^\d{1,9}$/.test(limit)) {
      setLimitError(t('recommendations.limitInvalid'))
      return
    }
    setLimitError(null)
    try {
      await save.mutateAsync({
        movies: shown.movies,
        series: shown.series,
        cartoons: shown.cartoons,
        documentaries: shown.documentaries,
        dailySearchLimit: Number(limit),
      })
      setDraft(null)
      setSaved(true)
    } catch (e: unknown) {
      const sentence = getApiErrorMessage(e, t('recommendations.failed'))
      if (isAxiosError(e) && e.response?.status === 400) setLimitError(sentence)
      else setError(sentence)
    }
  }

  return (
    // noValidate: the rules live on the server, and its sentence is the one to show.
    <form className="profile-settings-form" noValidate onSubmit={(event) => void submit(event)}>
      <fieldset className="profile-settings-group" aria-describedby={tabsHintId}>
        <legend className="ui-label">{t('recommendations.tabs')}</legend>
        <div className="profile-checks">
          {RECOMMENDATION_TABS.map((tab) => (
            <label key={tab} className="settings-check">
              <input
                type="checkbox"
                checked={shown[TAB_SETTINGS[tab]]}
                onChange={(event) => edit({ [TAB_SETTINGS[tab]]: event.target.checked })}
              />
              {t(`recommendations:tabs.${tab}`)}
            </label>
          ))}
        </div>
        <p id={tabsHintId} className="ui-hint">{t('recommendations.tabsHint')}</p>
      </fieldset>
      <Field label={t('recommendations.limit')} hint={t('recommendations.limitHint')} error={limitError}>
        {(control) => (
          <Input
            {...control}
            className="profile-limit-input"
            value={shown.limit}
            inputMode="numeric"
            autoComplete="off"
            onChange={(event) => {
              edit({ limit: event.target.value })
              setLimitError(null)
            }}
          />
        )}
      </Field>
      <p className="profile-note">
        {t('recommendations.used', { count: settings.searchesLast24Hours, value: formatNumber(settings.searchesLast24Hours) })}
      </p>
      {error && <Banner>{error}</Banner>}
      <div className="profile-actions">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? t('recommendations.saving') : t('recommendations.save')}
        </Button>
        {saved && <span role="status" className="profile-muted">{t('recommendations.saved')}</span>}
      </div>
    </form>
  )
}
