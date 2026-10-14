export type AuthenticationConfiguration = {
  configured: boolean
  authority: string
  tenantId: string
  spaClientId: string
  apiClientId: string
  apiScope: string
  redirectUri: string
}

export type OperatorSession = {
  objectId: string
  tenantId: string
  displayName: string
  username: string
  roles: string[]
  permissions: Record<string, boolean>
}

export type AuthenticationState =
  | { kind: 'loading' }
  | { kind: 'setup-required' }
  | { kind: 'signed-out'; signIn: () => Promise<void> }
  | { kind: 'signing-in' }
  | {
      kind: 'signed-in'
      session: OperatorSession
      signOut: () => Promise<void>
    }
  | {
      kind: 'error'
      message: string
      retry: () => Promise<void>
    }
