import { describe, expect, it, vi } from 'vitest'
import { createRecommendedRegistrations } from './graphRegistrationCreation.ts'

describe('createRecommendedRegistrations', () => {
  it('creates a credential-free SPA/API pair and assigns SecurityAdmin', async () => {
    const fetchImplementation = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(json({ id: 'spa-object', appId: crypto.randomUUID(), displayName: 'SPA' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(json({ id: 'api-object', appId: crypto.randomUUID(), displayName: 'API' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(json({ id: 'spa-sp', appId: 'spa-app' }))
      .mockResolvedValueOnce(json({ id: 'api-sp', appId: 'api-app' }))
      .mockResolvedValueOnce(json({ id: 'assignment' }))

    const result = await createRecommendedRegistrations(
      'graph-token',
      crypto.randomUUID(),
      fetchImplementation,
    )

    expect(result.spa.passwordCredentialCount).toBe(0)
    expect(result.api.keyCredentialCount).toBe(0)
    expect(result.signedInUserRoles).toEqual(['SecurityAdmin'])
    const requestBodies = fetchImplementation.mock.calls
      .map((call) => call[1]?.body)
      .filter((body): body is string => typeof body === 'string')
    expect(requestBodies.join(' ')).not.toContain('passwordCredentials')
    expect(requestBodies.join(' ')).not.toContain('keyCredentials')
    expect(requestBodies.join(' ')).not.toContain('clientSecret')
    expect(requestBodies.join(' ')).toContain(
      '797f4846-ba00-4fd7-ba43-dac1f8f63013',
    )
    expect(requestBodies.join(' ')).toContain(
      '41094075-9dad-400e-a0bd-54e686782033',
    )
  })

  it('removes a partially created SPA when API creation fails', async () => {
    const fetchImplementation = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        json({ id: 'spa-object', appId: crypto.randomUUID(), displayName: 'SPA' }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response('{}', { status: 403 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))

    await expect(
      createRecommendedRegistrations(
        'graph-token',
        crypto.randomUUID(),
        fetchImplementation,
      ),
    ).rejects.toThrow('returned 403')
    expect(fetchImplementation).toHaveBeenLastCalledWith(
      'https://graph.microsoft.com/v1.0/applications/spa-object',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})

function json(value: unknown) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}
