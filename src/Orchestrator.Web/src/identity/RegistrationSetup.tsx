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
import { useMemo, useState } from 'react'
import type { OperatorSession } from '../auth/authTypes.ts'
import {
  discoverOwnedApplications,
  servicePrincipalExists,
  type OwnedEntraApplication,
} from './graphDiscovery.ts'

type ValidationCheck = {
  code: string
  status: 'Passed' | 'Failed'
  message: string
  remediation: string
}

type ValidationResponse = {
  passed: boolean
  checks: ValidationCheck[]
}

export function RegistrationSetup({
  acquireAccessToken,
  apiScope,
  session,
}: {
  acquireAccessToken: (scopes: string[]) => Promise<string>
  apiScope: string
  session: OperatorSession
}) {
  const [applications, setApplications] = useState<OwnedEntraApplication[]>([])
  const [spaAppId, setSpaAppId] = useState('')
  const [apiAppId, setApiAppId] = useState('')
  const [validation, setValidation] = useState<ValidationResponse>()
  const [status, setStatus] = useState<
    'idle' | 'discovering' | 'validating'
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
    setStatus('discovering')
    setError(undefined)
    setValidation(undefined)
    try {
      const token = await acquireAccessToken(['User.Read'])
      const discovered = await discoverOwnedApplications(token)
      setApplications(discovered)
      setSpaAppId('')
      setApiAppId('')
    } catch (discoveryError) {
      setError(errorMessage(discoveryError))
    } finally {
      setStatus('idle')
    }
  }

  async function validate() {
    if (!spa || !api) {
      return
    }

    setStatus('validating')
    setError(undefined)
    try {
      const graphToken = await acquireAccessToken(['Application.Read.All'])
      const [spaServicePrincipal, apiServicePrincipal, localApiToken] =
        await Promise.all([
          servicePrincipalExists(spa.appId, graphToken),
          servicePrincipalExists(api.appId, graphToken),
          acquireAccessToken([apiScope]),
        ])
      const response = await fetch('/api/identity/registrations/validate', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${localApiToken}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          tenantId: session.tenantId,
          signedInTenantId: session.tenantId,
          spa,
          api,
          spaServicePrincipalExists: spaServicePrincipal,
          apiServicePrincipalExists: apiServicePrincipal,
          signedInUserRoles: session.roles,
        }),
      })
      if (!response.ok) {
        throw new Error(`Registration validation returned ${response.status}.`)
      }
      setValidation((await response.json()) as ValidationResponse)
    } catch (validationError) {
      setError(errorMessage(validationError))
    } finally {
      setStatus('idle')
    }
  }

  return (
    <section className="registration-setup" aria-labelledby="registration-heading">
      <div className="section-heading">
        <div>
          <Title2 id="registration-heading">Entra registrations</Title2>
          <Text>
            Discover application registrations you own, select a SPA/API pair,
            and validate it without exposing credential values.
          </Text>
        </div>
        <Button
          appearance="secondary"
          disabled={status !== 'idle'}
          onClick={() => void discover()}
        >
          Discover owned registrations
        </Button>
      </div>

      {status === 'discovering' ? (
        <Spinner label="Reading owned applications from Microsoft Graph" />
      ) : null}

      {error ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Registration operation failed</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {applications.length > 0 ? (
        <Card>
          <CardHeader
            header={<Text weight="semibold">Select registration pair</Text>}
            description={
              <Text>
                Service-principal validation requests Application.Read.All only
                after you choose Validate.
              </Text>
            }
          />
          <div className="registration-fields">
            <Field label="SPA registration">
              <Select
                value={spaAppId}
                onChange={(_, data) => setSpaAppId(data.value)}
              >
                <option value="">Select a SPA registration</option>
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
                <option value="">Select an API registration</option>
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
              disabled={!spa || !api || spa.appId === api.appId || status !== 'idle'}
              onClick={() => void validate()}
            >
              Validate registrations
            </Button>
          </CardFooter>
        </Card>
      ) : null}

      {status === 'validating' ? (
        <Spinner label="Validating Entra registration contract" />
      ) : null}

      {validation ? (
        <div className="validation-results">
          <Badge
            appearance="filled"
            color={validation.passed ? 'success' : 'danger'}
          >
            {validation.passed ? 'Registration pair valid' : 'Validation failed'}
          </Badge>
          {validation.checks.map((check) => (
            <Card key={check.code} size="small">
              <CardHeader
                header={
                  <Text weight="semibold">
                    {check.status === 'Passed' ? 'Passed' : 'Failed'} · {check.code}
                  </Text>
                }
                description={
                  <Text>
                    {check.message}
                    {check.remediation ? ` ${check.remediation}` : ''}
                  </Text>
                }
              />
            </Card>
          ))}
        </div>
      ) : null}
    </section>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'The operation failed.'
}
