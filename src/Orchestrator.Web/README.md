# FSP Orchestrator Web

React and TypeScript operator shell for FSP Orchestrator. Vite provides the
development server and production bundle; Fluent UI supplies accessible base
components.

The API project owns production builds of this application. From the repository
root, `dotnet build Fsp.Orchestrator.slnx` installs dependencies, builds the SPA,
and copies the output beside the API.

For frontend-only development:

```powershell
npm install
npm run dev
```

The Vite development server proxies `/api` and `/openapi` to
`http://localhost:5286`.
