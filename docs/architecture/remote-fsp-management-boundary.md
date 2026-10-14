# Remote FSP management boundary

Status: accepted  
Date: 2026-10-14

## Context

FSP Orchestrator runs on a standalone Windows computer and manages Lite and
Enterprise deployments that can be on other hosts or in private AKS clusters.
A design that reads an FSP checkout, installation directory, pod volume,
configuration file, or Helm working directory would couple the Orchestrator to
one machine layout and would not work for remote or private deployments.

## Decision

Production Orchestrator code manages FSP only through authenticated remote
control planes:

- Azure discovery, validation, what-if, deployment, and resource lifecycle use
  Azure Resource Manager, Resource Graph, and deployment APIs.
- Helm operations against private AKS use the Azure AKS command API. They do not
  require a shared filesystem or a kubeconfig on the Orchestrator host.
- FSP registration, health, fleet, queue, backup, restore, configuration, and
  source inspection use versioned FSP HTTPS APIs.
- Deployment secrets are written through the Azure Key Vault API. FSP receives
  references or workload identities, not files copied by the Orchestrator.
- Deployment records store Azure resource IDs, FSP endpoint URIs, contract
  versions, and immutable artifact digests. They do not store deployment paths.

The Orchestrator must not mount, browse, read, or write an FSP host filesystem,
container filesystem, Kubernetes volume, or source checkout. It must not depend
on files such as `config.*.json` being locally reachable.

Local paths are allowed only for the Orchestrator's own database, logs,
installer files, and isolated development or contract-test fixtures. Test
fixtures must be named as test-only inputs and must not be referenced by
production application projects.

## Consequences

- Existing FSP deployments can be registered with an endpoint and identity
  information without access to their hosts.
- Private AKS deployments remain manageable through Azure control-plane APIs.
- Missing FSP operations require a versioned API addition; filesystem access is
  not an acceptable fallback.
- Contract tests may launch an exact FSP build from a local fixture, but all
  assertions communicate with that process over HTTP.
