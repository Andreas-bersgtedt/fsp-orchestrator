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

The installer workflow will automate certificate creation, trust, private-key
permissions, service registration, repair, and removal. Until that workflow is
implemented, service-mode smoke testing requires an administrator to:

1. publish the application;
2. install and trust a `localhost` certificate in `LocalMachine\My`;
3. grant the service identity read access to its private key;
4. configure `Orchestrator:Certificate:Thumbprint` and
   `Orchestrator:Certificate:StoreLocation=LocalMachine`;
5. register `Orchestrator.Api.exe` as the **FSP Orchestrator** Windows Service;
6. start, stop, and restart the service;
7. repeat the HTTPS checks above and confirm `%PROGRAMDATA%\FspOrchestrator`
   survives restart.

Do not configure a wildcard or LAN listener as a workaround. Startup must fail
when the certificate or fixed loopback origin is unavailable.
