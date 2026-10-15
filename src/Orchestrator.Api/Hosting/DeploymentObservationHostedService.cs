using Fsp.Orchestrator.Application.Deployments;

namespace Fsp.Orchestrator.Api.Hosting;

public sealed partial class DeploymentObservationHostedService(
    IDeploymentObservationService observationService,
    DeploymentObservationOptions options,
    TimeProvider timeProvider,
    ILogger<DeploymentObservationHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await observationService.PollAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogCycleFailure(
                    logger,
                    exception.GetType().FullName ?? exception.GetType().Name);
            }

            await Task.Delay(options.Interval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Scheduled observation cycle failed with {ExceptionType}.")]
    private static partial void LogCycleFailure(
        ILogger logger,
        string exceptionType);
}
