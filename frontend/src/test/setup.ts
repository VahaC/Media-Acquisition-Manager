import '@testing-library/jest-dom/vitest'

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
