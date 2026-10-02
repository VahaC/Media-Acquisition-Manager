import { canCancelJob, DOWNLOAD_JOB_STATUSES, isJobFollowed } from '@/lib/download-jobs'
import { jobOf } from '@/test/downloads'

/** 5 minutes, the server's InterruptedSendAge, in ms. */
const INTERRUPTED_SEND_AGE_MS = 5 * 60_000

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
