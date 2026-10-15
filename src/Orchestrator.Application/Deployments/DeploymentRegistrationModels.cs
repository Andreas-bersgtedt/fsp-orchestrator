using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed record DeploymentRegistrationRequest(
    Guid Id,
    string Name,
    Uri ManagerEndpoint,
    string? AzureResourceId,
    FspCredentialKind CredentialKind,
    Uri? CredentialReference);

public sealed record DeploymentProbeRequest(
    Uri ManagerEndpoint,
    FspCredentialKind CredentialKind,
    Uri? CredentialReference);

public sealed record DeploymentProbeResult(
    FspTopology Topology,
    IReadOnlyList<ObservedComponent> Components);

public sealed record DeploymentRegistrationResult(
    DeploymentInventoryItem Deployment,
    IReadOnlyList<ObservedComponent> Components);

public enum FspTopology
{
    Lite,
    Enterprise,
}

public enum FspCredentialKind
{
    None,
    Bearer,
    Basic,
}

public enum DeploymentRegistrationFailure
{
    InvalidInput,
    DuplicateEndpoint,
    Unreachable,
    TlsValidationFailed,
    IdentityRejected,
    IncompatibleVersion,
    ContractMismatch,
}

public sealed class DeploymentRegistrationException(
    DeploymentRegistrationFailure failure,
    string code,
    string safeMessage)
    : Exception(safeMessage)
{
    public DeploymentRegistrationFailure Failure { get; } = failure;

    public string Code { get; } = code;
}
