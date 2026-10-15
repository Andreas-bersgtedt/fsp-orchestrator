namespace Fsp.Orchestrator.Application.Persistence;

public sealed record UpsertDeployment(
    Guid Id,
    string Name,
    string Topology,
    Uri ManagerEndpoint,
    string? AzureResourceId,
    string? CredentialKind = null,
    Uri? CredentialReference = null);

public sealed record RegisterDeployment(
    Guid Id,
    string Name,
    string Topology,
    Uri ManagerEndpoint,
    string? AzureResourceId,
    string? CredentialKind,
    Uri? CredentialReference,
    IReadOnlyList<ObservedComponent> Components,
    DateTimeOffset ObservedAt);

public sealed record ObservedComponent(
    string Name,
    string Version,
    string Status);

public sealed record RecordObservation(
    Guid DeploymentId,
    string Component,
    string Version,
    string Status,
    DateTimeOffset ObservedAt);

public sealed record CreateJob(
    Guid Id,
    Guid? DeploymentId,
    string Kind,
    string IdempotencyKey,
    IReadOnlyList<string> Steps);

public sealed record TransitionJob(
    Guid JobId,
    JobStatus TargetStatus,
    long ExpectedVersion,
    string? SafeOutput);

public sealed record AppendAuditMetadata(
    DateTimeOffset OccurredAt,
    string ActorObjectId,
    string TenantId,
    string Action,
    string TargetType,
    string TargetId,
    string Result,
    string CorrelationId,
    string InputHash);
