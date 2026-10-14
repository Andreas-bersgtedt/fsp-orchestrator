# Fixed local HTTPS origin

Status: accepted  
Date: 2026-10-14

## Decision

FSP Orchestrator uses one fixed browser and API origin:

```text
https://localhost:9443
```

Kestrel binds the port with `ListenLocalhost`, which listens only on IPv4 and
IPv6 loopback. HTTP, wildcard, LAN, alternate-port, and non-loopback URL
overrides are rejected during startup. The SPA, API, and OpenAPI document share
this origin, so no CORS policy is required.

The process validates a non-expired `localhost` certificate with a private key
before Kestrel starts. Development uses the current user's HTTPS development
certificate. Service installation will place and trust a certificate for the
service account, grant private-key access, and configure its thumbprint and
store location.

Runtime state is kept outside the installation directory:

- development: `%LOCALAPPDATA%\FspOrchestrator\Development`;
- installed service: `%PROGRAMDATA%\FspOrchestrator`;
- tests and supported overrides: an explicit absolute data directory.

The application never deletes this directory during service stop or restart.
Installer removal and data-retention choices are handled by the installer
workflow.

## Consequences

- Entra can use one exact redirect origin without wildcard URIs.
- Browser and API requests remain same-origin.
- Another process occupying port `9443`, a missing certificate, an invalid
  certificate, or a non-loopback URL override prevents readiness.
- Changing the port is a compatibility and application-registration decision,
  not a runtime preference.
