using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

public sealed class OrchestratorDbContextFactory
    : IDesignTimeDbContextFactory<OrchestratorDbContext>
{
    public OrchestratorDbContext CreateDbContext(string[] args)
    {
        var databasePath = Environment.GetEnvironmentVariable(
            "ORCHESTRATOR_MIGRATION_DATABASE")
            ?? Path.Combine(Path.GetTempPath(), "fsp-orchestrator-migrations.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OrchestratorDbContext(options);
    }
}
