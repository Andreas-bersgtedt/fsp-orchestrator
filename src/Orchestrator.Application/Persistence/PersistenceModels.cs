namespace Fsp.Orchestrator.Application.Persistence;

public sealed record DeploymentInventoryItem(
    Guid Id,
    string Name,
    string Topology,
    Uri ManagerEndpoint,
    string? AzureResourceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record DeploymentObservation(
    Guid Id,
    Guid DeploymentId,
    string Component,
    string Version,
    string Status,
    DateTimeOffset ObservedAt);

public sealed record JobInfo(
    Guid Id,
    Guid? DeploymentId,
    string Kind,
    string IdempotencyKey,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record JobStepInfo(
    Guid Id,
    Guid JobId,
    int Sequence,
    string Name,
    JobStatus Status,
    int AttemptCount,
    string? SafeOutput,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record AuditMetadata(
    long Id,
    DateTimeOffset OccurredAt,
    string ActorObjectId,
    string TenantId,
    string Action,
    string TargetType,
    string TargetId,
    string Result,
    string CorrelationId,
    string InputHash);

public sealed record DatabaseDiagnostics(
    string DatabaseFileName,
    string Integrity,
    string Migration,
    long DeploymentCount,
    long JobCount,
    long AuditEventCount,
    DateTimeOffset CheckedAt);

public sealed record DatabaseBackupInfo(
    string FileName,
    string Sha256,
    long Length,
    DateTimeOffset CreatedAt);

public enum JobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
