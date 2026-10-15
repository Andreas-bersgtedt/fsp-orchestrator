namespace Fsp.Orchestrator.Application.Prerequisites;

public sealed record PrerequisiteCheckDefinition(
    string Id,
    string Category,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> InputKeys,
    TimeSpan Timeout,
    TimeSpan Lifetime,
    bool Blocking,
    string Version);

public sealed record PrerequisiteRunRequest(
    Guid Id,
    string Operation,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<string>? CheckIds = null);

public sealed record PrerequisiteCheckContext(
    Guid RunId,
    string Operation,
    IReadOnlyDictionary<string, string> Inputs);

public sealed record PrerequisiteCheckOutcome(
    PrerequisiteCheckStatus Status,
    string Code,
    string Summary,
    IReadOnlyDictionary<string, string> Evidence,
    string Remediation);

public sealed record PrerequisiteCheckResult(
    string CheckId,
    string Category,
    bool Blocking,
    PrerequisiteCheckStatus Status,
    string Code,
    string Summary,
    IReadOnlyDictionary<string, string> Evidence,
    string Remediation,
    string InputFingerprint,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    long DurationMilliseconds,
    PrerequisiteWarningAcceptance? WarningAcceptance);

public sealed record PrerequisiteWarningAcceptance(
    string ActorObjectId,
    string Reason,
    DateTimeOffset AcceptedAt);

public sealed record PrerequisiteRun(
    Guid Id,
    string Operation,
    PrerequisiteRunStatus Status,
    string InputFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PrerequisiteCheckResult> Results);

public sealed record PrerequisiteGateResult(
    bool Allowed,
    string Code,
    string Message,
    IReadOnlyList<string> BlockingCheckIds);

public enum PrerequisiteCheckStatus
{
    Pending,
    Running,
    Passed,
    Warning,
    Failed,
    Skipped,
    Unknown,
    Cancelled,
}

public enum PrerequisiteRunStatus
{
    Pending,
    Running,
    Passed,
    Warning,
    Failed,
    Cancelled,
}
