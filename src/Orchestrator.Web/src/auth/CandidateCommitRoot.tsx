import {
  BrowserCacheLocation,
  PublicClientApplication,
} from '@azure/msal-browser'
import { useEffect, useState } from 'react'
import App from '../App.tsx'
import type { AuthenticationState } from './authTypes.ts'
import {
  clearPendingCandidateSetup,
  type PendingCandidateSetup,
} from './candidateSetup.ts'

export function CandidateCommitRoot({
  pending,
}: {
  pending: PendingCandidateSetup
}) {
  const [authentication, setAuthentication] = useState<AuthenticationState>({
    kind: 'bootstrap-signing-in',
  })

  useEffect(() => {
    let active = true

    async function complete() {
      try {
        const candidate = new PublicClientApplication({
          auth: {
            authority: `https://login.microsoftonline.com/${pending.tenantId}`,
            clientId: pending.spaClientId,
            redirectUri: 'https://localhost:9443/auth/callback',
          },
          cache: {
            cacheLocation: BrowserCacheLocation.SessionStorage,
          },
        })
        await candidate.initialize()
        const result = await candidate.handleRedirectPromise({
          navigateToLoginRequestUrl: false,
        })
        if (!result?.accessToken) {
          throw new Error('Candidate API authentication did not return an access token.')
        }

        const response = await fetch('/api/identity/registrations/apply', {
          method: 'POST',
          headers: {
            Accept: 'application/json',
            Authorization: `Bearer ${result.accessToken}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            candidate: {
              tenantId: pending.tenantId,
              spaClientId: pending.spaClientId,
              apiClientId: pending.apiClientId,
              scopeName: 'orchestrator.access_as_user',
            },
            registration: {
              tenantId: pending.tenantId,
              signedInTenantId: pending.tenantId,
              spa: pending.pair.spa,
              api: pending.pair.api,
              spaServicePrincipalExists:
                pending.pair.spaServicePrincipalExists,
              apiServicePrincipalExists:
                pending.pair.apiServicePrincipalExists,
              signedInUserRoles: pending.pair.signedInUserRoles,
            },
          }),
        })
        if (!response.ok) {
          throw new Error(`Identity configuration commit returned ${response.status}.`)
        }

        clearPendingCandidateSetup()
        await waitForRestart()
      } catch (error) {
        clearPendingCandidateSetup()
        if (active) {
          setAuthentication({
            kind: 'error',
            message:
              error instanceof Error
                ? error.message
                : 'Candidate identity configuration failed.',
            retry: async () => globalThis.location.assign('/'),
          })
        }
      }
    }

    void complete()
    return () => {
      active = false
    }
  }, [pending])

  return <App authentication={authentication} />
}

async function waitForRestart() {
  const deadline = Date.now() + 60_000
  await new Promise((resolve) => setTimeout(resolve, 1_000))
  while (Date.now() < deadline) {
    try {
      const response = await fetch('/api/auth/config', {
        headers: { Accept: 'application/json' },
      })
      if (response.ok) {
        const configuration = (await response.json()) as { configured: boolean }
        if (configuration.configured) {
          globalThis.location.assign('/')
          return
        }
      }
    } catch {
      // The old process has released the port and the replacement is starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000))
  }
  throw new Error(
    'Identity configuration was committed, but Orchestrator did not restart within 60 seconds.',
  )
}
