export type DeploymentProfile = {
  id?: string
  name: string
  subscriptionId: string
  resourceGroupName: string
  deploymentName: string
  location: string
  kubernetesVersion: string
  nodeVmSku: string
  nodeCount: number
  requestedRegionalVcpu: number
  aksName: string
  vnetName: string
  subnetName: string
  vnetCidr: string
  aksSubnetCidr: string
  podCidr: string
  serviceCidr: string
  dnsServiceIp: string
  privateDnsZoneName: string
  existingVnetResourceId?: string
  existingContainerRegistryResourceId?: string
  existingKeyVaultResourceId?: string
}

export type DeploymentWhatIfChange = {
  resourceId: string
  changeType:
    | 'Create'
    | 'Modify'
    | 'Delete'
    | 'NoChange'
    | 'Ignore'
    | 'Deploy'
    | 'Unsupported'
  beforeJson?: string
  afterJson?: string
  deltaJson?: string
}

export type DeploymentPreview = {
  id: string
  profileId: string
  profileFingerprint: string
  prerequisiteRunId: string
  validationCorrelationId: string
  whatIfCorrelationId: string
  createCount: number
  modifyCount: number
  deleteCount: number
  changes: DeploymentWhatIfChange[]
  createdAt: string
}

export type DeploymentApproval = {
  id: string
  profileId: string
  previewId: string
  profileFingerprint: string
  actorTenantId: string
  actorObjectId: string
  createCount: number
  modifyCount: number
  deleteCount: number
  approvedAt: string
  invalidatedAt?: string
}

export type DeploymentProfileDocument = {
  profile: DeploymentProfile & { id: string }
  fingerprint: string
  revision: number
  prerequisiteRunId?: string
  currentPreviewId?: string
  currentApprovalId?: string
  createdAt: string
  updatedAt: string
}

export type DeploymentProfileState = {
  document: DeploymentProfileDocument
  preview?: DeploymentPreview
  approval?: DeploymentApproval
}

export const defaultDeploymentProfile: DeploymentProfile = {
  name: 'Production FSP',
  subscriptionId: '',
  resourceGroupName: 'fsp-rg',
  deploymentName: 'fsp-deployment',
  location: 'swedencentral',
  kubernetesVersion: '1.32.7',
  nodeVmSku: 'Standard_D4s_v5',
  nodeCount: 3,
  requestedRegionalVcpu: 12,
  aksName: 'fsp-aks',
  vnetName: 'fsp-vnet',
  subnetName: 'aks-subnet',
  vnetCidr: '10.0.0.0/16',
  aksSubnetCidr: '10.0.0.0/24',
  podCidr: '10.1.0.0/16',
  serviceCidr: '10.2.0.0/16',
  dnsServiceIp: '10.2.0.10',
  privateDnsZoneName: 'privatelink.swedencentral.azmk8s.io',
}

export function toPrerequisiteInputs(
  profile: DeploymentProfile,
): Record<string, string> {
  return {
    profileName: profile.name,
    subscriptionId: profile.subscriptionId,
    resourceGroupName: profile.resourceGroupName,
    deploymentName: profile.deploymentName,
    location: profile.location,
    kubernetesVersion: profile.kubernetesVersion,
    nodeVmSku: profile.nodeVmSku,
    nodeCount: String(profile.nodeCount),
    requestedRegionalVcpu: String(profile.requestedRegionalVcpu),
    aksName: profile.aksName,
    vnetName: profile.vnetName,
    subnetName: profile.subnetName,
    vnetCidr: profile.vnetCidr,
    aksSubnetCidr: profile.aksSubnetCidr,
    podCidr: profile.podCidr,
    serviceCidr: profile.serviceCidr,
    dnsServiceIp: profile.dnsServiceIp,
    privateDnsZoneName: profile.privateDnsZoneName,
    existingVnetResourceId: profile.existingVnetResourceId ?? '',
    existingContainerRegistryResourceId:
      profile.existingContainerRegistryResourceId ?? '',
    existingKeyVaultResourceId: profile.existingKeyVaultResourceId ?? '',
  }
}
