import type { DeploymentListItem } from './deploymentTypes.ts'

export type DisplayStatus =
  | 'Healthy'
  | 'Stale'
  | 'Unreachable'
  | 'Authentication failed'
  | 'Incompatible'
  | 'Invalid response'
  | 'Not observed'

export function deploymentDisplayStatus(
  item: DeploymentListItem,
  staleAfterSeconds: number,
  nowMilliseconds = Date.now(),
): DisplayStatus {
  const latest = item.observation?.latestAttempt
  if (!latest) {
    return 'Not observed'
  }

  switch (latest.status) {
    case 'AuthenticationFailed':
      return 'Authentication failed'
    case 'Unreachable':
      return 'Unreachable'
    case 'Incompatible':
      return 'Incompatible'
    case 'InvalidResponse':
      return 'Invalid response'
    case 'Healthy': {
      const ageMilliseconds =
        nowMilliseconds - new Date(latest.completedAt).getTime()
      return ageMilliseconds > staleAfterSeconds * 1000 ? 'Stale' : 'Healthy'
    }
    default:
      return 'Invalid response'
  }
}

export function statusAnnouncement(
  item: DeploymentListItem,
  staleAfterSeconds: number,
  nowMilliseconds = Date.now(),
) {
  const status = deploymentDisplayStatus(
    item,
    staleAfterSeconds,
    nowMilliseconds,
  )
  const collected = item.observation?.latestAttempt?.completedAt
  return collected
    ? `${item.deployment.name}: ${status}. Collected ${formatTime(collected)}.`
    : `${item.deployment.name}: ${status}.`
}

export function formatTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value))
}
