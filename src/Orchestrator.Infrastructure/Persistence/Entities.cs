namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class DeploymentEntity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Topology { get; set; }

    public required string ManagerEndpoint { get; set; }

    public string? AzureResourceId { get; set; }

    public string? CredentialKind { get; set; }

    public string? CredentialReference { get; set; }

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

    public Guid? AttemptId { get; set; }

    public DeploymentEntity? Deployment { get; set; }

    public ObservationAttemptEntity? Attempt { get; set; }
}

internal sealed class ObservationAttemptEntity
{
    public Guid Id { get; set; }

    public Guid DeploymentId { get; set; }

    public Application.Deployments.DeploymentObservationStatus Status { get; set; }

    public required string Code { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public long CompletedAtUnixMilliseconds { get; set; }

    public DeploymentEntity? Deployment { get; set; }

    public List<ObservationEntity> Values { get; } = [];
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

internal sealed class PrerequisiteRunEntity
{
    public Guid Id { get; set; }

    public required string Operation { get; set; }

    public Application.Prerequisites.PrerequisiteRunStatus Status { get; set; }

    public required string InputFingerprint { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<PrerequisiteResultEntity> Results { get; } = [];
}

internal sealed class PrerequisiteResultEntity
{
    public Guid RunId { get; set; }

    public required string CheckId { get; set; }

    public required string Category { get; set; }

    public bool Blocking { get; set; }

    public Application.Prerequisites.PrerequisiteCheckStatus Status { get; set; }

    public required string Code { get; set; }

    public required string Summary { get; set; }

    public required string EvidenceJson { get; set; }

    public required string Remediation { get; set; }

    public required string InputFingerprint { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public long DurationMilliseconds { get; set; }

    public string? WarningAcceptedByObjectId { get; set; }

    public string? WarningAcceptanceReason { get; set; }

    public DateTimeOffset? WarningAcceptedAt { get; set; }

    public PrerequisiteRunEntity? Run { get; set; }
}

internal sealed class DeploymentProfileEntity
{
    public Guid Id { get; set; }

    public required string ProfileJson { get; set; }

    public required string Fingerprint { get; set; }

    public long Revision { get; set; }

    public Guid? PrerequisiteRunId { get; set; }

    public Guid? CurrentPreviewId { get; set; }

    public Guid? CurrentApprovalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<DeploymentPreviewEntity> Previews { get; } = [];

    public List<DeploymentApprovalEntity> Approvals { get; } = [];
}

internal sealed class DeploymentPreviewEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public required string ProfileFingerprint { get; set; }

    public Guid PrerequisiteRunId { get; set; }

    public required string ValidationCorrelationId { get; set; }

    public required string WhatIfCorrelationId { get; set; }

    public int CreateCount { get; set; }

    public int ModifyCount { get; set; }

    public int DeleteCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DeploymentProfileEntity? Profile { get; set; }

    public List<DeploymentWhatIfChangeEntity> Changes { get; } = [];
}

internal sealed class DeploymentWhatIfChangeEntity
{
    public Guid Id { get; set; }

    public Guid PreviewId { get; set; }

    public required string ResourceId { get; set; }

    public Application.Deployments.DeploymentChangeType ChangeType { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string? DeltaJson { get; set; }

    public DeploymentPreviewEntity? Preview { get; set; }
}

internal sealed class DeploymentApprovalEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public Guid PreviewId { get; set; }

    public required string ProfileFingerprint { get; set; }

    public required string ActorTenantId { get; set; }

    public required string ActorObjectId { get; set; }

    public int CreateCount { get; set; }

    public int ModifyCount { get; set; }

    public int DeleteCount { get; set; }

    public DateTimeOffset ApprovedAt { get; set; }

    public DateTimeOffset? InvalidatedAt { get; set; }

    public DeploymentProfileEntity? Profile { get; set; }
}
