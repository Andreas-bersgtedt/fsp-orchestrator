import { describe, expect, it, vi } from 'vitest'
import { clearStaleMsalInteraction } from './msalInteraction.ts'

describe('clearStaleMsalInteraction', () => {
  it('removes only the global interaction marker', () => {
    const removeItem = vi.fn()

    clearStaleMsalInteraction({ removeItem })

    expect(removeItem).toHaveBeenCalledOnce()
    expect(removeItem).toHaveBeenCalledWith('msal.interaction.status')
  })
})
