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
