# Windows installer

The FSP Orchestrator MSI is a per-machine WiX package. Windows Installer owns
elevation, file repair, Windows Service registration, rollback, and major
upgrade sequencing.

## Build

Build a self-contained `win-x64` package from the repository root:

```powershell
.\eng\Build-Installer.ps1 -Version 0.1.0
```

The script publishes the API and installer helper, builds the WiX package, and
writes the MSI under `artifacts\installer\package`.

Release tags use the same script with an Authenticode certificate imported
from the protected `installer-signing` GitHub environment. Signing uses SHA-256
and an RFC 3161 timestamp, then runs `signtool verify`. Configure these
environment secrets before creating a release tag:

- `INSTALLER_SIGNING_CERTIFICATE_BASE64`
- `INSTALLER_SIGNING_CERTIFICATE_PASSWORD`

## Lifecycle

- **Install**: Windows Installer requests per-machine elevation, installs the
  self-contained application, creates and trusts an installer-owned localhost
  certificate, registers `FSP Orchestrator` as an automatic LocalSystem
  service, and starts it.
- **Repair**: missing application files are restored and the certificate is
  recreated if necessary.
- **Upgrade**: a WiX major upgrade stops the old service, installs the new
  binaries transactionally, preserves `%PROGRAMDATA%\FspOrchestrator`, and
  starts the replacement service.
- **Uninstall**: interactive removal asks whether to retain metadata, audit
  history, and identity configuration. Quiet removal retains metadata by
  default. Pass `REMOVELOCALDATA=1` to explicitly remove it:

  ```powershell
  msiexec.exe /x Fsp.Orchestrator-0.1.0-win-x64.msi /qn REMOVELOCALDATA=1
  ```

The installer removes only the certificate carrying the
`FSP Orchestrator localhost` friendly name. It does not remove unrelated
localhost or development certificates.

## Clean-machine verification

The Windows installer workflow builds two package versions and runs
`tests\installer\Test-Installer.ps1` on a fresh GitHub-hosted Windows VM. The
suite verifies:

1. install, service startup, trusted HTTPS, and certificate creation;
2. repair of a deliberately removed file;
3. major upgrade with metadata preservation;
4. default uninstall with metadata retention;
5. reinstall and explicit metadata removal.
