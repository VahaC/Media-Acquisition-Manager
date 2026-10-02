import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ThemeSwitch } from '@/components/ThemeSwitch'
import { useThemeStore } from '@/lib/theme-store'

const isDark = () => document.documentElement.classList.contains('dark')

describe('ThemeSwitch', () => {
  beforeEach(() => {
    useThemeStore.setState({ mode: 'system' })
    document.documentElement.classList.remove('dark')
    localStorage.clear()
  })

  it('offers Light, Dark and System under the name Theme, with the theme in use checked', () => {
    // Arrange
    useThemeStore.setState({ mode: 'dark' })

    // Act
    render(<ThemeSwitch />)

    // Assert
    expect(screen.getByRole('radiogroup', { name: 'Theme' })).toBeInTheDocument()
    expect(screen.getAllByRole('radio').map((radio) => radio.getAttribute('value'))).toEqual(['light', 'dark', 'system'])
    expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Light' })).not.toBeChecked()
    expect(screen.getByRole('radio', { name: 'System' })).not.toBeChecked()
  })

  it('switches the theme at once and remembers it in this browser', async () => {
    // Arrange
    const user = userEvent.setup()
    render(<ThemeSwitch />)

    // Act
    await user.click(screen.getByRole('radio', { name: 'Dark' }))

    // Assert
    expect(isDark()).toBe(true)
    expect(localStorage.getItem('mam:theme')).toBe('dark')
    expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
  })

  it('forgets the stored theme when System is picked', async () => {
    // Arrange
    useThemeStore.getState().setMode('dark')
    const user = userEvent.setup()
    render(<ThemeSwitch />)

    // Act
    await user.click(screen.getByRole('radio', { name: 'System' }))

    // Assert
    expect(localStorage.getItem('mam:theme')).toBeNull()
    // The light operating system test/setup.ts stands in for.
    expect(isDark()).toBe(false)
    expect(screen.getByRole('radio', { name: 'System' })).toBeChecked()
  })

  it('keeps two switches on screen in step', async () => {
    // Arrange
    const user = userEvent.setup()
    render(
      <>
        <ThemeSwitch />
        <ThemeSwitch showLabels />
      </>,
    )
    const [first, second] = screen.getAllByRole('radio', { name: 'Light' })

    // Act
    await user.click(first)

    // Assert
    expect(first).toBeChecked()
    expect(second).toBeChecked()
    expect(screen.getAllByRole('radio', { name: 'System' }).filter((radio) => (radio as HTMLInputElement).checked)).toEqual([])
  })

  it('names each icon in a tooltip, unless the words are shown beside the icons', () => {
    // Arrange
    const { container: iconsOnly } = render(<ThemeSwitch />)

    // Act
    const { container: withLabels } = render(<ThemeSwitch showLabels />)

    // Assert
    expect(within(iconsOnly).getByTitle('Light')).toContainElement(within(iconsOnly).getByRole('radio', { name: 'Light' }))
    expect(within(iconsOnly).getByTitle('Dark')).toBeInTheDocument()
    expect(within(iconsOnly).getByTitle('System')).toBeInTheDocument()
    expect(within(withLabels).queryByTitle(/Light|Dark|System/)).not.toBeInTheDocument()
  })
})
