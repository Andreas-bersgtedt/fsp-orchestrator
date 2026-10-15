namespace Fsp.Orchestrator.Application.Prerequisites;

public sealed class PrerequisiteGate(
    IPrerequisiteRunStore store,
    IEnumerable<IPrerequisiteCheck> checks,
    TimeProvider timeProvider)
    : IPrerequisiteGate
{
    private readonly Dictionary<string, PrerequisiteCheckDefinition>
        definitions = checks.ToDictionary(
            check => check.Definition.Id,
            check => check.Definition,
            StringComparer.Ordinal);

    public async Task<PrerequisiteGateResult> ValidateAsync(
        Guid runId,
        string operation,
        IReadOnlyDictionary<string, string> currentInputs,
        CancellationToken cancellationToken)
    {
        var run = await store.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return Denied(
                "prerequisite.run_missing",
                "The prerequisite run does not exist.");
        }

        if (!string.Equals(run.Operation, operation, StringComparison.Ordinal))
        {
            return Denied(
                "prerequisite.operation_mismatch",
                "The prerequisite run belongs to another operation.");
        }

        if (run.Status is PrerequisiteRunStatus.Pending
            or PrerequisiteRunStatus.Running
            or PrerequisiteRunStatus.Cancelled)
        {
            return Denied(
                "prerequisite.run_incomplete",
                "The prerequisite run is not complete.");
        }

        var now = timeProvider.GetUtcNow();
        var blocking = new List<string>();
        var stale = new List<string>();
        var expired = new List<string>();
        var immediateRecheck = new List<string>();
        foreach (var result in run.Results.Where(result => result.Blocking))
        {
            if (!definitions.TryGetValue(result.CheckId, out var definition))
            {
                blocking.Add(result.CheckId);
                continue;
            }

            if (!string.Equals(
                    result.InputFingerprint,
                    PrerequisiteFingerprint.Compute(
                        currentInputs,
                        definition.InputKeys),
                    StringComparison.Ordinal))
            {
                stale.Add(result.CheckId);
            }

            if (result.ExpiresAt is null || result.ExpiresAt <= now)
            {
                expired.Add(result.CheckId);
            }

            if (definition.ApplyFreshness is { } freshness
                && (result.CompletedAt is null
                    || result.CompletedAt + freshness <= now))
            {
                immediateRecheck.Add(result.CheckId);
            }

            if (result.Status is not (PrerequisiteCheckStatus.Passed
                or PrerequisiteCheckStatus.Warning))
            {
                blocking.Add(result.CheckId);
            }
        }

        if (stale.Count > 0)
        {
            return Denied(
                "prerequisite.run_stale",
                "One or more prerequisite inputs changed.",
                stale);
        }

        if (expired.Count > 0)
        {
            return Denied(
                "prerequisite.run_expired",
                "One or more prerequisite results expired.",
                expired);
        }

        if (immediateRecheck.Count > 0)
        {
            return Denied(
                "prerequisite.immediate_recheck_required",
                "One or more time-sensitive checks must run again immediately before apply.",
                immediateRecheck);
        }

        if (blocking.Count > 0)
        {
            return Denied(
                "prerequisite.blocking_failure",
                "One or more blocking prerequisite checks did not pass.",
                blocking);
        }

        return new PrerequisiteGateResult(
            true,
            "prerequisite.allowed",
            "Prerequisite run permits the requested operation.",
            []);
    }

    private static PrerequisiteGateResult Denied(
        string code,
        string message,
        IReadOnlyList<string>? checks = null) =>
        new(false, code, message, checks ?? []);
}
