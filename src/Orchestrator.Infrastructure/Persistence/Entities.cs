namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class DeploymentEntity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Topology { get; set; }

    public required string ManagerEndpoint { get; set; }

    public string? AzureResourceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }

    public List<ObservationEntity> Observations { get; } = [];

    public List<JobEntity> Jobs { get; } = [];
}

internal sealed class ObservationEntity
{
    public Guid Id { get; set; }

    public Guid DeploymentId { get; set; }

    public required string Component { get; set; }

    public required string Version { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public DeploymentEntity? Deployment { get; set; }
}

internal sealed class JobEntity
{
    public Guid Id { get; set; }

    public Guid? DeploymentId { get; set; }

    public required string Kind { get; set; }

    public required string IdempotencyKey { get; set; }

    public Application.Persistence.JobStatus Status { get; set; }

    public string? SafeOutput { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }

    public DeploymentEntity? Deployment { get; set; }

    public List<JobStepEntity> Steps { get; } = [];
}

internal sealed class JobStepEntity
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public int Sequence { get; set; }

    public required string Name { get; set; }

    public Application.Persistence.JobStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public string? SafeOutput { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public JobEntity? Job { get; set; }
}

internal sealed class AuditEventEntity
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string ActorObjectId { get; set; }

    public required string TenantId { get; set; }

    public required string Action { get; set; }

    public required string TargetType { get; set; }

    public required string TargetId { get; set; }

    public required string Result { get; set; }

    public required string CorrelationId { get; set; }

    public required string InputHash { get; set; }
}
