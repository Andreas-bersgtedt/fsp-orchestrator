export type AuthenticationConfiguration = {
  configured: boolean
  authority: string
  tenantId: string
  spaClientId: string
  apiClientId: string
  apiScope: string
  redirectUri: string
  bootstrap: BootstrapAuthenticationConfiguration
}

export type BootstrapAuthenticationConfiguration = {
  spaClientId: string
  authority: string
  redirectUri: string
  discoveryScopes: string[]
  creationScopes: string[]
}

export type BootstrapOperatorAccount = {
  displayName: string
  objectId: string
  tenantId: string
  username: string
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
  | { kind: 'bootstrap-signed-out'; signIn: () => Promise<void> }
  | { kind: 'bootstrap-signing-in' }
  | {
      kind: 'bootstrap-signed-in'
      account: BootstrapOperatorAccount
      bootstrap: BootstrapAuthenticationConfiguration
      acquireAccessToken: (scopes: string[]) => Promise<string>
      signOut: () => Promise<void>
    }
  | { kind: 'signed-out'; signIn: () => Promise<void> }
  | { kind: 'signing-in' }
  | {
      kind: 'signed-in'
      session: OperatorSession
      apiScope: string
      acquireAccessToken: (scopes: string[]) => Promise<string>
      signOut: () => Promise<void>
    }
  | {
      kind: 'error'
      message: string
      retry: () => Promise<void>
    }
