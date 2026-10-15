namespace Fsp.Orchestrator.Application.Deployments;

public interface IReleaseCompatibilityPolicy
{
    ReleaseCompatibilityRequirements Requirements { get; }
}

public interface IDeploymentMutationGate
{
    Task<DeploymentCompatibilityResult?> EvaluateAsync(
        Guid deploymentId,
        CancellationToken cancellationToken);
}
