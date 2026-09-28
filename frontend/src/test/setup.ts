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
