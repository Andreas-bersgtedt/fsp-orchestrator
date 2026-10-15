# FSP Orchestrator

FSP Orchestrator is a local management plane for
[Fabric Shortcut Proxy](https://github.com/Andreas-bersgtedt/Fabric-Shortcut-Proxy).
It is intended to deploy, register, configure, observe, upgrade, back up, and
recover FSP environments from one browser application.

## Project status

The FSP 3.1.0 contract milestone is complete. The repository now contains the
.NET 10 Windows service, React operator shell, Entra PKCE authentication,
SQLite persistence, prerequisite workflows, and a WiX installer. Deployment
and remote FSP management workflows remain under development.

The first planned release targets:

- Windows 11 and Windows Server;
- a Windows service that binds to a fixed loopback HTTPS origin;
- a React and TypeScript single-page application;
- an ASP.NET Core API on .NET 10;
- single-tenant Microsoft Entra ID authentication;
- FSP Lite and Enterprise 3.1.0 registration;
- FSP Enterprise deployment to Azure Kubernetes Service;
- prerequisite checks in the UI and API;
- Bicep and Helm deployment adapters;
- SQLite inventory and job state;
- Azure Key Vault references for deployed secrets.

Development work is tracked in
[GitHub Issues](https://github.com/Andreas-bersgtedt/fsp-orchestrator/issues)
and
[GitHub Milestones](https://github.com/Andreas-bersgtedt/fsp-orchestrator/milestones).

## Planned operator workflow

1. Install the Windows service.
2. Open the local HTTPS address in a browser.
3. Sign in with Microsoft Entra ID.
4. Run local, identity, Azure, network, capacity, and FSP prerequisite checks.
5. Register an existing FSP environment or preview a new AKS deployment.
6. Approve and run deployment or configuration jobs.
7. Inspect health, Agents, work queues, versions, backups, and audit records.
8. Apply upgrades through a versioned release and rollback contract.

## Process layout

```text
Browser SPA
    |
    | HTTPS on loopback
    v
FSP Orchestrator Windows service
    |-- Entra token validation and role checks
    |-- prerequisite checker engine
    |-- deployment and configuration jobs
    |-- SQLite inventory, observations, and audit metadata
    |
    +--> Azure Resource Manager
    +--> Azure Key Vault
    +--> private AKS command channel
    +--> FSP Manager and Config Builder APIs
```

The Orchestrator does not serve Fabric S3 traffic and does not generate Parquet
files. Those responsibilities remain in FSP.

## Authentication and authorization

The browser application will use Microsoft Entra ID authorization code flow
with PKCE. The SPA has no client secret. The local API validates tenant, issuer,
audience, scope, expiration, and authorized client ID.

Planned Orchestrator roles are:

- `Reader`
- `Operator`
- `ConfigurationAdmin`
- `DeploymentAdmin`
- `SecurityAdmin`

FSP keeps its own deployment-level roles. An Orchestrator role does not imply
an FSP role.

## Prerequisite checks

Prerequisite checks are planned for:

- local Windows host and service state;
- local certificate, port, disk, database, and system clock;
- Entra application settings and role assignments;
- Azure subscription access and provider registration;
- Azure actions at the selected resource scope;
- regional SKU and quota availability;
- network ranges, private DNS, and source connectivity paths;
- FSP health, version agreement, control contract, and required APIs;
- source drivers, source-side connection tests, and storage access;
- upgrade, restore, rollback, and removal requirements.

A deployment or configuration apply request must reference a passing,
unexpired prerequisite run whose input fingerprint matches the requested
operation.

## Repository layout

The planned source layout is:

```text
src/
  Orchestrator.Api/
  Orchestrator.Application/
  Orchestrator.Domain/
  Orchestrator.Infrastructure/
  Orchestrator.Web/
  Orchestrator.Installer/
contracts/
  fsp/
deploy/
  bicep/
  helm/
tests/
  unit/
  integration/
  contract/
  installer/
```

The Domain, Application, Infrastructure, API, Web, installer, unit-test,
architecture-test, and contract-test projects are present. Deployment projects
will be added by their assigned milestones.

## Development

Install the .NET 10 SDK and Node.js 22, then build the API and production SPA
with one command:

```powershell
dotnet build Fsp.Orchestrator.slnx
```

Run unit and architecture tests:

```powershell
dotnet test Fsp.Orchestrator.slnx
```

Install and trust the development certificate, then start the local API, which
hosts the built SPA:

```powershell
dotnet dev-certs https --trust
dotnet run --project src\Orchestrator.Api\Orchestrator.Api.csproj
```

The fixed origin is `https://localhost:9443`. The initial smoke endpoints are
`/`, `/api/health`, `/api/ready`, and `/openapi/v1.json`. See the
[local service smoke test](./docs/testing/local-service-smoke.md).

Interactive sign-in uses Microsoft Entra authorization code flow with PKCE.
Configure the tenant ID, API client ID, and SPA client ID through
`Authentication:Entra` or the matching double-underscore environment
variables. These are public identifiers; the SPA has no client secret. Until
they are configured, the shell reports that Entra setup is required and
protected API routes return `401`. See the
[authentication decision](./docs/architecture/entra-user-authentication.md).
The exact SPA/API registration shape and validation codes are documented in
[Microsoft Entra application registrations](./docs/setup/entra-application-registrations.md).
An unconfigured installation offers a built-in, credential-free
[first-run bootstrap flow](./docs/setup/entra-bootstrap.md) for discovering or
creating registrations and atomically committing only public identifiers.

Non-secret inventory, job, and audit metadata is stored in a migrated SQLite
database under the service data directory. Migrations and integrity checks run
before readiness; backup and restore use managed files and approved SHA-256
hashes. See [local SQLite persistence](./docs/architecture/sqlite-persistence.md).

Protected operations use the typed
[prerequisite checker engine](./docs/architecture/prerequisite-engine.md) for
dependency ordering, bounded concurrency, timeout/cancellation, input
fingerprints, expiry, and blocking apply gates.

Existing Lite and Enterprise deployments are added through the
[registration probe](./docs/architecture/deployment-registration.md). The
probe checks HTTPS health, readiness, the FSP 3.1.0 OpenAPI identity, topology,
and Enterprise credentials before one SQLite transaction creates inventory and
component-version observations.

Azure discovery uses a
[delegated in-memory ARM session](./docs/architecture/delegated-azure-session.md).
The API verifies that the Orchestrator and ARM tokens identify the same tenant,
user, and SPA client before listing allowed subscriptions and stable resource
IDs.

Registered deployments are polled by the
[observation scheduler](./docs/architecture/deployment-observations.md).
Latest failures are reported separately from the timestamp and values of the
last successful poll, so disconnected endpoints are never shown as currently
healthy.

Build, signing, certificate lifecycle, repair, upgrade, and removal behavior
for the WiX package are documented in the
[Windows installer guide](./docs/setup/windows-installer.md).

The engineering sequence is:

1. publish and test the FSP 3.1.0 API and release contracts;
2. create the Windows service, SPA shell, installer, and Entra sign-in;
3. register FSP deployments and collect observed state;
4. deploy FSP Enterprise to AKS;
5. manage configuration and Key Vault secret references;
6. add operations, backup, and recovery;
7. add release upgrades and federated deployment management.

## Security boundary

- The service binds only to loopback.
- Production FSP Manager connections require TLS.
- Access tokens and refresh tokens are not stored in SQLite.
- Azure access tokens remain in memory for the active job.
- Database passwords, S3 secret keys, Agent tokens, and Key Vault values are
  not stored in the Orchestrator database.
- The Orchestrator never requires access to an FSP host, pod, volume, install
  directory, configuration file, or source checkout. Azure resources are
  managed through ARM and AKS APIs; FSP state is managed through HTTPS APIs.
  See the [remote management boundary](./docs/architecture/remote-fsp-management-boundary.md).
- Mutating API requests require Entra authentication, an allowed role, an
  idempotency key, and any operation-specific prerequisite run.
- Job output and exported reports pass through secret redaction.

Security defects should not be filed in a public issue. Use GitHub's private
vulnerability reporting channel when it is enabled for this repository.

## Contributing

The issue plan separates work into Epic, Story, and Task labels. Before starting
work:

1. select an issue from the active milestone;
2. confirm its parent epic and acceptance criteria;
3. record contract changes before changing an FSP integration surface;
4. add the smallest test set that proves the acceptance criteria;
5. avoid committing credentials, generated tokens, local databases, or FSP
   configuration containing secrets.

Contribution and pull request templates will be added with the solution
skeleton.

## License

This project is licensed under the [MIT License](./LICENSE).
