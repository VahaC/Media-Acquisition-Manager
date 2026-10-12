import { useTranslation } from 'react-i18next'
import { formatProgress } from '@/lib/format'
import '@/styles/settings-list.css' // .settings-muted — the line that says the progress isn't known
import '@/styles/jobs.css' // .jobs-progress* — the bar and its percentage

/**
 * MAM-14 — how far a download has got, as the client last reported it: a bar and its percentage.
 * `progress` runs from 0 to 1; null until the client has reported any, which is said in words rather
 * than drawn as an empty bar (0% would claim the client said nothing is downloaded).
 *
 * The percentage is cut down, never rounded up, to a tenth: 99.96% must not read 100% while the job
 * still waits for its last piece. A native <progress> carries the value and its name for a screen
 * reader; jobs.css only sets its look.
 */
export function JobProgress({ progress }: { progress: number | null }) {
  const { t } = useTranslation('jobs')

  if (progress === null) return <span className="settings-muted">{t('progress.unknown')}</span>

  const value = Math.min(1, Math.max(0, progress))

  return (
    <span className="jobs-progress">
      <progress className="jobs-progress-bar" value={value} max={1} aria-label={t('progress.label')} />
      <span className="jobs-progress-value">{formatProgress(value)}</span>
    </span>
  )
}
