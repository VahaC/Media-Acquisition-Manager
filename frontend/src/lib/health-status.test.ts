import { HEALTH_STATUS_TEXT } from '@/lib/health-status'
import { i18n } from '@/lib/i18n'

describe('HEALTH_STATUS_TEXT', () => {
  it('words each readiness status the way the home page and the account menu show it', () => {
    // Arrange
    const statuses = ['Healthy', 'Degraded', 'Unhealthy'] as const

    // Act
    const words = statuses.map((status) => i18n.t(HEALTH_STATUS_TEXT[status], { ns: 'home' }))

    // Assert
    expect(words).toEqual(['Ready', 'Ready, with warnings', 'Not ready'])
  })
})
