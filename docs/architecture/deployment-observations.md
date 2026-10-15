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
