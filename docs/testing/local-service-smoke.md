# Local HTTPS and Windows Service smoke test

## Console mode

1. Install and trust the current user's development certificate:

   ```powershell
   dotnet dev-certs https --trust
   ```

2. Start the application:

   ```powershell
   dotnet run --project src\Orchestrator.Api\Orchestrator.Api.csproj
   ```

3. Open `https://localhost:9443`.
4. Confirm the shell reports **Local service online**.
5. Confirm these requests return `200`:

   ```powershell
   Invoke-WebRequest https://localhost:9443/api/health
   Invoke-WebRequest https://localhost:9443/api/ready
   Invoke-WebRequest https://localhost:9443/openapi/v1.json
   ```

6. Confirm `/api/does-not-exist` returns Problem Details with `404`, while a
   browser route such as `/deployments/example` returns the SPA.
7. Stop and restart the process. Confirm files under
   `%LOCALAPPDATA%\FspOrchestrator\Development` remain present.

## Installed-service mode

Build and install the WiX package as documented in
[Windows installer](../setup/windows-installer.md). Confirm:

1. Windows requests approval for the per-machine installation.
2. The **FSP Orchestrator** service is running and set to automatic startup.
3. `https://localhost:9443` is trusted and the HTTPS checks above pass.
4. Repair restores a removed application file.
5. Upgrade preserves `%PROGRAMDATA%\FspOrchestrator`.
6. Uninstall offers metadata retention and removes the installer-managed
   certificate.

Do not configure a wildcard or LAN listener as a workaround. Startup must fail
when the certificate or fixed loopback origin is unavailable.
