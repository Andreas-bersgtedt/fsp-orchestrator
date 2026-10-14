# Interactive Entra user authentication

Status: accepted  
Date: 2026-10-14

## Decision

FSP Orchestrator authenticates operators through Microsoft Entra authorization
code flow with PKCE. The browser is a public client and has no client secret,
certificate, API key, or stored user password.

Two single-tenant application registrations form the boundary:

- the SPA registration redirects to
  `https://localhost:9443/auth/callback` and requests the delegated API scope;
- the API registration exposes `orchestrator.access_as_user` and defines the
  Reader, Operator, ConfigurationAdmin, DeploymentAdmin, and SecurityAdmin
  application roles.

The API validates the token signature, tenant, issuer, audience, lifetime,
delegated scope, and authorized SPA client (`azp` or `appid`). Role policy is
enforced by the API. The SPA may disable controls for usability, but hidden or
disabled controls are never treated as authorization.

MSAL stores its browser cache in session storage. Logout clears the SPA session
state before MSAL clears the account cache and redirects through Entra logout.
Access tokens, refresh tokens, authorization headers, and raw claims are not
returned by session APIs or written to application state, logs, or local
storage.

## Configuration

These values are public identifiers, not credentials:

```text
Authentication__Entra__TenantId
Authentication__Entra__ClientId
Authentication__Entra__SpaClientId
Authentication__Entra__ScopeName
```

`ClientId` is the API application ID. `SpaClientId` is the public browser
application ID. No client-secret setting exists.

When identifiers are missing, the application fails closed: the SPA reports
that setup is required and protected API routes return `401`.
