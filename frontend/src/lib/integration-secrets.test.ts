import { toSecretUpsert } from './integration-secrets'

describe('toSecretUpsert', () => {
  it('sends Set for a typed value, Clear when removal is ticked and nothing otherwise', () => {
    // Arrange
    const typed = { value: ' pass word ', clear: false }
    const removed = { value: '', clear: true }
    const untouched = { value: '', clear: false }

    // Act
    const set = toSecretUpsert(typed)
    const clear = toSecretUpsert(removed)
    const keep = toSecretUpsert(untouched)

    // Assert
    // As typed: spaces around a password are part of it.
    expect(set).toEqual({ mode: 'Set', value: ' pass word ' })
    expect(clear).toEqual({ mode: 'Clear' })
    expect(keep).toBeUndefined()
  })
})
