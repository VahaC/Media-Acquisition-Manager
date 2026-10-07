import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/react'
// The app's own i18n instance with the real English resources, the way main.tsx starts it. An
// assertion on English copy therefore also proves that its key resolves.
import '@/lib/i18n'

// findBy* and waitFor give up after one second by default. On the CI agent, with a jsdom per test
// file and the backend tests running beside it, a render took 1.2-1.3 s and build #43 failed on two
// tests that pass on any rerun. Four seconds still ends a test that waits for something that never
// comes, well inside the test timeout (vitest.config.ts).
configure({ asyncUtilTimeout: 4000 })

// Browser APIs jsdom does not implement. Radix calls some of them while it mounts a dialog; the
// rest are here so the first component that needs one doesn't fail with an unrelated TypeError.

class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}

globalThis.ResizeObserver ??= ResizeObserverStub

Element.prototype.scrollIntoView ??= function scrollIntoView() {}

// The theme store asks for the operating system's colour scheme when it loads, and useNarrowScreen
// whether the window is a phone's. Here every query answers "no match": a light system that never
// changes, and a wide window, so a library card can show its versions as a table unless a test
// fakes a narrow one (test/match-media.ts). theme-store.test.ts brings its own.
window.matchMedia ??= (query: string) =>
  ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }) as MediaQueryList

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
