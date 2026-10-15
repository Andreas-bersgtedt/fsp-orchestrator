using System.Collections.Concurrent;
using System.Diagnostics;

namespace Fsp.Orchestrator.Application.Prerequisites;

public sealed class PrerequisiteEngine : IPrerequisiteEngine
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEvidence =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, IPrerequisiteCheck> checks;
    private readonly IPrerequisiteRunStore store;
    private readonly IPrerequisiteEvidenceSanitizer evidenceSanitizer;
    private readonly IPrerequisiteErrorReporter errorReporter;
    private readonly TimeProvider timeProvider;
    private readonly int maximumConcurrency;

    public PrerequisiteEngine(
        IEnumerable<IPrerequisiteCheck> checks,
        IPrerequisiteRunStore store,
        IPrerequisiteEvidenceSanitizer evidenceSanitizer,
        IPrerequisiteErrorReporter errorReporter,
        TimeProvider timeProvider,
        int maximumConcurrency = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrency);

        this.checks = checks.ToDictionary(
            check => check.Definition.Id,
            StringComparer.Ordinal);
        this.store = store;
        this.evidenceSanitizer = evidenceSanitizer;
        this.errorReporter = errorReporter;
        this.timeProvider = timeProvider;
        this.maximumConcurrency = maximumConcurrency;
        ValidateCatalog(this.checks);
        Catalog = this.checks.Values
            .Select(check => check.Definition)
            .OrderBy(definition => definition.Category, StringComparer.Ordinal)
            .ThenBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<PrerequisiteCheckDefinition> Catalog { get; }

    public async Task<PrerequisiteRun> RunAsync(
        PrerequisiteRunRequest request,
        CancellationToken cancellationToken)
    {
        var selected = SelectDefinitions(request);
        var inputFingerprint = PrerequisiteFingerprint.Compute(request.Inputs);
        await store.CreateAsync(
            request,
            selected,
            inputFingerprint,
            cancellationToken);
        var startedAt = timeProvider.GetUtcNow();
        await store.MarkRunStartedAsync(
            request.Id,
            startedAt,
            cancellationToken);

        using var concurrency = new SemaphoreSlim(maximumConcurrency);
        var tasks = new ConcurrentDictionary<
            string,
            Lazy<Task<PrerequisiteCheckResult>>>(StringComparer.Ordinal);

        Task<PrerequisiteCheckResult> Execute(
            PrerequisiteCheckDefinition definition) =>
            tasks.GetOrAdd(
                definition.Id,
                _ => new Lazy<Task<PrerequisiteCheckResult>>(
                    () => ExecuteCheckAsync(
                        request,
                        definition,
                        selected,
                        tasks,
                        concurrency,
                        cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication))
                .Value;

        var results = await Task.WhenAll(selected.Select(Execute));
        var status = OverallStatus(results);
        await store.CompleteRunAsync(
            request.Id,
            status,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return await store.GetAsync(request.Id, CancellationToken.None)
            ?? throw new InvalidOperationException(
                $"Prerequisite run '{request.Id}' was not persisted.");
    }

    private async Task<PrerequisiteCheckResult> ExecuteCheckAsync(
        PrerequisiteRunRequest request,
        PrerequisiteCheckDefinition definition,
        IReadOnlyList<PrerequisiteCheckDefinition> selected,
        ConcurrentDictionary<string, Lazy<Task<PrerequisiteCheckResult>>> tasks,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        var dependencies = definition.Dependencies
            .Select(
                dependencyId => tasks.GetOrAdd(
                        dependencyId,
                        _ => new Lazy<Task<PrerequisiteCheckResult>>(
                            () => ExecuteCheckAsync(
                                request,
                                selected.Single(item => item.Id == dependencyId),
                                selected,
                                tasks,
                                concurrency,
                                cancellationToken),
                            LazyThreadSafetyMode.ExecutionAndPublication))
                    .Value)
            .ToArray();
        var dependencyResults = await Task.WhenAll(dependencies);
        if (dependencyResults.Any(
                result => result.Blocking
                    && result.Status is PrerequisiteCheckStatus.Failed
                        or PrerequisiteCheckStatus.Unknown
                        or PrerequisiteCheckStatus.Cancelled
                        or PrerequisiteCheckStatus.Skipped))
        {
            return await SaveResultAsync(
                request,
                definition,
                PrerequisiteCheckStatus.Skipped,
                "prerequisite.dependency_blocked",
                "A blocking dependency did not pass.",
                EmptyEvidence,
                "Resolve the blocking dependency and rerun this check.",
                null,
                timeProvider.GetUtcNow(),
                0,
                CancellationToken.None);
        }

        var startedAt = timeProvider.GetUtcNow();
        await SaveResultAsync(
            request,
            definition,
            PrerequisiteCheckStatus.Running,
            "prerequisite.running",
            "Check is running.",
            EmptyEvidence,
            string.Empty,
            startedAt,
            null,
            0,
            CancellationToken.None);
        var stopwatch = Stopwatch.StartNew();
        PrerequisiteCheckOutcome outcome;
        try
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(definition.Timeout);
                outcome = await checks[definition.Id].ExecuteAsync(
                    new PrerequisiteCheckContext(
                        request.Id,
                        request.Operation,
                        request.Inputs),
                    timeout.Token);
            }
            finally
            {
                concurrency.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = new PrerequisiteCheckOutcome(
                PrerequisiteCheckStatus.Cancelled,
                "prerequisite.cancelled",
                "Check was cancelled.",
                EmptyEvidence,
                "Rerun the check when the operation is ready.");
        }
        catch (OperationCanceledException)
        {
            outcome = new PrerequisiteCheckOutcome(
                PrerequisiteCheckStatus.Failed,
                "prerequisite.timeout",
                "Check exceeded its timeout.",
                EmptyEvidence,
                "Resolve the provider delay or increase the check timeout.");
        }
        catch (Exception exception)
        {
            errorReporter.Report(
                definition.Id,
                exception.GetType().FullName ?? exception.GetType().Name);
            outcome = new PrerequisiteCheckOutcome(
                PrerequisiteCheckStatus.Unknown,
                "prerequisite.unhandled_error",
                "Check failed with an unclassified error.",
                EmptyEvidence,
                "Review safe diagnostics and rerun the check.");
        }
        finally
        {
            stopwatch.Stop();
        }

        var completedAt = timeProvider.GetUtcNow();
        return await SaveResultAsync(
            request,
            definition,
            Normalize(outcome.Status),
            outcome.Code,
            outcome.Summary,
            evidenceSanitizer.Sanitize(outcome.Evidence),
            outcome.Remediation,
            startedAt,
            completedAt,
            stopwatch.ElapsedMilliseconds,
            CancellationToken.None);
    }

    private async Task<PrerequisiteCheckResult> SaveResultAsync(
        PrerequisiteRunRequest request,
        PrerequisiteCheckDefinition definition,
        PrerequisiteCheckStatus status,
        string code,
        string summary,
        IReadOnlyDictionary<string, string> evidence,
        string remediation,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        var result = new PrerequisiteCheckResult(
            definition.Id,
            definition.Category,
            definition.Blocking,
            status,
            code,
            summary,
            evidence,
            remediation,
            PrerequisiteFingerprint.Compute(request.Inputs, definition.InputKeys),
            startedAt,
            completedAt,
            completedAt is null
                ? null
                : completedAt.Value.Add(definition.Lifetime),
            durationMilliseconds);
        await store.UpdateResultAsync(
            request.Id,
            result,
            cancellationToken);
        return result;
    }

    private PrerequisiteCheckDefinition[] SelectDefinitions(
        PrerequisiteRunRequest request)
    {
        var applicable = Catalog
            .Where(
                definition => definition.Operations.Count == 0
                    || definition.Operations.Contains(
                        request.Operation,
                        StringComparer.Ordinal))
            .ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var selectedIds = request.CheckIds is null or { Count: 0 }
            ? applicable.Keys.ToHashSet(StringComparer.Ordinal)
            : request.CheckIds.ToHashSet(StringComparer.Ordinal);
        foreach (var checkId in selectedIds.ToArray())
        {
            AddDependencies(checkId, applicable, selectedIds);
        }

        return selectedIds
            .Select(
                id => applicable.GetValueOrDefault(id)
                    ?? throw new ArgumentException(
                        $"Prerequisite check '{id}' is not available for operation "
                        + $"'{request.Operation}'.",
                        nameof(request)))
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddDependencies(
        string checkId,
        IReadOnlyDictionary<string, PrerequisiteCheckDefinition> applicable,
        ISet<string> selected)
    {
        var definition = applicable.GetValueOrDefault(checkId)
            ?? throw new ArgumentException(
                $"Prerequisite check '{checkId}' is not in the catalog.");
        foreach (var dependency in definition.Dependencies)
        {
            if (selected.Add(dependency))
            {
                AddDependencies(dependency, applicable, selected);
            }
        }
    }

    private static PrerequisiteRunStatus OverallStatus(
        IReadOnlyList<PrerequisiteCheckResult> results)
    {
        if (results.Any(result => result.Status == PrerequisiteCheckStatus.Cancelled))
        {
            return PrerequisiteRunStatus.Cancelled;
        }

        if (results.Any(
                result => result.Blocking
                    && result.Status is PrerequisiteCheckStatus.Failed
                        or PrerequisiteCheckStatus.Unknown
                        or PrerequisiteCheckStatus.Skipped))
        {
            return PrerequisiteRunStatus.Failed;
        }

        return results.Any(result => result.Status == PrerequisiteCheckStatus.Warning)
            ? PrerequisiteRunStatus.Warning
            : PrerequisiteRunStatus.Passed;
    }

    private static PrerequisiteCheckStatus Normalize(PrerequisiteCheckStatus status) =>
        status is PrerequisiteCheckStatus.Passed
            or PrerequisiteCheckStatus.Warning
            or PrerequisiteCheckStatus.Failed
            or PrerequisiteCheckStatus.Unknown
            or PrerequisiteCheckStatus.Cancelled
            ? status
            : PrerequisiteCheckStatus.Unknown;

    private static void ValidateCatalog(
        IReadOnlyDictionary<string, IPrerequisiteCheck> checks)
    {
        foreach (var check in checks.Values)
        {
            foreach (var dependency in check.Definition.Dependencies)
            {
                if (!checks.ContainsKey(dependency))
                {
                    throw new InvalidOperationException(
                        $"Check '{check.Definition.Id}' depends on missing check "
                        + $"'{dependency}'.");
                }
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var checkId in checks.Keys)
        {
            Visit(checkId, checks, visiting, visited);
        }
    }

    private static void Visit(
        string checkId,
        IReadOnlyDictionary<string, IPrerequisiteCheck> checks,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(checkId))
        {
            return;
        }

        if (!visiting.Add(checkId))
        {
            throw new InvalidOperationException(
                $"Prerequisite dependency cycle includes '{checkId}'.");
        }

        foreach (var dependency in checks[checkId].Definition.Dependencies)
        {
            Visit(dependency, checks, visiting, visited);
        }

        visiting.Remove(checkId);
        visited.Add(checkId);
    }
}
