import type { OwnedEntraApplication } from './graphDiscovery.ts'

const graphRoot = 'https://graph.microsoft.com/v1.0'
const graphAppId = '00000003-0000-0000-c000-000000000000'
const userReadScopeId = 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'
const applicationReadAllScopeId = 'c79f8feb-a9db-4090-85f9-90d820caa0eb'
const redirectUri = 'https://localhost:9443/auth/callback'
const scopeName = 'orchestrator.access_as_user'
const roles = [
  'Reader',
  'Operator',
  'ConfigurationAdmin',
  'DeploymentAdmin',
  'SecurityAdmin',
] as const

type GraphApplication = {
  id: string
  appId: string
  displayName: string
}

type GraphServicePrincipal = {
  id: string
  appId: string
}

export type CreatedRegistrationPair = {
  spa: OwnedEntraApplication
  api: OwnedEntraApplication
  spaServicePrincipalExists: boolean
  apiServicePrincipalExists: boolean
  signedInUserRoles: string[]
}

export async function createRecommendedRegistrations(
  accessToken: string,
  userObjectId: string,
  fetchImplementation: typeof fetch = fetch,
): Promise<CreatedRegistrationPair> {
  const createdApplications: string[] = []
  const createdServicePrincipals: string[] = []
  try {
    const spa = await graphRequest<GraphApplication>(
      '/applications',
      accessToken,
      fetchImplementation,
      {
        method: 'POST',
        body: {
          displayName: 'FSP Orchestrator SPA',
          requiredResourceAccess: [
            {
              resourceAppId: graphAppId,
              resourceAccess: [
                { id: userReadScopeId, type: 'Scope' },
                { id: applicationReadAllScopeId, type: 'Scope' },
              ],
            },
          ],
          signInAudience: 'AzureADMyOrg',
          spa: { redirectUris: [redirectUri] },
        },
      },
    )
    createdApplications.push(spa.id)

    const scopeId = crypto.randomUUID()
    const appRoles = roles.map((role) => ({
      id: crypto.randomUUID(),
      value: role,
      displayName: role,
      description: `FSP Orchestrator ${role} role`,
      allowedMemberTypes: ['User'],
      isEnabled: true,
    }))
    const api = await graphRequest<GraphApplication>(
      '/applications',
      accessToken,
      fetchImplementation,
      {
        method: 'POST',
        body: {
          appRoles,
          displayName: 'FSP Orchestrator API',
          signInAudience: 'AzureADMyOrg',
          api: {
            requestedAccessTokenVersion: 2,
            oauth2PermissionScopes: [
              {
                id: scopeId,
                value: scopeName,
                type: 'User',
                isEnabled: true,
                adminConsentDisplayName: 'Access FSP Orchestrator',
                adminConsentDescription:
                  'Access FSP Orchestrator as the signed-in user.',
                userConsentDisplayName: 'Access FSP Orchestrator',
                userConsentDescription:
                  'Allow FSP Orchestrator to act on your behalf.',
              },
            ],
          },
        },
      },
    )
    createdApplications.push(api.id)

    await graphRequest(
      `/applications/${api.id}`,
      accessToken,
      fetchImplementation,
      {
        method: 'PATCH',
        body: {
          identifierUris: [`api://${api.appId}`],
          api: {
            requestedAccessTokenVersion: 2,
            oauth2PermissionScopes: [
              {
                id: scopeId,
                value: scopeName,
                type: 'User',
                isEnabled: true,
                adminConsentDisplayName: 'Access FSP Orchestrator',
                adminConsentDescription:
                  'Access FSP Orchestrator as the signed-in user.',
                userConsentDisplayName: 'Access FSP Orchestrator',
                userConsentDescription:
                  'Allow FSP Orchestrator to act on your behalf.',
              },
            ],
            preAuthorizedApplications: [
              {
                appId: spa.appId,
                delegatedPermissionIds: [scopeId],
              },
            ],
          },
        },
      },
    )
    await graphRequest(
      `/applications/${spa.id}`,
      accessToken,
      fetchImplementation,
      {
        method: 'PATCH',
        body: {
          requiredResourceAccess: [
            {
              resourceAppId: graphAppId,
              resourceAccess: [
                { id: userReadScopeId, type: 'Scope' },
                { id: applicationReadAllScopeId, type: 'Scope' },
              ],
            },
            {
              resourceAppId: api.appId,
              resourceAccess: [{ id: scopeId, type: 'Scope' }],
            },
          ],
        },
      },
    )

    const spaServicePrincipal = await createServicePrincipal(
      spa.appId,
      accessToken,
      fetchImplementation,
    )
    createdServicePrincipals.push(spaServicePrincipal.id)
    const apiServicePrincipal = await createServicePrincipal(
      api.appId,
      accessToken,
      fetchImplementation,
    )
    createdServicePrincipals.push(apiServicePrincipal.id)
    const securityAdminRole = appRoles.find(
      (role) => role.value === 'SecurityAdmin',
    )!
    await graphRequest(
      `/servicePrincipals/${apiServicePrincipal.id}/appRoleAssignedTo`,
      accessToken,
      fetchImplementation,
      {
        method: 'POST',
        body: {
          principalId: userObjectId,
          resourceId: apiServicePrincipal.id,
          appRoleId: securityAdminRole.id,
        },
      },
    )

    return {
      spa: {
        objectId: spa.id,
        appId: spa.appId,
        displayName: spa.displayName,
        signInAudience: 'AzureADMyOrg',
        spaRedirectUris: [redirectUri],
        identifierUris: [],
        delegatedScopes: [],
        appRoles: [],
        preAuthorizedApplications: [],
        passwordCredentialCount: 0,
        keyCredentialCount: 0,
      },
      api: {
        objectId: api.id,
        appId: api.appId,
        displayName: api.displayName,
        signInAudience: 'AzureADMyOrg',
        spaRedirectUris: [],
        identifierUris: [`api://${api.appId}`],
        delegatedScopes: [
          {
            id: scopeId,
            value: scopeName,
            type: 'User',
            isEnabled: true,
          },
        ],
        appRoles: appRoles.map((role) => ({
          id: role.id,
          value: role.value,
          allowedMemberTypes: role.allowedMemberTypes,
          isEnabled: role.isEnabled,
        })),
        preAuthorizedApplications: [
          {
            appId: spa.appId,
            delegatedPermissionIds: [scopeId],
          },
        ],
        passwordCredentialCount: 0,
        keyCredentialCount: 0,
      },
      spaServicePrincipalExists: true,
      apiServicePrincipalExists: true,
      signedInUserRoles: ['SecurityAdmin'],
    }
  } catch (error) {
    const cleanupErrors = await cleanup(
      createdServicePrincipals,
      createdApplications,
      accessToken,
      fetchImplementation,
    )
    if (cleanupErrors.length > 0) {
      throw new AggregateError(
        [error, ...cleanupErrors],
        'Registration creation failed and one or more created objects could not be removed.',
      )
    }
    throw error
  }
}

