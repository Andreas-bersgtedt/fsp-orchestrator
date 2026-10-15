import {
  Badge,
  Button,
  Card,
  CardFooter,
  CardHeader,
  FluentProvider,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Title1,
  Title2,
  webLightTheme,
} from '@fluentui/react-components'
import { lazy, Suspense, useEffect, useState } from 'react'
import type { AuthenticationState } from './auth/authTypes.ts'
import './App.css'

const RegistrationSetup = lazy(async () => {
  const module = await import('./identity/RegistrationSetup.tsx')
  return { default: module.RegistrationSetup }
})
const BootstrapSetup = lazy(async () => {
  const module = await import('./identity/BootstrapSetup.tsx')
  return { default: module.BootstrapSetup }
})
const DatabaseStatus = lazy(async () => {
  const module = await import('./system/DatabaseStatus.tsx')
  return { default: module.DatabaseStatus }
})
const PrerequisitesPage = lazy(async () => {
  const module = await import('./prerequisites/PrerequisitesPage.tsx')
  return { default: module.PrerequisitesPage }
})
const RegisterDeployment = lazy(async () => {
  const module = await import('./deployments/RegisterDeployment.tsx')
  return { default: module.RegisterDeployment }
})
const DeploymentInventory = lazy(async () => {
  const module = await import('./deployments/DeploymentInventory.tsx')
  return { default: module.DeploymentInventory }
})
const AzureDiscovery = lazy(async () => {
  const module = await import('./azure/AzureDiscovery.tsx')
  return { default: module.AzureDiscovery }
})
const DeploymentProfilePage = lazy(async () => {
  const module = await import(
    './deployments/DeploymentProfilePage.tsx'
  )
  return { default: module.DeploymentProfilePage }
})

type HealthResponse = {
  status: string
  checkedAt: string
}

type HealthState =
  | { kind: 'loading' }
  | { kind: 'online'; response: HealthResponse }
  | { kind: 'offline' }

const workflows = [
  {
    title: 'Run prerequisites',
    description:
      'Validate this host, identity, Azure access, network paths, capacity, and FSP compatibility.',
    action: 'Open prerequisites',
    permission: 'Operator',
  },
  {
    title: 'Register deployment',
    description:
      'Connect to an existing FSP Lite or Enterprise endpoint through its versioned HTTPS API.',
    action: 'Register FSP',
    permission: 'Operator',
  },
  {
    title: 'Deploy to Azure',
    description:
      'Preview and apply an Enterprise deployment through ARM and the private AKS command API.',
    action: 'Create deployment',
    permission: 'DeploymentAdmin',
  },
] as const

