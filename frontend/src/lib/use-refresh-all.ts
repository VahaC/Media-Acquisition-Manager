import { useEffect, useRef, useState } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { isSyncUnderWay } from '@/lib/library-runs'
import { qk, useStartFileInspection, useStartFolderScan, useStartLibrarySync } from '@/lib/queries'
import type { LibrarySource, LibrarySourceList } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'

/** Which of the three steps Refresh all is on. */
export type RefreshStep = 'sync' | 'scan' | 'inspect'

/** Why the chain stopped, as the sentence the page shows; anything else thrown is unexpected. */
class RefreshStopped extends Error {}

/**
 * MAM-36 — Refresh all, run from the page: a sync of every enabled library the app can read, then a
 * scan of the media folders, then an inspection of the files, each started only once the step before
 * it has ended. The order matters: the inspection reads the files the syncs and the scan stored, so it
 * goes last.
 *
 * The waiting reads the sources the page already polls (useLibrarySources) rather than polling by
 * itself, so a run is followed exactly as the rows follow it. A start the API refuses because the same
 * run is under way joins that run; any other refusal, or a run that ends Failed, stops the chain at
 * that step with a sentence, and the steps after it are not started.
 *
 * The chain lives in the page: leaving it stops the chain after the step under way. The API keeps
 * that run and queues the inspection after a sync or a scan by itself, so nothing is left half-done.
 */
export function useRefreshAll() {
  const { t } = useTranslation('library')
  const queryClient = useQueryClient()
  const startSync = useStartLibrarySync()
  const startScan = useStartFolderScan()
  const startInspection = useStartFileInspection()
  const [step, setStep] = useState<RefreshStep | null>(null)
  const [error, setError] = useState<string | null>(null)
  const running = useRef<AbortController | null>(null)

  useEffect(() => () => running.current?.abort(), [])

  const read = () => queryClient.getQueryData<LibrarySourceList>(qk.library.sources)
  const stopped = (reason: string) => new RefreshStopped(t('refreshAll.stopped', { reason }))
  const latestRunOf = (integrationId: string) => read()?.items.find((item) => item.integrationId === integrationId)?.latestRun

  /** The sync step: start every library, join the ones already syncing, wait for all of them. */
  const syncLibraries = async (libraries: LibrarySource[], signal: AbortSignal) => {
    setStep('sync')
    for (const library of libraries) {
      try {
        await startSync.mutateAsync(library.integrationId)
      } catch (e: unknown) {
        // The refusal of a sync already under way is no failure: the chain waits for that one. The
        // start refetched the sources before it rejected, so the cache already shows it.
        if (!isSyncUnderWay(latestRunOf(library.integrationId))) {
          throw stopped(getApiErrorMessage(e, t('sources.startFailed')))
        }
      }
    }
    const synced = await untilSources(queryClient, signal, (list) => libraries.every(
      (library) => !isSyncUnderWay(list.items.find((item) => item.integrationId === library.integrationId)?.latestRun),
    ))
    const failed = libraries.find(
      (library) => synced.items.find((item) => item.integrationId === library.integrationId)?.latestRun?.status === 'Failed',
    )
    if (failed) throw stopped(t('refreshAll.syncFailed', { name: failed.name }))
  }

  const run = async (signal: AbortSignal) => {
    const first = read()
    if (first === undefined) return
    const libraries = first.items.filter((source) => source.isEnabled && source.canReadLibrary)
    if (libraries.length > 0) await syncLibraries(libraries, signal)

    // Read again: without a media folder the API refuses a scan and an inspection with a 409, and a
    // sync that just ended may have been the last thing there was to do.
    if (read()?.inspection.hasAllowedRoots !== true) return

    throwIfAborted(signal)
    setStep('scan')
    try {
      await startScan.mutateAsync()
    } catch (e: unknown) {
      throw stopped(getApiErrorMessage(e, t('scan.startFailed')))
    }
    const scanned = await untilSources(queryClient, signal, (list) => list.scan.activeRun === null)
    if (scanned.scan.lastFinishedRun?.status === 'Failed') throw stopped(t('refreshAll.scanFailed'))

    throwIfAborted(signal)
    setStep('inspect')
    try {
      await startInspection.mutateAsync()
    } catch (e: unknown) {
      throw stopped(getApiErrorMessage(e, t('inspection.startFailed')))
    }
    const inspected = await untilSources(queryClient, signal, (list) => list.inspection.activeRun === null)
    if (inspected.inspection.lastFinishedRun?.status === 'Failed') throw stopped(t('refreshAll.inspectFailed'))
  }

  const start = () => {
    if (running.current !== null) return
    const controller = new AbortController()
    running.current = controller
    setError(null)
    void run(controller.signal)
      .catch((e: unknown) => {
        if (controller.signal.aborted) return
        setError(e instanceof RefreshStopped ? e.message : t('refreshAll.stopped', { reason: getApiErrorMessage(e, t('refreshAll.failed')) }))
      })
      .finally(() => {
        running.current = null
        if (!controller.signal.aborted) setStep(null)
      })
  }

  return { step, error, start }
}

function throwIfAborted(signal: AbortSignal) {
  if (signal.aborted) throw signal.reason
}

/**
 * Resolves with the sources as soon as `done` holds for them, checking the cache now and after every
 * change to it, so the answer that ends a step is the one the step goes on from. Rejects if `signal`
 * aborts first.
 */
function untilSources(
  queryClient: QueryClient,
  signal: AbortSignal,
  done: (list: LibrarySourceList) => boolean,
): Promise<LibrarySourceList> {
  return new Promise((resolve, reject) => {
    const check = () => {
      const list = queryClient.getQueryData<LibrarySourceList>(qk.library.sources)
      if (list === undefined || !done(list)) return
      cleanUp()
      resolve(list)
    }
    const abort = () => {
      cleanUp()
      reject(signal.reason)
    }
    const unsubscribe = queryClient.getQueryCache().subscribe(check)
    const cleanUp = () => {
      unsubscribe()
      signal.removeEventListener('abort', abort)
    }
    signal.addEventListener('abort', abort)
    if (signal.aborted) abort()
    else check()
  })
}
