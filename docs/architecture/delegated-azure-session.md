# Delegated Azure session

Status: accepted  
Date: 2026-10-15

## Token handoff

The SPA requests
`https://management.azure.com/user_impersonation` from Microsoft Entra ID. It
sends that ARM access token once to the loopback API over HTTPS while using the
normal Orchestrator API token for request authentication.

The API validates the ARM token's:

- signing key and issuer;
- ARM audience and `user_impersonation` scope;
- expiry;
- authorized SPA client;
- tenant ID and object ID against the Orchestrator API token.

Only identity metadata and expiry are returned. The ARM token remains in a
process-memory dictionary keyed by tenant and object ID. It is not written to
SQLite, audit metadata, logs, or API responses.

## Expiry and jobs

Sessions are treated as expired one minute before token expiry. Discovery
returns `azure.sign_in_required` with a Bearer challenge. A running Azure job
that requests an expired or missing session transitions to `Paused` with safe
output `azure.sign_in_required`; it can resume after the user connects Azure
again.

## Discovery scope

Subscription discovery calls ARM with API version `2022-12-01` and returns only
enabled or warned subscriptions. Resource discovery first reloads that allowed
subscription list on the server. A subscription not present in the list returns
`azure.scope_denied`.

Resources are read with API version `2021-04-01`. Responses contain stable ARM
resource IDs, names, resource types, locations, and the API version. Redirects
are disabled, pagination is limited to ten pages, and every `nextLink` must use
HTTPS on `management.azure.com`.
