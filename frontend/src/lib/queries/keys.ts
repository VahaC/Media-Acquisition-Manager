import type { UsersQuery } from '../types'

/**
 * Every query key in the app. Hierarchical arrays that mirror the URL, so a prefix invalidates a
 * whole family. Components never write a literal key.
 */
export const qk = {
  health: {
    ready: ['health', 'ready'] as const,
  },
  setup: {
    status: ['setup', 'status'] as const,
  },
  settings: {
    users: {
      /** Every page of every search: what a change to any user invalidates. */
      all: ['settings', 'users'] as const,
      /** The whole query object is part of the key, so each page and search is cached apart. */
      list: (query: UsersQuery) => ['settings', 'users', 'list', query] as const,
    },
  },
}
