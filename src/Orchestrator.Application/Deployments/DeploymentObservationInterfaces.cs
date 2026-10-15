using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Deployments;

public interface IFspObservationProbe
{
    Task<DeploymentObservationSnapshot> ObserveAsync(
        DeploymentInventoryItem deployment,
        CancellationToken cancellationToken);
}

public interface IDeploymentObservationStore
{
    Task RecordAttemptAsync(
        DeploymentObservationAttempt attempt,
        CancellationToken cancellationToken);

    Task<DeploymentObservationState?> GetStateAsync(
        Guid deploymentId,
        CancellationToken cancellationToken);
}

public interface IDeploymentObservationService
{
    Task PollAllAsync(CancellationToken cancellationToken);

    Task<DeploymentObservationState> PollAsync(
        Guid deploymentId,
        CancellationToken cancellationToken);
}
