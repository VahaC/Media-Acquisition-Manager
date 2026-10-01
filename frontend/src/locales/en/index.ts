import auth from './auth.json'
import common from './common.json'
import confirm from './confirm.json'
import destinations from './destinations.json'
import errors from './errors.json'
import home from './home.json'
import integrations from './integrations.json'
import library from './library.json'
import mediaFolders from './mediaFolders.json'
import notFound from './notFound.json'
import users from './users.json'

/**
 * English, one namespace per feature. It is the source language every other language is translated
 * from, and the fallback for a key another language hasn't translated yet. Another language is a
 * sibling folder with the same files and this same object shape.
 */
export const en = { auth, common, confirm, destinations, errors, home, integrations, library, mediaFolders, notFound, users }
