import { describe, expect, it, vi } from 'vitest'
import { discoverOwnedApplications } from './graphDiscovery.ts'

describe('discoverOwnedApplications', () => {
  it('follows trusted pagination and returns normalized credential counts', async () => {
    const fetchImplementation = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            value: [
              {
                id: 'object-2',
                appId: 'app-2',
                displayName: 'Zulu',
                passwordCredentials: [{ keyId: 'not-returned' }],
                keyCredentials: [],
              },
            ],
            '@odata.nextLink':
              'https://graph.microsoft.com/v1.0/me/ownedObjects/microsoft.graph.application?$skiptoken=next',
          }),
          { status: 200 },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            value: [
              {
                id: 'object-1',
                appId: 'app-1',
                displayName: 'Alpha',
                passwordCredentials: [],
                keyCredentials: [{ keyId: 'not-returned' }],
              },
            ],
          }),
          { status: 200 },
        ),
      )

    const result = await discoverOwnedApplications(
      'graph-token',
      fetchImplementation,
    )

    expect(result.map((application) => application.displayName)).toEqual([
      'Alpha',
      'Zulu',
    ])
    expect(result[0]).not.toHaveProperty('passwordCredentials')
    expect(result[0]?.keyCredentialCount).toBe(1)
    expect(result[1]?.passwordCredentialCount).toBe(1)
    expect(fetchImplementation).toHaveBeenCalledTimes(2)
  })

  it('rejects an untrusted pagination URL before sending the token', async () => {
    const fetchImplementation = vi.fn<typeof fetch>().mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          value: [],
          '@odata.nextLink': 'https://attacker.example/steal',
        }),
        { status: 200 },
      ),
    )

    await expect(
      discoverOwnedApplications('graph-token', fetchImplementation),
    ).rejects.toThrow('untrusted pagination URL')
    expect(fetchImplementation).toHaveBeenCalledTimes(1)
  })
})
