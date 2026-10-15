export type OwnedEntraApplication = {
  objectId: string
  appId: string
  displayName: string
  signInAudience: string
  spaRedirectUris: string[]
  identifierUris: string[]
  delegatedScopes: {
    id: string
    value: string
    type: string
    isEnabled: boolean
  }[]
  appRoles: {
    id: string
    value: string
    allowedMemberTypes: string[]
    isEnabled: boolean
  }[]
  preAuthorizedApplications: {
    appId: string
    delegatedPermissionIds: string[]
  }[]
  passwordCredentialCount: number
  keyCredentialCount: number
}

type GraphApplication = {
  id?: string
  appId?: string
  displayName?: string
  signInAudience?: string
  spa?: { redirectUris?: string[] }
  identifierUris?: string[]
  api?: {
    oauth2PermissionScopes?: {
      id?: string
      value?: string
      type?: string
      isEnabled?: boolean
    }[]
    preAuthorizedApplications?: {
      appId?: string
      delegatedPermissionIds?: string[]
    }[]
  }
  appRoles?: {
    id?: string
    value?: string
    allowedMemberTypes?: string[]
    isEnabled?: boolean
  }[]
  passwordCredentials?: unknown[]
  keyCredentials?: unknown[]
}

type GraphCollection<T> = {
  value?: T[]
  '@odata.nextLink'?: string
}

const ownedApplicationsUrl =
  'https://graph.microsoft.com/v1.0/me/ownedObjects/microsoft.graph.application' +
  '?$select=id,appId,displayName,signInAudience,spa,identifierUris,api,appRoles,' +
  'passwordCredentials,keyCredentials'

export async function discoverOwnedApplications(
  accessToken: string,
  fetchImplementation: typeof fetch = fetch,
): Promise<OwnedEntraApplication[]> {
  const applications: OwnedEntraApplication[] = []
  let nextUrl: string | undefined = ownedApplicationsUrl

  while (nextUrl) {
    ensureTrustedGraphUrl(nextUrl)
    const response = await fetchImplementation(nextUrl, {
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
    })
    if (!response.ok) {
      throw new Error(`Microsoft Graph registration discovery returned ${response.status}.`)
    }

    const page = (await response.json()) as GraphCollection<GraphApplication>
    applications.push(...(page.value ?? []).map(normalizeApplication))
    nextUrl = page['@odata.nextLink']
  }

  return applications.sort((left, right) =>
    left.displayName.localeCompare(right.displayName),
  )
}

export async function servicePrincipalExists(
  appId: string,
  accessToken: string,
  fetchImplementation: typeof fetch = fetch,
): Promise<boolean> {
  if (!isGuid(appId)) {
    throw new Error('The selected application ID is not a GUID.')
  }

  const filter = encodeURIComponent(`appId eq '${appId}'`)
  const url =
    `https://graph.microsoft.com/v1.0/servicePrincipals?$filter=${filter}` +
    '&$select=id&$top=1'
  const response = await fetchImplementation(url, {
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
  })
  if (!response.ok) {
    throw new Error(`Microsoft Graph service-principal lookup returned ${response.status}.`)
  }

  function isGuid(value: string) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
      value,
    )
  }

  const result = (await response.json()) as GraphCollection<{ id?: string }>
  return (result.value?.length ?? 0) > 0
}

function normalizeApplication(
  application: GraphApplication,
): OwnedEntraApplication {
  if (!application.id || !application.appId) {
    throw new Error('Microsoft Graph returned an application without stable IDs.')
  }

  return {
    objectId: application.id,
    appId: application.appId,
    displayName: application.displayName?.trim() || application.appId,
    signInAudience: application.signInAudience ?? '',
    spaRedirectUris: application.spa?.redirectUris ?? [],
    identifierUris: application.identifierUris ?? [],
    delegatedScopes: (application.api?.oauth2PermissionScopes ?? []).map(
      (scope) => ({
        id: scope.id ?? '',
        value: scope.value ?? '',
        type: scope.type ?? '',
        isEnabled: scope.isEnabled ?? false,
      }),
    ),
    appRoles: (application.appRoles ?? []).map((role) => ({
      id: role.id ?? '',
      value: role.value ?? '',
      allowedMemberTypes: role.allowedMemberTypes ?? [],
      isEnabled: role.isEnabled ?? false,
    })),
    preAuthorizedApplications: (
      application.api?.preAuthorizedApplications ?? []
    ).map((authorized) => ({
      appId: authorized.appId ?? '',
      delegatedPermissionIds: authorized.delegatedPermissionIds ?? [],
    })),
    passwordCredentialCount: application.passwordCredentials?.length ?? 0,
    keyCredentialCount: application.keyCredentials?.length ?? 0,
  }
}

function ensureTrustedGraphUrl(url: string) {
  const parsed = new URL(url)
  if (
    parsed.protocol !== 'https:' ||
    parsed.hostname !== 'graph.microsoft.com' ||
    !parsed.pathname.startsWith('/v1.0/')
  ) {
    throw new Error('Microsoft Graph returned an untrusted pagination URL.')
  }
}
