import { CircleCheck, CirclePause, CircleSlash, CircleX, Clock, Loader2, TriangleAlert, type LucideIcon } from 'lucide-react'
import type { DownloadJobStatus } from '@/lib/types'

// MAM-14 — how a job status looks wherever it is shown: its icon, and the status-line tone library.css
// colours. MAM-41 — moved out of JobStatus, so the Jobs row's Download cell (JobDownloadSummary) and the
// job's facts can't drift apart.

/** Each status's icon. Downloading spins while nothing blocks it: the work under way. */
export const STATUS_ICONS: Record<DownloadJobStatus, LucideIcon> = {
  Created: Clock,
  Submitted: Clock,
  Downloading: Loader2,
  Paused: CirclePause,
  Downloaded: CircleCheck,
  Failed: CircleX,
  NeedsLinking: TriangleAlert,
  Cancelled: CircleSlash,
}

/** The Library's status-line tone a job status takes: library.css colours these two. */
export function statusTone(status: DownloadJobStatus): 'Succeeded' | 'Failed' | undefined {
  if (status === 'Downloaded') return 'Succeeded'
  if (status === 'Failed') return 'Failed'
  return undefined
}
