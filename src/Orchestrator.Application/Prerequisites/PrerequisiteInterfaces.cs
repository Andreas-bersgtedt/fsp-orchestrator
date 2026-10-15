namespace Fsp.Orchestrator.Application.Prerequisites;

public interface IPrerequisiteCheck
{
    PrerequisiteCheckDefinition Definition { get; }

    Task<PrerequisiteCheckOutcome> ExecuteAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken);
}

public interface IPrerequisiteRunStore
{
    Task CreateAsync(
        PrerequisiteRunRequest request,
        IReadOnlyList<PrerequisiteCheckDefinition> definitions,
        string inputFingerprint,
        CancellationToken cancellationToken);

    Task MarkRunStartedAsync(
        Guid runId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task UpdateResultAsync(
        Guid runId,
        PrerequisiteCheckResult result,
        CancellationToken cancellationToken);

    Task CompleteRunAsync(
        Guid runId,
        PrerequisiteRunStatus status,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<PrerequisiteRun?> GetAsync(
        Guid runId,
        CancellationToken cancellationToken);
}

public interface IPrerequisiteEngine
{
    IReadOnlyList<PrerequisiteCheckDefinition> Catalog { get; }

    Task<PrerequisiteRun> RunAsync(
        PrerequisiteRunRequest request,
        CancellationToken cancellationToken);
}

public interface IPrerequisiteRunCoordinator
{
    Task<Guid> StartAsync(
        PrerequisiteRunRequest request,
        CancellationToken cancellationToken);

    bool Cancel(Guid runId);
}

public interface IPrerequisiteGate
{
    Task<PrerequisiteGateResult> ValidateAsync(
        Guid runId,
        string operation,
        IReadOnlyDictionary<string, string> currentInputs,
        CancellationToken cancellationToken);
}

public interface IPrerequisiteEvidenceSanitizer
{
    IReadOnlyDictionary<string, string> Sanitize(
        IReadOnlyDictionary<string, string> evidence);
}

public interface IPrerequisiteErrorReporter
{
    void Report(string checkId, string exceptionType);
}
