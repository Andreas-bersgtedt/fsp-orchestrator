# Microsoft Entra application registrations

FSP Orchestrator uses two single-tenant registrations and delegated user
authentication. Neither registration needs a client secret or certificate
credential.

The machine-readable source for these expectations is
[`contracts/orchestrator/entra-registration.json`](../../contracts/orchestrator/entra-registration.json).

## SPA registration

Create **FSP Orchestrator SPA** with:

- supported account type: accounts in this organizational directory only;
- platform: Single-page application;
- redirect URI: `https://localhost:9443/auth/callback`;
- no client secret or certificate;
- delegated access to the Orchestrator API
  `orchestrator.access_as_user` scope;
- Microsoft Graph delegated `User.Read` for owned-application discovery.

The setup UI requests `Application.Read.All` only when checking whether selected
enterprise applications exist. That permission requires tenant admin consent.
The [bootstrap workflow](./entra-bootstrap.md) uses delegated
`Application.ReadWrite.All` and `AppRoleAssignment.ReadWrite.All` only when an
administrator chooses to create the pair and assign the first SecurityAdmin.
Microsoft Graph does not expose `Application.ReadWrite.OwnedBy` as a delegated
public-client scope.

## API registration

Create **FSP Orchestrator API** with:

- supported account type: accounts in this organizational directory only;
- application ID URI: `api://<api-client-id>`;
- delegated scope: `orchestrator.access_as_user`;
- scope type: users and admins;
- the selected SPA client pre-authorized for that scope;
- no client secret or certificate.

Define these enabled application roles for **Users/Groups**:

| Value | Purpose |
| --- | --- |
| `Reader` | Read inventory, status, observations, and reports |
| `Operator` | Run approved operational actions |
| `ConfigurationAdmin` | Preview and apply FSP configuration |
| `DeploymentAdmin` | Preview and apply Azure deployments and upgrades |
| `SecurityAdmin` | Manage identity, role, and security settings |

Assign at least one role to each operator through the API enterprise
application. Role values are case-sensitive.

## Validation

The SecurityAdmin setup panel discovers application objects owned by the
signed-in user and reports stable checks for:

- `entra.tenant.match`
- `entra.client_ids.valid`
- `entra.audience.single_tenant`
- `entra.spa.redirect_uri`
- `entra.applications.no_credentials`
- `entra.api.identifier_uri`
- `entra.api.delegated_scope`
- `entra.api.application_roles`
- `entra.api.authorized_spa`
- `entra.service_principals.present`
- `entra.operator.role_assignment`

Discovery and validation never display credential values, Graph access tokens,
Orchestrator access tokens, or authorization headers. Microsoft Graph tokens
are sent only to `https://graph.microsoft.com` and are never accepted as
Orchestrator API tokens.
