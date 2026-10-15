using Fsp.Orchestrator.Application.Persistence;
using Fsp.Orchestrator.Application.Prerequisites;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestratorPersistence(
        this IServiceCollection services,
        string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            Cache = SqliteCacheMode.Shared,
            DataSource = databasePath,
            DefaultTimeout = 30,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        services.AddDbContextFactory<OrchestratorDbContext>(
            options => options.UseSqlite(connectionString));
        services.AddSingleton<IPersistenceValueGuard, PersistenceValueGuard>();
        services.AddSingleton<IInventoryStore, InventoryStore>();
        services.AddSingleton<IJobStore, JobStore>();
        services.AddSingleton<IAuditMetadataStore, AuditMetadataStore>();
        services.AddSingleton<IPrerequisiteRunStore, PrerequisiteRunStore>();
        services.AddSingleton<IDatabaseMaintenance>(
            provider => new DatabaseMaintenance(
                provider.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>(),
                databasePath));
        return services;
    }
}
