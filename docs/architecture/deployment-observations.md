# Deployment observations

Status: accepted  
Date: 2026-10-15

## Schedule and concurrency

The service polls registered deployments every 60 seconds by default. One
process-wide limit allows four targets at once, while a per-deployment gate
prevents overlapping requests to the same endpoint.

Network and timeout failures receive three attempts with one- and two-second
delays. Authentication, incompatible-version, and invalid-response failures are
not retried. Configuration values are bounded to 1 through 32 concurrent
targets, 1 through 10 attempts, and at most 60 seconds of initial backoff.

## Collected values

Every Lite poll checks:

- `/healthz`;
- `/readyz`;
- release and topology data from `/openapi.json`.

Enterprise polls also call `/agents` and `/control/work-queue` with the
referenced Key Vault credential. Stored values include active and dead Agent
counts, bounded top-level queue array counts, Manager or Lite version, and
Agent versions exposed by the fleet response.

## Current and last successful state

Each poll writes one observation-attempt row and all of its values in one
SQLite transaction. Failure attempts contain a status and stable code but no
partial values.

The current API returns two separate facts:

- the latest attempt, which can be `AuthenticationFailed`, `Unreachable`,
  `Incompatible`, or `InvalidResponse`;
- the timestamp and values from the last `Healthy` attempt.

A failed attempt therefore cannot make an older successful result look
current. Service cancellation stops the active probe or transaction and does
not write a partial attempt.

## Inventory display

The inventory screen derives `Stale` when the latest healthy attempt is older
than twice the configured polling interval. `Unreachable`,
`Authentication failed`, `Incompatible`, and `Invalid response` come directly
from the latest attempt and are never replaced by an older healthy value.

Deployment detail shows:

- topology and Azure resource scope;
- latest and last-successful collection times;
- health, readiness, fleet, queue, and component-version values from the last
  successful attempt;
- current job reports;
- explicit `Not evaluated` or `Not configured` labels for drift, backup, and
  upgrade features that are not implemented yet.

Deployment selectors are native buttons with `aria-pressed`. The selected
status uses an atomic polite live region so keyboard and screen-reader users
receive the new status and collection time after selection.

## Mutation compatibility

The Infrastructure assembly embeds
`contracts/fsp/3.1.0/release-manifest.json`. The mutation gate reads the Lite
package version, Manager image tag, release version, and
`componentsMoveTogether` setting from that resource.

Mutation is allowed only when:

- the latest observation succeeded and is not stale;
- the topology's required `lite` or `manager` value is present;
- that value matches its manifest version;
- every other observed version matches the release version when components
  move together.

The API returns `deployment.component_version_mismatch` with one row per
component containing expected and observed versions. Missing, stale, and
failed observations use separate stable codes. Health, observation refresh,
inventory, and job-report reads do not use the mutation gate.
