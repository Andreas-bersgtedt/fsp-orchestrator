# FSP contracts

The `3.1.0` directory contains the release manifest and OpenAPI documents used
by the Orchestrator. The OpenAPI documents are generated from Fabric Shortcut
Proxy commit `8bb0474558c274666d6625a763a8ca9f02481c83`, the source commit for the
3.1.0 release.

Generate the documents from an exact checkout:

```powershell
python scripts\export-fsp-openapi.py `
  --fsp-source C:\path\to\Fabric-Shortcut-Proxy
```

The exporter records authentication implemented by FSP middleware because
FastAPI cannot infer those boundaries from route declarations. Generated files
must not be edited manually.

The revision and Problem Details contract required by the Orchestrator is an
explicit compatibility overlay. It is not implemented by the immutable 3.1.0
runtime. Upstream implementation is tracked in
[Fabric Shortcut Proxy #176](https://github.com/Andreas-bersgtedt/Fabric-Shortcut-Proxy/issues/176).

Generate and compile the typed clients:

```powershell
npm ci
npm run generate:fsp
npm run typecheck
```

Run the clients against exact Lite and Enterprise 3.1.0 processes:

```powershell
$env:FSP_SOURCE = "C:\path\to\Fabric-Shortcut-Proxy"
npm run test:fsp-runtime
```
