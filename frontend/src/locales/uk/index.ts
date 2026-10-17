import auth from './auth.json'
import common from './common.json'
import confirm from './confirm.json'
import destinations from './destinations.json'
import downloads from './downloads.json'
import duplicates from './duplicates.json'
import errors from './errors.json'
import home from './home.json'
import integrations from './integrations.json'
import jobs from './jobs.json'
import library from './library.json'
import libraryLinks from './libraryLinks.json'
import mediaFolders from './mediaFolders.json'
import notFound from './notFound.json'
import profile from './profile.json'
import recommendations from './recommendations.json'
import search from './search.json'
import users from './users.json'

/**
 * MAM-46 — Ukrainian, translated from `en/` with the same files and this same object shape. Every
 * word follows docs/i18n/glossary-uk.md; src/locales/locales.test.ts holds each namespace to `en`'s
 * keys, with the four Ukrainian plural forms for every counted one.
 */
export const uk = { auth, common, confirm, destinations, downloads, duplicates, errors, home, integrations, jobs, library, libraryLinks, mediaFolders, notFound, profile, recommendations, search, users }
