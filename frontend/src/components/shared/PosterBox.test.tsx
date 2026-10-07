import { createRef } from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { PosterBox } from '@/components/shared/PosterBox'
import type { MediaWorkKind } from '@/lib/types'

// MAM-35 — the box CatalogPoster and LibraryPoster both show their picture in; how each fetches it is
// in their own tests.

const jpeg = `data:image/jpeg;base64,${btoa('jpeg bytes')}`
const otherJpeg = `data:image/jpeg;base64,${btoa('other jpeg bytes')}`

describe('PosterBox', () => {
  it('shows the picture it is given in the box, named by the title', () => {
    // Arrange
    const source = jpeg

    // Act
    const { container } = render(<PosterBox source={source} title="Чужі" kind="Movie" />)

    // Assert
    const image = screen.getByRole('img', { name: 'Poster of Чужі' })
    expect(image).toHaveAttribute('src', jpeg)
    expect(image.parentElement).toBe(container.querySelector('.poster-box'))
    expect(container.querySelector('svg')).not.toBeInTheDocument()
  })

  it('puts the ref on the box itself, the element a caller watches to fetch the picture', () => {
    // Arrange
    const ref = createRef<HTMLDivElement>()

    // Act
    const { container } = render(<PosterBox ref={ref} title="Чужі" kind="Movie" />)

    // Assert
    expect(ref.current).toBe(container.querySelector('.poster-box'))
  })

  it.each<[MediaWorkKind, string]>([
    ['Movie', 'lucide-film'],
    ['Series', 'lucide-tv'],
    ['Episode', 'lucide-tv'],
  ])('shows the placeholder of a %s without a picture', (kind, icon) => {
    // Arrange
    const source = null

    // Act
    const { container } = render(<PosterBox source={source} title="Чужі" kind={kind} />)

    // Assert
    expect(container.querySelector(`.poster-box > .${icon}`)).toHaveAttribute('aria-hidden', 'true')
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('goes back to the placeholder when the browser cannot decode the picture, and shows the next one it is given', () => {
    // Arrange
    const { container, rerender } = render(<PosterBox source={jpeg} title="Чужі" kind="Movie" />)
    fireEvent.error(screen.getByRole('img', { name: 'Poster of Чужі' }))
    const placeholderAfterError = container.querySelector('.lucide-film')

    // Act
    rerender(<PosterBox source={otherJpeg} title="Чужі" kind="Movie" />)

    // Assert
    expect(placeholderAfterError).not.toBeNull()
    expect(screen.getByRole('img', { name: 'Poster of Чужі' })).toHaveAttribute('src', otherJpeg)
  })
})
