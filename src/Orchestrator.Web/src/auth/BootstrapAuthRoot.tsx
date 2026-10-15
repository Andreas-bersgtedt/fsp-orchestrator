import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  InteractionStatus,
  PublicClientApplication,
} from '@azure/msal-browser'
import { MsalProvider, useMsal } from '@azure/msal-react'
import { useEffect, useState } from 'react'
import App from '../App.tsx'
import type {
  AuthenticationConfiguration,
  AuthenticationState,
} from './authTypes.ts'
import { clearSessionAndLogout } from './logout.ts'

export function BootstrapAuthRoot({
  configuration,
}: {
  configuration: AuthenticationConfiguration
}) {
  const [instance, setInstance] = useState<PublicClientApplication>()
  const [error, setError] = useState<string>()
  const bootstrap = configuration.bootstrap

  useEffect(() => {
    let active = true

    async function initialize() {
      try {
        const client = new PublicClientApplication({
          auth: {
            authority: bootstrap.authority,
            clientId: bootstrap.spaClientId,
            redirectUri: bootstrap.redirectUri,
            postLogoutRedirectUri: new URL(bootstrap.redirectUri).origin,
          },
          cache: {
            cacheLocation: BrowserCacheLocation.SessionStorage,
          },
          system: {
            allowPlatformBroker: false,
          },
        })
        await client.initialize()
        const result = await client.handleRedirectPromise({
          navigateToLoginRequestUrl: true,
        })
        const account = result?.account ?? client.getAllAccounts()[0]
        if (account) {
          client.setActiveAccount(account)
        }
        if (active) {
          setInstance(client)
        }
      } catch (initializationError) {
        if (active) {
          setError(errorMessage(initializationError))
        }
      }
    }

    void initialize()
    return () => {
      active = false
    }
  }, [bootstrap])

  if (error) {
    return (
      <App
        authentication={{
          kind: 'error',
          message: error,
          retry: async () => globalThis.location.reload(),
        }}
      />
    )
  }

  if (!instance) {
    return <App authentication={{ kind: 'loading' }} />
  }

  return (
    <MsalProvider instance={instance}>
      <BootstrapSession
        configuration={configuration}
      />
    </MsalProvider>
  )
}

function BootstrapSession({
  configuration,
}: {
  configuration: AuthenticationConfiguration
}) {
  const { accounts, inProgress, instance } = useMsal()
  const account = instance.getActiveAccount() ?? accounts[0]
  const bootstrap = configuration.bootstrap
  const [error, setError] = useState<string>()

  async function acquireAccessToken(scopes: string[]) {
    if (!account) {
      throw new Error('Sign in before requesting Microsoft Graph access.')
    }

    try {
      const result = await instance.acquireTokenSilent({
        account,
        scopes,
      })
      return result.accessToken
    } catch (tokenError) {
      if (tokenError instanceof InteractionRequiredAuthError) {
        await instance.acquireTokenRedirect({
          account,
          scopes,
        })
      }
      throw tokenError
    }
  }

  async function signIn() {
    setError(undefined)
    await instance.loginRedirect({
      prompt: 'select_account',
      redirectUri: bootstrap.redirectUri,
      scopes: bootstrap.discoveryScopes,
    })
  }

  async function signOut() {
    setError(undefined)
    await clearSessionAndLogout(
      instance,
      account,
      new URL(bootstrap.redirectUri).origin,
      () => setError(undefined),
    )
  }

  let authentication: AuthenticationState
  if (error) {
    authentication = { kind: 'error', message: error, retry: signIn }
  } else if (!account && inProgress !== InteractionStatus.None) {
    authentication = { kind: 'bootstrap-signing-in' }
  } else if (account) {
    authentication = {
      kind: 'bootstrap-signed-in',
      account: {
        displayName: account.name ?? account.username,
        objectId: account.localAccountId,
        tenantId: account.tenantId,
        username: account.username,
      },
      bootstrap,
      acquireAccessToken,
      signOut,
    }
  } else {
    authentication = { kind: 'bootstrap-signed-out', signIn }
  }

  return <App authentication={authentication} />
}

function errorMessage(error: unknown) {
  return error instanceof Error
    ? error.message
    : 'Bootstrap authentication could not be initialized.'
}
