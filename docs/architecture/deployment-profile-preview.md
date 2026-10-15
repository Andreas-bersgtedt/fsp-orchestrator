# Deployment profile preview

Status: accepted  
Date: 2026-10-15

## Profile contract

The deployment profile stores typed Azure input:

- subscription, resource group, deployment, AKS, virtual-network, and subnet
  names;
- region, Kubernetes version, node VM SKU, node count, and requested regional
  vCPU;
- VNet, AKS subnet, pod, and service CIDRs plus the DNS service IP;
- private DNS zone;
- optional existing VNet, Container Registry, and Key Vault resource IDs.

Validation rejects invalid Azure names, region syntax, VM SKU syntax, CIDRs,
overlapping ranges, DNS addresses outside the service CIDR, and resource IDs
with the wrong subscription or resource type. The deployment prerequisite run
checks provider registration, permissions, regional Kubernetes availability,
and quota through ARM.

The profile fingerprint is SHA-256 over the sorted typed input. Updating a
profile with a different fingerprint clears its prerequisite run, current
preview, and current approval in one SQLite transaction. The old approval row
is retained with `InvalidatedAt` for audit history.

## ARM preview

The service creates the ARM template in memory. Its `_generator` metadata marks
the template as Bicep-generated input. Runtime code does not read a Bicep file,
an FSP checkout, a deployment directory, a pod volume, or any other remote
filesystem.

The preview endpoint:

1. checks that the supplied prerequisite run matches the current profile input;
2. retrieves the signed-in operator's delegated ARM token from the in-memory
   Azure session;
3. calls the resource-group deployment `validate` API;
4. calls the resource-group deployment `whatIf` API and follows only HTTPS
   polling URLs on `management.azure.com`;
5. stores each resource ID, change type, before value, after value, and delta as
   a structured change row.

Access tokens and ARM error bodies are not stored. Persisted ARM metadata is
limited to correlation IDs and the structured what-if result.

## Approval

Approval is valid only for the current profile fingerprint and preview ID. The
approval row records:

- signed-in tenant and object IDs;
- create, modify, and delete counts copied from the preview;
- profile fingerprint, preview ID, and approval time.

Running another what-if invalidates the prior approval even when the profile
input did not change. The deployment state machine can therefore require the
current approval ID without interpreting a provider response again.

## API

```text
GET  /api/deployment-profiles
PUT  /api/deployment-profiles/{profileId}
GET  /api/deployment-profiles/{profileId}
POST /api/deployment-profiles/{profileId}/preview
POST /api/deployment-profiles/{profileId}/approval
```

All routes require `DeploymentAdmin`. Starting and reading the prerequisite run
also require the existing `Operator` and `Reader` policies.

