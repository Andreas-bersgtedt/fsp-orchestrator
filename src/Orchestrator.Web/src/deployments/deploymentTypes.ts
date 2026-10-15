export type Deployment = {
  id: string
  name: string
  topology: string
  managerEndpoint: string
  azureResourceId?: string
  credentialKind?: string
  credentialReference?: string
  createdAt: string
  updatedAt: string
  version: number
}

export type ObservedValue = {
  name: string
  version: string
  status: string
}

export type ObservationAttempt = {
  id: string
  status: string
  code: string
  startedAt: string
  completedAt: string
  values: ObservedValue[]
}

export type ObservationState = {
  deploymentId: string
  latestAttempt?: ObservationAttempt
  lastSuccessfulAt?: string
  lastSuccessfulValues: ObservedValue[]
}

export type DeploymentListItem = {
  deployment: Deployment
  observation?: ObservationState
  compatibility?: {
    allowed: boolean
    status: string
    code: string
    message: string
    observedAt?: string
    mismatches: {
      component: string
      expectedVersion: string
      observedVersion: string
    }[]
  }
}

export type DeploymentListResponse = {
  deployments: DeploymentListItem[]
  staleAfterSeconds: number
  checkedAt: string
}
