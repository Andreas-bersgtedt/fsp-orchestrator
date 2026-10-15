using System.Collections.Concurrent;
using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed class DeploymentObservationService(
    IInventoryStore inventoryStore,
    IDeploymentObservationStore observationStore,
    IFspObservationProbe probe,
    TimeProvider timeProvider,
    DeploymentObservationOptions options)
    : IDeploymentObservationService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> targetGates = new();
    private readonly DeploymentObservationOptions options = Validate(options);

    public async Task PollAllAsync(CancellationToken cancellationToken)
    {
        var targets = await inventoryStore.ListAsync(cancellationToken);
        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = options.MaximumConcurrency,
            },
            async (target, token) =>
            {
                await PollTargetAsync(target, skipIfRunning: true, token);
            });
    }

    public async Task<DeploymentObservationState> PollAsync(
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        var target = await inventoryStore.GetAsync(
            deploymentId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Deployment '{deploymentId}' is not registered.");
        await PollTargetAsync(target, skipIfRunning: false, cancellationToken);
        return await observationStore.GetStateAsync(
            deploymentId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Observation state for '{deploymentId}' was not persisted.");
    }

    private async Task PollTargetAsync(
        DeploymentInventoryItem target,
        bool skipIfRunning,
        CancellationToken cancellationToken)
    {
        var gate = targetGates.GetOrAdd(target.Id, _ => new SemaphoreSlim(1, 1));
        var entered = skipIfRunning
            ? await gate.WaitAsync(0, cancellationToken)
            : await WaitAsync(gate, cancellationToken);
        if (!entered)
        {
            return;
        }

        try
        {
            await ExecuteAttemptsAsync(target, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ExecuteAttemptsAsync(
        DeploymentInventoryItem target,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        for (var attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                var snapshot = await probe.ObserveAsync(
                    target,
                    cancellationToken);
                await observationStore.RecordAttemptAsync(
                    new DeploymentObservationAttempt(
                        Guid.NewGuid(),
                        target.Id,
                        DeploymentObservationStatus.Healthy,
                        "observation.healthy",
                        startedAt,
                        timeProvider.GetUtcNow(),
                        snapshot.Values),
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DeploymentRegistrationException exception)
            {
                var status = Map(exception.Failure);
                if (status == DeploymentObservationStatus.Unreachable
                    && attempt < options.MaximumAttempts)
                {
                    var delay = TimeSpan.FromTicks(
                        options.InitialBackoff.Ticks * (1L << (attempt - 1)));
                    await Task.Delay(delay, timeProvider, cancellationToken);
                    continue;
                }

                await observationStore.RecordAttemptAsync(
                    new DeploymentObservationAttempt(
                        Guid.NewGuid(),
                        target.Id,
                        status,
                        ObservationCode(status),
                        startedAt,
                        timeProvider.GetUtcNow(),
                        []),
                    cancellationToken);
                return;
            }
        }
    }

    private static async Task<bool> WaitAsync(
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        return true;
    }

    private static DeploymentObservationStatus Map(
        DeploymentRegistrationFailure failure) =>
        failure switch
        {
            DeploymentRegistrationFailure.IdentityRejected =>
                DeploymentObservationStatus.AuthenticationFailed,
            DeploymentRegistrationFailure.Unreachable
                or DeploymentRegistrationFailure.TlsValidationFailed =>
                DeploymentObservationStatus.Unreachable,
            DeploymentRegistrationFailure.IncompatibleVersion =>
                DeploymentObservationStatus.Incompatible,
            _ => DeploymentObservationStatus.InvalidResponse,
        };

    private static string ObservationCode(DeploymentObservationStatus status) =>
        status switch
        {
            DeploymentObservationStatus.AuthenticationFailed =>
                "observation.authentication_failed",
            DeploymentObservationStatus.Unreachable =>
                "observation.unreachable",
            DeploymentObservationStatus.Incompatible =>
                "observation.incompatible",
            _ => "observation.invalid_response",
        };

    private static DeploymentObservationOptions Validate(
        DeploymentObservationOptions value)
    {
        if (value.Interval <= TimeSpan.Zero
            || value.MaximumConcurrency is < 1 or > 32
            || value.MaximumAttempts is < 1 or > 10
            || value.InitialBackoff < TimeSpan.Zero
            || value.InitialBackoff > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Observation scheduling options are outside supported bounds.");
        }

        return value;
    }
}
