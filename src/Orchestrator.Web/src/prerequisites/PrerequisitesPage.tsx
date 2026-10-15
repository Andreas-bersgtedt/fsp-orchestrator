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
  Textarea,
  Title2,
} from '@fluentui/react-components'
import { useCallback, useEffect, useMemo, useState } from 'react'

type CheckDefinition = {
  id: string
  category: string
  displayName: string
  description: string
  dependencies: string[]
  operations: string[]
  timeoutMilliseconds: number
  lifetimeSeconds: number
  blocking: boolean
}

type CheckResult = {
  checkId: string
  category: string
  blocking: boolean
  status: string
  code: string
  summary: string
  evidence: Record<string, string>
  remediation: string
  completedAt?: string
  expiresAt?: string
  durationMilliseconds: number
  warningAcceptance?: {
    actorObjectId: string
    reason: string
    acceptedAt: string
  }
}

type Run = {
  id: string
  operation: string
  status: string
  createdAt: string
  startedAt?: string
  completedAt?: string
  results: CheckResult[]
}

export function PrerequisitesPage({
  acquireAccessToken,
  apiScope,
  canRun,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
  canRun: boolean
}) {
  const [catalog, setCatalog] = useState<CheckDefinition[]>([])
  const [run, setRun] = useState<Run>()
  const [operation, setOperation] = useState('setup')
  const [target, setTarget] = useState('local')
  const [category, setCategory] = useState('all')
  const [statusFilter, setStatusFilter] = useState('all')
  const [warningReasons, setWarningReasons] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  const apiFetch = useCallback(
    async (path: string, init?: RequestInit) => {
      const token = await acquireAccessToken([apiScope])
      return fetch(path, {
        ...init,
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${token}`,
          ...init?.headers,
        },
      })
    },
    [acquireAccessToken, apiScope],
  )

  useEffect(() => {
    const timeout = globalThis.setTimeout(
      () =>
        void apiFetch('/api/prerequisites/catalog')
          .then(async (response) => {
            if (!response.ok) {
              throw new Error(`Prerequisite catalog returned ${response.status}.`)
            }
            const data = (await response.json()) as { checks: CheckDefinition[] }
            setCatalog(data.checks)
          })
          .catch((loadError) => setError(errorMessage(loadError))),
      0,
    )
    return () => globalThis.clearTimeout(timeout)
  }, [apiFetch])

  useEffect(() => {
    if (!run || (run.status !== 'Pending' && run.status !== 'Running')) {
      return
    }

    const runId = run.id
    let cancelled = false
    let timeout: number | undefined
    async function poll() {
      try {
        const response = await apiFetch(`/api/prerequisites/runs/${runId}`, {
          cache: 'no-store',
        })
        if (!response.ok) {
          throw new Error(`Prerequisite run returned ${response.status}.`)
        }
        const next = (await response.json()) as Run
        if (!cancelled) {
          setRun(next)
          if (next.status === 'Pending' || next.status === 'Running') {
            timeout = globalThis.setTimeout(() => void poll(), 500)
          }
        }
      } catch (pollError) {
        if (!cancelled) {
          setError(errorMessage(pollError))
        }
      }
    }

    void poll()
    return () => {
      cancelled = true
      if (timeout) {
        globalThis.clearTimeout(timeout)
      }
    }
  }, [apiFetch, run])

  const categories = useMemo(
    () => [...new Set(catalog.map((check) => check.category))].sort(),
    [catalog],
  )
  const visibleResults = useMemo(
    () =>
      (run?.results ?? []).filter(
        (result) =>
          (category === 'all' || result.category === category) &&
          (statusFilter === 'all' || result.status === statusFilter),
      ),
    [category, run, statusFilter],
  )
  const counts = useMemo(
    () =>
      (run?.results ?? []).reduce<Record<string, number>>((current, result) => {
        current[result.status] = (current[result.status] ?? 0) + 1
        return current
      }, {}),
    [run],
  )

  async function start(checkIds?: string[]) {
    setBusy(true)
    setError(undefined)
    try {
      const response = await apiFetch('/api/prerequisites/runs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          operation,
          inputs: { target },
          checkIds,
        }),
      })
      if (!response.ok) {
        throw new Error(`Starting prerequisite run returned ${response.status}.`)
      }
      const data = (await response.json()) as { runId: string }
      const runResponse = await apiFetch(`/api/prerequisites/runs/${data.runId}`)
      setRun((await runResponse.json()) as Run)
    } catch (startError) {
      setError(errorMessage(startError))
    } finally {
      setBusy(false)
    }
  }

  async function cancel() {
    if (!run) {
      return
    }
    await apiFetch(`/api/prerequisites/runs/${run.id}/cancel`, {
      method: 'POST',
    })
  }

  async function acceptWarning(checkId: string) {
    if (!run) {
      return
    }
    const response = await apiFetch(
      `/api/prerequisites/runs/${run.id}/results/${encodeURIComponent(checkId)}/accept-warning`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: warningReasons[checkId] ?? '' }),
      },
    )
    if (!response.ok) {
      setError(`Warning acceptance returned ${response.status}.`)
      return
    }
    const refreshed = await apiFetch(`/api/prerequisites/runs/${run.id}`)
    setRun((await refreshed.json()) as Run)
  }

  async function exportReport() {
    if (!run) {
      return
    }
    const response = await apiFetch(
      `/api/prerequisites/runs/${run.id}/report`,
    )
    if (!response.ok) {
      setError(`Report export returned ${response.status}.`)
      return
    }
    const blob = new Blob(
      [JSON.stringify(await response.json(), null, 2)],
      { type: 'application/json' },
    )
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `prerequisite-run-${run.id}.json`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  return (
    <section id="prerequisites" className="prerequisites-page">
      <div className="section-heading">
        <div>
          <Title2>Prerequisites</Title2>
          <Text>
            Run typed checks, inspect safe evidence, and validate operation inputs.
          </Text>
        </div>
        <div className="prerequisite-actions">
          <Button
            appearance="primary"
            disabled={!canRun || busy}
            onClick={() => void start()}
          >
            Run all checks
          </Button>
          <Button
            disabled={!run}
            onClick={() => void exportReport()}
          >
            Export JSON
          </Button>
        </div>
      </div>

      <div className="prerequisite-inputs">
        <Field label="Operation">
          <Input value={operation} onChange={(_, data) => setOperation(data.value)} />
        </Field>
        <Field label="Target fingerprint input">
          <Input value={target} onChange={(_, data) => setTarget(data.value)} />
        </Field>
        <Field label="Category filter">
          <Select value={category} onChange={(_, data) => setCategory(data.value)}>
            <option value="all">All categories</option>
            {categories.map((value) => (
              <option key={value} value={value}>{value}</option>
            ))}
          </Select>
        </Field>
        <Field label="Status filter">
          <Select
            value={statusFilter}
            onChange={(_, data) => setStatusFilter(data.value)}
          >
            <option value="all">All statuses</option>
            {['Pending', 'Running', 'Passed', 'Warning', 'Failed', 'Skipped', 'Unknown', 'Cancelled']
              .map((value) => <option key={value} value={value}>{value}</option>)}
          </Select>
        </Field>
      </div>

      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Prerequisite operation failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {catalog.length === 0 ? (
        <MessageBar intent="info">
          <MessageBarBody>
            <MessageBarTitle>No checkers registered yet</MessageBarTitle>
            The engine is ready. #21 adds the Windows host and Entra checker set.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {run ? (
        <>
          <Card>
            <CardHeader
              header={<Text weight="semibold">Run {run.id}</Text>}
              description={
                <Text>
                  {run.status} · {age(run.completedAt ?? run.createdAt)}
                </Text>
              }
            />
            <CardFooter className="status-counts">
              {Object.entries(counts).map(([name, count]) => (
                <Badge key={name} appearance="outline">
                  {name}: {count}
                </Badge>
              ))}
              {run.status === 'Pending' || run.status === 'Running' ? (
                <Button onClick={() => void cancel()}>Cancel run</Button>
              ) : (
                <Button
                  disabled={!canRun}
                  onClick={() => void start(run.results.map((result) => result.checkId))}
                >
                  Rerun complete
                </Button>
              )}
            </CardFooter>
          </Card>

          <div className="prerequisite-results">
            {visibleResults.map((result) => (
              <Card key={result.checkId}>
                <CardHeader
                  header={
                    <div className="result-title">
                      <Text weight="semibold">{result.checkId}</Text>
                      <Badge appearance="filled" color={statusColor(result.status)}>
                        {result.status}
                      </Badge>
                    </div>
                  }
                  description={
                    <Text>
                      {result.summary} · {result.durationMilliseconds} ms ·{' '}
                      {result.expiresAt ? `expires ${age(result.expiresAt)}` : 'not complete'}
                    </Text>
                  }
                />
                {Object.keys(result.evidence).length > 0 ? (
                  <details>
                    <summary>Safe evidence</summary>
                    <dl>
                      {Object.entries(result.evidence).map(([key, value]) => (
                        <div key={key}>
                          <dt>{key}</dt>
                          <dd>{value}</dd>
                        </div>
                      ))}
                    </dl>
                  </details>
                ) : null}
                {result.remediation ? (
                  <Text>Remediation: {result.remediation}</Text>
                ) : null}
                {result.status === 'Warning' && !result.warningAcceptance ? (
                  <div className="warning-acceptance">
                    <Textarea
                      aria-label={`Reason for ${result.checkId}`}
                      placeholder="Written acceptance reason"
                      value={warningReasons[result.checkId] ?? ''}
                      onChange={(_, data) =>
                        setWarningReasons((current) => ({
                          ...current,
                          [result.checkId]: data.value,
                        }))
                      }
                    />
                    <Button
                      disabled={
                        !canRun ||
                        !(warningReasons[result.checkId] ?? '').trim()
                      }
                      onClick={() => void acceptWarning(result.checkId)}
                    >
                      Accept warning
                    </Button>
                  </div>
                ) : null}
                {result.warningAcceptance ? (
                  <Badge appearance="outline" color="warning">
                    Accepted: {result.warningAcceptance.reason}
                  </Badge>
                ) : null}
                <CardFooter>
                  <Button
                    disabled={!canRun}
                    onClick={() => void start([result.checkId])}
                  >
                    Rerun check
                  </Button>
                  <Button
                    disabled={!canRun}
                    onClick={() =>
                      void start(
                        catalog
                          .filter((check) => check.category === result.category)
                          .map((check) => check.id),
                      )
                    }
                  >
                    Rerun category
                  </Button>
                </CardFooter>
              </Card>
            ))}
          </div>
        </>
      ) : busy ? (
        <Spinner label="Starting prerequisite run" />
      ) : null}
    </section>
  )
}

function age(value: string) {
  const milliseconds = Date.now() - new Date(value).getTime()
  if (milliseconds < 0) {
    return `in ${Math.ceil(Math.abs(milliseconds) / 1000)}s`
  }
  if (milliseconds < 60_000) {
    return `${Math.floor(milliseconds / 1000)}s ago`
  }
  return `${Math.floor(milliseconds / 60_000)}m ago`
}

function statusColor(status: string) {
  if (status === 'Passed') return 'success'
  if (status === 'Warning') return 'warning'
  if (status === 'Failed' || status === 'Unknown') return 'danger'
  return 'informative'
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'The prerequisite operation failed.'
}
