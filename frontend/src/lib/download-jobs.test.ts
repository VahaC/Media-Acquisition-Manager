import {
  canCancelJob,
  DOWNLOAD_JOB_STATUSES,
  isJobFollowed,
  isJobWorkedOn,
  isReplacementBeingChecked,
  isReplacementWorkedOn,
} from '@/lib/download-jobs'
import type { ReplacementJobState } from '@/lib/types'
import { jobOf } from '@/test/downloads'
import { replacementOf } from '@/test/replacements'

/** 5 minutes, the server's InterruptedSendAge, in ms. */
const INTERRUPTED_SEND_AGE_MS = 5 * 60_000

/** Every state a replacement can be in, spelled out in the order of its chain. */
const REPLACEMENT_JOB_STATES: readonly ReplacementJobState[] = [
  'Scheduled',
  'Submitted',
  'Downloading',
  'Paused',
  'Downloaded',
  'Inspecting',
  'Verified',
  'Quarantining',
  'Quarantined',
  'Restoring',
  'Restored',
  'Deleting',
  'Completed',
  'NeedsReview',
  'Failed',
  'Cancelled',
]

describe('download job statuses', () => {
  it('lists every status once, in the order the API lists them', () => {
    // Arrange
    const expected = ['Created', 'Submitted', 'Downloading', 'Paused', 'Downloaded', 'Failed', 'NeedsLinking', 'Cancelled']

    // Act
    const statuses = [...DOWNLOAD_JOB_STATUSES]

    // Assert
    expect(statuses).toEqual(expected)
  })

  it('counts Created, Submitted, Downloading and Paused as followed, and every other status as ended', () => {
    // Arrange
    const statuses = DOWNLOAD_JOB_STATUSES

    // Act
    const followed = statuses.filter(isJobFollowed)

    // Assert
    expect(followed).toEqual(['Created', 'Submitted', 'Downloading', 'Paused'])
  })

  it('counts every followed job as worked on, whether or not it carries a replacement', () => {
    // Arrange
    const jobs = DOWNLOAD_JOB_STATUSES.flatMap((status) => [
      jobOf({ status }),
      jobOf({ status, replacement: replacementOf({ state: 'Scheduled' }) }),
    ])

    // Act
    const workedOn = jobs.filter(isJobWorkedOn).map((job) => job.status)

    // Assert
    expect(workedOn).toEqual(['Created', 'Created', 'Submitted', 'Submitted', 'Downloading', 'Downloading', 'Paused', 'Paused'])
  })

  // MAM-25 — the download stops moving once it is Downloaded, while the check of its replacement's new
  // files goes on by itself until it ends Verified, NeedsReview or Failed. MAM-26 — and the mover takes
  // a Verified one up by itself, then the renames end it Quarantined, Restored, Completed or NeedsReview.
  it('counts a downloaded job as worked on while its replacement is checked, verified or being renamed, and in no other replacement state', () => {
    // Arrange
    const jobs = REPLACEMENT_JOB_STATES.map((state) => jobOf({ status: 'Downloaded', progress: 1, replacement: replacementOf({ state }) }))

    // Act
    const workedOn = jobs.filter(isJobWorkedOn).map((job) => job.replacement?.state)

    // Assert
    expect(workedOn).toEqual(['Downloaded', 'Inspecting', 'Verified', 'Quarantining', 'Restoring', 'Deleting'])
  })

  // MAM-26 — while replacement is switched off the mover leaves a Verified one untouched (ADR 0033), so
  // asking again would bring the same answer; the check of the new files and a rename under way go on.
  it('counts a replacement as worked on while replacement is switched off only while it is checked or being renamed', () => {
    // Arrange
    const replacements = REPLACEMENT_JOB_STATES.map((state) => replacementOf({ state, switchedOff: true }))

    // Act
    const workedOn = replacements.filter(isReplacementWorkedOn).map((replacement) => replacement.state)

    // Assert
    expect(workedOn).toEqual(['Downloaded', 'Inspecting', 'Quarantining', 'Restoring', 'Deleting'])
  })

  it('counts a replacement as being checked only while it is Downloaded or Inspecting', () => {
    // Arrange
    const states = REPLACEMENT_JOB_STATES

    // Act
    const checked = states.filter(isReplacementBeingChecked)

    // Assert
    expect(checked).toEqual(['Downloaded', 'Inspecting'])
  })

  it('counts no ended job without a replacement as worked on', () => {
    // Arrange
    const jobs = (['Downloaded', 'Failed', 'NeedsLinking', 'Cancelled'] as const).map((status) => jobOf({ status }))

    // Act
    const workedOn = jobs.filter(isJobWorkedOn)

    // Assert
    expect(workedOn).toEqual([])
  })

  it('offers Cancel for a followed job and for one that needs linking, never for one that ended', () => {
    // Arrange
    const jobs = DOWNLOAD_JOB_STATUSES.map((status) => jobOf({ status }))
    const now = Date.parse(jobOf({}).createdUtc) + INTERRUPTED_SEND_AGE_MS

    // Act
    const cancellable = jobs.filter((job) => canCancelJob(job, now)).map((job) => job.status)

    // Assert
    expect(cancellable).toEqual(['Created', 'Submitted', 'Downloading', 'Paused', 'NeedsLinking'])
  })

  it.each<[string, number, boolean]>([
    ['just sent', 0, false],
    ['a second short of 5 minutes old', INTERRUPTED_SEND_AGE_MS - 1_000, false],
    ['5 minutes old', INTERRUPTED_SEND_AGE_MS, true],
    ['an hour old', 60 * 60_000, true],
  ])('offers Cancel on a Created job %s: %s', (_, age, offered) => {
    // Arrange
    const job = jobOf({ status: 'Created', clientJobId: null })
    const now = Date.parse(job.createdUtc) + age

    // Act
    const cancellable = canCancelJob(job, now)

    // Assert
    expect(cancellable).toBe(offered)
  })

  it('offers Cancel on a Submitted, Downloading, Paused or NeedsLinking job however young', () => {
    // Arrange
    const jobs = (['Submitted', 'Downloading', 'Paused', 'NeedsLinking'] as const).map((status) => jobOf({ status }))
    const now = Date.parse(jobOf({}).createdUtc)

    // Act
    const cancellable = jobs.filter((job) => canCancelJob(job, now))

    // Assert
    expect(cancellable).toEqual(jobs)
  })
})
