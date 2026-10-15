# Prerequisite checker engine

Status: accepted  
Date: 2026-10-15

## Contract

Each prerequisite checker declares:

- stable ID, category, name, description, and version;
- applicable operation names;
- dependency check IDs;
- relevant input keys;
- timeout and result lifetime;
- whether failure blocks apply.

A run records the operation, deterministic input fingerprint, timestamps,
status, and one persisted result per selected check. Results contain stable
codes, safe evidence, remediation, relevant-input fingerprint, duration, and
expiry.

## Execution

- The catalog is validated for missing dependencies and cycles at startup.
- Selecting a check automatically includes its dependency closure.
- Independent checks run with bounded concurrency.
- Dependencies complete before dependents start.
- A failed/unknown/cancelled/skipped blocking dependency skips its dependents.
- Timeouts and user cancellation map to stable result codes.
- Unclassified checker errors become `prerequisite.unhandled_error`; exception
  messages are not persisted as evidence.
- Evidence is sorted, bounded, truncated, and redacted through the same
  token/secret guard used by SQLite persistence.

## Apply gate

An apply operation supplies the run ID, operation name, and current inputs. The
gate rejects:

- a missing or incomplete run;
- operation mismatch;
- blocking failed, unknown, skipped, or cancelled results;
- results whose relevant input fingerprint changed;
- expired blocking results.

Warning results do not bypass authorization and are handled by the warning
acceptance workflow in #20.

## Built-in local and identity checks

The first production catalog contains:

- Windows host and Windows Service hosting;
- the fixed loopback HTTPS listener;
- localhost certificate subject, expiry, and remaining lifetime;
- SQLite integrity, pending migrations, and data-volume free space;
- measured clock skew from the HTTPS `Date` header returned by the Microsoft
  identity platform;
- configured Entra tenant, distinct SPA/API client IDs, redirect URI, delegated
  scope, authorized SPA client, and current user role assignment.

The API adds the authenticated tenant, client ID, scopes, and roles to
server-owned run inputs. Client-provided values cannot override them. These
values participate in relevant-input fingerprints so an apply gate rejects a
run if the authenticated identity context changes.

Certificate evidence includes only the subject, expiry, and remaining days.
No certificate private-key data, access token, or credential value is collected
or persisted.
