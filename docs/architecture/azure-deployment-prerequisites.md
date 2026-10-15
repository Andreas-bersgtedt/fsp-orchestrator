# Azure deployment prerequisites

Status: accepted  
Date: 2026-10-15

## Checker set

The `deploy` operation adds these blocking checks:

| Check | Inputs | Result |
| --- | --- | --- |
| `azure.session` | authenticated tenant and object ID | Active delegated ARM session |
| `azure.providers` | subscription ID | Registration state for Container Service, Compute, Key Vault, Managed Identity, and Network |
| `azure.permissions` | subscription and resource group | Exact denied action and evaluated scope |
| `azure.quota` | subscription, region, requested vCPUs | Current use, limit, and requested amount |
| `azure.region` | subscription, region, Kubernetes version | Selected version availability |
| `azure.naming` | resource group, AKS, VNet, and subnet names | Azure name and length validation |
| `azure.network` | VNet, subnet, pod, service, DNS IP, private DNS, and region | Subnet containment, CIDR overlap, DNS IP membership, and regional private DNS |

ARM response bodies and access tokens are not persisted as evidence. Provider
errors become blocking `Unknown` results with stable codes.

## Permission actions

The permission check evaluates these actions at the selected resource-group
scope:

- `Microsoft.Authorization/roleAssignments/write`
- `Microsoft.ContainerService/managedClusters/read`
- `Microsoft.ContainerService/managedClusters/runCommand/action`
- `Microsoft.ContainerService/managedClusters/write`
- `Microsoft.ManagedIdentity/userAssignedIdentities/assign/action`
- `Microsoft.Network/virtualNetworks/subnets/join/action`
- `Microsoft.Resources/deployments/validate/action`
- `Microsoft.Resources/deployments/write`

The result names the first denied action and the exact ARM scope.

## Apply freshness

Quota and regional Kubernetes-version checks have a 30-second apply-freshness
window. The prerequisite gate returns
`prerequisite.immediate_recheck_required` after that window, even though the
normal five-minute result lifetime has not expired. Apply cannot continue until
those checks run again.
