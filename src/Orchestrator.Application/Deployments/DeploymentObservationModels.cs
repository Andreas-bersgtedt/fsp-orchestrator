using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed record DeploymentObservationSnapshot(
    IReadOnlyList<ObservedComponent> Values);

public sealed record DeploymentObservationAttempt(
    Guid Id,
    Guid DeploymentId,
    DeploymentObservationStatus Status,
    string Code,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ObservedComponent> Values);

public sealed record DeploymentObservationState(
    Guid DeploymentId,
    DeploymentObservationAttempt? LatestAttempt,
    DateTimeOffset? LastSuccessfulAt,
    IReadOnlyList<ObservedComponent> LastSuccessfulValues);

public enum DeploymentObservationStatus
{
    Healthy,
    AuthenticationFailed,
    Unreachable,
    Incompatible,
    InvalidResponse,
}

public sealed record DeploymentObservationOptions(
    bool Enabled,
    TimeSpan Interval,
    int MaximumConcurrency,
    int MaximumAttempts,
    TimeSpan InitialBackoff)
{
    public static DeploymentObservationOptions Default { get; } =
        new(
            true,
            TimeSpan.FromMinutes(1),
            4,
            3,
            TimeSpan.FromSeconds(1));
}
