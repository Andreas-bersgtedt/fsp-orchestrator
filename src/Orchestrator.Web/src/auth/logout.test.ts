import { describe, expect, it, vi } from 'vitest'
import { clearSessionAndLogout } from './logout.ts'

describe('clearSessionAndLogout', () => {
  it('clears SPA state before MSAL starts account logout', async () => {
    const sequence: string[] = []
    const logoutRedirect = vi.fn(async () => {
      sequence.push('msal-logout')
    })

    await clearSessionAndLogout(
      { logoutRedirect },
      undefined,
      'https://localhost:9443',
      () => sequence.push('clear-spa-session'),
    )

    expect(sequence).toEqual(['clear-spa-session', 'msal-logout'])
    expect(logoutRedirect).toHaveBeenCalledWith({
      account: undefined,
      postLogoutRedirectUri: 'https://localhost:9443',
    })
  })
})
