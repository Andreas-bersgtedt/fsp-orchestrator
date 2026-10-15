import {
  Badge,
  Button,
  Card,
  CardFooter,
  CardHeader,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Select,
  Spinner,
  Text,
  Title2,
} from '@fluentui/react-components'
import { useCallback, useEffect, useState } from 'react'

const armScope = 'https://management.azure.com/user_impersonation'

type AzureSession = {
  tenantId: string
  objectId: string
  expiresAt: string
}

type Subscription = {
  subscriptionId: string
  displayName: string
  state: string
  resourceId: string
  apiVersion: string
}

type Resource = {
  resourceId: string
  name: string
  type: string
  location?: string
  apiVersion: string
}

export function AzureDiscovery({
  acquireAccessToken,
  apiScope,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
}) {
  const [session, setSession] = useState<AzureSession>()
  const [subscriptions, setSubscriptions] = useState<Subscription[]>([])
  const [subscriptionId, setSubscriptionId] = useState('')
  const [resourceGroup, setResourceGroup] = useState('')
  const [resources, setResources] = useState<Resource[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  const apiFetch = useCallback(
    async (path: string, init?: RequestInit) => {
      const apiToken = await acquireAccessToken([apiScope])
      return fetch(path, {
        ...init,
        headers: {
          Accept: 'application/json',
          Authorization: ['Bearer', apiToken].join(' '),
          ...init?.headers,
        },
      })
    },
    [acquireAccessToken, apiScope],
  )

  const loadSubscriptions = useCallback(async () => {
    const response = await apiFetch('/api/azure/subscriptions')
    if (!response.ok) {
      await throwProblem(response)
    }

    const result = (await response.json()) as {
      subscriptions: Subscription[]
      tokenExpiresAt: string
    }
    setSubscriptions(result.subscriptions)
    setSubscriptionId((current) =>
      result.subscriptions.some(
        (subscription) => subscription.subscriptionId === current,
      )
        ? current
        : result.subscriptions[0]?.subscriptionId ?? '',
    )
  }, [apiFetch])

  useEffect(() => {
    const controller = new AbortController()
    void apiFetch('/api/azure/session', { signal: controller.signal })
      .then(async (response) => {
        if (response.status === 401) {
          return
        }
        if (!response.ok) {
          await throwProblem(response)
        }
        setSession((await response.json()) as AzureSession)
        await loadSubscriptions()
      })
      .catch((sessionError) => {
        if (
          sessionError instanceof DOMException &&
          sessionError.name === 'AbortError'
        ) {
          return
        }
        setError(errorMessage(sessionError))
      })
    return () => controller.abort()
  }, [apiFetch, loadSubscriptions])

  async function connect() {
    setBusy(true)
    setError(undefined)
    try {
      const armToken = await acquireAccessToken([armScope])
      const response = await apiFetch('/api/azure/session', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ accessToken: armToken }),
      })
      if (!response.ok) {
        await throwProblem(response)
      }

      setSession((await response.json()) as AzureSession)
      await loadSubscriptions()
    } catch (connectError) {
      setError(errorMessage(connectError))
    } finally {
      setBusy(false)
    }
  }

  async function disconnect() {
    setBusy(true)
    setError(undefined)
    try {
      const response = await apiFetch('/api/azure/session', {
        method: 'DELETE',
      })
      if (!response.ok) {
        await throwProblem(response)
      }
      setSession(undefined)
      setSubscriptions([])
      setSubscriptionId('')
      setResources([])
    } catch (disconnectError) {
      setError(errorMessage(disconnectError))
    } finally {
      setBusy(false)
    }
  }

  async function discoverResources() {
    if (!subscriptionId) {
      return
    }

    setBusy(true)
    setError(undefined)
    try {
      const query = new URLSearchParams({ subscriptionId })
      if (resourceGroup.trim()) {
        query.set('resourceGroup', resourceGroup.trim())
      }
      const response = await apiFetch(`/api/azure/resources?${query}`)
      if (!response.ok) {
        await throwProblem(response)
      }
      const result = (await response.json()) as { resources: Resource[] }
      setResources(result.resources)
    } catch (discoveryError) {
      if (
        discoveryError instanceof Error &&
        discoveryError.message === 'azure.sign_in_required'
      ) {
        setSession(undefined)
      }
      setError(errorMessage(discoveryError))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section
      id="azure-discovery"
      className="azure-discovery"
      aria-labelledby="azure-discovery-heading"
    >
      <div className="section-heading">
        <div>
          <Title2 id="azure-discovery-heading">Azure scope discovery</Title2>
          <Text>
            Use your delegated identity to list subscriptions and resources.
          </Text>
        </div>
        <Badge appearance="outline">Requires DeploymentAdmin</Badge>
      </div>

      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Azure discovery failed</MessageBarTitle>
            {error === 'azure.sign_in_required'
              ? 'Azure sign-in expired. Connect again to resume.'
              : error}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {!session ? (
        <Card>
          <CardHeader
            header={<Text weight="semibold">No delegated Azure session</Text>}
            description={
              <Text>
                The ARM token stays in service memory and is never written to
                SQLite.
              </Text>
            }
          />
          <CardFooter>
            <Button
              appearance="primary"
              disabled={busy}
              onClick={() => void connect()}
            >
              {busy ? <Spinner size="tiny" /> : null}
              Connect Azure
            </Button>
          </CardFooter>
        </Card>
      ) : (
        <Card>
          <CardHeader
            header={<Text weight="semibold">Delegated Azure session active</Text>}
            description={
              <Text>
                Expires{' '}
                <time dateTime={session.expiresAt}>
                  {new Date(session.expiresAt).toLocaleString()}
                </time>
              </Text>
            }
          />
          <div className="azure-discovery-fields">
            <Field label="Subscription">
              <Select
                value={subscriptionId}
                onChange={(_, data) => setSubscriptionId(data.value)}
              >
                {subscriptions.map((subscription) => (
                  <option
                    key={subscription.subscriptionId}
                    value={subscription.subscriptionId}
                  >
                    {subscription.displayName}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Resource group">
              <Input
                value={resourceGroup}
                maxLength={90}
                onChange={(_, data) => setResourceGroup(data.value)}
                placeholder="Optional"
              />
            </Field>
          </div>
          <CardFooter>
            <Button
              appearance="primary"
              disabled={busy || !subscriptionId}
              onClick={() => void discoverResources()}
            >
              {busy ? <Spinner size="tiny" /> : null}
              List resources
            </Button>
            <Button
              appearance="secondary"
              disabled={busy}
              onClick={() => void disconnect()}
            >
              Disconnect Azure
            </Button>
          </CardFooter>
        </Card>
      )}

      {resources.length > 0 ? (
        <div className="azure-resource-list" aria-label="Azure resources">
          {resources.map((resource) => (
            <Card key={resource.resourceId}>
              <CardHeader
                header={<Text weight="semibold">{resource.name}</Text>}
                description={<Text>{resource.type}</Text>}
              />
              <Text>{resource.location ?? 'Global'}</Text>
              <Text size={200}>{resource.resourceId}</Text>
              <Badge appearance="outline">{resource.apiVersion}</Badge>
            </Card>
          ))}
        </div>
      ) : null}
    </section>
  )
}

async function throwProblem(response: Response): Promise<never> {
  const problem = (await response.json()) as {
    code?: string
    detail?: string
  }
  throw new Error(problem.code ?? problem.detail ?? `Request failed: ${response.status}`)
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Azure discovery failed.'
}
