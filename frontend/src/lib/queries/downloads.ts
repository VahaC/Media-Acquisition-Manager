import { useMutation, useQuery } from '@tanstack/react-query'
import { downloadsApi } from '../downloads-api'
import type { SendDownloadRequest } from '../types'
import { qk } from './keys'

/**
 * MAM-13 — the download clients and destinations a release can be sent to, for the Download dialog.
 * Asked again each time the dialog opens: the owner may have just added a client or a destination.
 *
 * Asked even while the browser says it is offline (`networkMode: 'always'`): a query TanStack held
 * back would leave the dialog with no data and no error, and it would tell the person no client can
 * take the release, when only the network is down. Asked, it fails into the dialog's error line.
 */
export const useDownloadOptions = (releaseId: string) =>
  useQuery({
    queryKey: qk.downloads.options(releaseId),
    queryFn: () => downloadsApi.options(releaseId),
    // A 404 for an expired release won't change on a second try.
    retry: false,
    networkMode: 'always',
  })

/**
 * Sends a release to a download client. Never retried on its own (mutations aren't by default): the
 * request id keeps a resend from becoming a second download, but only the person's own Send resends.
 * Nothing on screen lists jobs yet (MAM-14), so there is nothing to refetch.
 *
 * Sent even while the browser says it is offline (`networkMode: 'always'`): TanStack would otherwise
 * hold the send back and send it on its own once the browser is back online — after the person may
 * have closed the dialog, sending a download nobody sees. Sent, it fails at once into the dialog's
 * error line, and the person's own Send repeats it under the same request id.
 */
export const useSendDownload = () =>
  useMutation({
    mutationFn: (request: SendDownloadRequest) => downloadsApi.send(request),
    networkMode: 'always',
  })
