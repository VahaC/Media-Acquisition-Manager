import { screen } from '@testing-library/react'
import type { UserEvent } from '@testing-library/user-event'

/**
 * MAM-36 — the Sources card starts as a status bar, and its rows (each source's sentences and button,
 * the Media folders row, the inspection row) are what Details opens. A test of a row opens them once
 * the sources have arrived, so it reads the rows as it always did.
 */
export async function openSourceDetails(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Details' }))
}
