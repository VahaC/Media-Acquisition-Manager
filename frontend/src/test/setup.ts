import '@testing-library/jest-dom/vitest'
// The app's own i18n instance with the real English resources, the way main.tsx starts it. An
// assertion on English copy therefore also proves that its key resolves.
import '@/lib/i18n'

// Browser APIs jsdom does not implement. Radix calls some of them while it mounts a dialog; the
// rest are here so the first component that needs one doesn't fail with an unrelated TypeError.

class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}

globalThis.ResizeObserver ??= ResizeObserverStub

Element.prototype.scrollIntoView ??= function scrollIntoView() {}

// jsdom's own getContext prints "Not implemented" to the console on every call.
HTMLCanvasElement.prototype.getContext = (() => null) as typeof HTMLCanvasElement.prototype.getContext

// Every test file gets channels of its own. jsdom has no BroadcastChannel, so the tests use Node's,
// which delivers across every worker of the process: a test in another file that signs out (or
// whose refresh finds the session ended) reached this file's auth store as "another tab signed out",
// and a sign-in test then started at home instead of the page it opened — publish build #8 failed on
// exactly that. Scoped by file, the "other tabs" a test opens itself still reach its own store.
const NativeBroadcastChannel = globalThis.BroadcastChannel
const channelScope = crypto.randomUUID()

class FileScopedBroadcastChannel extends NativeBroadcastChannel {
  constructor(name: string) {
    super(`${channelScope}:${name}`)
  }
}

globalThis.BroadcastChannel = FileScopedBroadcastChannel
