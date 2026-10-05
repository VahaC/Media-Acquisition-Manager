import { act } from '@testing-library/react'

// An IntersectionObserver a test controls. jsdom has none, and lays nothing out: here no element is
// near the window until the test scrolls it there with `scrollNear`. The test's
// vi.unstubAllGlobals() takes it away again.

/** One observer the page created, with what it watches. */
interface Observer {
  callback: IntersectionObserverCallback
  options: IntersectionObserverInit | undefined
  targets: Set<Element>
  instance: IntersectionObserver
}

/** Puts a controlled IntersectionObserver in place of the browser's. */
export function stubIntersectionObserver() {
  const observers = new Set<Observer>()

  class ControlledIntersectionObserver implements IntersectionObserver {
    readonly root = null
    readonly rootMargin: string
    readonly thresholds = [0]
    readonly scrollMargin = '0px'
    private readonly observer: Observer

    constructor(callback: IntersectionObserverCallback, options?: IntersectionObserverInit) {
      this.rootMargin = options?.rootMargin ?? '0px'
      this.observer = { callback, options, targets: new Set(), instance: this }
      observers.add(this.observer)
    }

    observe(target: Element) {
      this.observer.targets.add(target)
    }

    unobserve(target: Element) {
      this.observer.targets.delete(target)
    }

    disconnect() {
      this.observer.targets.clear()
      observers.delete(this.observer)
    }

    takeRecords(): IntersectionObserverEntry[] {
      return []
    }
  }

  vi.stubGlobal('IntersectionObserver', ControlledIntersectionObserver)

  return {
    /** The elements watched right now, by every observer. */
    watched: () => [...observers].flatMap((observer) => [...observer.targets]),
    /** The options each live observer was created with. */
    options: () => [...observers].map((observer) => observer.options),
    /** Scrolls `target` near the window: every observer watching it is told it intersects. */
    scrollNear(target: Element) {
      act(() => {
        for (const observer of [...observers]) {
          if (!observer.targets.has(target)) continue
          const rect = target.getBoundingClientRect()
          const entry: IntersectionObserverEntry = {
            target,
            isIntersecting: true,
            intersectionRatio: 1,
            boundingClientRect: rect,
            intersectionRect: rect,
            rootBounds: null,
            time: performance.now(),
          }
          observer.callback([entry], observer.instance)
        }
      })
    },
  }
}
