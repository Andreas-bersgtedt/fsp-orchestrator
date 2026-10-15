using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Api.Hosting;

public sealed partial class DatabaseMigrationHostedService(
    IDatabaseMaintenance databaseMaintenance,
    ILogger<DatabaseMigrationHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogApplyingMigrations(logger);
        await databaseMaintenance.MigrateAsync(cancellationToken);
        LogDatabaseReady(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Applying Orchestrator database migrations")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Orchestrator database is migrated and healthy")]
    private static partial void LogDatabaseReady(ILogger logger);
}
