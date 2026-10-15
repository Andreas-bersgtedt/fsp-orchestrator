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
  Spinner,
  Text,
  Title2,
} from '@fluentui/react-components'
import { useCallback, useEffect, useState } from 'react'
import {
  defaultDeploymentProfile,
  type DeploymentProfile,
  type DeploymentProfileDocument,
  type DeploymentProfileState,
  toPrerequisiteInputs,
} from './deploymentProfile.ts'

const armScope = 'https://management.azure.com/user_impersonation'

export function DeploymentProfilePage({
  acquireAccessToken,
  apiScope,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
}) {
  const [profile, setProfile] = useState<DeploymentProfile>(
    defaultDeploymentProfile,
  )
  const [profileId, setProfileId] = useState<string>()
  const [state, setState] = useState<DeploymentProfileState>()
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string>()
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
    const controller = new AbortController()
    void apiFetch('/api/deployment-profiles', { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          await throwProblem(response)
        }
        const documents =
          (await response.json()) as DeploymentProfileDocument[]
        const current = documents[0]
        if (!current) {
          return
        }
        const stateResponse = await apiFetch(
          `/api/deployment-profiles/${current.profile.id}`,
          { signal: controller.signal },
        )
        if (!stateResponse.ok) {
          await throwProblem(stateResponse)
        }
        const loaded = (await stateResponse.json()) as DeploymentProfileState
        setProfileId(loaded.document.profile.id)
        setProfile(loaded.document.profile)
        setState(loaded)
      })
      .catch((loadError) => {
        if (
          loadError instanceof DOMException &&
          loadError.name === 'AbortError'
        ) {
          return
        }
        setError(errorMessage(loadError))
      })
    return () => controller.abort()
  }, [apiFetch])

  function update<K extends keyof DeploymentProfile>(
    key: K,
    value: DeploymentProfile[K],
  ) {
    setProfile((current) => ({ ...current, [key]: value }))
    setStatus(undefined)
  }

  async function save(): Promise<DeploymentProfileState> {
    const id = profileId ?? crypto.randomUUID()
    const response = await apiFetch(`/api/deployment-profiles/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(profile),
    })
    if (!response.ok) {
      await throwProblem(response)
    }
    const saved = (await response.json()) as DeploymentProfileState
    setProfileId(id)
    setState(saved)
    return saved
  }

  async function saveOnly() {
    setBusy(true)
    setError(undefined)
    try {
      await save()
      setStatus(
        'Profile saved. Material changes invalidate the prior prerequisite run and approval.',
      )
    } catch (saveError) {
      setError(errorMessage(saveError))
    } finally {
      setBusy(false)
    }
  }

  async function preview() {
    setBusy(true)
    setError(undefined)
    setStatus('Saving the profile')
    try {
      const saved = await save()
      setStatus('Connecting the delegated Azure session')
      const armToken = await acquireAccessToken([armScope])
      const handoff = await apiFetch('/api/azure/session', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ accessToken: armToken }),
      })
      if (!handoff.ok) {
        await throwProblem(handoff)
      }

      setStatus('Running deployment prerequisites')
      const start = await apiFetch('/api/prerequisites/runs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          operation: 'deploy',
          inputs: toPrerequisiteInputs(saved.document.profile),
        }),
      })
      if (!start.ok) {
        await throwProblem(start)
      }
      const { runId } = (await start.json()) as { runId: string }
      await waitForPassingRun(runId, apiFetch, setStatus)

      setStatus('Running ARM validation and what-if')
      const response = await apiFetch(
        `/api/deployment-profiles/${saved.document.profile.id}/preview`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ prerequisiteRunId: runId }),
        },
      )
      if (!response.ok) {
        await throwProblem(response)
      }
      const refreshed = await apiFetch(
        `/api/deployment-profiles/${saved.document.profile.id}`,
      )
      if (!refreshed.ok) {
        await throwProblem(refreshed)
      }
      setState((await refreshed.json()) as DeploymentProfileState)
      setStatus('ARM validation and what-if completed')
    } catch (previewError) {
      setError(errorMessage(previewError))
      setStatus(undefined)
    } finally {
      setBusy(false)
    }
  }

  async function approve() {
    if (!profileId || !state?.preview) {
      return
    }
    setBusy(true)
    setError(undefined)
    try {
      const response = await apiFetch(
        `/api/deployment-profiles/${profileId}/approval`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ previewId: state.preview.id }),
        },
      )
      if (!response.ok) {
        await throwProblem(response)
      }
      const approval = (await response.json()) as DeploymentProfileState['approval']
      setState((current) =>
        current ? { ...current, approval } : current,
      )
      setStatus('The exact current what-if is approved')
    } catch (approvalError) {
      setError(errorMessage(approvalError))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section
      id="deployment-profile"
      className="deployment-profile"
      aria-labelledby="deployment-profile-heading"
    >
      <div className="section-heading">
        <div>
          <Title2 id="deployment-profile-heading">
            Azure deployment profile
          </Title2>
          <Text>
            Save typed input, run prerequisites, and approve the exact ARM
            what-if. No FSP checkout or deployment directory is used.
          </Text>
        </div>
        <Badge appearance="outline">Requires DeploymentAdmin</Badge>
      </div>

      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Deployment preview failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}
      {status ? (
        <MessageBar intent="info">
          <MessageBarBody>{status}</MessageBarBody>
        </MessageBar>
      ) : null}

      <Card>
        <CardHeader
          header={<Text weight="semibold">Typed deployment input</Text>}
          description={
            <Text>
              Names, Azure scope, SKU, network ranges, and optional existing
              resource IDs are validated before persistence.
            </Text>
          }
        />
        <div className="deployment-profile-fields">
          <ProfileField
            label="Profile name"
            value={profile.name}
            onChange={(value) => update('name', value)}
          />
          <ProfileField
            label="Subscription ID"
            value={profile.subscriptionId}
            onChange={(value) => update('subscriptionId', value)}
          />
          <ProfileField
            label="Resource group"
            value={profile.resourceGroupName}
            onChange={(value) => update('resourceGroupName', value)}
          />
          <ProfileField
            label="ARM deployment name"
            value={profile.deploymentName}
            onChange={(value) => update('deploymentName', value)}
          />
          <ProfileField
            label="Region"
            value={profile.location}
            onChange={(value) => {
              update('location', value)
              update(
                'privateDnsZoneName',
                `privatelink.${value}.azmk8s.io`,
              )
            }}
          />
          <ProfileField
            label="Kubernetes version"
            value={profile.kubernetesVersion}
            onChange={(value) => update('kubernetesVersion', value)}
          />
          <ProfileField
            label="Node VM SKU"
            value={profile.nodeVmSku}
            onChange={(value) => update('nodeVmSku', value)}
          />
          <NumberField
            label="Node count"
            value={profile.nodeCount}
            onChange={(value) => update('nodeCount', value)}
          />
          <NumberField
            label="Requested regional vCPU"
            value={profile.requestedRegionalVcpu}
            onChange={(value) => update('requestedRegionalVcpu', value)}
          />
          <ProfileField
            label="AKS name"
            value={profile.aksName}
            onChange={(value) => update('aksName', value)}
          />
          <ProfileField
            label="Virtual network name"
            value={profile.vnetName}
            onChange={(value) => update('vnetName', value)}
          />
          <ProfileField
            label="AKS subnet name"
            value={profile.subnetName}
            onChange={(value) => update('subnetName', value)}
          />
          <ProfileField
            label="Virtual network CIDR"
            value={profile.vnetCidr}
            onChange={(value) => update('vnetCidr', value)}
          />
          <ProfileField
            label="AKS subnet CIDR"
            value={profile.aksSubnetCidr}
            onChange={(value) => update('aksSubnetCidr', value)}
          />
          <ProfileField
            label="Pod CIDR"
            value={profile.podCidr}
            onChange={(value) => update('podCidr', value)}
          />
          <ProfileField
            label="Service CIDR"
            value={profile.serviceCidr}
            onChange={(value) => update('serviceCidr', value)}
          />
          <ProfileField
            label="DNS service IP"
            value={profile.dnsServiceIp}
            onChange={(value) => update('dnsServiceIp', value)}
          />
          <ProfileField
            label="Private DNS zone"
            value={profile.privateDnsZoneName}
            onChange={(value) => update('privateDnsZoneName', value)}
          />
          <ProfileField
            label="Existing VNet resource ID (optional)"
            value={profile.existingVnetResourceId ?? ''}
            onChange={(value) => update('existingVnetResourceId', value)}
          />
          <ProfileField
            label="Existing ACR resource ID (optional)"
            value={profile.existingContainerRegistryResourceId ?? ''}
            onChange={(value) =>
              update('existingContainerRegistryResourceId', value)
            }
          />
          <ProfileField
            label="Existing Key Vault resource ID (optional)"
            value={profile.existingKeyVaultResourceId ?? ''}
            onChange={(value) =>
              update('existingKeyVaultResourceId', value)
            }
          />
        </div>
        <CardFooter>
          <Button disabled={busy} onClick={() => void saveOnly()}>
            Save profile
          </Button>
          <Button
            appearance="primary"
            disabled={busy}
            onClick={() => void preview()}
          >
            {busy ? <Spinner size="tiny" /> : null}
            Validate and preview
          </Button>
        </CardFooter>
      </Card>

      {state?.preview ? (
        <Card>
          <CardHeader
            header={<Text weight="semibold">Structured ARM what-if</Text>}
            description={
              <Text>
                Validation correlation {state.preview.validationCorrelationId}
              </Text>
            }
          />
          <div className="preview-counts">
            <Badge color="success">
              {state.preview.createCount} create
            </Badge>
            <Badge color="warning">
              {state.preview.modifyCount} modify
            </Badge>
            <Badge color="danger">
              {state.preview.deleteCount} delete
            </Badge>
          </div>
          <div className="what-if-changes">
            {state.preview.changes.map((change) => (
              <div key={`${change.changeType}:${change.resourceId}`}>
                <Badge appearance="outline">{change.changeType}</Badge>
                <Text>{change.resourceId}</Text>
              </div>
            ))}
          </div>
          <CardFooter>
            <Button
              appearance="primary"
              disabled={busy || Boolean(state.approval)}
              onClick={() => void approve()}
            >
              {state.approval ? 'Preview approved' : 'Approve exact preview'}
            </Button>
            {state.approval ? (
              <Text size={200}>
                Approved {state.approval.createCount} create,{' '}
                {state.approval.modifyCount} modify, and{' '}
                {state.approval.deleteCount} delete changes.
              </Text>
            ) : null}
          </CardFooter>
        </Card>
      ) : null}
    </section>
  )
}

function ProfileField({
  label,
  value,
  onChange,
}: {
  label: string
  value: string
  onChange: (value: string) => void
}) {
  return (
    <Field label={label}>
      <Input
        value={value}
        onChange={(_, data) => onChange(data.value)}
      />
    </Field>
  )
}

function NumberField({
  label,
  value,
  onChange,
}: {
  label: string
  value: number
  onChange: (value: number) => void
}) {
  return (
    <Field label={label}>
      <Input
        type="number"
        min={1}
        value={String(value)}
        onChange={(_, data) => onChange(Number(data.value))}
      />
    </Field>
  )
}

async function waitForPassingRun(
  runId: string,
  apiFetch: (path: string, init?: RequestInit) => Promise<Response>,
  setStatus: (status: string) => void,
) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    const response = await apiFetch(`/api/prerequisites/runs/${runId}`)
    if (!response.ok) {
      await throwProblem(response)
    }
    const run = (await response.json()) as {
      status: 'Pending' | 'Running' | 'Passed' | 'Warning' | 'Failed' | 'Cancelled'
      results: { status: string }[]
    }
    if (run.status === 'Passed' || run.status === 'Warning') {
      return
    }
    if (run.status === 'Failed' || run.status === 'Cancelled') {
      throw new Error(
        'Deployment prerequisites did not pass. Review the Prerequisites section.',
      )
    }
    setStatus(`Running deployment prerequisites (${run.results.length} checks)`)
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
  throw new Error('Deployment prerequisites did not complete in time.')
}

async function throwProblem(response: Response): Promise<never> {
  const problem = (await response.json().catch(() => undefined)) as
    | { code?: string; detail?: string; errors?: unknown }
    | undefined
  throw new Error(
    problem?.detail ?? problem?.code ?? `Request failed (${response.status})`,
  )
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected request failure'
}

