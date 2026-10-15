import {
  Badge,
  Button,
  Card,
  CardFooter,
  CardHeader,
  Link,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Title2,
  Title3,
} from '@fluentui/react-components'
import {
  type ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from 'react'
import {
  deploymentDisplayStatus,
  formatTime,
  statusAnnouncement,
} from './deploymentStatus.ts'
import type {
  DeploymentListItem,
  DeploymentListResponse,
  ObservationState,
} from './deploymentTypes.ts'

type DeploymentJob = {
  id: string
  kind: string
  status: string
  createdAt: string
  updatedAt: string
  version: number
}

export function DeploymentInventory({
  acquireAccessToken,
  apiScope,
  canRefresh,
  refreshKey,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
  canRefresh: boolean
  refreshKey: number
}) {
  const [inventory, setInventory] = useState<DeploymentListResponse>()
  const [selectedId, setSelectedId] = useState<string>()
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [jobs, setJobs] = useState<DeploymentJob[]>([])
  const [error, setError] = useState<string>()

  const apiFetch = useCallback(
    async (path: string, init?: RequestInit) => {
      const token = await acquireAccessToken([apiScope])
      return fetch(path, {
        ...init,
        headers: {
          Accept: 'application/json',
          Authorization: ['Bearer', token].join(' '),
          ...init?.headers,
        },
      })
    },
    [acquireAccessToken, apiScope],
  )

  const load = useCallback(async () => {
    setError(undefined)
    const response = await apiFetch('/api/deployments', { cache: 'no-store' })
    if (!response.ok) {
      throw new Error(`Deployment inventory returned ${response.status}.`)
    }

    const next = (await response.json()) as DeploymentListResponse
    setInventory(next)
    setSelectedId((current) =>
      next.deployments.some((item) => item.deployment.id === current)
        ? current
        : next.deployments[0]?.deployment.id,
    )
  }, [apiFetch])

  useEffect(() => {
    let cancelled = false
    const timeout = globalThis.setTimeout(() => {
      setLoading(true)
      void load()
        .catch((loadError) => {
          if (!cancelled) {
            setError(errorMessage(loadError))
          }
        })
        .finally(() => {
          if (!cancelled) {
            setLoading(false)
          }
        })
    }, 0)
    return () => {
      cancelled = true
      globalThis.clearTimeout(timeout)
    }
  }, [load, refreshKey])

  const selected = useMemo(
    () =>
      inventory?.deployments.find(
        (item) => item.deployment.id === selectedId,
      ),
    [inventory, selectedId],
  )

  useEffect(() => {
    if (!selectedId) {
      return
    }

    const controller = new AbortController()
    void apiFetch(`/api/deployments/${selectedId}/jobs`, {
      cache: 'no-store',
      signal: controller.signal,
    })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`Job reports returned ${response.status}.`)
        }
        const result = (await response.json()) as { jobs: DeploymentJob[] }
        setJobs(result.jobs)
      })
      .catch((jobError) => {
        if (jobError instanceof DOMException && jobError.name === 'AbortError') {
          return
        }
        setError(errorMessage(jobError))
      })
    return () => controller.abort()
  }, [apiFetch, selectedId])

  async function refresh(item: DeploymentListItem) {
    setRefreshing(true)
    setError(undefined)
    try {
      const response = await apiFetch(
        `/api/deployments/${item.deployment.id}/observations`,
        { method: 'POST' },
      )
      if (!response.ok) {
        throw new Error(`Observation refresh returned ${response.status}.`)
      }

      const observation = (await response.json()) as ObservationState
      setInventory((current) =>
        current
          ? {
              ...current,
              checkedAt: new Date().toISOString(),
              deployments: current.deployments.map((candidate) =>
                candidate.deployment.id === item.deployment.id
                  ? { ...candidate, observation }
                  : candidate,
              ),
            }
          : current,
      )
    } catch (refreshError) {
      setError(errorMessage(refreshError))
    } finally {
      setRefreshing(false)
    }
  }

  const staleAfterSeconds = inventory?.staleAfterSeconds ?? 120

  return (
    <section
      id="deployment-inventory"
      className="deployment-inventory"
      aria-labelledby="deployment-inventory-heading"
    >
      <div className="section-heading">
        <div>
          <Title2 id="deployment-inventory-heading">Deployment inventory</Title2>
          <Text>
            Current state and the last successful observation remain separate.
          </Text>
        </div>
        <Button appearance="secondary" onClick={() => void load()}>
          Reload inventory
        </Button>
      </div>

      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Inventory request failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {loading ? <Spinner label="Loading deployment inventory" /> : null}

      {!loading && inventory?.deployments.length === 0 ? (
        <MessageBar>
          <MessageBarBody>
            <MessageBarTitle>No deployments registered</MessageBarTitle>
            Use the registration form to verify and add an FSP endpoint.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {inventory && inventory.deployments.length > 0 ? (
        <div className="inventory-layout">
          <div className="inventory-list" aria-label="Registered deployments">
            {inventory.deployments.map((item) => {
              const status = deploymentDisplayStatus(
                item,
                staleAfterSeconds,
              )
              return (
                <Button
                  key={item.deployment.id}
                  className="inventory-item"
                  appearance={
                    selectedId === item.deployment.id ? 'primary' : 'secondary'
                  }
                  aria-pressed={selectedId === item.deployment.id}
                  aria-label={`Open ${item.deployment.name}, status ${status}`}
                  onClick={() => setSelectedId(item.deployment.id)}
                >
                  <span>
                    <strong>{item.deployment.name}</strong>
                    <small>{item.deployment.topology}</small>
                  </span>
                  <Badge appearance="outline">{status}</Badge>
                </Button>
              )
            })}
          </div>

          {selected ? (
            <DeploymentDetail
              item={selected}
              staleAfterSeconds={staleAfterSeconds}
              canRefresh={canRefresh}
              refreshing={refreshing}
              jobs={jobs}
              onRefresh={() => void refresh(selected)}
            />
          ) : null}
        </div>
      ) : null}
    </section>
  )
}

function DeploymentDetail({
  item,
  staleAfterSeconds,
  canRefresh,
  refreshing,
  jobs,
  onRefresh,
}: {
  item: DeploymentListItem
  staleAfterSeconds: number
  canRefresh: boolean
  refreshing: boolean
  jobs: DeploymentJob[]
  onRefresh: () => void
}) {
  const status = deploymentDisplayStatus(item, staleAfterSeconds)
  const latest = item.observation?.latestAttempt
  const values = item.observation?.lastSuccessfulValues ?? []
  const activeJob = jobs.find(
    (job) => job.status === 'Pending' || job.status === 'Running',
  )
  const jobsId = `deployment-jobs-${item.deployment.id}`

  return (
    <Card className="deployment-detail">
      <CardHeader
        header={<Title3>{item.deployment.name}</Title3>}
        description={<Text>{item.deployment.managerEndpoint}</Text>}
      />
      <div
        className="deployment-status-announcement"
        role="status"
        aria-live="polite"
        aria-atomic="true"
      >
        {statusAnnouncement(item, staleAfterSeconds)}
      </div>
      <dl className="deployment-metadata">
        <Metadata label="Topology" value={item.deployment.topology} />
        <Metadata
          label="Azure scope"
          value={item.deployment.azureResourceId ?? 'Not linked'}
        />
        <Metadata label="Current state" value={status} />
        <Metadata
          label="Latest collection"
          value={
            latest ? (
              <time dateTime={latest.completedAt}>
                {formatTime(latest.completedAt)}
              </time>
            ) : (
              'Not collected'
            )
          }
        />
        <Metadata
          label="Last successful collection"
          value={
            item.observation?.lastSuccessfulAt ? (
              <time dateTime={item.observation.lastSuccessfulAt}>
                {formatTime(item.observation.lastSuccessfulAt)}
              </time>
            ) : (
              'No successful collection'
            )
          }
        />
        <Metadata
          label="Active job"
          value={
            activeJob ? `${activeJob.kind}: ${activeJob.status}` : 'None'
          }
        />
        <Metadata label="Drift" value="Not evaluated" />
        <Metadata label="Backup" value="Not configured" />
        <Metadata label="Upgrade" value="Not evaluated" />
      </dl>

      <div className="observation-values" aria-label="Last successful observations">
        {values.length === 0 ? (
          <Text>No successful observation values are available.</Text>
        ) : (
          values.map((value) => (
            <div key={value.name}>
              <Text weight="semibold">{value.name}</Text>
              <Text>{value.version}</Text>
              <Badge appearance="outline">{value.status}</Badge>
            </div>
          ))
        )}
      </div>

      <div
        id={jobsId}
        className="deployment-job-reports"
        aria-label="Job reports"
      >
        <Text weight="semibold">Job reports</Text>
        {jobs.length === 0 ? (
          <Text>No jobs recorded for this deployment.</Text>
        ) : (
          jobs.map((job) => (
            <div key={job.id}>
              <Text>{job.kind}</Text>
              <Badge appearance="outline">{job.status}</Badge>
              <time dateTime={job.updatedAt}>{formatTime(job.updatedAt)}</time>
            </div>
          ))
        )}
      </div>

      <CardFooter>
        <Button
          appearance="primary"
          disabled={!canRefresh || refreshing}
          onClick={onRefresh}
        >
          {refreshing ? <Spinner size="tiny" /> : null}
          Refresh now
        </Button>
        <Link href="#prerequisites">Prerequisite reports</Link>
        <Link href={`#${jobsId}`}>Job reports</Link>
      </CardFooter>
    </Card>
  )
}

function Metadata({
  label,
  value,
}: {
  label: string
  value: ReactNode
}) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Inventory request failed.'
}
