import { describe, expect, it } from 'vitest'
import {
  defaultDeploymentProfile,
  toPrerequisiteInputs,
} from './deploymentProfile.ts'

describe('deployment profile prerequisite inputs', () => {
  it('maps every material field and normalizes optional resource IDs', () => {
    const inputs = toPrerequisiteInputs(defaultDeploymentProfile)

    expect(inputs.nodeCount).toBe('3')
    expect(inputs.nodeVmSku).toBe('Standard_D4s_v5')
    expect(inputs.aksSubnetCidr).toBe('10.0.0.0/24')
    expect(inputs.existingVnetResourceId).toBe('')
    expect(Object.keys(inputs)).toHaveLength(21)
  })
})
