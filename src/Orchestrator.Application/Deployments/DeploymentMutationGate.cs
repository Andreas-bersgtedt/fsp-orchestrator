using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed class DeploymentMutationGate(
    IInventoryStore inventoryStore,
    IDeploymentObservationStore observationStore,
    IReleaseCompatibilityPolicy releasePolicy,
    DeploymentObservationOptions observationOptions,
    TimeProvider timeProvider)
    : IDeploymentMutationGate
{
    private static readonly HashSet<string> NonVersionValues =
    [
        "fleet",
        "health",
        "queue",
        "readiness",
    ];

    public async Task<DeploymentCompatibilityResult?> EvaluateAsync(
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        var deployment = await inventoryStore.GetAsync(
            deploymentId,
            cancellationToken);
        if (deployment is null)
        {
            return null;
        }

        var state = await observationStore.GetStateAsync(
            deploymentId,
            cancellationToken);
        if (state?.LatestAttempt is null || state.LastSuccessfulAt is null)
        {
            return Denied(
                deploymentId,
                DeploymentCompatibilityStatus.ObservationMissing,
                "deployment.observation_missing",
                "No successful deployment observation is available.",
                state?.LastSuccessfulAt);
        }

        if (state.LatestAttempt.Status != DeploymentObservationStatus.Healthy)
        {
            return Denied(
                deploymentId,
                DeploymentCompatibilityStatus.ObservationUnavailable,
                "deployment.observation_unavailable",
                "The latest deployment observation did not succeed.",
                state.LastSuccessfulAt);
        }

        var staleAfter = TimeSpan.FromTicks(
            observationOptions.Interval.Ticks * 2);
        if (state.LatestAttempt.CompletedAt + staleAfter
            <= timeProvider.GetUtcNow())
        {
            return Denied(
                deploymentId,
                DeploymentCompatibilityStatus.ObservationStale,
                "deployment.observation_stale",
                "The latest deployment observation is stale.",
                state.LastSuccessfulAt);
        }

        var requirements = releasePolicy.Requirements;
        var versions = state.LastSuccessfulValues
            .Where(value => !NonVersionValues.Contains(value.Name))
            .ToArray();
        var requiredComponent = string.Equals(
            deployment.Topology,
            FspTopology.Lite.ToString(),
            StringComparison.Ordinal)
            ? "lite"
            : "manager";
        var expectedBaseVersion = requiredComponent == "lite"
            ? requirements.LiteVersion
            : requirements.ManagerVersion;
        var mismatches = versions
            .Select(
                value => new ComponentVersionMismatch(
                    value.Name,
                    string.Equals(
                        value.Name,
                        requiredComponent,
                        StringComparison.Ordinal)
                        ? expectedBaseVersion
                        : requirements.ComponentsMoveTogether
                            ? requirements.ReleaseVersion
                            : value.Version,
                    value.Version))
            .Where(
                mismatch => !string.Equals(
                    mismatch.ExpectedVersion,
                    mismatch.ObservedVersion,
                    StringComparison.Ordinal))
            .ToList();
        if (!versions.Any(
                value => string.Equals(
                    value.Name,
                    requiredComponent,
                    StringComparison.Ordinal)))
        {
            mismatches.Add(
                new ComponentVersionMismatch(
                    requiredComponent,
                    expectedBaseVersion,
                    "missing"));
        }

        if (mismatches.Count > 0)
        {
            return new DeploymentCompatibilityResult(
                deploymentId,
                false,
                DeploymentCompatibilityStatus.Mismatched,
                "deployment.component_version_mismatch",
                "One or more observed FSP components do not match the release manifest.",
                state.LastSuccessfulAt,
                mismatches);
        }

        return new DeploymentCompatibilityResult(
            deploymentId,
            true,
            DeploymentCompatibilityStatus.Compatible,
            "deployment.compatible",
            "Observed FSP components match the release manifest.",
            state.LastSuccessfulAt,
            []);
    }

    private static DeploymentCompatibilityResult Denied(
        Guid deploymentId,
        DeploymentCompatibilityStatus status,
        string code,
        string message,
        DateTimeOffset? observedAt) =>
        new(
            deploymentId,
            false,
            status,
            code,
            message,
            observedAt,
            []);
}
