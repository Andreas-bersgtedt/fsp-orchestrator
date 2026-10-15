namespace Fsp.Orchestrator.Application.Deployments;

public sealed record ReleaseCompatibilityRequirements(
    string ReleaseVersion,
    string LiteVersion,
    string ManagerVersion,
    bool ComponentsMoveTogether);

public sealed record ComponentVersionMismatch(
    string Component,
    string ExpectedVersion,
    string ObservedVersion);

public sealed record DeploymentCompatibilityResult(
    Guid DeploymentId,
    bool Allowed,
    DeploymentCompatibilityStatus Status,
    string Code,
    string Message,
    DateTimeOffset? ObservedAt,
    IReadOnlyList<ComponentVersionMismatch> Mismatches);

public enum DeploymentCompatibilityStatus
{
    Compatible,
    Mismatched,
    ObservationMissing,
    ObservationStale,
    ObservationUnavailable,
}
