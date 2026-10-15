import {
  Badge,
  Button,
  Card,
  CardFooter,
  CardHeader,
  Field,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Select,
  Spinner,
  Text,
  Title2,
} from '@fluentui/react-components'
import { PublicClientApplication } from '@azure/msal-browser'
import { useEffect, useMemo, useState } from 'react'
import type {
  BootstrapAuthenticationConfiguration,
  BootstrapOperatorAccount,
} from '../auth/authTypes.ts'
import {
  discoverOwnedApplications,
  servicePrincipalExists,
  type OwnedEntraApplication,
} from './graphDiscovery.ts'
import {
  createRecommendedRegistrations,
  type CreatedRegistrationPair,
} from './graphRegistrationCreation.ts'

type CommitResponse = {
  committed: boolean
  restartRequired: boolean
  message: string
}

export function BootstrapSetup({
  account,
  acquireAccessToken,
  bootstrap,
}: {
  account: BootstrapOperatorAccount
  acquireAccessToken: (scopes: string[]) => Promise<string>
  bootstrap: BootstrapAuthenticationConfiguration
}) {
  const [applications, setApplications] = useState<OwnedEntraApplication[]>([])
  const [spaAppId, setSpaAppId] = useState('')
  const [apiAppId, setApiAppId] = useState('')
  const [createdPair, setCreatedPair] = useState<CreatedRegistrationPair>()
  const [candidateClientState, setCandidateClientState] = useState<{
    key: string
    client: PublicClientApplication
  }>()
  const [commit, setCommit] = useState<CommitResponse>()
  const [discoveryCompleted, setDiscoveryCompleted] = useState(false)
  const [status, setStatus] = useState<
    'idle' | 'discovering' | 'creating' | 'committing'
  >('idle')
  const [error, setError] = useState<string>()
  const spa = useMemo(
    () => applications.find((application) => application.appId === spaAppId),
    [applications, spaAppId],
  )
  const api = useMemo(
    () => applications.find((application) => application.appId === apiAppId),
    [apiAppId, applications],
  )
  const candidateKey =
    spa && api && spa.appId !== api.appId
      ? `${account.tenantId}:${spa.appId}:${api.appId}`
      : undefined
  const candidateClient =
    candidateClientState && candidateClientState.key === candidateKey
      ? candidateClientState.client
      : undefined

  useEffect(() => {
    let active = true
    if (!spa || !candidateKey) {
      return () => {
        active = false
      }
    }

    const client = new PublicClientApplication({
      auth: {
        authority: `https://login.microsoftonline.com/${account.tenantId}`,
        clientId: spa.appId,
        redirectUri: 'https://localhost:9443/auth/callback',
      },
      cache: {
        cacheLocation: 'sessionStorage',
      },
    })
    void client.initialize().then(() => {
      if (active) {
        setCandidateClientState({ key: candidateKey, client })
      }
    })
    return () => {
      active = false
    }
  }, [account.tenantId, candidateKey, spa])

  async function discover() {
    await run('discovering', async () => {
      const token = await acquireAccessToken(bootstrap.discoveryScopes)
      const discovered = await discoverOwnedApplications(token)
      setApplications(discovered)
      setDiscoveryCompleted(true)
      setCreatedPair(undefined)
      setSpaAppId('')
      setApiAppId('')
    })
  }

  async function create() {
    await run('creating', async () => {
      const token = await acquireAccessToken(bootstrap.creationScopes)
      const me = await graphMe(token)
      const pair = await createRecommendedRegistrations(token, me.id)
      setCreatedPair(pair)
      setDiscoveryCompleted(true)
      setApplications([pair.spa, pair.api])
      setSpaAppId(pair.spa.appId)
      setApiAppId(pair.api.appId)
    })
  }

  async function commitConfiguration() {
    if (!spa || !api || !candidateClient) {
      return
    }

    await run('committing', async () => {
      const apiScope = `api://${api.appId}/orchestrator.access_as_user`
      const candidateTokenPromise = candidateClient.loginPopup({
        prompt: 'select_account',
        scopes: [apiScope],
      })
      const graphToken = await acquireAccessToken(bootstrap.creationScopes)
      const pair =
        createdPair ??
        ({
          spa,
          api,
          spaServicePrincipalExists: await servicePrincipalExists(
            spa.appId,
            graphToken,
          ),
          apiServicePrincipalExists: await servicePrincipalExists(
            api.appId,
            graphToken,
          ),
          signedInUserRoles: ['SecurityAdmin'],
        } satisfies CreatedRegistrationPair)
      const candidateToken = await candidateTokenPromise
      const response = await fetch('/api/bootstrap/identity/commit', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${candidateToken.accessToken}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          candidate: {
            tenantId: account.tenantId,
            spaClientId: spa.appId,
            apiClientId: api.appId,
            scopeName: 'orchestrator.access_as_user',
          },
          registration: {
            tenantId: account.tenantId,
            signedInTenantId: account.tenantId,
            spa: pair.spa,
            api: pair.api,
            spaServicePrincipalExists: pair.spaServicePrincipalExists,
            apiServicePrincipalExists: pair.apiServicePrincipalExists,
            signedInUserRoles: pair.signedInUserRoles,
          },
        }),
      })
      if (!response.ok) {
        throw new Error(`Identity configuration commit returned ${response.status}.`)
      }
      setCommit((await response.json()) as CommitResponse)
      await waitForRestart()
    })
  }

  async function run(
    nextStatus: typeof status,
    action: () => Promise<void>,
  ) {
    setStatus(nextStatus)
    setError(undefined)
    try {
      await action()
    } catch (operationError) {
      setError(errorMessage(operationError))
    } finally {
      setStatus('idle')
    }
  }

  return (
    <section className="registration-setup" aria-labelledby="bootstrap-heading">
      <div className="section-heading">
        <div>
          <Title2 id="bootstrap-heading">Configure Microsoft Entra</Title2>
          <Text>
            Select registrations you own or create the recommended credential-free
            SPA/API pair as {account.username}.
          </Text>
        </div>
        <Badge appearance="outline">Bootstrap PKCE session</Badge>
      </div>

      <div className="bootstrap-actions">
        <Button
          appearance="secondary"
          disabled={status !== 'idle'}
          onClick={() => void discover()}
        >
          Discover owned registrations
        </Button>
        <Button
          appearance="primary"
          disabled={status !== 'idle'}
          onClick={() => void create()}
        >
          Create recommended registrations
        </Button>
      </div>

      {status !== 'idle' ? <Spinner label={`${status} Entra configuration`} /> : null}
      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Entra setup failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {applications.length > 0 ? (
        <Card>
          <CardHeader
            header={<Text weight="semibold">Candidate registrations</Text>}
            description={
              <Text>
                The final commit requires a fresh candidate API token. Graph tokens
                are never accepted by the local API.
              </Text>
            }
          />
          <div className="registration-fields">
            <Field label="SPA registration">
              <Select
                value={spaAppId}
                onChange={(_, data) => setSpaAppId(data.value)}
              >
                <option value="">Select SPA</option>
                {applications.map((application) => (
                  <option key={application.appId} value={application.appId}>
                    {application.displayName} ({application.appId})
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="API registration">
              <Select
                value={apiAppId}
                onChange={(_, data) => setApiAppId(data.value)}
              >
                <option value="">Select API</option>
                {applications.map((application) => (
                  <option key={application.appId} value={application.appId}>
                    {application.displayName} ({application.appId})
                  </option>
                ))}
              </Select>
            </Field>
          </div>
          <CardFooter>
            <Button
              appearance="primary"
              disabled={
                !spa ||
                !api ||
                !candidateClient ||
                spa.appId === api.appId ||
                status !== 'idle'
              }
              onClick={() => void commitConfiguration()}
            >
              Validate and commit configuration
            </Button>
          </CardFooter>
        </Card>
      ) : null}

      {discoveryCompleted && applications.length === 0 ? (
        <MessageBar intent="info">
          <MessageBarBody>
            <MessageBarTitle>No owned registrations found</MessageBarTitle>
            Create the recommended credential-free SPA/API pair or ask an owner
            to add your account to existing registrations.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {commit ? (
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>Identity configuration committed</MessageBarTitle>
            {commit.message}
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </section>
  )
}

async function graphMe(accessToken: string) {
  const response = await fetch('https://graph.microsoft.com/v1.0/me?$select=id', {
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
  })
  if (!response.ok) {
    throw new Error(`Microsoft Graph user lookup returned ${response.status}.`)
  }
  return (await response.json()) as { id: string }
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
          globalThis.location.reload()
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

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'The bootstrap operation failed.'
}
