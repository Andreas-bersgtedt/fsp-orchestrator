namespace Fsp.Orchestrator.Application.Persistence;

public interface IInventoryStore
{
    Task<IReadOnlyList<DeploymentInventoryItem>> ListAsync(
        CancellationToken cancellationToken);

    Task<DeploymentInventoryItem?> GetAsync(
        Guid deploymentId,
        CancellationToken cancellationToken);

    Task<bool> ExistsByManagerEndpointAsync(
        Uri managerEndpoint,
        CancellationToken cancellationToken);

    Task<DeploymentInventoryItem> RegisterAsync(
        RegisterDeployment request,
        CancellationToken cancellationToken);

    Task<DeploymentInventoryItem> UpsertAsync(
        UpsertDeployment request,
        CancellationToken cancellationToken);

    Task<DeploymentObservation> RecordObservationAsync(
        RecordObservation request,
        CancellationToken cancellationToken);
}

public interface IJobStore
{
    Task<IReadOnlyList<JobInfo>> ListForDeploymentAsync(
        Guid deploymentId,
        CancellationToken cancellationToken);

    Task<JobInfo> CreateAsync(
        CreateJob request,
        CancellationToken cancellationToken);

    Task<JobInfo> TransitionAsync(
        TransitionJob request,
        CancellationToken cancellationToken);
}

public interface IAuditMetadataStore
{
    Task<AuditMetadata> AppendAsync(
        AppendAuditMetadata request,
        CancellationToken cancellationToken);
}

public interface IDatabaseMaintenance
{
    Task MigrateAsync(CancellationToken cancellationToken);

    Task<DatabaseDiagnostics> GetDiagnosticsAsync(
        CancellationToken cancellationToken);

    Task<DatabaseBackupInfo> CreateBackupAsync(
        CancellationToken cancellationToken);

    Task RestoreBackupAsync(
        string fileName,
        string expectedSha256,
        CancellationToken cancellationToken);
}

public interface IPersistenceValueGuard
{
    void EnsureSafe(params string?[] values);
}