async function createServicePrincipal(
  appId: string,
  accessToken: string,
  fetchImplementation: typeof fetch,
) {
  return graphRequest<GraphServicePrincipal>(
    '/servicePrincipals',
    accessToken,
    fetchImplementation,
    {
      method: 'POST',
      body: { appId },
    },
  )
}

async function cleanup(
  servicePrincipals: string[],
  applications: string[],
  accessToken: string,
  fetchImplementation: typeof fetch,
) {
  const errors: Error[] = []
  for (const id of [...servicePrincipals].reverse()) {
    const error = await graphDelete(
      `/servicePrincipals/${id}`,
      accessToken,
      fetchImplementation,
    )
    if (error) {
      errors.push(error)
    }
  }
  for (const id of [...applications].reverse()) {
    const error = await graphDelete(
      `/applications/${id}`,
      accessToken,
      fetchImplementation,
    )
    if (error) {
      errors.push(error)
    }
  }
  return errors
}

async function graphDelete(
  path: string,
  accessToken: string,
  fetchImplementation: typeof fetch,
) {
  try {
    await graphRequest(path, accessToken, fetchImplementation, {
      method: 'DELETE',
    })
    return undefined
  } catch (error) {
    return error instanceof Error ? error : new Error('Graph cleanup failed.')
  }
}

async function graphRequest<T = unknown>(
  path: string,
  accessToken: string,
  fetchImplementation: typeof fetch,
  request: { method: 'POST' | 'PATCH' | 'DELETE'; body?: unknown },
): Promise<T> {
  const response = await fetchImplementation(`${graphRoot}${path}`, {
    method: request.method,
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
    body: request.body ? JSON.stringify(request.body) : undefined,
  })
  if (!response.ok) {
    throw new Error(
      `Microsoft Graph ${request.method} ${path} returned ${response.status}.`,
    )
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
