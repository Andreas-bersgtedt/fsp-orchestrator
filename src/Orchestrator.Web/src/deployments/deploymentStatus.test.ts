import { describe, expect, it } from 'vitest'
import {
  deploymentDisplayStatus,
  statusAnnouncement,
} from './deploymentStatus.ts'
import type { DeploymentListItem } from './deploymentTypes.ts'

describe('deployment status accessibility', () => {
  it('keeps stale and unreachable labels distinct', () => {
    const now = Date.parse('2026-10-15T16:00:00Z')
    const stale = item('Healthy', '2026-10-15T15:55:00Z')
    const unreachable = item('Unreachable', '2026-10-15T15:59:59Z')

    expect(deploymentDisplayStatus(stale, 120, now)).toBe('Stale')
    expect(deploymentDisplayStatus(unreachable, 120, now)).toBe('Unreachable')
  })

  it('announces status and collection time for screen readers', () => {
    const deployment = item('Healthy', '2026-10-15T15:59:59Z')

    const announcement = statusAnnouncement(
      deployment,
      120,
      Date.parse('2026-10-15T16:00:00Z'),
    )

    expect(announcement).toContain('Test deployment: Healthy.')
    expect(announcement).toContain('Collected')
  })
})

function item(status: string, completedAt: string): DeploymentListItem {
  return {
    deployment: {
      id: '00000000-0000-0000-0000-000000000001',
      name: 'Test deployment',
      topology: 'Lite',
      managerEndpoint: 'https://lite.example.test/',
      createdAt: completedAt,
      updatedAt: completedAt,
      version: 1,
    },
    observation: {
      deploymentId: '00000000-0000-0000-0000-000000000001',
      latestAttempt: {
        id: '00000000-0000-0000-0000-000000000002',
        status,
        code: `observation.${status.toLowerCase()}`,
        startedAt: completedAt,
        completedAt,
        values: [],
      },
      lastSuccessfulAt:
        status === 'Healthy' ? completedAt : '2026-10-15T15:50:00Z',
      lastSuccessfulValues: [],
    },
  }
}
