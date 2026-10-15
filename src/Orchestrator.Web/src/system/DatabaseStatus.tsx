import {
  Badge,
  Button,
  Card,
  CardFooter,
  CardHeader,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Title2,
} from '@fluentui/react-components'
import { useCallback, useEffect, useState } from 'react'

type Diagnostics = {
  databaseFileName: string
  integrity: string
  migration: string
  deploymentCount: number
  jobCount: number
  auditEventCount: number
  checkedAt: string
}

type Backup = {
  fileName: string
  sha256: string
  length: number
  createdAt: string
}

export function DatabaseStatus({
  acquireAccessToken,
  apiScope,
  canManage,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
  canManage: boolean
}) {
  const [diagnostics, setDiagnostics] = useState<Diagnostics>()
  const [backup, setBackup] = useState<Backup>()
  const [status, setStatus] = useState<'loading' | 'idle' | 'backup' | 'restore'>(
    'loading',
  )
  const [error, setError] = useState<string>()

  const load = useCallback(async () => {
    try {
      const token = await acquireAccessToken([apiScope])
      const response = await fetch('/api/system/database', {
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${token}`,
        },
      })
      if (!response.ok) {
        throw new Error(`Database diagnostics returned ${response.status}.`)
      }
      setDiagnostics((await response.json()) as Diagnostics)
      setError(undefined)
    } catch (loadError) {
      setError(errorMessage(loadError))
    } finally {
      setStatus('idle')
    }
  }, [acquireAccessToken, apiScope])

  useEffect(() => {
    const timeout = globalThis.setTimeout(() => void load(), 0)
    return () => globalThis.clearTimeout(timeout)
  }, [load])

  async function createBackup() {
    setStatus('backup')
    try {
      const token = await acquireAccessToken([apiScope])
      const response = await fetch('/api/system/database/backups', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${token}`,
        },
      })
      if (!response.ok) {
        throw new Error(`Database backup returned ${response.status}.`)
      }
      setBackup((await response.json()) as Backup)
      await load()
    } catch (backupError) {
      setError(errorMessage(backupError))
    } finally {
      setStatus('idle')
    }
  }

  async function restoreBackup() {
    if (!backup) {
      return
    }

    setStatus('restore')
    try {
      const token = await acquireAccessToken([apiScope])
      const response = await fetch('/api/system/database/restore', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          fileName: backup.fileName,
          sha256: backup.sha256,
        }),
      })
      if (!response.ok) {
        throw new Error(`Database restore returned ${response.status}.`)
      }
      await load()
    } catch (restoreError) {
      setError(errorMessage(restoreError))
    } finally {
      setStatus('idle')
    }
  }

  return (
    <section className="database-status" aria-labelledby="database-heading">
      <div className="section-heading">
        <div>
          <Title2 id="database-heading">Local metadata database</Title2>
          <Text>
            Migration, integrity, and non-secret inventory/job/audit counts.
          </Text>
        </div>
        {diagnostics ? (
          <Badge
            appearance="filled"
            color={diagnostics.integrity === 'ok' ? 'success' : 'danger'}
          >
            Integrity: {diagnostics.integrity}
          </Badge>
        ) : null}
      </div>

      {status === 'loading' ? <Spinner label="Reading database diagnostics" /> : null}
      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Database operation failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}
      {diagnostics ? (
        <Card>
          <CardHeader
            header={<Text weight="semibold">{diagnostics.databaseFileName}</Text>}
            description={<Text>{diagnostics.migration}</Text>}
          />
          <div className="database-counts">
            <Text>Deployments: {diagnostics.deploymentCount}</Text>
            <Text>Jobs: {diagnostics.jobCount}</Text>
            <Text>Audit events: {diagnostics.auditEventCount}</Text>
          </div>
          {canManage ? (
            <CardFooter>
              <Button
                disabled={status !== 'idle'}
                onClick={() => void createBackup()}
              >
                Create backup
              </Button>
              <Button
                disabled={!backup || status !== 'idle'}
                onClick={() => void restoreBackup()}
              >
                Restore latest backup
              </Button>
            </CardFooter>
          ) : null}
        </Card>
      ) : null}
      {backup ? (
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>Backup verified</MessageBarTitle>
            {backup.fileName} · {backup.length} bytes · SHA-256 {backup.sha256}
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </section>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'The database operation failed.'
}
