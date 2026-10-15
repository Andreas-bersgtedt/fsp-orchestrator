# Local SQLite persistence

Status: accepted  
Date: 2026-10-15

## Decision

FSP Orchestrator stores non-secret local state in `orchestrator.db` under the
service data directory. EF Core migrations run before the application reports
ready.

The first schema contains:

- deployment inventory and observed component metadata;
- jobs with unique idempotency keys and optimistic concurrency versions;
- ordered job steps and bounded safe output;
- append-only audit metadata;
- EF migration history.

Domain and Application projects do not import EF Core, SQLite, HTTP, or Azure
SDKs. Persistence interfaces and immutable records live in Application; EF
entities, mappings, transactions, migrations, and maintenance live in
Infrastructure.

## Safety and reliability

- Job transitions run in serializable transactions and include the expected
  version in the update contract.
- Invalid state edges and stale versions fail explicitly.
- Audit events can be inserted but not updated or deleted through the context.
- All string metadata passes a persistence guard that rejects bearer tokens,
  JWT shapes, authorization headers, secret field names, passwords, account
  keys, and shared-access signatures.
- SQLite uses foreign keys, full synchronous writes, and WAL journaling.
- Startup applies migrations and runs `PRAGMA integrity_check` before readiness.
- Online backups are integrity-checked and addressed by file name plus SHA-256.
- Restore accepts only a file from the managed backup directory with the
  approved hash, validates it, and copies it through SQLite's backup API.

The database and backups contain Orchestrator metadata only. FSP credentials,
Azure tokens, database passwords, Key Vault values, and access keys are not
valid persistence inputs.
