/**
 * Every query key in the app. Hierarchical arrays that mirror the URL, so a prefix invalidates a
 * whole family. Components never write a literal key.
 */
export const qk = {
  health: {
    ready: ['health', 'ready'] as const,
  },
}
