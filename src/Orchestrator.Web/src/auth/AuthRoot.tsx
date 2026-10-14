import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  InteractionStatus,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser'
import {
  MsalProvider,
  useIsAuthenticated,
  useMsal,
} from '@azure/msal-react'
import { useEffect, useState } from 'react'
import App from '../App.tsx'
import type {
  AuthenticationConfiguration,
  AuthenticationState,
  OperatorSession,
} from './authTypes.ts'
import { clearSessionAndLogout } from './logout.ts'

type BootstrapState =
  | { kind: 'loading' }
  | { kind: 'ready'; configuration: AuthenticationConfiguration }
  | { kind: 'error'; message: string }

export function AuthRoot() {
  const [bootstrap, setBootstrap] = useState<BootstrapState>({ kind: 'loading' })

  useEffect(() => {
    const controller = new AbortController()

    async function loadConfiguration() {
      try {
        const response = await fetch('/api/auth/config', {
          headers: { Accept: 'application/json' },
          signal: controller.signal,
        })
        if (!response.ok) {
          throw new Error(`Authentication configuration returned ${response.status}.`)
        }

        setBootstrap({
          kind: 'ready',
          configuration: (await response.json()) as AuthenticationConfiguration,
        })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        setBootstrap({
          kind: 'error',
          message:
            error instanceof Error
              ? error.message
              : 'Authentication configuration could not be loaded.',
        })
      }
    }

    void loadConfiguration()
    return () => controller.abort()
  }, [])

  if (bootstrap.kind === 'loading') {
    return <App authentication={{ kind: 'loading' }} />
  }

  if (bootstrap.kind === 'error') {
    return (
      <App
        authentication={{
          kind: 'error',
          message: bootstrap.message,
          retry: async () => globalThis.location.reload(),
        }}
      />
    )
  }

  if (!bootstrap.configuration.configured) {
    return <App authentication={{ kind: 'setup-required' }} />
  }

  return <ConfiguredAuthentication configuration={bootstrap.configuration} />
}

function ConfiguredAuthentication({
  configuration,
}: {
  configuration: AuthenticationConfiguration
}) {
  const [instance, setInstance] = useState<PublicClientApplication>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let active = true

    async function initialize() {
      try {
        const client = new PublicClientApplication({
          auth: {
            authority: configuration.authority,
            clientId: configuration.spaClientId,
            postLogoutRedirectUri: new URL(configuration.redirectUri).origin,
            redirectUri: configuration.redirectUri,
          },
          cache: {
            cacheLocation: BrowserCacheLocation.SessionStorage,
          },
          system: {
            allowPlatformBroker: false,
          },
        })
        await client.initialize()
        const redirectResult = await client.handleRedirectPromise({
          navigateToLoginRequestUrl: true,
        })
        const account = redirectResult?.account ?? client.getAllAccounts()[0]
        if (account) {
          client.setActiveAccount(account)
        }
        if (active) {
          setInstance(client)
        }
      } catch (initializationError) {
        if (active) {
          setError(
            initializationError instanceof Error
              ? initializationError.message
              : 'Microsoft sign-in could not be initialized.',
          )
        }
      }
    }

    void initialize()
    return () => {
      active = false
    }
  }, [configuration])

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
      <SessionController configuration={configuration} />
    </MsalProvider>
  )
}

function SessionController({
  configuration,
}: {
  configuration: AuthenticationConfiguration
}) {
  const { accounts, inProgress, instance } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const [session, setSession] = useState<OperatorSession>()
  const [error, setError] = useState<string>()
  const account = instance.getActiveAccount() ?? accounts[0]

  useEffect(() => {
    const controller = new AbortController()

    async function loadSession(activeAccount: AccountInfo) {
      try {
        const token = await instance.acquireTokenSilent({
          account: activeAccount,
          scopes: [configuration.apiScope],
        })
        const response = await fetch('/api/session', {
          headers: {
            Accept: 'application/json',
            Authorization: `Bearer ${token.accessToken}`,
          },
          signal: controller.signal,
        })
        if (!response.ok) {
          throw new Error(`The Orchestrator API rejected the session (${response.status}).`)
        }

        setSession((await response.json()) as OperatorSession)
        setError(undefined)
      } catch (sessionError) {
        if (sessionError instanceof DOMException && sessionError.name === 'AbortError') {
          return
        }
        if (sessionError instanceof InteractionRequiredAuthError) {
          await instance.acquireTokenRedirect({
            account: activeAccount,
            scopes: [configuration.apiScope],
          })
          return
        }
        setError(
          sessionError instanceof Error
            ? sessionError.message
            : 'The signed-in session could not be loaded.',
        )
      }
    }

    if (account && isAuthenticated && inProgress === InteractionStatus.None) {
      void loadSession(account)
    }

    return () => controller.abort()
  }, [
    account,
    configuration.apiScope,
    inProgress,
    instance,
    isAuthenticated,
  ])

  async function signIn() {
    setError(undefined)
    await instance.loginRedirect({
      prompt: 'select_account',
      redirectUri: configuration.redirectUri,
      scopes: [configuration.apiScope],
    })
  }

  async function signOut() {
    await clearSessionAndLogout(
      instance,
      account,
      new URL(configuration.redirectUri).origin,
      () => {
        setSession(undefined)
        setError(undefined)
      },
    )
  }

  let authentication: AuthenticationState
  if (error) {
    authentication = { kind: 'error', message: error, retry: signIn }
  } else if (inProgress !== InteractionStatus.None) {
    authentication = { kind: 'signing-in' }
  } else if (session && account && isAuthenticated) {
    authentication = { kind: 'signed-in', session, signOut }
  } else {
    authentication = { kind: 'signed-out', signIn }
  }

  return <App authentication={authentication} />
}
