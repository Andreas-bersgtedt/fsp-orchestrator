import type { CreatedRegistrationPair } from '../identity/graphRegistrationCreation.ts'
import {
  BrowserCacheLocation,
  PublicClientApplication,
} from '@azure/msal-browser'
import { clearStaleMsalInteraction } from './msalInteraction.ts'

const storageKey = 'fsp.orchestrator.bootstrap.candidate'

export type PendingCandidateSetup = {
  tenantId: string
  spaClientId: string
  apiClientId: string
  apiScope: string
  pair: CreatedRegistrationPair
}

export function savePendingCandidateSetup(setup: PendingCandidateSetup) {
  sessionStorage.setItem(storageKey, JSON.stringify(setup))
}

export function loadPendingCandidateSetup(): PendingCandidateSetup | undefined {
  const value = sessionStorage.getItem(storageKey)
  if (!value) {
    return undefined
  }

  try {
    return JSON.parse(value) as PendingCandidateSetup
  } catch {
    sessionStorage.removeItem(storageKey)
    return undefined
  }
}

export function clearPendingCandidateSetup() {
  sessionStorage.removeItem(storageKey)
}

export async function beginCandidateConfiguration(
  setup: PendingCandidateSetup,
) {
  savePendingCandidateSetup(setup)
  const candidate = new PublicClientApplication({
    auth: {
      authority: `https://login.microsoftonline.com/${setup.tenantId}`,
      clientId: setup.spaClientId,
      redirectUri: 'https://localhost:9443/auth/callback',
    },
    cache: {
      cacheLocation: BrowserCacheLocation.SessionStorage,
    },
  })
  await candidate.initialize()
  clearStaleMsalInteraction()
  await candidate.loginRedirect({
    prompt: 'select_account',
    scopes: [setup.apiScope],
  })
}
