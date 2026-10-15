using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.UnitTests;

public sealed class DeploymentObservationServiceTests
{
    [Fact]
    public async Task NetworkFailureRetriesWithBoundedAttemptsThenSucceeds()
    {
        var target = Deployment();
        var probe = new SequenceProbe(
            [
                Failure(
                    DeploymentRegistrationFailure.Unreachable,
                    "deployment.endpoint_unreachable"),
                Failure(
                    DeploymentRegistrationFailure.Unreachable,
                    "deployment.endpoint_unreachable"),
                new DeploymentObservationSnapshot(
                    [new ObservedComponent("health", "ok", "current")]),
            ]);
        var store = new TestObservationStore();
        var service = Service(target, probe, store, maximumAttempts: 3);

        var state = await service.PollAsync(target.Id, CancellationToken.None);

        Assert.Equal(3, probe.Attempts);
        Assert.Equal(
            DeploymentObservationStatus.Healthy,
            state.LatestAttempt!.Status);
        Assert.Single(store.Attempts);
    }

    [Fact]
    public async Task AuthenticationFailureIsRecordedWithoutRetry()
    {
        var target = Deployment();
        var probe = new SequenceProbe(
            [
                Failure(
                    DeploymentRegistrationFailure.IdentityRejected,
                    "deployment.identity_rejected"),
            ]);
        var store = new TestObservationStore();
        var service = Service(target, probe, store, maximumAttempts: 3);

        var state = await service.PollAsync(target.Id, CancellationToken.None);

        Assert.Equal(1, probe.Attempts);
        Assert.Equal(
            DeploymentObservationStatus.AuthenticationFailed,
            state.LatestAttempt!.Status);
        Assert.Equal(
            "observation.authentication_failed",
            state.LatestAttempt.Code);
    }

    [Fact]
    public async Task CancellationDoesNotPersistPartialAttempt()
    {
        var target = Deployment();
        var probe = new CancellingProbe();
        var store = new TestObservationStore();
        var service = Service(target, probe, store, maximumAttempts: 3);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.PollAsync(target.Id, cancellation.Token));

        Assert.Empty(store.Attempts);
    }

    [Fact]
    public async Task ConcurrentPollsForSameTargetDoNotOverlap()
    {
        var target = Deployment();
        var probe = new ConcurrencyProbe();
        var store = new TestObservationStore();
        var service = Service(target, probe, store, maximumAttempts: 1);

        await Task.WhenAll(
            service.PollAsync(target.Id, CancellationToken.None),
            service.PollAsync(target.Id, CancellationToken.None));

        Assert.Equal(1, probe.MaximumConcurrency);
        Assert.Equal(2, store.Attempts.Count);
    }

    [Fact]
    public async Task PollAllUsesConfiguredGlobalConcurrency()
    {
        var targets = Enumerable.Range(0, 5)
            .Select(_ => Deployment())
            .ToArray();
        var probe = new ConcurrencyProbe();
        var store = new TestObservationStore();
        var service = new DeploymentObservationService(
            new TestInventoryStore(targets),
            store,
            probe,
            TimeProvider.System,
            new DeploymentObservationOptions(
                true,
                TimeSpan.FromMinutes(1),
                2,
                1,
                TimeSpan.Zero));

        await service.PollAllAsync(CancellationToken.None);

        Assert.Equal(2, probe.MaximumConcurrency);
        Assert.Equal(5, store.Attempts.Count);
    }

    private static DeploymentObservationService Service(
        DeploymentInventoryItem target,
        IFspObservationProbe probe,
        TestObservationStore store,
        int maximumAttempts) =>
        new(
            new TestInventoryStore([target]),
            store,
            probe,
            TimeProvider.System,
            new DeploymentObservationOptions(
                true,
                TimeSpan.FromMinutes(1),
                2,
                maximumAttempts,
                TimeSpan.Zero));

    private static DeploymentInventoryItem Deployment() =>
        new(
            Guid.NewGuid(),
            "Test",
            "Lite",
            new Uri("https://lite.example.test/"),
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

    private static DeploymentRegistrationException Failure(
        DeploymentRegistrationFailure failure,
        string code) =>
        new(failure, code, "Safe failure.");

    private sealed class SequenceProbe(IReadOnlyList<object> results)
        : IFspObservationProbe
    {
        public int Attempts { get; private set; }

        public Task<DeploymentObservationSnapshot> ObserveAsync(
            DeploymentInventoryItem deployment,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = results[Attempts++];
            return result is DeploymentRegistrationException exception
                ? Task.FromException<DeploymentObservationSnapshot>(exception)
                : Task.FromResult((DeploymentObservationSnapshot)result);
        }
    }

    private sealed class CancellingProbe : IFspObservationProbe
    {
        public async Task<DeploymentObservationSnapshot> ObserveAsync(
            DeploymentInventoryItem deployment,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class ConcurrencyProbe : IFspObservationProbe
    {
        private int current;

        public int MaximumConcurrency { get; private set; }

        public async Task<DeploymentObservationSnapshot> ObserveAsync(
            DeploymentInventoryItem deployment,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref current);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try
            {
                await Task.Delay(30, cancellationToken);
                return new DeploymentObservationSnapshot(
                    [new ObservedComponent("health", "ok", "current")]);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }
    }

    private sealed class TestInventoryStore(
        IReadOnlyList<DeploymentInventoryItem> targets)
        : IInventoryStore
    {
        public Task<IReadOnlyList<DeploymentInventoryItem>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(targets);

        public Task<DeploymentInventoryItem?> GetAsync(
            Guid deploymentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentInventoryItem?>(
                targets.SingleOrDefault(target => target.Id == deploymentId));

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

    private sealed class TestObservationStore : IDeploymentObservationStore
    {
        public List<DeploymentObservationAttempt> Attempts { get; } = [];

        public Task RecordAttemptAsync(
            DeploymentObservationAttempt attempt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<DeploymentObservationState?> GetStateAsync(
            Guid deploymentId,
            CancellationToken cancellationToken)
        {
            var latest = Attempts.LastOrDefault(
                attempt => attempt.DeploymentId == deploymentId);
            var successful = Attempts.LastOrDefault(
                attempt => attempt.DeploymentId == deploymentId
                    && attempt.Status == DeploymentObservationStatus.Healthy);
            return Task.FromResult<DeploymentObservationState?>(
                new DeploymentObservationState(
                    deploymentId,
                    latest,
                    successful?.CompletedAt,
                    successful?.Values ?? []));
        }
    }
}
