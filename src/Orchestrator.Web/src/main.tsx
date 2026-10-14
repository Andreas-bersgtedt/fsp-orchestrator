import { lazy, StrictMode, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'

export const AuthRoot = lazy(async () => {
  const module = await import('./auth/AuthRoot.tsx')
  return { default: module.AuthRoot }
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Suspense fallback={<div className="bootstrap-loading">Loading FSP Orchestrator</div>}>
      <AuthRoot />
    </Suspense>
  </StrictMode>,
)
