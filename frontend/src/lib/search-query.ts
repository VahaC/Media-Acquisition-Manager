import type { SearchRequest } from './types'

/**
 * MAM-12 — a search's id as the API makes it (a GUID). `?id=` is the address's, so anyone can put
 * anything there: a link with `../settings/…` in it would otherwise send the reader's token to that
 * API path and read the answer as a search. Encoding the id (search-api.ts) keeps a `/` inside one
 * segment, but not `..` itself, so the app sends no other value at all. MAM-16 — shared by the
 * Search page and the header's search field, which reads the same search.
 */
const SEARCH_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * MAM-16, ADR 0015 — a year from 1900 to 2099 in round brackets that end the line, after the title:
 * "Interstellar (2014)". Only in brackets: "Blade Runner 2049", "Wonder Woman 1984" and "Death Race
 * 2000" are titles that end in such a number, and a number taken from one could not be put back
 * (the page narrows with the text the server stored). `\d` is ASCII digits only, as the server reads
 * a year.
 */
const BRACKETED_TRAILING_YEAR = /^(.*\S)\s*\(((?:19|20)\d{2})\)$/

/**
 * MAM-33 — a TMDb id as the Recommendations page puts it beside `?q=`: ASCII digits, at most ten (TMDb
 * numbers its films well under that). Anything else is ignored rather than sent: the address is
 * anyone's to edit, and a refused id would cost the search its title too.
 */
const TMDB_ID_PATTERN = /^\d{1,10}$/

/** Whether `value` is shaped like a search's id, the only `?id=` the app sends anywhere. */
export function isSearchId(value: string): boolean {
  return SEARCH_ID_PATTERN.test(value)
}

/**
 * MAM-16 — the search a line typed in the header asks for: the line as the title, trimmed, except a
 * bracketed year that ends it, which becomes the year (ADR 0015). An IMDb or a TMDb id typed there
 * is a title too; the page's own fields take ids.
 */
export function parseSearchQuery(line: string): SearchRequest {
  const text = line.trim()
  const bracketed = BRACKETED_TRAILING_YEAR.exec(text)
  return bracketed === null ? { text } : { text: bracketed[1], year: bracketed[2] }
}

/** MAM-33 — the `?tmdbId=` of an address when it is one (digits only), else null. */
export function parseTmdbId(value: string | null): string | null {
  const trimmed = value?.trim() ?? ''
  return TMDB_ID_PATTERN.test(trimmed) ? trimmed : null
}
