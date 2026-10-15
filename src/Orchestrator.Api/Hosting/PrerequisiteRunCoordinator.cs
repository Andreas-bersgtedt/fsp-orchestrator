using System.Collections.Concurrent;
using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.Api.Hosting;

public sealed partial class PrerequisiteRunCoordinator(
    IPrerequisiteEngine engine,
    IPrerequisiteRunStore store,
    ILogger<PrerequisiteRunCoordinator> logger)
    : IPrerequisiteRunCoordinator, IDisposable
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = [];

    public async Task<Guid> StartAsync(
        PrerequisiteRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new CancellationTokenSource();
        if (!active.TryAdd(request.Id, source))
        {
            source.Dispose();
            throw new InvalidOperationException(
                $"Prerequisite run '{request.Id}' is already active.");
        }

        var execution = Task.Run(
            () => engine.RunAsync(request, source.Token),
            CancellationToken.None);
        _ = ObserveAsync(request.Id, execution, source);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (execution.IsFaulted)
            {
                await execution;
            }

            if (await store.GetAsync(request.Id, cancellationToken) is not null)
            {
                return request.Id;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        throw new InvalidOperationException(
            $"Prerequisite run '{request.Id}' did not initialize.");
    }

    public bool Cancel(Guid runId)
    {
        if (!active.TryGetValue(runId, out var source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public void Dispose()
    {
        foreach (var source in active.Values)
        {
            source.Cancel();
            source.Dispose();
        }

        active.Clear();
    }

    private async Task ObserveAsync(
        Guid runId,
        Task<PrerequisiteRun> execution,
        CancellationTokenSource source)
    {
        try
        {
            await execution;
        }
        catch (Exception exception)
        {
            LogRunFailed(
                logger,
                runId,
                exception.GetType().FullName ?? exception.GetType().Name);
        }
        finally
        {
            active.TryRemove(runId, out _);
            source.Dispose();
        }
    }

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Error,
        Message = "Prerequisite run {RunId} failed outside checker error mapping with {ExceptionType}")]
    private static partial void LogRunFailed(
        ILogger logger,
        Guid runId,
        string exceptionType);
}