export default function App({
  authentication,
}: {
  authentication: AuthenticationState
}) {
  const [health, setHealth] = useState<HealthState>({ kind: 'loading' })
  const [inventoryRefreshKey, setInventoryRefreshKey] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    async function loadHealth() {
      try {
        const response = await fetch('/api/health', {
          headers: { Accept: 'application/json' },
          signal: controller.signal,
        })
        if (!response.ok) {
          setHealth({ kind: 'offline' })
          return
        }
        setHealth({
          kind: 'online',
          response: (await response.json()) as HealthResponse,
        })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        setHealth({ kind: 'offline' })
      }
    }

    void loadHealth()
    return () => controller.abort()
  }, [])

  return (
    <FluentProvider theme={webLightTheme}>
      <div className="app-shell">
        <header className="top-bar">
          <div>
            <Text size={200} weight="semibold" className="product-kicker">
              FABRIC SHORTCUT PROXY
            </Text>
            <Text size={500} weight="semibold">
              Orchestrator
            </Text>
          </div>
          <div className="header-actions">
            <HealthBadge health={health} />
            <AccountControl authentication={authentication} />
          </div>
        </header>

        <main>
          <section className="hero">
            <Text size={300} weight="semibold" className="eyebrow">
              LOCAL MANAGEMENT PLANE
            </Text>
            <Title1>Operate every FSP deployment from one place.</Title1>
            <Text size={500} className="hero-copy">
              Register existing environments or deploy to Azure through remote,
              authenticated control-plane APIs. No access to FSP filesystems is
              required.
            </Text>
          </section>

          <AuthenticationPanel authentication={authentication} />

          {authentication.kind === 'bootstrap-signed-in' ? (
            <Suspense fallback={<Spinner label="Loading Entra bootstrap setup" />}>
              <BootstrapSetup
                account={authentication.account}
                acquireAccessToken={authentication.acquireAccessToken}
                bootstrap={authentication.bootstrap}
                interactionInProgress={authentication.interactionInProgress}
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          authentication.session.permissions.SecurityAdmin ? (
            <Suspense fallback={<Spinner label="Loading Entra registration setup" />}>
              <RegistrationSetup
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
                identityConfiguration={authentication.identityConfiguration}
                session={authentication.session}
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          (authentication.session.permissions.Reader ||
            authentication.session.permissions.Operator) ? (
            <Suspense fallback={<Spinner label="Loading prerequisites" />}>
              <PrerequisitesPage
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
                canRun={Boolean(authentication.session.permissions.Operator)}
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          authentication.session.permissions.Operator ? (
            <Suspense fallback={<Spinner label="Loading deployment registration" />}>
              <RegisterDeployment
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
                onRegistered={() =>
                  setInventoryRefreshKey((current) => current + 1)
                }
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          authentication.session.permissions.Reader ? (
            <Suspense fallback={<Spinner label="Loading deployment inventory" />}>
              <DeploymentInventory
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
                canRefresh={Boolean(
                  authentication.session.permissions.Operator,
                )}
                refreshKey={inventoryRefreshKey}
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          authentication.session.permissions.DeploymentAdmin ? (
            <Suspense fallback={<Spinner label="Loading deployment profile" />}>
              <DeploymentProfilePage
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          authentication.session.permissions.DeploymentAdmin ? (
            <Suspense fallback={<Spinner label="Loading Azure discovery" />}>
              <AzureDiscovery
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
              />
            </Suspense>
          ) : null}

          {authentication.kind === 'signed-in' &&
          (authentication.session.permissions.Reader ||
            authentication.session.permissions.SecurityAdmin) ? (
            <Suspense fallback={<Spinner label="Loading database diagnostics" />}>
              <DatabaseStatus
                acquireAccessToken={authentication.acquireAccessToken}
                apiScope={authentication.apiScope}
                canManage={Boolean(
                  authentication.session.permissions.SecurityAdmin,
                )}
              />
            </Suspense>
          ) : null}

          <section aria-labelledby="workflow-heading">
            <div className="section-heading">
              <div>
                <Title2 id="workflow-heading">Start a workflow</Title2>
                <Text>Choose the next operation for your FSP estate.</Text>
              </div>
              <Badge appearance="outline">FSP 3.1.0 contracts loaded</Badge>
            </div>

            <div className="workflow-grid">
              {workflows.map((workflow) => {
                const allowed =
                  authentication.kind === 'signed-in' &&
                  authentication.session.permissions[workflow.permission]
                return (
                  <Card key={workflow.title} className="workflow-card">
                    <CardHeader
                      header={<Text weight="semibold">{workflow.title}</Text>}
                      description={<Text>{workflow.description}</Text>}
                    />
                    <CardFooter>
                      <Button
                        appearance="primary"
                        disabled={
                          !allowed
                        }
                        onClick={() => {
                          const target =
                            workflow.title === 'Run prerequisites'
                              ? 'prerequisites'
                              : workflow.title === 'Deploy to Azure'
                                ? 'deployment-profile'
                                : 'register-deployment'
                          document
                            .getElementById(target)
                            ?.scrollIntoView({ behavior: 'smooth' })
                        }}
                      >
                        {workflow.action}
                      </Button>
                      <Text size={200}>
                        Requires {workflow.permission}
                      </Text>
                    </CardFooter>
                  </Card>
                )
              })}
            </div>
          </section>
        </main>

        <footer>
          <Text size={200}>
            Remote management boundary: ARM, AKS command, Key Vault, and FSP
            HTTPS APIs.
          </Text>
        </footer>
      </div>
    </FluentProvider>
  )
}

function AuthenticationPanel({
  authentication,
}: {
  authentication: AuthenticationState
}) {
  if (authentication.kind === 'setup-required') {
    return (
      <MessageBar intent="warning" className="auth-panel">
        <MessageBarBody>
          <MessageBarTitle>Microsoft Entra setup required</MessageBarTitle>
          Configure the tenant, SPA client ID, API client ID, delegated scope,
          and application roles. No client secret is required or supported.
        </MessageBarBody>
      </MessageBar>
    )
  }

  if (authentication.kind === 'bootstrap-signed-out') {
    return (
      <Card className="auth-panel">
        <CardHeader
          header={<Text weight="semibold">Configure Microsoft Entra</Text>}
          description={
            <Text>
              Sign in with your organizational account using the built-in public
              bootstrap SPA. No client secret or application key is used.
            </Text>
          }
        />
        <CardFooter>
          <Button
            appearance="primary"
            onClick={() => void authentication.signIn()}
          >
            Sign in to configure Entra
          </Button>
        </CardFooter>
      </Card>
    )
  }

  if (authentication.kind === 'bootstrap-signed-in') {
    return (
      <Card className="auth-panel">
        <CardHeader
          header={
            <Text weight="semibold">
              Bootstrap session: {authentication.account.displayName}
            </Text>
          }
          description={<Text>{authentication.account.username}</Text>}
        />
      </Card>
    )
  }

  if (authentication.kind === 'signed-out') {
    return (
      <Card className="auth-panel">
        <CardHeader
          header={<Text weight="semibold">Sign in to manage FSP</Text>}
          description={
            <Text>
              Use your organizational Microsoft account. Authentication uses
              authorization code flow with PKCE and no application secret.
            </Text>
          }
        />
        <CardFooter>
          <Button
            appearance="primary"
            onClick={() => void authentication.signIn()}
          >
            Sign in with Microsoft
          </Button>
        </CardFooter>
      </Card>
    )
  }

  if (authentication.kind === 'signed-in') {
    return (
      <Card className="auth-panel">
        <CardHeader
          header={
            <Text weight="semibold">
              Signed in as {authentication.session.displayName}
            </Text>
          }
          description={<Text>{authentication.session.username}</Text>}
        />
        <CardFooter className="role-list">
          {authentication.session.roles.map((role) => (
            <Badge key={role} appearance="outline">
              {role}
            </Badge>
          ))}
        </CardFooter>
      </Card>
    )
  }

  if (authentication.kind === 'error') {
    return (
      <MessageBar intent="error" className="auth-panel">
        <MessageBarBody>
          <MessageBarTitle>Authentication unavailable</MessageBarTitle>
          {authentication.message}
          <Button
            appearance="subtle"
            onClick={() => void authentication.retry()}
          >
            Retry
          </Button>
        </MessageBarBody>
      </MessageBar>
    )
  }

  return (
    <div className="auth-panel auth-progress">
      <Spinner
        label={
          authentication.kind === 'signing-in' ||
          authentication.kind === 'bootstrap-signing-in'
            ? 'Completing Microsoft sign-in'
            : 'Loading authentication'
        }
      />
    </div>
  )
}

function AccountControl({
  authentication,
}: {
  authentication: AuthenticationState
}) {
  if (authentication.kind === 'bootstrap-signed-in') {
    return (
      <div className="account-control">
        <Text size={200}>{authentication.account.username}</Text>
        <Button
          appearance="subtle"
          size="small"
          onClick={() => void authentication.signOut()}
        >
          Sign out
        </Button>
      </div>
    )
  }

  if (authentication.kind !== 'signed-in') {
    return null
  }

  return (
    <div className="account-control">
      <Text size={200}>{authentication.session.username}</Text>
      <Button
        appearance="subtle"
        size="small"
        onClick={() => void authentication.signOut()}
      >
        Sign out
      </Button>
    </div>
  )
}

function HealthBadge({ health }: { health: HealthState }) {
  if (health.kind === 'loading') {
    return <Spinner size="tiny" label="Checking local service" />
  }

  return health.kind === 'online' ? (
    <Badge appearance="filled" color="success">
      Local service online
    </Badge>
  ) : (
    <Badge appearance="filled" color="danger">
      Local service unavailable
    </Badge>
  )
}
