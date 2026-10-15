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
import { type FormEvent, useCallback, useState } from 'react'

type CredentialKind = 'None' | 'Bearer' | 'Basic'

type RegistrationResponse = {
  deployment: {
    id: string
    name: string
    topology: string
    managerEndpoint: string
    azureResourceId?: string
    credentialKind?: string
    credentialReference?: string
  }
  components: {
    name: string
    version: string
    status: string
  }[]
}

export function RegisterDeployment({
  acquireAccessToken,
  apiScope,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
}) {
  const [name, setName] = useState('')
  const [endpoint, setEndpoint] = useState('')
  const [azureResourceId, setAzureResourceId] = useState('')
  const [credentialKind, setCredentialKind] =
    useState<CredentialKind>('None')
  const [credentialReference, setCredentialReference] = useState('')
  const [result, setResult] = useState<RegistrationResponse>()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  const register = useCallback(
    async (event: FormEvent) => {
      event.preventDefault()
      setBusy(true)
      setError(undefined)
      setResult(undefined)
      try {
        const token = await acquireAccessToken([apiScope])
        const response = await fetch('/api/deployments', {
          method: 'POST',
          headers: {
            Accept: 'application/json',
            Authorization: ['Bearer', token].join(' '),
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            name,
            managerEndpoint: endpoint,
            azureResourceId: azureResourceId || undefined,
            credentialKind,
            credentialReference:
              credentialKind === 'None'
                ? undefined
                : credentialReference || undefined,
          }),
        })
        if (!response.ok) {
          const problem = (await response.json()) as {
            detail?: string
            code?: string
          }
          throw new Error(
            problem.detail ??
              problem.code ??
              `Registration returned ${response.status}.`,
          )
        }

        setResult((await response.json()) as RegistrationResponse)
      } catch (registrationError) {
        setError(errorMessage(registrationError))
      } finally {
        setBusy(false)
      }
    },
    [
      acquireAccessToken,
      apiScope,
      azureResourceId,
      credentialKind,
      credentialReference,
      endpoint,
      name,
    ],
  )

  return (
    <section
      id="register-deployment"
      className="registration-setup"
      aria-labelledby="register-deployment-heading"
    >
      <div className="section-heading">
        <div>
          <Title2 id="register-deployment-heading">Register FSP deployment</Title2>
          <Text>
            Verify an FSP 3.1.0 endpoint before adding it to local inventory.
          </Text>
        </div>
        <Badge appearance="outline">Requires Operator</Badge>
      </div>

      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Registration failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      <Card>
        <form onSubmit={(event) => void register(event)}>
          <CardHeader
            header={<Text weight="semibold">Existing Lite or Enterprise endpoint</Text>}
            description={
              <Text>
                Credentials remain in Key Vault. Orchestrator stores only the
                secret URI.
              </Text>
            }
          />
          <div className="registration-fields">
            <Field label="Deployment name" required>
              <Input
                value={name}
                maxLength={200}
                onChange={(_, data) => setName(data.value)}
                placeholder="Production FSP"
                required
              />
            </Field>
            <Field label="Manager or Lite endpoint" required>
              <Input
                value={endpoint}
                type="url"
                maxLength={2048}
                onChange={(_, data) => setEndpoint(data.value)}
                placeholder="https://fsp.example.com"
                required
              />
            </Field>
            <Field label="Azure resource ID">
              <Input
                value={azureResourceId}
                maxLength={2048}
                onChange={(_, data) => setAzureResourceId(data.value)}
                placeholder="/subscriptions/.../resourceGroups/..."
              />
            </Field>
            <Field label="Manager credential kind">
              <Select
                value={credentialKind}
                onChange={(_, data) =>
                  setCredentialKind(data.value as CredentialKind)
                }
              >
                <option value="None">None for Lite</option>
                <option value="Bearer">Bearer token</option>
                <option value="Basic">Basic username:password</option>
              </Select>
            </Field>
            {credentialKind !== 'None' ? (
              <Field label="Key Vault secret reference" required>
                <Input
                  value={credentialReference}
                  type="url"
                  maxLength={2048}
                  onChange={(_, data) => setCredentialReference(data.value)}
                  placeholder="https://vault.vault.azure.net/secrets/fsp-manager"
                  required
                />
              </Field>
            ) : null}
          </div>
          <CardFooter>
            <Button
              appearance="primary"
              type="submit"
              disabled={busy || !name.trim() || !endpoint.trim()}
            >
              {busy ? <Spinner size="tiny" /> : null}
              Verify and register
            </Button>
          </CardFooter>
        </form>
      </Card>

      {result ? (
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>
              {result.deployment.name} registered as {result.deployment.topology}
            </MessageBarTitle>
            Observed{' '}
            {result.components
              .map((component) => `${component.name} ${component.version}`)
              .join(', ')}
            .
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </section>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Registration failed.'
}
