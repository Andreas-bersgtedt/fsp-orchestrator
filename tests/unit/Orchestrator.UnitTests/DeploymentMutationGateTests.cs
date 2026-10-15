using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.UnitTests;

public sealed class DeploymentMutationGateTests
{
    [Fact]
    public async Task MixedObservedVersionsReturnStableMismatchCode()
    {
        var now = DateTimeOffset.UtcNow;
        var deployment = Deployment("Enterprise");
        var gate = Gate(
            deployment,
            State(
                deployment.Id,
                now,
                [
                    new ObservedComponent("manager", "3.1.0", "current"),
                    new ObservedComponent("agent-a", "3.0.1", "observed"),
                ]),
            now);

        var result = await gate.EvaluateAsync(
            deployment.Id,
            CancellationToken.None);

        Assert.False(result!.Allowed);
        Assert.Equal(
            "deployment.component_version_mismatch",
            result.Code);
        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal("agent-a", mismatch.Component);
        Assert.Equal("3.1.0", mismatch.ExpectedVersion);
        Assert.Equal("3.0.1", mismatch.ObservedVersion);
    }

    [Fact]
    public async Task MatchingLiteVersionAllowsMutation()
    {
        var now = DateTimeOffset.UtcNow;
        var deployment = Deployment("Lite");
        var gate = Gate(
            deployment,
            State(
                deployment.Id,
                now,
                [new ObservedComponent("lite", "3.1.0", "current")]),
            now);

        var result = await gate.EvaluateAsync(
            deployment.Id,
            CancellationToken.None);

        Assert.True(result!.Allowed);
        Assert.Equal("deployment.compatible", result.Code);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public async Task StaleAndUnavailableObservationsBlockMutation()
    {
        var now = DateTimeOffset.UtcNow;
        var deployment = Deployment("Lite");
        var staleGate = Gate(
            deployment,
            State(
                deployment.Id,
                now.AddMinutes(-3),
                [new ObservedComponent("lite", "3.1.0", "current")]),
            now);
        var unavailableState = State(
            deployment.Id,
            now,
            [new ObservedComponent("lite", "3.1.0", "current")]) with
        {
            LatestAttempt = new DeploymentObservationAttempt(
                Guid.NewGuid(),
                deployment.Id,
                DeploymentObservationStatus.Unreachable,
                "observation.unreachable",
                now,
                now,
                []),
        };
        var unavailableGate = Gate(deployment, unavailableState, now);

        var stale = await staleGate.EvaluateAsync(
            deployment.Id,
            CancellationToken.None);
        var unavailable = await unavailableGate.EvaluateAsync(
            deployment.Id,
            CancellationToken.None);

        Assert.Equal("deployment.observation_stale", stale!.Code);
        Assert.Equal(
            "deployment.observation_unavailable",
            unavailable!.Code);
    }

    private static DeploymentMutationGate Gate(
        DeploymentInventoryItem deployment,
        DeploymentObservationState state,
        DateTimeOffset now) =>
        new(
            new TestInventoryStore(deployment),
            new TestObservationStore(state),
            new TestReleasePolicy(),
            new DeploymentObservationOptions(
                true,
                TimeSpan.FromMinutes(1),
                4,
                3,
                TimeSpan.FromSeconds(1)),
            new FixedTimeProvider(now));

    private static DeploymentInventoryItem Deployment(string topology) =>
        new(
            Guid.NewGuid(),
            "Test",
            topology,
            new Uri("https://fsp.example.test/"),
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

    private static DeploymentObservationState State(
        Guid deploymentId,
        DateTimeOffset observedAt,
        IReadOnlyList<ObservedComponent> values)
    {
        var attempt = new DeploymentObservationAttempt(
            Guid.NewGuid(),
            deploymentId,
            DeploymentObservationStatus.Healthy,
            "observation.healthy",
            observedAt,
            observedAt,
            values);
        return new DeploymentObservationState(
            deploymentId,
            attempt,
            observedAt,
            values);
    }

    private sealed class TestReleasePolicy : IReleaseCompatibilityPolicy
    {
        public ReleaseCompatibilityRequirements Requirements { get; } =
            new("3.1.0", "3.1.0", "3.1.0", true);
    }

    private sealed class TestObservationStore(DeploymentObservationState state)
        : IDeploymentObservationStore
    {
        public Task RecordAttemptAsync(
            DeploymentObservationAttempt attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeploymentObservationState?> GetStateAsync(
            Guid deploymentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentObservationState?>(
                deploymentId == state.DeploymentId ? state : null);
    }

    private sealed class TestInventoryStore(DeploymentInventoryItem deployment)
        : IInventoryStore
    {
        public Task<IReadOnlyList<DeploymentInventoryItem>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeploymentInventoryItem>>([deployment]);

        public Task<DeploymentInventoryItem?> GetAsync(
            Guid deploymentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentInventoryItem?>(
                deploymentId == deployment.Id ? deployment : null);

        public Task<bool> ExistsByManagerEndpointAsync(
            Uri managerEndpoint,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeploymentInventoryItem> RegisterAsync(
            RegisterDeployment request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeploymentInventoryItem> UpsertAsync(
            UpsertDeployment request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeploymentObservation> RecordObservationAsync(
            RecordObservation request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
