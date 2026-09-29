import type { PathMapping, PathMappingDraft } from './types'

let lastKey = 0

/**
 * A path mapping row for the integration dialog, empty or from a stored mapping, with a key no
 * other row of this page load has.
 *
 * MAM-7 — a key from one counter rather than the row's index: removing a row would otherwise hand
 * its inputs to the row below it, typed text included. Not `crypto.randomUUID()`: a LAN address
 * over plain http is no secure context, and the browser leaves it out there.
 */
export function newPathMappingDraft(mapping: PathMapping = { servicePrefix: '', appPrefix: '' }): PathMappingDraft {
  lastKey += 1
  return { key: lastKey, servicePrefix: mapping.servicePrefix, appPrefix: mapping.appPrefix }
}
