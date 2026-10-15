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
import { useMemo, useState } from 'react'
import type {
  BootstrapAuthenticationConfiguration,
  BootstrapOperatorAccount,
} from '../auth/authTypes.ts'
import { savePendingCandidateSetup } from '../auth/candidateSetup.ts'
import { clearStaleMsalInteraction } from '../auth/msalInteraction.ts'
import {
  discoverOwnedApplications,
  servicePrincipalExists,
  type OwnedEntraApplication,
} from './graphDiscovery.ts'
import {
  createRecommendedRegistrations,
  type CreatedRegistrationPair,
} from './graphRegistrationCreation.ts'

export function BootstrapSetup({
  account,
  acquireAccessToken,
  bootstrap,
  interactionInProgress,
}: {
  account: BootstrapOperatorAccount
  acquireAccessToken: (scopes: string[]) => Promise<string>
  bootstrap: BootstrapAuthenticationConfiguration
  interactionInProgress: boolean
}) {
  const [applications, setApplications] = useState<OwnedEntraApplication[]>([])
  const [spaAppId, setSpaAppId] = useState('')
  const [apiAppId, setApiAppId] = useState('')
  const [createdPair, setCreatedPair] = useState<CreatedRegistrationPair>()
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
    if (!spa || !api) {
      return
    }

    await run('committing', async () => {
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
      const apiScope = `api://${api.appId}/orchestrator.access_as_user`
      savePendingCandidateSetup({
        tenantId: account.tenantId,
        spaClientId: spa.appId,
        apiClientId: api.appId,
        apiScope,
        pair,
      })
      const candidate = new PublicClientApplication({
        auth: {
          authority: `https://login.microsoftonline.com/${account.tenantId}`,
          clientId: spa.appId,
          redirectUri: 'https://localhost:9443/auth/callback',
        },
        cache: {
          cacheLocation: 'sessionStorage',
        },
      })
      await candidate.initialize()
      clearStaleMsalInteraction()
      await candidate.loginRedirect({
        prompt: 'select_account',
        scopes: [apiScope],
      })
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
          disabled={status !== 'idle' || interactionInProgress}
          onClick={() => void discover()}
        >
          Discover owned registrations
        </Button>
        <Button
          appearance="primary"
          disabled={status !== 'idle' || interactionInProgress}
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
                spa.appId === api.appId ||
                status !== 'idle' ||
                interactionInProgress
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

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'The bootstrap operation failed.'
}
