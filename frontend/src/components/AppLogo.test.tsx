import { render } from '@testing-library/react'
import { AppLogo } from '@/components/AppLogo'

// The browser tab shows public/favicon.svg and the header draws AppLogo inline; they are one
// logo, so a change to one drawing has to reach the other.
const favicons = import.meta.glob<string>('../../public/favicon.svg', { query: '?raw', import: 'default', eager: true })
const faviconSvg = Object.values(favicons)[0]

function drawingOf(svg: SVGSVGElement) {
  return {
    viewBox: svg.getAttribute('viewBox'),
    tile: ['width', 'height', 'rx'].map((name) => svg.querySelector('rect')?.getAttribute(name)),
    transform: svg.querySelector('g')?.getAttribute('transform'),
    strokeWidth: svg.querySelector('g')?.getAttribute('stroke-width'),
    paths: [...svg.querySelectorAll('path')].map((path) => path.getAttribute('d')),
  }
}

describe('AppLogo', () => {
  it('draws the same logo as the favicon', () => {
    // Arrange
    const favicon = new DOMParser().parseFromString(faviconSvg, 'image/svg+xml').documentElement as unknown as SVGSVGElement

    // Act
    const { container } = render(<AppLogo />)

    // Assert
    expect(faviconSvg).toBeDefined()
    expect(drawingOf(container.querySelector('svg')!)).toEqual(drawingOf(favicon))
  })

  it('is hidden from screen readers, which read the app name next to it', () => {
    // Arrange
    const expected = 'true'

    // Act
    const { container } = render(<AppLogo />)

    // Assert
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', expected)
  })
})
