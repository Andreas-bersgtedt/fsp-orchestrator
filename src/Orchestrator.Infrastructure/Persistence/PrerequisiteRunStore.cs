using System.Text.Json;
using Fsp.Orchestrator.Application.Persistence;
using Fsp.Orchestrator.Application.Prerequisites;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class PrerequisiteRunStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IPrerequisiteRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);

    public async Task CreateAsync(
        PrerequisiteRunRequest request,
        IReadOnlyList<PrerequisiteCheckDefinition> definitions,
        string inputFingerprint,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            [
                request.Operation,
                inputFingerprint,
                .. request.Inputs.SelectMany(item => new[] { item.Key, item.Value }),
            ]);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entity = new PrerequisiteRunEntity
        {
            Id = request.Id,
            Operation = request.Operation,
            Status = PrerequisiteRunStatus.Pending,
            InputFingerprint = inputFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        entity.Results.AddRange(
            definitions.Select(
                definition => new PrerequisiteResultEntity
                {
                    RunId = request.Id,
                    CheckId = definition.Id,
                    Category = definition.Category,
                    Blocking = definition.Blocking,
                    Status = PrerequisiteCheckStatus.Pending,
                    Code = "prerequisite.pending",
                    Summary = "Check is pending.",
                    EvidenceJson = "{}",
                    Remediation = string.Empty,
                    InputFingerprint = PrerequisiteFingerprint.Compute(
                        request.Inputs,
                        definition.InputKeys),
                    DurationMilliseconds = 0,
                }));
        context.PrerequisiteRuns.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRunStartedAsync(
        Guid runId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var run = await context.PrerequisiteRuns.FindAsync(
            [runId],
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Prerequisite run '{runId}' was not found.");
        run.Status = PrerequisiteRunStatus.Running;
        run.StartedAt = startedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateResultAsync(
        Guid runId,
        PrerequisiteCheckResult result,
        CancellationToken cancellationToken)
    {
        var evidenceJson = JsonSerializer.Serialize(result.Evidence, JsonOptions);
        valueGuard.EnsureSafe(
            result.Code,
            result.Summary,
            result.Remediation,
            evidenceJson);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entity = await context.PrerequisiteResults.FindAsync(
            [runId, result.CheckId],
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Prerequisite result '{runId}/{result.CheckId}' was not found.");
        entity.Status = result.Status;
        entity.Code = result.Code;
        entity.Summary = result.Summary;
        entity.EvidenceJson = evidenceJson;
        entity.Remediation = result.Remediation;
        entity.InputFingerprint = result.InputFingerprint;
        entity.StartedAt = result.StartedAt;
        entity.CompletedAt = result.CompletedAt;
        entity.ExpiresAt = result.ExpiresAt;
        entity.DurationMilliseconds = result.DurationMilliseconds;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        Guid runId,
        PrerequisiteRunStatus status,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var run = await context.PrerequisiteRuns.FindAsync(
            [runId],
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Prerequisite run '{runId}' was not found.");
        run.Status = status;
        run.CompletedAt = completedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PrerequisiteRun?> GetAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var run = await context.PrerequisiteRuns
            .AsNoTracking()
            .Include(item => item.Results)
            .SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        return run is null
            ? null
            : new PrerequisiteRun(
                run.Id,
                run.Operation,
                run.Status,
                run.InputFingerprint,
                run.CreatedAt,
                run.StartedAt,
                run.CompletedAt,
                run.Results
                    .OrderBy(result => result.CheckId, StringComparer.Ordinal)
                    .Select(Map)
                    .ToArray());
    }

    public async Task<PrerequisiteWarningAcceptance> AcceptWarningAsync(
        Guid runId,
        string checkId,
        string actorObjectId,
        string reason,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Warning acceptance requires a written reason.",
                nameof(reason));
        }

        valueGuard.EnsureSafe(checkId, actorObjectId, reason);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var result = await context.PrerequisiteResults.FindAsync(
            [runId, checkId],
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Prerequisite result '{runId}/{checkId}' was not found.");
        if (result.Status != PrerequisiteCheckStatus.Warning)
        {
            throw new InvalidOperationException(
                "Only warning results can be accepted.");
        }

        result.WarningAcceptedByObjectId = actorObjectId;
        result.WarningAcceptanceReason = reason;
        result.WarningAcceptedAt = acceptedAt;
        await context.SaveChangesAsync(cancellationToken);
        return new PrerequisiteWarningAcceptance(
            actorObjectId,
            reason,
            acceptedAt);
    }

    private static PrerequisiteCheckResult Map(PrerequisiteResultEntity entity) =>
        new(
            entity.CheckId,
            entity.Category,
            entity.Blocking,
            entity.Status,
            entity.Code,
            entity.Summary,
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                entity.EvidenceJson,
                JsonOptions) ?? [],
            entity.Remediation,
            entity.InputFingerprint,
            entity.StartedAt,
            entity.CompletedAt,
            entity.ExpiresAt,
            entity.DurationMilliseconds,
            entity.WarningAcceptedByObjectId is null
                || entity.WarningAcceptanceReason is null
                || entity.WarningAcceptedAt is null
                ? null
                : new PrerequisiteWarningAcceptance(
                    entity.WarningAcceptedByObjectId,
                    entity.WarningAcceptanceReason,
                    entity.WarningAcceptedAt.Value));
}
