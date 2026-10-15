import type { CreatedRegistrationPair } from '../identity/graphRegistrationCreation.ts'

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
