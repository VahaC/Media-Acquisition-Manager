import type { SearchRelease } from './types'

/**
 * The name a release goes by: its listing's description when the listing gave one — Toloka's whole
 * title, Ukrainian first, where "Strip Cyrillic Letters" leaves a Ukrainian-only release's title as
 * punctuation alone, "' (2016) HDTV 1080p" — else its title. The server names the release's job the
 * same way (`DownloadJob.releaseTitle`), so the Search page, the Download dialog and the Jobs page
 * call one release by one name.
 */
export function releaseName(release: Pick<SearchRelease, 'title' | 'description'>): string {
  return release.description ?? release.title
}
