using Fsp.Orchestrator.Application.Persistence;
using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.UnitTests;

public sealed class PrerequisiteEngineTests
{
    [Fact]
    public async Task IndependentChecksUseBoundedConcurrencyAndDependenciesWait()
    {
        var tracker = new ConcurrencyTracker();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var checks = new IPrerequisiteCheck[]
        {
            Check("a", [], async cancellationToken =>
            {
                await tracker.RunAsync(cancellationToken);
                lock (completed)
                {
                    completed.Add("a");
                }
                return Passed();
            }),
            Check("b", [], async cancellationToken =>
            {
                await tracker.RunAsync(cancellationToken);
                lock (completed)
                {
                    completed.Add("b");
                }
                return Passed();
            }),
            Check("c", ["a"], cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (completed)
                {
                    Assert.Contains("a", completed);
                }
                return Task.FromResult(Passed());
            }),
        };
        var store = new InMemoryRunStore();
        var engine = CreateEngine(checks, store, maximumConcurrency: 2);

        var run = await engine.RunAsync(
            Request(),
            CancellationToken.None);

        Assert.Equal(PrerequisiteRunStatus.Passed, run.Status);
        Assert.Equal(2, tracker.Maximum);
        Assert.All(run.Results, result => Assert.Equal(
            PrerequisiteCheckStatus.Passed,
            result.Status));
    }

    [Fact]
    public async Task BlockingFailureSkipsDependentCheck()
    {
        var dependentExecuted = false;
        var checks = new IPrerequisiteCheck[]
        {
            Check(
                "failure",
                [],
                _ => Task.FromResult(
                    new PrerequisiteCheckOutcome(
                        PrerequisiteCheckStatus.Failed,
                        "test.failure",
                        "failed",
                        EmptyEvidence(),
                        "fix"))),
            Check("dependent", ["failure"], _ =>
            {
                dependentExecuted = true;
                return Task.FromResult(Passed());
            }),
        };
        var engine = CreateEngine(checks, new InMemoryRunStore());

        var run = await engine.RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PrerequisiteRunStatus.Failed, run.Status);
        Assert.False(dependentExecuted);
        Assert.Equal(
            PrerequisiteCheckStatus.Skipped,
            run.Results.Single(result => result.CheckId == "dependent").Status);
    }

    [Fact]
    public async Task TimeoutMapsToStableFailure()
    {
        var check = Check(
            "slow",
            [],
            async cancellationToken =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return Passed();
            },
            timeout: TimeSpan.FromMilliseconds(20));
        var engine = CreateEngine([check], new InMemoryRunStore());

        var run = await engine.RunAsync(Request(), CancellationToken.None);

        var result = Assert.Single(run.Results);
        Assert.Equal(PrerequisiteCheckStatus.Failed, result.Status);
        Assert.Equal("prerequisite.timeout", result.Code);
    }

    [Fact]
    public async Task CancellationPersistsCancelledResult()
    {
        var check = Check(
            "cancel",
            [],
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Passed();
            });
        var engine = CreateEngine([check], new InMemoryRunStore());
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(30));

        var run = await engine.RunAsync(Request(), cancellation.Token);

        Assert.Equal(PrerequisiteRunStatus.Cancelled, run.Status);
        Assert.Equal(
            PrerequisiteCheckStatus.Cancelled,
            Assert.Single(run.Results).Status);
    }

    [Fact]
    public async Task EvidenceIsRedactedBeforePersistence()
    {
        var check = Check(
            "evidence",
            [],
            _ => Task.FromResult(
                new PrerequisiteCheckOutcome(
                    PrerequisiteCheckStatus.Passed,
                    "test.passed",
                    "passed",
                    new Dictionary<string, string>
                    {
                        ["authorization"] = "Bearer abcdefghijklmnopqrstuvwxyz",
                        ["resource"] = "safe-resource",
                    },
                    string.Empty)));
        var engine = CreateEngine([check], new InMemoryRunStore());

        var run = await engine.RunAsync(Request(), CancellationToken.None);

        var evidence = Assert.Single(run.Results).Evidence;
        Assert.Equal("[REDACTED]", evidence["authorization"]);
        Assert.Equal("safe-resource", evidence["resource"]);
    }

    [Fact]
    public async Task GateRejectsStaleExpiredAndMismatchedRuns()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var check = Check(
            "target",
            [],
            _ => Task.FromResult(Passed()),
            inputKeys: ["target"],
            lifetime: TimeSpan.FromMinutes(1));
        var store = new InMemoryRunStore();
        var engine = CreateEngine([check], store, timeProvider: time);
        var request = Request(
            operation: "deploy",
            inputs: new Dictionary<string, string> { ["target"] = "a" });
        var run = await engine.RunAsync(request, CancellationToken.None);
        var gate = new PrerequisiteGate(store, [check], time);

        var stale = await gate.ValidateAsync(
            run.Id,
            "deploy",
            new Dictionary<string, string> { ["target"] = "b" },
            CancellationToken.None);
        var mismatch = await gate.ValidateAsync(
            run.Id,
            "configure",
            request.Inputs,
            CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(2));
        var expired = await gate.ValidateAsync(
            run.Id,
            "deploy",
            request.Inputs,
            CancellationToken.None);

        Assert.Equal("prerequisite.run_stale", stale.Code);
        Assert.Equal("prerequisite.operation_mismatch", mismatch.Code);
        Assert.Equal("prerequisite.run_expired", expired.Code);
    }

    [Fact]
    public void MissingDependencyAndCycleAreRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => CreateEngine(
                [Check("a", ["missing"], _ => Task.FromResult(Passed()))],
                new InMemoryRunStore()));
        Assert.Throws<InvalidOperationException>(
            () => CreateEngine(
                [
                    Check("a", ["b"], _ => Task.FromResult(Passed())),
                    Check("b", ["a"], _ => Task.FromResult(Passed())),
                ],
                new InMemoryRunStore()));
    }

    private static PrerequisiteEngine CreateEngine(
        IEnumerable<IPrerequisiteCheck> checks,
        InMemoryRunStore store,
        int maximumConcurrency = 4,
        TimeProvider? timeProvider = null) =>
        new(
            checks,
            store,
            new PrerequisiteEvidenceSanitizer(
                new PersistenceValueGuard()),
            new TestErrorReporter(),
            timeProvider ?? TimeProvider.System,
            maximumConcurrency);

    private static FakeCheck Check(
        string id,
        IReadOnlyList<string> dependencies,
        Func<CancellationToken, Task<PrerequisiteCheckOutcome>> execute,
        TimeSpan? timeout = null,
        TimeSpan? lifetime = null,
        IReadOnlyList<string>? inputKeys = null) =>
        new(
            new PrerequisiteCheckDefinition(
                id,
                "test",
                id,
                id,
                dependencies,
                ["deploy", "configure"],
                inputKeys ?? [],
                timeout ?? TimeSpan.FromSeconds(5),
                lifetime ?? TimeSpan.FromMinutes(5),
                true,
                "1.0"),
            execute);

    private static PrerequisiteRunRequest Request(
        string operation = "deploy",
        IReadOnlyDictionary<string, string>? inputs = null) =>
        new(
            Guid.NewGuid(),
            operation,
            inputs ?? new Dictionary<string, string>(),
            null);

    private static PrerequisiteCheckOutcome Passed() =>
        new(
            PrerequisiteCheckStatus.Passed,
            "test.passed",
            "passed",
            EmptyEvidence(),
            string.Empty);

    private static Dictionary<string, string> EmptyEvidence() =>
        new Dictionary<string, string>();

    private sealed class FakeCheck(
        PrerequisiteCheckDefinition definition,
        Func<CancellationToken, Task<PrerequisiteCheckOutcome>> execute)
        : IPrerequisiteCheck
    {
        public PrerequisiteCheckDefinition Definition { get; } = definition;

        public Task<PrerequisiteCheckOutcome> ExecuteAsync(
            PrerequisiteCheckContext context,
            CancellationToken cancellationToken) =>
            execute(cancellationToken);
    }

    private sealed class TestErrorReporter : IPrerequisiteErrorReporter
    {
        public void Report(string checkId, string exceptionType)
        {
        }
    }

    private sealed class ConcurrencyTracker
    {
        private int current;
        private int maximum;

        public int Maximum => maximum;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var value = Interlocked.Increment(ref current);
            UpdateMaximum(value);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }

        private void UpdateMaximum(int value)
        {
            var snapshot = Volatile.Read(ref maximum);
            while (value > snapshot)
            {
                var original = Interlocked.CompareExchange(
                    ref maximum,
                    value,
                    snapshot);
                if (original == snapshot)
                {
                    return;
                }

                snapshot = original;
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration)
        {
            current = current.Add(duration);
        }
    }

    private sealed class InMemoryRunStore : IPrerequisiteRunStore
    {
        private readonly object sync = new();
        private readonly Dictionary<Guid, PrerequisiteRun> runs = [];

        public Task CreateAsync(
            PrerequisiteRunRequest request,
            IReadOnlyList<PrerequisiteCheckDefinition> definitions,
            string inputFingerprint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                runs.Add(
                    request.Id,
                    new PrerequisiteRun(
                        request.Id,
                        request.Operation,
                        PrerequisiteRunStatus.Pending,
                        inputFingerprint,
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        definitions.Select(
                            definition => new PrerequisiteCheckResult(
                                definition.Id,
                                definition.Category,
                                definition.Blocking,
                                PrerequisiteCheckStatus.Pending,
                                "prerequisite.pending",
                                "pending",
                                EmptyEvidence(),
                                string.Empty,
                                string.Empty,
                                null,
                                null,
                                null,
                                0,
                                null)).ToArray()));
            }

            return Task.CompletedTask;
        }

        public Task MarkRunStartedAsync(
            Guid runId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var run = runs[runId];
                runs[runId] = run with
                {
                    Status = PrerequisiteRunStatus.Running,
                    StartedAt = startedAt,
                };
            }

            return Task.CompletedTask;
        }

        public Task UpdateResultAsync(
            Guid runId,
            PrerequisiteCheckResult result,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var run = runs[runId];
                runs[runId] = run with
                {
                    Results = run.Results
                        .Select(item => item.CheckId == result.CheckId ? result : item)
                        .ToArray(),
                };
            }

            return Task.CompletedTask;
        }

        public Task CompleteRunAsync(
            Guid runId,
            PrerequisiteRunStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var run = runs[runId];
                runs[runId] = run with
                {
                    Status = status,
                    CompletedAt = completedAt,
                };
            }

            return Task.CompletedTask;
        }

        public Task<PrerequisiteRun?> GetAsync(
            Guid runId,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                return Task.FromResult(runs.GetValueOrDefault(runId));
            }
        }

        public Task<PrerequisiteWarningAcceptance> AcceptWarningAsync(
            Guid runId,
            string checkId,
            string actorObjectId,
            string reason,
            DateTimeOffset acceptedAt,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
