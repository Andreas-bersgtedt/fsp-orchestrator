# Existing deployment registration

Status: accepted  
Date: 2026-10-15

## Input boundary

Registration accepts:

- an HTTPS Lite or Manager endpoint;
- an optional Azure resource ID;
- an optional Azure Key Vault secret URI and credential kind.

It does not accept an FSP host path, install directory, volume, configuration
file, or source checkout. Key Vault secret values are resolved in memory with
`DefaultAzureCredential`; only the URI is written to SQLite.

## Probe sequence

The service completes every remote check before opening the inventory
transaction:

1. `GET /healthz` must return `{"status":"ok"}`.
2. `GET /readyz` must return `{"status":"ready"}`.
3. `GET /openapi.json` must identify release `3.1.0`.
4. The OpenAPI title and required paths identify Lite or Enterprise topology.
5. Enterprise registration resolves the Key Vault reference and calls
   `GET /agents` with Basic or Bearer authentication.

Redirects are disabled. TLS, timeout, identity, version, and contract failures
return stable problem codes without provider response bodies or exception
messages.

## Persistence

The normalized Manager endpoint has a unique SQLite index. A successful probe
creates the deployment and its observed component versions in one transaction.
A failed probe writes no deployment row. A concurrent duplicate produces
`deployment.endpoint_duplicate`.

Audit rows contain the actor, correlation ID, result code, and a SHA-256 hash
of non-secret inputs. They do not contain the resolved Key Vault value or an
authorization header.
