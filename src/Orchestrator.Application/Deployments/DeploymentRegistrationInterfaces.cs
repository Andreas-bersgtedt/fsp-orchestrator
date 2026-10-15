namespace Fsp.Orchestrator.Application.Deployments;

public interface IDeploymentRegistrationService
{
    Task<DeploymentRegistrationResult> RegisterAsync(
        DeploymentRegistrationRequest request,
        CancellationToken cancellationToken);
}

public interface IFspDeploymentProbe
{
    Task<DeploymentProbeResult> ProbeAsync(
        DeploymentProbeRequest request,
        CancellationToken cancellationToken);
}

public interface IFspCredentialResolver
{
    Task<string?> ResolveAsync(
        FspCredentialKind kind,
        Uri? reference,
        CancellationToken cancellationToken);
}
