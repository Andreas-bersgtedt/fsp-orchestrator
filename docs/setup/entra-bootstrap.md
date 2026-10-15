# First-run Entra bootstrap

An unconfigured installation uses the credential-free, multi-tenant **FSP
Orchestrator Bootstrap** SPA registration:

```text
Client ID: ca857dc1-f0bb-47e6-bc8a-02edc0e17cff
Redirect:  https://localhost:9443/auth/bootstrap-callback
```

The client ID is a public identifier. The registration has no client secret,
certificate credential, application key, or password.

## Permissions

The bootstrap SPA requests delegated Microsoft Graph permissions incrementally:

| Operation | Delegated permissions | Consent |
| --- | --- | --- |
| Sign in and list owned applications | `User.Read` | User consent when tenant policy permits |
| Create or fully validate registrations | `Application.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, `User.Read` | Admin consent required |

Microsoft Graph does not expose `Application.ReadWrite.OwnedBy` as a delegated
public-client scope. Interactive registration creation therefore requires the
broader `Application.ReadWrite.All` permission and an explicit administrator
action. The application requests it only when the operator selects create or
full service-principal validation.

## Trust boundaries

- Bootstrap and Graph tokens remain in MSAL session storage or memory.
- Graph tokens are sent only to `https://graph.microsoft.com`.
- The local commit endpoint accepts only a token minted for the selected
  candidate Orchestrator API.
- The candidate token is validated for signature, tenant, issuer, audience,
  lifetime, delegated scope, selected SPA client, operator object ID, and the
  SecurityAdmin role.
- Only public identifiers and validation metadata are written to
  `orchestrator.identity.json`.
- The file is replaced atomically and, on Windows, inheritance is removed so
  only the service identity receives full control.
- Failed or cancelled Graph operations do not modify local configuration.

After a successful commit, FSP Orchestrator starts a restart helper, releases
the loopback port, and activates the tenant SPA/API configuration in a
replacement process. Installed-service mode asks Windows Service Control
Manager to restart the registered service.
